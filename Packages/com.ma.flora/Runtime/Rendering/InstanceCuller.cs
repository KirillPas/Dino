// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Flora.Rendering.Occlusion;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
using OcclusionCullingSettings = MA.Flora.Rendering.Occlusion.OcclusionCullingSettings;
using OcclusionTest = MA.Flora.Rendering.Occlusion.OcclusionTest;
using SubviewOcclusionTest = MA.Flora.Rendering.Occlusion.SubviewOcclusionTest;
#else
using ComputeCommandBuffer = UnityEngine.Rendering.CommandBuffer;
#endif

namespace MA.Flora.Rendering
{
    [Flags]
    enum InstanceCullingFlags
    {
        None                    = 0,
        DisableShadows          = 1 << 0,
        DisableOcclusionCulling = 1 << 1,
        SelectionOnly           = 1 << 2,

        Default                 = None,
        PickingPass             = DisableShadows | DisableOcclusionCulling,
        OutlinePass             = DisableShadows | DisableOcclusionCulling | SelectionOnly,
    }

    [DebuggerDisplay("Pass={FilterSettings.ShadowCastingMode}, Mesh={MeshID.ToString()}, Material={MaterialID.ToString()}, Commands=({CommandIndex}:{CommandCount})")]
    struct IndirectDrawCommand
    {
        public int CommandIndex;
        public int CommandCount;
        public int IndirectVisibleIndex;
        public InstancedBatchID BatchID;
        public RenderFilterSettings FilterSettings;
        public int LODIndex;
        public InstancedMaterialID MaterialID;
        public InstancedMaterialVariant MaterialVariant;
        public InstancedMeshID MeshID;
        public ushort SubMeshIndex;
        public AxisAlignedBox Bounds;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IndirectDispatchCommand
    {
        public InstancedBatchID BatchID;
        public int Start;
        public int Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IndirectBatchPayload
    {
        public float CullDistance;
        public float3 LODCoefficients;
        public float4 LODDistances;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IndirectDrawInfo
    {
        public uint IndexCountPerInstance;
        public uint InstanceCount;
        public uint StartIndex;
        public uint BaseVertexIndex;
        public uint IndirectStartIndex;
        public uint CullFlags;
    }

    struct CullingContext : IDisposable
    {
        public InstancedCameraID CameraID;
        public DrawBatcher DrawBatcher;

        public UnsafeIndirectList<IndirectDrawCommand> DrawCommands;
        public UnsafeIndirectList<IndirectDispatchCommand> DispatchCommands;
        public UnsafeIndirectList<IndirectBatchPayload> Payloads;
        public UnsafeIndirectList<IndirectDrawInfo> DrawInfos;

        public GraphicsBuffer PerDrawBuffer;
        public GraphicsBuffer IndirectBatchPayloads;
        public GraphicsBuffer BatchItemBuffer;
        public GraphicsBuffer BatchBuffer;
        public GraphicsBuffer IndirectDrawInfoBuffer;
        public GraphicsBuffer VisibleInstancesBuffer;
        public GraphicsBuffer IndirectArgsBuffer;

        public void Dispose()
        {
            DrawBatcher.Dispose();
            DrawCommands.Dispose();
            DispatchCommands.Dispose();
            Payloads.Dispose();
            DrawInfos.Dispose();

            PerDrawBuffer?.Dispose();
            IndirectBatchPayloads?.Dispose();
            BatchItemBuffer?.Dispose();
            BatchBuffer?.Dispose();
            IndirectDrawInfoBuffer?.Dispose();
            VisibleInstancesBuffer?.Dispose();
            IndirectArgsBuffer?.Dispose();
        }
    }

    [BurstCompile]
    sealed unsafe class InstanceCuller : IDisposable
    {
        enum DebugCounterIndex
        {
            Visible  = 0,
            Culled   = 1,
            Occluded = 2,
            Count    = 3,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BatchingContext
        {
            [NoAlias] public DrawBatcher DrawBatcher;

            [NoAlias] public UnsafeList<IndirectDrawCommand> DrawCommands;
            [NoAlias] public UnsafeList<IndirectDispatchCommand> DispatchCommands;
            [NoAlias] public UnsafeList<IndirectBatchPayload> Payloads;
            [NoAlias] public UnsafeList<IndirectDrawInfo> DrawInfos;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static BatchingContext* Create(AllocatorManager.AllocatorHandle allocator)
            {
                BatchingContext* context = AllocatorManager.Allocate<BatchingContext>(allocator);
                *context = new BatchingContext(allocator);
                return context;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Destroy(BatchingContext* context, AllocatorManager.AllocatorHandle allocator)
            {
                context->Dispose();
                AllocatorManager.Free(allocator, context);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public BatchingContext(AllocatorManager.AllocatorHandle allocator)
            {
                DrawBatcher = new DrawBatcher(1024, allocator);
                DrawCommands = new UnsafeList<IndirectDrawCommand>(1024, allocator);
                DispatchCommands = new UnsafeList<IndirectDispatchCommand>(1024, allocator);
                Payloads = new UnsafeList<IndirectBatchPayload>(1024, allocator);
                DrawInfos = new UnsafeList<IndirectDrawInfo>(1024, allocator);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                DrawBatcher.Dispose();
                DrawCommands.Dispose();
                DispatchCommands.Dispose();
                Payloads.Dispose();
                DrawInfos.Dispose();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                DrawBatcher.Reset();
                DrawCommands.Clear();
                DispatchCommands.Clear();
                Payloads.Clear();
                DrawInfos.Clear();
            }
        }

        static class ComputeID
        {
            public static readonly int BuildInstanceDrawsVariables = Shader.PropertyToID("BuildInstanceDrawsVariables");

            public static readonly int CameraPosition = Shader.PropertyToID("_CameraPosition");
            public static readonly int CameraAnimPositionPrev = Shader.PropertyToID("_CameraAnimPositionPrev");
            public static readonly int CameraAnimPositionCurr = Shader.PropertyToID("_CameraAnimPositionCurr");

            public static readonly int DebugCounterRW = Shader.PropertyToID("_DebugCounterBufferRW");
            public static readonly int OcclusionDebugOverlay = Shader.PropertyToID("_OcclusionDebugOverlay");

            public static readonly int InstanceBatches = Shader.PropertyToID("_InstanceBatches");
            public static readonly int InstanceBatchItems = Shader.PropertyToID("_InstanceBatchItems");

            public static readonly int IndirectBatchPayloads = Shader.PropertyToID("_IndirectBatchPayloads");
            public static readonly int IndirectDrawInfos = Shader.PropertyToID("_IndirectDrawInfos");
            public static readonly int IndirectDrawArgsRW = Shader.PropertyToID("_IndirectDrawArgsRW");
            public static readonly int IndirectInstanceVisibilityRW = Shader.PropertyToID("_IndirectInstanceVisibilityRW");
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BuildInstanceDrawsShaderVariables
        {
            public float4 _CameraPosition;
            public float4 _CameraAnimPositionPrev;
            public float4 _CameraAnimPositionCurr;
            public uint   _BatchStart;
            public uint   _BatchCount;
            public uint   _DebugCounterIndex;
            public uint   _IndirectDrawCount;
        }

        public static class Profiling
        {
            public static readonly ProfilerMarker ProcessBatchesJob = new ProfilerMarker("InstanceCuller.ProcessBatches");
            public static readonly ProfilerMarker UpdateBuffers = new ProfilerMarker("InstanceCuller.UpdateBuffers");
            public static readonly ProfilerMarker RenderMeshIndirect = new ProfilerMarker("InstanceCuller.RenderMeshIndirect");
        }

        public static class ProfilingCounters
        {
            public static readonly ProfilerCounterValue<int> Draws = new ProfilerCounterValue<int>("Flora.Draws", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<int> Batches = new ProfilerCounterValue<int>("Flora.Batches", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<int> Submitted = new ProfilerCounterValue<int>("Flora.Instances", ProfilerMarkerDataUnit.Count);

            public static readonly ProfilerCounterValue<int> Visible = new ProfilerCounterValue<int>("Flora.VisibleInstances", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<int> Culled = new ProfilerCounterValue<int>("Flora.CulledInstances", ProfilerMarkerDataUnit.Count);
            public static readonly ProfilerCounterValue<int> Occluded = new ProfilerCounterValue<int>("Flora.OccludedInstances", ProfilerMarkerDataUnit.Count);

            [Conditional("ENABLE_PROFILER")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void NextFrame()
            {
                Draws.Value = 0;
                Batches.Value = 0;
                Submitted.Value = 0;
                Visible.Value = 0;
                Culled.Value = 0;
                Occluded.Value = 0;
            }
        }

        InstancingContext m_InstancingContext;
        Camera m_Camera;
        InstancedCameraID m_CameraID;

        UnsafeThreadLocalList<DrawBatch> m_Batches;
        BatchingContext* m_BatchContext;
        JobHandle m_CullingHandle;
        bool m_HasScheduledCull;

        ComputeShader m_Compute;
        int m_ResetIndirectDrawArgsKernel;
        int m_BuildInstanceDrawsKernel;
        LocalKeyword m_UseDensityKeyword;
        LocalKeyword m_UseOcclusionKeyword;
        LocalKeyword m_DebugCountersKeyword;
        LocalKeyword m_EditorSelectionOnlyKeyword;
        OcclusionTestComputeShader m_OcclusionTestCompute;

        GraphicsBuffer m_PerDrawBuffer;
        GraphicsBuffer m_IndirectBatchPayloads;
        GraphicsBuffer m_BatchItemBuffer;
        GraphicsBuffer m_BatchBuffer;
        GraphicsBuffer m_IndirectDrawInfoBuffer;
        GraphicsBuffer m_VisibleInstancesBuffer;
        GraphicsBuffer m_IndirectArgsBuffer;
#if UNITY_2022_3_OR_NEWER
        ProfilingSampler m_BuildInstanceDrawsSampler;
#endif
        NativeArray<BuildInstanceDrawsShaderVariables> m_BuildInstanceDrawsVariables;
        GraphicsBuffer[] m_BuildInstanceDrawsVariablesBuffers;

        RenderParams[] m_RenderParams = new RenderParams[64];
        int m_RenderParamsIndex;

        MaterialPropertyBlock[] m_DrawMaterialProperties = new MaterialPropertyBlock[64];
        int m_DrawMaterialPropertiesIndex;

        const int k_MaxDebugCounters = 16 * (int)DebugCounterIndex.Count;
        GraphicsBuffer m_CounterBuffer;
        NativeQueue<CounterRequest> m_CounterPassRequests;
        int m_DebugCounterIndex;

        struct CounterRequest
        {
            public int Count;
            public AsyncGPUReadbackRequest Readback;
        }

        public GraphicsBuffer IndirectArgsBuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_IndirectArgsBuffer;
        }

        public GraphicsBuffer VisibleInstancesBuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_VisibleInstancesBuffer;
        }

        public UnsafeList<IndirectDrawCommand> IndirectDrawCommands
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_BatchContext->DrawCommands;
        }

        public bool HasScheduledCull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_HasScheduledCull;
        }

        public InstanceCuller(InstancingContext instancingContext, Camera camera, InstancedCameraID cameraID)
        {
            m_InstancingContext = instancingContext;
            m_Camera = camera;
            m_CameraID = cameraID;

            m_Compute = Resources.Load<ComputeShader>("Compute/BuildInstanceDraws");
            m_ResetIndirectDrawArgsKernel = m_Compute.FindKernel("ResetIndirectDrawArgs");
            m_BuildInstanceDrawsKernel = m_Compute.FindKernel("BuildInstanceDraws");
            m_UseDensityKeyword = new LocalKeyword(m_Compute, "USE_DENSITY");
            m_UseOcclusionKeyword = new LocalKeyword(m_Compute, "USE_OCCLUSION");
            m_DebugCountersKeyword = new LocalKeyword(m_Compute, "DEBUG_COUNTERS");
            m_EditorSelectionOnlyKeyword = new LocalKeyword(m_Compute, "EDITOR_SELECTION_ONLY");
            m_OcclusionTestCompute.Init(m_Compute);

            m_BatchContext = BatchingContext.Create(AllocatorManager.Persistent);
            m_Batches = new UnsafeThreadLocalList<DrawBatch>(1024, AllocatorManager.Persistent);

            m_CounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)DebugCounterIndex.Count * k_MaxDebugCounters, UnsafeUtility.SizeOf<int>());
            m_CounterBuffer.name = "DebugCounterBuffer";
            m_CounterPassRequests = new NativeQueue<CounterRequest>(AllocatorManager.Persistent);

            m_BuildInstanceDrawsVariables = new NativeArray<BuildInstanceDrawsShaderVariables>(1, Allocator.Persistent);
            m_BuildInstanceDrawsVariablesBuffers = Array.Empty<GraphicsBuffer>();

#if UNITY_2023_3_OR_NEWER
            m_BuildInstanceDrawsSampler = new ProfilingSampler("Flora: BuildInstanceDraws");
#endif
        }

        public void Dispose()
        {
            if (m_HasScheduledCull)
            {
                m_CullingHandle.Complete();
                m_CullingHandle = default;
                m_HasScheduledCull = false;
            }

            BatchingContext.Destroy(m_BatchContext, AllocatorManager.Persistent);
            m_BatchContext = null;
            m_Batches.Dispose();

            m_BatchBuffer?.Dispose();
            m_BatchItemBuffer?.Dispose();
            m_IndirectBatchPayloads?.Dispose();
            m_PerDrawBuffer?.Dispose();
            m_IndirectDrawInfoBuffer?.Dispose();
            m_VisibleInstancesBuffer?.Dispose();
            m_IndirectArgsBuffer?.Dispose();

            m_CounterBuffer?.Dispose();
            m_CounterPassRequests.Dispose();

            m_BuildInstanceDrawsVariables.Dispose();
            foreach (GraphicsBuffer buffer in m_BuildInstanceDrawsVariablesBuffers)
                buffer.Dispose();
            m_BuildInstanceDrawsVariablesBuffers = Array.Empty<GraphicsBuffer>();
        }

        void Reset()
        {
            m_BatchContext->Reset();
            m_RenderParamsIndex = 0;
            m_DrawMaterialPropertiesIndex = 0;

            if (m_HasScheduledCull)
            {
                m_CullingHandle.Complete();
                m_CullingHandle = default;
            }
        }

        public void NextFrame()
        {
            if (m_DebugCounterIndex > 0)
            {
                m_CounterPassRequests.Enqueue(new CounterRequest
                {
                    Count = m_DebugCounterIndex,
                    Readback = AsyncGPUReadback.Request(m_CounterBuffer, m_DebugCounterIndex * (int)DebugCounterIndex.Count * sizeof(uint), 0)
                });

                m_DebugCounterIndex = 0;
            }

            while (!m_CounterPassRequests.IsEmpty() && m_CounterPassRequests.Peek().Readback.done)
            {
                var req = m_CounterPassRequests.Dequeue();
                if (!req.Readback.hasError)
                {
                    NativeArray<int> src = req.Readback.GetData<int>();
                    if (src.Length == req.Count * (int)DebugCounterIndex.Count)
                    {
                        int totalVisible = 0;
                        int totalCulled = 0;
                        int totalOccluded = 0;

                        for (int i = 0; i < req.Count; i++)
                        {
                            int offset = i * (int)DebugCounterIndex.Count;
                            totalVisible += src[offset + (int)DebugCounterIndex.Visible];
                            totalCulled += src[offset + (int)DebugCounterIndex.Culled];
                            totalOccluded += src[offset + (int)DebugCounterIndex.Occluded];
                        }

                        ProfilingCounters.Visible.Value += totalVisible;
                        ProfilingCounters.Culled.Value += totalCulled;
                        ProfilingCounters.Occluded.Value += totalOccluded;
                        break;
                    }
                }
            }

            // clear the GPU buffer for the next frame
            var zeros = new NativeArray<int>(k_MaxDebugCounters * (int)InstanceOcclusionTestDebugCounter.Count, Allocator.Temp);
            m_CounterBuffer.SetData(zeros);
            zeros.Dispose();
        }

        public void ScheduleCull(UnsafeIndirectList<InstancedRendererID> renderers)
        {
            if (m_HasScheduledCull)
                return;

            Reset();

            m_HasScheduledCull = true;
            m_CullingHandle = ScheduleClearBatches(m_InstancingContext.RendererManager.UpdateJobHandle);
            m_CullingHandle = ScheduleCullingJob(renderers, m_CullingHandle);
            m_CullingHandle = ScheduleProcessBatches(m_CullingHandle);
            m_InstancingContext.RendererManager.UpdateJobHandle = m_CullingHandle;
        }

        public void SubmitIndirectRenderCommands(UnsafeIndirectList<InstancedRendererID> renderers)
        {
            if (m_HasScheduledCull is false)
                ScheduleCull(renderers);

            m_CullingHandle.Complete();
            m_HasScheduledCull = false;

            if (m_BatchContext->DrawCommands.Length > 0)
            {
                UpdateBuffers();
                SubmitDrawCommands();
#if ENABLE_PROFILER
                ProfilingCounters.Draws.Value += m_BatchContext->DrawInfos.Length;
                ProfilingCounters.Batches.Value += m_BatchContext->DrawBatcher.Batches.Length;
                ProfilingCounters.Submitted.Value += m_BatchContext->DrawBatcher.TotalInstances;
#endif
            }
        }

        void SubmitDrawCommands()
        {
            if (m_BatchContext->DrawCommands.Length == 0)
                return;

            using ProfilerMarker.AutoScope _ = Profiling.RenderMeshIndirect.Auto();
            InstancedMeshID currentMeshID = InstancedMeshID.Null;
            Mesh currentMesh = null;
            InstancedMaterialID currentMaterialID = InstancedMaterialID.Null;
            InstancedMaterialVariant currentMaterialVariant = InstancedMaterialVariant.Instanced;
            Material currentMaterial = null;
            MaterialPropertyBlock currentProperties = null;
            InstancedBatchID currentBatchID = m_BatchContext->DrawCommands[0].BatchID;
            m_InstancingContext.SceneData.SetBuiltinPropertyMetadata(currentBatchID);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool hasDebugOverride = DebugDisplayData.IsActive() && DebugDisplayData.Instance.DebugShaderOverrideMode != DebugShaderOverrideMode.None;
#else
            bool hasDebugOverride = false;
#endif

            for (int i = 0; i < m_BatchContext->DrawCommands.Length; i++)
            {
                ref readonly IndirectDrawCommand indirectDrawCmd = ref m_BatchContext->DrawCommands.Ptr[i];

                if (indirectDrawCmd.MeshID != currentMeshID)
                {
                    if (!m_InstancingContext.MeshManager.TryGetMesh(indirectDrawCmd.MeshID, out currentMesh))
                    {
                        Debug.Log($"Failed to get mesh for MeshID: {indirectDrawCmd.MeshID}");
                        continue;
                    }

                    currentMeshID = indirectDrawCmd.MeshID;
                }

                if (indirectDrawCmd.MaterialID != currentMaterialID || indirectDrawCmd.MaterialVariant != currentMaterialVariant)
                {
                    if (!m_InstancingContext.MaterialManager.TryGetMaterialVariant(indirectDrawCmd.MaterialID, indirectDrawCmd.MaterialVariant, hasDebugOverride, out currentMaterial))
                    {
                        Debug.Log($"Failed to get material for MaterialID: {indirectDrawCmd.MaterialID}");
                        continue;
                    }

                    currentMaterialID = indirectDrawCmd.MaterialID;
                    currentMaterialVariant = indirectDrawCmd.MaterialVariant;
                }

                if (indirectDrawCmd.BatchID != currentBatchID || hasDebugOverride)
                {
                    if (m_DrawMaterialProperties.Length <= m_DrawMaterialPropertiesIndex)
                        Array.Resize(ref m_DrawMaterialProperties, m_DrawMaterialPropertiesIndex * 2);

                    currentProperties = m_DrawMaterialProperties[m_DrawMaterialPropertiesIndex] ?? (m_DrawMaterialProperties[m_DrawMaterialPropertiesIndex] = new MaterialPropertyBlock());
                    m_DrawMaterialPropertiesIndex++;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (hasDebugOverride)
                        currentProperties.SetInteger(DebugShaderID.flora_DebugLODIndex, indirectDrawCmd.LODIndex);
#endif

                    m_InstancingContext.SceneData.SetBuiltinPropertyMetadata(indirectDrawCmd.BatchID, currentProperties);
                    currentBatchID = indirectDrawCmd.BatchID;
                }

                if (m_RenderParams.Length <= m_RenderParamsIndex)
                    Array.Resize(ref m_RenderParams, m_RenderParamsIndex * 2);

                ref RenderParams renderParams = ref m_RenderParams[m_RenderParamsIndex++];
                renderParams.layer = indirectDrawCmd.FilterSettings.Layer;
                renderParams.renderingLayerMask = indirectDrawCmd.FilterSettings.RenderingLayerMask;
                renderParams.rendererPriority = 0;
                renderParams.motionVectorMode = indirectDrawCmd.FilterSettings.MotionMode;
                renderParams.reflectionProbeUsage = indirectDrawCmd.FilterSettings.ReflectionProbeUsage;
                renderParams.material = currentMaterial;
                renderParams.matProps = currentProperties;
                renderParams.shadowCastingMode = indirectDrawCmd.FilterSettings.ShadowCastingMode;
                renderParams.receiveShadows = indirectDrawCmd.FilterSettings.ReceiveShadows;
                renderParams.lightProbeUsage = indirectDrawCmd.FilterSettings.LightProbeUsage;
                renderParams.lightProbeProxyVolume = null;
                renderParams.worldBounds = indirectDrawCmd.Bounds;
                renderParams.camera = m_Camera;

                Graphics.RenderMeshIndirect(renderParams, currentMesh, m_IndirectArgsBuffer, indirectDrawCmd.CommandCount, indirectDrawCmd.CommandIndex);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        JobHandle ScheduleClearBatches(JobHandle inputDeps) => new ClearBatchesJob { Batches = m_Batches }.Schedule(inputDeps);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        JobHandle ScheduleCullingJob(UnsafeIndirectList<InstancedRendererID> renderers, JobHandle inputDeps)
        {
            InstancedCameraCullingData cameraCullingData = m_InstancingContext.CameraManager.Data.Culling[m_CameraID];
            if (cameraCullingData.CullingPlanePackets.Length == 0)
                return default;

            UnsafeArray<bool> staticOcclusionResults = default;
            if (m_InstancingContext.HasStaticOcclusionManager() &&
                m_InstancingContext.GetStaticOcclusionManager().TryGetContext(m_CameraID, out StaticOcclusionContext staticOcclusionContext))
            {
                staticOcclusionResults = staticOcclusionContext.Culled;
            }

            int forceLOD = -1;
            int onlyLOD = -1;
            bool disableCulling = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugDisplayData.IsActive())
            {
                forceLOD = DebugDisplayData.Instance.ForceLOD;
                onlyLOD = DebugDisplayData.Instance.OnlyLOD;
                disableCulling = DebugDisplayData.Instance.DisableCulling;
            }
#endif

            InstancedPrototypeDataArrays.ReadOnly prototypeArrays = m_InstancingContext.PrototypeManager.Data.AsReadOnly();
            InstancedRendererArrays rendererArrays = m_InstancingContext.RendererManager.Data;
            InstancedCameraLODData cameraLODData = m_InstancingContext.CameraManager.Data.LOD[m_CameraID];
            InstancedCameraAnimatedCrossFadeData animatedCrossFadeData = m_InstancingContext.CameraManager.Data.AnimatedCrossFade[m_CameraID];

            InstanceCullingJob cullingJob = new InstanceCullingJob
            {
                DrawBatches = m_Batches,
                FrameAllocator = m_InstancingContext.FrameAllocator,

                CullViewType = InstanceCullViewType.Camera,
                IsSceneViewCamera = cameraCullingData.Flags.IsSceneViewCamera(),
                CameraPosition = cameraLODData.Origin,
                AnimatedCameraPosition0 = animatedCrossFadeData.ViewPosition0,
                AnimatedCameraPosition1 = animatedCrossFadeData.ViewPosition1,
                FarClipPlane = cameraCullingData.FarClipPlane,
                DisableCullDistance = InstancingSystem.DisableRenderDistance,
                CullingLayerMask = cameraCullingData.CullingLayerMask,
                SceneCullingMask = cameraCullingData.SceneCullingMask,
                CullingPlanePackets = cameraCullingData.CullingPlanePackets.AsReadOnly(),
                MaxLOD = QualitySettings.maximumLODLevel,
                IsOrthographic = cameraLODData.IsOrthographic,
                MinimumScreenSize = cameraLODData.MinimumScreenSize,
                ScreenRelativeMetric = cameraLODData.ScreenRelativeMetric,
                Renderer = renderers.AsUnsafeArray().AsReadOnly(),
                StaticOcclusionResults = staticOcclusionResults.AsReadOnly(),
                RendererCulling = rendererArrays.Culling.AsReadOnly(),
                RendererTreeData = rendererArrays.Tree.AsReadOnly(),
                RendererOcclusionData = rendererArrays.Occlusion.AsReadOnly(),
                RendererBufferAllocations = rendererArrays.BatchAllocation.AsReadOnly(),
                RendererIsVisibleInScene = rendererArrays.IsVisibleInScene,
                CullingNodeStore = rendererArrays.NodeStore.AsReadOnly(),
                UnbuiltInstanceBoundsStore = rendererArrays.UnbuiltBoundsStore.AsReadOnly(),
                PrototypeArrays = prototypeArrays,
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                DebugForceLOD = forceLOD,
                DebugOnlyLOD = onlyLOD,
                DebugDisableCulling = disableCulling,
#endif
            };

            JobHandle cameraCullingHandle = cullingJob.ScheduleBatchByRef(renderers.Length, InstanceCullingJob.BatchSize, inputDeps);

            InstancedCameraShadowData cameraShadowData = m_InstancingContext.CameraManager.Data.Shadow[m_CameraID];
            if (cameraShadowData.ShadowEnabled)
            {
                cullingJob.CullViewType = InstanceCullViewType.Light;
                cullingJob.RendererHasShadowCasters = rendererArrays.HasShadowCasters;
                cullingJob.CullingPlanePackets = cameraShadowData.CullingPlanePackets.AsReadOnly();
                cullingJob.LightFacingPlanes = cameraShadowData.LightFacingPlanes.AsReadOnly();
                cullingJob.WorldToLightSpaceRotation = cameraShadowData.WorldToLightSpaceRotation;
                cullingJob.ReceiverSphereInLightSpace = cameraShadowData.ReceiverSphereInLightSpace;

                JobHandle shadowCullingHandle = cullingJob.ScheduleBatchByRef(renderers.Length, InstanceCullingJob.BatchSize, inputDeps);
                cameraCullingHandle = JobHandle.CombineDependencies(cameraCullingHandle, shadowCullingHandle);
            }

            return cameraCullingHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        JobHandle ScheduleProcessBatches(JobHandle inputDeps)
        {
            return new ProcessBatchesJob
            {
                Batches = m_Batches,
                BatchContext = m_BatchContext,
            }.Schedule(inputDeps);
        }

        struct InstanceBatchComparer : IComparer<DrawBatch>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare(DrawBatch x, DrawBatch y)
            {
                int batchCmp = x.BatchID.CompareTo(y.BatchID);
                if (batchCmp != 0) return batchCmp;
                int keyCmp = x.SortKey.CompareTo(y.SortKey);
                if (keyCmp != 0) return keyCmp;
                return x.RendererID.CompareTo(y.RendererID);
            }
        }

        [BurstCompile]
        struct ClearBatchesJob : IJob
        {
            public UnsafeThreadLocalList<DrawBatch> Batches;

            public void Execute()
            {
                Batches.Clear();
            }
        }

        [NoAlias, BurstCompile]
        struct ProcessBatchesJob : IJob
        {
            [ReadOnly] public UnsafeThreadLocalList<DrawBatch> Batches;
            [NoAlias, WriteOnly, NativeDisableUnsafePtrRestriction] public BatchingContext* BatchContext;

            public void Execute()
            {
                Profiling.ProcessBatchesJob.Begin();

                int totalBatchCount = 0;
                for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
                    totalBatchCount += Batches[i].Length;

                using UnsafeArray<DrawBatch> sortedBatches = new UnsafeArray<DrawBatch>(totalBatchCount, AllocatorManager.Temp);
                int sortedBatchesIndex = 0;

                for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
                {
                    UnsafeList<DrawBatch>* batch = Batches[i].List;
                    if (batch->Length != 0)
                    {
                        UnsafeUtility.MemCpy(sortedBatches.Ptr + sortedBatchesIndex, batch->Ptr, batch->Length * UnsafeUtility.SizeOf<DrawBatch>());
                        sortedBatchesIndex += batch->Length;
                    }
                }

                NativeSortExtension.Sort(sortedBatches.Ptr, totalBatchCount, new InstanceBatchComparer());

                IndirectDispatchCommand currentDispatchCommand = default;
                IndirectDrawCommand currentDrawCommand = default;
                SubMeshDescriptor currentDrawSubMesh = default;
                int currentPayloadLOD = -1;
                InstancedRendererID currentPayloadRendererID = InstancedRendererID.Null;

                for (int index = 0; index < totalBatchCount; index++)
                {
                    ref readonly DrawBatch batch = ref sortedBatches[index];

                    bool needsNewDispatchCommand = batch.BatchID != currentDispatchCommand.BatchID;

                    bool needsNewCommand = needsNewDispatchCommand ||
                                           batch.FilterSettings != currentDrawCommand.FilterSettings ||
                                           batch.MaterialVariant != currentDrawCommand.MaterialVariant ||
                                           batch.MaterialID != currentDrawCommand.MaterialID ||
                                           batch.MeshID != currentDrawCommand.MeshID;

                    bool needsNewDrawArg = needsNewCommand ||
                                           batch.SubMesh.indexStart != currentDrawSubMesh.indexStart ||
                                           batch.SubMesh.indexCount != currentDrawSubMesh.indexCount ||
                                           batch.SubMesh.baseVertex != currentDrawSubMesh.baseVertex;

                    bool needsNewPayload = needsNewCommand ||
                                           batch.LODIndex != currentPayloadLOD ||
                                           batch.RendererID != currentPayloadRendererID;

                    int currentInstanceDrawOffset = BatchContext->DrawBatcher.TotalInstances;

                    if (needsNewDispatchCommand)
                    {
                        BatchContext->DrawBatcher.FinishCurrentBatch();
                        currentDispatchCommand.Count = BatchContext->DrawBatcher.Batches.Length - currentDispatchCommand.Start;

                        if (currentDispatchCommand.Count > 0)
                            BatchContext->DispatchCommands.Add(currentDispatchCommand);

                        currentDispatchCommand.BatchID = batch.BatchID;
                        currentDispatchCommand.Start = BatchContext->DrawBatcher.Batches.Length;
                        currentDispatchCommand.Count = 0;
                    }

                    if (needsNewCommand)
                    {
                        if (currentDrawCommand.CommandCount > 0)
                            BatchContext->DrawCommands.Add(currentDrawCommand);

                        currentDrawCommand.LODIndex = batch.LODIndex;
                        currentDrawCommand.CommandIndex = BatchContext->DrawInfos.Length;
                        currentDrawCommand.CommandCount = 0;
                        currentDrawCommand.IndirectVisibleIndex = currentInstanceDrawOffset;
                        currentDrawCommand.MaterialID = batch.MaterialID;
                        currentDrawCommand.MaterialVariant = batch.MaterialVariant;
                        currentDrawCommand.BatchID = batch.BatchID;
                        currentDrawCommand.MeshID = batch.MeshID;
                        currentDrawCommand.SubMeshIndex = batch.SubMeshIndex;
                        currentDrawCommand.FilterSettings = batch.FilterSettings;
                        currentDrawCommand.Bounds = AxisAlignedBox.Empty;
                    }

                    if (needsNewDrawArg)
                    {
                        BatchContext->DrawInfos.Add(new IndirectDrawInfo
                        {
                            IndexCountPerInstance = (uint)batch.SubMesh.indexCount,
                            InstanceCount = 0,
                            StartIndex = (uint)batch.SubMesh.indexStart,
                            BaseVertexIndex = (uint)batch.SubMesh.baseVertex,
                            IndirectStartIndex = (uint)currentInstanceDrawOffset,
                            CullFlags = (uint)batch.CullFlags,
                        });

                        currentDrawSubMesh = batch.SubMesh;
                        currentDrawCommand.CommandCount++;
                    }

                    if (needsNewPayload)
                    {
                        BatchContext->Payloads.Add(new IndirectBatchPayload
                        {
                            CullDistance = batch.CullDistance,
                            LODDistances = batch.LODDistances,
                            LODCoefficients = batch.LODCoefficients,
                        });

                        currentPayloadLOD = batch.LODIndex;
                        currentPayloadRendererID = batch.RendererID;
                    }

                    int indirectDrawIndex = BatchContext->DrawInfos.Length - 1;
                    int payloadIndex = BatchContext->Payloads.Length - 1;
                    uint packedDrawAndPayloadIndex = PackPayload(indirectDrawIndex, payloadIndex);

                    for (int i = 0; i < batch.DrawRanges.Length; i++)
                    {
                        DrawRange drawRange = batch.DrawRanges[i];
                        if (drawRange.InstanceCount > 0)
                        {
                            int instanceStartIndex = batch.InstanceDataOffset + drawRange.StartInstanceIndex;
                            BatchContext->DrawBatcher.AddInstances(instanceStartIndex, drawRange.InstanceCount, packedDrawAndPayloadIndex);
                        }
                    }

                    currentDrawCommand.Bounds += batch.Bounds;
                }

                BatchContext->DrawBatcher.FinalizeBatches();
                currentDispatchCommand.Count = BatchContext->DrawBatcher.Batches.Length - currentDispatchCommand.Start;

                if (currentDispatchCommand.Count > 0)
                    BatchContext->DispatchCommands.Add(currentDispatchCommand);

                if (currentDrawCommand.CommandCount > 0)
                    BatchContext->DrawCommands.Add(currentDrawCommand);

                Profiling.ProcessBatchesJob.End();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint PackPayload(int indirectDrawIndex, int payloadIndex) => (uint)(((indirectDrawIndex & 0xFFFF) << 16) | (payloadIndex & 0xFFFF));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int GrowCapacity(int capacity) => math.max(capacity + (capacity >> 1), 64);

        void UpdateBuffers()
        {
            using (Profiling.UpdateBuffers.Auto())
            {
                int batchCount = m_BatchContext->DrawBatcher.Batches.Length;
                if (m_BatchBuffer == null || m_BatchBuffer.count < batchCount)
                {
                    int newCapacity = GrowCapacity(batchCount);
                    m_BatchBuffer?.Dispose();
                    m_BatchBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity, 8);
                    m_BatchBuffer.name = "InstanceBatches";
                }

                m_BatchBuffer.SetDataUnsafe(m_BatchContext->DrawBatcher.Batches.Ptr, batchCount);

                int batchItemCount = m_BatchContext->DrawBatcher.Items.Length;
                if (m_BatchItemBuffer == null || m_BatchItemBuffer.count < batchItemCount)
                {
                    int newCapacity = GrowCapacity(batchItemCount);
                    m_BatchItemBuffer?.Dispose();
                    m_BatchItemBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity, 8);
                    m_BatchItemBuffer.name = "InstanceBatchItems";
                }

                m_BatchItemBuffer.SetDataUnsafe(m_BatchContext->DrawBatcher.Items.Ptr, batchItemCount);

                int payloadCount = m_BatchContext->Payloads.Length;
                if (m_IndirectBatchPayloads == null || m_IndirectBatchPayloads.count < payloadCount)
                {
                    int newCapacity = GrowCapacity(payloadCount);
                    m_IndirectBatchPayloads?.Dispose();
                    m_IndirectBatchPayloads = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity, UnsafeUtility.SizeOf<IndirectBatchPayload>());
                    m_IndirectBatchPayloads.name = "IndirectBatchPayloads";
                }

                m_IndirectBatchPayloads.SetDataUnsafe(m_BatchContext->Payloads.Ptr, payloadCount);

                int drawCount = m_BatchContext->DrawInfos.Length;

                if (m_IndirectDrawInfoBuffer == null || m_IndirectDrawInfoBuffer.count < drawCount)
                {
                    int newCapacity = GrowCapacity(drawCount);
                    m_IndirectDrawInfoBuffer?.Dispose();
                    m_IndirectDrawInfoBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity, UnsafeUtility.SizeOf<IndirectDrawInfo>());
                    m_IndirectDrawInfoBuffer.name = "IndirectDrawInfos";
                }

                m_IndirectDrawInfoBuffer.SetDataUnsafe(m_BatchContext->DrawInfos.Ptr, drawCount);

                if (m_IndirectArgsBuffer == null || m_IndirectArgsBuffer.count < drawCount)
                {
                    int newCapacity = GrowCapacity(drawCount);
                    m_IndirectArgsBuffer?.Dispose();
                    m_IndirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, newCapacity, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    m_IndirectArgsBuffer.name = "IndirectArgs";

                    // Whenever this is created/resized, we need to zero out the buffer so Unity doesn't try to draw with invalid data
                    using NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs> zeroArgs = new NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs>(newCapacity, Allocator.Temp);
                    m_IndirectArgsBuffer.SetData(zeroArgs);
                }

                int visibleInstanceCount = m_BatchContext->DrawBatcher.TotalInstances;
                if (m_VisibleInstancesBuffer == null || m_VisibleInstancesBuffer.count < visibleInstanceCount)
                {
                    int newCapacity = GrowCapacity(visibleInstanceCount);
                    m_VisibleInstancesBuffer?.Dispose();
                    m_VisibleInstancesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, newCapacity, 4);
                    m_VisibleInstancesBuffer.name = "IndirectInstanceVisibilityBuffer";
                }

                InstancedCameraAnimatedCrossFadeData cameraAnimatedCrossFade = m_InstancingContext.CameraManager.Data.AnimatedCrossFade[m_CameraID];
                InstancedCameraLODData cameraLOD = m_InstancingContext.CameraManager.Data.LOD[m_CameraID];

                float transitionAlpha = cameraAnimatedCrossFade.GetTransitionAlpha(m_InstancingContext.Time);
                float4 cameraPosition = new float4(cameraLOD.Origin, cameraLOD.ScreenRelativeMetricSq);
                float4 cameraAnimPositionPrev = new float4(cameraAnimatedCrossFade.ViewPosition0, 1.0f - transitionAlpha);
                float4 cameraAnimPositionCurr = new float4(cameraAnimatedCrossFade.ViewPosition1, transitionAlpha);
                int debugCounterIndex = m_DebugCounterIndex;
                int indirectDrawCount = m_BatchContext->DrawInfos.Length;
                int dispatchCount = m_BatchContext->DispatchCommands.Length;

                m_BuildInstanceDrawsVariables.Resize(m_BatchContext->DispatchCommands.Length, Allocator.Persistent);

                for (int i = 0; i < dispatchCount; i++)
                {
                    IndirectDispatchCommand dispatchCommand = m_BatchContext->DispatchCommands.Ptr[i];
                    m_BuildInstanceDrawsVariables[i] = new BuildInstanceDrawsShaderVariables
                    {
                        _CameraPosition = cameraPosition,
                        _CameraAnimPositionPrev = cameraAnimPositionPrev,
                        _CameraAnimPositionCurr = cameraAnimPositionCurr,
                        _BatchStart = (uint)dispatchCommand.Start,
                        _BatchCount = (uint)dispatchCommand.Count,
                        _DebugCounterIndex = (uint)debugCounterIndex,
                        _IndirectDrawCount = (uint)indirectDrawCount,
                    };
                }

                if (m_BuildInstanceDrawsVariablesBuffers.Length != dispatchCount)
                {
                    if (dispatchCount < m_BuildInstanceDrawsVariablesBuffers.Length)
                    {
                        for (int i = dispatchCount; i < m_BuildInstanceDrawsVariablesBuffers.Length; i++)
                            m_BuildInstanceDrawsVariablesBuffers[i].Dispose();
                    }

                    Array.Resize(ref m_BuildInstanceDrawsVariablesBuffers, dispatchCount);
                }

                for (int i = 0; i < dispatchCount; i++)
                {
                    if (m_BuildInstanceDrawsVariablesBuffers[i] == null)
                    {
                        m_BuildInstanceDrawsVariablesBuffers[i] = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, UnsafeUtility.SizeOf<BuildInstanceDrawsShaderVariables>());
                        m_BuildInstanceDrawsVariablesBuffers[i].name = $"BuildInstanceDrawsVariables_Batch_{i}";
                    }

                    m_BuildInstanceDrawsVariablesBuffers[i].SetData(m_BuildInstanceDrawsVariables, i, 0, 1);
                }
            }
        }

        // --- CommandBuffer API ---

        public void BuildInstanceDrawsWithoutOcclusion(CommandBuffer cmd, InstanceCullingFlags cullingFlags = InstanceCullingFlags.None) => BuildInstanceDraws(cmd, default, default, cullingFlags);

        public void BuildInstanceDraws(CommandBuffer cmd, in OcclusionCullingSettings settings, ReadOnlySpan<SubviewOcclusionTest> subviewOcclusionTests, InstanceCullingFlags cullingFlags = InstanceCullingFlags.None)
        {
            if (m_IndirectDrawInfoBuffer == null || m_BatchContext->DrawInfos.Length == 0)
                return;

            InstanceOcclusionTestSubviewSettings subviewSettings = InstanceOcclusionTestSubviewSettings.FromSpan(subviewOcclusionTests);
            PrepareOcclusionCulling(cmd, settings, subviewSettings);
            AddBuildInstanceDrawsDispatch(CommandBufferHelpers.GetComputeCommandBuffer(cmd), cullingFlags);
        }

        void PrepareOcclusionCulling(CommandBuffer cmd, in OcclusionCullingSettings settings, in InstanceOcclusionTestSubviewSettings subviewSettings)
        {
            if (settings.OcclusionTest == OcclusionTest.None || subviewSettings.TestCount == 0)
                return;

            bool hasOcclusionCulling = false;
            bool hasOcclusionCullingDebug = false;

            if (m_InstancingContext.HasOcclusionManager())
            {
                OcclusionManager occlusionManager = m_InstancingContext.GetOcclusionManager();
                int viewInstanceID = m_InstancingContext.CameraManager.CameraIDHash.GetKey(m_CameraID);

                hasOcclusionCulling = occlusionManager.GetOccluderContext(viewInstanceID, out OccluderContext occluderCtx);
                if (hasOcclusionCulling)
                {
                    // check we have occluders for all the required subviews, disable the occlusion test if not
                    hasOcclusionCulling = ((subviewSettings.OccluderSubviewMask & occluderCtx.SubviewValidMask) == subviewSettings.OccluderSubviewMask);

                    if (hasOcclusionCulling)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        if (DebugDisplayData.IsActive())
                        {
                            hasOcclusionCulling = !DebugDisplayData.Instance.OcclusionOverrideTestToAlwaysPass;
                            hasOcclusionCullingDebug = OcclusionManager.UseOcclusionDebug(in occluderCtx);
                        }
#endif

                        OccluderHandles occluderHandles = new OccluderHandles
                        {
                            OccluderDepthPyramid = occluderCtx.OccluderDepthPyramid,
                            OcclusionDebugOverlay = occluderCtx.OcclusionDebugOverlay,
                        };

                        occlusionManager.PrepareCulling(cmd, in occluderCtx, settings, subviewSettings, m_OcclusionTestCompute, hasOcclusionCullingDebug);

                        OcclusionManager.SetDepthPyramid(cmd, m_OcclusionTestCompute, m_BuildInstanceDrawsKernel, occluderHandles);

                        if (hasOcclusionCullingDebug)
                            OcclusionManager.SetDebugPyramid(cmd, m_OcclusionTestCompute, m_BuildInstanceDrawsKernel, occluderHandles);
                    }
                }
            }

            cmd.SetKeyword(m_Compute, m_UseDensityKeyword, !InstancingSystem.DisableDensityCulling);
            cmd.SetKeyword(m_Compute, m_UseOcclusionKeyword, hasOcclusionCulling);
        }

        void AddBuildInstanceDrawsDispatch(ComputeCommandBuffer cmd, InstanceCullingFlags cullingFlags = InstanceCullingFlags.None)
        {
            bool isEditorSelection = (cullingFlags & InstanceCullingFlags.SelectionOnly) != 0;

            bool debugCounters = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugDisplayData.IsActive())
            {
                DebugDisplayData debugDisplayData = DebugDisplayData.Instance;
                debugCounters = debugDisplayData.EnableDispatchCounters && m_DebugCounterIndex + 1 < k_MaxDebugCounters;
                if (debugCounters)
                {
                    cmd.SetComputeBufferParam(m_Compute, m_BuildInstanceDrawsKernel, ComputeID.DebugCounterRW, m_CounterBuffer);
                    m_DebugCounterIndex++;
                }
            }
#endif

            const int kThreadGroupSize = 64;

            cmd.SetKeyword(m_Compute, m_UseDensityKeyword, !InstancingSystem.DisableDensityCulling);
            cmd.SetKeyword(m_Compute, m_DebugCountersKeyword, debugCounters);
            cmd.SetKeyword(m_Compute, m_EditorSelectionOnlyKeyword, isEditorSelection);
            cmd.SetComputeConstantBufferParam(m_Compute, ComputeID.BuildInstanceDrawsVariables, m_BuildInstanceDrawsVariablesBuffers[0], 0, sizeof(BuildInstanceDrawsShaderVariables));

            cmd.SetComputeBufferParam(m_Compute, m_ResetIndirectDrawArgsKernel, ComputeID.IndirectDrawInfos, m_IndirectDrawInfoBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_ResetIndirectDrawArgsKernel, ComputeID.IndirectDrawArgsRW, m_IndirectArgsBuffer);
            int3 resetGroupsCount = ComputeUtility.WrapDispatchCount(m_BatchContext->DrawInfos.Length, kThreadGroupSize);
            cmd.DispatchCompute(m_Compute, m_ResetIndirectDrawArgsKernel, resetGroupsCount);

            cmd.SetComputeBufferParam(m_Compute, m_BuildInstanceDrawsKernel, ComputeID.InstanceBatches, m_BatchBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_BuildInstanceDrawsKernel, ComputeID.InstanceBatchItems, m_BatchItemBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_BuildInstanceDrawsKernel, ComputeID.IndirectBatchPayloads, m_IndirectBatchPayloads);
            cmd.SetComputeBufferParam(m_Compute, m_BuildInstanceDrawsKernel, ComputeID.IndirectDrawInfos, m_IndirectDrawInfoBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_BuildInstanceDrawsKernel, ComputeID.IndirectInstanceVisibilityRW, m_VisibleInstancesBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_BuildInstanceDrawsKernel, ComputeID.IndirectDrawArgsRW, m_IndirectArgsBuffer);
            m_InstancingContext.SceneData.SetComputeBuffers(cmd, m_Compute, m_BuildInstanceDrawsKernel);

            for (int batchIndex = 0; batchIndex < m_BatchContext->DispatchCommands.Length; batchIndex++)
            {
                IndirectDispatchCommand indirectDispatchCommand = m_BatchContext->DispatchCommands.Ptr[batchIndex];
                m_InstancingContext.SceneData.SetBuiltinPropertyMetadata(indirectDispatchCommand.BatchID, cmd, m_Compute);

                if (batchIndex > 0)
                {
                    cmd.SetComputeConstantBufferParam(m_Compute, ComputeID.BuildInstanceDrawsVariables, m_BuildInstanceDrawsVariablesBuffers[batchIndex], 0, sizeof(BuildInstanceDrawsShaderVariables));
                }

                // Note: `indirectDispatchCommand.Count` corresponds to `DrawBatcher` batches, which are calculated in groups of `kThreadGroupSize`
                int3 buildGroupsCount = ComputeUtility.WrapGroupCount(indirectDispatchCommand.Count);
                cmd.DispatchCompute(m_Compute, m_BuildInstanceDrawsKernel, buildGroupsCount);
            }

            cmd.SetGlobalBuffer(InstancingShaderID.flora_IndirectInstanceVisibility, m_VisibleInstancesBuffer);
        }

