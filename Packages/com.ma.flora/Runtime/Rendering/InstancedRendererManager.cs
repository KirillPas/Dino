// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MA.Flora.Rendering
{
    [DebuggerTypeProxy(typeof(InstancedRendererIDDebugView))]
    struct InstancedRendererID : IEquatable<InstancedRendererID>, IComparable<InstancedRendererID>
    {
        public static readonly InstancedRendererID Null = new InstancedRendererID { Value = 0 };

        public int Value;

        public bool IsCreated { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Value > 0; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedRendererID(int value) => Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Value.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedRendererID other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedRendererID other) => (int) Value == (int) other.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedRendererID other) => Value.CompareTo(other.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(InstancedRendererID id) => id.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedRendererID a, InstancedRendererID b) => a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedRendererID a, InstancedRendererID b) => !a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            if (InstancingSystem.IsActive() && IsCreated && InstancingSystem.Instance.Context.RendererManager.Exists(this))
                return InstancingSystem.Instance.Context.RendererManager.GameObjects[this].name;

            return Value == 0 ? $"{nameof(InstancedRendererID)}.Null" : Value.ToString();
        }
    }

    static class InstancedRendererIDHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsValid(this InstancedRendererID id)
            => id.IsCreated && InstancingSystem.IsActive() && InstancingSystem.Instance.Context.RendererManager.Exists(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetInstanceCount(this InstancedRendererID id)
            => IsValid(id) ? InstancingSystem.Instance.Context.RendererManager.Data.Culling[id].InstanceCount : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AxisAlignedBox GetLocalBounds(this InstancedRendererID id)
            => IsValid(id) ? InstancingSystem.Instance.Context.RendererManager.Data.LocalBounds[id] : AxisAlignedBox.Empty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AxisAlignedBox GetWorldBounds(this InstancedRendererID id)
            => IsValid(id) ? InstancingSystem.Instance.Context.RendererManager.Data.WorldBounds[id] : AxisAlignedBox.Empty;
    }

    struct InstanceRendererData
    {
        public byte Layer;
        public float CullingDistance;
        public float ShadowDistance;
        public float StreamingDistance;
        public float LODAverageWorldSpaceSize;

        public InstancedBatchID BatchID;
        public InstancedPrototypeID PrototypeID;
        public int PrototypeVersion;

        public int InstanceCount;
        public int InstanceCountToRender;
        public int AttributeCount;
        public int InstancesVersion;

        public int Version;
        public int EnabledVersion;

        public bool AllSelected;
        public bool InstancesSelected;
        public ulong SceneCullingMask;
    }

    struct InstanceRendererTreeData
    {
        public int Version;
        public int Count;
        public int Offset;
        public int MinimumVerticesPerCluster;
        public int UnbuiltCount;
        public int UnbuiltOffset;
        public int FirstUnbuiltIndex;
        public AxisAlignedBox UnbuiltBoundsCombined;
    }

    struct InstanceRendererOcclusionData
    {
        public bool Enabled => Count > 0;
        public int Count;
        public int Offset;
        public int FirstNode;
        public int LastNode;
    }

    struct InstanceRendererLightProbeData
    {
        public bool SampleLightProbes;
        public float3 SampleLightProbesOffset;
    }

    struct InstancedRendererArrays : IDisposable
    {
        UnsafeArray<int> m_CountCapacity;
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_CountCapacity[0];
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => m_CountCapacity[0] = value;
        }

        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]get => m_CountCapacity[1];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]set => m_CountCapacity[1] = value;
        }

        public UnsafeArray<int> InstanceID;

        public UnsafeArray<LocalTransform> LocalToWorld;
        public UnsafeArray<float4x4> LocalToWorldMatrix;
        public UnsafeArray<float4x4> WorldToLocalMatrix;

        public UnsafeArray<AxisAlignedBox> LocalBounds;
        public UnsafeArray<AxisAlignedBox> WorldBounds;

        public UnsafeBitList InRange;
        public UnsafeBitList InRangeLastFrame;
        public UnsafeArray<int> LastFrameInRange;

        public UnsafeBitList IsValid;
        public UnsafeBitList IsLoaded;
        public UnsafeBitList IsVisibleInScene;
        public UnsafeBitList IsRenderStateDirty;
        public UnsafeBitList IsBuildingTree;
        public UnsafeBitList HasShadowCasters;

        public UnsafeArray<InstanceRendererData> Culling;
        public UnsafeArray<InstanceRendererTreeData> Tree;
        public UnsafeArray<InstanceRendererOcclusionData> Occlusion;
        public UnsafeArray<InstanceRendererLightProbeData> LightProbe;

        public UnsafeArray<InstancedBatchDescriptor> BatchDescription;
        public UnsafeArray<BufferAllocation> BatchAllocation;

        public NativeArray<CullingNode> NodeStore;
        public NativeArray<AxisAlignedBox> UnbuiltBoundsStore;

        public InstancedRendererArrays(int capacity)
        {
            m_CountCapacity = new UnsafeArray<int>(2, AllocatorManager.Persistent);
            InstanceID = new UnsafeArray<int>(capacity, AllocatorManager.Persistent);

            LocalToWorld = new UnsafeArray<LocalTransform>(capacity, AllocatorManager.Persistent);
            LocalToWorldMatrix = new UnsafeArray<float4x4>(capacity, AllocatorManager.Persistent);
            WorldToLocalMatrix = new UnsafeArray<float4x4>(capacity, AllocatorManager.Persistent);

            LocalBounds = new UnsafeArray<AxisAlignedBox>(capacity, AllocatorManager.Persistent);
            WorldBounds = new UnsafeArray<AxisAlignedBox>(capacity, AllocatorManager.Persistent);

            InRange = new UnsafeBitList(capacity, AllocatorManager.Persistent);
            InRangeLastFrame = new UnsafeBitList(capacity, AllocatorManager.Persistent);
            LastFrameInRange = new UnsafeArray<int>(capacity, AllocatorManager.Persistent);

            IsValid = new UnsafeBitList(capacity, AllocatorManager.Persistent);
            IsLoaded = new UnsafeBitList(capacity, AllocatorManager.Persistent);
            IsVisibleInScene = new UnsafeBitList(capacity, AllocatorManager.Persistent);
            IsRenderStateDirty = new UnsafeBitList(capacity, AllocatorManager.Persistent);
            IsBuildingTree = new UnsafeBitList(capacity, AllocatorManager.Persistent);
            HasShadowCasters = new UnsafeBitList(capacity, AllocatorManager.Persistent);

            BatchDescription = new UnsafeArray<InstancedBatchDescriptor>(capacity, AllocatorManager.Persistent);
            Culling = new UnsafeArray<InstanceRendererData>(capacity, AllocatorManager.Persistent);
            Tree = new UnsafeArray<InstanceRendererTreeData>(capacity, AllocatorManager.Persistent);
            Occlusion = new UnsafeArray<InstanceRendererOcclusionData>(capacity, AllocatorManager.Persistent);
            LightProbe = new UnsafeArray<InstanceRendererLightProbeData>(capacity, AllocatorManager.Persistent);
            BatchAllocation = new UnsafeArray<BufferAllocation>(capacity, AllocatorManager.Persistent);

            NodeStore = new NativeArray<CullingNode>(capacity, Allocator.Persistent);
            UnbuiltBoundsStore = new NativeArray<AxisAlignedBox>(capacity, Allocator.Persistent);

            Capacity = capacity;
        }

        public void Dispose()
        {
            m_CountCapacity.Dispose();
            InstanceID.Dispose();

            LocalToWorld.Dispose();
            LocalToWorldMatrix.Dispose();
            WorldToLocalMatrix.Dispose();

            LocalBounds.Dispose();
            WorldBounds.Dispose();

            InRange.Dispose();
            InRangeLastFrame.Dispose();
            LastFrameInRange.Dispose();

            IsValid.Dispose();
            IsLoaded.Dispose();
            IsVisibleInScene.Dispose();
            IsRenderStateDirty.Dispose();
            IsBuildingTree.Dispose();
            HasShadowCasters.Dispose();

            BatchDescription.Dispose();
            Culling.Dispose();
            Tree.Dispose();
            Occlusion.Dispose();
            LightProbe.Dispose();
            BatchAllocation.Dispose();

            NodeStore.Dispose();
            UnbuiltBoundsStore.Dispose();
        }

        public void EnsureCapacity(int newCapacity)
        {
            if (newCapacity > Capacity)
            {
                InstanceID.Resize(newCapacity, AllocatorManager.Persistent);

                LocalToWorld.Resize(newCapacity, AllocatorManager.Persistent);
                LocalToWorldMatrix.Resize(newCapacity, AllocatorManager.Persistent);
                WorldToLocalMatrix.Resize(newCapacity, AllocatorManager.Persistent);

                LocalBounds.Resize(newCapacity, AllocatorManager.Persistent);
                WorldBounds.Resize(newCapacity, AllocatorManager.Persistent);

                InRange.Resize(newCapacity);
                InRangeLastFrame.Resize(newCapacity);
                LastFrameInRange.Resize(newCapacity, AllocatorManager.Persistent);

                IsValid.Resize(newCapacity);
                IsLoaded.Resize(newCapacity);
                IsVisibleInScene.Resize(newCapacity);
                IsRenderStateDirty.Resize(newCapacity);
                IsBuildingTree.Resize(newCapacity);
                HasShadowCasters.Resize(newCapacity);

                BatchDescription.Resize(newCapacity, AllocatorManager.Persistent);
                Culling.Resize(newCapacity, AllocatorManager.Persistent);
                Tree.Resize(newCapacity, AllocatorManager.Persistent);
                Occlusion.Resize(newCapacity, AllocatorManager.Persistent);
                LightProbe.Resize(newCapacity, AllocatorManager.Persistent);
                BatchAllocation.Resize(newCapacity, AllocatorManager.Persistent);

                Capacity = newCapacity;
            }
        }
    }

    [BurstCompile]
    sealed class InstancedRendererManager : IDisposable
    {
        public int Count { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Data.Count; }
        public int Capacity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Data.Capacity; }

        public InstancingContext Context;
        public InstancedRendererArrays Data;

        SlotAllocator m_IDAllocator;
        ElementAllocator m_NodeAllocator;
        ElementAllocator m_TreeUnbuiltAllocator;

        public Transform[] Transforms;
        public GameObject[] GameObjects;
        public Scene[] Scenes;
        public IInstancedRenderer[] Renderers;
        public bool[] IsComponent;
