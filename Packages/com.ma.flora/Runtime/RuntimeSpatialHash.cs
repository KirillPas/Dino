// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
#endif

namespace MA.Flora
{
    class RuntimeSpatialHash
    {
        public const int DefaultCellSize = 128;
        public const int DefaultGridSize = 256;

        List<Grid> m_AllocatedGrids = new List<Grid>();
        Dictionary<InstancedPrototype, int> m_GridIndexHash = new Dictionary<InstancedPrototype, int>();

        static RuntimeSpatialHash s_DefaultInstance = new RuntimeSpatialHash();

        [UnityEditor.InitializeOnLoadMethod]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneUnloaded(Scene scene)
        {
            if (s_DefaultInstance != null)
            {
                foreach (Grid grid in s_DefaultInstance.m_AllocatedGrids)
                {
                    foreach (CellData cell in grid.AllocatedCells)
                    {
                        List<int> invalidContainerIndices = new List<int>();
                        for (int i = 0; i < cell.Containers.Count; i++)
                        {
                            if (cell.Containers[i] == null)
                                invalidContainerIndices.Add(i);
                        }

                        foreach (int index in invalidContainerIndices)
                        {
                            cell.Containers.RemoveAtSwapBack(index);
                        }
                    }
                }
            }
        }

        public static RuntimeSpatialHash Instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_DefaultInstance;
        }

        void Reset()
        {
            m_AllocatedGrids.Clear();
            m_GridIndexHash.Clear();
        }

        public GridLayout2D GetPrototypeGridLayout(InstancedPrototype prototype)
        {
            if (m_GridIndexHash.TryGetValue(prototype, out int gridIndex))
                return m_AllocatedGrids[gridIndex].Layout2D;

            return new GridLayout2D(float3.zero, DefaultGridSize, DefaultCellSize);
        }

        public void Update(InstancedMeshContainer container) => Update(container, container.CalculateBounds(Space.World));

        public void Update(InstancedMeshContainer container, AxisAlignedBox bounds)
        {
            if (container == null || container.Prototype == null)
                return;

            InstancedPrototype prototype = container.Prototype;
            Grid grid;
            if (!m_GridIndexHash.TryGetValue(prototype, out int gridIndex))
            {
                gridIndex = m_AllocatedGrids.Count;
                m_GridIndexHash[prototype] = gridIndex;
                grid = new Grid(prototype, DefaultCellSize, DefaultGridSize);
                m_AllocatedGrids.Add(grid);
            }
            else
            {
                grid = m_AllocatedGrids[m_GridIndexHash[prototype]];
            }

            grid.Update(container, bounds);
        }

        public void Remove(InstancedMeshContainer container)
        {
            if (container == null || container.Prototype == null)
                return;

            InstancedPrototype prototype = container.Prototype;
            if (m_GridIndexHash.TryGetValue(prototype, out int gridIndex))
            {
                Grid grid = m_AllocatedGrids[gridIndex];
                grid.Remove(container);

                if (grid.AllocatedCells.Count == 0)
                {
                    int lastIndexInGrids = m_AllocatedGrids.Count - 1;
                    m_GridIndexHash.Remove(prototype);
                    if (gridIndex != lastIndexInGrids)
                    {
                        Grid lastGrid = m_AllocatedGrids[lastIndexInGrids];
                        InstancedPrototype lastPrototype = lastGrid.Prototype;
                        m_GridIndexHash[lastPrototype] = gridIndex;
                    }
                    m_AllocatedGrids.RemoveAtSwapBack(gridIndex);
                }
            }
        }

        public int GetOverlappingBounds(InstancedPrototype prototype, AxisAlignedBox bounds, List<InstancedMeshContainer> containers)
        {
            if (m_GridIndexHash.TryGetValue(prototype, out int gridIndex))
            {
                Grid grid = m_AllocatedGrids[gridIndex];
                return grid.GetOverlappingBounds(bounds, containers);
            }

            return 0;
        }

        public int GetOverlappingBounds(AxisAlignedBox bounds, List<InstancedMeshContainer> containers)
        {
            int count = 0;
            foreach (Grid grid in m_AllocatedGrids)
                count += grid.GetOverlappingBounds(bounds, containers);

            return count;
        }

        public int GetOverlappingSphere(InstancedPrototype prototype, float3 position, float radius, List<InstancedMeshContainer> containers)
        {
            if (m_GridIndexHash.TryGetValue(prototype, out int gridIndex))
            {
                Grid grid = m_AllocatedGrids[gridIndex];
                return grid.GetOverlappingSphere(position, radius, containers);
            }

            return 0;
        }

