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
    sealed class FoliageDetailProvider : IFoliageProvider
    {
        InstancedTerrainFoliage m_TerrainFoliage;
        Terrain m_Terrain;
        float3 m_TerrainPosition;
        float3 m_TerrainSize;

        DetailPrototype[] m_Prototypes = Array.Empty<DetailPrototype>();
        int[] m_PrototypeHashes = Array.Empty<int>();
        InstancedPrototype[] m_InstancedPrototypes = Array.Empty<InstancedPrototype>();
        float[] m_LoadDistances = Array.Empty<float>();
        float[] m_CullingDistances = Array.Empty<float>();

        int m_GridSize;
        float2 m_CellSize;

        public Terrain Terrain => m_Terrain;
        public float3 TerrainPosition => m_TerrainPosition;
        public float3 TerrainSize => m_TerrainSize;
        public int LayerCount => m_Prototypes.Length;
        public int GridSize => m_GridSize;
        public float2 CellSize => m_CellSize;

        public FoliageDetailProvider(InstancedTerrainFoliage terrainFoliage)
        {
            m_TerrainFoliage = terrainFoliage;
            m_Terrain = terrainFoliage.Terrain;
        }

        public void Dispose()
        {
        }

        public FoliageDataChangeFlags RefreshData()
        {
            FoliageDataChangeFlags changeFlags = FoliageDataChangeFlags.None;
            if (m_Terrain == null)
                return changeFlags;

            if (RefreshPrototypes())
                changeFlags |= FoliageDataChangeFlags.Layers;

            TerrainData terrainData = m_Terrain.terrainData;
            bool sizeChanged = !m_TerrainSize.Equals(terrainData.size);
            m_TerrainSize = (float3)terrainData.size;
            m_GridSize = terrainData.detailPatchCount;
            m_CellSize = m_TerrainSize.xz / m_GridSize;
            if (sizeChanged)
                changeFlags |= FoliageDataChangeFlags.Size;

            bool positionChanged = !m_TerrainPosition.Equals(m_Terrain.transform.position);
            m_TerrainPosition = (float3)m_Terrain.transform.position;
            if (positionChanged)
                changeFlags |= FoliageDataChangeFlags.Position;

            for (int i = 0; i < m_PrototypeHashes.Length; i++)
            {
                int hash = FoliageUtility.CalculatePrototypeHashCode(m_Prototypes[i]);
                m_PrototypeHashes[i] = hash;
                if (m_PrototypeHashes[i] != hash)
                    changeFlags |= FoliageDataChangeFlags.Force;
            }

            return changeFlags;
        }

        bool RefreshPrototypes()
        {
            DetailPrototype[] prototypes = m_Terrain.terrainData.detailPrototypes;
            bool changed = false;

            for (int i = 0; i < prototypes.Length; ++i)
            {
                if (prototypes[i] == null || prototypes[i].prototype == null)
                    continue;

                if (!FoliageUtility.IsSupportedPrototype(prototypes[i]))
                {
                    prototypes[i].prototype = FoliageUtility.CreatePrefabIfImmutable(prototypes[i].prototype);
                    prototypes[i].prototype = FoliageUtility.GetUnityCompatibleDetailPrefab(prototypes[i]);
                    changed = true;
                }
            }

            if (changed)
            {
                m_Terrain.terrainData.detailPrototypes = prototypes;
#if UNITY_EDITOR
                if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    UnityEditor.EditorUtility.SetDirty(m_Terrain.terrainData);
                    UnityEditor.AssetDatabase.Refresh();
                }
#endif
            }

            m_Prototypes = prototypes;

            if (m_PrototypeHashes.Length != m_Prototypes.Length)
            {
                m_PrototypeHashes = new int[m_Prototypes.Length];
                changed = true;
            }

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
                    m_CullingDistances[i] = m_Terrain.detailObjectDistance;
                    continue;
                }

                TerrainFoliageCullMode cullMode = m_TerrainFoliage.DetailsCullMode;
                m_CullingDistances[i] = cullMode == TerrainFoliageCullMode.FromTerrain
                    ? m_Terrain.detailObjectDistance
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
                    m_LoadDistances[i] = m_Terrain.detailObjectDistance;
                    continue;
                }

                TerrainFoliageLoadMode loadMode = m_TerrainFoliage.TreesLoadMode;
                float loadDistance = loadMode == TerrainFoliageLoadMode.AlwaysLoaded
                    ? float.MaxValue
                    : m_InstancedPrototypes[i].StreamingDistance;

                if (loadDistance <= 0)
                {
                    loadDistance = m_TerrainFoliage.DetailsCullMode == TerrainFoliageCullMode.FromTerrain
                        ? m_Terrain.treeDistance
                        : m_InstancedPrototypes[i].CullingDistance;
                }

                if (loadDistance <= 0)
                    loadDistance = m_Terrain.detailObjectDistance;

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

            DetailPrototype m_Prototype = m_Prototypes[layer];
            float density = 1.0f;