#if UNITY_EDITOR
        public IInstancedRendererEditorData[] EditorRenderers;
        public HashSet<InstancedMeshContainer> SelectedContainers;
#endif
        public InstancedMeshContainer[] Containers;
        public JobHandle UpdateJobHandle;

        Dictionary<int, InstancedRendererID> m_InstanceIDHash;

        public UnsafeIndirectList<InstancedRendererID> Valid;
        public UnsafeIndirectList<InstancedRendererID> Loaded;
        public UnsafeIndirectList<InstancedRendererID> Dirty;
        public UnsafeIndirectList<InstancedRendererID> TreeBuildsInProgress;
        public UnsafeIndirectList<InstancedRendererID> RequiringLoad;
        public UnsafeIndirectList<InstancedRendererID> RequiringUnload;

        public InstancedRendererManager(InstancingContext context, int capacity)
        {
            Context = context;
            Context.PrototypeManager.PrototypeUpdated += OnPrototypeUpdated;

            m_IDAllocator = new SlotAllocator(capacity, AllocatorManager.Persistent);
            m_IDAllocator.Allocate(); // Null slot

            m_NodeAllocator = new ElementAllocator(capacity, AllocatorManager.Persistent);
            m_TreeUnbuiltAllocator = new ElementAllocator(capacity, AllocatorManager.Persistent);

            Data = new InstancedRendererArrays(capacity);
            Transforms = new Transform[capacity];
            GameObjects = new GameObject[capacity];
            Scenes = new Scene[capacity];
            Renderers = new IInstancedRenderer[capacity];
            IsComponent = new bool[capacity];
            Containers = new InstancedMeshContainer[capacity];
            m_InstanceIDHash = new Dictionary<int, InstancedRendererID> { { 0, InstancedRendererID.Null } };

            Valid = new UnsafeIndirectList<InstancedRendererID>(capacity, AllocatorManager.Persistent);
            Loaded = new UnsafeIndirectList<InstancedRendererID>(capacity, AllocatorManager.Persistent);
            Dirty = new UnsafeIndirectList<InstancedRendererID>(capacity, AllocatorManager.Persistent);
            TreeBuildsInProgress = new UnsafeIndirectList<InstancedRendererID>(capacity, AllocatorManager.Persistent);
            RequiringLoad = new UnsafeIndirectList<InstancedRendererID>(capacity, AllocatorManager.Persistent);
            RequiringUnload = new UnsafeIndirectList<InstancedRendererID>(capacity, AllocatorManager.Persistent);

#if UNITY_EDITOR
            EditorRenderers = new IInstancedRendererEditorData[capacity];
            SelectedContainers = new HashSet<InstancedMeshContainer>();
            UnityEditor.SceneVisibilityManager.visibilityChanged += OnSceneVisibilityChanged;
            UnityEditor.SceneManagement.PrefabStage.prefabStageClosing += OnPrefabStageClosing;
#endif
        }

        public void Dispose()
        {
            UpdateJobHandle.Complete(); // Ensure all jobs are complete before disposing

            Context.PrototypeManager.PrototypeUpdated -= OnPrototypeUpdated;

#if UNITY_EDITOR
            UnityEditor.SceneManagement.PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            UnityEditor.SceneVisibilityManager.visibilityChanged -= OnSceneVisibilityChanged;
#endif

            m_IDAllocator.Dispose();

            m_NodeAllocator.Dispose();
            m_TreeUnbuiltAllocator.Dispose();

            Data.Dispose();

            Valid.Dispose();
            Loaded.Dispose();
            Dirty.Dispose();
            TreeBuildsInProgress.Dispose();
            RequiringLoad.Dispose();
            RequiringUnload.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Exists(InstancedRendererID id) => id > 0 && m_IDAllocator.Exists(id);

#if UNITY_EDITOR
        internal bool HasSelection
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => SelectedContainers.Count > 0;
        }

        void OnPrefabStageClosing(PrefabStage stage)
        {
            for (int i = 0; i < Valid.Length; i++)
            {
                InstancedRendererID id = Valid[i];
                if (Exists(id))
                {
                    Data.Culling[id].SceneCullingMask = GameObjects[id].sceneCullingMask;
                }
            }
        }

        void OnSceneVisibilityChanged()
        {
            for (int i = 0; i < Valid.Length; i++)
            {
                InstancedRendererID id = Valid[i];
                if (Exists(id))
                {
                    GameObject gameObject = GameObjects[id];
                    if (gameObject)
                    {
                        bool isVisible = !UnityEditor.SceneVisibilityManager.instance.IsHidden(gameObject);
                        Data.IsVisibleInScene[id] = isVisible;
                    }
                }
            }
        }
