// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MA.Collections.Unsafe;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace MA.Flora
{
    struct FoliageUpdatePacket : IDisposable
    {
        public int InstanceCount => Transforms.Length;
        public NativeArray<LocalTransform> Transforms;
        public NativeArray<AxisAlignedBox> Bounds;

        public FoliageUpdatePacket(int count, Allocator allocator)
        {
            Transforms = new NativeArray<LocalTransform>(count, allocator);
            Bounds = new NativeArray<AxisAlignedBox>(count, allocator);
        }

        public void Dispose()
        {
            Transforms.Dispose();
            Bounds.Dispose();
        }
    }

    struct FoliageStreamingSource
    {
        public InstancedCameraID CameraID;
        public float3 Center;
        public float MaxDistance;
        public ushort FixedDistanceMoved;
    }

    struct FoliageDirtyCell : IEquatable<FoliageDirtyCell>
    {
        public int Index;
        public float DistanceSq;

        public bool Equals(FoliageDirtyCell other) => Index == other.Index;
        public override bool Equals(object obj) => obj is FoliageDirtyCell other && Equals(other);
        public override int GetHashCode() => Index;
    }

    struct FoliageDirtyCellComparer : IComparer<FoliageDirtyCell>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(FoliageDirtyCell x, FoliageDirtyCell y)
        {
            return x.DistanceSq.CompareTo(y.DistanceSq);
        }
    }

    struct FoliageBuiltCell : IEquatable<FoliageBuiltCell>
    {
        public int Index;
        public FoliageUpdatePacket Packet;

        public bool Equals(FoliageBuiltCell other) => Index == other.Index;
        public override bool Equals(object obj) => obj is FoliageBuiltCell other && Equals(other);
        public override int GetHashCode() => Index;
    }

    sealed class FoliageLayer<TProvider> : IDisposable
        where TProvider : IFoliageProvider
    {
        TProvider m_Provider;
        FoliageScheduler m_Scheduler;
        FoliageJobType m_JobState;
        FoliageJobKey m_JobKey;

        int m_Layer;
        FoliageRenderer[] m_Renderers;
        UnsafeBitList m_DirtyCells;
        NativeList<FoliageDirtyCell> m_GatheredCells;
        HashSet<int> m_ActiveCells;
        HashSet<FoliageBuiltCell> m_BuiltCells;

        AxisAlignedBox2D m_DirtyRegion;
        bool m_AllDirty;

        public FoliageLayer(TProvider provider, FoliageScheduler scheduler, int layer)
        {
            m_Provider = provider;
            m_Scheduler = scheduler;
            m_JobState = FoliageJobType.None;
            m_JobKey = FoliageJobKey.Null;

            m_Layer = layer;
            m_Renderers = Array.Empty<FoliageRenderer>();
            m_DirtyCells = new UnsafeBitList(64, Allocator.Persistent);
            m_GatheredCells = new NativeList<FoliageDirtyCell>(64, Allocator.Persistent);
            m_ActiveCells = new HashSet<int>(16);
            m_BuiltCells = new HashSet<FoliageBuiltCell>(16);
            m_AllDirty = true;
            m_DirtyRegion = AxisAlignedBox2D.Empty;
        }

        void CancelJobs()
        {
            if (m_JobKey != FoliageJobKey.Null)
                m_Scheduler.Remove(m_JobKey);

            m_JobKey = FoliageJobKey.Null;
            m_JobState = FoliageJobType.None;
            m_GatheredCells.Clear();
            m_BuiltCells.Clear();
        }

        public void Dispose()
        {
            CancelJobs();

            foreach (int activeCellIndex in m_ActiveCells)
                m_Renderers[activeCellIndex].Dispose();

            m_ActiveCells.Clear();
            m_DirtyCells.Dispose();
            m_GatheredCells.Dispose();
        }

        public void Resize(int gridSize)
        {
            if (m_Renderers.Length == gridSize * gridSize)
                return;

            CancelJobs();

            foreach (int activeCellIndex in m_ActiveCells)
                m_Renderers[activeCellIndex].Dispose();

            int cellCount = gridSize * gridSize;
            m_Renderers = new FoliageRenderer[cellCount];
            m_ActiveCells.Clear();

            m_DirtyCells.Resize(cellCount);
            m_DirtyCells.SetAll(true);
            m_GatheredCells.Capacity = cellCount;
            m_BuiltCells.Clear();
        }

        public void MarkAllDirty()
        {
            m_AllDirty = true;
        }

        public void MarkRenderStateDirty()
        {
            foreach (int activeCellIndex in m_ActiveCells)
                m_Renderers[activeCellIndex].MarkRenderStateDirty();
        }

        public void MarkRegionDirty(AxisAlignedBox2D region)
        {
            m_DirtyRegion += region;
        }

        public void ForceRebuild()
        {
            MarkAllDirty();

            foreach (int activeCellIndex in m_ActiveCells)
                m_Renderers[activeCellIndex].ForceUpdate();
        }

        public void UpdatePrototype()
        {
            InstancedPrototype prototype = m_Provider.GetPrototype(m_Layer);
            foreach (int activeCellIndex in m_ActiveCells)
                m_Renderers[activeCellIndex].UpdatePrototype(prototype);
        }

        void CompleteGather()
        {
            if (m_JobState == FoliageJobType.GatherDirtyCells)
                m_Scheduler.ForceComplete(m_JobKey);
        }

        const int k_MaxComputeFrames = 4;

        public void NextFrame(FoliageStreamingSource streamingSource)
        {
            bool scheduleUpdate = m_JobState > FoliageJobType.None || streamingSource.FixedDistanceMoved > 0;

            int cellCount = m_Provider.CellCount;
            if (m_DirtyCells.Length != cellCount)
            {
                scheduleUpdate = true;
                Resize(m_Provider.GridSize);
            }

            if (m_AllDirty)
            {
                scheduleUpdate = true;
                CompleteGather();

                m_AllDirty = false;
                m_DirtyCells.SetAll(true);
            }

            if (!m_DirtyRegion.IsEmpty)
            {
                scheduleUpdate = true;
                CompleteGather();

                float2 cellSize = m_Provider.CellSize;
                int gridSize = m_Provider.GridSize;

                int2 minCell = math.clamp(new int2(math.floor(m_DirtyRegion.Min / cellSize)), 0, gridSize - 1);
                int2 maxCell = math.clamp(new int2(math.floor(m_DirtyRegion.Max / cellSize)), 0, gridSize - 1);

                for (int y = minCell.y; y <= maxCell.y; ++y)
                {
                    for (int x = minCell.x; x <= maxCell.y; ++x)
                    {
                        int cellIndex = y * gridSize + x;
                        m_DirtyCells[cellIndex] = true;
                        m_Renderers[cellIndex]?.ForceUpdate();
                    }
                }

                m_DirtyRegion = AxisAlignedBox2D.Empty;
            }

            if (!scheduleUpdate)
                return;

            switch (m_JobState)
            {
                case FoliageJobType.None:
                {
                    if (ScheduleGatherDirtyCells(streamingSource, out var gatherHandle))
                    {
                        m_JobState = FoliageJobType.GatherDirtyCells;
                        m_JobKey = m_Scheduler.Schedule(new FoliageJob
                        {
                            Handle = gatherHandle,
                            Frames = k_MaxComputeFrames
                        });
                    }
                    break;
                }
                case FoliageJobType.GatherDirtyCells:
                {
                    if (m_Scheduler.IsComplete(m_JobKey))
                    {
                        m_Scheduler.Remove(m_JobKey);

                        if (ScheduleBuildDirtyCells(m_GatheredCells.AsArray(), out var buildHandle))
                        {
                            m_JobState = FoliageJobType.BuildInstances;
                            m_JobKey = m_Scheduler.Schedule(new FoliageJob
                            {
                                Handle = buildHandle,
                                Frames = k_MaxComputeFrames
                            });
                        }
                        else
                        {
                            m_JobState = FoliageJobType.None;
                        }

                        m_GatheredCells.Clear();
                    }
                    break;
                }
                case FoliageJobType.BuildInstances:
                {
                    if (m_Scheduler.IsComplete(m_JobKey))
                    {
                        m_JobState = FoliageJobType.None;
                        m_Scheduler.Remove(m_JobKey);
                        UpdateDirtyCellRenderers();
                    }
                    break;
                }
            }
        }

        bool ScheduleGatherDirtyCells(FoliageStreamingSource source, out JobHandle jobHandle)
        {
            jobHandle = default;

            bool hasDirtyCells = m_DirtyCells.Contains(true);
            if (!hasDirtyCells)
                return false;

            GatherDirtyFoliageCellsInRange gatherDirtyFoliageCellsInRange = new GatherDirtyFoliageCellsInRange
            {
                GridSize = m_Provider.GridSize,
                CellSize = m_Provider.CellSize,
                TerrainPosition = m_Provider.TerrainPosition.xz,
                DirtyCells = m_DirtyCells,
                StreamingSource = source,
                LoadDistance = m_Provider.GetLoadDistance(m_Layer),
                Result = m_GatheredCells.AsParallelWriter()
            };

            jobHandle = gatherDirtyFoliageCellsInRange.ScheduleBatchByRef(m_Provider.CellCount, GatherDirtyFoliageCellsInRange.BatchSize);
            jobHandle = m_GatheredCells
                .AsDeferredJobArray()
                .SortJob(new FoliageDirtyCellComparer())
                .Schedule(jobHandle);

            return true;
        }

        bool ScheduleBuildDirtyCells(NativeArray<FoliageDirtyCell> gatheredCells, out JobHandle jobHandle)
        {
            jobHandle = default;

            bool scheduledBuild = false;

            for (int i = 0; i < gatheredCells.Length; ++i)
            {
                int cellIndex = gatheredCells[i].Index;
                InstancedPrototype prototype = m_Provider.GetPrototype(m_Layer);

                if (m_Provider.ScheduleBuild(m_Layer, prototype, cellIndex, Allocator.TempJob, out JobHandle handle, out FoliageUpdatePacket packet))
                {
                    jobHandle = JobHandle.CombineDependencies(jobHandle, handle);
                    scheduledBuild = true;

                    m_BuiltCells.Add(new FoliageBuiltCell
                    {
                        Index = cellIndex,
                        Packet = packet
                    });
                }
            }

            return scheduledBuild;
        }

        void UpdateDirtyCellRenderers()
        {
            foreach (var buildCell in m_BuiltCells)
            {
                int cellIndex = buildCell.Index;
                m_DirtyCells[cellIndex] = false;

                FoliageUpdatePacket packet = buildCell.Packet;
                FoliageRenderer renderer = m_Renderers[cellIndex];

                if (packet.InstanceCount == 0)
                {
                    if (renderer != null)
                    {
                        renderer.Dispose();
                        m_Renderers[cellIndex] = null;
                        m_ActiveCells.Remove(cellIndex);
                    }
                }
                else
                {
                    if (renderer == null)
                    {
                        renderer = new FoliageRenderer();
                        renderer.Initialize(m_Provider.Terrain.transform, m_Provider.GetPrototype(m_Layer));
                        m_Renderers[cellIndex] = renderer;
                        m_ActiveCells.Add(cellIndex);
                    }

                    float cullingDistance = m_Provider.GetCullingDistance(m_Layer);
                    renderer.UpdateCullingDistance(cullingDistance);

                    if (renderer.RequiresUpdate || packet.InstanceCount != renderer.InstanceCount)
                        renderer.UpdateTransforms(packet);
                }

                packet.Dispose();
            }

            m_BuiltCells.Clear();
        }
    }

    [BurstCompile]
    unsafe struct GatherDirtyFoliageCellsInRange : IJobParallelForBatchLegacyCompatible
    {
        public const int BatchSize = 128;

        public int GridSize;
        public float2 CellSize;
        public float2 TerrainPosition;
        public FoliageStreamingSource StreamingSource;
        public float LoadDistance;

        [ReadOnly] public UnsafeBitList DirtyCells;
        [WriteOnly] public NativeList<FoliageDirtyCell>.ParallelWriter Result;

        public void Execute(int startIndex, int count)
        {
            float streamingRadius = math.min(LoadDistance, StreamingSource.MaxDistance);
            float streamingRadiusSq = streamingRadius * streamingRadius;
            using UnsafeList<FoliageDirtyCell> cellsToLoad = new UnsafeList<FoliageDirtyCell>(count, Allocator.Temp);

            foreach (int cellIndex in DirtyCells.SetBitEnumerator(startIndex, count))
            {
                int2 cell = new int2(cellIndex % GridSize, cellIndex / GridSize);

                float2 min = TerrainPosition + cell * CellSize;
                float2 max = min + CellSize;
                AxisAlignedBox2D cellBounds = new AxisAlignedBox2D(min, max);

                float2 sourceCenter2D = StreamingSource.Center.xz;
                float2 point = cellBounds.GetClosestPointTo(sourceCenter2D);
                float2 delta = sourceCenter2D - point;
                float distSq = math.lengthsq(delta);

                if (distSq <= streamingRadiusSq)
                {
                    FoliageDirtyCell key = new FoliageDirtyCell
                    {
                        Index = cellIndex,
                        DistanceSq = distSq
                    };

                    cellsToLoad.Add(key);
                }
            }

            if (cellsToLoad.Length > 0)
                Result.AddRangeNoResize(cellsToLoad);
        }
    }
}
