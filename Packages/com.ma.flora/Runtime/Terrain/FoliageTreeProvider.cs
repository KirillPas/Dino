// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    sealed class FoliageTreeProvider : IFoliageProvider
    {
        InstancedTerrainFoliage m_TerrainFoliage;
        Terrain m_Terrain;
        float3 m_TerrainPosition;
        float3 m_TerrainSize;

        TreePrototype[] m_Prototypes = Array.Empty<TreePrototype>();
        InstancedPrototype[] m_InstancedPrototypes = Array.Empty<InstancedPrototype>();
        float[] m_LoadDistances = Array.Empty<float>();
        float[] m_CullingDistances = Array.Empty<float>();

        int m_GridSize;
        float2 m_CellSize;
        NativeArray<TreeInstance> m_TreeInstances;
        JobHandle m_BuildJobHandle;

        public Terrain Terrain => m_Terrain;
        public float3 TerrainPosition => m_TerrainPosition;
        public float3 TerrainSize => m_TerrainSize;
        public int LayerCount => m_Prototypes.Length;
        public int GridSize => m_GridSize;
        public float2 CellSize => m_CellSize;

        public FoliageTreeProvider(InstancedTerrainFoliage terrainFoliage)
        {
            m_TerrainFoliage = terrainFoliage;
            m_Terrain = terrainFoliage.Terrain;
            m_TreeInstances = new NativeArray<TreeInstance>(0, Allocator.Persistent);
        }

        public void Dispose()
        {
            m_TreeInstances.Dispose();
        }

        public FoliageDataChangeFlags RefreshData()
        {
            FoliageDataChangeFlags changeFlags = FoliageDataChangeFlags.None;
            if (m_Terrain == null)
                return changeFlags;

            if (RefreshPrototypes())
                changeFlags |= FoliageDataChangeFlags.Layers;

            bool positionChanged = !m_TerrainPosition.Equals(m_Terrain.transform.position);
            m_TerrainPosition = (float3)m_Terrain.transform.position;
            if (positionChanged)
                changeFlags |= FoliageDataChangeFlags.Position;

            bool sizeChanged = !m_TerrainSize.Equals(m_Terrain.terrainData.size);
            m_TerrainSize = (float3)m_Terrain.terrainData.size;
            m_GridSize = m_TerrainFoliage.TreeGridSize;
            m_CellSize = m_TerrainSize.xz / m_GridSize;
            if (sizeChanged)
                changeFlags |= FoliageDataChangeFlags.Size;

            int treeInstanceCount = m_Terrain.terrainData.treeInstanceCount;
            if (m_TreeInstances.Length != treeInstanceCount)
            {
                m_BuildJobHandle.Complete();
                m_BuildJobHandle = default;

                m_TreeInstances.Dispose();
                m_TreeInstances = new NativeArray<TreeInstance>(m_Terrain.terrainData.treeInstances, Allocator.Persistent);

                changeFlags |= FoliageDataChangeFlags.Dirty;
            }

            return changeFlags;
        }

        bool RefreshPrototypes()
        {
            TreePrototype[] prototypes = m_Terrain.terrainData.treePrototypes;
            bool changed = false;

            for (int i = 0; i < prototypes.Length; ++i)
            {
                if (prototypes[i] == null || prototypes[i].prefab == null)
                    continue;

                if (!FoliageUtility.IsSupportedPrototype(prototypes[i]))
                {
                    prototypes[i].prefab = FoliageUtility.CreatePrefabIfImmutable(prototypes[i].prefab);
                    changed = true;
                }
            }

            if (changed)
            {
                m_Terrain.terrainData.treePrototypes = prototypes;
                prototypes = m_Terrain.terrainData.treePrototypes;
#if UNITY_EDITOR
                if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    UnityEditor.EditorUtility.SetDirty(m_Terrain.terrainData);
                    UnityEditor.AssetDatabase.Refresh();
                }
#endif
            }

            m_Prototypes = prototypes;

            if (m_InstancedPrototypes.Length != m_Prototypes.Length)
            {
                m_InstancedPrototypes = new InstancedPrototype[m_Prototypes.Length];
                changed = true;
            }

            for (int i = 0; i < m_Prototypes.Length; i++)
            {
                InstancedPrototype oldPrototype = m_InstancedPrototypes[i];
                m_InstancedPrototypes[i] = FoliageUtility.GetInstancedPrototype(m_Prototypes[i]);
                changed |= oldPrototype != m_InstancedPrototypes[i];
            }

            if (m_CullingDistances.Length != m_Prototypes.Length)
            {
                m_CullingDistances = new float[m_Prototypes.Length];
                changed = true;
            }

            for (int i = 0; i < m_Prototypes.Length; i++)
            {
                if (m_InstancedPrototypes[i] == null)
                {
                    m_CullingDistances[i] = 0;
                    continue;
                }

                TerrainFoliageCullMode cullMode = m_TerrainFoliage.TreesCullMode;
                m_CullingDistances[i] = cullMode == TerrainFoliageCullMode.FromTerrain
                    ? m_Terrain.treeDistance
                    : m_InstancedPrototypes[i].CullingDistance;
            }

            if (m_LoadDistances.Length != m_Prototypes.Length)
            {
                m_LoadDistances = new float[m_Prototypes.Length];
                changed = true;
            }

            for (int i = 0; i < m_Prototypes.Length; i++)
            {
                if (m_InstancedPrototypes[i] == null)
                {
                    m_LoadDistances[i] = 0;
                    continue;
                }

                TerrainFoliageLoadMode loadMode = m_TerrainFoliage.TreesLoadMode;
                float loadDistance = loadMode == TerrainFoliageLoadMode.AlwaysLoaded
                    ? float.MaxValue
                    : m_InstancedPrototypes[i].StreamingDistance;

                if (loadDistance <= 0)
                {
                    loadDistance = m_TerrainFoliage.TreesCullMode == TerrainFoliageCullMode.FromTerrain
                        ? m_Terrain.treeDistance
                        : m_InstancedPrototypes[i].CullingDistance;
                }

                if (loadDistance <= 0)
                    loadDistance = m_Terrain.treeDistance;

                m_LoadDistances[i] = loadDistance;
            }

            return changed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsLayerEnabled(int layer)
            => m_InstancedPrototypes.IsValidIndex(layer) && m_InstancedPrototypes[layer] != null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedPrototype GetPrototype(int layer)
            => m_InstancedPrototypes[layer];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetLoadDistance(int layer)
            => m_LoadDistances[layer];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCullingDistance(int layer)
            => m_CullingDistances[layer];

        public bool ScheduleBuild(int layer, InstancedPrototype prototype, int cellIndex, Allocator allocator, out JobHandle jobHandle, out FoliageUpdatePacket updatePacket)
        {
            jobHandle = default;
            updatePacket = default;

            if (!IsLayerEnabled(layer))
                return false;

            TerrainData terrainData = m_Terrain.terrainData;
            int cellCapacity = math.min(m_TreeInstances.Length, 1024);
            NativeList<TreeInstance> cellTrees = new NativeList<TreeInstance>(cellCapacity, allocator);

            new GetPatchTreesJob
            {
                TerrainSize = terrainData.size,
                GridSize = m_GridSize,
                CellSize = m_CellSize,
                Layer = layer,
                CellIndex = cellIndex,
                TreeInstances = m_TreeInstances.AsReadOnly(),
                CellTrees = cellTrees
            }.Run();

            updatePacket = new FoliageUpdatePacket(cellTrees.Length, allocator);
            jobHandle = new ConvertTreeInstancesJob
            {
                TerrainSize = terrainData.size,
                TreeInstances = cellTrees.AsDeferredJobArray(),
                PrototypeBounds = prototype.Bounds,
                UpdatePacket = updatePacket
            }.Schedule(cellTrees.Length, ConvertTreeInstancesJob.BatchSize);

            jobHandle = cellTrees.Dispose(jobHandle);
            m_BuildJobHandle = JobHandle.CombineDependencies(m_BuildJobHandle, jobHandle);

            return true;
        }

        [BurstCompile]
        struct GetPatchTreesJob : IJob
        {
            public const int BatchSize = 128;

            public float3 TerrainSize;
            public int GridSize;
            public float2 CellSize;
            public int Layer;
            public int CellIndex;

            [ReadOnly] public NativeArray<TreeInstance>.ReadOnly TreeInstances;
            [WriteOnly] public NativeList<TreeInstance> CellTrees;

            public void Execute()
            {
                for (int i = 0; i < TreeInstances.Length; i++)
                {
                    TreeInstance treeInstance = TreeInstances[i];
                    if (treeInstance.prototypeIndex == Layer)
                    {
                        float3 treePosition = treeInstance.position * TerrainSize;
                        int2 cell = new int2(math.floor(treePosition.xz / CellSize));
                        int cellIndex = cell.y * GridSize + cell.x;
                        if (cellIndex == CellIndex)
                            CellTrees.Add(treeInstance);
                    }
                }
            }
        }

        [BurstCompile]
        struct ConvertTreeInstancesJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            public float3 TerrainSize;
            [ReadOnly] public NativeArray<TreeInstance> TreeInstances;

            public AxisAlignedBox PrototypeBounds;
            [WriteOnly] public FoliageUpdatePacket UpdatePacket;

            public void Execute(int startIndex, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int instanceIndex = startIndex + i;
                    LocalTransform instanceTransform = ConvertTreeInstanceToTransform(TerrainSize, TreeInstances[instanceIndex]);
                    UpdatePacket.Transforms[instanceIndex] = instanceTransform;

                    AxisAlignedBox instanceBounds = PrototypeBounds.TransformBy(instanceTransform);
                    UpdatePacket.Bounds[instanceIndex] = instanceBounds;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static LocalTransform ConvertTreeInstanceToTransform(in float3 terrainSize, in TreeInstance treeInstance)
        {
            float3 position = treeInstance.position * terrainSize;
            quaternion rotation = quaternion.RotateY(treeInstance.rotation);
            float3 scale = new float3(treeInstance.widthScale, treeInstance.heightScale, treeInstance.widthScale);
            return new LocalTransform(position, rotation, scale);
        }
    }
}