#endif

        void OnPrototypeUpdated(InstancedPrototypeID prototypeID)
        {
            for (int i = 0; i < Valid.Length; i++)
            {
                InstancedRendererID id = Valid[i];
                if (Data.Culling[id].PrototypeID == prototypeID)
                    SetRendererDirty(id);
            }
        }

        public void SetLoaded(InstancedRendererID id, bool loaded)
        {
            if (Data.IsLoaded[id] == loaded || !Exists(id))
                return;

            Data.IsLoaded[id] = loaded;
            if (loaded)
            {
                Loaded.AddSorted(id, GetGroupSorter());
            }
            else
            {
                Loaded.RemoveSwapBack(id);
            }
        }

        public void SetRendererDirty(InstancedRendererID id)
        {
            if (Data.IsRenderStateDirty[id] || !Exists(id))
                return;

            Data.IsRenderStateDirty[id] = true;
            Dirty.Add(id);
        }

        public void AddTreeBuild(InstancedRendererID id)
        {
            if (Data.IsBuildingTree[id])
                return;

            Data.IsBuildingTree[id] = true;
            TreeBuildsInProgress.Add(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ByPrototype GetGroupSorter() => new ByPrototype { Culling = Data.Culling };

        public InstancedRendererID Register(IInstancedRenderer renderer)
        {
            if (renderer == null)
                return InstancedRendererID.Null;

            int instanceID = renderer.GetHashCode();
            if (instanceID == 0)
                return InstancedRendererID.Null;

            if (m_InstanceIDHash.TryGetValue(instanceID, out InstancedRendererID id))
                return id;

            id = new InstancedRendererID { Value = m_IDAllocator.Allocate() };
            if (m_IDAllocator.MaxAllocatedSlot >= Data.Capacity)
            {
                int newCapacity = math.ceilpow2(m_IDAllocator.MaxAllocatedSlot + 1);
                Data.EnsureCapacity(newCapacity);
                Array.Resize(ref Transforms, newCapacity);
                Array.Resize(ref GameObjects, newCapacity);
                Array.Resize(ref Scenes, newCapacity);
                Array.Resize(ref Renderers, newCapacity);
                Array.Resize(ref IsComponent, newCapacity);
#if UNITY_EDITOR
                Array.Resize(ref EditorRenderers, newCapacity);
#endif
                Array.Resize(ref Containers, newCapacity);

                RequiringUnload.Reserve(newCapacity);
                RequiringLoad.Reserve(newCapacity);
            }

            Transforms[id] = renderer.Transform;
            GameObjects[id] = Transforms[id].gameObject;
            Scenes[id] = GameObjects[id].scene;

            Renderers[id] = renderer;
            IsComponent[id] = false;
            if (renderer is InstancedMeshContainer component)
            {
                IsComponent[id] = true;
                Containers[id] = component;
            }

#if UNITY_EDITOR
            EditorRenderers[id] = renderer as IInstancedRendererEditorData;
#endif

            Data.InstanceID[id] = instanceID;

            Data.LocalToWorld[id] = LocalTransform.Identity;
            Data.LocalToWorldMatrix[id] = float4x4.identity;
            Data.WorldToLocalMatrix[id] = float4x4.identity;
            Data.LocalBounds[id] = AxisAlignedBox.Empty;
            Data.WorldBounds[id] = AxisAlignedBox.Empty;

            Data.InRange[id] = false;
            Data.InRangeLastFrame[id] = false;
            Data.LastFrameInRange[id] = -1;

            Data.IsValid[id] = false;
            Data.IsLoaded[id] = false;
            Data.IsVisibleInScene[id] = true;
            Data.HasShadowCasters[id] = false;

            Data.Culling[id] = default;
            Data.Tree[id] = default;
            Data.Occlusion[id] = default;
            Data.LightProbe[id] = default;
            Data.BatchAllocation[id] = default;
            Data.BatchDescription[id] = default;

#if UNITY_EDITOR
            if (GameObjects[id] && UnityEditor.SceneVisibilityManager.instance.IsHidden(GameObjects[id]))
                Data.IsVisibleInScene[id] = false;
#endif
            m_InstanceIDHash[instanceID] = id;
            SetRendererDirty(id);
            Data.Count++;
            return id;
        }

        public void Unregister(InstancedRendererID id)
        {
            if (!Exists(id))
                return;

            int instanceID = Data.InstanceID[id];
            Context.SceneData.Unload(id);

            m_InstanceIDHash.Remove(instanceID);

            if (Data.IsBuildingTree[id])
            {
                CullingData tree = Renderers[id].CullingData;
                tree.UpdateTreeBuild(true);
                TreeBuildsInProgress.RemoveSwapBack(id);
            }

            ref InstanceRendererData rendererData = ref Data.Culling[id];
            if (Context.SceneData.HasBatch(rendererData.BatchID))
                Context.SceneData.UnregisterBatch(rendererData.BatchID);

            rendererData = default;

            ref InstanceRendererTreeData treeData = ref Data.Tree[id];
            if (treeData.Count > 0)
                m_NodeAllocator.Free(treeData.Offset, treeData.Count);
            if (treeData.UnbuiltCount > 0)
                m_TreeUnbuiltAllocator.Free(treeData.UnbuiltOffset, treeData.UnbuiltCount);
            treeData = default;

            ref InstanceRendererOcclusionData occlusionData = ref Data.Occlusion[id];
            if (occlusionData.Count > 0 && Context.HasStaticOcclusionManager())
            {
                Context.GetStaticOcclusionManager().Free(occlusionData.Offset, occlusionData.Count);
            }
            occlusionData = default;

            Renderers[id] = null;
#if UNITY_EDITOR
            EditorRenderers[id] = null;
            if (IsComponent[id])
                SelectedContainers.Remove(Containers[id]);
#endif
            Transforms[id] = null;
            GameObjects[id] = null;
            Scenes[id] = default;
            Containers[id] = null;

            Data.InstanceID[id] = 0;
            Data.LocalToWorld[id] = LocalTransform.Identity;
            Data.LocalToWorldMatrix[id] = float4x4.identity;
            Data.WorldToLocalMatrix[id] = float4x4.identity;
            Data.LocalBounds[id] = AxisAlignedBox.Empty;
            Data.WorldBounds[id] = AxisAlignedBox.Empty;
            Data.InRange[id] = false;
            Data.InRangeLastFrame[id] = false;
            Data.LastFrameInRange[id] = -1;
            Data.IsValid[id] = false;
            Data.IsLoaded[id] = false;
            Data.IsVisibleInScene[id] = false;
            Data.IsRenderStateDirty[id] = false;
            Data.HasShadowCasters[id] = false;
            Data.BatchDescription[id].Dispose();

            m_IDAllocator.Free(id);
            Valid.RemoveSwapBack(id);
            Loaded.RemoveSwapBack(id);
            Dirty.RemoveSwapBack(id);
            TreeBuildsInProgress.RemoveSwapBack(id);
            RequiringLoad.RemoveSwapBack(id);
            RequiringUnload.RemoveSwapBack(id);

            Data.Count--;
        }

        struct ByPrototype : IComparer<InstancedRendererID>
        {
            public UnsafeArray<InstanceRendererData> Culling;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare(InstancedRendererID rendererA, InstancedRendererID rendererB)
            {
                return Culling[rendererA].PrototypeID.CompareTo(Culling[rendererB].PrototypeID);
            }
        }

        public void NextFrame()
        {
            UpdateJobHandle.Complete(); // Ensure all jobs are complete before updating

            UpdateDirtyClusters();
            UpdateTreeBuilds();

            for (int i = 0; i < RequiringUnload.Length; i++)
            {
                if (Exists(RequiringUnload[i]))
                    Context.SceneData.Unload(RequiringUnload[i]);
            }

            RequiringUnload.Clear();

            for (int i = 0; i < RequiringLoad.Length; i++)
            {
                if (Exists(RequiringLoad[i]))
                    Context.SceneData.RequestUpload(RequiringLoad[i]);
            }

            RequiringLoad.Clear();

            m_TreeUnbuiltAllocator.MergeFree();
            m_NodeAllocator.MergeFree();

#if UNITY_EDITOR
            EditorSpatialHash.Instance.NextFrame();
#endif
        }

        void UpdateDirtyClusters()
        {
            for (int i = Dirty.Length - 1; i >= 0; i--)
            {
                if (Exists(Dirty[i]))
                    Update(Dirty[i]);
            }

            Dirty.Clear();
        }

        void UpdateTreeBuilds()
        {
            for (int i = TreeBuildsInProgress.Length - 1; i >= 0; i--)
            {
                InstancedRendererID id = TreeBuildsInProgress[i];
                bool completed = !Exists(id);
                if (!completed)
                {
                    CullingData tree = Renderers[id].CullingData;
                    if (tree.UpdateTreeBuild())
                    {
                        completed = true;
                        SetRendererDirty(id);
                    }
                }

                if (completed)
                {
                    Data.IsBuildingTree[id] = false;
                    TreeBuildsInProgress.RemoveAtSwapBack(i);
                }
            }
        }

        void Update(InstancedRendererID id)
        {
            IInstancedRenderer renderer = Renderers[id];
            if (!renderer.IsValid || GameObjects[id] == null || (IsComponent[id] && Containers[id] == null))
            {
                Unregister(id);
                return;
            }

            ref InstanceRendererData culling = ref Data.Culling[id];
            culling.Version++;
            culling.InstanceCount = renderer.InstanceCount;
            culling.InstanceCountToRender = renderer.CullingData.InstanceCountToRender;
            culling.InstancesVersion = renderer.InstanceTransformsVersion;

            ref readonly InstancedPrototypeDataArrays prototypeData = ref Context.PrototypeManager.Data;
            InstancedPrototypeID oldPrototypeID = culling.PrototypeID;
            InstancedPrototypeID newPrototypeID = Context.PrototypeManager.Register(renderer.Prototype);

            if (!oldPrototypeID.IsCreated || oldPrototypeID != newPrototypeID)
            {
                if (oldPrototypeID.IsCreated)
                    Context.PrototypeManager.Unregister(oldPrototypeID);

                culling.PrototypeID = newPrototypeID;
                culling.PrototypeVersion = 0;
                culling.CullingDistance = 0;
                culling.StreamingDistance = 0;
                culling.ShadowDistance = 0;

                if (newPrototypeID.IsCreated)
                {
                    Data.HasShadowCasters[id] = prototypeData.Shadow[newPrototypeID].HasShadowCasters;
                    culling.PrototypeVersion = prototypeData.Version[newPrototypeID];

                    ref readonly InstancedPrototypeCullingData cullingData = ref prototypeData.Culling[newPrototypeID];
                    culling.CullingDistance = cullingData.CullingMode == InstancedCullingMode.Override ? cullingData.CullingDistance : 0;
                    if (renderer.CullingDistance > 0)
                        culling.CullingDistance = renderer.CullingDistance;

                    culling.StreamingDistance = cullingData.StreamingMode == InstancedStreamingMode.Override ? cullingData.StreamingDistance : 0;
                    culling.StreamingDistance = math.max(renderer.CullingDistance, cullingData.StreamingDistance);
                    if (renderer.StreamingDistance > 0)
                        culling.StreamingDistance = math.max(culling.CullingDistance, renderer.StreamingDistance);

                    culling.ShadowDistance = prototypeData.Shadow[newPrototypeID].ShadowDistance;
                }
            }

            culling.Layer = newPrototypeID.IsCreated && prototypeData.Culling[newPrototypeID].LayerMask == InstancedLayerMask.FromPrefab
                ? prototypeData.Culling[newPrototypeID].Layer
                : (byte)GameObjects[id].layer;

            Data.IsVisibleInScene[id] = true;

            bool isValid = newPrototypeID.IsCreated;
            bool wasValid = Data.IsValid[id];
            Data.IsValid[id] = isValid;

            bool isLoaded = Data.IsLoaded[id];
            if (isValid != wasValid)
            {
                if (isValid)
                {
                    Valid.AddSorted(id, new ByPrototype { Culling = Data.Culling });
                }
                else
                {
                    Valid.RemoveSwapBack(id);
                    if (isLoaded)
                    {
                        isLoaded = false;
                        Context.SceneData.Unload(id);
                    }
                }
            }

            using InstancedBatchDescriptor batchDescriptor = new InstancedBatchDescriptor(renderer, AllocatorManager.Temp);
            if (Data.BatchDescription[id] != batchDescriptor)
            {
                InstancedBatchID oldBatchID = culling.BatchID;
                InstancedBatchID newBatchID = Context.BatchManager.RegisterBatch(batchDescriptor);
                if (newBatchID != oldBatchID)
                {
                    if (oldBatchID.IsValid)
                    {
                        if (isLoaded) Context.SceneData.Unload(id);
                        Context.BatchManager.UnregisterBatch(oldBatchID);
                        Context.SceneData.UnregisterBatch(oldBatchID);
                    }

                    culling.BatchID = newBatchID;

                    if (newBatchID.IsValid)
                        Context.SceneData.RegisterBatch(newBatchID, batchDescriptor);
                }

                Data.BatchDescription[id].Dispose();
                Data.BatchDescription[id] = new InstancedBatchDescriptor(batchDescriptor, AllocatorManager.Persistent);
            }

            float4x4 localToWorld = Transforms[id].localToWorldMatrix;
            Data.LocalToWorld[id] = LocalTransform.FromMatrix(localToWorld);
            Data.LocalToWorldMatrix[id] = localToWorld;
            Data.WorldToLocalMatrix[id] = math.inverse(localToWorld);

            AxisAlignedBox localBounds = renderer.CalculateBounds(Space.Self);
            Data.LocalBounds[id] = localBounds;
            Data.WorldBounds[id] = localBounds.TransformBy(localToWorld);

            culling.EnabledVersion = renderer.InstancesEnabledVersion;

            // --- Editor Data ---

            if (IsComponent[id])
            {
                RuntimeSpatialHash.Instance.Update(Containers[id], Data.WorldBounds[id]);
#if UNITY_EDITOR
                culling.AllSelected = UnityEditor.Selection.Contains(GameObjects[id]);
                culling.InstancesSelected = culling.AllSelected || Containers[id].HasSelection();

                if (culling.InstancesSelected)
                    SelectedContainers.Add(Containers[id]);
                else
                    SelectedContainers.Remove(Containers[id]);
#endif
            }

#if UNITY_EDITOR
            culling.SceneCullingMask = GameObjects[id].sceneCullingMask;
#endif

            // --- Tree Data ---

            ref InstanceRendererTreeData tree = ref Data.Tree[id];
            int newTreeCount = renderer.CullingData.BuiltNodes.Length;
            if (newTreeCount != tree.Count)
            {
                if (tree.Count > 0)
                    m_NodeAllocator.Free(tree.Offset, tree.Count);

                tree.Count = 0;
                tree.Offset = 0;

                if (newTreeCount > 0)
                {
                    tree.Count = newTreeCount;
                    tree.Offset = m_NodeAllocator.Allocate(tree.Count);
                }
            }

            if (m_NodeAllocator.MaxAllocatedSize > Data.NodeStore.Length)
            {
                UpdateJobHandle.Complete(); // Ensure all jobs are complete before resizing

                int newCapacity = math.max(math.ceilpow2(m_NodeAllocator.MaxAllocatedSize), 256);
                Data.NodeStore.Resize(newCapacity, Allocator.Persistent);
            }

            float localLODSize = isValid ? prototypeData.LOD[newPrototypeID].LocalSize : 0;
            float3 localToWorldScale = localToWorld.Scale();
            if (tree.Count > 0)
            {
                culling.LODAverageWorldSpaceSize = math.cmax(math.abs(localLODSize * localToWorldScale * renderer.CullingData.BuiltAverageInstanceScale));

                NativeArray<CullingNode> src = new NativeArray<CullingNode>(renderer.CullingData.BuiltNodes.Length, Allocator.TempJob);
                src.CopyFrom(renderer.CullingData.BuiltNodes);

                NativeArray<CullingNode> dst = Data.NodeStore.GetSubArray(tree.Offset, tree.Count);

                TransformNodesJob job = new TransformNodesJob
                {
                    Transform = localToWorld,
                    Src = src.AsReadOnly(),
                    Dst = dst,
                };

                UpdateJobHandle = job.ScheduleBatchByRef(tree.Count, TransformBoundsJob.BatchSize, UpdateJobHandle);
                UpdateJobHandle = src.Dispose(UpdateJobHandle);
            }
            else
            {
                culling.LODAverageWorldSpaceSize = math.cmax(math.abs(localLODSize * localToWorldScale));
            }

            tree.Version = renderer.CullingData.BuiltVersion;
            tree.MinimumVerticesPerCluster = renderer.CullingData.BuildSettings.MinVerticesPerCluster;
            tree.UnbuiltBoundsCombined = renderer.CullingData.UnbuiltInstanceBoundsCombined;

            // --- Unbuilt Instances ---

            int newUnbuiltCount = renderer.CullingData.UnbuiltInstanceBounds.Count;
            if (newUnbuiltCount != tree.UnbuiltCount)
            {
                if (tree.UnbuiltCount > 0)
                    m_TreeUnbuiltAllocator.Free(tree.UnbuiltOffset, tree.UnbuiltCount);

                tree.UnbuiltOffset = 0;
                tree.UnbuiltCount = newUnbuiltCount;

                if (tree.UnbuiltCount > 0)
                {
                    tree.UnbuiltOffset = m_TreeUnbuiltAllocator.Allocate(tree.UnbuiltCount);
                }
            }

            if (m_TreeUnbuiltAllocator.MaxAllocatedSize > Data.UnbuiltBoundsStore.Length)
            {
                UpdateJobHandle.Complete(); // Ensure all jobs are complete before resizing

                int newCapacity = math.max(math.ceilpow2(m_TreeUnbuiltAllocator.MaxAllocatedSize), 256);
                Data.UnbuiltBoundsStore.Resize(newCapacity, Allocator.Persistent);
            }

            if (tree.UnbuiltCount > 0)
            {
                NativeArray<AxisAlignedBox> src = renderer.CullingData.UnbuiltInstanceBounds.ToNativeArray(Allocator.TempJob);
                NativeArray<AxisAlignedBox> dst = Data.UnbuiltBoundsStore.GetSubArray(tree.UnbuiltOffset, tree.UnbuiltCount);

                TransformBoundsJob job = new TransformBoundsJob
                {
                    Transform = localToWorld,
                    Src = src.AsReadOnly(),
                    Dst = dst,
                };

                UpdateJobHandle = job.ScheduleBatchByRef(tree.UnbuiltCount, TransformBoundsJob.BatchSize, UpdateJobHandle);
                UpdateJobHandle = src.Dispose(UpdateJobHandle);
            }

            tree.FirstUnbuiltIndex = renderer.CullingData.BuiltInstanceCount;

            // --- Static Occlusion Data ---

            ref InstanceRendererOcclusionData occlusion = ref Data.Occlusion[id];
            if (Context.HasStaticOcclusionManager() && occlusion.Count > 0)
                Context.GetStaticOcclusionManager().Free(occlusion.Offset, occlusion.Count);

            occlusion.Offset = 0;
            occlusion.Count = 0;
            occlusion.FirstNode = -1;
            occlusion.LastNode = -1;

            int occlusionLayerCount = renderer.CullingData.BuiltOcclusionLayerCount;
            if (Context.HasStaticOcclusionManager() && newPrototypeID.IsCreated && occlusionLayerCount > 0 && newTreeCount > 0)
            {
                int firstOcclusionNode = 0;
                int lastOcclusionNode = 0;

                while (true)
                {
                    int nextFirstOcclusionNode = Data.NodeStore[tree.Offset + firstOcclusionNode].FirstChild;
                    int nextLastOcclusionNode = Data.NodeStore[tree.Offset + lastOcclusionNode].LastChild;

                    if (nextFirstOcclusionNode < 0 || nextLastOcclusionNode < 0)
                        break;

                    int nextNodeCount = 1 + nextLastOcclusionNode - nextFirstOcclusionNode;
                    if (nextNodeCount > occlusionLayerCount)
                        break;

                    firstOcclusionNode = nextFirstOcclusionNode;
                    lastOcclusionNode = nextLastOcclusionNode;
                }

                int occlusionNodeCount = 1 + lastOcclusionNode - firstOcclusionNode;
                if (occlusionNodeCount > 1)
                {
                    int occlusionOffset = Context.GetStaticOcclusionManager().Allocate(occlusionNodeCount);
                    occlusion.Count = occlusionNodeCount;
                    occlusion.Offset = occlusionOffset;
                    occlusion.FirstNode = firstOcclusionNode;
                    occlusion.LastNode = lastOcclusionNode;

                    NativeArray<BoundingSphere> dst = Context.GetStaticOcclusionManager().GetBoundingSpheres(occlusionOffset, occlusionNodeCount);
                    NativeArray<CullingNode> src = Data.NodeStore.GetSubArray(tree.Offset + firstOcclusionNode, occlusionNodeCount);
                    TransformBoundingSpheresJob job = new TransformBoundingSpheresJob { Transform = localToWorld, Src = src.AsReadOnly(), Dst = dst, };
                    job.ScheduleBatchByRef(occlusionNodeCount, TransformBoundingSpheresJob.BatchSize).Complete();
                }
            }

            // --- Light Probes ---

            ref InstanceRendererLightProbeData lightProbes = ref Data.LightProbe[id];
            if (newPrototypeID.IsCreated)
            {
                lightProbes.SampleLightProbes = renderer.Prototype.SampleLightProbes;
                lightProbes.SampleLightProbesOffset = renderer.Prototype.SampleLightProbesOffset;
            }
            else
            {
                lightProbes.SampleLightProbes = false;
                lightProbes.SampleLightProbesOffset = float3.zero;
            }

            // --- Render State ---

            if (!InstancingSystem.DisableAutoBuildTrees && renderer.CullingData.AutoBuildEnabled)
            {
                if (renderer.CullingData.TryBuildTree())
                    AddTreeBuild(id);
            }

            Context.SceneData.RequestUpload(id);

            Data.IsRenderStateDirty[id] = false;
        }

        [BurstCompile]
        struct TransformBoundsJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public float4x4 Transform;
            [ReadOnly] public NativeArray<AxisAlignedBox>.ReadOnly Src;
            [WriteOnly] public NativeArray<AxisAlignedBox> Dst;

            public void Execute(int startIndex, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = startIndex + i;
                    Dst[index] = Src[index].TransformBy(Transform);
                }
            }
        }

        [BurstCompile]
        struct TransformNodesJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public float4x4 Transform;
            [ReadOnly] public NativeArray<CullingNode>.ReadOnly Src;
            [WriteOnly] public NativeArray<CullingNode> Dst;

            public void Execute(int startIndex, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = startIndex + i;
                    Dst[index] = Src[index].TransformBy(Transform);
                }
            }
        }

        [BurstCompile]
        struct TransformBoundingSpheresJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public float4x4 Transform;
            [ReadOnly] public NativeArray<CullingNode>.ReadOnly Src;
            [WriteOnly] public NativeArray<BoundingSphere> Dst;

            public void Execute(int startIndex, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = startIndex + i;
                    Dst[index] = Src[index].Bounds.TransformBy(Transform).GetBoundingSphere();
                }
            }
        }
    }
}
