// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    [Flags]
    enum FoliageDataChangeFlags
    {
        None      = 0,
        Layers    = 1 << 0,
        Size      = 1 << 1,
        Position  = 1 << 2,
        Dirty     = 1 << 3,
        Force     = 1 << 4,
        All       = Layers | Size | Position | Dirty | Force
    }

    interface IFoliageProvider : IDisposable
    {
        Terrain Terrain { get; }
        float3 TerrainPosition { get; }
        float3 TerrainSize { get; }

        int LayerCount { get; }
        int GridSize { get; }
        float2 CellSize { get; }
        int CellCount => GridSize * GridSize;

        FoliageDataChangeFlags RefreshData();
        bool IsLayerEnabled(int layer);
        InstancedPrototype GetPrototype(int layer);
        float GetLoadDistance(int layer);
        float GetCullingDistance(int layer);
        bool ScheduleBuild(int layer, InstancedPrototype prototype, int cellIndex, Allocator allocator, out JobHandle jobHandle, out FoliageUpdatePacket updatePacket);
    }
}
