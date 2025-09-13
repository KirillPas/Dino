// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    [DebuggerTypeProxy(typeof(InstancedPrototypeIDDebugView))]
    struct InstancedPrototypeID : IEquatable<InstancedPrototypeID>, IComparable<InstancedPrototypeID>
    {
        public static InstancedPrototypeID Null => new InstancedPrototypeID(0);

        public int Index;

        public bool IsCreated { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Index > 0; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedPrototypeID(int index) => Index = index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedPrototypeID other) => Index.Equals(other.Index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedPrototypeID other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedPrototypeID other) => Index.CompareTo(other.Index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(InstancedPrototypeID id) => id.Index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedPrototypeID left, InstancedPrototypeID right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedPrototypeID left, InstancedPrototypeID right) => !left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            if (InstancingSystem.IsActive() && IsCreated && InstancingSystem.Instance.Context.PrototypeManager.Exists(this))
                return InstancingSystem.Instance.Context.PrototypeManager.Prototypes[this].name;

            return $"InstancedPrototypeID({Index})";
        }
    }

    static class InstancedPrototypeIDHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AxisAlignedBox GetBounds(this InstancedPrototypeID id)
            => id.IsCreated && InstancingSystem.IsActive() && InstancingSystem.Instance.Context.PrototypeManager.Exists(id)
                ? InstancingSystem.Instance.Context.PrototypeManager.Data.Bounds[id]
                : AxisAlignedBox.Empty;
    }

    [Flags]
    enum InstancePrototypeFlags : byte
    {
        None               = 0,
        DynamicDensity     = 1 << 0,
        CrossFade          = 1 << 1,
        Animated           = 1 << 2,
        SpeedTree          = 1 << 3,
        LastLODIsBillboard = 1 << 4,
    }

    struct InstancedPrototypeCullingData
    {
        public byte Layer;
        public InstancedCullingMode CullingMode;
        public float CullingDistance;
        public InstancedStreamingMode StreamingMode;
        public float StreamingDistance;
        public InstancedLayerMask LayerMask;
        public InstancePrototypeFlags CullingFlags;

        public bool HasDynamicDensity => (CullingFlags & InstancePrototypeFlags.DynamicDensity) != 0;
        public bool HasCrossFade => (CullingFlags & InstancePrototypeFlags.CrossFade) != 0;
        public bool HasAnimatedCrossFade => (CullingFlags & InstancePrototypeFlags.Animated) != 0;
        public bool HasSpeedTreeCrossFade => (CullingFlags & InstancePrototypeFlags.SpeedTree) != 0;
        public bool LastLODIsBillboard => (CullingFlags & InstancePrototypeFlags.LastLODIsBillboard) != 0;
    }

    struct InstancedPrototypeDensityData
    {
        public float Density;
        public float Falloff;
        public Interval Range;
        public bool Enabled => Density < 1.0f && Range.Length > 0;
    }

    unsafe struct InstancedPrototypeLODData
    {
        public const int MaxLODCount = 8;

        public float3 LocalReferencePoint;
        public float LocalSize;
        public int LODCount;

        public fixed int VertexCount[MaxLODCount];
        public fixed float LODTransitionHeight[MaxLODCount];
        public fixed float LODHeight[MaxLODCount];
        public fixed bool PercentageFlags[MaxLODCount];
        public fixed int DrawCount[MaxLODCount];
        public fixed int DrawOffset[MaxLODCount];
    }

    struct InstancedPrototypeShadowData
    {
        public bool HasShadowCasters;
        public bool HasShadowReceivers;
        public float ShadowDistance;
        public IntervalInt ShadowLODRange;
        public InstancedShadowOverrideMode ShadowOverrideMode;
        public InstancedMaterialID ShadowMaterial;
    }

    struct InstancedPrototypeStateData : IEquatable<InstancedPrototypeStateData>
    {
        public InstancedMeshID Mesh;
        public InstancedMaterialID Material;
        public RenderFilterSettings FilterSettings;
        public ushort SubMeshIndex;
        public SubMeshDescriptor SubMeshDescriptor;

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Mesh.IsCreated && Material.IsCreated && SubMeshDescriptor.vertexCount > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedPrototypeStateData other)
        {
            return Mesh.Equals(other.Mesh) &&
                   Material.Equals(other.Material) &&
                   FilterSettings.Equals(other.FilterSettings) &&
                   SubMeshIndex == other.SubMeshIndex &&
                   SubMeshDescriptor.Equals(other.SubMeshDescriptor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedPrototypeStateData other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Mesh.GetHashCode();
                hashCode = (hashCode * 397) ^ Material.GetHashCode();
                hashCode = (hashCode * 397) ^ FilterSettings.GetHashCode();
                hashCode = (hashCode * 397) ^ SubMeshIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ SubMeshDescriptor.GetHashCode();
                return hashCode;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedPrototypeStateData left, InstancedPrototypeStateData right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedPrototypeStateData left, InstancedPrototypeStateData right) => !left.Equals(right);
    }

    struct InstancedPrototypeDataArrays : IDisposable
    {
        UnsafeArray<int> m_CountCapacity;

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_CountCapacity[0];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => m_CountCapacity[0] = value;
        }

        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_CountCapacity[1];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => m_CountCapacity[1] = value;
        }

        // Prototype data

        public UnsafeArray<int> InstanceID;
        public UnsafeArray<int> Version;
        public UnsafeArray<AxisAlignedBox> Bounds;
        public UnsafeArray<InstancedPrototypeCullingData> Culling;
        public UnsafeArray<InstancedPrototypeLODData> LOD;
        public UnsafeArray<InstancedPrototypeShadowData> Shadow;
        public UnsafeArray<InstancedPrototypeDensityData> Density;

        // Draw data

        public UnsafeArray<InstancedPrototypeStateData> DrawStore;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedPrototypeDataArrays(int capacity)
        {
            m_CountCapacity = new UnsafeArray<int>(2, AllocatorManager.Persistent);
            InstanceID = new UnsafeArray<int>(capacity, AllocatorManager.Persistent);
            Version = new UnsafeArray<int>(capacity, AllocatorManager.Persistent);
            Bounds = new UnsafeArray<AxisAlignedBox>(capacity, AllocatorManager.Persistent);
            Culling = new UnsafeArray<InstancedPrototypeCullingData>(capacity, AllocatorManager.Persistent);
            LOD = new UnsafeArray<InstancedPrototypeLODData>(capacity, AllocatorManager.Persistent);
            Shadow = new UnsafeArray<InstancedPrototypeShadowData>(capacity, AllocatorManager.Persistent);
            Density = new UnsafeArray<InstancedPrototypeDensityData>(capacity, AllocatorManager.Persistent);
            DrawStore = new UnsafeArray<InstancedPrototypeStateData>(capacity, AllocatorManager.Persistent);
            Capacity = capacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            m_CountCapacity.Dispose();
            InstanceID.Dispose();
            Version.Dispose();
            Bounds.Dispose();
            Culling.Dispose();
            LOD.Dispose();
            Shadow.Dispose();
            Density.Dispose();
            DrawStore.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Exists(InstancedPrototypeID id) => id.Index < Count && InstanceID[id.Index] != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int newCapacity)
        {
            if (newCapacity > Capacity)
            {
                InstanceID.Resize(newCapacity, AllocatorManager.Persistent);
                Version.Resize(newCapacity, AllocatorManager.Persistent);
                Bounds.Resize(newCapacity, AllocatorManager.Persistent);
                Culling.Resize(newCapacity, AllocatorManager.Persistent);
                LOD.Resize(newCapacity, AllocatorManager.Persistent);
                Shadow.Resize(newCapacity, AllocatorManager.Persistent);
                Density.Resize(newCapacity, AllocatorManager.Persistent);
                Capacity = newCapacity;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureDrawCapacity(int newCapacity)
        {
            if (newCapacity > DrawStore.Length)
            {
                DrawStore.Resize(newCapacity, AllocatorManager.Persistent);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnly AsReadOnly() => new ReadOnly(this);

        public struct ReadOnly
        {
            public readonly int Count;

            public UnsafeArray<int>.ReadOnly InstanceID;
            public UnsafeArray<int>.ReadOnly Version;
            public UnsafeArray<AxisAlignedBox>.ReadOnly Bounds;
            public UnsafeArray<InstancedPrototypeCullingData>.ReadOnly Culling;
            public UnsafeArray<InstancedPrototypeLODData>.ReadOnly LOD;
            public UnsafeArray<InstancedPrototypeShadowData>.ReadOnly Shadow;
            public UnsafeArray<InstancedPrototypeStateData>.ReadOnly DrawStore;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Exists(InstancedPrototypeID id) => id.Index < Count && InstanceID[id.Index] != 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ReadOnly(InstancedPrototypeDataArrays arrays)
            {
                Count = arrays.Count;
                InstanceID = arrays.InstanceID.AsReadOnly();
                Version = arrays.Version.AsReadOnly();
                Bounds = arrays.Bounds.AsReadOnly();
                Culling = arrays.Culling.AsReadOnly();
                LOD = arrays.LOD.AsReadOnly();
                Shadow = arrays.Shadow.AsReadOnly();
                DrawStore = arrays.DrawStore.AsReadOnly();
            }
        }
    }

    sealed unsafe class InstancedPrototypeManager
    {
        public event Action<InstancedPrototypeID> PrototypeUpdated;

        public InstancingContext Context;
        public InstancedPrototypeDataArrays Data;
        public SlotAllocator IDAllocator;
        public Dictionary<int, InstancedPrototypeID> InstanceIDToHandle;
        public Dictionary<int, InstancedPrototypeID> ChildComponentToHandle;
        public ElementAllocator DrawAllocator;
        public InstancedPrototype[] Prototypes;
        public int[][] ComponentInstanceIDs;
#if UNITY_EDITOR
        public Dictionary<UnityEditor.GUID, InstancedPrototypeID> GUIDToHandle;
#endif

        int[] m_ReferenceCount;

        public InstancedPrototypeManager(InstancingContext context, int capacity)
        {
            Context = context;

            Data = new InstancedPrototypeDataArrays(capacity);
            IDAllocator = new SlotAllocator(capacity, AllocatorManager.Persistent);
            IDAllocator.Allocate(); // Reserve the first element for the null prototype.
            InstanceIDToHandle = new Dictionary<int, InstancedPrototypeID>(capacity);
            ChildComponentToHandle = new Dictionary<int, InstancedPrototypeID>(capacity);

            DrawAllocator = new ElementAllocator(capacity, AllocatorManager.Persistent);
            DrawAllocator.Allocate(); // Reserve the first element for the null sub-mesh.

            Prototypes = new InstancedPrototype[capacity];
            ComponentInstanceIDs = new int[capacity][];
            m_ReferenceCount = new int[capacity];

#if UNITY_EDITOR
            GUIDToHandle = new Dictionary<UnityEditor.GUID, InstancedPrototypeID>(capacity);
            UnityEditor.ObjectChangeEvents.changesPublished += OnObjectChangeEventsChangesPublished;
#endif
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            UnityEditor.ObjectChangeEvents.changesPublished -= OnObjectChangeEventsChangesPublished;
#endif

            IDAllocator.Dispose();
            DrawAllocator.Dispose();
            Data.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Exists(InstancedPrototypeID id) => IDAllocator.Exists(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedPrototypeID Get(InstancedPrototype prototype)
        {
            if (prototype == null)
                return InstancedPrototypeID.Null;

            int instanceID = prototype.GetInstanceID();
            if (instanceID == 0)
                return InstancedPrototypeID.Null;

            return InstanceIDToHandle.TryGetValue(instanceID, out InstancedPrototypeID id) ? id : InstancedPrototypeID.Null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedPrototype GetPrototype(InstancedPrototypeID id)
        {
            return IDAllocator.Exists(id) ? Prototypes[id] : null;
        }

        public InstancedPrototypeID Register(InstancedPrototype prototype)
        {
            if (prototype == null)
                return InstancedPrototypeID.Null;

            int instanceID = prototype.GetInstanceID();
            if (instanceID == 0)
                return InstancedPrototypeID.Null;

            if (InstanceIDToHandle.TryGetValue(instanceID, out InstancedPrototypeID id))
            {
                m_ReferenceCount[id]++;
                return id;
            }

            int index = IDAllocator.Allocate();
            if (IDAllocator.MaxAllocatedSlot >= Data.Capacity)
            {
                int newCapacity = math.ceilpow2(IDAllocator.MaxAllocatedSlot + 1);
                Data.EnsureCapacity(newCapacity);
                Array.Resize(ref Prototypes, newCapacity);
                Array.Resize(ref ComponentInstanceIDs, newCapacity);
                Array.Resize(ref m_ReferenceCount, newCapacity);
            }

            id = new InstancedPrototypeID(index);
            InstanceIDToHandle.Add(instanceID, id);
            ChildComponentToHandle.Add(instanceID, id);

            Data.InstanceID[index] = instanceID;
            Prototypes[index] = prototype;
            m_ReferenceCount[index] = 1;
            Data.Count++;
#if UNITY_EDITOR
            UnityEditor.GUID guid = UnityEditor.AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(prototype.gameObject));
            GUIDToHandle[guid] = id;
#endif
            Update(id);
            return id;
        }

        public void Unregister(InstancedPrototypeID id)
        {
            if (!IDAllocator.Exists(id))
                return;

            if (--m_ReferenceCount[id] > 0)
                return;

            IDAllocator.Free(id);

            int instanceID = Data.InstanceID[id];
            InstanceIDToHandle.Remove(instanceID);

            ChildComponentToHandle.Remove(instanceID);
            foreach (int componentInstanceID in ComponentInstanceIDs[id])
                ChildComponentToHandle.Remove(componentInstanceID);

            ref InstancedPrototypeLODData lodData = ref Data.LOD[id];
            for (int lodIndex = 0; lodIndex < lodData.LODCount; lodIndex++)
            {
                int count = lodData.DrawCount[lodIndex];
                if (count == 0) continue;

                int offset = lodData.DrawOffset[lodIndex];
                DrawAllocator.Free(offset, count);

                lodData.DrawCount[lodIndex] = 0;
                lodData.DrawOffset[lodIndex] = 0;
            }

            Data.InstanceID[id] = 0;
            Data.Version[id] = 0;
            Data.Culling[id] = default;
            Data.Shadow[id] = default;
            Prototypes[id] = null;
            ComponentInstanceIDs[id] = Array.Empty<int>();

            Data.Count--;
        }

        void Destroy(InstancedPrototypeID id)
        {
            if (!IDAllocator.Exists(id))
                return;

            m_ReferenceCount[id] = 0;
            Unregister(id);
        }

        static List<InstancedMaterialID> s_PrevMaterialIDs = new List<InstancedMaterialID>();
        static List<InstancedMeshID> s_PrevMeshIDs = new List<InstancedMeshID>();

        public void Update(InstancedPrototypeID id)
        {
            if (!IDAllocator.Exists(id))
                return;

            InstancedPrototypeLODData lodData = Data.LOD[id];

            for (int lodIndex = 0; lodIndex < lodData.LODCount; lodIndex++)
            {
                int count = lodData.DrawCount[lodIndex];
                if (count == 0) continue;

                int offset = lodData.DrawOffset[lodIndex];
                for (int drawIndex = offset; drawIndex < offset + count; drawIndex++)
                {
                    InstancedPrototypeStateData stateData = Data.DrawStore[drawIndex];
                    s_PrevMaterialIDs.Add(stateData.Material);
                    s_PrevMeshIDs.Add(stateData.Mesh);
                }

                DrawAllocator.Free(offset, count);
            }

            DrawAllocator.MergeFree();
            Data.Version[id]++;
            Data.Culling[id] = default;

            InstancedPrototype prototype = Prototypes[id];
            float localSpaceSize;
            float3 localReferencePoint = float3.zero;
            InstancePrototypeFlags cullingFlags = InstancePrototypeFlags.None;
            if (prototype.DynamicDensitySettings.Enabled)
                cullingFlags |= InstancePrototypeFlags.DynamicDensity;

            bool isLODGroup = false;
            bool useDitheringCrossFade = false;
            bool useSpeedTreeCrossFade = false;
            bool crossFadeIsAnimated = false;

            LOD[] unityLODs;
            if (prototype.TryGetComponent(out LODGroup group))
            {
                isLODGroup = true;
                unityLODs = group.GetLODs();
                localSpaceSize = group.size;

                useDitheringCrossFade = group.fadeMode != LODFadeMode.None;
                useSpeedTreeCrossFade = group.fadeMode == LODFadeMode.SpeedTree;
                crossFadeIsAnimated = !useSpeedTreeCrossFade && group.animateCrossFading;

                if (useDitheringCrossFade)
                    cullingFlags |= InstancePrototypeFlags.CrossFade;
                if (useSpeedTreeCrossFade)
                    cullingFlags |= InstancePrototypeFlags.SpeedTree;
                if (crossFadeIsAnimated)
                    cullingFlags |= InstancePrototypeFlags.Animated;

#if UNITY_2022_2_OR_NEWER
                if (group.lastLODBillboard)
                    cullingFlags |= InstancePrototypeFlags.LastLODIsBillboard;
#endif

                localReferencePoint = group.localReferencePoint;
            }
            else
            {
                MeshRenderer[] meshRenderers = prototype.GetComponentsInChildren<MeshRenderer>();
                unityLODs = new LOD[1];
                unityLODs[0] = new LOD(0.0001f, meshRenderers.Cast<Renderer>().ToArray());

                AxisAlignedBox rendererBounds = AxisAlignedBox.Empty;

                foreach (MeshRenderer meshRenderer in meshRenderers)
                {
                    if (!meshRenderer.enabled || !meshRenderer.TryGetComponent(out MeshFilter meshFilter) || !meshFilter.sharedMesh)
                        continue;

                    rendererBounds += meshRenderer.bounds;
                }

                localSpaceSize = rendererBounds.Radius;
            }

            var cullingData = new InstancedPrototypeCullingData
            {
                Layer = (byte)prototype.gameObject.layer,
                CullingMode = isLODGroup ? prototype.CullingMode : InstancedCullingMode.Override,
                CullingDistance = prototype.CullingDistance,
                StreamingMode = prototype.StreamingMode,
                StreamingDistance = prototype.StreamingDistance,
                LayerMask = prototype.LayerMask,
                CullingFlags = cullingFlags,
            };

            lodData = new InstancedPrototypeLODData
            {
                LocalSize = localSpaceSize,
                LocalReferencePoint = localReferencePoint,
                LODCount = unityLODs.Length,
            };

            if (Data.Shadow[id].ShadowMaterial.IsCreated)
                s_PrevMaterialIDs.Add(Data.Shadow[id].ShadowMaterial);

            Data.Shadow[id] = new InstancedPrototypeShadowData
            {
                ShadowDistance = prototype.ShadowDistance,
                ShadowLODRange = prototype.ShadowLODRange,
                ShadowOverrideMode = prototype.ShadowOverrideMode,
                ShadowMaterial = Context.MaterialManager.Register(prototype.ShadowCustomMaterial),
            };

            InstancedDynamicDensitySettings dynamicDensitySettings = prototype.DynamicDensitySettings;
            Data.Density[id] = new InstancedPrototypeDensityData
            {
                Density = dynamicDensitySettings.Density,
                Falloff = dynamicDensitySettings.Falloff,
                Range = dynamicDensitySettings.Range,
            };

            if (dynamicDensitySettings.Enabled)
                cullingFlags |= InstancePrototypeFlags.DynamicDensity;

            int crossFadeLODBegin = 0;
            if ((cullingFlags & InstancePrototypeFlags.SpeedTree) != 0)
            {
                int lastLODIndex = unityLODs.Length - 1;
                bool hasBillboardLOD = unityLODs.Length > 0 && unityLODs[lastLODIndex].renderers.Length == 1 && (cullingFlags & InstancePrototypeFlags.LastLODIsBillboard) != 0;

                if (unityLODs.Length == 0)
                    crossFadeLODBegin = 0;
                else if (hasBillboardLOD)
                    crossFadeLODBegin = math.max(unityLODs.Length, 2) - 2;
                else
                    crossFadeLODBegin = unityLODs.Length - 1;
            }

            AxisAlignedBox bounds = AxisAlignedBox.Empty;
            bool hasShadowCasters = false;
            bool hasShadowReceivers = false;

            for (int lodIndex = 0; lodIndex < unityLODs.Length; lodIndex++)
            {
                LOD unityLOD = unityLODs[lodIndex];

                float lodHeight = unityLOD.screenRelativeTransitionHeight;
                lodData.LODHeight[lodIndex] = lodHeight;
                lodData.LODTransitionHeight[lodIndex] = lodHeight;
                lodData.VertexCount[lodIndex] = 0;
                lodData.PercentageFlags[lodIndex] = false;

                if (useSpeedTreeCrossFade && lodIndex < crossFadeLODBegin)
                {
                    // SpeedTree cross-fade is not used when the last LOD is a billboard.
                    lodData.PercentageFlags[lodIndex] = true;
                }

                if (!crossFadeIsAnimated && useDitheringCrossFade && lodIndex >= crossFadeLODBegin)
                {
                    float prevLODHeight = lodIndex > 0 ? unityLODs[lodIndex - 1].screenRelativeTransitionHeight : 1.0f;
                    float transitionHeight = lodHeight + unityLOD.fadeTransitionWidth * (prevLODHeight - lodHeight);
                    lodData.LODTransitionHeight[lodIndex] = transitionHeight;
                }

                int drawCount = 0;
                for (int rendererIndex = 0; rendererIndex < unityLOD.renderers.Length; rendererIndex++)
                {
                    Renderer unityRenderer = unityLOD.renderers[rendererIndex];
                    if (!unityRenderer || !unityRenderer.TryGetComponent(out MeshFilter meshFilter) || !meshFilter.sharedMesh)
                        continue;

                    Mesh mesh = meshFilter.sharedMesh;
                    for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                    {
                        if (!unityRenderer.sharedMaterials.IsValidIndex(subMeshIndex))
                            continue;

                        Material material = unityRenderer.sharedMaterials[subMeshIndex];
                        if (!material) continue;
                        drawCount++;
                    }
                }

                lodData.DrawCount[lodIndex] = 0;
                lodData.DrawOffset[lodIndex] = 0;

                if (drawCount == 0)
                    continue;

                int drawOffset = DrawAllocator.Allocate(drawCount);
                if (DrawAllocator.MaxAllocatedSize > Data.DrawStore.Length)
                    Data.EnsureDrawCapacity(math.ceilpow2(DrawAllocator.MaxAllocatedSize));

                lodData.DrawCount[lodIndex] = drawCount;
                lodData.DrawOffset[lodIndex] = drawOffset;

                int drawIndex = drawOffset;
                for (int rendererIndex = 0; rendererIndex < unityLOD.renderers.Length; rendererIndex++)
                {
                    Renderer unityRenderer = unityLOD.renderers[rendererIndex];
                    if (!unityRenderer)
                        continue;

                    if (!unityRenderer.TryGetComponent(out MeshFilter meshFilter) || !meshFilter.sharedMesh)
                        continue;

                    Mesh mesh = meshFilter.sharedMesh;
                    RenderFilterSettings filterSettings = new RenderFilterSettings(unityRenderer);
                    hasShadowCasters |= filterSettings.ShadowCastingMode != ShadowCastingMode.Off;
                    hasShadowReceivers |= filterSettings.ReceiveShadows;
                    int subMeshCount = mesh.subMeshCount;

                    for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                    {
                        if (!unityRenderer.sharedMaterials.IsValidIndex(subMeshIndex))
                            continue;

                        Material material = unityRenderer.sharedMaterials[subMeshIndex];
                        if (!material) continue;

                        SubMeshDescriptor subMeshDescriptor = mesh.GetSubMesh(subMeshIndex);

                        Data.DrawStore[drawIndex] = new InstancedPrototypeStateData
                        {
                            Mesh = Context.MeshManager.Register(mesh),
                            Material = Context.MaterialManager.Register(material),
                            FilterSettings = filterSettings,
                            SubMeshIndex = (ushort) subMeshIndex,
                            SubMeshDescriptor = subMeshDescriptor,
                        };

                        lodData.VertexCount[lodIndex] += subMeshDescriptor.vertexCount;
                        drawIndex++;
                    }

                    if (lodIndex == 0)
                        bounds += mesh.bounds;
                }
            }

            Data.Bounds[id] = bounds;
            Data.Shadow[id].HasShadowReceivers = hasShadowReceivers;
            Data.Shadow[id].HasShadowCasters = hasShadowCasters;

            Data.Culling[id] = cullingData;
            Data.LOD[id] = lodData;

            s_TempComponents.Clear();
            prototype.GetComponentsInChildren(s_TempComponents);

            s_TempInstanceIDs.Clear();
            foreach (Component component in s_TempComponents)
            {
                if (component)
                    s_TempInstanceIDs.Add(component.GetInstanceID());
            }

            foreach (int instanceID in s_TempInstanceIDs)
                ChildComponentToHandle[instanceID] = id;

            ComponentInstanceIDs[id] = s_TempInstanceIDs.ToArray();

            for (int i = 0; i < s_PrevMaterialIDs.Count; i++)
                Context.MaterialManager.Unregister(s_PrevMaterialIDs[i]);

            for (int i = 0; i < s_PrevMeshIDs.Count; i++)
                Context.MeshManager.Unregister(s_PrevMeshIDs[i]);

            s_PrevMaterialIDs.Clear();
            s_PrevMeshIDs.Clear();

            PrototypeUpdated?.Invoke(id);
        }

        static List<Component> s_TempComponents = new List<Component>();
        static List<int> s_TempInstanceIDs = new List<int>();

#if UNITY_EDITOR
        void OnObjectChangeEventsChangesPublished(ref UnityEditor.ObjectChangeEventStream stream)
        {
            for (int i = 0; i != stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case UnityEditor.ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs evt);

                        UnityEditor.SceneManagement.PrefabStage prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                        if (prefabStage != null && prefabStage.scene.isLoaded)
                        {
                            UnityEditor.GUID guid = UnityEditor.AssetDatabase.GUIDFromAssetPath(prefabStage.assetPath);
                            if (GUIDToHandle.TryGetValue(guid, out InstancedPrototypeID id))
                            {
                                Update(id);
                            }
                        }
                        else
                        {
                            if (ChildComponentToHandle.TryGetValue(evt.instanceId, out InstancedPrototypeID id))
                            {
                                Update(id);
                            }
                        }
                        break;
                    }
                    case UnityEditor.ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    {
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out ChangeGameObjectStructureHierarchyEventArgs evt);
                        if (ChildComponentToHandle.TryGetValue(evt.instanceId, out InstancedPrototypeID id))
                        {
                            Update(id);
                        }
                        break;
                    }
                    case UnityEditor.ObjectChangeKind.ChangeGameObjectStructure:
                    {
                        stream.GetChangeGameObjectStructureEvent(i, out ChangeGameObjectStructureEventArgs evt);
                        if (ChildComponentToHandle.TryGetValue(evt.instanceId, out InstancedPrototypeID id))
                        {
                            Update(id);
                        }
                        break;
                    }
                    case UnityEditor.ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        stream.GetDestroyGameObjectHierarchyEvent(i, out DestroyGameObjectHierarchyEventArgs evt);
                        if (InstanceIDToHandle.TryGetValue(evt.instanceId, out InstancedPrototypeID id))
                        {
                            Destroy(id);
                        }

                        if (ChildComponentToHandle.TryGetValue(evt.instanceId, out InstancedPrototypeID _))
                        {
                            Update(id);
                            Context.MaterialManager.RemoveNullMaterials();
                        }
                        break;
                    }
                }
            }
        }
#endif
    }
}
