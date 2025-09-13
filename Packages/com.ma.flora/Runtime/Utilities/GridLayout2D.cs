// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Mathematics;
using Unity.Mathematics;

namespace MA.Flora
{
    /// <summary>Utility struct for working with a 2D grid of cells.</summary>
    [DebuggerDisplay("Origin = {Origin}, GridSize = {GridSize}, CellSize = {CellSize}")]
    readonly struct GridLayout2D
    {
        /// <summary>The 3D origin of the grid.</summary>
        public readonly float3 Origin;
        /// <summary>The 2D origin of the grid.</summary>
        public float2 Origin2D => Origin.xz;
        /// <summary>Size of the grid, in cells.</summary>
        public readonly int GridSize;
        /// <summary>Size of each cell.</summary>
        public readonly int CellSize;
        /// <summary>Surface area of each cell.</summary>
        public float CellSurfaceArea => CellSize * CellSize;

        /// <summary>Constructs a new <see cref="GridLayout2D"/> instance.</summary>
        /// <param name="origin">Origin of the grid.</param>
        /// <param name="gridSize">Size of the grid, in cells.</param>
        /// <param name="cellSize">Size of each cell.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GridLayout2D(float3 origin, int gridSize, int cellSize)
        {
            Origin = origin;
            GridSize = gridSize;
            CellSize = cellSize;
        }

        // --- Cell Coordinates ---

        /// <summary>Checks if the given cell are valid within this grid.</summary>
        /// <param name="cell">Coordinates to check.</param>
        /// <returns>True if the cell are inside the grid, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsCell(int2 cell) => math.all(cell >= 0) && math.all(cell < GridSize);

        /// <summary>Gets the cell coordinates for the given 2D position.</summary>
        /// <param name="position2D">Position to get the cell coordinates for.</param>
        /// <returns>Cell coordinates for the given position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 GetCell(float2 position2D) => new(math.floor(((position2D - Origin2D) / CellSize) + GridSize * 0.5f));

        /// <summary>Gets the cell coordinates for the given position.</summary>
        /// <param name="position">Position to get the cell coordinates for.</param>
        /// <returns>Cell coordinates for the given position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 GetCell(float3 position) => GetCell(position.xz);

        /// <summary>Gets the cell coordinates for the given linear cell index.</summary>
        /// <param name="cellIndex">Linear cell index to get the cell coordinates for.</param>
        /// <returns>Cell coordinates for the given cell index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 GetCell(int cellIndex) => new int2(cellIndex % GridSize, cellIndex / GridSize);

        /// <summary>Tries to get the cell coordinates for the given 2D position.</summary>
        /// <param name="position2D">Position to get the cell coordinates for.</param>
        /// <param name="cell">Output cell coordinates.</param>
        /// <returns>True if the position is inside the grid, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCell(float2 position2D, out int2 cell)
        {
            cell = GetCell(position2D);
            return ContainsCell(cell);
        }

        /// <summary>Tries to get the cell coordinates for the given position.</summary>
        /// <param name="position">Position to get the cell coordinates for.</param>
        /// <param name="cell">Output cell coordinates.</param>
        /// <returns>True if the position is inside the grid, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCell(float3 position, out int2 cell) => TryGetCell(position.xz, out cell);

        /// <summary>Tries to get the range of cell coordinates for the given 2D bounds.</summary>
        /// <param name="bounds2D">Bounds to get the cell for.</param>
        /// <param name="minCell">Output minimum cell coordinates.</param>
        /// <param name="maxCell">Output maximum cell coordinates.</param>
        /// <returns>True if the bounds fall within the grid, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCells(AxisAlignedBox2D bounds2D, out int2 minCell, out int2 maxCell)
        {
            minCell = int.MaxValue;
            maxCell = int.MinValue;

            TryGetCell(bounds2D.Min, out minCell);
            if (math.any(minCell >= GridSize))
                return false;

            TryGetCell(bounds2D.Max, out maxCell);
            if (math.any(maxCell < 0))
                return false;

            minCell = math.clamp(minCell, 0, GridSize - 1);
            maxCell = math.clamp(maxCell, 0, GridSize - 1);
            return true;
        }

        /// <summary>Tries to get the range of cell for the given bounds.</summary>
        /// <param name="bounds">Bounds to get the cell for.</param>
        /// <param name="minCell">Output minimum cell coordinates.</param>
        /// <param name="maxCell">Output maximum cell coordinates.</param>
        /// <returns>True if the bounds fall within the grid, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCells(AxisAlignedBox bounds, out int2 minCell, out int2 maxCell) => TryGetCells(new AxisAlignedBox2D(bounds.Min.xz, bounds.Max.xz), out minCell, out maxCell);

        // --- Cell Bounds ---

        /// <summary>Gets the 2D bounds of the cell at the given cell.</summary>
        /// <param name="cell">Coordinates of the cell.</param>
        /// <returns>The 2D bounds of the cell.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AxisAlignedBox2D GetCellBounds(int2 cell)
        {
            float2 min = (Origin2D - new float2(GridSize * CellSize * 0.5f)) + new float2(cell * CellSize);
            float2 max = min + new float2(CellSize);
            return new AxisAlignedBox2D(min, max);
        }

        /// <summary>Gets the 2D bounds of the cell at the given linear cell index.</summary>
        /// <param name="cellIndex">Linear cell index of the cell.</param>
        /// <returns>The 2D bounds of the cell.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AxisAlignedBox2D GetCellBounds(int cellIndex)
        {
            int2 cell = GetCell(cellIndex);
            return GetCellBounds(cell);
        }

        /// <summary>Try to get the 2D bounds of the cell at the given cell.</summary>
        /// <param name="cell">Coordinates of the cell.</param>
        /// <param name="bounds2D">Output 2D bounds of the cell.</param>
        /// <param name="validateCell">Whether to validate the cell.</param>
        /// <returns>True if the cell coordinates are inside the grid, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCellBounds(int2 cell, out AxisAlignedBox2D bounds2D, bool validateCell = true)
        {
            if (!validateCell || ContainsCell(cell))
            {
                bounds2D = GetCellBounds(cell);
                return true;
            }

            bounds2D = AxisAlignedBox2D.Empty;
            return false;
        }

        /// <summary>Try to get the 2D bounds of the cell at the given 3D position.</summary>
        /// <param name="position">Position to get the cell bounds for.</param>
        /// <param name="bounds2D">The output 2D bounds of the cell.</param>
        /// <returns>True if the position is inside the grid, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCellBounds(float3 position, out AxisAlignedBox2D bounds2D)
        {
            if (TryGetCell(position, out int2 cell))
            {
                bounds2D = GetCellBounds(cell);
                return true;
            }

            bounds2D = AxisAlignedBox2D.Empty;
            return false;
        }

        /// <summary>Try to get the 2D bounds of the cell at the linear grid index.</summary>
        /// <param name="cellIndex">Linear index of the cell.</param>
        /// <param name="bounds2D">The output 2D bounds of the cell.</param>
        /// <returns>True if the cell index is inside the grid, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCellBounds(int cellIndex, out AxisAlignedBox2D bounds2D)
        {
            if (cellIndex >= 0 && cellIndex <= (GridSize * GridSize))
            {
                int2 cell = GetCell(cellIndex);
                bounds2D = GetCellBounds(cell);
                return true;
            }

            bounds2D = AxisAlignedBox2D.Empty;
            return false;
        }

        // --- Cell Index ---

        /// <summary>Gets the linear cell index for the given cell.</summary>
        /// <param name="cell">Coordinates of the cell.</param>
        /// <returns>The linear cell index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetCellIndex(int2 cell) => (cell.y * GridSize) + cell.x;

        /// <summary>Tries to get the linear cell index for the given cell.</summary>
        /// <param name="cell">Coordinates of the cell.</param>
        /// <param name="cellIndex">The output linear cell index.</param>
        /// <returns>True if the cell index is valid for the given cell, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCellIndex(int2 cell, out int cellIndex)
        {
            if (ContainsCell(cell))
            {
                cellIndex = GetCellIndex(cell);
                return true;
            }

            cellIndex = -1;
            return false;
        }

        /// <summary>Tries to get the linear cell index for the given 2D position.</summary>
        /// <param name="position2D">Position to get the cell index for.</param>
        /// <param name="cellIndex">The output linear cell index.</param>
        /// <returns>True if the cell index is valid for the given position, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCellIndex(float2 position2D, out int cellIndex) => TryGetCellIndex(GetCell(position2D), out cellIndex);

        /// <summary>Tries to get the linear cell index for the given 3D position.</summary>
        /// <param name="position">Position to get the cell index for.</param>
        /// <param name="cellIndex">The output linear cell index.</param>
        /// <returns>True if the cell index is valid for the given position, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCellIndex(float3 position, out int cellIndex) => TryGetCellIndex(GetCell(position), out cellIndex);

        // --- Cell Count ---

        /// <summary>Counts the number of cells intersecting the given 2D bounds.</summary>
        /// <param name="bounds2D">Bounds to count the cells for.</param>
        /// <returns>The number of cells intersecting the bounds.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CountCellsIntersecting(AxisAlignedBox2D bounds2D)
        {
            if (TryGetCells(bounds2D, out int2 minCell, out int2 maxCell))
                return (maxCell.x - minCell.x + 1) * (maxCell.y - minCell.y + 1);

            return 0;
        }

        /// <summary>Counts the number of cells intersecting the given bounds.</summary>
        /// <param name="bounds">Bounds to count the cells for.</param>
        /// <returns>The number of cells intersecting the bounds.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CountCellsIntersecting(AxisAlignedBox bounds) => CountCellsIntersecting(new AxisAlignedBox2D(bounds.Min.xz, bounds.Max.xz));

        // --- Cell Iteration ---

        /// <summary>Iterates over each cell intersecting the given bounds.</summary>
        /// <param name="bounds">Bounds to iterate over.</param>
        /// <param name="operation">An operation to perform for each cell, returning false to stop iteration.</param>
        /// <returns>The number of cells iterated over.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ForEachCellIntersecting(AxisAlignedBox bounds, Func<int2, bool> operation)
        {
            int count = 0;

            if (TryGetCells(bounds, out int2 minCell, out int2 maxCell))
            {
                for (int y = minCell.y; y <= maxCell.y; ++y)
                {
                    for (int x = minCell.x; x <= maxCell.x; ++x)
                    {
                        int2 cell = new int2(x, y);
                        if (ContainsCell(cell))
                        {
                            if (!operation(cell))
                                return count;

                            ++count;
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>Iterates over each cell intersecting the given bounds.</summary>
        /// <param name="bounds">Bounds to iterate over.</param>
        /// <param name="action">Action to perform for each cell.</param>
        /// <returns>The number of cells iterated over.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ForEachCellIntersecting(AxisAlignedBox bounds, Action<int2> action)
        {
            int count = 0;

            if (TryGetCells(bounds, out int2 minCell, out int2 maxCell))
            {
                for (int y = minCell.y; y <= maxCell.y; ++y)
                {
                    for (int x = minCell.x; x <= maxCell.x; ++x)
                    {
                        int2 cell = new int2(x, y);
                        if (ContainsCell(cell))
                        {
                            action(cell);
                            ++count;
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>Iterates over each cell intersecting the given sphere.</summary>
        /// <param name="sphere">Sphere to iterate over.</param>
        /// <param name="action">Action to perform for each cell.</param>
        /// <returns>The number of cells iterated over.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ForEachCellIntersecting(Sphere sphere, Action<int2> action)
        {
            int count = 0;

            float sphereRadiusSq = sphere.Radius * sphere.Radius;

            if (TryGetCells(sphere.Bounds, out int2 minCell, out int2 maxCell))
            {
                for (int y = minCell.y; y <= maxCell.y; ++y)
                {
                    for (int x = minCell.x; x <= maxCell.x; ++x)
                    {
                        int2 cell = new int2(x, y);
                        if (ContainsCell(cell))
                        {
                            AxisAlignedBox2D cellBounds = GetCellBounds(cell);
                            if (cellBounds.Overlaps(sphere.Center.xz, sphereRadiusSq))
                            {
                                action(cell);
                                ++count;
                            }
                        }
                    }
                }
            }

            return count;
        }
    }
}