        public int GetOverlappingSphere(float3 position, float radius, List<InstancedMeshContainer> containers)
        {
            int count = 0;
            foreach (Grid grid in m_AllocatedGrids)
                count += grid.GetOverlappingSphere(position, radius, containers);

            return count;
        }

        internal void DrawGizmos(Color color)
        {
            int gridCount = m_AllocatedGrids.Count;
            float alphaIncrement = 1.0f / gridCount;
            float alpha = alphaIncrement;

            foreach (Grid grid in m_AllocatedGrids)
            {
                foreach (CellData cell in grid.AllocatedCells)
                {
                    Gizmos.color = new Color(color.r, color.g, color.b, alpha);

                    float3 center = new float3(cell.Bounds.Center.x, 0.0f, cell.Bounds.Center.y);
                    float3 size = new float3(cell.Bounds.Size.x, 0, cell.Bounds.Size.y);
                    Gizmos.DrawWireCube(center, size);

                    alpha += alphaIncrement;
                }
            }
        }

        // --- Grid Data Structures ---

        [DebuggerDisplay("Coords: {Coords}, Containers: {Containers.Count}")]
        class CellData
        {
            public readonly int2 Coords;
            public readonly AxisAlignedBox2D Bounds;
            public readonly List<InstancedMeshContainer> Containers;
            public readonly Dictionary<InstancedMeshContainer, int> ContainerIndexMap;
            public readonly MultiHashMap<Transform, InstancedMeshContainer> ParentMultiMap;
            public bool IsEmpty => Containers.Count == 0;

            internal CellData(int2 coords, AxisAlignedBox2D bounds)
            {
                Coords = coords;
                Bounds = bounds;
                Containers = new List<InstancedMeshContainer>();
                ContainerIndexMap = new Dictionary<InstancedMeshContainer, int>();
            }
        }

        [DebuggerDisplay("Model: {Prototype.name}, CellSize: {Layout2D.CellSize}, GridSize: {Layout.GridSize}, Cells: {AllocatedCells.Count}")]
        class Grid
        {
            public struct ContainerInfo
            {
                public int2 CellCoords;
            }

            public readonly GridLayout2D Layout2D;
            public readonly InstancedPrototype Prototype;
            public readonly List<CellData> AllocatedCells;
            public readonly Dictionary<int, int> CellIndexHash;
            public readonly Dictionary<InstancedMeshContainer, ContainerInfo> ContainerInfoMap;
            public bool IsEmpty => AllocatedCells.Count == 0;

            public Grid(InstancedPrototype prototype, int cellSize, int gridSize)
            {
                Prototype = prototype;
                Layout2D = new GridLayout2D(float3.zero, gridSize, cellSize);
                AllocatedCells = new List<CellData>();
                CellIndexHash = new Dictionary<int, int>();
                ContainerInfoMap = new Dictionary<InstancedMeshContainer, ContainerInfo>();
            }

            public void Update(InstancedMeshContainer container, AxisAlignedBox bounds)
            {
                Remove(container);
                if (bounds.IsEmpty)
                    return;

                float3 center = bounds.Center;
                if (!Layout2D.TryGetCell(center, out int2 cell))
                {
                    Debug.Log($"SpatialHash: Unable to add container {container.name}. Position ({center}) is outside of the grid.");
                    return;
                }

                int cellIndex = Layout2D.GetCellIndex(cell);
                CellData cellData;
                if (!CellIndexHash.TryGetValue(cellIndex, out int mappedCellIndex))
                {
                    mappedCellIndex = AllocatedCells.Count;
                    cellData = new CellData(cell, Layout2D.GetCellBounds(cell));
                    AllocatedCells.Add(cellData);
                    CellIndexHash[cellIndex] = mappedCellIndex;
                }
                else
                {
                    cellData = AllocatedCells[mappedCellIndex];
                }

                ContainerInfoMap[container] = new ContainerInfo { CellCoords = cell };

                if (!cellData.ContainerIndexMap.ContainsKey(container))
                {
                    int containerIndex = cellData.Containers.Count;
                    cellData.Containers.Add(container);
                    cellData.ContainerIndexMap[container] = containerIndex;
                }
            }

