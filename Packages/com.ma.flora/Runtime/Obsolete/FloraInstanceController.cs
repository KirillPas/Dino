// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using JetBrains.Annotations;
using MA.Core;
using MA.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FloraInstanceRenderer))]
    [AddComponentMenu("")]
    [Obsolete]
    public sealed class FloraInstanceController : MonoBehaviour
    {
        public FloraCell Cell => m_Cell;
        [SerializeField] FloraCell m_Cell;
        
        public FloraInstanceRenderer Renderer => m_Renderer;
        [SerializeField] FloraInstanceRenderer m_Renderer;
        
        public FloraPrototype Prototype
        {
            get => m_Prototype;
            set => throw new Exception("FloraInstanceController is obsolete.");
        }
        [SerializeField] FloraPrototype m_Prototype;
        
        public int InstanceCount => m_InstanceCollection.Count;
        public FloraInstanceCollection Instances => m_InstanceCollection;
        [SerializeField] FloraInstanceCollection m_InstanceCollection;
        
        public InstancePlacementHash<int> PlacementHash => throw new Exception("FloraInstanceController is obsolete.");
        public FloraInstanceParentHash ParentHash => throw new Exception("FloraInstanceController is obsolete.");

        [SerializeField] bool m_Created; // This controller was created with Create().
        [SerializeField, UsedImplicitly] bool m_EnableInstanceTrackingAtRuntime;
        [SerializeField] SerializableGuid m_PrototypeChangeGuid = SerializableGuid.Empty;
        [SerializeField] int m_LastKnownCollectionVersion;

        public static FloraInstanceController Create(FloraCell cell, FloraPrototype prototype) => throw new Exception("FloraInstanceController is obsolete.");
        public void MarkRenderStateDirty(bool forceRebuildRenderData = false) => throw new Exception("FloraInstanceController is obsolete.");
        public void TryBuildCullingTree(bool async, bool force = false) => throw new Exception("FloraInstanceController is obsolete.");
        
        public void BeginUpdate() => throw new Exception("FloraInstanceController is obsolete.");
        public void EndUpdate() => throw new Exception("FloraInstanceController is obsolete.");

        public void AddInstances(ReadOnlySpan<FloraInstance> newInstances, Space space) => throw new Exception("FloraInstanceController is obsolete.");
        public void AddInstances(ReadOnlySpan<FloraInstance> newInstances, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceController is obsolete.");
        public JobHandle ScheduleAddInstances(NativeArray<FloraInstance> newInstances, Space space, JobHandle dependsOn = default) => throw new Exception("FloraInstanceController is obsolete.");
        public JobHandle ScheduleAddInstances(NativeArray<FloraInstance> newInstances, float3 worldToLocalOffset, JobHandle dependsOn = default) => throw new Exception("FloraInstanceController is obsolete.");
        
        public void UpdateInstanceTransforms(int startInstanceIndex, ReadOnlySpan<LocalTransform> transforms, Space space) => throw new Exception("FloraInstanceController is obsolete.");
        public void UpdateInstanceTransforms(int startInstanceIndex, ReadOnlySpan<LocalTransform> transforms, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceController is obsolete.");
        public JobHandle ScheduleUpdateInstanceTransforms(int startInstanceIndex, NativeArray<LocalTransform> transforms, Space space, JobHandle dependsOn = default) => throw new Exception("FloraInstanceController is obsolete.");
        public JobHandle ScheduleUpdateInstanceTransforms(int startInstanceIndex, NativeArray<LocalTransform> transforms, float3 worldToLocalOffset, JobHandle dependsOn = default) => throw new Exception("FloraInstanceController is obsolete.");
        
        public void UpdateInstances(ReadOnlySpan<FloraInstanceIndex> instances, Space space) => throw new Exception("FloraInstanceController is obsolete.");
        public void UpdateInstances(ReadOnlySpan<FloraInstanceIndex> instances, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceController is obsolete.");
        public JobHandle ScheduleUpdateInstances(NativeArray<FloraInstanceIndex> instances, Space space, JobHandle dependsOn = default) => throw new Exception("FloraInstanceController is obsolete.");
        public JobHandle ScheduleUpdateInstances(NativeArray<FloraInstanceIndex> instances, JobHandle dependsOn = default) => throw new Exception("FloraInstanceController is obsolete.");
        public JobHandle ScheduleUpdateInstances(NativeArray<FloraInstanceIndex> instances, float3 worldToLocalOffset, JobHandle dependsOn = default) => throw new Exception("FloraInstanceController is obsolete.");
        public void PostUpdateInstances(NativeArray<int> instancesUpdated, bool reAddToPlacementHash) => throw new Exception("FloraInstanceController is obsolete.");
        public void ClearInstances() => throw new Exception("FloraInstanceController is obsolete.");
        
        public void RemoveInstances(ReadOnlySpan<int> instancesToRemove, bool rebuildCullingTree) => throw new Exception("FloraInstanceController is obsolete.");
        public JobHandle ScheduleRemoveInstances(NativeArray<int> instancesToRemove,  JobHandle dependsOn = default) => throw new Exception("FloraInstanceController is obsolete.");
        
        public void GetInstancesWithParent(FloraParentID parentId, NativeList<int> results) => throw new Exception("FloraInstanceController is obsolete.");
        
        public bool CheckForOverlappingInsideSphere(Sphere sphere, Space space) => throw new Exception("FloraInstanceController is obsolete.");
        public bool CheckForOverlappingInstanceExcluding(int instanceIndex, float radius, NativeArray<int> excludeInstances) => throw new Exception("FloraInstanceController is obsolete.");
        public NativeArray<int> GetInstancesInsideBounds(AxisAlignedBox bounds, Space space, Allocator allocator) => throw new Exception("FloraInstanceController is obsolete.");
        public void GetInstancesInsideBounds(AxisAlignedBox bounds, Space space, NativeList<int> resultIndices) => throw new Exception("FloraInstanceController is obsolete.");
        public NativeList<int> GetInstancesInsideSphere(Sphere sphere, Space space, Allocator allocator) => throw new Exception("FloraInstanceController is obsolete.");
        public void GetInstancesInsideSphere(Sphere sphere, Space space, NativeList<int> resultIndices) => throw new Exception("FloraInstanceController is obsolete.");
        public bool TryGetInstanceAtPosition(float3 position, Space space, out int instanceIndex) => throw new Exception("FloraInstanceController is obsolete.");
        public void RecomputeInstanceHashes() => throw new Exception("FloraInstanceController is obsolete.");
        public void RegisterInstanceUndo(string undoName) => throw new Exception("FloraInstanceController is obsolete.");
        public void EnterEditMode() => throw new Exception("FloraInstanceController is obsolete.");
        public void ExitEditMode() => throw new Exception("FloraInstanceController is obsolete.");
    }
    
    [Obsolete]
    public struct FloraInstanceParentHash : IDisposable
    {
        public FloraInstanceParentHash(int capacity, AllocatorManager.AllocatorHandle allocator) => throw new Exception("FloraInstanceParentHash is obsolete.");
        public bool IsCreated => throw new Exception("FloraInstanceParentHash is obsolete.");
        public void Dispose() => throw new Exception("FloraInstanceParentHash is obsolete.");
        public void Clear() => throw new Exception("FloraInstanceParentHash is obsolete.");
        public bool TryGetIndices(FloraParentID parentId, out UnsafeParallelHashSet<int> indices) => throw new Exception("FloraInstanceParentHash is obsolete.");
        public void Add(FloraParentID parentId, int instanceIndex) => throw new Exception("FloraInstanceParentHash is obsolete.");
        public void Remove(FloraParentID parentId, int instanceIndex) => throw new Exception("FloraInstanceParentHash is obsolete.");
    }
}
