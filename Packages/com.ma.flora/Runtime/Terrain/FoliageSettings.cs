// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    readonly struct FoliageSettings
    {
        public readonly float3 Size;
        public readonly int GridSize;
        public readonly float2 CellSize;
        public readonly int SizeHash;
        public int CellCount => GridSize * GridSize;

        public readonly float3 Position;
        public readonly TerrainFoliageLoadMode LoadMode;
        public readonly float LoadDistance;
        public readonly float LoadDistanceSq;
        public readonly TerrainFoliageCullMode CullMode;
        public readonly float CullDistance;
        public readonly float CullDistanceSq;
        public readonly int RenderHash;

        FoliageSettings(
            float3 position, float3 size, int gridSize, float2 cellSize,
            TerrainFoliageLoadMode loadMode, float loadDistance, float loadDistanceSq,
            TerrainFoliageCullMode cullMode, float cullDistance, float cullDistanceSq)
        {
            Position = position;
            Size = size;
            GridSize = gridSize;
            CellSize = cellSize;
            SizeHash = CalculateSizeHash(size, gridSize, cellSize);

            LoadMode = loadMode;
            LoadDistance = loadDistance;
            LoadDistanceSq = loadDistanceSq;
            CullMode = cullMode;
            CullDistance = cullDistance;
            CullDistanceSq = cullDistanceSq;
            RenderHash = CalculateRenderHash(position, loadMode, loadDistanceSq, cullMode, cullDistanceSq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSizeChange(in FoliageSettings other) => SizeHash != other.SizeHash;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsRenderChange(in FoliageSettings other) => RenderHash != other.RenderHash;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FoliageSettings CreateDetailSettings(InstancedTerrainFoliage terrainFoliage, InstancedPrototype prototype)
        {
            Terrain terrain = terrainFoliage.Terrain;
            float3 position = (float3)terrain.transform.position;
            float3 size = (float3)terrain.terrainData.size;
            int gridSize = terrain.terrainData.detailPatchCount;
            float2 cellSize = size.xz / gridSize;

            TerrainFoliageLoadMode loadMode = terrainFoliage.DetailsLoadMode;
            TerrainFoliageCullMode cullMode = terrainFoliage.DetailsCullMode;

            float culledDistance = cullMode == TerrainFoliageCullMode.FromPrototype
                ? prototype.CullingDistance
                : terrainFoliage.Terrain.detailObjectDistance;
            float culledDistanceSq = culledDistance * culledDistance;

            float loadDistance = loadMode == TerrainFoliageLoadMode.AlwaysLoaded
                ? float.MaxValue
                : culledDistance;
            float loadDistanceSq = loadDistance * loadDistance;

            return new FoliageSettings(position, size, gridSize, cellSize, loadMode, loadDistance, loadDistanceSq, cullMode, culledDistance, culledDistanceSq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FoliageSettings CreateTreeSettings(InstancedTerrainFoliage terrainFoliage, InstancedPrototype prototype, int gridSize)
        {
            Terrain terrain = terrainFoliage.Terrain;
            float3 position = (float3)terrain.transform.position;
            float3 size = (float3)terrain.terrainData.size;
            float2 cellSize = size.xz / gridSize;

            TerrainFoliageLoadMode loadMode = terrainFoliage.TreesLoadMode;
            TerrainFoliageCullMode cullMode = terrainFoliage.TreesCullMode;

            float culledDistance = cullMode == TerrainFoliageCullMode.FromPrototype
                ? prototype.CullingDistance
                : terrainFoliage.Terrain.treeDistance;
            float culledDistanceSq = culledDistance * culledDistance;

            float loadDistance = loadMode == TerrainFoliageLoadMode.AlwaysLoaded
                ? float.MaxValue
                : culledDistance;
            float loadDistanceSq = loadDistance * loadDistance;

            return new FoliageSettings(position, size, gridSize, cellSize, loadMode, loadDistance, loadDistanceSq, cullMode, culledDistance, culledDistanceSq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CalculateRenderHash(float3 position, TerrainFoliageLoadMode loadMode, float loadDistanceSq, TerrainFoliageCullMode cullMode, float cullDistanceSq)
        {
            unchecked
            {
                int hashCode = position.GetHashCode();
                hashCode = (hashCode * 397) ^ (int) loadMode;
                hashCode = (hashCode * 397) ^ loadDistanceSq.GetHashCode();
                hashCode = (hashCode * 397) ^ (int) cullMode;
                hashCode = (hashCode * 397) ^ cullDistanceSq.GetHashCode();
                return hashCode;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CalculateSizeHash(float3 size, int gridSize, float2 cellSize)
        {
            unchecked
            {
                int hashCode = size.GetHashCode();
                hashCode = (hashCode * 397) ^ gridSize;
                hashCode = (hashCode * 397) ^ cellSize.GetHashCode();
                return hashCode;
            }
        }
    }
}