        // --- RenderGraph Integration ---
#if UNITY_2023_3_OR_NEWER
        struct BuildInstanceDrawsHandles
        {
            public BufferHandle BatchBuffer;
            public BufferHandle BatchItemBuffer;
            public BufferHandle IndirectBatchPayloads;
            public BufferHandle IndirectDrawInfoBuffer;
            public BufferHandle IndirectDrawArgsBuffer;
            public BufferHandle IndirectInstanceVisibilityBuffer;

            public void Use(IBaseRenderGraphBuilder builder)
            {
                builder.UseBuffer(BatchBuffer);
                builder.UseBuffer(BatchItemBuffer);
                builder.UseBuffer(IndirectBatchPayloads);
                builder.UseBuffer(IndirectDrawInfoBuffer);
                builder.UseBuffer(IndirectDrawArgsBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(IndirectInstanceVisibilityBuffer, AccessFlags.Write);
            }
        }

        class BuildInstanceDrawsData
        {
            public InstanceCuller CullingContext;
            public OcclusionCullingSettings Settings;
            public OccluderHandlesRenderGraph OccluderHandles;
            public InstanceOcclusionTestSubviewSettings SubviewSettings;
            public BuildInstanceDrawsHandles DrawHandles;
        }

        BuildInstanceDrawsHandles ImportBuffers(RenderGraph renderGraph)
        {
            return new BuildInstanceDrawsHandles
            {
                BatchBuffer = renderGraph.ImportBuffer(m_BatchBuffer),
                BatchItemBuffer = renderGraph.ImportBuffer(m_BatchItemBuffer),
                IndirectBatchPayloads = renderGraph.ImportBuffer(m_IndirectBatchPayloads),
                IndirectDrawArgsBuffer = renderGraph.ImportBuffer(m_IndirectArgsBuffer),
                IndirectDrawInfoBuffer = renderGraph.ImportBuffer(m_IndirectDrawInfoBuffer),
                IndirectInstanceVisibilityBuffer = renderGraph.ImportBuffer(m_VisibleInstancesBuffer),
            };
        }

