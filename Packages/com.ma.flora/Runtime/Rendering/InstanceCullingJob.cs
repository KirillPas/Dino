// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine.Rendering;
using Plane = MA.Mathematics.Plane;

namespace MA.Flora.Rendering
{
    [DebuggerDisplay("Index={StartInstanceIndex} Count={InstanceCount}")]
    [StructLayout(LayoutKind.Sequential)]
    struct DrawRange
    {
        public int StartInstanceIndex;
        public int EndInstanceIndex;

        public int InstanceCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => EndInstanceIndex - StartInstanceIndex + 1;
        }
    }

    [Flags]
    enum DrawCullingFlags : byte
    {
        None               = 0,
        Density            = 1 << 0,
        CrossFade          = 1 << 1,
        CrossFadeSpeedTree = 1 << 2,
        CrossFadeAnimated  = 1 << 3,
        Occlusion          = 1 << 4,
    }

    [DebuggerDisplay("Mode={FilterSettings.ShadowCastingMode} Material={MaterialID.ToString()} Mesh={MeshID.ToString()} LOD={LODIndex} Draws={DrawRanges.Length}")]
    [StructLayout(LayoutKind.Sequential)]
    struct DrawBatch
    {
        public DrawCullingFlags CullFlags;
        public AxisAlignedBox Bounds;
        public int InstanceDataOffset;
        public UnsafeArray<DrawRange> DrawRanges;
        public DrawSortKey SortKey;

        public InstancedRendererID RendererID;
        public InstancedMeshID MeshID;
        public ushort SubMeshIndex;
        public SubMeshDescriptor SubMesh;

        public InstancedMaterialID MaterialID;
        public InstancedBatchID BatchID;
        public InstancedMaterialVariant MaterialVariant;
        public RenderFilterSettings FilterSettings;
        public int RenderPriority;

        public byte LODIndex;
        public float4 LODDistances;
        public float3 LODCoefficients;
        public float CullDistance;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InitializeSortKey() => SortKey = new DrawSortKey(this);
    }

    unsafe struct DrawSortKey : IEquatable<DrawSortKey>, IComparable<DrawSortKey>
    {
        public uint4 Key;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DrawSortKey(in DrawBatch batch)
        {
            Key.x = batch.FilterSettings.RenderingLayerMask;
            Key.y = batch.FilterSettings.Packed;
            Key.z = (uint)((int)batch.MaterialVariant & 0xff) << 24 | (uint)(batch.MaterialID.GetHashCode() & 0x00ffffff);
            Key.w = ((uint)(batch.MeshID.GetHashCode() & 0x00ffffff) << 8) | (uint)(batch.SubMeshIndex & 0xff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(DrawSortKey other)
        {
            uint4 a = Key;
            uint4 b = other.Key;

            int4 lt = math.select(int4.zero, new int4(-1), a < b);
            int4 gt = math.select(int4.zero, new int4(1), a > b);
            int4 ne = lt | gt;

            int* firstNonZero = stackalloc int[4];
            bool4 nz = ne != int4.zero;
            bool anyNz = math.any(nz);
            math.compress(firstNonZero, 0, ne, nz);

            return anyNz ? firstNonZero[0] : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(DrawSortKey other) => Key.Equals(other.Key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is DrawSortKey other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Key.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in DrawSortKey lhs, in DrawSortKey rhs) => lhs.Equals(rhs);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(in DrawSortKey lhs, in DrawSortKey rhs) => !lhs.Equals(rhs);
    }

    enum InstanceCullViewType
    {
        Camera,
        Light,
    }

    [NoAlias, BurstCompile]
    unsafe struct InstanceCullingJob : IJobParallelForBatchLegacyCompatible
    {
        public const int BatchSize = 32;

        public ThreadLocalAllocator FrameAllocator;

        public InstanceCullViewType CullViewType;
        public float3 CameraPosition;
        public float3 AnimatedCameraPosition0;
        public float3 AnimatedCameraPosition1;
        public float FarClipPlane;
        public int MaxLOD;
        public bool IsOrthographic;
        public float MinimumScreenSize;
        public float ScreenRelativeMetric;
        public bool IsSceneViewCamera;
        public bool DisableCullDistance;
        public uint CullingLayerMask;
        public ulong SceneCullingMask;

        [ReadOnly] public UnsafeArray<FrustumSIMDPacket>.ReadOnly CullingPlanePackets;
        [ReadOnly] public UnsafeArray<Plane>.ReadOnly LightFacingPlanes;
        [ReadOnly] public float3x3 WorldToLightSpaceRotation;
        [ReadOnly] public float4 ReceiverSphereInLightSpace;

        [ReadOnly] public UnsafeArray<InstancedRendererID>.ReadOnly Renderer;
        [ReadOnly] public UnsafeArray<InstanceRendererData>.ReadOnly RendererCulling;
        [ReadOnly] public UnsafeArray<InstanceRendererTreeData>.ReadOnly RendererTreeData;
        [ReadOnly] public UnsafeArray<InstanceRendererOcclusionData>.ReadOnly RendererOcclusionData;
        [ReadOnly] public UnsafeArray<BufferAllocation>.ReadOnly RendererBufferAllocations;
        [ReadOnly] public UnsafeBitList RendererIsVisibleInScene;
        [ReadOnly] public UnsafeBitList RendererHasShadowCasters;

        [ReadOnly] public InstancedPrototypeDataArrays.ReadOnly PrototypeArrays;
        [ReadOnly] public NativeArray<CullingNode>.ReadOnly CullingNodeStore;
        [ReadOnly] public NativeArray<AxisAlignedBox>.ReadOnly UnbuiltInstanceBoundsStore;
        [ReadOnly] public UnsafeArray<bool>.ReadOnly StaticOcclusionResults;

        [WriteOnly] public UnsafeThreadLocalList<DrawBatch> DrawBatches;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public int DebugForceLOD;
        public int DebugOnlyLOD;
        public bool DebugDisableCulling;
#endif

        [NativeSetThreadIndex] int m_ThreadIndex;

        const int k_MaxLODCount = 8;

        public static readonly ProfilerMarker CullCameraMarker = new ProfilerMarker("InstanceCullingJob (Camera)");
        public static readonly ProfilerMarker CullLightMarker = new ProfilerMarker("InstanceCullingJob (Light)");

        public void Execute(int startIndex, int count)
        {
            using ProfilerMarker.AutoScope _ = CullViewType == InstanceCullViewType.Camera
                ? CullCameraMarker.Auto()
                : CullLightMarker.Auto();

            AllocatorManager.AllocatorHandle allocator = FrameAllocator.ThreadAllocator(m_ThreadIndex)->Handle;
            UnsafeIndirectList<DrawBatch> batches = DrawBatches.GetThreadLocalList(m_ThreadIndex);

            bool isShadowPass = CullViewType == InstanceCullViewType.Light;
            bool isCameraPass = !isShadowPass;

            for (int index = 0; index < count; index++)
            {
                InstancedRendererID rendererID = Renderer[startIndex + index];
#if UNITY_EDITOR
                if (IsSceneViewCamera)
                {
                    if (!RendererIsVisibleInScene[rendererID])
                        continue;

                    if ((SceneCullingMask & RendererCulling[rendererID].SceneCullingMask) == 0)
                        continue;
                }
#endif

                if (isShadowPass && !RendererHasShadowCasters[rendererID])
                    continue;

                CullParameters parameters = new CullParameters(this, rendererID);
                if (parameters.PrototypeID == -1)
                    continue;

                bool hasValidTree = parameters.TreeNodes != null;
                if (hasValidTree)
                {
                    CullResult result = new CullResult(parameters.HasValidCrossFade, allocator);

                    if (isCameraPass)
                    {
                        CameraCuller culler = new CameraCuller { CameraFrustumPacket = CullingPlanePackets[0] };
                        CullRecursive(ref result, parameters.TreeNodes, parameters, culler, 0, parameters.LODMin, parameters.LODMax + 1, DebugDisableCulling);
                    }
                    else
                    {
                        ShadowCuller culler = new ShadowCuller
                        {
                            Packets = CullingPlanePackets,
                            LightFacingPlanes = LightFacingPlanes,
                            WorldToLightSpaceRotation = WorldToLightSpaceRotation,
                            ReceiverSphereInLightSpace = ReceiverSphereInLightSpace,
                        };
                        CullRecursive(ref result, parameters.TreeNodes, parameters, culler, 0, parameters.LODMin, parameters.LODMax + 1, DebugDisableCulling);
                    }

                    if (result.TotalInstanceCount > 0)
                        CreateBatches(parameters, result, batches, false);
                }

                if (parameters.UnbuiltInstanceBounds.Length > 0)
                {
                    // Disable camera cross-fade animation for unbuilt instances
                    parameters.HasAnimatedCrossFade = false;
                    parameters.HasValidCrossFade = false;
                    parameters.CameraPosition0 = parameters.CameraPosition1 = CameraPosition;
                    GatherUnbuiltBatches(parameters, batches, allocator);
                }
            }
        }

        void GatherUnbuiltBatches(in CullParameters parameters, UnsafeIndirectList<DrawBatch> batches, AllocatorManager.AllocatorHandle allocator)
        {
            int unbuiltCount = parameters.InstanceCountToRender - parameters.FirstUnbuiltRenderIndex;
            if (unbuiltCount == 0)
                return;

            if (unbuiltCount > parameters.UnbuiltInstanceBounds.Length)
                unbuiltCount = parameters.UnbuiltInstanceBounds.Length;

            CullResult result = new CullResult(false, allocator);
            int lastUnbuiltIndex = parameters.FirstUnbuiltRenderIndex + unbuiltCount - 1;

            if (parameters.LODMax == parameters.LODMin)
            {
                AddInstancesToResult(ref result, parameters.LODMax, parameters.LODMax, parameters.UnbuiltInstanceBoundsCombined, parameters.FirstUnbuiltRenderIndex, lastUnbuiltIndex);
            }
            else
            {
                int minLOD = parameters.LODMin;
                int maxLOD = parameters.LODMax + 1;
                CalculateLODRange(parameters, parameters.UnbuiltInstanceBounds[0].Center, parameters.UnbuiltInstanceBounds[0].Radius, ref minLOD, ref maxLOD);
                AxisAlignedBox drawBounds = parameters.UnbuiltInstanceBounds[0];

                int firstIndexInRun = 0;
                for (int index = 1; index < unbuiltCount; ++index)
                {
                    int tempMinLOD = parameters.LODMin;
                    int tempMaxLOD = parameters.LODMax + 1;
                    CalculateLODRange(parameters, parameters.UnbuiltInstanceBounds[index].Center, parameters.UnbuiltInstanceBounds[index].Radius, ref tempMinLOD, ref tempMaxLOD);
                    drawBounds += parameters.UnbuiltInstanceBounds[index];

                    if (tempMinLOD != minLOD)
                    {
                        if (tempMinLOD <= parameters.LODMax)
                        {
                            AddInstancesToResult(ref result, minLOD, minLOD, drawBounds, firstIndexInRun + parameters.FirstUnbuiltRenderIndex, (index - 1) + parameters.FirstUnbuiltRenderIndex);
                            drawBounds = parameters.UnbuiltInstanceBounds[index];
                        }

                        minLOD = tempMinLOD;
                        firstIndexInRun = index;
                    }
                }

                AddInstancesToResult(ref result, minLOD, minLOD, drawBounds, firstIndexInRun + parameters.FirstUnbuiltRenderIndex, lastUnbuiltIndex);
            }

            CreateBatches(parameters, result, batches, true);
        }

        void CreateBatches(in CullParameters parameters, in CullResult result, UnsafeIndirectList<DrawBatch> batches, bool unbuilt)
        {
            ref readonly InstanceRendererData renderer = ref RendererCulling[parameters.RendererID];

            InstancedPrototypeLODData prototypeLOD = PrototypeArrays.LOD[parameters.PrototypeID];
            InstancedPrototypeCullingData prototypeCulling = PrototypeArrays.Culling[parameters.PrototypeID];
            InstancedPrototypeShadowData prototypeShadows = PrototypeArrays.Shadow[parameters.PrototypeID];

            bool wantsSharedMaterial = prototypeShadows.ShadowOverrideMode != InstancedShadowOverrideMode.None;
            InstancedMaterialID shadowMaterialID = InstancedMaterialID.Null;
            bool isShadowPass = CullViewType == InstanceCullViewType.Light;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int onlyLOD = math.min(DebugOnlyLOD, parameters.LODCount - 1);
            int firstLOD = onlyLOD < 0 ? parameters.LODMin : onlyLOD;
            int finalLod = onlyLOD < 0 ? parameters.LODMax : onlyLOD;
#else
            int firstLOD = parameters.LODMin;
            int finalLod = parameters.LODMax;
#endif

            DrawCullingFlags prototypeFlags = DrawCullingFlags.None;
            if (prototypeCulling.HasDynamicDensity)
                prototypeFlags |= DrawCullingFlags.Density;

            int drawTypeCount = result.MultiDraw ? 2 : 1;
            for (int drawTypeIndex = 0; drawTypeIndex < drawTypeCount; ++drawTypeIndex)
            {
                bool isMultiDrawLOD = drawTypeIndex == 1;
                UnsafeArray<UnsafeList<DrawRange>> drawArrays = isMultiDrawLOD
                    ? result.LODMultiDraws
                    : result.LODDraws;

                for (int lod = firstLOD; lod <= finalLod; lod++)
                {
                    UnsafeList<DrawRange> drawRanges = drawArrays[lod];
                    if (!drawRanges.IsCreated || drawRanges.Length == 0)
                        continue;

                    int drawCount = prototypeLOD.DrawCount[lod];
                    if (drawCount == 0)
                        continue;

                    AxisAlignedBox drawBounds = result.LODDrawBounds[lod];
                    DrawCullingFlags lodFlags = prototypeFlags;
                    bool lodIsCrossFade =  !unbuilt && isMultiDrawLOD;
                    InstancedMaterialVariant materialVariant = InstancedMaterialVariant.Instanced;
                    float4 lodDistances = new float4(float.MinValue, float.MinValue, float.MinValue, 1.0f);
                    float3 lodCoefficients = new float3(0.0f, 0.0f, 0.0f);

                    if (lodIsCrossFade)
                    {
                        lodFlags |= DrawCullingFlags.CrossFade;
                        materialVariant = InstancedMaterialVariant.LODCrossFade;

                        bool isPercentageFade = prototypeLOD.PercentageFlags[lod];
                        if (isPercentageFade)
                        {
                            lodFlags |= DrawCullingFlags.CrossFadeSpeedTree;
                            materialVariant = InstancedMaterialVariant.LODCrossFadePercentage;

                            lodDistances.x = lod > 0 ? parameters.LODPlanesMax[lod - 1] : parameters.LODWorldSpaceSize;
                            lodDistances.y = parameters.LODPlanesMax[lod];
                        }
                        else if (parameters.HasAnimatedCrossFade)
                        {
                            lodFlags |= DrawCullingFlags.CrossFadeAnimated;

                            lodCoefficients = new float3 { x = lod > firstLOD ? -1.0f : 0.0f, y = 0.0f, z = 1.0f };
                            lodCoefficients.y -= lodCoefficients.x;
                            lodCoefficients.z -= lodCoefficients.x + lodCoefficients.y;

                            lodDistances.x = lod > firstLOD ? parameters.LODPlanesMax[lod - 1] : float.MinValue;
                            lodDistances.y = parameters.LODPlanesMax[lod];
                            lodDistances.z = float.MaxValue;
                        }
                        else
                        {
                            lodDistances.x = lod > firstLOD ? parameters.LODPlanesMin[lod - 1] : 0.0f;
                            lodDistances.y = lod > firstLOD ? parameters.LODPlanesMax[lod - 1] : 0.0f;
                            lodDistances.z = parameters.LODPlanesMin[lod];
                            lodDistances.w = parameters.LODPlanesMax[lod];
                        }
                    }

                    int drawOffset = prototypeLOD.DrawOffset[lod];
                    int drawEnd = drawOffset + drawCount;

                    for (int lodDrawIndex = drawOffset; lodDrawIndex < drawEnd; lodDrawIndex++)
                    {
                        ref readonly InstancedPrototypeStateData prototypeStateData = ref PrototypeArrays.DrawStore[lodDrawIndex];
                        if (!prototypeStateData.IsValid)
                            continue;

                        RenderFilterSettings filterSettings = prototypeStateData.FilterSettings;
                        if ((filterSettings.LayerMask & CullingLayerMask) == 0)
                            continue;

                        ShadowCastingMode shadowCastingMode = filterSettings.ShadowCastingMode;
                        switch (isShadowPass)
                        {
                            // Don't add Batches for the shadow pass if the render state doesn't cast shadows
                            case true when shadowCastingMode == ShadowCastingMode.Off:
                                continue;
                            // Don't add Batches for the main pass if the render state only casts shadows
                            case false when shadowCastingMode == ShadowCastingMode.ShadowsOnly:
                                continue;
                        }

                        filterSettings.ShadowCastingMode = isShadowPass ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off;

                        DrawBatch batch = new DrawBatch
                        {
                            CullFlags = lodFlags,
                            Bounds = drawBounds,
                            RendererID = parameters.RendererID,
                            MaterialID = prototypeStateData.Material,
                            BatchID = renderer.BatchID,
                            MaterialVariant = materialVariant,
                            MeshID = prototypeStateData.Mesh,
                            SubMeshIndex = prototypeStateData.SubMeshIndex,
                            SubMesh = prototypeStateData.SubMeshDescriptor,
                            FilterSettings = filterSettings,
                            RenderPriority = 0,
                            InstanceDataOffset = parameters.InstanceDataOffset,
                            DrawRanges = drawRanges.AsUnsafeArray(),
                            LODIndex = (byte)lod,
                            SortKey = default,
                            LODDistances = lodDistances,
                            LODCoefficients = lodCoefficients,
                            CullDistance = parameters.CullDistance
                        };

                        if (isShadowPass && wantsSharedMaterial)
                        {
                            if (!shadowMaterialID.IsCreated)
                                shadowMaterialID = prototypeStateData.Material;

                            batch.MaterialID = shadowMaterialID;
                        }

                        batch.InitializeSortKey();
                        batches.Add(batch);
                    }
                }
            }
        }

        struct CullParameters
        {
            public InstancedRendererID RendererID;
            public InstancedPrototypeID PrototypeID;
            public int InstanceCountToRender;
            public int InstanceDataOffset;

            public float3 CameraPosition0;
            public float3 CameraPosition1;

            public bool IsOrthographic;
            public bool HasValidCrossFade;
            public bool HasAnimatedCrossFade;

            public float CullDistance;
            public float LODWorldSpaceSize;
            public float LODScreenRelativeMetric;
            public float LODScreenRelativeMetricSq;

            public int LODMin;
            public int LODMax;
            public int LODCount;

            public fixed int MaxInstancesPerGroup[k_MaxLODCount];
            public fixed float LODPlanesMax[k_MaxLODCount];
            public fixed float LODPlanesMin[k_MaxLODCount];

            [NoAlias] public CullingNode* TreeNodes;

            public AxisAlignedBox UnbuiltInstanceBoundsCombined;
            [NoAlias] public NativeArray<AxisAlignedBox>.ReadOnly UnbuiltInstanceBounds;
            public int FirstUnbuiltRenderIndex;

            [NoAlias] public UnsafeArray<bool>.ReadOnly OcclusionResults;
            public int FirstOcclusionNode;
            public int LastOcclusionNode;

            public CullParameters(in InstanceCullingJob job, InstancedRendererID rendererID)
            {
                RendererID = rendererID;

                ref readonly InstanceRendererTreeData tree = ref job.RendererTreeData[rendererID];
                TreeNodes = tree.Count > 0 ? job.CullingNodeStore.GetUnsafeReadOnlyPtrAt(tree.Offset) : null;

                ref readonly InstanceRendererData rendererCullingData = ref job.RendererCulling[rendererID];
                BufferAllocation instanceDataAllocation = job.RendererBufferAllocations[rendererID];

                if (!job.PrototypeArrays.Exists(rendererCullingData.PrototypeID) || !instanceDataAllocation.IsValid)
                {
                    this = default;
                    return;
                }

                PrototypeID = rendererCullingData.PrototypeID;
                InstanceCountToRender = rendererCullingData.InstanceCountToRender;
                InstanceDataOffset = instanceDataAllocation.Offset;

                InstancedPrototypeLODData prototypeLODData = job.PrototypeArrays.LOD[PrototypeID];
                InstancedPrototypeCullingData prototypeCulling = job.PrototypeArrays.Culling[PrototypeID];
                InstancedPrototypeShadowData prototypeShadows = job.PrototypeArrays.Shadow[PrototypeID];

                IsOrthographic = job.IsOrthographic;
                HasValidCrossFade = prototypeCulling.HasCrossFade && prototypeLODData.LODCount > 1;
                HasAnimatedCrossFade = HasValidCrossFade && prototypeCulling.HasAnimatedCrossFade;

                if (HasAnimatedCrossFade)
                {
                    // If we're cross-fading with animation, use the animated camera positions
                    CameraPosition0 = job.AnimatedCameraPosition0;
                    CameraPosition1 = job.AnimatedCameraPosition1;
                }
                else
                {
                    CameraPosition0 = job.CameraPosition;
                    CameraPosition1 = job.CameraPosition;
                }

                LODCount = prototypeLODData.LODCount;
                LODMin = job.MaxLOD;
                LODMax = LODCount - 1;
                if (job.CullViewType == InstanceCullViewType.Light)
                {
                    LODMin = math.max(job.MaxLOD, prototypeShadows.ShadowLODRange.Min);
                    LODMax = math.min(LODMax, prototypeShadows.ShadowLODRange.Max);
                }
                LODMin = math.clamp(LODMin, 0, LODCount - 1);
                LODMax = math.clamp(LODMax, 0, LODCount - 1);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                int forceLOD = job.DebugForceLOD;
                if (forceLOD >= 0)
                {
                    // If we're forcing an LOD, use that
                    forceLOD = math.min(forceLOD, LODCount - 1);
                    LODMin = (byte)forceLOD;
                    LODMax = (byte)forceLOD;
                    HasValidCrossFade = false;
                }
#endif

                LODScreenRelativeMetric = job.ScreenRelativeMetric;
                LODScreenRelativeMetricSq = LODScreenRelativeMetric * LODScreenRelativeMetric;
                LODWorldSpaceSize = rendererCullingData.LODAverageWorldSpaceSize;

                float cullDistance = job.FarClipPlane;
                if (!job.DisableCullDistance && rendererCullingData.CullingDistance > 0.0f)
                    cullDistance = math.min(cullDistance, rendererCullingData.CullingDistance);
                else
                    cullDistance = math.min(cullDistance, LODGroupUtility.CalculateLODDistance(prototypeLODData.LODHeight[LODCount - 1], LODWorldSpaceSize));

                if (job is { IsOrthographic: false, MinimumScreenSize: > 0.0f })
                    cullDistance = math.min(cullDistance, LODGroupUtility.CalculateLODDistance(job.MinimumScreenSize, cullDistance));

                if (job.CullViewType == InstanceCullViewType.Light)
                {
                    if (!job.DisableCullDistance && prototypeShadows.ShadowDistance > 0.0f)
                        cullDistance = math.min(cullDistance, prototypeShadows.ShadowDistance);
                }

                CullDistance = cullDistance;

                for (int lodIndex = 0; lodIndex < LODCount; lodIndex++)
                {
                    float transitionStartDistance = math.max(0.0f, LODGroupUtility.CalculateLODDistance(prototypeLODData.LODTransitionHeight[lodIndex], LODWorldSpaceSize));
                    float lodDistance = math.max(0.0f, LODGroupUtility.CalculateLODDistance(prototypeLODData.LODHeight[lodIndex], LODWorldSpaceSize));

                    LODPlanesMin[lodIndex] = math.min(transitionStartDistance, cullDistance);
                    LODPlanesMax[lodIndex] = math.min(lodDistance, cullDistance);

                    MaxInstancesPerGroup[lodIndex] = 2;
                    int vertexCount = prototypeLODData.VertexCount[lodIndex];
                    if (vertexCount > 0)
                        MaxInstancesPerGroup[lodIndex] = tree.MinimumVerticesPerCluster / vertexCount;
                }

                if (tree.UnbuiltCount > 0)
                {
                    FirstUnbuiltRenderIndex = tree.FirstUnbuiltIndex;
                    UnbuiltInstanceBounds = job.UnbuiltInstanceBoundsStore.GetSubArray(tree.UnbuiltOffset, tree.UnbuiltCount);
                    UnbuiltInstanceBoundsCombined = tree.UnbuiltBoundsCombined;
                }
                else
                {
                    FirstUnbuiltRenderIndex = -1;
                    UnbuiltInstanceBounds = default;
                    UnbuiltInstanceBoundsCombined = AxisAlignedBox.Empty;
                }

                ref readonly InstanceRendererOcclusionData occlusionData = ref job.RendererOcclusionData[rendererID];
                if (job.RendererOcclusionData.IsCreated && job.StaticOcclusionResults.Length > 0 &&
                    occlusionData is { Count: > 0, FirstNode: >= 0, LastNode: >= 0 } &&
                    occlusionData.FirstNode <= occlusionData.LastNode)
                {
                    OcclusionResults = job.StaticOcclusionResults.GetSubArray(occlusionData.Offset, occlusionData.Count);
                    FirstOcclusionNode = occlusionData.FirstNode;
                    LastOcclusionNode = occlusionData.LastNode;
                }
                else
                {
                    OcclusionResults = default;
                    FirstOcclusionNode = -1;
                    LastOcclusionNode = -1;
                }
            }
        }

        struct CullResult
        {
            public AllocatorManager.AllocatorHandle Allocator;
            public bool MultiDraw;
            [NoAlias] public UnsafeArray<UnsafeList<DrawRange>> LODDraws;
            [NoAlias] public UnsafeArray<UnsafeList<DrawRange>> LODMultiDraws;
            [NoAlias] public UnsafeArray<AxisAlignedBox> LODDrawBounds;
            public int TotalInstanceCount;

            public CullResult(bool multiDraw, AllocatorManager.AllocatorHandle allocator)
            {
                Allocator = allocator;
                MultiDraw = multiDraw;

                LODDraws = new UnsafeArray<UnsafeList<DrawRange>>(k_MaxLODCount, allocator);

                LODMultiDraws = default;
                if (MultiDraw)
                    LODMultiDraws = new UnsafeArray<UnsafeList<DrawRange>>(k_MaxLODCount, allocator);

                LODDrawBounds = new UnsafeArray<AxisAlignedBox>(k_MaxLODCount, allocator);
                for (int i = 0; i < k_MaxLODCount; i++)
                    LODDrawBounds[i] = AxisAlignedBox.Empty;

                TotalInstanceCount = 0;
            }
        }

        interface INodeCuller
        {
            bool Cull(in float3 center, in float3 extent, float radius, out bool fullyInside);
        }

        struct CameraCuller : INodeCuller
        {
            public FrustumSIMDPacket CameraFrustumPacket;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Cull(in float3 center, in float3 extent, float radius, out bool fullyInside)
            {
                return IntersectSIMDPacket(CameraFrustumPacket, center, extent, out fullyInside);
            }
        }

        struct ShadowCuller : INodeCuller
        {
            public UnsafeArray<FrustumSIMDPacket>.ReadOnly Packets;
            public UnsafeArray<Plane>.ReadOnly LightFacingPlanes;
            public float3x3 WorldToLightSpaceRotation;
            public float4 ReceiverSphereInLightSpace;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Cull(in float3 center, in float3 extent, float radius, out bool fullyInside)
            {
                fullyInside = false;

                if (!FrustumUtility.OverlapsBoundsSIMD(Packets, center, extent))
                    return true;

                if (CullReceiverSphere(LightFacingPlanes, WorldToLightSpaceRotation, ReceiverSphereInLightSpace, center, radius, out fullyInside))
                    return true;

                return false;
            }
        }

        static void CullRecursive<TNodeCuller>(
            ref CullResult result,
            [NoAlias] CullingNode* tree,
            in CullParameters parameters,
            in TNodeCuller culler,
            [AssumeRange(0, 7)] int index,
            [AssumeRange(0, 7)] int minLOD,
            [AssumeRange(0, 7)] int maxLOD,
            bool inside)
            where TNodeCuller : INodeCuller
        {
            ref readonly CullingNode node = ref tree[index];
            float3 center = node.Bounds.Center;
            float3 extents = node.Bounds.Extents;
            float radius = math.length(extents);

            if (!inside && culler.Cull(center, extents, radius, out inside))
                return; // Skip the node if culled

            if (index >= parameters.FirstOcclusionNode && index <= parameters.LastOcclusionNode)
            {
                int occlusionIndex = index - parameters.FirstOcclusionNode;
                if (parameters.OcclusionResults[occlusionIndex])
                    return; // Skip the node if occluded
            }

            if (minLOD != maxLOD)
            {
                CalculateLODRange(parameters, center, radius, ref minLOD, ref maxLOD);
                if (minLOD >= parameters.LODCount)
                    return; // Skip the node if outside the LOD range
            }

            bool canGroup = node.IsLeaf || (node.InstanceCount < parameters.MaxInstancesPerGroup[minLOD] && InsideCullDistance(parameters, center, radius));
            bool wantsSplit = (!inside || minLOD < maxLOD || index < parameters.FirstOcclusionNode) && !canGroup;

            if (wantsSplit)
            {
                for (int childIndex = node.FirstChild; childIndex <= node.LastChild; childIndex++)
                {
                    CullRecursive(ref result, tree, parameters, culler, childIndex, minLOD, maxLOD, inside);
                }
            }
            else
            {
                maxLOD = math.min(maxLOD, parameters.LODCount - 1);
                AddInstancesToResult(ref result, minLOD, maxLOD, node.Bounds, node.FirstInstance, node.LastInstance);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AddInstancesToResult(
            ref CullResult result,
            [AssumeRange(0, 7)] int currentMinLOD,
            [AssumeRange(0, 7)] int currentMaxLOD,
            in AxisAlignedBox bounds,
            int firstInstance,
            int lastInstance)
        {
            DrawRange drawRange = new DrawRange
            {
                StartInstanceIndex = firstInstance,
                EndInstanceIndex = lastInstance
            };

            // When cross-fading, use multi-draw if there is a LOD range, or we are at the last LOD
            if (result.MultiDraw)
            {
                for (int lodIndex = currentMinLOD; lodIndex <= currentMaxLOD; ++lodIndex)
                {
                    AddInstancesToDraws(ref result.LODMultiDraws[lodIndex], drawRange, result.Allocator);
                    result.TotalInstanceCount += drawRange.InstanceCount;
                    result.LODDrawBounds[lodIndex] += bounds;
                }
            }
            else
            {
                AddInstancesToDraws(ref result.LODDraws[currentMinLOD], drawRange, result.Allocator);
                result.TotalInstanceCount += drawRange.InstanceCount;
                result.LODDrawBounds[currentMinLOD] += bounds;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AddInstancesToDraws(ref UnsafeList<DrawRange> draws, in DrawRange draw, in AllocatorManager.AllocatorHandle allocator)
        {
            if (!draws.IsCreated)
                draws = new UnsafeList<DrawRange>(math.ceilpow2(draw.InstanceCount), allocator);

            if (draws.Length > 0)
            {
                // Check if the new range can be merged with the previous range
                ref DrawRange last = ref draws.Ptr[draws.Length - 1];
                if (last.EndInstanceIndex + 1 == draw.StartInstanceIndex)
                {
                    last.EndInstanceIndex = draw.EndInstanceIndex;
                    return;
                }
            }

            draws.Add(draw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IntersectSIMDPacket(in FrustumSIMDPacket packet, in float3 center, in float3 extent, out bool fullyInside)
        {
            float4 cx = center.xxxx;
            float4 cy = center.yyyy;
            float4 cz = center.zzzz;

            float4 ex = extent.xxxx;
            float4 ey = extent.yyyy;
            float4 ez = extent.zzzz;

            float4 distances = packet.Nx * cx + packet.Ny * cy + packet.Nz * cz + packet.D;
            float4 radii = packet.AbsNx * ex + packet.AbsNy * ey + packet.AbsNz * ez;

            fullyInside = math.all(distances >= radii);
            return math.any(distances + radii < float4.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool InsideCullDistance(in CullParameters input, in float3 center, float radius)
        {
            float d0 = CalculateDistance(input, input.CameraPosition0, center);
            float d1 = CalculateDistance(input, input.CameraPosition1, center);
            float maxDist = math.max(d0, d1) + radius;
            return maxDist <= input.LODPlanesMax[input.LODCount - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CalculateLODRange(in CullParameters input, in float3 center, float radius, ref int minLOD, ref int maxLOD)
        {
            if (minLOD == maxLOD) return;

            float d0 = CalculateDistance(input, input.CameraPosition0, center);
            float d1 = CalculateDistance(input, input.CameraPosition1, center);
            float minDist = math.min(d0, d1) - radius;
            float maxDist = math.max(d0, d1) + radius;

            while (maxLOD > minLOD && minDist > input.LODPlanesMax[minLOD])
            {
                minLOD++;
            }

            while (maxLOD > minLOD && maxDist < input.LODPlanesMin[maxLOD - 1])
            {
                maxLOD--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float CalculateDistance(in CullParameters input, in float3 cameraPosition, in float3 center)
        {
            return input.IsOrthographic
                ? input.LODScreenRelativeMetric
                : LODGroupUtility.CalculatePerspectiveDistance(cameraPosition, center, input.LODScreenRelativeMetricSq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool CullReceiverSphere(
            UnsafeArray<Plane>.ReadOnly lightFacingFrustumPlanes, in float3x3 worldToLightSpaceRotation, in float4 receiverSphereLightSpace,
            in float3 center, in float casterRadius,
            out bool inside)
        {
            float3 casterCenterWorldSpace = center;
            float3 casterCenterLightSpace = math.mul(worldToLightSpaceRotation, center);

            // push the (light-facing) frustum planes back by the caster radius, then intersect with a line through the caster capsule center,
            // to compute the length of the shadow that will cover all possible receivers within the whole frustum (not just this split)
            float3 shadowDirection = math.transpose(worldToLightSpaceRotation).c2;
            float shadowLength = math.INFINITY;
            for (int i = 0; i < lightFacingFrustumPlanes.Length; ++i)
            {
                shadowLength = math.min(shadowLength, DistanceUntilCylinderFullyCrossesPlane(
                    casterCenterWorldSpace,
                    shadowDirection,
                    casterRadius,
                    lightFacingFrustumPlanes[i]));
            }
            shadowLength = math.max(shadowLength, 0.0f);

            float3 receiverCenterLightSpace = receiverSphereLightSpace.xyz;
            float receiverRadius = receiverSphereLightSpace.w;
            float3 receiverToCasterLightSpace = casterCenterLightSpace - receiverCenterLightSpace;

            // compute the light space z coordinate where the caster sphere and receiver sphere just intersect
            float sphereIntersectionMaxDistance = casterRadius + receiverRadius;
            float zSqAtSphereIntersection = math.lengthsq(sphereIntersectionMaxDistance) - math.lengthsq(receiverToCasterLightSpace.xy);
            inside = false;

            // if this is negative, the spheres do not overlap as circles in the XY plane, so cull the caster
            if (zSqAtSphereIntersection < 0.0f)
                return true;

            // if the caster is outside of the receiver sphere in the light direction, it cannot cast a shadow on it, so cull it
            if (receiverToCasterLightSpace.z > 0.0f && math.lengthsq(receiverToCasterLightSpace.z) > zSqAtSphereIntersection)
                return true;

            const float cascadeBlendCullingFactor = 1.0f;

            // check if the caster capsule is fully contained within the "core" sphere
            // (it is sufficient to test that only the capsule start and end spheres are within the "core" receiver sphere)
            float coreRadius = receiverRadius * cascadeBlendCullingFactor;
            float3 receiverToShadowEndLightSpace = receiverToCasterLightSpace + new float3(0.0f, 0.0f, shadowLength);
            float capsuleMaxDistance = coreRadius - casterRadius;
            float capsuleDistanceSq = math.max(math.lengthsq(receiverToCasterLightSpace), math.lengthsq(receiverToShadowEndLightSpace));
            if (capsuleMaxDistance > 0.0f && capsuleDistanceSq < math.lengthsq(capsuleMaxDistance))
                inside = true;

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float DistanceUntilCylinderFullyCrossesPlane(float3 cylinderCenter, float3 cylinderDirection, float cylinderRadius, Plane plane)
        {
            const float cosEpsilon = 0.001f; // clamp the cosine of glancing angles

            // compute the distance until the center intersects the plane
            float cosTheta = math.max(math.abs(math.dot(plane.Normal, cylinderDirection)), cosEpsilon);
            float heightAbovePlane = math.dot(plane.Normal, cylinderCenter) + plane.Distance;
            float centerDistanceToPlane = heightAbovePlane / cosTheta;

            // compute the additional distance until the edge of the cylinder intersects the plane
            float sinTheta = math.sqrt(math.max(1.0f - cosTheta * cosTheta, 0.0f));
            float edgeDistanceToPlane = cylinderRadius * sinTheta / cosTheta;

            return centerDistanceToPlane + edgeDistanceToPlane;
        }
    }
}
