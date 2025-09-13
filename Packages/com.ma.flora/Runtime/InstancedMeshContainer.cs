// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Core;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    /// <summary>
    /// Describes the different types of changes that can occur to an instance container.
    /// </summary>
    public enum InstancedMeshContainerChange
    {
        /// <summary>An instance was modified.</summary>
        Instance,
        /// <summary>An instance was added.</summary>
        InstanceAdded,
        /// <summary>An instance was removed.</summary>
        InstanceRemoved,
        /// <summary>An instance index was relocated (e.g. due to removal, or sorting).</summary>
        InstanceIndex,
        /// <summary>An instance was enabled or disabled.</summary>
        InstanceEnabled,
        /// <summary>An instance property was modified.</summary>
        InstanceProperty,
        /// <summary>The prototype was changed.</summary>
        PrototypeChanged,
        /// <summary>The container transform was changed.</summary>
        ContainerTransform,
        /// <summary>All instances were cleared.</summary>
        ContainerCleared,
        /// <summary>All instances were destroyed, including the container.</summary>
        ContainerDestroyed,
    }

    /// <summary>
    /// The primary component used to manage and render instances in the scene.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Flora/Instanced Mesh Container")]
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedMeshContainer Icon.png")]
    [HelpURL("https://flora.magneticarcade.com/components/instanced-mesh-container")]
    public sealed partial class InstancedMeshContainer : MonoBehaviour, IInstancedRenderer
    {
        /// <summary>The maximum number of instances that can be added to a single container.</summary>
        public const int MaxInstanceCount = 1 << 24;

        /// <summary>Delegate for handling changes to the container.</summary>
        /// <param name="container">The container that was modified.</param>
        /// <param name="type">The type of modification that occurred.</param>
        /// <param name="instanceIndex">The index of the instance that was modified, or -1 if the modification was not specific to an instance.</param>
        /// <param name="oldInstanceIndex">The previous index of the instance that was relocated, or -1 if the modification was not an index change.</param>
        public delegate void ChangeDelegate(InstancedMeshContainer container, InstancedMeshContainerChange type, int instanceIndex = -1, int oldInstanceIndex = -1);

        /// <summary>Event that is invoked when the instance container is modified.</summary>
        public static event ChangeDelegate Changed;

        // --- Properties ---

        /// <summary>The total number of instances in the container.</summary>
        public int InstanceCount { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_InstanceCount; }

        /// <summary>The mesh prefab used as the prototype for all instances in the container.</summary>
        public GameObject Prefab
        {
            get => m_Prototype ? m_Prototype.gameObject : null;
            set
            {
                if (value)
                {
#if UNITY_EDITOR
                    if (!UnityEditor.PrefabUtility.IsPartOfPrefabAsset(value))
                        throw new ArgumentException("InstancedMeshContainer: The GameObject must be a prefab asset.");
#endif
                    if (!value.TryGetComponent(out InstancedPrototype prototype))
                        prototype = UndoUtility.AddComponent<InstancedPrototype>(value);

                    Prototype = prototype;
                }
                else
                {
                    Prototype = null;
                }
            }
        }

        /// <summary>The prototype attached to the prefab. Used as the base model for all instances in the container.</summary>
        public InstancedPrototype Prototype
        {
            get => m_Prototype;
            set
            {
#if UNITY_EDITOR
                if (value)
                {
                    bool isPrefabInstance = UnityEditor.PrefabUtility.IsPartOfNonAssetPrefabInstance(value.gameObject);
                    if (isPrefabInstance)
                    {
                        GameObject prefab = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(value.gameObject);
                        if (!prefab)
                            throw new ArgumentException("InstancedMeshContainer: The prototype must be a prefab asset.");

                        value = prefab.GetComponent<InstancedPrototype>();
                    }
                    else if (!UnityEditor.PrefabUtility.IsPartOfPrefabAsset(value.gameObject))
                    {
                        throw new ArgumentException("InstancedMeshContainer: The prototype must be a prefab asset.");
                    }
                }
#endif

                if (m_Prototype != value)
                {
                    if (m_Prototype)
                    {
                        InstancingSystem.UnregisterRenderer(InstancedRendererID);
                        InstancedRendererID = InstancedRendererID.Null;
                        RuntimeSpatialHash.Instance.Remove(this);
                        m_Prototype.Changed -= OnPrototypeChanged;
                        m_Prototype.InstancedPropertyArrayChanged -= OnPrototypePropertiesChanged;
                        m_Prototype.InstancedPropertyUpdated -= OnPrototypePropertyUpdated;
                    }

                    m_Prototype = value;
                    m_CullingData.OutOfDate = true;

                    if (m_Prototype)
                    {
                        m_Prototype.Changed += OnPrototypeChanged;
                        m_Prototype.InstancedPropertyArrayChanged += OnPrototypePropertiesChanged;
                        m_Prototype.InstancedPropertyUpdated += OnPrototypePropertyUpdated;
                        RebuildInstanceProperties();
                        RuntimeSpatialHash.Instance.Update(this);
                        InstancedRendererID = InstancingSystem.RegisterRenderer(this);
                    }
                    else
                    {
                        m_InstancePropertyArrays.ClearProperties();
                    }

                    RecreateChildLinkedObjects();
                    SetDirty(InstancedMeshContainerChange.PrototypeChanged);
                }
            }
        }

        // --- Render State ---

        /// <summary>Marks the render state of the container as dirty.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkRenderStateDirty()
        {
            InstancingSystem.MarkRendererDirty(InstancedRendererID);
        }

        /// <summary>Marks the transforms of the container as dirty.</summary>
        /// <remarks>Will trigger a rebuild of the culling tree and upload of instance data.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkTransformDirty()
        {
            UpdateTransform();
            UpdateEditorCell();
            RuntimeSpatialHash.Instance.Update(this);
            SetDirty(InstancedMeshContainerChange.ContainerTransform);
        }

        // --- Global IDs ---

        /// <summary>The runtime instance IDs of all instances in the container.</summary>
        /// <remarks>The container must be enabled for runtime instance IDs to be valid.</remarks>
        public ReadOnlySpan<InstancedGlobalID> GlobalIDs => m_GlobalIDs.AsReadOnlySpan();

        /// <summary>Returns the <see cref="InstancedGlobalID"/> of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <returns>The <see cref="InstancedGlobalID"/> of the instance, or <see cref="InstancedGlobalID.Null"/> if the index is invalid.</returns>
        /// <remarks><see cref="InstancedGlobalID"/> is a globally unique identifier for an active instance in the scene.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedGlobalID GetGlobalInstancedID(int instanceIndex)
        {
            return m_GlobalIDs.IsValidIndex(instanceIndex)
                ? m_GlobalIDs[instanceIndex]
                : InstancedGlobalID.Null;
        }

        // --- Instances ---

        /// <summary>The transforms of all instances in the container.</summary>
        public ReadOnlySpan<LocalTransform> InstanceTransforms { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_InstanceTransforms.AsSpan(); }

        /// <summary>Returns the world transforms of all instances in the container.</summary>
        public NativeArray<LocalTransform> GetWorldInstanceTransforms(Allocator allocator)
        {
            NativeArray<LocalTransform> worldTransforms = new NativeArray<LocalTransform>(m_InstanceCount, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < m_InstanceCount; i++)
                worldTransforms[i] = GetInstanceTransform(i, Space.World);

            return worldTransforms;
        }

        /// <summary>Returns true if the specified instance index is valid.</summary>
        /// <param name="instanceIndex">The index of the instance to check.</param>
        /// <returns>True if the instance index is valid, otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Exists(int instanceIndex) => instanceIndex >= 0 && instanceIndex < m_InstanceCount;

        /// <summary>Returns the transform of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <param name="space">The target space of the transform.</param>
        /// <returns>A local transform representing the instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LocalTransform GetInstanceTransform(int instanceIndex, Space space)
        {
            LocalTransform localTransform = m_InstanceTransforms[instanceIndex];
            return space == Space.World ? m_CachedLocalToWorld.Transform(localTransform) : localTransform;
        }

        /// <summary>Returns the position of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <param name="space">The target space of the position.</param>
        /// <returns>The position of the instance in the specified space.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 GetInstancePosition(int instanceIndex, Space space)
        {
            if (!Exists(instanceIndex))
                throw new IndexOutOfRangeException("The instance index is out of range.");

            float3 position = m_InstanceTransforms[instanceIndex].Position;
            return space == Space.World ? m_CachedLocalToWorld.TransformPoint(position) : position;
        }

        /// <summary>Returns the rotation of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <param name="space">The target space of the rotation.</param>
        /// <returns>The rotation of the instance in the specified space.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public quaternion GetInstanceRotation(int instanceIndex, Space space)
        {
            quaternion rotation = m_InstanceTransforms[instanceIndex].Rotation;
            return space == Space.World ? m_CachedLocalToWorld.TransformRotation(rotation) : rotation;
        }

        /// <summary>Returns the scale of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <param name="space">The target space of the scale.</param>
        /// <returns>The scale of the instance in the specified space.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 GetInstanceScale(int instanceIndex, Space space)
        {
            float3 scale = m_InstanceTransforms[instanceIndex].Scale;
            return space == Space.World ? m_CachedLocalToWorld.TransformScale(scale) : scale;
        }

        /// <summary>Returns the bounds of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <param name="space">The target space of the bounds.</param>
        /// <returns>The bounds of the instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AxisAlignedBox GetInstanceBounds(int instanceIndex, Space space)
        {
            if (!m_Prototype) return AxisAlignedBox.Empty;
            AxisAlignedBox instanceBounds = m_Prototype.Bounds.TransformBy(m_InstanceTransforms[instanceIndex]);
            return space == Space.World ? instanceBounds.TransformBy(m_Transform.localToWorldMatrix) : instanceBounds;
        }

        /// <summary>Clears all instances from the container.</summary>
        public void ClearInstances()
        {
            UnregisterGlobalIDs();
            DestroyChildLinkedObjects();

            m_CullingData.ClearData();
            m_InstanceTransforms.Clear();
            m_InstancePropertyArrays.ClearInstances();

            m_InstanceEnabled.Clear();
            m_PlacementHash.Clear();

            m_InstanceOrderVersion++;
            m_InstanceTransformsVersion++;

#if UNITY_EDITOR
            m_InstanceProceduralGUIDs.Clear();
            m_ProceduralHash.Clear();

            m_SelectedInstances.Clear();
            m_SelectedInstancesVersion++;
#endif

            SetDirty(InstancedMeshContainerChange.ContainerCleared);
        }

        /// <summary>Reserves space for the specified number of instances.</summary>
        /// <param name="instanceCount">The number of instances to reserve space for.</param>
        /// <remarks>Reserving space will help reduce the number of reallocations when adding instances.</remarks>
        public void Reserve(int instanceCount)
        {
            if (m_InstanceTransforms.Capacity < instanceCount)
            {
                int newCapacity = math.max(math.ceilpow2(instanceCount), 64);

                m_InstanceTransforms.Capacity = newCapacity;
                m_GlobalIDs.Capacity = newCapacity;
                m_CullingData.RenderIndexLookup.Reserve(newCapacity);

                if (m_PlacementHash.Capacity < newCapacity)
                    m_PlacementHash.Capacity = newCapacity;

#if UNITY_EDITOR
                m_InstanceProceduralGUIDs.Capacity = newCapacity;
                if (m_ProceduralHash.Capacity < newCapacity)
                    m_ProceduralHash.Capacity = newCapacity;
#endif
            }
        }

        /// <summary>Reserves space for the specified number of additional instances.</summary>
        /// <param name="additionalInstanceCount">The number of additional instances to reserve space for.</param>
        /// <remarks>Reserving space will help reduce the number of reallocations when adding instances.</remarks>
        public void ReserveAdditional(int additionalInstanceCount)
        {
            Reserve(m_InstanceCount + additionalInstanceCount);

            if (m_Prototype)
            {
                m_InstancePropertyArrays.ReserveInstances(m_InstanceCount + additionalInstanceCount);
                m_CullingData.UnbuiltInstanceBounds.ReserveAdditional(additionalInstanceCount);
            }
        }

        /// <summary>Adds a new instance to the container.</summary>
        /// <param name="position">The position of the instance.</param>
        /// <param name="rotation">The rotation of the instance.</param>
        /// <param name="scale">The scale of the instance.</param>
        /// <param name="space">The space of the transform.</param>
        /// <returns>The index of the new instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddInstance(float3 position, quaternion rotation, float3 scale, Space space)
            => AddInstance(LocalTransform.FromPositionRotationScale(position, rotation, scale), space);

        /// <summary>Adds a new instance to the container.</summary>
        /// <param name="position">The position of the instance.</param>
        /// <param name="rotation">The rotation of the instance.</param>
        /// <param name="space">The space of the transform.</param>
        /// <returns>The index of the new instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddInstance(float3 position, quaternion rotation, Space space)
            => AddInstance(LocalTransform.FromPositionRotationScale(position, rotation, 1), space);

        /// <summary>Adds a new instance to the container.</summary>
        /// <param name="position">The position of the instance.</param>
        /// <param name="space">The space of the transform.</param>
        /// <returns>The index of the new instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddInstance(float3 position, Space space)
            => AddInstance(LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1), space);

        /// <summary>Adds a new instance to the container.</summary>
        /// <param name="transform">The transform of the instance to add.</param>
        /// <param name="space">The space of the transform.</param>
        /// <returns>The index of the new instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddInstance(in LocalTransform transform, Space space)
            => AddInstanceInternal(transform, space, true);

        /// <summary>Adds the specified number of instances to the container.</summary>
        /// <param name="instances">The transforms of the instances to add.</param>
        /// <param name="space">The space of the transforms.</param>
        public void AddInstances(ReadOnlySpan<LocalTransform> instances, Space space)
        {
            ReserveAdditional(instances.Length);
            for (int i = 0; i < instances.Length; i++)
                AddInstanceInternal(instances[i], space, true);
        }

        /// <summary>Updates the position of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance to update.</param>
        /// <param name="position">The new position of the instance.</param>
        /// <param name="space">The space of the new position.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstancePosition(int instanceIndex, float3 position, Space space)
        {
            LocalTransform oldLocalTransform = m_InstanceTransforms[instanceIndex];
            float3 newLocalPosition = space == Space.World ? m_CachedLocalToWorld.InverseTransformPoint(position) : position;
            LocalTransform newLocalTransform = new LocalTransform(newLocalPosition, oldLocalTransform.Rotation, oldLocalTransform.Scale);
            UpdateInstanceTransformInternal(instanceIndex, newLocalTransform, Space.Self);
        }

        /// <summary>Updates the position of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance to update.</param>
        /// <param name="rotation">The new rotation of the instance.</param>
        /// <param name="space">The space of the new rotation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceRotation(int instanceIndex, quaternion rotation, Space space)
        {
            LocalTransform oldLocalTransform = m_InstanceTransforms[instanceIndex];
            quaternion newLocalRotation = space == Space.World ? m_CachedLocalToWorld.InverseTransformRotation(rotation) : rotation;
            LocalTransform newLocalTransform = new LocalTransform(oldLocalTransform.Position, newLocalRotation, oldLocalTransform.Scale);
            UpdateInstanceTransformInternal(instanceIndex, newLocalTransform, Space.Self);
        }


        /// <summary>Updates the position of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance to update.</param>
        /// <param name="scale">The new scale of the instance.</param>
        /// <param name="space">The space of the new scale.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceScale(int instanceIndex, float3 scale, Space space)
        {
            LocalTransform oldLocalTransform = m_InstanceTransforms[instanceIndex];
            float3 newLocalScale = space == Space.World ? m_CachedLocalToWorld.InverseTransformScale(scale) : scale;
            LocalTransform newLocalTransform = new LocalTransform(oldLocalTransform.Position, oldLocalTransform.Rotation, newLocalScale);
            UpdateInstanceTransformInternal(instanceIndex, newLocalTransform, Space.Self);
        }

        /// <summary>Updates the transform of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance to update.</param>
        /// <param name="position">The new position of the instance.</param>
        /// <param name="rotation">The new rotation of the instance.</param>
        /// <param name="scale">The new scale of the instance.</param>
        /// <param name="space">The space of position, rotation, and scale.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceTransform(int instanceIndex, float3 position, quaternion rotation, float3 scale, Space space)
            => UpdateInstanceTransform(instanceIndex, LocalTransform.FromPositionRotationScale(position, rotation, scale), space);

        /// <summary>Updates the transform of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance to update.</param>
        /// <param name="position">The new position of the instance.</param>
        /// <param name="rotation">The new rotation of the instance.</param>
        /// <param name="space">The space of position and rotation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceTransform(int instanceIndex, float3 position, quaternion rotation, Space space)
            => UpdateInstanceTransform(instanceIndex, LocalTransform.FromPositionRotationScale(position, rotation, 1), space);

        /// <summary>Updates the transform of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance to update.</param>
        /// <param name="position">The new position of the instance.</param>
        /// <param name="space">The space of position.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceTransform(int instanceIndex, float3 position, Space space)
            => UpdateInstanceTransform(instanceIndex, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1), space);

        /// <summary>Updates the transform of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance to update.</param>
        /// <param name="newTransform">The new transform of the instance.</param>
        /// <param name="space">The space of the new transform.</param>
        /// <exception cref="IndexOutOfRangeException">Thrown if the instance index is out of range.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceTransform(int instanceIndex, in LocalTransform newTransform, Space space)
            => UpdateInstanceTransformInternal(instanceIndex, newTransform, space);

        /// <summary>Updates the transform of the instances starting at the specified index.</summary>
        /// <param name="startInstanceIndex">The index of the first instance to update.</param>
        /// <param name="newTransforms">The new transforms of the instances.</param>
        /// <param name="space">The space of the new transforms.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the number of instance transforms exceeds the number of instances in the container.</exception>
        public void UpdateInstanceTransforms(int startInstanceIndex, ReadOnlySpan<LocalTransform> newTransforms, Space space)
        {
            m_CullingData.UnbuiltInstanceBounds.ReserveAdditional(newTransforms.Length);

            for (int i = 0; i < newTransforms.Length; i++)
                UpdateInstanceTransformInternal(startInstanceIndex + i, newTransforms[i], space);
        }

        /// <summary>Updates the transform of the instances with the specified indices.</summary>
        /// <param name="instances">The indices of the instances to update.</param>
        /// <param name="newTransforms">The new transforms of the instances.</param>
        /// <param name="space">The space of the transforms.</param>
        /// <exception cref="ArgumentException">Thrown if the number of instance indices does not match the number of instance transforms.</exception>
        public void UpdateInstanceTransforms(ReadOnlySpan<int> instances, ReadOnlySpan<LocalTransform> newTransforms, Space space)
        {
            if (instances.Length != newTransforms.Length)
                throw new ArgumentException("The number of instance indices must match the number of instance transforms.", nameof(newTransforms));

            m_CullingData.UnbuiltInstanceBounds.ReserveAdditional(newTransforms.Length);

            for (int i = 0; i < newTransforms.Length; i++)
                UpdateInstanceTransformInternal(instances[i], newTransforms[i], space);
        }

        /// <summary>Call to start a batch of instance updates, such as moving multiple instances. This will delay expensive operations until <see cref="EndBatchMove"/>.</summary>
        /// <param name="instancesToMove">The indices of the instances that will be moved.</param>
        public unsafe void BeginBatchMove(ReadOnlySpan<int> instancesToMove)
        {
            if (!m_MovingBatch)
            {
                m_MovingBatch = true;
                m_MovingSavedAutoRebuild = m_CullingData.AutoBuildEnabled;
                m_CullingData.AutoBuildEnabled = false;
                m_MovingBatchIndices.Clear();
                m_MovingBatchIndices.AddRange(instancesToMove);

                fixed (LocalTransform* instanceTransformsPtr = m_InstanceTransforms.AsSpan())
                {
                    RemovePlacedInstancesJob removeFromHashJob = new RemovePlacedInstancesJob
                    {
                        InstanceIndicesToRemove = m_MovingBatchIndices.AsUnsafeArray(),
                        InstanceTransforms = new UnsafeArray<LocalTransform>(instanceTransformsPtr, m_InstanceTransforms.Count),
                        PlacementHash = m_PlacementHash,
                    };
                    m_MovingBatchHandle = removeFromHashJob.Schedule();
                }
            }
        }

        /// <summary>Called after update methods that were performed after <see cref="BeginBatchMove"/> to update the placement hash.</summary>
        public unsafe void EndBatchMove()
        {
            if (m_MovingBatch)
            {
                m_MovingBatch = false;
                m_CullingData.AutoBuildEnabled = m_MovingSavedAutoRebuild;

                fixed (LocalTransform* instanceTransformsPtr = m_InstanceTransforms.AsSpan())
                {
                    AddPlacedInstancesJob addToHashJob = new AddPlacedInstancesJob
                    {
                        InstanceIndicesToAdd = m_MovingBatchIndices.AsUnsafeArray(),
                        InstanceTransforms = new UnsafeArray<LocalTransform>(instanceTransformsPtr, m_InstanceTransforms.Count),
                        PlacementHash = m_PlacementHash,
                    };
                    m_MovingBatchHandle = addToHashJob.Schedule(m_MovingBatchHandle);
                    m_MovingBatchHandle.Complete();
                }
            }
        }

        /// <summary>Removes all instances with the specified indices.</summary>
        /// <param name="instancesToRemove">The indices of the instances to remove.</param>
        /// <param name="alreadyReverseSorted">True if the indices are already sorted, in reverse order (high to low).</param>
        public unsafe void RemoveInstances(ReadOnlySpan<int> instancesToRemove, bool alreadyReverseSorted = false)
        {
            if (instancesToRemove.Length == 0)
                return;

            fixed (int* pinnedIndices = instancesToRemove)
            {
                UnsafeArray<int> sortedIndices;
                if (!alreadyReverseSorted)
                {
                    if (instancesToRemove.Length <= 4096)
                    {
                        int* stackIndices = stackalloc int[instancesToRemove.Length];
                        sortedIndices = new UnsafeArray<int>(stackIndices, instancesToRemove.Length, AllocatorManager.None);
                    }
                    else
                    {
                        sortedIndices = new UnsafeArray<int>(instancesToRemove.Length, AllocatorManager.Temp);
                    }

                    UnsafeUtility.MemCpy(sortedIndices.Ptr, pinnedIndices, instancesToRemove.Length * sizeof(int));
                    NativeSortExtension.Sort(sortedIndices.Ptr, instancesToRemove.Length, new OrderDescending());
                }
                else
                {
                    sortedIndices = new UnsafeArray<int>(pinnedIndices, instancesToRemove.Length);
                }

                for (int i = 0; i < instancesToRemove.Length; i++)
                    RemoveInstanceInternal(sortedIndices[i]);

                sortedIndices.Dispose();
            }
        }

        /// <summary>Removes the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance to remove.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveInstance(int instanceIndex)
            => RemoveInstanceInternal(instanceIndex);

        // --- Enableable Instances ---

        /// <summary>Returns true if the instance at the specified index is enabled.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <returns>True if the instance is enabled, otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInstanceEnabled(int instanceIndex)
            => m_InstanceEnabled.IsCreated && m_InstanceEnabled[instanceIndex];

        /// <summary>Sets the visibility of the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <param name="enabled">True if the instance should be visible, otherwise false.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInstanceEnabled(int instanceIndex, bool enabled)
        {
            if (m_InstanceEnabled.IsCreated)
            {
                if (m_InstanceEnabled[instanceIndex] != enabled)
                {
                    m_InstanceEnabled[instanceIndex] = enabled;
                    SetDirty(InstancedMeshContainerChange.InstanceEnabled, instanceIndex);
                }

#if UNITY_EDITOR
                if (!enabled && m_SelectedInstances.Length > 0)
                    SetSelected(false, instanceIndex);
#endif
            }
        }

        // --- Instance Properties ---

        /// <summary>Returns true if the container has an instanced property with the specified name.</summary>
        /// <param name="name">The name of the instanced property.</param>
        /// <returns>True if the container has an instanced property with the specified name, otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasInstancedProperty(string name)
            => HasInstancedProperty(Shader.PropertyToID(name));

        /// <summary>Returns true if the container has an instanced property with the specified name ID.</summary>
        /// <param name="nameID">The name ID of the instanced property.</param>
        /// <returns>True if the container has an instanced property with the specified name ID, otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasInstancedProperty(int nameID)
            => m_InstancePropertyArrays.HasProperty(nameID);

        /// <summary>Returns the value of the instanced property with the specified name.</summary>
        /// <param name="name">The name of the instanced property.</param>
        /// <param name="instanceIndex">The index of the instance to get the property value from.</param>
        /// <returns>The value of the instanced property.</returns>
        /// <remarks>Use this method to get the value of an instanced property. The property must exist in the container.</remarks>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the property does not exist.</exception>
        /// <exception cref="ArgumentException">The type of the property value does not match the type of the property.</exception>
        /// <exception cref="ArgumentException">The instance index is out of range.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetInstancedProperty<T>(string name, int instanceIndex) where T : unmanaged
            => GetInstancedProperty<T>(Shader.PropertyToID(name), instanceIndex);

        /// <summary>Returns the value of the instanced property with the specified name ID.</summary>
        /// <param name="nameID">The name ID of the instanced property.</param>
        /// <param name="instanceIndex">The index of the instance to get the property value from.</param>
        /// <returns>The value of the instanced property.</returns>
        /// <remarks>Use this method to get the value of an instanced property. The property must exist in the container.</remarks>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the property does not exist.</exception>
        /// <exception cref="ArgumentException">The type of the property value does not match the type of the property.</exception>
        /// <exception cref="ArgumentException">The instance index is out of range.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetInstancedProperty<T>(int nameID, int instanceIndex) where T : unmanaged
            => m_InstancePropertyArrays.GetPropertyValue<T>(nameID, instanceIndex);

        /// <summary>Set the value of the instanced property with the specified name.</summary>
        /// <param name="name">The name of the instanced property.</param>
        /// <param name="instanceIndex">The index of the instance to set the property value for.</param>
        /// <param name="value">The new value of the instanced property.</param>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the property does not exist.</exception>
        /// <exception cref="ArgumentException">The type of the property value does not match the type of the property.</exception>
        /// <exception cref="ArgumentException">The instance index is out of range.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInstancedProperty<T>(string name, int instanceIndex, T value) where T : unmanaged
            => SetInstancedProperty(Shader.PropertyToID(name), instanceIndex, value);

        /// <summary>Set the value of the instanced property with the specified name ID.</summary>
        /// <param name="nameID">The name ID of the instanced property.</param>
        /// <param name="instanceIndex">The index of the instance to set the property value for.</param>
        /// <param name="value">The new value of the instanced property.</param>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the property does not exist.</exception>
        /// <exception cref="ArgumentException">The type of the property value does not match the type of the property.</exception>
        /// <exception cref="ArgumentException">The instance index is out of range.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInstancedProperty<T>(int nameID, int instanceIndex, T value) where T : unmanaged
        {
            m_InstancePropertyArrays.SetPropertyValue(nameID, instanceIndex, value);
            SetDirty(InstancedMeshContainerChange.InstanceProperty, instanceIndex);
        }

        /// <summary>Sets a range of instanced property values for the specified property name.</summary>
        /// <param name="name">The name of the instanced property.</param>
        /// <param name="startInstanceIndex">The index of the first instance to set the property value for.</param>
        /// <param name="values">The new values of the instanced property.</param>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the property does not exist.</exception>
        /// <exception cref="ArgumentException">The type of the property value does not match the type of the property.</exception>
        /// <exception cref="ArgumentException">The range of instance indices is out of range.</exception>
        public void SetInstancedPropertyRange<T>(string name, int startInstanceIndex, ReadOnlySpan<T> values) where T : unmanaged
            => SetInstancedPropertyRange(Shader.PropertyToID(name), startInstanceIndex, values);

        /// <summary>Sets a range of instanced property values for the specified property name ID.</summary>
        /// <param name="nameID">The name ID of the instanced property.</param>
        /// <param name="startInstanceIndex">The index of the first instance to set the property values for.</param>
        /// <param name="values">The new values of the instanced property.</param>
        /// <typeparam name="T">The type of the property values.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the property does not exist.</exception>
        /// <exception cref="ArgumentException">The type of the property values does not match the type of the property.</exception>
        /// <exception cref="ArgumentException">The range of instance indices is out of range.</exception>
        public void SetInstancedPropertyRange<T>(int nameID, int startInstanceIndex, ReadOnlySpan<T> values) where T : unmanaged
        {
            m_InstancePropertyArrays.SetPropertyValueRange(nameID, startInstanceIndex, values);
            SetDirty(InstancedMeshContainerChange.InstanceProperty);
        }

        // --- Linked Objects ---

        /// <summary>Returns true if any instances in the container have linked objects.</summary>
        public bool HasLinkedObjects
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_LinkedObjects.Count > 0;
        }

        /// <summary>Checks if the instance at the specified index has a <see cref="GameObject"/> attached.</summary>
        /// <param name="instanceIndex">The index of the instance to check.</param>
        /// <returns>True if the instance has a <see cref="GameObject"/> attached; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasLinkedObject(int instanceIndex)
            => m_LinkedObjects.IsValidIndex(instanceIndex) && m_LinkedObjects[instanceIndex] != null;

        /// <summary>Returns the <see cref="GameObject"/> attached to the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <returns>The <see cref="GameObject"/> attached to the instance, or null if no <see cref="GameObject"/> is attached.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the instance index is not a valid instance.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GameObject GetLinkedGameObject(int instanceIndex)
        {
            return m_LinkedObjects.IsValidIndex(instanceIndex)
                ? (m_LinkedObjects[instanceIndex] ? m_LinkedObjects[instanceIndex].gameObject : null)
                : null;
        }

        /// <summary>Returns the <see cref="InstancedObjectLink"/> attached to the instance at the specified index.</summary>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <returns>The <see cref="InstancedObjectLink"/> attached to the instance, or null if no <see cref="InstancedObjectLink"/> is attached.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the instance index is not a valid instance.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedObjectLink GetLinkedObject(int instanceIndex)
        {
            return m_LinkedObjects.IsValidIndex(instanceIndex)
                ? m_LinkedObjects[instanceIndex]
                : null;
        }

        /// <summary>Adds a <see cref="GameObject"/> as a link to an instance in the container.</summary>
        /// <param name="gameObject">The <see cref="GameObject"/> to link to the instance.</param>
        /// <returns>The index of the new instance.</returns>
        /// <remarks>
        /// Creates a new instance and attaches the <see cref="GameObject"/> to it.
        /// An <see cref="InstancedObjectLink"/> will be added to the <see cref="GameObject"/> if it does not already exist.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if the <see cref="GameObject"/> is null.</exception>
        public int AddLinkedObject(GameObject gameObject)
        {
            if (!gameObject)
                throw new ArgumentNullException(nameof(gameObject), "The game object cannot be null.");

            if (!gameObject.TryGetComponent(out InstancedObjectLink link))
                link = UndoUtility.AddComponent<InstancedObjectLink>(gameObject);

            return AddLinkedObject(link);
        }

        /// <summary>Adds an <see cref="InstancedObjectLink"/> to an instance in the container.</summary>
        /// <param name="link">The <see cref="InstancedObjectLink"/> to add to the container.</param>
        /// <returns>The index of the new instance.</returns>
        /// <remarks>
        /// Creates a new instance and attaches the <see cref="InstancedObjectLink"/> to it.
        /// An <see cref="InstancedObjectLink"/> will be added to the <see cref="GameObject"/> if it does not already exist.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if the <see cref="InstancedObjectLink"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the prefab asset does not match the container prototype.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the link is already attached to a container.</exception>
        public int AddLinkedObject(InstancedObjectLink link)
        {
            if (!link)
                throw new ArgumentNullException(nameof(link), "The link cannot be null.");
            if (!Prototype)
                throw new ArgumentException("The container must have a prototype assigned.");
            if (link.Container)
                throw new InvalidOperationException("The link is already attached to a container.");

            if (link.IsRenderable)
            {
                if (!link.PrefabGuid.IsValid)
                    throw new ArgumentException("The link must have been created from a valid prefab asset.");
                if (link.PrefabGuid != m_Prototype.PrefabGuid)
                    throw new ArgumentException("The link instance must be created from the same prefab as the container.");
            }

            Space space = link.transform.parent == transform ? Space.Self : Space.World;
            int instanceIndex = AddInstanceInternal(link.transform.GetTransform(space), space, false);
            RegisterLinkedObjectInternal(instanceIndex, link, false);
            return instanceIndex;
        }

        /// <summary>Links a <see cref="GameObject"/> to an existing instance in the container.</summary>
        /// <param name="instanceIndex">The index of the instance to attach the <see cref="GameObject"/> to.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <see cref="GameObject"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the instance index is out of range.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the instance already has a link attached.</exception>
        /// <exception cref="ArgumentException">Thrown if a renderable <see cref="InstancedObjectLink"/> does not match the container prototype.</exception>
        public GameObject InstantiateLinkedObject(int instanceIndex)
        {
            if (!Exists(instanceIndex))
                throw new ArgumentOutOfRangeException(nameof(instanceIndex), "The instance index is not a valid instance.");
            if (HasLinkedObject(instanceIndex))
                throw new InvalidOperationException("The instance already has a link attached.");

#if UNITY_EDITOR
            GameObject prefabInstance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(Prefab);
#else
            GameObject prefabInstance = Instantiate(Prefab);
#endif
            prefabInstance.transform.SetParent(transform, false);
            AttachLinkedObject(instanceIndex, prefabInstance, teleportToInstance: true);

            return prefabInstance;
        }

        /// <summary>Links a <see cref="GameObject"/> to an existing instance in the container.</summary>
        /// <param name="instanceIndex">The index of the instance to attach the <see cref="GameObject"/> to.</param>
        /// <param name="gameObject">The <see cref="GameObject"/> to link to the instance.</param>
        /// <param name="teleportToInstance">True if the <see cref="GameObject"/> should be moved to the instance position; otherwise, the instance will be moved to the <see cref="GameObject"/> position.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <see cref="GameObject"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the instance index is out of range.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the instance already has a link attached.</exception>
        /// <exception cref="ArgumentException">Thrown if a renderable <see cref="InstancedObjectLink"/> does not match the container prototype.</exception>
        public void AttachLinkedObject(int instanceIndex, GameObject gameObject, bool teleportToInstance)
        {
            if (!gameObject)
                throw new ArgumentNullException(nameof(gameObject), "The game object cannot be null.");

            if (!gameObject.TryGetComponent(out InstancedObjectLink link))
                link = UndoUtility.AddComponent<InstancedObjectLink>(gameObject);

            AttachLinkedObject(instanceIndex, link, teleportToInstance);
        }

        /// <summary>Links an <see cref="InstancedObjectLink"/> to an existing instance in the container.</summary>
        /// <param name="instanceIndex">The index of the instance to attach the link to.</param>
        /// <param name="link">The <see cref="InstancedObjectLink"/> to attach to the instance.</param>
        /// <param name="teleportToInstance">True if the <see cref="InstancedObjectLink"/> should be moved to the instance position; otherwise, the instance will be moved to the <see cref="GameObject"/> position.</param>
        /// <exception cref="ArgumentNullException">Thrown if the link is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the instance index is out of range.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the instance already has a link attached.</exception>
        /// <exception cref="ArgumentException">Thrown if a renderable <see cref="InstancedObjectLink"/> does not match the container prototype.</exception>
        public void AttachLinkedObject(int instanceIndex, InstancedObjectLink link, bool teleportToInstance)
        {
            if (!link)
                throw new ArgumentNullException(nameof(link), "The link cannot be null.");
            if (!Prototype)
                throw new ArgumentException("The container must have a prototype assigned.");
            if (!Exists(instanceIndex))
                throw new ArgumentOutOfRangeException(nameof(instanceIndex), "The instance index is not a valid instance.");
            if (m_LinkedObjects.IsValidIndex(instanceIndex) && m_LinkedObjects[instanceIndex] != null)
                throw new InvalidOperationException("The instance already has a link attached.");

            if (link.IsRenderable)
            {
                if (!link.PrefabGuid.IsValid)
                    throw new ArgumentException("The link must have been created from a valid prefab asset.");
                if (link.PrefabGuid != m_Prototype.PrefabGuid)
                    throw new ArgumentException("The link instance must be created from the same prefab as the container.");
            }

            RegisterLinkedObjectInternal(instanceIndex, link, teleportToInstance);
        }

        /// <summary>Unlinks an <see cref="InstancedObjectLink"/> from an existing instance in the container.</summary>
        /// <param name="instanceIndex">The index of the instance to detach the link from.</param>
        /// <param name="removeInstance">True if the instance should be removed from the container; otherwise, the instance will remain in the container.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the instance index is out of range.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not have a link attached.</exception>
        public void DetachLinkedObject(int instanceIndex, bool removeInstance = true)
        {
            if (!Exists(instanceIndex))
                throw new ArgumentOutOfRangeException(nameof(instanceIndex), "The instance index is not a valid instance.");
            if (!m_LinkedObjects.IsValidIndex(instanceIndex) || m_LinkedObjects[instanceIndex] == null)
                throw new InvalidOperationException("The instance does not have a link attached.");

            UnregisterLinkedObjectInternal(instanceIndex, removeInstance, false);
        }

        /// <summary>Unlinks all <see cref="InstancedObjectLink"/> instances from the container.</summary>
        /// <param name="destroyLinkedObjects">True if the linked objects should be destroyed; otherwise, the linked objects will remain in the scene.</param>
        /// <param name="removeInstances">True if the instances should be removed from the container; otherwise, the instances will remain in the container.</param>
        public void DetachAllLinkedObjects(bool destroyLinkedObjects, bool removeInstances = true)
        {
            for (int i = 0; i < m_LinkedObjects.Count; i++)
            {
                if (m_LinkedObjects.IsValidIndex(i) && m_LinkedObjects[i] != null)
                {
                    UnregisterLinkedObjectInternal(i, removeInstances, destroyLinkedObjects);
                }
            }

            m_LinkedObjects.Clear();
        }

        // --- Queries ---

        /// <summary>Calculates the bounds of all instances in the container.</summary>
        /// <param name="space">The target space of the bounds.</param>
        /// <returns>An axis-aligned box that encapsulates all instances in the container.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AxisAlignedBox CalculateBounds(Space space)
        {
            AxisAlignedBox result = m_CullingData.BuiltBounds + m_CullingData.UnbuiltInstanceBoundsCombined;
            return space == Space.World ? result.TransformBy(m_Transform.localToWorldMatrix) : result;
        }

        /// <summary>Checks if the container has any instances overlapping with the specified ray.</summary>
        /// <param name="ray">The ray to check for overlapping instances.</param>
        /// <param name="space">The space of the ray.</param>
        /// <param name="hitInstanceIndex">The index of the instance that was hit, or -1 if no instance was hit.</param>
        /// <returns>True if any instances overlap with the ray, otherwise false.</returns>
        public bool AnyInstanceOverlapsRay(Ray ray, Space space, out int hitInstanceIndex)
        {
            hitInstanceIndex = -1;
            if (m_InstanceCount == 0 || !m_Prototype)
                return false;

            Ray localRay = space == Space.World ? ray.TransformBy(m_Transform.worldToLocalMatrix) : ray;
            AxisAlignedBox prototypeBounds = m_Prototype.Bounds;

            if (m_CullingData.IsBuilt && m_CullingData.OverlapsRay(localRay, out int hitNodeIndex))
            {
                int firstInstanceIndex = m_CullingData.BuiltNodes[hitNodeIndex].FirstInstance;
                int lastInstanceIndex = m_CullingData.BuiltNodes[hitNodeIndex].LastInstance;

                for (int i = firstInstanceIndex; i <= lastInstanceIndex; i++)
                {
                    int instanceIndex = m_CullingData.GetInstanceIndexByRenderIndex(i);
                    if (instanceIndex == -1)
                        continue;

                    AxisAlignedBox instanceBounds = prototypeBounds.TransformBy(m_InstanceTransforms[instanceIndex]);
                    if (instanceBounds.Overlaps(localRay))
                    {
                        hitInstanceIndex = instanceIndex;
                        return true;
                    }
                }
            }
            else
            {
                for (int instanceIndex = 0; instanceIndex < m_InstanceCount; instanceIndex++)
                {
                    AxisAlignedBox instanceBounds = prototypeBounds.TransformBy(m_InstanceTransforms[instanceIndex]);
                    if (instanceBounds.Overlaps(localRay))
                    {
                        hitInstanceIndex = instanceIndex;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Adds the instance indices of all instances that overlap with the specified ray to the provided list.</summary>
        /// <param name="ray">The ray to check for overlapping instances.</param>
        /// <param name="space">The space of the ray.</param>
        /// <param name="hitInstances">The list to store the indices of the instances that were hit.</param>
        /// <returns>The number of instances that were hit.</returns>
        public int GetInstancesOverlappingRay(Ray ray, Space space, NativeList<int> hitInstances)
        {
            if (m_InstanceCount == 0 || !m_Prototype)
                return 0;

            Ray localRay = space == Space.World ? ray.TransformBy(m_Transform.worldToLocalMatrix) : ray;
            AxisAlignedBox prototypeBounds = m_Prototype.Bounds;

            if (m_CullingData.IsBuilt && m_CullingData.OverlapsRay(localRay, out int hitNodeIndex))
            {
                int firstInstanceIndex = m_CullingData.BuiltNodes[hitNodeIndex].FirstInstance;
                int lastInstanceIndex = m_CullingData.BuiltNodes[hitNodeIndex].LastInstance;

                for (int i = firstInstanceIndex; i <= lastInstanceIndex; i++)
                {
                    int instanceIndex = m_CullingData.GetInstanceIndexByRenderIndex(i);
                    if (instanceIndex == -1)
                        continue;

                    AxisAlignedBox instanceBounds = prototypeBounds.TransformBy(m_InstanceTransforms[instanceIndex]);
                    if (instanceBounds.Overlaps(localRay))
                        hitInstances.Add(instanceIndex);
                }
            }
            else
            {
                for (int instanceIndex = 0; instanceIndex < m_InstanceCount; instanceIndex++)
                {
                    AxisAlignedBox instanceBounds = prototypeBounds.TransformBy(m_InstanceTransforms[instanceIndex]);
                    if (instanceBounds.Overlaps(localRay))
                        hitInstances.Add(instanceIndex);
                }
            }

            return hitInstances.Length;
        }

        /// <summary>Checks if the container has any instances overlapping with the specified sphere.</summary>
        /// <param name="sphere">The sphere to check for overlapping instances.</param>
        /// <param name="space">The space of the sphere.</param>
        /// <returns>True if any instances overlap with the sphere, otherwise false.</returns>
        public unsafe bool AnyInstancesInsideSphere(Sphere sphere, Space space)
        {
            fixed (LocalTransform* transforms = m_InstanceTransforms.AsSpan())
            {
                bool* anyInstanceInside = stackalloc bool[1];
                new AnyInstancesInsideSphereJob
                {
                    Sphere = sphere,
                    Space = space,
                    WorldToLocalMatrix = m_Transform.worldToLocalMatrix,
                    PlacementHash = m_PlacementHash,
                    InstanceTransforms = new UnsafeArray<LocalTransform>(transforms, m_InstanceTransforms.Count),
                    AnyInstanceInside = new UnsafeArray<bool>(anyInstanceInside, 1)
                }.Run();

                return anyInstanceInside[0];
            }
        }

        /// <summary>Checks if the container has any instances within <paramref name="radius"/> of <paramref name="instanceIndex"/>, excluding instances in <paramref name="excludeInstances"/>.</summary>
        /// <param name="instanceIndex">The instance to check for overlapping instances.</param>
        /// <param name="radius">The radius to check for overlapping instances.</param>
        /// <param name="excludeInstances">The instances indices to exclude from the check.</param>
        /// <returns>True if any instances overlap with the sphere, otherwise false.</returns>
        public unsafe bool AnyInstancesWithinRadiusOfInstance(int instanceIndex, float radius, ReadOnlySpan<int> excludeInstances)
        {
            fixed (LocalTransform* transforms = m_InstanceTransforms.AsSpan())
            fixed (int* excludeInstancesPtr = excludeInstances)
            {
                bool* anyWithinRadius = stackalloc bool[1];
                new AnyInstancesWithinRadiusOfInstanceJob
                {
                    InstanceIndex = instanceIndex,
                    Radius = radius,
                    ExcludeInstances = new UnsafeArray<int>(excludeInstancesPtr, excludeInstances.Length),
                    InstanceTransforms = new UnsafeArray<LocalTransform>(transforms, m_InstanceTransforms.Count),
                    AnyWithinRadius = new UnsafeArray<bool>(anyWithinRadius, 1)
                }.Run();

                return anyWithinRadius[0];
            }
        }

        /// <summary>Returns an <see cref="NativeArray{T}"/> containing the instance indices of all instances inside the specified bounds.</summary>
        /// <param name="bounds">The bounds to query for instances.</param>
        /// <param name="space">The space of the bounds.</param>
        /// <param name="allocator">The allocator to use for the result array.</param>
        /// <returns>A <see cref="NativeArray{T}"/> containing the instance indices of all instances inside the bounds.</returns>
        public NativeArray<int> GetInstancesInsideBounds(AxisAlignedBox bounds, Space space, Allocator allocator)
        {
            NativeList<int> result = new NativeList<int>(256, allocator);
            GetInstancesInsideBounds(bounds, space, result);
            return result.TransferOwnershipToNativeArray();
        }

        /// <summary>Stores the instance indices of all instances inside the specified bounds in the provided list.</summary>
        /// <param name="bounds">The bounds to query for instances.</param>
        /// <param name="space">The space of the bounds.</param>
        /// <param name="result">The list to store the instance indices in.</param>
        public unsafe void GetInstancesInsideBounds(AxisAlignedBox bounds, Space space, NativeList<int> result)
        {
            fixed (LocalTransform* transforms = m_InstanceTransforms.AsSpan())
            {
                new GetInstancesInsideBoundsJob
                {
                    Bounds = bounds,
                    Space = space,
                    WorldToLocalMatrix = m_Transform.worldToLocalMatrix,
                    PlacementHash = m_PlacementHash,
                    InstanceTransforms = new UnsafeArray<LocalTransform>(transforms, m_InstanceTransforms.Count),
                    Result = result
                }.Execute();
            }
        }

        /// <summary>Returns an <see cref="NativeArray{T}"/> containing the instance indices of all instances inside the specified sphere.</summary>
        /// <param name="sphere">The sphere to query for instances.</param>
        /// <param name="space">The space of the sphere.</param>
        /// <param name="allocator">The allocator to use for the result array.</param>
        /// <returns>A <see cref="NativeArray{T}"/> containing the instance indices of all instances inside the sphere.</returns>
        public NativeArray<int> GetInstancesInsideSphere(Sphere sphere, Space space, Allocator allocator)
        {
            NativeList<int> result = new NativeList<int>(256, allocator);
            GetInstancesInsideSphere(sphere, space, result);
            return result.TransferOwnershipToNativeArray();
        }

        /// <summary>Stores the instance IDs of all instances inside the specified sphere in the provided list.</summary>
        /// <param name="sphere">The sphere to query for instances.</param>
        /// <param name="space">The space of the bounds.</param>
        /// <param name="result">The list to store the instance indices in.</param>
        public unsafe void GetInstancesInsideSphere(Sphere sphere, Space space, NativeList<int> result)
        {
            fixed (LocalTransform* transforms = m_InstanceTransforms.AsSpan())
            {
                new GetInstancesInsideSphereJob
                {
                    Sphere = sphere,
                    Space = space,
                    WorldToLocalMatrix = m_Transform.worldToLocalMatrix,
                    PlacementHash = m_PlacementHash,
                    LocalTransforms = new UnsafeArray<LocalTransform>(transforms, m_InstanceTransforms.Count),
                    Result = result,
                }.Execute();
            }
        }

        /// <summary>Tries to find the instance at the specified position.</summary>
        /// <param name="position">The position to search for an instance.</param>
        /// <param name="space">The space of the position.</param>
        /// <param name="instanceIndex">The resulting instance index, or -1 if no instance was found.</param>
        /// <returns>True if an instance was found at the specified position, otherwise false.</returns>
        public unsafe bool TryGetInstanceAtPosition(float3 position, Space space, out int instanceIndex)
        {
            fixed (int* result = &instanceIndex)
            fixed (LocalTransform* transforms = m_InstanceTransforms.AsSpan())
            {
                new GetInstanceAtPositionJob
                {
                    Position = position,
                    Space = space,
                    WorldToLocalMatrix = m_Transform.worldToLocalMatrix,
                    PlacementHash = m_PlacementHash,
                    LocalTransforms = new UnsafeArray<LocalTransform>(transforms, m_InstanceTransforms.Count),
                    Result = new UnsafeArray<int>(result, 1)
                }.Run();
            }

            return instanceIndex >= 0;
        }
    }
}