        public void BuildInstanceDraws(RenderGraph renderGraph, in OcclusionCullingSettings settings, ReadOnlySpan<SubviewOcclusionTest> subviewOcclusionTests)
        {
            if (m_IndirectDrawInfoBuffer == null || m_BatchContext->DrawInfos.Length == 0)
                return;

            using (var builder = renderGraph.AddComputePass<BuildInstanceDrawsData>("Flora:BuildInstanceDraws", out var passData, m_BuildInstanceDrawsSampler))
            {
                builder.AllowGlobalStateModification(true);

                passData.CullingContext = this;

                if (m_InstancingContext.HasOcclusionManager())
                {
                    OcclusionManager occlusionManager = m_InstancingContext.GetOcclusionManager();
                    int viewInstanceID = m_InstancingContext.CameraManager.CameraIDHash.GetKey(m_CameraID);

#if FLORA_ENABLE_EXPERIMENTAL_GPU_DRIVEN_OCCLUSION_INTEGRATION
                    bool isGPUResidentOcclusionEnabled = GPUResidentDrawer.IsInstanceOcclusionCullingEnabled();
                    if (isGPUResidentOcclusionEnabled && GPUDrivenUtility.TryGetOccluderHandles(renderGraph, viewInstanceID, out OccluderHandlesRenderGraph occluderHandles))
                    {
                        passData.OccluderHandles = occluderHandles;
                    }
                    else
#endif
                    {
                        if (occlusionManager.GetOccluderContext(viewInstanceID, out OccluderContext occluderCtx))
                            passData.OccluderHandles = occluderCtx.Import(renderGraph);
                    }

                    if (passData.OccluderHandles.IsValid())
                    {
                        passData.Settings = settings;
                        passData.SubviewSettings = InstanceOcclusionTestSubviewSettings.FromSpan(subviewOcclusionTests);
                        passData.OccluderHandles.UseForOcclusionTest(builder);
                    }
                }

                passData.DrawHandles = ImportBuffers(renderGraph);
                passData.DrawHandles.Use(builder);

                builder.SetRenderFunc((BuildInstanceDrawsData data, ComputeGraphContext context) =>
                {
                    if (data.OccluderHandles.IsValid())
                    {
                        data.CullingContext.PrepareOcclusionCulling(context.cmd, data.Settings, data.SubviewSettings, data.OccluderHandles);
                    }
                    else
                    {
                        context.cmd.SetKeyword(m_Compute, m_UseOcclusionKeyword, false);
                        context.cmd.SetKeyword(m_Compute, m_OcclusionTestCompute.OcclusionDebugKeyword, false);
                    }

                    data.CullingContext.AddBuildInstanceDrawsDispatch(context.cmd);
                });
            }
        }

