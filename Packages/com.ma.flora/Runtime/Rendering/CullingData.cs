// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace MA.Flora.Rendering
{
    [BurstCompile]
    struct CombineUnbuiltBoundsJob : IJob
    {
        [ReadOnly] public NativeArray<AxisAlignedBox> UnbuiltInstanceBounds;
        public NativeArray<AxisAlignedBox> CombinedUnbuiltInstanceBounds;

        public void Execute()
        {
            CombinedUnbuiltInstanceBounds[0] = AxisAlignedBox.Empty;
            for (int i = 0; i < UnbuiltInstanceBounds.Length; i++)
                CombinedUnbuiltInstanceBounds[0] += UnbuiltInstanceBounds[i];
        }
    }

    [Serializable]
    sealed class CullingData : IDisposable, ISerializationCallbackReceiver
    {
        enum Version { Initial = 1, SerializeNodesAsBytes = 2, Latest = SerializeNodesAsBytes }

        [SerializeField] Version m_Version = Version.Latest;
        [SerializeField] List<int> m_RenderIndexLookup = new List<int>(16);
        [SerializeField] List<int> m_InstanceIndexLookup = new List<int>(16);

        [SerializeField] uint m_DensitySeed;
        [FormerlySerializedAs("m_BuiltDensity")] [SerializeField] float m_Density = 1.0f;

        [NonSerialized] List<CullingNode> m_BuiltNodes = new List<CullingNode>(16);
        [SerializeField] AxisAlignedBox m_BuiltBounds = AxisAlignedBox.Empty;
        [SerializeField] int m_BuiltInstanceCount;
        [SerializeField] int m_BuiltRenderInstanceCount;
        [SerializeField] int m_BuiltOcclusionLayerCount;
        [SerializeField] AxisAlignedBox m_BuiltPrototypeBounds = AxisAlignedBox.Empty;
        [SerializeField] CullingTreeSettings m_BuildSettings = CullingTreeSettings.Default;
        [SerializeField] float m_BuiltAverageInstanceScale = 1;
        [SerializeField] int m_BuiltVersion = 1;

        [NonSerialized] bool m_IsInitialized;
        [NonSerialized] int m_InstanceCountToRender;
        [NonSerialized] bool m_OutOfDate;

        [NonSerialized] JobHandle m_BuildJobHandle;
        [NonSerialized] CullingTreeBuildResult m_TreeBuildResult;

        [NonSerialized] List<AxisAlignedBox> m_UnbuiltInstanceBounds = new List<AxisAlignedBox>(16);
        [NonSerialized] AxisAlignedBox m_UnbuiltInstanceBoundsCombined = AxisAlignedBox.Empty;
        [NonSerialized] IInstancedRenderer m_Renderer;

        // --- Byte Serialization ---

        [SerializeField] byte[] m_SerializedNodeBytes = Array.Empty<byte>();

        // --- Lifecycle ---

        public void Initialize(IInstancedRenderer renderer)
        {
            if (m_IsInitialized)
                return;

            m_IsInitialized = true;
            m_Renderer = renderer;
            InstancingSceneSettings.GlobalSceneSettingsChanged += UpdateDensity;
            CanEnableDensityScalingChanged += UpdateDensity;

            if (m_Version != Version.Latest)
            {
                m_Version = Version.Latest;
                ClearData();
            }
        }

        public void Dispose()
        {
            if (!m_IsInitialized)
                return;

            m_IsInitialized = false;
            CanEnableDensityScalingChanged -= UpdateDensity;
            InstancingSceneSettings.GlobalSceneSettingsChanged -= UpdateDensity;
            CancelTreeBuildAsync();
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            UpdateTreeBuild(forceComplete: true);
            SerializationHelpers.SerializeListToBytes(m_BuiltNodes, ref m_SerializedNodeBytes);
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (m_Version >= Version.SerializeNodesAsBytes)
            {
                SerializationHelpers.DeserializeBytesToList(ref m_SerializedNodeBytes, m_BuiltNodes);
            }

            m_InstanceCountToRender = m_BuiltRenderInstanceCount;
            m_UnbuiltInstanceBounds.Clear();
            m_UnbuiltInstanceBoundsCombined = AxisAlignedBox.Empty;
        }

        // --- Density ---

        static event Action CanEnableDensityScalingChanged;
        internal static bool CanEnableDensityScaling
        {
            get => s_CanEnableDensityScaling;
            set
            {
                if (value != s_CanEnableDensityScaling)
                {
                    s_CanEnableDensityScaling = value;
                    CanEnableDensityScalingChanged?.Invoke();
                }
            }
        }
        static bool s_CanEnableDensityScaling = true;

        bool IsAffectedByGlobalDensity()
        {
            if (m_Renderer == null || m_Renderer.Transform == null)
                return false;

            if (!s_CanEnableDensityScaling || !m_Renderer.Prototype || !m_Renderer.Prototype.AffectedByGlobalInstanceDensity)
                return false;

            GameObject gameObject = m_Renderer.Transform.gameObject;
            if (gameObject == null)
                return false;

#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(gameObject) ||
                UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null)
                return false;
#endif

            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                if (s_CanEnableDensityScaling)
                    return true;
            }

            return false;
        }

        void UpdateDensity()
        {
            if (IsAffectedByGlobalDensity())
                OutOfDate = m_Density != InstancingSceneSettings.Global.GlobalInstanceDensity;
            else
                OutOfDate = m_Density != 1.0f;

            if (OutOfDate)
                m_Renderer.MarkRenderStateDirty();
        }

        float ComputeDensityForBuild()
        {
            if (!IsAffectedByGlobalDensity())
                return 1.0f;

            if (m_DensitySeed == 0)
                m_DensitySeed = (uint)Random.Range(1, int.MaxValue);

            return math.clamp(InstancingSceneSettings.Global.GlobalInstanceDensity, 0.0f, 1.0f);
        }

        // --- Events ---

        public delegate void TreeBuiltDelegate(CullingData tree);
        public event TreeBuiltDelegate TreeBuilt;

        // --- Render Data ---

        public List<int> RenderIndexLookup => m_RenderIndexLookup;

        public List<int> InstanceIndexLookup => m_InstanceIndexLookup;

        public int InstanceCountToRender => m_InstanceCountToRender;

        public List<AxisAlignedBox> UnbuiltInstanceBounds => m_UnbuiltInstanceBounds;

        public ref AxisAlignedBox UnbuiltInstanceBoundsCombined => ref m_UnbuiltInstanceBoundsCombined;

        public IInstancedRenderer Renderer => m_Renderer;

        // --- Tree Build Settings ---

        public uint DensitySeed
        {
            get => m_DensitySeed;
            set
            {
                if (value != m_DensitySeed)
                {
                    m_DensitySeed = math.max(value, 1);
                    OutOfDate = true;
                }
            }
        }

        // --- Built Tree Data (Valid after a successful build) ---

        public float Density => m_Density;

        public CullingTreeSettings BuildSettings => m_BuildSettings;

        public ReadOnlySpan<CullingNode> BuiltNodes => m_BuiltNodes.AsReadOnlySpan();

        public AxisAlignedBox BuiltBounds => m_BuiltBounds;

        public int BuiltInstanceCount => m_BuiltInstanceCount;

        public int BuiltRenderInstanceCount => m_BuiltRenderInstanceCount;

        public int BuiltOcclusionLayerCount => m_BuiltOcclusionLayerCount;

        public AxisAlignedBox BuiltPrototypeBounds => m_BuiltPrototypeBounds;

        public float BuiltAverageInstanceScale => m_BuiltAverageInstanceScale;

        public int BuiltVersion => m_BuiltVersion;

        public bool IsBuilt => m_BuiltNodes.Count > 0 && m_BuiltInstanceCount > 0;

        public bool OutOfDate
        {
            get => m_OutOfDate;
            set
            {
                if (value != m_OutOfDate)
                {
                    m_OutOfDate = value;

                    // Check if we have changes during an async build
                    if (value && IsBuilding)
                        HasConcurrentChanges = true;
                }
            }
        }

        public bool AutoBuildEnabled { get; set; } = true;

        public bool HasConcurrentChanges { get; private set; }

        public bool IsBuilding { get; private set; }

        // --- Render Index Table ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetInstanceIndexByRenderIndex(int renderIndex)
            => m_InstanceIndexLookup.IsValidIndex(renderIndex) ? m_InstanceIndexLookup[renderIndex] : -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetRenderIndex(int instanceIndex)
            => m_RenderIndexLookup.IsValidIndex(instanceIndex) ? m_RenderIndexLookup[instanceIndex] : instanceIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddUnbuiltInstance(int instanceIndex, in AxisAlignedBox instanceBounds)
        {
            int startRenderIndex = m_InstanceCountToRender - m_RenderIndexLookup.Count;
            m_RenderIndexLookup.Add(startRenderIndex + instanceIndex);
            m_InstanceCountToRender++;

            m_UnbuiltInstanceBounds.Add(instanceBounds);
            m_UnbuiltInstanceBoundsCombined += instanceBounds;
            m_OutOfDate = true;
        }

        public void AddUnbuiltInstances(int startInstanceIndex, ReadOnlySpan<AxisAlignedBox> instanceBounds)
        {
            m_RenderIndexLookup.Reserve(startInstanceIndex + instanceBounds.Length);

            int startRenderIndex = m_InstanceCountToRender - m_RenderIndexLookup.Count;
            for (int i = 0; i < instanceBounds.Length; i++)
            {
                m_RenderIndexLookup.Add(startRenderIndex + startInstanceIndex + i);
                m_UnbuiltInstanceBounds.Add(instanceBounds[i]);
                m_UnbuiltInstanceBoundsCombined += instanceBounds[i];
            }

            m_InstanceCountToRender += instanceBounds.Length;
            m_OutOfDate = true;
        }

        public void AddUnbuiltIndices(int startInstanceIndex, int instanceCount)
        {
            m_RenderIndexLookup.Reserve(startInstanceIndex + instanceCount);

            int startRenderIndex = m_InstanceCountToRender - m_RenderIndexLookup.Count;
            for (int i = 0; i < instanceCount; i++)
                m_RenderIndexLookup.Add(startRenderIndex + startInstanceIndex + i);

            m_InstanceCountToRender += instanceCount;
            m_OutOfDate = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool UpdateInstanceBounds(int instanceIndex, in LocalTransform oldLocalTransform, in LocalTransform newLocalTransform)
        {
            bool positionChanged = !newLocalTransform.Position.NearlyEquals(oldLocalTransform.Position);

            if (m_RenderIndexLookup.IsValidIndex(instanceIndex))
            {
                int renderIndex = GetRenderIndex(instanceIndex);
                bool isOmittedInstance = renderIndex == -1;
                bool isBuiltInstance = !isOmittedInstance && renderIndex < m_BuiltRenderInstanceCount;
                bool doInPlaceUpdate = isBuiltInstance && !positionChanged;

                AxisAlignedBox newInstanceBounds = m_BuiltPrototypeBounds.TransformBy(newLocalTransform);
                if (doInPlaceUpdate)
                {
                    AxisAlignedBox oldInstanceBounds = m_BuiltPrototypeBounds.TransformBy(oldLocalTransform);
                    if (!oldInstanceBounds.IsInside(newInstanceBounds))
                        m_BuiltBounds += newInstanceBounds;
                }
                else
                {
                    m_UnbuiltInstanceBounds.Add(newInstanceBounds);
                    m_UnbuiltInstanceBoundsCombined += newInstanceBounds;
                    m_OutOfDate = true;
                }
            }

            return positionChanged;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveInstanceSwapBack(int instanceToRemove, int lastInstanceIndex)
        {
            if (m_RenderIndexLookup.IsValidIndex(instanceToRemove))
            {
                int renderIndexToRemove = m_RenderIndexLookup[instanceToRemove];
                int renderIndexToMove = m_RenderIndexLookup[lastInstanceIndex];

                m_RenderIndexLookup[instanceToRemove] = renderIndexToMove;
                m_RenderIndexLookup[lastInstanceIndex] = RenderIndexUtility.PackRenderFlags(renderIndexToRemove, RenderIndexFlags.Destroyed);

                m_OutOfDate = true;
            }
        }

        // --- Tree Building ---

        public bool TryBuildTree(bool async = true, bool force = false)
        {
            if (m_Renderer == null)
                return false;

            AxisAlignedBox prototypeBounds = m_Renderer.Prototype?.Bounds ?? AxisAlignedBox.Empty;
            CullingTreeSettings settings = m_Renderer.Prototype?.CullingTreeSettings ?? CullingTreeSettings.Default;
            float buildDensity = ComputeDensityForBuild();

            if (force
                || m_OutOfDate
                || m_RenderIndexLookup.Count != m_Renderer.InstanceCount
                || m_BuiltInstanceCount != m_Renderer.InstanceCount
                || m_BuiltPrototypeBounds != prototypeBounds
                || m_UnbuiltInstanceBounds.Count > 0
                || !MathUtility.NearlyEquals(m_Density, buildDensity)
                || m_BuildSettings != settings)
            {
                if (m_Renderer.InstanceCount == 0 || !m_Renderer.Prototype || prototypeBounds.IsEmpty)
                {
                    // Debug.Log("InstanceTree: No instances to build.");
                    ClearData();
                    return true;
                }
                else
                {
                    // Mark as out of date so the async build can complete
                    OutOfDate = true;

                    if (async)
                    {
                        if (IsBuilding)
                        {
                            // Invalidate the results of the current async build, since we need to build again
                            // Debug.Log("InstanceTree: Concurrent changes during async build.");
                            HasConcurrentChanges = true;
                        }
                        else
                        {
                            BuildTreeAsync(buildDensity);
                            return true;
                        }
                    }
                    else
                    {
                        BuildTreeSync(buildDensity);
                        return true;
                    }
                }
            }

            return false;
        }

        public bool CancelTreeBuildAsync()
        {
            if (IsBuilding)
            {
                m_BuildJobHandle.Complete();
                m_TreeBuildResult.Dispose();
                m_TreeBuildResult = default;
                IsBuilding = false;
                HasConcurrentChanges = false;
                return true;
            }

            return false;
        }

        public void ClearData()
        {
            CancelTreeBuildAsync();
            OutOfDate = false;

            m_BuiltNodes.Clear();
            m_RenderIndexLookup.Clear();
            m_InstanceIndexLookup.Clear();
            m_Density = InstancingSceneSettings.Global.GlobalInstanceDensity;
            m_BuildSettings = m_Renderer.Prototype?.CullingTreeSettings ?? CullingTreeSettings.Default;
            m_BuiltInstanceCount = 0;
            m_BuiltRenderInstanceCount = 0;
            m_BuiltBounds = AxisAlignedBox.Empty;
            m_BuiltPrototypeBounds = m_Renderer.Prototype?.Bounds ?? AxisAlignedBox.Empty;
            m_BuiltOcclusionLayerCount = 0;
            m_UnbuiltInstanceBounds.Clear();
            m_UnbuiltInstanceBoundsCombined = AxisAlignedBox.Empty;
            m_BuiltVersion++;
            m_InstanceCountToRender = 0;

            TreeBuilt?.Invoke(this);
        }

        public bool UpdateTreeBuild(bool forceComplete = false)
        {
            bool isComplete = !IsBuilding;
            if (!isComplete)
            {
                if (forceComplete || m_BuildJobHandle.IsCompleted)
                {
                    TryApplyTreeAsyncResult();
                    isComplete = !IsBuilding;
                }
            }

            return isComplete;
        }

        public AxisAlignedBox CalculateBounds(Space space)
        {
            AxisAlignedBox bounds = m_BuiltBounds + m_UnbuiltInstanceBoundsCombined;
            return space == Space.Self ? bounds : bounds.TransformBy(m_Renderer.Transform.localToWorldMatrix);
        }

        CullingTreeBuildJob CreateBuildJob(in NativeArray<LocalTransform> transforms)
        {
            InstancedPrototype prototype = m_Renderer.Prototype;
            CullingTreeSettings settings = prototype.CullingTreeSettings;

            return new CullingTreeBuildJob
            {
                InstanceTransforms = transforms,
                InstanceBounds = prototype.Bounds,
                DensityRandomSeed = m_DensitySeed,
                SplitFactor = settings.BranchingFactor,
                MaxInstancesPerLeaf = settings.CalculateMaxInstancesPerLeafNode(prototype),
                MinOcclusionQueries = settings.MinOcclusionQueries,
                MaxOcclusionQueries = settings.MaxOcclusionQueries,
                MinInstancesPerOcclusionQuery = settings.MinInstancesPerOcclusionQuery,
                Result = m_TreeBuildResult
            };
        }

        void BuildTreeSync(float density)
        {
            m_BuildJobHandle.Complete();
            m_TreeBuildResult.Dispose();

            using NativeArray<LocalTransform> transforms = m_Renderer.InstanceTransforms.ToNativeArray(Allocator.TempJob);
            m_TreeBuildResult = new CullingTreeBuildResult(transforms.Length, density, Allocator.TempJob);
            CreateBuildJob(transforms).Run();
            ApplyTreeBuildResult();

            m_TreeBuildResult.Dispose();
            m_TreeBuildResult = default;
        }

        void BuildTreeAsync(float density)
        {
            m_BuildJobHandle.Complete();
            m_TreeBuildResult.Dispose();

            NativeArray<LocalTransform> transforms = m_Renderer.InstanceTransforms.ToNativeArray(Allocator.Persistent);
            m_TreeBuildResult = new CullingTreeBuildResult(transforms.Length, density, Allocator.Persistent);

            JobHandle asyncJob = CreateBuildJob(transforms).Schedule();
            asyncJob = transforms.Dispose(asyncJob);

            m_BuildJobHandle = asyncJob;
            IsBuilding = true;
        }

        void TryApplyTreeAsyncResult()
        {
            IsBuilding = false;

            if (!OutOfDate)
            {
                // We did a sync build while async building.
                // The sync build is newer so we will use that.
                HasConcurrentChanges = false;
            }
            else if (HasConcurrentChanges)
            {
                // There were changes while we were building, build again
                HasConcurrentChanges = false;
                BuildTreeAsync(m_TreeBuildResult.Density);
            }
            else
            {
                ApplyTreeBuildResult();
            }
        }

        void ApplyTreeBuildResult()
        {
            OutOfDate = false;
            m_BuildJobHandle.Complete();

            m_BuiltNodes.CopyFrom(m_TreeBuildResult.Nodes.AsArray());
            m_RenderIndexLookup.CopyFrom(m_TreeBuildResult.RemappedInstances.AsArray());
            m_InstanceIndexLookup.CopyFrom(m_TreeBuildResult.SortedIndices.AsArray());

            m_BuildSettings = m_Renderer.Prototype.CullingTreeSettings;
            m_Density = m_TreeBuildResult.Density;
            m_BuiltInstanceCount = m_TreeBuildResult.RemappedInstances.Length;
            m_BuiltRenderInstanceCount = m_TreeBuildResult.SortedIndices.Length;
            m_BuiltBounds = m_BuiltNodes.Count > 0 ? new AxisAlignedBox(m_BuiltNodes[0].Bounds.Min, m_BuiltNodes[0].Bounds.Max) : AxisAlignedBox.Empty;
            m_BuiltOcclusionLayerCount = m_TreeBuildResult.OcclusionLayerCount.Value;
            m_BuiltPrototypeBounds = m_Renderer.Prototype.Bounds;
            m_UnbuiltInstanceBounds.Clear();
            m_UnbuiltInstanceBoundsCombined = AxisAlignedBox.Empty;
            m_BuiltVersion++;
            m_InstanceCountToRender = m_BuiltRenderInstanceCount;

            m_TreeBuildResult.Dispose();
            m_TreeBuildResult = default;

            TreeBuilt?.Invoke(this);
        }

        // --- Tree Helpers ---

        internal bool OverlapsRay(Ray localRay, out int hitNodeIndex)
        {
            hitNodeIndex = -1;
            if (!IsBuilt)
                return false;

            if (!m_BuiltBounds.Overlaps(localRay))
                return false;

            OverlapRayRecursive(0, localRay, ref hitNodeIndex);
            return hitNodeIndex >= 0;
        }

        void OverlapRayRecursive(int nodeIndex, Ray localRay, ref int hitNodeIndex)
        {
            if (hitNodeIndex > 0)
                return;

            CullingNode node = m_BuiltNodes[nodeIndex];
            if (node.Bounds.Overlaps(localRay))
            {
                if (node.IsLeaf)
                {
                    hitNodeIndex = nodeIndex;
                }
                else
                {
                    for (int childIndex = node.FirstChild; childIndex <= node.LastChild; ++childIndex)
                        OverlapRayRecursive(childIndex, localRay, ref hitNodeIndex);
                }
            }
        }
    }
}
