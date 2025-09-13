// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Core;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Plane = MA.Mathematics.Plane;
using Random = Unity.Mathematics.Random;

namespace MA.Flora
{
    partial class InstancedMeshContainer : IInstancedRendererEditorData, ISerializationCallbackReceiver
    {
        enum Version { Initial = 1, GlobalDensity = 2, SerializeDataAsBytes = 3, Latest = SerializeDataAsBytes }

#pragma warning disable CS0414
        [SerializeField] Version m_Version = Version.Latest;
#pragma warning restore CS0414
        [SerializeField] InstancedPrototype m_Prototype;
        [SerializeField] CullingData m_CullingData = new CullingData();

        [SerializeField] float m_RenderDistance;
        [SerializeField] float m_StreamDistance;

        [SerializeField] int m_InstanceCount;
        [NonSerialized] List<LocalTransform> m_InstanceTransforms = new List<LocalTransform>();
        [SerializeField] int m_InstanceTransformsVersion = 1;
        [SerializeField] int m_InstanceOrderVersion = 1;

        [SerializeField] InstancedPropertyArrays m_InstancePropertyArrays = new InstancedPropertyArrays();
        [FormerlySerializedAs("m_InstanceProxies")] [SerializeField] List<InstancedObjectLink> m_LinkedObjects = new List<InstancedObjectLink>();

        [NonSerialized] int m_EnabledInstancesVersion = 1;
        [NonSerialized] UnsafeBitList m_InstanceEnabled;

        [NonSerialized] InstancePlacementHash<int> m_PlacementHash;

        [NonSerialized] bool m_Active;
        [NonSerialized] int m_ContainerInstanceID;
        [NonSerialized] List<InstancedGlobalID> m_GlobalIDs = new List<InstancedGlobalID>();

        [NonSerialized] Transform m_Transform;
        [NonSerialized] LocalTransform m_CachedLocalToWorld;
        [NonSerialized] internal InstancedRendererID InstancedRendererID;
        [NonSerialized] bool m_IsDestroying;
        [NonSerialized] Terrain m_ParentTerrain;

        [NonSerialized] bool m_MovingBatch;
        [NonSerialized] bool m_MovingSavedAutoRebuild;
        [NonSerialized] UnsafeIndirectList<int> m_MovingBatchIndices;
        [NonSerialized] JobHandle m_MovingBatchHandle;

#if UNITY_EDITOR
        [NonSerialized] List<SerializableGuid> m_InstanceProceduralGUIDs = new List<SerializableGuid>();
        [NonSerialized] UnsafeParallelMultiHashMap<SerializableGuid, int> m_ProceduralHash;

        [NonSerialized] UnsafeBitList m_SelectedInstances;
        [SerializeField] int m_SelectedInstancesVersion = 1;

        internal static Action<InstancedMeshContainer> AfterRendererWasModified;
        [NonSerialized] bool m_QueueAfterRendererModifiedCallback;
        [NonSerialized] bool m_IsSceneSaving;

        internal const int EditorCellSize = 256;
        [SerializeField] int3 m_EditorCell;
        [SerializeField] AxisAlignedBox m_EditorBounds = AxisAlignedBox.Empty;
#endif

        // --- Byte Serialization ---

        [SerializeField] int m_SerializedTransformCount;
        [SerializeField] byte[] m_SerializedTransformBytes = Array.Empty<byte>();
#if UNITY_EDITOR
        [SerializeField] byte[] m_SerializedProceduralGUIDBytes = Array.Empty<byte>();
#endif

        [SerializeField, FormerlySerializedAs("m_InstanceTransforms")] List<LocalTransform> m_LegacySerializedInstanceTransforms = new List<LocalTransform>();
        [SerializeField, FormerlySerializedAs("m_InstanceProceduralGUIDs")] List<SerializableGuid> m_LegacySerializedProceduralGUIDs = new List<SerializableGuid>();

        // --- Events ---