        void PrepareOcclusionCulling(ComputeCommandBuffer cmd, in OcclusionCullingSettings settings, in InstanceOcclusionTestSubviewSettings subviewSettings, in OccluderHandlesRenderGraph occluderHandles)
        {
            if (settings.OcclusionTest == OcclusionTest.None || subviewSettings.TestCount == 0)
                return;

            bool hasOcclusionCulling = false;
            bool hasOcclusionCullingDebug = false;

            if (m_InstancingContext.HasOcclusionManager())
            {
                OcclusionManager occlusionManager = m_InstancingContext.GetOcclusionManager();
                int viewInstanceID = m_InstancingContext.CameraManager.CameraIDHash.GetKey(m_CameraID);

                hasOcclusionCulling = occlusionManager.GetOccluderContext(viewInstanceID, out OccluderContext occluderCtx);
                if (hasOcclusionCulling)
                {
                    // check we have occluders for all the required subviews, disable the occlusion test if not
                    hasOcclusionCulling = ((subviewSettings.OccluderSubviewMask & occluderCtx.SubviewValidMask) == subviewSettings.OccluderSubviewMask);

                    if (hasOcclusionCulling)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        if (DebugDisplayData.IsActive())
                        {
                            hasOcclusionCulling = !DebugDisplayData.Instance.OcclusionOverrideTestToAlwaysPass;
                            hasOcclusionCullingDebug = OcclusionManager.UseOcclusionDebug(in occluderCtx);
                        }
#endif

                        occlusionManager.PrepareCulling(cmd, in occluderCtx, settings, subviewSettings, m_OcclusionTestCompute, hasOcclusionCullingDebug);

                        OcclusionManager.SetDepthPyramid(cmd, m_OcclusionTestCompute, m_BuildInstanceDrawsKernel, occluderHandles);

                        if (hasOcclusionCullingDebug)
                            OcclusionManager.SetDebugPyramid(cmd, m_OcclusionTestCompute, m_BuildInstanceDrawsKernel, occluderHandles);
                    }
                }
            }

            cmd.SetKeyword(m_Compute, m_UseOcclusionKeyword, hasOcclusionCulling);
        }
#endif

        static void OnDebugCounterReadbackComplete(AsyncGPUReadbackRequest request)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (request.hasError)
            {
                Debug.LogError("DebugCounterReadback: Readback error: " + request);
                return;
            }

            if (request.done)
            {
                NativeArray<int> counterData = request.GetData<int>();
                if (counterData.Length == (int)DebugCounterIndex.Count)
                {
                    ProfilingCounters.Visible.Value += counterData[(int)DebugCounterIndex.Visible];
                    ProfilingCounters.Culled.Value += counterData[(int)DebugCounterIndex.Culled];
                    ProfilingCounters.Occluded.Value += counterData[(int)DebugCounterIndex.Occluded];
                }
                else
                {
                    Debug.LogError("DebugCounterReadback: Invalid counter data length " + counterData.Length);
                }
            }
#endif
        }
    }
}
