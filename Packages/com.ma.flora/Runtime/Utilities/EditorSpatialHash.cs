// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine.Assertions;

namespace MA.Flora
{
    class EditorSpatialHash
    {
#if UNITY_EDITOR
        internal const int DefaultCellSize = 128;

        static EditorSpatialHash s_DefaultInstance = new EditorSpatialHash();
        static EditorSpatialHash s_PrefabStageInstance = new EditorSpatialHash();

        [UnityEditor.InitializeOnLoadMethod]
        static void Initialize()
        {
            PrefabStage.prefabStageClosing += _ => s_PrefabStageInstance = new EditorSpatialHash();
        }

        public static EditorSpatialHash Instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => PrefabStageUtility.GetCurrentPrefabStage() != null ? s_PrefabStageInstance : s_DefaultInstance;
        }

        internal struct CellCoord : IEquatable<CellCoord>
        {
            public int X;
            public int Y;
            public int Z;
            public int Level;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public CellCoord(int x, int y, int z, int level)
            {
                X = x;
                Y = y;
                Z = z;
                Level = level;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetChildIndex()
                => ((X & 1) << 2) | ((Y & 1) << 1) | (Z & 1);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public CellCoord GetChildCoordinate(int childIndex)
                => new(
                    (X << 1) | (childIndex >> 2),
                    (Y << 1) | ((childIndex >> 1) & 1),
                    (Z << 1) | (childIndex & 1),
                    Level - 1);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public CellCoord GetParentCoordinate()
                => new(X >> 1, Y >> 1, Z >> 1, Level + 1);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(CellCoord other) => X == other.X && Y == other.Y && Z == other.Z && Level == other.Level;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override bool Equals(object obj) => obj is CellCoord other && Equals(other);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode() => new int4(X, Y, Z, Level).GetHashCode();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator ==(CellCoord left, CellCoord right) => left.Equals(right);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator !=(CellCoord left, CellCoord right) => !left.Equals(right);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override string ToString() => $"({X}, {Y}, {Z}, {Level})";
        }

