// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    [Obsolete("FloraContainer is obsolete. Please use the upgrade the container.")]
    public sealed class FloraContainer : MonoBehaviour
    {
        [Serializable]
        internal struct SerializableCell
        {
            public CellCoordinate Coordinate;
            public FloraCell Cell;
        }
        
        [SerializeField] internal List<FloraPrototype> m_Prototypes = new List<FloraPrototype>();
        [SerializeField] List<SerializableCell> m_SerializedCells = new List<SerializableCell>();
        
        public int GridSize => throw new Exception("FloraCell is obsolete.");
        public IReadOnlyDictionary<CellCoordinate, FloraCell> Cells => throw new Exception("FloraCell is obsolete.");
        public IReadOnlyList<FloraPrototype> Prototypes => throw new Exception("FloraCell is obsolete.");
        public AxisAlignedBox LocalBounds => throw new Exception("FloraCell is obsolete.");
        public AxisAlignedBox WorldBounds => throw new Exception("FloraCell is obsolete.");
        
        public void TryBuildCullingTrees(FloraPrototype prototype, bool async = true, bool force = false) => throw new Exception("FloraContainer is obsolete.");
        public void TryBuildCullingTrees(bool async = true, bool force = false) => throw new Exception("FloraContainer is obsolete.");
        
        public void AddPrototype(FloraPrototype prototype) => throw new Exception("FloraContainer is obsolete.");
        public void RemovePrototype(FloraPrototype prototype) => throw new Exception("FloraContainer is obsolete.");
        public void ReplacePrototype(FloraPrototype oldPrototype, FloraPrototype newPrototype) => throw new Exception("FloraContainer is obsolete.");

        public FloraCell GetOrCreateCell(CellCoordinate localCoord) => throw new Exception("FloraContainer is obsolete.");
        public bool TryGetCell(CellCoordinate localCoord, out FloraCell cell) => throw new Exception("FloraContainer is obsolete.");
        public bool TryGetCellAtLocalPosition(float3 localPosition, out FloraCell cell) => TryGetCell(GetCoordinateForLocalPosition(localPosition), out cell);
        public bool TryGetCellAtWorldPosition(float3 worldPosition, out FloraCell cell) => TryGetCell(GetCoordinateForWorldPosition(worldPosition), out cell);

        public void RemoveCell(CellCoordinate localCoord) => throw new Exception("FloraContainer is obsolete.");
        public CellCoordinate GetCoordinateForLocalPosition(float3 localPosition) => throw new Exception("FloraContainer is obsolete.");
        public CellCoordinate GetCoordinateForWorldPosition(float3 worldPosition) => throw new Exception("FloraContainer is obsolete.");
        public CellCoordinate GetMinimumLocalCoordinate() => throw new Exception("FloraContainer is obsolete.");
        public CellCoordinate GetMaximumLocalCoordinate()  => throw new Exception("FloraContainer is obsolete.");

        public AxisAlignedBox GetCellLocalBounds(CellCoordinate localCoord) => throw new Exception("FloraContainer is obsolete.");
        public AxisAlignedBox GetCellWorldBounds(CellCoordinate localCoord) => throw new Exception("FloraContainer is obsolete.");
        public FloraCell GetOrCreateCellAtLocalPosition(float3 localPosition) => throw new Exception("FloraContainer is obsolete.");
        public FloraCell GetOrCreateCellAtWorldPosition(float3 worldPosition) => throw new Exception("FloraContainer is obsolete.");
        public Dictionary<CellCoordinate, FloraCell>.ValueCollection.Enumerator GetCellEnumerator() => throw new Exception("FloraContainer is obsolete.");
        public BoundedCellEnumerator GetCellEnumeratorInLocalBounds(AxisAlignedBox localBounds) => throw new Exception("FloraContainer is obsolete.");
        public BoundedCellEnumerator GetCellEnumeratorInWorldBounds(AxisAlignedBox worldBounds) => throw new Exception("FloraContainer is obsolete.");

        public struct BoundedCellEnumerator : IEnumerator<FloraCell>, IEnumerable<FloraCell>
        {
            internal BoundedCellEnumerator(Dictionary<CellCoordinate, FloraCell> cells, CellCoordinate minCoordinate, CellCoordinate maxCoordinate) => throw new Exception("FloraContainer is obsolete.");
            public void Dispose() => throw new Exception("FloraContainer is obsolete.");
            public bool MoveNext() => throw new Exception("FloraContainer is obsolete.");
            public void Reset() => throw new Exception("FloraContainer is obsolete.");
            public FloraCell Current => throw new Exception("FloraContainer is obsolete.");
            public BoundedCellEnumerator GetEnumerator() => throw new Exception("FloraContainer is obsolete.");
            object IEnumerator.Current => Current;
            IEnumerator<FloraCell> IEnumerable<FloraCell>.GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;
        }
        
        public InstanceControllerEnumerator<Dictionary<CellCoordinate, FloraCell>.ValueCollection.Enumerator> GetInstanceControllerEnumerator(FloraPrototype prototype) => throw new Exception("FloraContainer is obsolete.");
        public InstanceControllerEnumerator<BoundedCellEnumerator> GetInstanceControllerEnumeratorInLocalBounds(FloraPrototype prototype, AxisAlignedBox localBounds) => throw new Exception("FloraContainer is obsolete.");
        public InstanceControllerEnumerator<BoundedCellEnumerator> GetInstanceControllerEnumeratorInWorldBounds(FloraPrototype prototype, AxisAlignedBox worldBounds) => throw new Exception("FloraContainer is obsolete.");
        public InstanceControllerEnumerator<BoundedCellEnumerator> GetInstanceControllerEnumeratorInLocalSphere(FloraPrototype prototype, Sphere localSphere) => throw new Exception("FloraContainer is obsolete.");
        public InstanceControllerEnumerator<BoundedCellEnumerator> GetInstanceControllerEnumeratorInWorldSphere(FloraPrototype prototype, Sphere worldSphere) => throw new Exception("FloraContainer is obsolete.");

        public struct InstanceControllerEnumerator<TCellEnumerator> : IEnumerator<FloraInstanceController>, IEnumerable<FloraInstanceController>
            where TCellEnumerator : struct, IEnumerator<FloraCell>
        {
            internal InstanceControllerEnumerator(FloraPrototype prototype, TCellEnumerator enumerator) => throw new Exception("FloraContainer is obsolete.");
            public void Dispose() => throw new Exception("FloraContainer is obsolete.");
            public bool MoveNext() => throw new Exception("FloraContainer is obsolete.");
            public void Reset() => throw new Exception("FloraContainer is obsolete.");
            public FloraInstanceController Current => throw new Exception("FloraContainer is obsolete.");
            public InstanceControllerEnumerator<TCellEnumerator> GetEnumerator() => throw new Exception("FloraContainer is obsolete.");
            object IEnumerator.Current => Current;
            IEnumerator IEnumerable.GetEnumerator() => this;
            IEnumerator<FloraInstanceController> IEnumerable<FloraInstanceController>.GetEnumerator() => this;
        }
    }
}