        void Awake()
        {
            if (m_Version < Version.GlobalDensity)
            {
                m_Version = Version.GlobalDensity;
                m_CullingData.OutOfDate = true;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

            if (m_Version < Version.SerializeDataAsBytes)
            {
                m_Version = Version.SerializeDataAsBytes;
                m_CullingData.OutOfDate = true;

                m_InstanceCount = m_LegacySerializedInstanceTransforms.Count;
                m_InstanceTransforms.CopyFrom(m_LegacySerializedInstanceTransforms);
                m_LegacySerializedInstanceTransforms.Clear();

#if UNITY_EDITOR
                m_InstanceProceduralGUIDs.CopyFrom(m_LegacySerializedProceduralGUIDs);
                m_LegacySerializedProceduralGUIDs.Clear();

                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }

        void OnEnable()
        {
            m_Transform = transform;
            m_ContainerInstanceID = GetInstanceID();
            m_ParentTerrain = GetComponentInParent<Terrain>();
            if (m_ParentTerrain)
                TerrainCallbacks.heightmapChanged += OnTerrainHeightmapChanged;

            m_CullingData.Initialize(this);
            m_CullingData.TreeBuilt += OnCullingDataBuilt;

            if (!m_Active)
            {
                m_Active = true;

                int capacity = m_InstanceCount > 0 ? m_InstanceCount : 64;
                m_InstanceEnabled = new UnsafeBitList(capacity, Allocator.Persistent);
                m_InstanceEnabled.Resize(m_InstanceCount);
                m_InstanceEnabled.SetAll(true);

                m_PlacementHash = new InstancePlacementHash<int>(capacity, Allocator.Persistent);
#if UNITY_EDITOR
                m_ProceduralHash = new UnsafeParallelMultiHashMap<SerializableGuid, int>(16, Allocator.Persistent);
#endif
                m_MovingBatchIndices = new UnsafeIndirectList<int>(16, Allocator.Persistent);
            }

            RuntimeSpatialHash.Instance.Update(this);

            if (m_Prototype)
            {
                m_Prototype.Changed += OnPrototypeChanged;
                m_Prototype.InstancedPropertyArrayChanged += OnPrototypePropertiesChanged;
                m_Prototype.InstancedPropertyUpdated += OnPrototypePropertyUpdated;
                RebuildInstanceProperties();
            }

            RebuildHashes();
            RebuildGlobalIDs();

            if (m_Prototype)
                InstancedRendererID = InstancingSystem.RegisterRenderer(this);

            UpdateTransform();
            UpdateEditorCell();

#if UNITY_EDITOR
            EditorSpatialHash.Instance.Add(this);
            EditorTransformTracker.Track(m_Transform, OnTransformHierarchyChanged);
            if (UnityEditor.Selection.Contains(gameObject))
                SelectAll();
#endif

            MarkRenderStateDirty();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            if (UndoUtility.IsProcessing)
            {
                InstancingSystem.MarkRendererDirty(InstancedRendererID);
            }
            else
#endif
            {
                InstancingSystem.UnregisterRenderer(InstancedRendererID);
                InstancedRendererID = InstancedRendererID.Null;
            }

            RuntimeSpatialHash.Instance.Remove(this);
            TerrainCallbacks.heightmapChanged -= OnTerrainHeightmapChanged;
            UnregisterGlobalIDs();

            m_CullingData.Dispose();
            m_CullingData.TreeBuilt -= OnCullingDataBuilt;

            if (m_Active)
            {
                m_Active = false;
                m_InstanceEnabled.Dispose();
                m_PlacementHash.Dispose();
#if UNITY_EDITOR
                m_ProceduralHash.Dispose();
#endif
                m_MovingBatchIndices.Dispose();
            }

            if (m_Prototype)
            {
                m_Prototype.Changed -= OnPrototypeChanged;
                m_Prototype.InstancedPropertyArrayChanged -= OnPrototypePropertiesChanged;
                m_Prototype.InstancedPropertyUpdated -= OnPrototypePropertyUpdated;
            }

#if UNITY_EDITOR
            EditorSpatialHash.Instance.Remove(this);
            EditorTransformTracker.UnTrack(m_Transform);
            m_SelectedInstances.Dispose();
#endif
        }

        void OnDestroy()
        {
            m_IsDestroying = true;
            m_InstancePropertyArrays.Dispose();

            if (gameObject.scene.isLoaded)
            {
                SetDirty(InstancedMeshContainerChange.ContainerDestroyed);
                DestroyChildLinkedObjects();
            }
        }

        void OnTransformHierarchyChanged(Transform transform)
        {
            if (transform)
                MarkTransformDirty();
        }

        void OnTransformParentChanged()
        {
            if (transform)
                MarkTransformDirty();
        }

        void OnPrototypeChanged()
        {
            m_CullingData.OutOfDate = true;
            MarkRenderStateDirty();
        }

        void OnCullingDataBuilt(CullingData tree)
        {
            m_InstanceOrderVersion++;
            m_InstanceTransformsVersion++;
#if UNITY_EDITOR
            m_SelectedInstancesVersion++;
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            MarkRenderStateDirty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateTransform()
        {
            m_CachedLocalToWorld = LocalTransform.FromPositionRotationScale(m_Transform.position, m_Transform.rotation, m_Transform.lossyScale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void RebuildHashes()
        {
            if (!m_PlacementHash.IsCreated)
                return;

            m_PlacementHash.Clear();

            for (int instanceIndex = 0; instanceIndex < m_InstanceTransforms.Count; instanceIndex++)
            {
                m_PlacementHash.AddInstance(m_InstanceTransforms[instanceIndex].Position, instanceIndex);
            }

#if UNITY_EDITOR
            m_ProceduralHash.Clear();

            for (int instanceIndex = 0; instanceIndex < m_InstanceProceduralGUIDs.Count; instanceIndex++)
            {
                if (m_InstanceProceduralGUIDs[instanceIndex].IsValid)
                    m_ProceduralHash.Add(m_InstanceProceduralGUIDs[instanceIndex], instanceIndex);
            }
#endif
        }

        // --- ISerializationCallbackReceiver ---
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            m_SerializedTransformCount = SerializationHelpers.SerializeTransformsToByteArray(m_InstanceTransforms, ref m_SerializedTransformBytes);
#if UNITY_EDITOR
            SerializationHelpers.SerializeListToBytes(m_InstanceProceduralGUIDs, ref m_SerializedProceduralGUIDBytes);
#endif
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (m_Version >= Version.SerializeDataAsBytes)
            {
                SerializationHelpers.DeserializeByteArrayToTransforms(ref m_SerializedTransformBytes, m_SerializedTransformCount, m_InstanceTransforms);
#if UNITY_EDITOR
                SerializationHelpers.DeserializeBytesToList(ref m_SerializedProceduralGUIDBytes, m_InstanceProceduralGUIDs);
#endif
            }

            RebuildHashes();
            RebuildGlobalIDs();
        }

        // --- IInstancedRenderer ---
        bool IInstancedRenderer.IsValid => this && enabled;
        Transform IInstancedRenderer.Transform => m_Transform;
        CullingData IInstancedRenderer.CullingData => m_CullingData;
        int IInstancedRenderer.InstanceOrderVersion => m_InstanceOrderVersion;
        int IInstancedRenderer.InstanceTransformsVersion => m_InstanceTransformsVersion;

        // --- IEnableableInstancedRenderer ---
        int IInstancedRenderer.InstancesEnabledVersion => m_EnabledInstancesVersion;
        UnsafeBitList IInstancedRenderer.InstancesEnabled => m_InstanceEnabled;

        // --- IInstancedPropertyRenderer ---
        InstancedPropertyArrays IInstancedRenderer.InstancePropertyArrays => m_InstancePropertyArrays;

        // --- IInstancedRendererEditorData ---
#if UNITY_EDITOR
        int IInstancedRendererEditorData.InstanceSelectionVersion => m_SelectedInstancesVersion;
        UnsafeBitList IInstancedRendererEditorData.InstanceSelection => m_SelectedInstances;
#endif

        // --- Render State ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetDirtyNoNotify(InstancedMeshContainerChange type)
        {
            unchecked
            {
                switch (type)
                {
                    case InstancedMeshContainerChange.Instance:
                    case InstancedMeshContainerChange.InstanceAdded:
                        m_InstanceTransformsVersion++;
                        break;
                    case InstancedMeshContainerChange.InstanceEnabled:
                        m_EnabledInstancesVersion++;
                        break;
                    case InstancedMeshContainerChange.InstanceRemoved:
                    case InstancedMeshContainerChange.ContainerCleared:
                    case InstancedMeshContainerChange.InstanceIndex:
                        m_InstanceTransformsVersion++;
                        m_InstanceOrderVersion++;
                        m_EnabledInstancesVersion++;
#if UNITY_EDITOR
                        m_SelectedInstancesVersion++;
#endif
                        break;
                }
            }

            MarkRenderStateDirty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetDirty(InstancedMeshContainerChange type, int instanceIndex = -1, int oldInstanceIndex = -1)
        {
            SetDirtyNoNotify(type);

            Changed?.Invoke(this, type, instanceIndex, oldInstanceIndex);

#if UNITY_EDITOR
            if (m_QueueAfterRendererModifiedCallback)
                return;

            m_QueueAfterRendererModifiedCallback = true;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                m_QueueAfterRendererModifiedCallback = false;
                AfterRendererWasModified?.Invoke(this);
            };
#endif
        }

        // --- Instances ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int AddInstanceInternal(in LocalTransform transform, Space space, bool createLinkedObject)
        {
            LocalTransform localTransform = space == Space.World ? m_CachedLocalToWorld.InverseTransform(transform) : transform;
            int instanceIndex = m_InstanceCount++;
            if (instanceIndex >= MaxInstanceCount)
                throw new InvalidOperationException($"Cannot add instance to {name} because it has reached the maximum instance count of {MaxInstanceCount}.");

            m_InstanceTransforms.Add(localTransform);
            m_InstancePropertyArrays.AddInstances(1);

            if (m_Active)
            {
                m_PlacementHash.AddInstance(localTransform.Position, instanceIndex);
                m_InstanceEnabled.Add(true);
                m_GlobalIDs.Add(RuntimeInstanceManager.RegisterInstance(m_ContainerInstanceID, instanceIndex));
            }

            if (m_Prototype)
            {
                AxisAlignedBox newInstanceBounds = m_Prototype.Bounds.TransformBy(localTransform);
                m_CullingData.AddUnbuiltInstance(instanceIndex, newInstanceBounds);
            }

            if (createLinkedObject)
                CreateLinkedObject(instanceIndex);

            SetDirty(InstancedMeshContainerChange.InstanceAdded, instanceIndex);

            return instanceIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateInstanceTransformInternal(int instanceIndex, in LocalTransform transform, Space space, bool updateLinkedObject = true)
        {
            LocalTransform oldLocalTransform = m_InstanceTransforms[instanceIndex];
            LocalTransform newLocalTransform = space == Space.World ? m_CachedLocalToWorld.InverseTransform(transform) : transform;

            m_InstanceTransforms[instanceIndex] = newLocalTransform;
            if (updateLinkedObject && m_LinkedObjects.IsValidIndex(instanceIndex) && m_LinkedObjects[instanceIndex])
                m_LinkedObjects[instanceIndex].TeleportToInstance();

            if (m_CullingData.UpdateInstanceBounds(instanceIndex, oldLocalTransform, newLocalTransform))
            {
                if (m_Active)
                {
                    m_PlacementHash.RemoveInstance(oldLocalTransform.Position, instanceIndex);
                    m_PlacementHash.AddInstance(newLocalTransform.Position, instanceIndex);
                }
            }

            SetDirty(InstancedMeshContainerChange.Instance, instanceIndex);
        }

        struct OrderDescending : IComparer<int>
        {
            public int Compare(int x, int y) => y.CompareTo(x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void RemoveInstanceInternal(int instanceToRemove, bool destroyLinkedObject = true)
        {
            if (m_IsDestroying || !Exists(instanceToRemove))
                return;

            int instanceCount = m_InstanceCount--;
            int lastInstanceIndex = instanceCount - 1;

            LocalTransform localTransform = m_InstanceTransforms[instanceToRemove];

            if (m_Active)
            {
                m_PlacementHash.RemoveInstance(localTransform.Position, instanceToRemove);
                m_InstanceEnabled.RemoveAtSwapBack(instanceToRemove);
            }

#if UNITY_EDITOR
            bool hasProceduralGUID = m_InstanceProceduralGUIDs.IsValidIndex(instanceToRemove);
            SerializableGuid proceduralGUID = hasProceduralGUID ? m_InstanceProceduralGUIDs[instanceToRemove] : default;
            if (m_Active && proceduralGUID.IsValid)
                m_ProceduralHash.Remove(proceduralGUID, instanceToRemove);
#endif

            if (instanceToRemove != lastInstanceIndex)
            {
                // Swap the last instance location in the placement hash
                LocalTransform lastLocalTransform = m_InstanceTransforms[lastInstanceIndex];

                if (m_Active)
                {
                    m_PlacementHash.RemoveInstance(lastLocalTransform.Position, lastInstanceIndex);
                    m_PlacementHash.AddInstance(lastLocalTransform.Position, instanceToRemove);
                }

#if UNITY_EDITOR
                if (hasProceduralGUID)
                {
                    int lastProceduralIndex = m_InstanceProceduralGUIDs.Count - 1;
                    SerializableGuid lastProceduralGUID = m_InstanceProceduralGUIDs[lastProceduralIndex];
                    if (m_ProceduralHash.IsCreated && lastProceduralGUID.IsValid)
                    {
                        m_ProceduralHash.Remove(lastProceduralGUID, lastProceduralIndex);
                        m_ProceduralHash.Add(lastProceduralGUID, instanceToRemove);
                    }
                }
#endif
            }

            RemoveGlobalIDAndSwapBack(instanceToRemove, lastInstanceIndex);

            if (m_LinkedObjects.IsValidIndex(instanceToRemove))
            {
                InstancedObjectLink linkedObject = m_LinkedObjects[instanceToRemove];
                m_LinkedObjects[instanceToRemove] = null;
                if (linkedObject && destroyLinkedObject)
                    DestroyLinkedObject(linkedObject);

                if (instanceToRemove != lastInstanceIndex && m_LinkedObjects.IsValidIndex(lastInstanceIndex) && m_LinkedObjects[lastInstanceIndex])
                    m_LinkedObjects[lastInstanceIndex].UpdateInstanceIndexInternal(instanceToRemove);

                m_LinkedObjects.RemoveAtSwapBack(instanceToRemove);
            }

            m_InstanceTransforms.RemoveAtSwapBack(instanceToRemove);
#if UNITY_EDITOR
            if (hasProceduralGUID)
                m_InstanceProceduralGUIDs.RemoveAtSwapBack(instanceToRemove);
#endif

            m_InstancePropertyArrays.RemoveInstanceSwapBack(instanceToRemove);

            m_CullingData.RemoveInstanceSwapBack(instanceToRemove, lastInstanceIndex);

#if UNITY_EDITOR
            if (m_SelectedInstances.Length == instanceCount)
            {
                m_SelectedInstances.RemoveAtSwapBack(instanceToRemove);
                m_SelectedInstancesVersion++;
            }
#endif

            SetDirty(InstancedMeshContainerChange.InstanceRemoved, instanceToRemove);
            if (instanceToRemove != lastInstanceIndex)
                SetDirty(InstancedMeshContainerChange.InstanceIndex, instanceToRemove, lastInstanceIndex);
        }

        // --- Instance Properties ---

        void RebuildInstanceProperties()
        {
            m_InstancePropertyArrays.SetProperties(m_Prototype.InstancedProperties);
            m_InstancePropertyArrays.ResizeInstances(m_InstanceCount);
        }

        void OnPrototypePropertiesChanged()
        {
            m_InstancePropertyArrays.SetProperties(m_Prototype.InstancedProperties);
        }

        void OnPrototypePropertyUpdated(InstancedPropertyDescriptor oldPropertyDescriptor, InstancedPropertyDescriptor newPropertyDescriptor)
        {
            m_InstancePropertyArrays.UpdatePropertyDescriptor(oldPropertyDescriptor, newPropertyDescriptor);
        }

        // --- Instance Proxies ---

        void CreateLinkedObject(int instanceIndex)
        {
            if (m_Prototype && m_Prototype.CreateLinkedObject)
            {
#if UNITY_EDITOR
                GameObject linkedGO = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(m_Prototype.gameObject);
                linkedGO.isStatic = true;
#else
                GameObject linkedGO = Instantiate(m_Prototype.gameObject);
#endif
                linkedGO.transform.SetParent(transform, false);
                UndoUtility.RegisterCreatedObjectUndo(linkedGO, "Create Instanced Object Link");

                InstancedObjectLink link = UndoUtility.AddComponent<InstancedObjectLink>(linkedGO);
                RegisterLinkedObjectInternal(instanceIndex, link, teleportToInstance: true);
                UndoUtility.RecordObject(link, "Create Instanced Object Link");
            }
        }

        internal void RegisterLinkedObjectInternal(int instanceIndex, InstancedObjectLink link, bool teleportToInstance)
        {
            if (m_LinkedObjects.Count != m_InstanceCount)
                m_LinkedObjects.Resize(m_InstanceCount);

            m_LinkedObjects[instanceIndex] = link;
            link.InitializeInternal(this, instanceIndex);

            if (teleportToInstance)
                link.TeleportToInstance();
        }

        internal void UnregisterLinkedObjectInternal(int instanceIndex, bool removeInstance, bool destroyLinkedObject)
        {
            if (m_IsDestroying || !m_LinkedObjects.IsValidIndex(instanceIndex))
                return;

            InstancedObjectLink linkedObject = m_LinkedObjects[instanceIndex];
            if (linkedObject != null)
            {
                linkedObject.InitializeInternal(null, -1);

                if (removeInstance)
                {
                    RemoveInstanceInternal(instanceIndex, destroyLinkedObject);
                }
                else
                {
                    if (destroyLinkedObject)
                        DestroyLinkedObject(linkedObject);

                    m_LinkedObjects[instanceIndex] = null;
                }
            }
        }

        void DestroyChildLinkedObjects()
        {
            for (int i = 0; i < m_LinkedObjects.Count; i++)
                DestroyLinkedObject(m_LinkedObjects[i]);

            m_LinkedObjects.Clear();
        }

        void RecreateChildLinkedObjects()
        {
            DestroyChildLinkedObjects();

            if (m_Prototype && m_Prototype.CreateLinkedObject)
            {
                for (int i = 0; i < m_InstanceCount; i++)
                    CreateLinkedObject(i);
            }
        }

        void DestroyLinkedObject(InstancedObjectLink link)
        {
            if (link)
            {
                if (link.IsContainerParent)
                {
                    if (!m_IsDestroying)
                        UndoUtility.DestroyObject(link.gameObject);
                }
                else
                {
                    UndoUtility.DestroyObject(link);
                }
            }
        }

        // --- Procedural Instances ---

#if UNITY_EDITOR
        internal bool HasProceduralGUIDs => m_InstanceProceduralGUIDs.Count > 0;

        internal Span<SerializableGuid> ProceduralGUIDs => m_InstanceProceduralGUIDs.AsSpan();

        internal bool HasProceduralGUID(int instanceIndex)
        {
            return m_InstanceProceduralGUIDs.IsValidIndex(instanceIndex) && m_InstanceProceduralGUIDs[instanceIndex].IsValid;
        }

        internal SerializableGuid GetInstanceProceduralGUID(int instanceIndex)
        {
            return m_InstanceProceduralGUIDs.IsValidIndex(instanceIndex) ? m_InstanceProceduralGUIDs[instanceIndex] : SerializableGuid.Empty;
        }

        internal void SetInstanceProceduralGUID(int instanceIndex, SerializableGuid proceduralGUID)
        {
            if (m_InstanceProceduralGUIDs.Count != m_InstanceCount)
                m_InstanceProceduralGUIDs.Resize(m_InstanceCount);

            m_InstanceProceduralGUIDs[instanceIndex] = proceduralGUID;

            if (m_Active)
            {
                if (proceduralGUID.IsValid)
                    m_ProceduralHash.Add(proceduralGUID, instanceIndex);
                else
                    m_ProceduralHash.Remove(proceduralGUID, instanceIndex);
            }
        }

        internal void GetInstancesWithProceduralGUID(SerializableGuid proceduralGUID, NativeList<int> result)
        {
            if (m_Active && proceduralGUID.IsValid && m_InstanceProceduralGUIDs.Count > 0)
            {
                foreach (int instanceIndex in m_ProceduralHash.GetValuesForKey(proceduralGUID))
                    result.Add(instanceIndex);
            }
        }
#endif

        // --- Global Instance IDs ---

        void RebuildGlobalIDs()
        {
            if (m_ContainerInstanceID == 0 || m_GlobalIDs.Count == m_InstanceCount)
                return;

            UnregisterGlobalIDs();
            for (int instanceIndex = 0; instanceIndex < m_InstanceCount; instanceIndex++)
                m_GlobalIDs.Add(RuntimeInstanceManager.RegisterInstance(m_ContainerInstanceID, instanceIndex));

#if UNITY_EDITOR
            SetSelectionDirty();
#endif
        }

        void UnregisterGlobalIDs()
        {
            if (m_GlobalIDs.Count == 0)
                return;

            RuntimeInstanceManager.UnregisterInstances(m_GlobalIDs.AsReadOnlySpan());
            m_GlobalIDs.Clear();
#if UNITY_EDITOR
            SetSelectionDirty();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void RemoveGlobalIDAndSwapBack(int instanceIndex, int lastInstanceIndex)
        {
            if (!m_Active || !m_GlobalIDs.IsValidIndex(instanceIndex))
                return;

            RuntimeInstanceManager.UnregisterInstance(m_GlobalIDs[instanceIndex]);
            m_GlobalIDs.RemoveAtSwapBack(instanceIndex);

            if (instanceIndex != lastInstanceIndex && m_GlobalIDs.IsValidIndex(lastInstanceIndex))
                RuntimeInstanceManager.UpdateInstanceIndex(m_GlobalIDs[lastInstanceIndex], instanceIndex);

#if UNITY_EDITOR
            SetSelectionDirty();
#endif
        }

        // --- Terrain Heightmap ---

        void OnTerrainHeightmapChanged(Terrain terrain, RectInt region, bool didSync)
        {
            if (!didSync || m_ParentTerrain != terrain || m_Prototype == null)
                return;

            TerrainData terrainData = terrain.terrainData;
            float3 terrainPosition = terrain.transform.position;
            float3 terrainSize = terrainData.size;

            float sampleToWorldScale = terrainSize.x / terrainData.heightmapResolution;
            float2 regionMin2D  = new float2(region.x, region.y) * sampleToWorldScale;
            float2 regionSize2D = new float2(region.width, region.height) * sampleToWorldScale;
            float3 regionMinWS  = new float3(regionMin2D.x, 0, regionMin2D.y) + terrainPosition;
            float3 regionMaxWS  = new float3(regionMin2D.x + regionSize2D.x, terrainSize.y, regionMin2D.y + regionSize2D.y) + terrainPosition;

            AxisAlignedBox regionBounds = new AxisAlignedBox(regionMinWS, regionMaxWS);
            AxisAlignedBox containerBounds = CalculateBounds(Space.World);
            if (!containerBounds.Overlaps(regionBounds))
                return;

            using NativeArray<int> instancesInBounds = GetInstancesInsideBounds(regionBounds, Space.World, Allocator.Temp);
            if (instancesInBounds.Length == 0)
                return;

            UndoUtility.RecordObject(this, "Terrain Heightmap Changed");
            InstancePlacementSettings placementSettings = m_Prototype.PlacementSettings;
            Random random = new Random((uint)DateTime.Now.Ticks + 1);

            for (int i = 0; i < instancesInBounds.Length; i++)
            {
                int instanceIndex = instancesInBounds[i];
                LocalTransform instanceTransform = GetInstanceTransform(instanceIndex, Space.World);

                if (regionBounds.Contains(instanceTransform.Position))
                {
                    float newHeightWS = terrain.SampleHeight(instanceTransform.Position);
                    if (!placementSettings.VerticalOffset.IsEmpty)
                        newHeightWS += placementSettings.VerticalOffset.Interpolate(random.NextFloat());

                    instanceTransform.Rotation = quaternion.identity;
                    if (placementSettings.RandomizeYaw)
                        instanceTransform.Rotation = quaternion.RotateY(random.NextFloat(math.PI * 2));

                    if (placementSettings.AlignToSurface)
                    {
                        float3 normalizedPosition = (instanceTransform.Position - terrainPosition) / terrainSize;
                        float3 newNormal = terrainData.GetInterpolatedNormal(normalizedPosition.x, normalizedPosition.z);
                        quaternion alignToSurface = Quaternion.FromToRotation(math.up(), newNormal);

                        if (placementSettings.AlignToSurfaceMaxAngle > 0.0f)
                        {
                            float maxAlignmentAngleRad = math.radians(placementSettings.AlignToSurfaceMaxAngle);
                            float currentAngle = math.acos(math.dot(newNormal, math.up()));
                            if (currentAngle > maxAlignmentAngleRad)
                            {
                                float3 rotationAxis = math.normalize(math.cross(math.up(), newNormal));
                                alignToSurface = quaternion.AxisAngle(rotationAxis, maxAlignmentAngleRad);
                            }
                        }

                        instanceTransform.Rotation = math.mul(alignToSurface, instanceTransform.Rotation);
                    }

                    instanceTransform.Position = new float3(instanceTransform.Position.x, newHeightWS, instanceTransform.Position.z);
                    UpdateInstanceTransformInternal(instanceIndex, instanceTransform, Space.World);
                }
            }
        }

        // --- Instance Placement Hash ---

        [BurstCompile, NoAlias]
        struct RemovePlacedInstancesJob : IJob
        {
            [ReadOnly] public UnsafeArray<int> InstanceIndicesToRemove;
            [ReadOnly] public UnsafeArray<LocalTransform> InstanceTransforms;
            public InstancePlacementHash<int> PlacementHash;

            public void Execute()
            {
                for (int i = 0; i < InstanceIndicesToRemove.Length; i++)
                {
                    int instanceIndex = InstanceIndicesToRemove[i];
                    ref readonly LocalTransform instanceTransform = ref InstanceTransforms[instanceIndex];
                    PlacementHash.RemoveInstance(instanceTransform.Position, instanceIndex);
                }
            }
        }

        [BurstCompile, NoAlias]
        struct AddPlacedInstancesJob : IJob
        {
            [ReadOnly] public UnsafeArray<int> InstanceIndicesToAdd;
            [ReadOnly] public UnsafeArray<LocalTransform> InstanceTransforms;
            public InstancePlacementHash<int> PlacementHash;

            public void Execute()
            {
                for (int i = 0; i < InstanceIndicesToAdd.Length; i++)
                {
                    int instanceIndex = InstanceIndicesToAdd[i];
                    ref readonly LocalTransform instanceTransform = ref InstanceTransforms[instanceIndex];
                    PlacementHash.AddInstance(instanceTransform.Position, instanceIndex);
                }
            }
        }

        [BurstCompile, NoAlias]
        struct AnyInstancesInsideSphereJob : IJob
        {
            public Sphere Sphere;
            public Space Space;
            public float4x4 WorldToLocalMatrix;
            public InstancePlacementHash<int> PlacementHash;
            public UnsafeArray<LocalTransform> InstanceTransforms;
            public UnsafeArray<bool> AnyInstanceInside;

            public void Execute()
            {
                Sphere localSphere = Space == Space.World ? Sphere.TransformBy(WorldToLocalMatrix) : Sphere;
                using NativeArray<int> instancesInBounds = PlacementHash.GetInstancesInsideBounds(localSphere.Bounds, Allocator.Temp);

                for (int i = 0; i < instancesInBounds.Length; i++)
                {
                    int instanceIndex = instancesInBounds[i];
                    LocalTransform instanceTransform = InstanceTransforms[instanceIndex];
                    if (localSphere.Contains(instanceTransform.Position))
                    {
                        AnyInstanceInside[0] = true;
                        return;
                    }
                }
            }
        }

        [BurstCompile, NoAlias]
        struct AnyInstancesWithinRadiusOfInstanceJob : IJob
        {
            public int InstanceIndex;
            public float Radius;
            public InstancePlacementHash<int> PlacementHash;
            public UnsafeArray<int> ExcludeInstances;
            public UnsafeArray<LocalTransform> InstanceTransforms;
            public UnsafeArray<bool> AnyWithinRadius;

            public void Execute()
            {
                LocalTransform instanceTransform = InstanceTransforms[InstanceIndex];
                Sphere localSphere = new Sphere(instanceTransform.Position, Radius);
                using NativeArray<int> closeInstances = PlacementHash.GetInstancesInsideBounds(localSphere.Bounds, Allocator.Temp);

                for (int i = 0; i < closeInstances.Length; i++)
                {
                    int closeInstanceIndex = closeInstances[i];
                    if (closeInstanceIndex == InstanceIndex || ExcludeInstances.IndexOf(closeInstanceIndex) > 0)
                        continue; // Skip the instance itself and any instances in the exclusion list

                    LocalTransform closeInstanceTransform = InstanceTransforms[closeInstanceIndex];
                    if (localSphere.Contains(closeInstanceTransform.Position))
                    {
                        AnyWithinRadius[0] = true;
                        return;
                    }
                }
            }
        }

        [BurstCompile, NoAlias]
        struct GetInstancesInsideBoundsJob : IJob
        {
            public AxisAlignedBox Bounds;
            public Space Space;
            public float4x4 WorldToLocalMatrix;
            public InstancePlacementHash<int> PlacementHash;
            public UnsafeArray<LocalTransform> InstanceTransforms;
            public NativeList<int> Result;

            public void Execute()
            {
                AxisAlignedBox localBounds = Space == Space.World ? Bounds.TransformBy(WorldToLocalMatrix) : Bounds;
                using NativeArray<int> instancesInBounds = PlacementHash.GetInstancesInsideBounds(localBounds, Allocator.Temp);

                Result.Clear();
                Result.Reserve(instancesInBounds.Length);

                for (int i = 0; i < instancesInBounds.Length; i++)
                {
                    int instanceIndex = instancesInBounds[i];
                    LocalTransform instanceTransform = InstanceTransforms[instanceIndex];
                    if (localBounds.Contains(instanceTransform.Position))
                        Result.Add(instanceIndex);
                }
            }
        }

        [BurstCompile, NoAlias]
        struct GetInstancesInsideSphereJob : IJob
        {
            public Sphere Sphere;
            public Space Space;
            public float4x4 WorldToLocalMatrix;
            public InstancePlacementHash<int> PlacementHash;
            public UnsafeArray<LocalTransform> LocalTransforms;
            public NativeList<int> Result;

            public void Execute()
            {
                Sphere localSphere = Space == Space.World ? Sphere.TransformBy(WorldToLocalMatrix) : Sphere;
                using NativeArray<int> instancesInBounds = PlacementHash.GetInstancesInsideBounds(localSphere.Bounds, Allocator.Temp);

                Result.Clear();
                Result.Reserve(instancesInBounds.Length);

                for (int i = 0; i < instancesInBounds.Length; i++)
                {
                    int instanceIndex = instancesInBounds[i];
                    LocalTransform instanceTransform = LocalTransforms[instanceIndex];
                    if (localSphere.Contains(instanceTransform.Position))
                        Result.Add(instanceIndex);
                }
            }
        }

        [BurstCompile, NoAlias]
        struct GetInstanceAtPositionJob : IJob
        {
            public float3 Position;
            public Space Space;
            public float4x4 WorldToLocalMatrix;
            public InstancePlacementHash<int> PlacementHash;
            public UnsafeArray<LocalTransform> LocalTransforms;
            public UnsafeArray<int> Result;

            public void Execute()
            {
                float3 localPosition = Space == Space.World ? math.transform(WorldToLocalMatrix, Position) : Position;
                AxisAlignedBox localBounds = AxisAlignedBox.FromExtents(localPosition, new float3(MathConstants.ZeroTolerance));
                Result[0] = -1;

                using NativeArray<int> instancesInBounds = PlacementHash.GetInstancesInsideBounds(localBounds, Allocator.Temp);
                float shortestDistance = float.MaxValue;

                for (int i = 0; i < instancesInBounds.Length; i++)
                {
                    int closeInstanceIndex = instancesInBounds[i];
                    LocalTransform instanceTransform = LocalTransforms[closeInstanceIndex];
                    float distance = math.lengthsq(instanceTransform.Position - localPosition);
                    if (distance < shortestDistance)
                    {
                        shortestDistance = distance;
                        Result[0] = closeInstanceIndex;
                    }
                }
            }
        }

        // --- Editor Only ---

        internal AxisAlignedBox EditorBounds => m_EditorBounds;

        internal int3 EditorCell => m_EditorCell;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int3 GetEditorCell(float3 position) => new(math.floor(position / EditorCellSize));

        [Conditional("UNITY_EDITOR")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateEditorCell()
        {
#if UNITY_EDITOR
            int3 oldEditorCell = m_EditorCell;
            m_EditorCell = new int3(math.floor(m_Transform.position / EditorCellSize));
            float3 min = m_EditorCell * EditorCellSize;
            float3 max = min + EditorCellSize;
            m_EditorBounds = new AxisAlignedBox(min, max);

            if (oldEditorCell.Equals(m_EditorCell))
                return;

            EditorSpatialHash.Instance.Remove(this);
            EditorSpatialHash.Instance.Add(this);
#endif
        }

#if UNITY_EDITOR
        internal void ClearSelection()
        {
            m_SelectedInstances.Clear();
            SetSelectionDirty();
        }

        internal bool HasSelection()
            => m_SelectedInstances.IsCreated && m_SelectedInstances.FindFirst(true) != -1;

        internal bool IsSelected(int instanceIndex)
            => m_SelectedInstances.IsValidIndex(instanceIndex) && m_SelectedInstances[instanceIndex];

        internal void SelectAll()
            => SetSelected(true, 0, m_InstanceCount - 1);

        internal void SetSelected(bool selected, int instanceIndex, int instanceCount = 1)
        {
            if (m_InstanceCount == 0) return;
            EnsureSelection();
            m_SelectedInstances.SetRange(instanceIndex, instanceCount, selected);
            SetSelectionDirty();
        }

        internal void SetSelectedIndices(bool selected, ReadOnlySpan<int> indices)
        {
            if (m_InstanceCount == 0) return;
            EnsureSelection();
            foreach (int instanceIndex in indices)
                m_SelectedInstances[instanceIndex] = selected;
            SetSelectionDirty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetSelectionDirty()
        {
            m_SelectedInstancesVersion++;
            MarkRenderStateDirty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureSelection()
        {
            if (!m_SelectedInstances.IsCreated)
                m_SelectedInstances = new UnsafeBitList(m_InstanceCount, Allocator.Persistent);
            else if (m_SelectedInstances.Length != m_InstanceCount)
                m_SelectedInstances.Resize(m_InstanceCount);
        }

        // --- Selection Frustum Culling ---

        internal void GetInstancesOverlappingFrustum_EditorOnly(NativeArray<Plane> frustumPlanes, bool checkVertices, bool insideOnly, NativeList<int> result)
        {
            if (!m_Prototype)
                return;

            AxisAlignedBox containerBounds = CalculateBounds(Space.World);
            if (FrustumUtility.IntersectBounds(frustumPlanes.AsReadOnlySpan(), containerBounds) == FrustumIntersectResult.Outside)
                return;

            Mesh.MeshDataArray modelData = default;
            JobHandle gatherHandle = default;
            UnsafeIndirectList<Vector3> vertices = default;
            UnsafeIndirectList<int> indices = default;
            UnsafeIndirectList<MeshDataSubset> subsets = default;

            if (checkVertices)
            {
                modelData = m_Prototype.AcquireLOD0Data_EditorOnly();
                if (modelData.Length > 0)
                {
                    int totalVertexCount = 0;
                    int totalIndexCount = 0;
                    for (int i = 0; i < modelData.Length; i++)
                    {
                        Mesh.MeshData meshData = modelData[i];
                        totalVertexCount += meshData.vertexCount;
                        for (int submeshIndex = 0; submeshIndex < meshData.subMeshCount; ++submeshIndex)
                        {
                            SubMeshDescriptor submesh = meshData.GetSubMesh(submeshIndex);
                            totalIndexCount += submesh.indexCount;
                        }
                    }

                    vertices = new UnsafeIndirectList<Vector3>(totalVertexCount, Allocator.TempJob);
                    indices = new UnsafeIndirectList<int>(totalIndexCount, Allocator.TempJob);
                    subsets = new UnsafeIndirectList<MeshDataSubset>(modelData.Length, Allocator.TempJob);

                    BuildMeshDataJob buildMeshDataJob = new BuildMeshDataJob
                    {
                        PrototypeData = modelData,
                        Vertices = vertices,
                        Indices = indices,
                        Subsets = subsets
                    };

                    gatherHandle = buildMeshDataJob.Schedule(gatherHandle);
                }
            }

            if (!vertices.IsCreated || !indices.IsCreated || !subsets.IsCreated)
                checkVertices = false;

            PinnedArrayView<LocalTransform> pinnedTransforms = new PinnedArrayView<LocalTransform>(m_InstanceTransforms);
            result.Reserve(pinnedTransforms.Length);

            GatherFrustumInstancesJob gatherJob = new GatherFrustumInstancesJob
            {
                InsideOnly = insideOnly,
                CheckVertices = checkVertices,
                PrototypeBounds = m_Prototype.Bounds,
                WorldToLocal = transform.worldToLocalMatrix,
                Vertices = vertices,
                Indices = indices,
                Subsets = subsets,
                WorldFrustumPlanes = frustumPlanes,
                InstanceTransforms = pinnedTransforms.AsArray(),
                Result = result.AsParallelWriter()
            };

            gatherHandle = gatherJob.ScheduleBatchByRef(pinnedTransforms.Length, GatherFrustumInstancesJob.BatchSize, gatherHandle);

            JobHandle finalHandle = JobHandle.CombineDependencies(gatherHandle, vertices.Dispose(gatherHandle));
            finalHandle = JobHandle.CombineDependencies(finalHandle, indices.Dispose(finalHandle));
            finalHandle = JobHandle.CombineDependencies(finalHandle, subsets.Dispose(finalHandle));
            finalHandle = JobHandle.CombineDependencies(finalHandle, pinnedTransforms.Dispose(finalHandle));
            finalHandle.Complete();

            if (modelData.Length > 0)
                modelData.Dispose();
        }

        struct MeshDataSubset
        {
            public int VertexStart;
            public int VertexCount;
            public int IndexStart;
            public int IndexCount;
        }

        [BurstCompile]
        struct BuildMeshDataJob : IJob
        {
            [ReadOnly] public Mesh.MeshDataArray PrototypeData;
            public UnsafeIndirectList<Vector3> Vertices;
            public UnsafeIndirectList<int> Indices;
            public UnsafeIndirectList<MeshDataSubset> Subsets;

            public void Execute()
            {
                int vertexStart = 0;
                int indexStart = 0;

                for (int i = 0; i < PrototypeData.Length; i++)
                {
                    Mesh.MeshData meshData = PrototypeData[i];
                    int vertexCount = meshData.vertexCount;
                    Vertices.Resize(vertexStart + vertexCount, NativeArrayOptions.UninitializedMemory);
                    meshData.GetVertices(Vertices.GetSubArray(vertexStart, vertexCount));

                    for (int submeshIndex = 0; submeshIndex < meshData.subMeshCount; ++submeshIndex)
                    {
                        SubMeshDescriptor submesh = meshData.GetSubMesh(submeshIndex);
                        int indexCount = submesh.indexCount;
                        Indices.Resize(indexStart + indexCount, NativeArrayOptions.UninitializedMemory);
                        meshData.GetIndices(Indices.GetSubArray(indexStart, indexCount), submeshIndex);

                        MeshDataSubset subset = new MeshDataSubset
                        {
                            VertexStart = vertexStart,
                            VertexCount = vertexCount,
                            IndexStart = indexStart,
                            IndexCount = indexCount
                        };

                        Subsets.Add(subset);
                        indexStart += indexCount;
                    }

                    vertexStart += vertexCount;
                }
            }
        }

        [BurstCompile]
        struct GatherFrustumInstancesJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public bool InsideOnly;
            public bool CheckVertices;
            public AxisAlignedBox PrototypeBounds;
            public float4x4 WorldToLocal;

            [ReadOnly] public UnsafeIndirectList<Vector3> Vertices;
            [ReadOnly] public UnsafeIndirectList<int> Indices;
            [ReadOnly] public UnsafeIndirectList<MeshDataSubset> Subsets;

            [ReadOnly] public NativeArray<Plane> WorldFrustumPlanes;
            [ReadOnly] public NativeArray<LocalTransform> InstanceTransforms;
            [WriteOnly] public NativeList<int>.ParallelWriter Result;

            public void Execute(int instanceStart, int instanceCount)
            {
                Span<Plane> localFrustumPlanes = stackalloc Plane[WorldFrustumPlanes.Length];
                for (int i = 0; i < WorldFrustumPlanes.Length; i++)
                    localFrustumPlanes[i] = WorldFrustumPlanes[i].TransformBy(WorldToLocal);

                Span<FrustumSIMDPacket> localFrustumPackets = stackalloc FrustumSIMDPacket[FrustumUtility.ComputeSIMDPacketCount(localFrustumPlanes.Length)];
                FrustumUtility.InitializeSIMDPackets(localFrustumPlanes, localFrustumPackets);

                for (int index = 0; index < instanceCount; index++)
                {
                    int instanceIndex = instanceStart + index;
                    LocalTransform instanceTransform = InstanceTransforms[instanceIndex];
                    AxisAlignedBox instanceBounds = PrototypeBounds.TransformBy(instanceTransform);

                    // Check if the instance bounds intersect with the frustum
                    FrustumIntersectResult intersectResult = FrustumUtility.IntersectBoundsSIMD(localFrustumPackets, instanceBounds);
                    switch (intersectResult)
                    {
                        case FrustumIntersectResult.Partial when !CheckVertices && !InsideOnly:
                        case FrustumIntersectResult.Outside:
                            continue;
                        case FrustumIntersectResult.Inside:
                            Result.AddNoResize(instanceIndex);
                            continue;
                        case FrustumIntersectResult.Partial when CheckVertices:
                        {
                            for (int subsetIndex = 0; subsetIndex < Subsets.Length; subsetIndex++)
                            {
                                MeshDataSubset subset = Subsets[subsetIndex];
                                NativeArray<Vector3> vertices = Vertices.GetSubArray(subset.VertexStart, subset.VertexCount);
                                NativeArray<int> indices = Indices.GetSubArray(subset.IndexStart, subset.IndexCount);

                                if (InstanceVerticesIntersectWithFrustum(instanceTransform, vertices, indices, localFrustumPlanes, localFrustumPackets))
                                {
                                    Result.AddNoResize(instanceIndex);
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool InstanceVerticesIntersectWithFrustum(
                in LocalTransform instanceTransform,
                NativeArray<Vector3> vertices, NativeArray<int> indices,
                ReadOnlySpan<Plane> planes, ReadOnlySpan<FrustumSIMDPacket> packets)
            {
                int triangleCount = indices.Length / 3;
                for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex += 3)
                {
                    int i0 = indices[triangleIndex + 0];
                    int i1 = indices[triangleIndex + 1];
                    int i2 = indices[triangleIndex + 2];

                    float3 v0 = vertices[i0];
                    float3 v1 = vertices[i1];
                    float3 v2 = vertices[i2];

                    float3 p0 = instanceTransform.TransformPoint(v0);
                    float3 p1 = instanceTransform.TransformPoint(v1);
                    float3 p2 = instanceTransform.TransformPoint(v2);

                    FrustumIntersectResult intersectResult = FrustumUtility.IntersectTriangleSIMD(planes, packets, p0, p1, p2);
                    if (intersectResult == FrustumIntersectResult.Outside)
                        continue;

                    if (!InsideOnly && intersectResult == FrustumIntersectResult.Partial)
                        return true;
                    else if (InsideOnly && intersectResult != FrustumIntersectResult.Inside)
                        return false;
                }

                return false;
            }
        }
#endif
    }
}
