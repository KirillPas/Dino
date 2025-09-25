// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using float4 = Unity.Mathematics.float4;

#if !UNITY_2022_3_OR_NEWER
using MA.Core;
#endif

namespace MA.Flora.Rendering
{
    struct GPUTransform
    {
        public static readonly GPUTransform Null = default;

        public float3 XAxis;
        public float3 YAxis;
        public float3 ZAxis;
        public float3 Position;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GPUTransform(in float3 xAxis, in float3 yAxis, in float3 zAxis, in float3 position)
        {
            XAxis = xAxis;
            YAxis = yAxis;
            ZAxis = zAxis;
            Position = position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GPUTransform From3x4(in float3x4 m)
            => new GPUTransform { XAxis = m.c0, YAxis = m.c1, ZAxis = m.c2, Position = m.c3 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GPUTransform FromMatrix(in float4x4 matrix)
            => new GPUTransform { XAxis = matrix.c0.xyz, YAxis = matrix.c1.xyz, ZAxis = matrix.c2.xyz, Position = matrix.c3.xyz };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GPUTransform FromLocalTransform(in LocalTransform transform)
            => FromMatrix(transform.ToMatrix());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GPUTransform TRS(in float3 position, in quaternion rotation, in float3 scale)
        {
            float3x3 r = new float3x3(rotation);
            return new GPUTransform
            {
                XAxis = r.c0 * scale.x,
                YAxis = r.c1 * scale.y,
                ZAxis = r.c2 * scale.z,
                Position = position
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GPUPackedTransform
    {
        public static readonly GPUPackedTransform Null = default;

        public float3 Position; // 12 bytes
        public half3 AxisX;     // 6 bytes
        public half3 AxisY;     // 6 bytes
        public half3 AxisZ;     // 6 bytes
        public ushort GroupID;  // 2 bytes

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GPUPackedTransform(int groupID, in float3 position, in quaternion rotation, in float3 scale)
        {
            GPUTransform transform = GPUTransform.TRS(position, rotation, scale);
            Position = position;
            AxisX = new half3(transform.XAxis);
            AxisY = new half3(transform.YAxis);
            AxisZ = new half3(transform.ZAxis);
            GroupID = (ushort)groupID;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GPUInstanceInfo
    {
        public uint Flags_RendererID;
        public uint RuntimeInstancedID;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct GPUInstancedRenderer
    {
        public static readonly int Float4Stride = sizeof(GPUInstancedRenderer) / sizeof(float4);

        public uint Flags_LODMask;
        public uint InstanceCount;
        public uint InstanceDataOffset;
        public uint Padding;
        public float4 PrototypeBoundingSphere;
        public float4 DynamicDensityParams;
        public float4 DynamicFadeParams;
    }

    [Flags]
    enum GPUInstanceFlags : uint
    {
        None   = 0,
        Hidden = 1 << 0,
    }

    sealed class InstancedSceneData : IDisposable
    {
        [Flags]
        enum UploadRequestFlags
        {
            None           = 0,
            Transform      = 1 << 0,
            SHCoefficients = 1 << 1,
            EditorData     = 1 << 2,
            CustomProperty = 1 << 3,
        }

        InstancingContext m_Context;
        BufferPool m_BufferPool;

        int[] m_RendererVersions;
        int[] m_TransformVersions;
        int[] m_OrderVersions;
        int[] m_EnabledVersions;
        int[] m_SHVersions;
        int[] m_PropertyCounts;
        UnsafeArray<int>[] m_PropertyVersions;
#if UNITY_EDITOR
        int[] m_SelectionIDVersions;
#endif
        UploadRequestFlags[] m_RequestFlags;
        bool[] m_Uploading;

        UnsafeArray<GPUInstancedRenderer> m_RendererData;
        UnsafeArray<float4> m_RendererDataSOA;
        GraphicsBuffer m_RendererDataBuffer;
        bool m_RendererDataDirty;
        UnsafeIndirectList<InstancedRendererID> m_Requests;
        UnsafeIndirectList<InstancedRendererID> m_Uploads;

        InstanceBuffer m_InstanceDataBuffer;
        BufferScatterUploader m_ElementUploader4;
        BufferScatterUploader m_ElementUploader16;
        JobHandle m_InstanceUploadHandle;

        SphericalHarmonicsL2 m_CachedAmbientProbe;
        ComputeBuffer m_InstanceGlobalValuesBuffer;
        UnsafeArray<InstancingGlobalShaderVariables> m_InstanceGlobalValues;

        public BufferPool BufferPool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_BufferPool;
        }

        public GraphicsBuffer RendererDataBuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_RendererDataBuffer;
        }

        public int RendererDataStrideSOA
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_Context.RendererManager.Capacity;
        }

        public GraphicsBuffer InstanceDataBuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_InstanceDataBuffer;
        }

        const int k_DefaultClusterCapacity  = 16;
        const int k_DefaultInstanceCapacity = 1024;

        public static class ProfilingCounters
        {
            public static readonly ProfilerCounterValue<int> InstanceBuffer = new ProfilerCounterValue<int>("Flora.InstanceBuffer", ProfilerMarkerDataUnit.Bytes);
            public static readonly ProfilerCounterValue<int> UploadBufferPool = new ProfilerCounterValue<int>("Flora.UploadBufferPool", ProfilerMarkerDataUnit.Bytes);
        }

        public InstancedSceneData(InstancingContext context)
        {
            m_Context = context;
            m_BufferPool = new BufferPool(16);

            m_RendererVersions = new int[k_DefaultClusterCapacity];
            m_TransformVersions = new int[k_DefaultClusterCapacity];
            m_OrderVersions = new int[k_DefaultClusterCapacity];
            m_EnabledVersions = new int[k_DefaultClusterCapacity];
            m_PropertyVersions = new UnsafeArray<int>[k_DefaultClusterCapacity];
            m_PropertyCounts = new int[k_DefaultClusterCapacity];
            m_SHVersions = new int[k_DefaultClusterCapacity];
            m_RequestFlags = new UploadRequestFlags[k_DefaultClusterCapacity];
            m_Uploading = new bool[k_DefaultClusterCapacity];

            m_RendererData = new UnsafeArray<GPUInstancedRenderer>(k_DefaultClusterCapacity, Allocator.Persistent);
            m_RendererDataSOA = new UnsafeArray<float4>(k_DefaultClusterCapacity * GPUInstancedRenderer.Float4Stride, Allocator.Persistent);
            m_Requests = new UnsafeIndirectList<InstancedRendererID>(k_DefaultClusterCapacity, Allocator.Persistent);
            m_Uploads = new UnsafeIndirectList<InstancedRendererID>(k_DefaultClusterCapacity, Allocator.Persistent);

            m_InstanceDataBuffer = new InstanceBuffer();
            m_ElementUploader4 = new BufferScatterUploader(4, m_BufferPool, "Elements4");
            m_ElementUploader16 = new BufferScatterUploader(16, m_BufferPool, "Elements16");

            m_InstanceGlobalValues = new UnsafeArray<InstancingGlobalShaderVariables>(1, Allocator.Persistent);
            m_InstanceGlobalValuesBuffer = new ComputeBuffer(1, UnsafeUtility.SizeOf<InstancingGlobalShaderVariables>(), ComputeBufferType.Constant);

#if UNITY_EDITOR
            m_SelectionIDVersions = new int[k_DefaultClusterCapacity];
#endif

            UpdateAmbientProbe(true);
        }

        public void Dispose()
        {
            for (int i = 0; i < m_PropertyVersions.Length; i++)
                m_PropertyVersions[i].Dispose();

            m_BufferPool.Dispose();

            m_RendererData.Dispose();
            m_RendererDataSOA.Dispose();
            m_RendererDataBuffer?.Dispose();
            m_Requests.Dispose();
            m_Uploads.Dispose();

            m_InstanceDataBuffer.Dispose();
            m_ElementUploader4.Dispose();
            m_ElementUploader16.Dispose();

            m_InstanceGlobalValues.Dispose();
            m_InstanceGlobalValuesBuffer.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearLightProbes()
        {
            for (int i = 0; i < m_SHVersions.Length; i++)
                m_SHVersions[i] = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void NextRenderFrame()
        {
            EndUploads();
            UpdateAmbientProbe();
            SetGlobalBuffers();
        }

        public void BeginUploads()
        {
            EndUploads();
            ResizeBuffersIfNeeded();
            m_BufferPool.NextFrame();

            int total4ByteUploadElements = 0;
            int total16ByteUploadElements = 0;

            for (int i = 0; i < m_Requests.Length; i++)
            {
                InstancedRendererID id = m_Requests[i];
                IInstancedRenderer renderer = m_Context.RendererManager.Renderers[id];
                if (renderer != null)
                {
                    ref readonly InstanceRendererData rendererData = ref m_Context.RendererManager.Data.Culling[id];
                    int instanceCountToRender = rendererData.InstanceCount;

                    if ((m_RequestFlags[id] & UploadRequestFlags.Transform) != 0)
                        total16ByteUploadElements += instanceCountToRender * InstanceBufferConfig.TransformElements;

                    if ((m_RequestFlags[id] & UploadRequestFlags.SHCoefficients) != 0)
                        total16ByteUploadElements += instanceCountToRender * InstanceBufferConfig.SHCoefficientsElements;
#if UNITY_EDITOR
                    if ((m_RequestFlags[id] & UploadRequestFlags.EditorData) != 0)
                        total4ByteUploadElements += instanceCountToRender * InstanceBufferConfig.EditorDataElements;
#endif

                    if (renderer.InstancePropertyArrays != null && (m_RequestFlags[id] & UploadRequestFlags.CustomProperty) != 0)
                    {
                        for (int j = 0; j < renderer.InstancePropertyArrays.DataArrays.Length; j++)
                        {
                            UnsafeUntypedList propertyArray = renderer.InstancePropertyArrays.DataArrays[j];
                            if (propertyArray.IsCreated)
                            {
                                bool is16ByteAligned = propertyArray.ElementSize % 16 == 0;
                                if (is16ByteAligned)
                                {
                                    int elementCount = propertyArray.ElementSize / 16;
                                    total16ByteUploadElements += instanceCountToRender * elementCount;
                                }
                                else
                                {
                                    int elementCount = propertyArray.ElementSize / 4;
                                    total4ByteUploadElements += instanceCountToRender * elementCount;
                                }
                            }
                        }
                    }
                }
            }

            ThreadedScatterUploader scatterUploader4Aligned = default;
            if (total4ByteUploadElements > 0)
            {
                m_ElementUploader4.MapUploader(total4ByteUploadElements, out scatterUploader4Aligned);
            }

            ThreadedScatterUploader scatterUploader16Aligned = default;
            if (total16ByteUploadElements > 0)
            {
                m_ElementUploader16.MapUploader(total16ByteUploadElements, out scatterUploader16Aligned);
            }


            JobHandle rendererUpdatesHandle = m_Context.RendererManager.UpdateJobHandle;

            for (int i = 0; i < m_Requests.Length; i++)
            {
                InstancedRendererID id = m_Requests[i];
                IInstancedRenderer renderer = m_Context.RendererManager.Renderers[id];
                if (renderer != null)
                {
                    if (!m_Uploading[id])
                    {
                        m_Uploading[id] = true;
                        m_Uploads.Add(id);
                    }

                    ref readonly InstanceRendererData rendererData = ref m_Context.RendererManager.Data.Culling[id];
                    int instanceCountToUpload = rendererData.InstanceCount;

                    bool hasRenderIndices = renderer.CullingData.RenderIndexLookup.Count > 0;
                    NativeArray<PackedRenderIndex> renderIndices = hasRenderIndices
                        ? renderer.CullingData.RenderIndexLookup.ToNativeArray(Allocator.TempJob).Reinterpret<PackedRenderIndex>()
                        : new NativeArray<PackedRenderIndex>(0, Allocator.TempJob);

                    bool requiresTransforms = (m_RequestFlags[id] & UploadRequestFlags.Transform) != 0 || (m_RequestFlags[id] & UploadRequestFlags.SHCoefficients) != 0;
                    NativeArray<LocalTransform> localTransforms = default;
                    if (requiresTransforms)
                        localTransforms = renderer.InstanceTransforms.ToNativeArray(Allocator.TempJob);

                    LocalTransform localToWorld = m_Context.RendererManager.Data.LocalToWorld[id];
                    BuiltinBatchOffsets batchOffsets = m_InstanceDataBuffer.GetBuiltinBatchOffsets(rendererData.BatchID);
                    BufferAllocation instanceDataAllocation = m_Context.RendererManager.Data.BatchAllocation[id];

                    if ((m_RequestFlags[id] & UploadRequestFlags.Transform) != 0)
                    {
                        Assert.IsTrue(batchOffsets.TransformsOffset > 0);

                        bool hasEnabled = false;
                        UnsafeBitList enabledInstances = default;
                        if (renderer.InstancesEnabled.IsCreated)
                        {
                            enabledInstances = new UnsafeBitList(renderer.InstancesEnabled, AllocatorManager.TempJob);
                            hasEnabled = true;
                        }

                        int bufferStart = batchOffsets.TransformsOffset + instanceDataAllocation.Offset * InstanceBufferConfig.TransformStride;
                        int bufferOffsetInElements = bufferStart / InstanceBufferConfig.TransformElementStride;
                        ScatterTransformsJob scatterTransformsJob = new ScatterTransformsJob
                        {
                            BufferOffset = bufferOffsetInElements,
                            LocalToWorld = localToWorld,
                            Enabled = enabledInstances,
                            Transforms = localTransforms.AsReadOnly(),
                            RenderIndices = renderIndices.AsReadOnly(),
                            ScatterUploader = scatterUploader16Aligned,
                            RendererID = id,
                        };

                        JobHandle instanceUploadHandle = scatterTransformsJob.ScheduleBatchByRef(instanceCountToUpload, ScatterTransformsJob.BatchSize, rendererUpdatesHandle);
                        if (hasEnabled)
                            instanceUploadHandle = enabledInstances.Dispose(instanceUploadHandle);

                        m_InstanceUploadHandle = JobHandle.CombineDependencies(m_InstanceUploadHandle, instanceUploadHandle);
                    }

                    if ((m_RequestFlags[id] & UploadRequestFlags.SHCoefficients) != 0)
                    {
#if UNITY_2022_2_OR_NEWER
                        Assert.IsTrue(batchOffsets.SHCoefficientsOffset > 0);
                        int bufferStart = batchOffsets.TransformsOffset + instanceDataAllocation.Offset * InstanceBufferConfig.SHCoefficientsStride;
                        int bufferOffsetInElements = bufferStart / InstanceBufferConfig.SHCoefficientsStride;
                        LightProbesQuery lightProbesQuery = new LightProbesQuery(Allocator.TempJob);
                        ScatterLightProbesJob scatterLightProbesJob = new ScatterLightProbesJob
                        {
                            BufferOffset = bufferOffsetInElements,
                            LocalToWorld = localToWorld,
                            Transforms = localTransforms.AsReadOnly(),
                            RenderIndices = renderIndices.AsReadOnly(),
                            Query = lightProbesQuery,
                            ScatterUploader = scatterUploader16Aligned
                        };

                        JobHandle lightProbeUploadHandle = scatterLightProbesJob.ScheduleBatchByRef(instanceCountToUpload, ScatterLightProbesJob.BatchSize, rendererUpdatesHandle);
                        lightProbeUploadHandle = lightProbesQuery.Dispose(lightProbeUploadHandle);
                        m_InstanceUploadHandle = JobHandle.CombineDependencies(m_InstanceUploadHandle, lightProbeUploadHandle);
#endif
                    }

                    if ((m_RequestFlags[id] & UploadRequestFlags.CustomProperty) != 0)
                    {
                        ReadOnlySpan<UnsafeUntypedList> propertyArrays = renderer.InstancePropertyArrays.DataArrays;
                        UnsafeArray<InstancedPropertyMetadata> propertyMetadatas = m_InstanceDataBuffer.GetPropertyMetadataArray(rendererData.BatchID);

                        JobHandle propertiesUploadHandle = default;
                        for (int j = 0; j < propertyMetadatas.Length; j++)
                        {
                            InstancedPropertyMetadata propertyMetadata = propertyMetadatas[j];
                            if (!propertyMetadata.IsCreated)
                                continue;

                            UnsafeUntypedList propertyArray = propertyArrays[j];
                            if (!propertyArray.IsCreated)
                                continue;

                            UnsafeUntypedList propertyArrayCopy = new UnsafeUntypedList(propertyArray, AllocatorManager.TempJob);

                            bool is16ByteAligned = propertyArrayCopy.ElementSize % 16 == 0;
                            int bytesPerElement = is16ByteAligned ? 16 : 4;
                            int elementsPerInstance = propertyArrayCopy.ElementSize / bytesPerElement;
                            int bufferStart = propertyMetadata.Offset + instanceDataAllocation.Offset * bytesPerElement;
                            int bufferOffsetInElements = bufferStart / bytesPerElement;
                            ScatterInstancedPropertyJob scatterInstancedPropertyJob = new ScatterInstancedPropertyJob
                            {
                                BufferOffset = bufferOffsetInElements,
                                ElementsPerInstance = elementsPerInstance,
                                BytesPerElement = bytesPerElement,
                                PropertyArray = propertyArrayCopy.AsReadOnly(),
                                RenderIndices = renderIndices.AsReadOnly(),
                                ScatterUploader = is16ByteAligned ? scatterUploader16Aligned : scatterUploader4Aligned,
                            };

                            JobHandle propertyUploadHandle = scatterInstancedPropertyJob.ScheduleBatchByRef(instanceCountToUpload, ScatterInstancedPropertyJob.BatchSize, rendererUpdatesHandle);
                            propertyUploadHandle = propertyArrayCopy.Dispose(propertyUploadHandle);
                            propertiesUploadHandle = JobHandle.CombineDependencies(propertiesUploadHandle, propertyUploadHandle);
                        }

                        m_InstanceUploadHandle = JobHandle.CombineDependencies(m_InstanceUploadHandle, propertiesUploadHandle);
                    }

#if UNITY_EDITOR
                    if ((m_RequestFlags[id] & UploadRequestFlags.EditorData) != 0)
                    {
                        Assert.IsTrue(batchOffsets.EditorDataOffset > 0);
                        int bufferStart = batchOffsets.EditorDataOffset + instanceDataAllocation.Offset * InstanceBufferConfig.EditorDataStride;
                        int bufferOffsetInElements = bufferStart / InstanceBufferConfig.EditorDataElementStride;
                        IInstancedRendererEditorData editorData = m_Context.RendererManager.EditorRenderers[id];
                        NativeArray<InstancedGlobalID> instanceIDs = renderer.GlobalIDs.ToNativeArray(Allocator.TempJob);

                        ScatterEditorDataJob scatterEditorDataJob = new ScatterEditorDataJob
                        {
                            BufferOffset = bufferOffsetInElements,
                            AllSelected = rendererData.AllSelected,
                            RenderIndices = renderIndices.AsReadOnly(),
                            InstancedIDs = instanceIDs.AsReadOnly(),
                            Selected = editorData.InstanceSelection,
                            ScatterUploader = scatterUploader4Aligned,
                        };

                        JobHandle selectionUploadHandle = scatterEditorDataJob.ScheduleBatchByRef(instanceCountToUpload, ScatterEditorDataJob.BatchSize, rendererUpdatesHandle);
                        selectionUploadHandle = instanceIDs.Dispose(selectionUploadHandle);
                        m_InstanceUploadHandle = JobHandle.CombineDependencies(m_InstanceUploadHandle, selectionUploadHandle);
                    }
#endif

                    if (localTransforms.IsCreated)
                        m_InstanceUploadHandle = localTransforms.Dispose(m_InstanceUploadHandle);

                    m_InstanceUploadHandle = renderIndices.Dispose(m_InstanceUploadHandle);

                    JobHandle.ScheduleBatchedJobs();
                }

                m_RequestFlags[id] = UploadRequestFlags.None;
            }

            m_Requests.Clear();

#if ENABLE_PROFILER
            ProfilingCounters.InstanceBuffer.Value = m_InstanceDataBuffer.AllocatedSizeInBytes;
            ProfilingCounters.UploadBufferPool.Value = m_BufferPool.AllocatedSizeInBytes;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetGlobalBuffers()
        {
            Shader.SetGlobalBuffer(InstancingShaderID.flora_RendererData, m_RendererDataBuffer);
            Shader.SetGlobalBuffer(InstancingShaderID.flora_InstanceData, m_InstanceDataBuffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetGlobalBuffers(CommandBuffer cmd)
        {
            cmd.SetGlobalBuffer(InstancingShaderID.flora_RendererData, m_RendererDataBuffer);
            cmd.SetGlobalBuffer(InstancingShaderID.flora_InstanceData, m_InstanceDataBuffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetComputeBuffers(CommandBuffer cmd, ComputeShader cs, int kernel)
        {
            cmd.SetComputeBufferParam(cs, kernel, InstancingShaderID.flora_RendererData, m_RendererDataBuffer);
            cmd.SetComputeBufferParam(cs, kernel, InstancingShaderID.flora_InstanceData, m_InstanceDataBuffer);
        }

#if UNITY_2023_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetComputeBuffers(ComputeCommandBuffer cmd, ComputeShader cs, int kernel)
        {
            cmd.SetComputeBufferParam(cs, kernel, InstancingShaderID.flora_RendererData, m_RendererDataBuffer);
            cmd.SetComputeBufferParam(cs, kernel, InstancingShaderID.flora_InstanceData, m_InstanceDataBuffer);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBuiltinPropertyMetadata(InstancedBatchID batchID)
            => m_InstanceDataBuffer.SetGlobalMetadata(batchID);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBuiltinPropertyMetadata(InstancedBatchID batchID, CommandBuffer cmd)
            => m_InstanceDataBuffer.SetGlobalMetadata(batchID, cmd);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBuiltinPropertyMetadata(InstancedBatchID batchID, Material mat)
            => m_InstanceDataBuffer.SetMaterialMetadata(batchID, mat);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBuiltinPropertyMetadata(InstancedBatchID batchID, MaterialPropertyBlock mpb)
            => m_InstanceDataBuffer.SetMaterialMetadata(batchID, mpb);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBuiltinPropertyMetadata(InstancedBatchID batchID, CommandBuffer cmd, ComputeShader cs)
            => m_InstanceDataBuffer.SetComputeMetadata(batchID, cmd, cs);

#if UNITY_2023_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBuiltinPropertyMetadata(InstancedBatchID batchID, ComputeCommandBuffer cmd, ComputeShader cs)
            => m_InstanceDataBuffer.SetComputeMetadata(batchID, cmd, cs);
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void UpdateAmbientProbe(bool forceUpdate = false)
        {
            fixed (SphericalHarmonicsL2* cachedProbe = &m_CachedAmbientProbe)
            {
                if (UpdateAmbientProbe(forceUpdate, cachedProbe, m_InstanceGlobalValues.Ptr))
                {
                    m_InstanceGlobalValuesBuffer.SetDataUnsafe(m_InstanceGlobalValues.Ptr, 1);
                    ClearLightProbes();
                }

                Shader.SetGlobalConstantBuffer(InstancingShaderID.flora_InstanceGlobalValues, m_InstanceGlobalValuesBuffer, 0, UnsafeUtility.SizeOf<InstancingGlobalShaderVariables>());
            }
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe bool UpdateAmbientProbe(bool force, SphericalHarmonicsL2* cachedProbe, InstancingGlobalShaderVariables* globals)
        {
            SphericalHarmonicsL2 ambientProbe = RenderSettings.ambientProbe;
            if (force || UnsafeUtility.MemCmp(cachedProbe, &ambientProbe, sizeof(SphericalHarmonicsL2)) != 0)
            {
                *cachedProbe = ambientProbe;
                globals[0].UpdateAmbientSH(ambientProbe);
                return true;
            }

            return false;
        }

        public void EndUploads()
        {
            if (m_Uploads.Length == 0)
                return;

            m_InstanceUploadHandle.Complete();
            m_InstanceUploadHandle = default;

            m_ElementUploader4.DispatchScatter(m_InstanceDataBuffer);
            m_ElementUploader16.DispatchScatter(m_InstanceDataBuffer);

            for (int i = 0; i < m_Uploads.Length; i++)
            {
                InstancedRendererID id = m_Uploads[i];
                m_Context.RendererManager.SetLoaded(id, true);
                m_RendererVersions[id] = m_Context.RendererManager.Data.Culling[id].Version;
                m_Uploading[id] = false;
            }

            m_Uploads.Clear();
        }

        unsafe void ResizeBuffersIfNeeded()
        {
            int rendererCount = m_Context.RendererManager.Capacity;
            int rendererBytes = rendererCount * GPUInstancedRenderer.Float4Stride * UnsafeUtility.SizeOf<float4>();
            BufferUtility.ResizeSOAIfNeeded(ref m_RendererDataBuffer, 16, rendererBytes, GPUInstancedRenderer.Float4Stride, GraphicsBuffer.Target.Structured, "RendererData");

            if (m_RendererDataDirty)
            {
                m_RendererDataBuffer.SetDataUnsafe(m_RendererDataSOA.Ptr, rendererCount * GPUInstancedRenderer.Float4Stride);
                m_RendererDataDirty = false;
                m_InstanceDataBuffer.UpdateGroupDataStrideSOA(rendererCount);
            }

            m_InstanceDataBuffer.RebuildLayoutIfNeeded();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasBatch(InstancedBatchID batchID)
            => m_InstanceDataBuffer.HasBatch(batchID);

        public void RegisterBatch(InstancedBatchID batchID, in InstancedBatchDescriptor descriptor)
            => m_InstanceDataBuffer.RegisterBatch(batchID, descriptor);

        public void UnregisterBatch(InstancedBatchID batchID)
            => m_InstanceDataBuffer.UnregisterBatch(batchID);

        public void RequestUpload(InstancedRendererID id)
        {
            if (!id.IsCreated)
                return;

            IInstancedRenderer renderer = m_Context.RendererManager.Renderers[id];
            if (renderer == null)
                return;

            int rendererCapacity = m_Context.RendererManager.Data.Capacity;
            if (rendererCapacity > m_RendererVersions.Length)
            {
                Array.Resize(ref m_RendererVersions, rendererCapacity);
                Array.Resize(ref m_OrderVersions, rendererCapacity);
                Array.Resize(ref m_TransformVersions, rendererCapacity);
                Array.Resize(ref m_EnabledVersions, rendererCapacity);
                Array.Resize(ref m_PropertyVersions, rendererCapacity);
                Array.Resize(ref m_PropertyCounts, rendererCapacity);
                Array.Resize(ref m_SHVersions, rendererCapacity);
                Array.Resize(ref m_RequestFlags, rendererCapacity);
                Array.Resize(ref m_Uploading, rendererCapacity);
#if UNITY_EDITOR
                Array.Resize(ref m_SelectionIDVersions, rendererCapacity);
#endif
            }

            if (!m_Context.RendererManager.Data.IsValid[id] || !m_Context.RendererManager.Data.InRange[id])
                return;

            ref InstanceRendererData culling = ref m_Context.RendererManager.Data.Culling[id];
            if (!m_InstanceDataBuffer.HasBatch(culling.BatchID))
                return;

            UploadRequestFlags uploadFlags = UploadRequestFlags.None;

            ref BufferAllocation bufferAllocation = ref m_Context.RendererManager.Data.BatchAllocation[id];
            int instanceCapacity = MathUtility.NextMultipleOf(culling.InstanceCountToRender, JobsUtility.CacheLineSize);

            bool anyChanges = false;
            bool forceUpload = m_OrderVersions[id] != renderer.InstanceOrderVersion;

            if (forceUpload || !bufferAllocation.IsValid || renderer.InstanceTransformsVersion != m_TransformVersions[id])
            {
                InstancedBatchID batchID = m_Context.RendererManager.Data.Culling[id].BatchID;

                if (!bufferAllocation.IsValid || instanceCapacity > bufferAllocation.Length || instanceCapacity < bufferAllocation.Length / 2)
                {
                    if (bufferAllocation.IsValid)
                        m_InstanceDataBuffer.FreeInstances(batchID, bufferAllocation);

                    bufferAllocation = BufferAllocation.Null;
                    if (instanceCapacity > 0)
                        bufferAllocation = m_InstanceDataBuffer.AllocateInstances(batchID, instanceCapacity);

                    forceUpload = true;
                }

                m_TransformVersions[id] = renderer.InstanceTransformsVersion;
                m_OrderVersions[id] = renderer.InstanceOrderVersion;
                uploadFlags |= UploadRequestFlags.Transform;
            }

            if (renderer.InstancesEnabled.IsCreated)
            {
                bool enabledChanged = renderer.InstancesEnabledVersion != m_EnabledVersions[id];
                if (enabledChanged)
                {
                    m_EnabledVersions[id] = renderer.InstancesEnabledVersion;
                    uploadFlags |= UploadRequestFlags.Transform;
                }
            }

            if (renderer.InstancePropertyArrays != null)
            {
                int oldArrayCount = m_PropertyCounts[id];
                int newArrayCount = renderer.InstancePropertyArrays.GetActiveArrayCount();
                bool arrayCountChanged = oldArrayCount != newArrayCount;
                UnsafeArray<int> propertyDataVersions = m_PropertyVersions[id];

                bool versionChanged = false;
                if (propertyDataVersions.IsCreated && !arrayCountChanged)
                {
                    for (int arrayIndex = 0; arrayIndex < newArrayCount; arrayIndex++)
                    {
                        if (renderer.InstancePropertyArrays.Versions[arrayIndex] != m_PropertyVersions[id][arrayIndex])
                        {
                            versionChanged = true;
                            break;
                        }
                    }
                }

                if (forceUpload || arrayCountChanged || versionChanged)
                {
                    if (!propertyDataVersions.IsCreated || propertyDataVersions.Length != newArrayCount)
                    {
                        if (propertyDataVersions.IsCreated)
                            propertyDataVersions.Dispose();

                        if (newArrayCount > 0)
                            propertyDataVersions = new UnsafeArray<int>(newArrayCount, Allocator.Persistent);
                    }

                    if (propertyDataVersions.IsCreated)
                    {
                        for (int arrayIndex = 0; arrayIndex < newArrayCount; arrayIndex++)
                            propertyDataVersions[arrayIndex] = renderer.InstancePropertyArrays.Versions[arrayIndex];

                        uploadFlags |= UploadRequestFlags.CustomProperty;
                        anyChanges = true;
                    }

                    m_PropertyCounts[id] = newArrayCount;
                    m_PropertyVersions[id] = propertyDataVersions;
                }
            }

            bool wantsLightProbes = m_Context.RendererManager.Data.LightProbe[id].SampleLightProbes && LightmapSettings.lightProbes?.count > 0;
            if (wantsLightProbes && (forceUpload || renderer.InstanceTransformsVersion != m_SHVersions[id]))
            {
                m_SHVersions[id] = renderer.InstanceTransformsVersion;
                uploadFlags |= UploadRequestFlags.SHCoefficients;
                anyChanges = true;
            }

#if UNITY_EDITOR
            IInstancedRendererEditorData rendererEditorData = m_Context.RendererManager.EditorRenderers[id];
            if (rendererEditorData != null && (forceUpload || rendererEditorData.InstanceSelectionVersion != m_SelectionIDVersions[id]))
            {
                m_SelectionIDVersions[id] = rendererEditorData.InstanceSelectionVersion;
                uploadFlags |= UploadRequestFlags.EditorData;
                anyChanges = true;
            }
#endif

            if (bufferAllocation.IsValid) // Never upload if the allocation is invalid
            {
                if (anyChanges || m_RendererVersions[id] != culling.Version)
                {
                    if (m_RequestFlags[id] == UploadRequestFlags.None)
                        m_Requests.Add(id);

                    m_RequestFlags[id] |= uploadFlags;
                    UpdateGPURendererData(id);
                }
            }
        }

        public void Unload(InstancedRendererID id)
        {
            if (id >= m_RendererVersions.Length)
                return;

            ref BufferAllocation instanceAllocation = ref m_Context.RendererManager.Data.BatchAllocation[id];
            if (instanceAllocation.IsValid)
                m_InstanceDataBuffer.FreeInstances(m_Context.RendererManager.Data.Culling[id].BatchID, instanceAllocation);

            instanceAllocation = default;
            m_RendererVersions[id] = 0;
            m_TransformVersions[id] = 0;
            m_OrderVersions[id] = 0;
            m_EnabledVersions[id] = 0;
            m_SHVersions[id] = 0;
            m_PropertyCounts[id] = 0;
            m_PropertyVersions[id].Dispose();
#if UNITY_EDITOR
            m_SelectionIDVersions[id] = 0;
#endif
            m_RequestFlags[id] = UploadRequestFlags.None;
            m_Context.RendererManager.SetLoaded(id, false);
        }

        unsafe void UpdateGPURendererData(InstancedRendererID id)
        {
            GPUInstancedRenderer gpuInstancedRenderer = default;
            InstancedPrototypeID prototypeID = m_Context.RendererManager.Data.Culling[id].PrototypeID;
            if (!m_Context.PrototypeManager.Data.Exists(prototypeID))
                return;

            ref readonly InstanceRendererData renderer = ref m_Context.RendererManager.Data.Culling[id];
            ref readonly InstancedPrototypeCullingData prototypeCulling = ref m_Context.PrototypeManager.Data.Culling[prototypeID];
            ref readonly InstancedPrototypeLODData prototypeLODs = ref m_Context.PrototypeManager.Data.LOD[prototypeID];

            uint lodMask = 0;
            for (int i = 0; i < prototypeLODs.LODCount; i++)
                lodMask |= 1u << i;

            gpuInstancedRenderer.InstanceCount = (uint)renderer.InstanceCountToRender;
            gpuInstancedRenderer.Flags_LODMask = (uint)prototypeCulling.CullingFlags << 8 | lodMask;

            BufferAllocation instanceDataAllocation = m_Context.RendererManager.Data.BatchAllocation[id];
            gpuInstancedRenderer.InstanceDataOffset = (uint)instanceDataAllocation.Offset;

            AxisAlignedBox prototypeBounds = m_Context.PrototypeManager.Data.Bounds[prototypeID];
            gpuInstancedRenderer.PrototypeBoundingSphere = new float4(prototypeBounds.Center, prototypeBounds.Radius);

            InstancedPrototypeDensityData densityData = m_Context.PrototypeManager.Data.Density[prototypeID];
            if (densityData.Enabled)
            {
                gpuInstancedRenderer.DynamicDensityParams.x = densityData.Density;
                gpuInstancedRenderer.DynamicDensityParams.y = densityData.Range.Min;
                gpuInstancedRenderer.DynamicDensityParams.z = densityData.Range.Max;
                gpuInstancedRenderer.DynamicDensityParams.w = densityData.Falloff;
            }
            else
            {
                gpuInstancedRenderer.DynamicDensityParams = new float4(1.0f, 0.0f, 0.0f, 1.0f);
            }

            float renderDistance = prototypeCulling.CullingDistance;
            gpuInstancedRenderer.DynamicFadeParams.x = 0;
            gpuInstancedRenderer.DynamicFadeParams.y = renderDistance;
            gpuInstancedRenderer.DynamicFadeParams.z = 1.0f / renderDistance;
            gpuInstancedRenderer.DynamicFadeParams.w = 0;

            int soaStride = m_Context.RendererManager.Capacity;
            bool rebuildAllRenderers = false;
            if (m_RendererData.Length != soaStride)
            {
                m_RendererData.Resize(soaStride, AllocatorManager.Persistent);
                m_RendererDataSOA.Resize(soaStride * GPUInstancedRenderer.Float4Stride, AllocatorManager.Persistent);
                rebuildAllRenderers = true;
            }

            m_RendererData[id] = gpuInstancedRenderer;

            if (rebuildAllRenderers)
            {
                for (int index = 0; index < soaStride; index++)
                {
                    GPUInstancedRenderer currentRenderer = m_RendererData[index];
                    for (int elementIndex = 0; elementIndex < GPUInstancedRenderer.Float4Stride; elementIndex++)
                    {
                        float4 element = ((float4*)&currentRenderer)[elementIndex];
                        m_RendererDataSOA[elementIndex * soaStride + index] = element;
                    }
                }
            }
            else
            {
                for (int elementIndex = 0; elementIndex < GPUInstancedRenderer.Float4Stride; elementIndex++)
                {
                    float4 element = ((float4*)&gpuInstancedRenderer)[elementIndex];
                    m_RendererDataSOA[elementIndex * soaStride + id] = element;
                }
            }

            m_RendererDataDirty = true;
        }

        [BurstCompile]
        unsafe struct ScatterTransformsJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public int BufferOffset;
            public int RendererID;
            public LocalTransform LocalToWorld;
            public UnsafeBitList Enabled;
            [ReadOnly] public NativeArray<LocalTransform>.ReadOnly Transforms;
            [ReadOnly] public NativeArray<PackedRenderIndex>.ReadOnly RenderIndices;
            [WriteOnly] public ThreadedScatterUploader ScatterUploader;

            public void Execute(int startInstanceIndex, int instanceCount)
            {
                int totalElementsToUpload = instanceCount * InstanceBufferConfig.TransformElements;
                ScatterUploader.BeginScatter(totalElementsToUpload, out byte* uploadPtr, out uint* scatterPtr);

                UnsafeArray<uint> scatterIndices = new UnsafeArray<uint>((uint*)scatterPtr, totalElementsToUpload);
                int scatterBaseIndex = 0;
                bool hasEnabled = Enabled.Length > 0;

                for (int i = 0; i < instanceCount; ++i)
                {
                    int instanceIndex = startInstanceIndex + i;

                    PackedRenderIndex packedIndex = RenderIndices.IsValidIndex(instanceIndex) ? RenderIndices[instanceIndex] : instanceIndex;
                    int renderIndex = packedIndex.Index;
                    if (renderIndex >= 0)
                    {
                        int renderOffset = BufferOffset + (int)renderIndex * InstanceBufferConfig.TransformElements;

                        if (!packedIndex.IsDestroyed && Transforms.IsValidIndex(instanceIndex) && (!hasEnabled || Enabled[instanceIndex]))
                        {
                            LocalTransform worldTransform = LocalToWorld.Transform(Transforms[instanceIndex]);
                            *((GPUPackedTransform*)uploadPtr) = new GPUPackedTransform((ushort)RendererID, worldTransform.Position, worldTransform.Rotation, worldTransform.Scale);
                        }
                        else
                        {
                            *((GPUPackedTransform*)uploadPtr) = GPUPackedTransform.Null;
                        }

                        scatterIndices[scatterBaseIndex + 0] = (uint)(renderOffset + 0);
                        scatterIndices[scatterBaseIndex + 1] = (uint)(renderOffset + 1);

                        uploadPtr += InstanceBufferConfig.TransformStride;
                        scatterBaseIndex += InstanceBufferConfig.TransformElements;
                    }
                }
            }
        }

#if UNITY_2022_2_OR_NEWER
        [BurstCompile]
        unsafe struct ScatterLightProbesJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public int BufferOffset;
            public LocalTransform LocalToWorld;
            [ReadOnly] public NativeArray<LocalTransform>.ReadOnly Transforms;
            [ReadOnly] public NativeArray<PackedRenderIndex>.ReadOnly RenderIndices;
            [ReadOnly] public LightProbesQuery Query;
            [WriteOnly] public ThreadedScatterUploader ScatterUploader;

            public void Execute(int startInstanceIndex, int instanceCount)
            {
                NativeArray<Vector3> positions = new NativeArray<Vector3>(instanceCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < instanceCount; ++i)
                    positions[i] = LocalToWorld.TransformPoint(Transforms[startInstanceIndex + i].Position);

                NativeArray<int> tetrahedronIndices = new NativeArray<int>(instanceCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<SphericalHarmonicsL2> lightProbes = new NativeArray<SphericalHarmonicsL2>(instanceCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<Vector4> occlusionProbes = new NativeArray<Vector4>(instanceCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                Query.CalculateInterpolatedLightAndOcclusionProbes(positions, tetrahedronIndices, lightProbes, occlusionProbes);

                int totalElementsToUpload = instanceCount * InstanceBufferConfig.SHCoefficientsElements;
                ScatterUploader.BeginScatter(totalElementsToUpload, out byte* uploadPtr, out uint* scatterPtr);

                UnsafeArray<uint> scatterIndices = new UnsafeArray<uint>((uint*)scatterPtr, totalElementsToUpload);
                int scatterBaseIndex = 0;

                for (int i = 0; i < instanceCount; ++i)
                {
                    int instanceIndex = startInstanceIndex + i;

                    PackedRenderIndex packedIndex = RenderIndices.IsValidIndex(instanceIndex) ? RenderIndices[instanceIndex] : instanceIndex;
                    int renderIndex = packedIndex.Index;
                    if (renderIndex >= 0)
                    {
                        int renderOffset = BufferOffset + renderIndex * InstanceBufferConfig.SHCoefficientsElements;

                        if (!packedIndex.IsDestroyed && Transforms.IsValidIndex(instanceIndex))
                            *((SHCoefficients*)uploadPtr) = new SHCoefficients(lightProbes[i], occlusionProbes[i]);
                        else
                            *((SHCoefficients*)uploadPtr) = default;

                        scatterIndices[scatterBaseIndex + 0] = (uint)(renderOffset + 0);
                        scatterIndices[scatterBaseIndex + 1] = (uint)(renderOffset + 1);
                        scatterIndices[scatterBaseIndex + 2] = (uint)(renderOffset + 2);
                        scatterIndices[scatterBaseIndex + 3] = (uint)(renderOffset + 3);
                        scatterIndices[scatterBaseIndex + 4] = (uint)(renderOffset + 4);
                        scatterIndices[scatterBaseIndex + 5] = (uint)(renderOffset + 5);
                        scatterIndices[scatterBaseIndex + 6] = (uint)(renderOffset + 6);
                        scatterIndices[scatterBaseIndex + 7] = (uint)(renderOffset + 7);

                        uploadPtr += InstanceBufferConfig.SHCoefficientsStride;
                        scatterBaseIndex += InstanceBufferConfig.SHCoefficientsElements;
                    }
                }
            }
        }
#endif

        [BurstCompile]
        unsafe struct ScatterInstancedPropertyJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public int BufferOffset;
            public int ElementsPerInstance;
            public int BytesPerElement;

            [ReadOnly] public UnsafeUntypedList.ReadOnly PropertyArray;
            [ReadOnly] public NativeArray<PackedRenderIndex>.ReadOnly RenderIndices;
            [WriteOnly] public ThreadedScatterUploader ScatterUploader;

            public void Execute(int startInstanceIndex, int instanceCount)
            {
                int totalElementsToUpload = instanceCount * ElementsPerInstance;
                ScatterUploader.BeginScatter(totalElementsToUpload, out byte* uploadPtr, out uint* scatterPtr);

                int propertySizeBytes = ElementsPerInstance * BytesPerElement;
                byte* nullElementPtr = stackalloc byte[BytesPerElement];
                byte* srcBytesPtr = (byte*)PropertyArray.Ptr + startInstanceIndex * propertySizeBytes;
                byte* scatterBytesPtr = uploadPtr;

                UnsafeArray<uint> scatterIndices = new UnsafeArray<uint>((uint*)scatterPtr, totalElementsToUpload);
                int scatterBaseIndex = 0;
                int renderOffsetBase = BufferOffset + startInstanceIndex * ElementsPerInstance;

                for (int i = 0; i < instanceCount; ++i)
                {
                    int instanceIndex = startInstanceIndex + i;

                    PackedRenderIndex packedIndex = RenderIndices.IsValidIndex(instanceIndex) ? RenderIndices[instanceIndex] : instanceIndex;
                    int renderIndex = packedIndex.Index;
                    if (renderIndex >= 0)
                    {
                        int renderOffset = renderOffsetBase + renderIndex * ElementsPerInstance;

                        if (!packedIndex.IsDestroyed && PropertyArray.IsValidIndex(instanceIndex))
                        {
                            for (int elementIndex = 0; elementIndex < ElementsPerInstance; ++elementIndex)
                                scatterIndices[scatterBaseIndex + elementIndex] = (uint)(renderOffset + elementIndex);

                            UnsafeUtility.MemCpy(scatterBytesPtr, srcBytesPtr, propertySizeBytes);
                        }
                        else
                        {
                            for (int elementIndex = 0; elementIndex < ElementsPerInstance; ++elementIndex)
                                scatterIndices[scatterBaseIndex + elementIndex] = 0;

                            UnsafeUtility.MemCpy(scatterBytesPtr, nullElementPtr, propertySizeBytes);
                        }

                        scatterBaseIndex += ElementsPerInstance;
                        srcBytesPtr += propertySizeBytes;
                        scatterBytesPtr += propertySizeBytes;
                    }
                }
            }
        }

#if UNITY_EDITOR
        [BurstCompile]
        unsafe struct ScatterEditorDataJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public int BufferOffset;
            public bool AllSelected;
            [ReadOnly] public NativeArray<PackedRenderIndex>.ReadOnly RenderIndices;
            [ReadOnly] public NativeArray<InstancedGlobalID>.ReadOnly InstancedIDs;
            [ReadOnly] public UnsafeBitList Selected;
            [WriteOnly] public ThreadedScatterUploader ScatterUploader;

            const uint k_SelectedFlag = 0x80000000;
            const uint k_SelectedMask = 0x7FFFFFFF;

            public void Execute(int startInstanceIndex, int instanceCount)
            {
                int totalElementsToUpload = instanceCount * InstanceBufferConfig.EditorDataElements;
                ScatterUploader.BeginScatter(totalElementsToUpload, out byte* uploadPtr, out uint* scatterPtr);

                UnsafeArray<uint> scatterElements = new UnsafeArray<uint>((uint*)uploadPtr, totalElementsToUpload);
                UnsafeArray<uint> scatterIndices = new UnsafeArray<uint>((uint*)scatterPtr, totalElementsToUpload);
                int scatterBaseIndex = 0;

                for (int i = 0; i < instanceCount; ++i)
                {
                    int instanceIndex = startInstanceIndex + i;

                    PackedRenderIndex packedIndex = RenderIndices.IsValidIndex(instanceIndex) ? RenderIndices[instanceIndex] : instanceIndex;
                    int renderIndex = packedIndex.Index;
                    if (renderIndex >= 0)
                    {
                        int renderOffset = BufferOffset + renderIndex * InstanceBufferConfig.EditorDataElements;

                        uint selectionID = 0;
                        if (!packedIndex.IsDestroyed && InstancedIDs.IsValidIndex(instanceIndex))
                        {
                            selectionID = (uint)(InstancedIDs[instanceIndex].Value) & k_SelectedMask;
                            if (AllSelected || (Selected.IsValidIndex(instanceIndex) && Selected[instanceIndex]))
                                selectionID |= k_SelectedFlag;
                        }

                        int scatterIndex = scatterBaseIndex;
                        scatterElements[scatterIndex] = selectionID;
                        scatterIndices[scatterIndex]  = (uint)renderOffset;

                        scatterBaseIndex += InstanceBufferConfig.EditorDataElements;
                    }
                }
            }
        }
#endif
    }
}