#if UNITY_2022_2_OR_NEWER
            if (m_Prototype.useDensityScaling)
                density *= m_Prototype.density * m_Terrain.detailObjectDensity;
#else
            density *= m_Terrain.detailObjectDensity;
#endif

            TerrainData terrainData = m_Terrain.terrainData;
            int2 cell = new int2(cellIndex % m_GridSize, cellIndex / m_GridSize);

            DetailInstanceTransform[] instances = density > 0
                ? terrainData.ComputeDetailInstanceTransforms(cell.x, cell.y, layer, density, out _)
                : Array.Empty<DetailInstanceTransform>();

            float2 terrainSize = new float2(terrainData.size.x, terrainData.size.z);
            float2 invSize = new float2(1.0f / terrainSize.x, 1.0f / terrainSize.y);
            NativeArray<float3> terrainNormals = new NativeArray<float3>(instances.Length, allocator, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < instances.Length; i++)
            {
                float2 uv = new float2(instances[i].posX, instances[i].posZ) * invSize;
                terrainNormals[i] = terrainData.GetInterpolatedNormal(uv.x, uv.y);
            }

            PinnedArrayView<DetailInstanceTransform> detailInstanceTransforms = new PinnedArrayView<DetailInstanceTransform>(instances);
            updatePacket = new FoliageUpdatePacket(instances.Length, allocator);

            jobHandle = new ConvertDetailsJob
            {
                DetailInstanceTransforms = detailInstanceTransforms.AsArray(),
#if UNITY_2022_2_OR_NEWER
                AlignToGround = m_Prototype.alignToGround,
#else
                AlignToGround = 1.0f,
#endif
                PrototypeBounds = prototype.Bounds,
                TerrainNormals = terrainNormals,
                UpdatePacket = updatePacket
            }. Schedule(instances.Length, ConvertDetailsJob.BatchSize);

            jobHandle = detailInstanceTransforms.Dispose(jobHandle);
            jobHandle = terrainNormals.Dispose(jobHandle);
            return true;
        }

        [BurstCompile, NoAlias]
        struct ConvertDetailsJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 512;

            [ReadOnly] public NativeArray<float3> TerrainNormals;
            [ReadOnly] public NativeArray<DetailInstanceTransform> DetailInstanceTransforms;

            public float AlignToGround;
            public AxisAlignedBox PrototypeBounds;

            [WriteOnly] public FoliageUpdatePacket UpdatePacket;

            public void Execute(int startIndex, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int instanceIndex = startIndex + i;
                    LocalTransform instanceTransform = ConvertDetailInstanceToTransform(DetailInstanceTransforms[instanceIndex], TerrainNormals[instanceIndex], AlignToGround);
                    UpdatePacket.Transforms[instanceIndex] = instanceTransform;

                    AxisAlignedBox instanceBounds = PrototypeBounds.TransformBy(instanceTransform);
                    UpdatePacket.Bounds[instanceIndex] = instanceBounds;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static LocalTransform ConvertDetailInstanceToTransform(in DetailInstanceTransform detailInstanceTransform, in float3 terrainNormal, float alignToGround)
        {
            float3 position = new float3(detailInstanceTransform.posX, detailInstanceTransform.posY, detailInstanceTransform.posZ);
            quaternion alignToTerrain = FromToRotation(math.up(), terrainNormal);
            quaternion randomYaw = quaternion.RotateY(detailInstanceTransform.rotationY);
            quaternion aligned = math.mul(alignToTerrain, randomYaw);
            quaternion finalRot = math.nlerp(randomYaw, aligned, alignToGround);
            float3 scale = new float3(detailInstanceTransform.scaleXZ, detailInstanceTransform.scaleY, detailInstanceTransform.scaleXZ);
            return new LocalTransform(position, finalRot, scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static quaternion FromToRotation(float3 from, float3 to)
        {
            float dot = math.dot(from, to);
            if (dot > 0.999999f)
                return quaternion.identity;

            float3 cross = math.cross(from, to);
            return math.normalizesafe(new quaternion(cross.x, cross.y, cross.z, dot + 1.0f));
        }
    }
}