            public void Remove(InstancedMeshContainer container)
            {
                if (!ContainerInfoMap.TryGetValue(container, out ContainerInfo containerInfo))
                    return;

                Remove(container, containerInfo.CellCoords);
                ContainerInfoMap.Remove(container);
            }

            void Remove(InstancedMeshContainer container, int2 cellCoords)
            {
                if (!Layout2D.TryGetCellIndex(cellCoords, out int cellIndex))
                    return;

                if (CellIndexHash.TryGetValue(cellIndex, out int mappedCellIndex))
                {
                    CellData cellData = AllocatedCells[mappedCellIndex];
                    if (cellData.ContainerIndexMap.TryGetValue(container, out int mappedContainerIndex))
                    {
                        int lastMappedContainerIndex = cellData.Containers.Count - 1;
                        if (mappedContainerIndex != lastMappedContainerIndex)
                        {
                            InstancedMeshContainer lastContainer = cellData.Containers[lastMappedContainerIndex];
                            cellData.ContainerIndexMap[lastContainer] = mappedContainerIndex;
                        }

                        cellData.Containers.RemoveAtSwapBack(mappedContainerIndex);
                        cellData.ContainerIndexMap.Remove(container);

                        if (cellData.Containers.Count == 0)
                        {
                            CellIndexHash.Remove(cellIndex);

                            int lastMappedCellIndex = AllocatedCells.Count - 1;
                            if (mappedCellIndex != lastMappedCellIndex)
                            {
                                CellData lastCellData = AllocatedCells[lastMappedCellIndex];
                                int lastCellIndex = Layout2D.GetCellIndex(lastCellData.Coords);
                                CellIndexHash[lastCellIndex] = mappedCellIndex;
                            }

                            AllocatedCells.RemoveAtSwapBack(mappedCellIndex);
                        }
                    }
                }
            }

            public bool TryGetInCell(float3 position, List<InstancedMeshContainer> containers)
            {
                if (Layout2D.TryGetCellIndex(position, out int cellIndex))
                {
                    if (CellIndexHash.TryGetValue(cellIndex, out int mappedCellIndex))
                    {
                        CellData cellData = AllocatedCells[mappedCellIndex];
                        containers.AddRange(cellData.Containers);
                        return true;
                    }
                }

                return false;
            }

            public bool TryGetFirstInCellWithParent(int2 cell, Transform parent, out InstancedMeshContainer foundContainer)
            {
                foundContainer = null;

                if (Layout2D.TryGetCellIndex(cell, out int cellIndex))
                {
                    if (CellIndexHash.TryGetValue(cellIndex, out int mappedCellIndex))
                    {
                        CellData cellData = AllocatedCells[mappedCellIndex];

                        for (int containerIndex = 0; containerIndex < cellData.Containers.Count; containerIndex++)
                        {
                            InstancedMeshContainer container = cellData.Containers[containerIndex];
                            if (container.transform.parent == parent)
                            {
                                foundContainer = container;
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            public int GetOverlappingBounds(AxisAlignedBox bounds, List<InstancedMeshContainer> containers)
            {
                return Layout2D.ForEachCellIntersecting(bounds, cell =>
                {
                    int cellIndex = Layout2D.GetCellIndex(cell);
                    if (CellIndexHash.TryGetValue(cellIndex, out int mappedCellIndex))
                    {
                        CellData cellData = AllocatedCells[mappedCellIndex];
                        for (int i = 0; i < cellData.Containers.Count; i++)
                        {
                            InstancedMeshContainer container = cellData.Containers[i];
                            AxisAlignedBox containerBounds = container.CalculateBounds(Space.World);
                            if (bounds.Overlaps(containerBounds))
                                containers.Add(container);
                        }
                    }
                });
            }

            public int GetOverlappingSphere(float3 center, float radius, List<InstancedMeshContainer> containers)
            {
                Sphere sphere = new Sphere(center, radius);
                return Layout2D.ForEachCellIntersecting(sphere, cell =>
                {
                    int cellIndex = Layout2D.GetCellIndex(cell);
                    if (CellIndexHash.TryGetValue(cellIndex, out int mappedCellIndex))
                    {
                        CellData cellData = AllocatedCells[mappedCellIndex];
                        for (int i = 0; i < cellData.Containers.Count; i++)
                        {
                            InstancedMeshContainer container = cellData.Containers[i];
                            AxisAlignedBox containerBounds = container.CalculateBounds(Space.World);
                            if (containerBounds.OverlapsSphere(sphere))
                                containers.Add(container);
                        }
                    }
                });
            }
        }
    }
}
