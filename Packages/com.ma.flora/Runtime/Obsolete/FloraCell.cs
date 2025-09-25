// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using MA.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MA.Flora
{
    /// <summary>A cell in a <see cref="FloraContainer"/>.</summary>
    /// <remarks>Contains a set of <see cref="FloraPrototype"/> and their associated <see cref="FloraInstanceController"/>s.</remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    [Obsolete]
    public sealed class FloraCell : MonoBehaviour
    {
        enum Version { None, Initial, }
        
        [Serializable]
        internal struct SerializablePrototypeInfo
        {
            public FloraPrototype Prototype;
            public FloraInstanceController Controller;
        }
        
        [SerializeField] internal FloraContainer m_Container;
        [SerializeField] internal int m_CellSize;
        [SerializeField] internal List<SerializablePrototypeInfo> m_SerializedPrototypeInfo = new List<SerializablePrototypeInfo>();
        [SerializeField] internal FloraParentIdCache m_ParentGameObjectCache = new FloraParentIdCache();
        
        public FloraContainer Container => throw new Exception("FloraCell is obsolete.");
        public int CellSize => throw new Exception("FloraCell is obsolete.");
        public CellCoordinate LocalCellCoordinate => throw new Exception("FloraCell is obsolete.");
        public AxisAlignedBox LocalBounds => throw new Exception("FloraCell is obsolete.");
        public AxisAlignedBox WorldBounds => throw new Exception("FloraCell is obsolete.");
        public IReadOnlyDictionary<FloraPrototype, FloraInstanceController> Controllers => throw new Exception("FloraCell is obsolete.");
        
        public void AddInstances(FloraPrototype prototype, ReadOnlySpan<FloraInstance> instances, Space space) => throw new Exception("FloraCell is obsolete.");
        public void AddInstances(FloraPrototype prototype, ReadOnlySpan<FloraInstance> instances) => throw new Exception("FloraCell is obsolete.");
        
        public JobHandle ScheduleAddInstances(FloraPrototype prototype, NativeArray<FloraInstance> instances, Space space, JobHandle dependsOn = default) => throw new Exception("FloraCell is obsolete.");
        public JobHandle ScheduleAddInstances(FloraPrototype prototype, NativeArray<FloraInstance> instances, JobHandle dependsOn = default) => throw new Exception("FloraCell is obsolete.");
        
        public void RemoveInstances(FloraPrototype prototype, ReadOnlySpan<int> instanceIndices, bool rebuildCullingTree = true) => throw new Exception("FloraCell is obsolete.");
        public JobHandle ScheduleRemoveInstances(FloraPrototype prototype, NativeArray<int> instanceIndices, JobHandle dependsOn = default) => throw new Exception("FloraCell is obsolete.");
        public void RemoveInstancesWithParent(GameObject parent, bool rebuildCullingTree = true) => throw new Exception("FloraCell is obsolete.");
        public void RemoveInstancesWithParent(FloraParentID parentId, bool rebuildCullingTree = true) => throw new Exception("FloraCell is obsolete.");

        public void GetInstancesWithParent(FloraPrototype prototype, GameObject parent, NativeList<FloraGlobalInstanceID> result) => throw new Exception("FloraCell is obsolete.");
        public void GetInstancesWithParent(FloraPrototype prototype, FloraParentID parentId, NativeList<FloraGlobalInstanceID> result) => throw new Exception("FloraCell is obsolete.");
        
        public void MarkRenderStateDirty(FloraPrototype prototype, bool forceRebuildRenderData = false) => throw new Exception("FloraCell is obsolete.");
        public void MarkRenderStateDirty(bool forceRebuildRenderData = false) => throw new Exception("FloraCell is obsolete.");

        public void TryBuildCullingTrees(FloraPrototype prototype, bool async = true, bool force = false) => throw new Exception("FloraCell is obsolete.");
        public void TryBuildCullingTrees(bool async = true, bool force = false) => throw new Exception("FloraCell is obsolete.");

        public bool TryGetParentId(GameObject parentGameObject, out FloraParentID parentId) => throw new Exception("FloraCell is obsolete.");
        public bool TryGetParentId(int parentGameObjectInstanceId, out FloraParentID parentId) => throw new Exception("FloraCell is obsolete.");

        public FloraInstanceController GetOrCreateController(FloraPrototype prototype) => throw new Exception("FloraCell is obsolete.");
        public bool TryGetController(FloraPrototype prototype, out FloraInstanceController controller) => throw new Exception("FloraCell is obsolete.");

        public void RemoveController(FloraPrototype prototype) => throw new Exception("FloraCell is obsolete.");
        public void ReplaceControllerPrototype(FloraPrototype oldPrototype, FloraPrototype newPrototype) => throw new Exception("FloraCell is obsolete.");
    }
}