        internal struct CellNode
        {
            public byte ChildMask;
            public bool HasChildren { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ChildMask != 0; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public CellNode(byte childMask) => ChildMask = childMask;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Clear() => ChildMask = 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool HasChild(int childIndex) => (ChildMask & (1 << childIndex)) != 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddChild(int childIndex) => ChildMask |= (byte)(1 << childIndex);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveChild(int childIndex) => ChildMask &= (byte)~(1 << childIndex);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ForEachChild(Action<int> action)
            {
                int currentMask = ChildMask;
                while (currentMask != 0)
                {
                    var childIndex = math.tzcnt(currentMask);
                    action(childIndex);
                    currentMask &= ~(1 << childIndex);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override string ToString() => Convert.ToString(ChildMask, 2).PadLeft(8, '0');
        }

        [DebuggerDisplay("{Bounds}, Count = {Containers.Count}")]
        internal class Cell
        {
            public AxisAlignedBox Bounds;
            public HashSet<InstancedMeshContainer> Containers = new();
        }

        [DebuggerDisplay("{Node}, {Cell}")]
        struct CellData
        {
            public CellNode Node;
            public Cell Cell;
        }

        int m_CellSize = DefaultCellSize;
        Dictionary<CellCoord, CellData>[] m_Levels = Array.Empty<Dictionary<CellCoord, CellData>>();
        HashSet<Cell> m_Cells = new HashSet<Cell>();

        AxisAlignedBox m_EditorBounds  = AxisAlignedBox.Empty; // Cell bounds
        AxisAlignedBox m_RuntimeBounds = AxisAlignedBox.Empty; // Container bounds
        bool m_IsBoundsDirty = true;

        internal void NextFrame()
        {
            if (m_IsBoundsDirty)
            {
                AxisAlignedBox newEditorBounds = AxisAlignedBox.Empty;

                m_RuntimeBounds = AxisAlignedBox.Empty;
                foreach (var cell in m_Cells)
                {
                    newEditorBounds += cell.Bounds;

                    foreach (var container in cell.Containers)
                        m_RuntimeBounds += container.EditorBounds;
                }

                int oldLevel = GetLevelForBounds(m_EditorBounds);
                int newLevel = GetLevelForBounds(newEditorBounds);
                Assert.IsTrue(newLevel <= oldLevel);

                if (newLevel < oldLevel)
                    Array.Resize(ref m_Levels, newLevel + 1);

                m_EditorBounds = newEditorBounds;
                m_IsBoundsDirty = false;
            }
        }

        internal void Update(InstancedMeshContainer container)
        {
            Remove(container);
            Add(container);
        }

        internal void Add(InstancedMeshContainer container)
        {
            AxisAlignedBox containerBounds = container.EditorBounds;
            int currentLevel = GetLevelForBounds(m_EditorBounds);
            int containerLevel = GetLevelForBounds(containerBounds);

            if (m_Levels.Length <= containerLevel)
            {
                int previousLevelCount = m_Levels.Length;
                Array.Resize(ref m_Levels, containerLevel + 1);
                for (int i = previousLevelCount; i < m_Levels.Length; i++)
                    m_Levels[i] = new Dictionary<CellCoord, CellData>();
            }

            ForEachCellInBounds(containerBounds, containerLevel, cellCoord =>
            {
                if (!m_Levels[containerLevel].TryGetValue(cellCoord, out var cellData))
                {
                    cellData = new CellData
                    {
                        Node = new CellNode(0),
                        Cell = new Cell { Bounds = GetCellBounds(cellCoord) }
                    };

                    m_Cells.Add(cellData.Cell);
                    m_Levels[containerLevel].Add(cellCoord, cellData);

                    m_EditorBounds += cellData.Cell.Bounds;

                    var currentCellCoord = cellCoord;
                    while (currentCellCoord.Level < currentLevel)
                    {
                        int childIndex = currentCellCoord.GetChildIndex();
                        currentCellCoord = currentCellCoord.GetParentCoordinate();

                        if (!m_Levels[currentCellCoord.Level].TryGetValue(currentCellCoord, out var parentCellData))
                        {
                            parentCellData = new CellData
                            {
                                Node = new CellNode(0),
                                Cell = new Cell { Bounds = GetCellBounds(currentCellCoord) }
                            };

                            m_Levels[currentCellCoord.Level].Add(currentCellCoord, parentCellData);
                        }

                        if (parentCellData.Node.HasChild(childIndex))
                            break;

                        parentCellData.Node.AddChild(childIndex);
                        m_Levels[currentCellCoord.Level][currentCellCoord] = parentCellData;
                    }
                }

                cellData.Cell.Containers.Add(container);
            });

            int newLevel = GetLevelForBounds(m_EditorBounds);
            if (newLevel > currentLevel)
            {
                if (m_Levels.Length <= newLevel)
                {
                    int previousLevelCount = m_Levels.Length;
                    Array.Resize(ref m_Levels, newLevel + 1);
                    for (int i = previousLevelCount; i < m_Levels.Length; i++)
                        m_Levels[i] = new Dictionary<CellCoord, CellData>();
                }

                for (int level = currentLevel; level < newLevel; level++)
                {
                    foreach ((var coord, var cellData) in m_Levels[level])
                    {
                        var levelCellCoord = coord;
                        while (levelCellCoord.Level < newLevel)
                        {
                            int childIndex = coord.GetChildIndex();
                            levelCellCoord = levelCellCoord.GetParentCoordinate();

                            if (!m_Levels[levelCellCoord.Level].TryGetValue(levelCellCoord, out var parentCellData))
                            {
                                parentCellData = new CellData
                                {
                                    Node = new CellNode(0),
                                    Cell = new Cell { Bounds = GetCellBounds(levelCellCoord) }
                                };

                                m_Levels[levelCellCoord.Level].Add(levelCellCoord, parentCellData);
                            }

                            if (parentCellData.Node.HasChild(childIndex))
                                break;

                            parentCellData.Node.AddChild(childIndex);
                            m_Levels[levelCellCoord.Level][levelCellCoord] = parentCellData;
                        }
                    }
                }
            }

            m_RuntimeBounds += containerBounds;
        }

        internal void Remove(InstancedMeshContainer container)
        {
            AxisAlignedBox containerBounds = container.EditorBounds;
            int currentLevel = GetLevelForBounds(m_EditorBounds);
            int containerLevel = GetLevelForBounds(containerBounds);
            if (!m_Levels.IsValidIndex(containerLevel))
                return;

            ForEachCellInBounds(containerBounds, containerLevel, cellCoord =>
            {
                if (!m_Levels[containerLevel].TryGetValue(cellCoord, out var cellData))
                    return;

                cellData.Cell.Containers.Remove(container);

                if (cellData.Cell.Containers.Count == 0)
                {
                    m_Cells.Remove(cellData.Cell);
                    cellData.Cell = null;

                    if (!cellData.Node.HasChildren)
                    {
                        CellCoord CurrentCellCoord = cellCoord;
                        while (CurrentCellCoord.Level < currentLevel)
                        {
                            CellCoord ParentCellCoord = CurrentCellCoord.GetParentCoordinate();
                            if (!m_Levels.IsValidIndex(ParentCellCoord.Level) || !m_Levels[ParentCellCoord.Level].TryGetValue(ParentCellCoord, out var parentCellData))
                                break;

                            int ChildIndex = CurrentCellCoord.GetChildIndex();
                            parentCellData.Node.RemoveChild(ChildIndex);
                            m_Levels[ParentCellCoord.Level][ParentCellCoord] = parentCellData;
                            m_Levels[CurrentCellCoord.Level].Remove(CurrentCellCoord);

                            if (parentCellData.Cell != null || parentCellData.Node.HasChildren)
                                break;

                            CurrentCellCoord = ParentCellCoord;
                        }
                    }
                }
            });

            m_IsBoundsDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ForEachIntersectingContainer(AxisAlignedBox bounds, Action<InstancedMeshContainer> action) => ForEachIntersectingContainer(bounds, action, AxisAlignedBox.Empty);

        internal int ForEachIntersectingContainer(AxisAlignedBox bounds, Action<InstancedMeshContainer> action, AxisAlignedBox minimumBounds)
        {
            HashSet<InstancedMeshContainer> containers = new HashSet<InstancedMeshContainer>();
            int minimumLevel = minimumBounds.IsEmpty ? 0 : GetLevelForBounds(minimumBounds);

            int count = 0;
            ForEachIntersectingCell(bounds, cell =>
            {
                foreach (var container in cell.Containers)
                {
                    if (!containers.Add(container))
                        continue;

                    if (bounds.Overlaps(container.EditorBounds))
                    {
                        action(container);
                        count++;
                    }
                }
            }, minimumLevel);

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ForEachIntersectingContainerBreakable(AxisAlignedBox bounds, Func<InstancedMeshContainer, bool> func) => ForEachIntersectingContainerBreakable(bounds, func, AxisAlignedBox.Empty);

        internal int ForEachIntersectingContainerBreakable(AxisAlignedBox bounds, Func<InstancedMeshContainer, bool> func, AxisAlignedBox minimumBounds)
        {
            HashSet<InstancedMeshContainer> containers = new HashSet<InstancedMeshContainer>();
            int minimumLevel = minimumBounds.IsEmpty ? 0 : GetLevelForBounds(minimumBounds);

            int count = 0;
            ForEachIntersectingCell(bounds, cell =>
            {
                foreach (var container in cell.Containers)
                {
                    if (!containers.Add(container))
                        continue;

                    if (bounds.Overlaps(container.EditorBounds))
                    {
                        count++;
                        if (func(container))
                            return;
                    }
                }
            }, minimumLevel);

            return count;
        }

        internal int ForEachIntersectingCell(AxisAlignedBox bounds, Action<Cell> action, int minimumLevel = 0)
        {
            int count = 0;

            if (m_Levels.Length > 0)
            {
                if (m_EditorBounds.Overlaps(bounds))
                {
                    ForEachCellInBounds(bounds, m_Levels.Length - 1, cellCoord =>
                    {
                        count += ForEachIntersectingCellInner(cellCoord, action, minimumLevel);
                    });
                }
            }

            return count;
        }

        int ForEachIntersectingCellInner(CellCoord cellCoord, Action<Cell> action, int minimumLevel = 0)
        {
            int count = 0;
            if (m_Levels[cellCoord.Level].TryGetValue(cellCoord, out var cellData))
            {
                if (cellData.Cell != null)
                {
                    action(cellData.Cell);
                    count++;
                }

                if (minimumLevel < cellCoord.Level)
                {
                    cellData.Node.ForEachChild(childIndex =>
                    {
                        var childCellCoord = cellCoord.GetChildCoordinate(childIndex);
                        AxisAlignedBox childBounds = GetCellBounds(childCellCoord);
                        if (m_EditorBounds.Overlaps(childBounds))
                            count += ForEachIntersectingCellInner(childCellCoord, action, minimumLevel);
                    });
                }

                return count;
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CellCoord GetCellCoordinate(float3 position, int level)
        {
            var cellSizeForLevel = m_CellSize * (1 << level);
            return new CellCoord(
                (int)math.floor(position.x / cellSizeForLevel),
                (int)math.floor(position.y / cellSizeForLevel),
                (int)math.floor(position.z / cellSizeForLevel),
                level);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal AxisAlignedBox GetCellBounds(CellCoord cellCoord)
        {
            var levelCellSize = m_CellSize * (1 << cellCoord.Level);
            var min = new float3(cellCoord.X * levelCellSize, cellCoord.Y * levelCellSize, cellCoord.Z * levelCellSize);
            var max = min + levelCellSize;
            return new AxisAlignedBox(min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetLevelForBounds(AxisAlignedBox bounds)
            => (int)math.ceil(math.max(math.log2(bounds.MaxDim / m_CellSize), 0));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ForEachCellInBounds(AxisAlignedBox bounds, Action<CellCoord> action) => ForEachCellInBounds(bounds, GetLevelForBounds(bounds), action);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int ForEachCellInBounds(AxisAlignedBox bounds, int level, Action<CellCoord> action)
        {
            var minCellCoord = GetCellCoordinate(bounds.Min, level);
            var maxCellCoord = GetCellCoordinate(bounds.Max, level);
            var count = 0;
            for (var x = minCellCoord.X; x <= maxCellCoord.X; x++)
            {
                for (var y = minCellCoord.Y; y <= maxCellCoord.Y; y++)
                {
                    for (var z = minCellCoord.Z; z <= maxCellCoord.Z; z++)
                    {
                        action(new CellCoord(x, y, z, level));
                        count++;
                    }
                }
            }
            return count;
        }
#endif
    }
}
