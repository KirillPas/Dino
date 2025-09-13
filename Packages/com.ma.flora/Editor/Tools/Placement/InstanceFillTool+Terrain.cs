// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
#if !UNITY_2022_2_OR_NEWER
using MA.Collections;
#endif
using MA.Core;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TerrainTools;

namespace MA.Flora.Editor
{
    partial class InstanceFillTool
    {
        ComputeShader m_TerrainFillCompute;
        int m_TerrainFillKernel;

        static readonly int _HeightMap        = Shader.PropertyToID("_HeightMap");
        static readonly int _NormalMap        = Shader.PropertyToID("_NormalMap");
        static readonly int _TransformsBuffer = Shader.PropertyToID("_TransformsBuffer");

        static readonly int _CellMinMax       = Shader.PropertyToID("_CellMinMax");
        static readonly int _TerrainPosition  = Shader.PropertyToID("_TerrainPosition");
        static readonly int _TerrainSize      = Shader.PropertyToID("_TerrainSize");

        static readonly int _MinScale         = Shader.PropertyToID("_MinScale");
        static readonly int _MaxScale         = Shader.PropertyToID("_MaxScale");
        static readonly int _MaskParams0      = Shader.PropertyToID("_MaskParams0");
        static readonly int _AlignmentParams0 = Shader.PropertyToID("_AlignmentParams0");
        static readonly int _AlignmentParams1 = Shader.PropertyToID("_AlignmentParams1");

        static readonly int _HaltonBaseIndex  = Shader.PropertyToID("_HaltonBaseIndex");
        static readonly int _DesiredCount     = Shader.PropertyToID("_DesiredCount");

        void TerrainDrawPreview(Terrain terrain, List<InstancedPrototype> prototypes)
        {
            bool prevDrawInstanced = terrain.drawInstanced;
            terrain.drawInstanced = true;

            BrushTransform brushTransform = TerrainPaintUtility.CalculateBrushTransform(terrain, new Vector2(0.5f, 0.5f), terrain.terrainData.size.x, 0);
            PaintContext paintContext = TerrainPaintUtility.BeginPaintHeightmap(terrain, brushTransform.GetBrushXYBounds(), 1);

            TerrainPaintUtility.SetupTerrainToolMaterialProperties(paintContext, brushTransform, PreviewMaterial);

            float4 maskParams = GetPreviewMaskParams(prototypes);
            PreviewMaterial.SetVector(_MaskParams0, maskParams);
            PreviewMaterial.SetTexture(_Heightmap, paintContext.sourceRenderTexture);
            PreviewMaterial.SetTexture(_Normalmap, terrain.normalmapTexture);

            TerrainPaintUtilityEditor.DrawBrushPreview(paintContext, TerrainBrushPreviewMode.SourceRenderTexture, paintContext.sourceRenderTexture, brushTransform, PreviewMaterial, (int)PreviewPasses.FillTerrain);
            TerrainPaintUtility.ReleaseContextResources(paintContext);

            terrain.drawInstanced = prevDrawInstanced;
        }

        int CalculateTotalDesiredInstanceCount(List<InstancedPrototype> prototypes, Terrain terrain)
        {
            int totalInstanceCount = 0;
            foreach (InstancedPrototype prototype in prototypes)
            {
                float3 terrainExtents = (float3)terrain.terrainData.size * 0.5f;
                AxisAlignedBox terrainBoundsLS = AxisAlignedBox.FromExtents(terrainExtents, terrainExtents);

                int cellSize = RuntimeSpatialHash.Instance.GetPrototypeGridLayout(prototype).CellSize;
                int2 gridSize = CreateFillGrid(terrainBoundsLS, cellSize, out int2 minCell, out int2 maxCell);
                float cellSurfaceArea = cellSize * cellSize;

                InstancePlacementSettings placementSettings = prototype.PlacementSettings;
                float densityStrength = InstancePlacementUtility.ComputeDensity(placementSettings);

                int desiredCountPerCell = (int)math.ceil(cellSurfaceArea * densityStrength * DensityStrength / (10.0f * 10.0f));
                switch (desiredCountPerCell)
                {
                    case 0:
                        continue;
                    case > k_MaxInstanceCountPerCell:
                        Debug.Log($"Desired count per cell ({desiredCountPerCell}) exceeds maximum fill count per cell ({k_MaxInstanceCountPerCell}). Clamping to maximum.");
                        desiredCountPerCell = k_MaxInstanceCountPerCell;
                        break;
                }

                totalInstanceCount += gridSize.x * gridSize.y * desiredCountPerCell;
            }

            return totalInstanceCount;
        }

        const string k_ProgressTitle = "Filling Terrain";
        const string k_ProgressMessage = "Filling terrain with instances...";

        async void ExecuteTerrainFill(List<InstancedPrototype> prototypes, Terrain terrain, TerrainCollider collider)
        {
            if (m_TerrainFillCompute == null)
            {
                m_TerrainFillCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.ma.flora/Editor/EditorResources/Compute/TerrainPlacement.compute");
                m_TerrainFillKernel = m_TerrainFillCompute.FindKernel("FillTerrain");
            }

            m_IsAsyncTaskRunning = true;
            InstancePlacementUtility.BeginPlacementOperation("Fill Terrain");
            InstancingSystem.DisableAutoBuildTrees = true;
            bool prevDrawInstanced = terrain.drawInstanced;
            terrain.drawInstanced = true; // Force enable normal map support with instancing

            int totalInstanceCount = CalculateTotalDesiredInstanceCount(prototypes, terrain);
            if (totalInstanceCount > k_UndoInstanceCountMax)
            {
                if (!EditorUtility.DisplayDialog("Instance Fill",
                        $"The total instance count ({totalInstanceCount}) exceeds the maximum undo instance count ({k_UndoInstanceCountMax}). If you continue, undo will be disabled for this operation.",
                        "Continue", "Cancel"))
                {
                    goto END_OPERATION;
                }

                m_UndoEnabled = false;
            }

            EditorUtility.DisplayProgressBar(k_ProgressTitle, k_ProgressMessage, 0.0f);

            foreach (InstancedPrototype prototype in prototypes)
            {
                await DispatchTerrainFillPrototype(prototype, terrain, collider);
                EditorUtility.DisplayProgressBar(k_ProgressTitle, k_ProgressMessage, prototypes.IndexOf(prototype) / (float)prototypes.Count);
            }

            EditorUtility.ClearProgressBar();

            END_OPERATION:
            terrain.drawInstanced = prevDrawInstanced;
            InstancingSystem.DisableAutoBuildTrees = false;
            InstancePlacementUtility.EndPlacementOperation();
            m_IsAsyncTaskRunning = false;
            m_UndoEnabled = true;
        }

        const int k_TerrainComputeGroupSize  = 256;
        const int k_MaxInstanceCountPerCell  = 1024 * 16;
        const int k_MaxCellsPerFrame         = 16;

        async Task DispatchTerrainFillPrototype(InstancedPrototype prototype, Terrain terrain, TerrainCollider collider)
        {
            float3 terrainSize = terrain.terrainData.size;
            AxisAlignedBox terrainBoundsLS;
            terrainBoundsLS.Min = 0;
            terrainBoundsLS.Max = terrainSize;

            AxisAlignedBox terrainBoundsWS = terrainBoundsLS;
            terrainBoundsWS.Min = terrain.transform.position;
            terrainBoundsWS.Max = terrainBoundsWS.Min + terrainSize;

            int cellSize = RuntimeSpatialHash.Instance.GetPrototypeGridLayout(prototype).CellSize;
            int2 gridSize = CreateFillGrid(terrainBoundsLS, cellSize, out int2 minCell, out int2 maxCell);
            float cellSurfaceArea = cellSize * cellSize;

            InstancePlacementSettings placementSettings = prototype.PlacementSettings;
            float densityStrength = InstancePlacementUtility.ComputeDensity(placementSettings);

            int desiredCountPerCell = (int)math.ceil(cellSurfaceArea * densityStrength * DensityStrength / (10.0f * 10.0f));
            switch (desiredCountPerCell)
            {
                case 0:
                    return;
                case > k_MaxInstanceCountPerCell:
                    Debug.Log($"Desired count per cell ({desiredCountPerCell}) exceeds maximum fill count per cell ({k_MaxInstanceCountPerCell}). Clamping to maximum.");
                    desiredCountPerCell = k_MaxInstanceCountPerCell;
                    break;
            }

            Texture heightmap = terrain.terrainData.heightmapTexture;
            Texture normalmap = terrain.normalmapTexture;
            if (normalmap == null) m_TerrainFillCompute.EnableKeyword("COMPUTE_NORMALS");
            else                     m_TerrainFillCompute.DisableKeyword("COMPUTE_NORMALS");

            long haltonBaseIndex = DateTime.Now.Ticks;
            int cellCount = gridSize.x * gridSize.y;
            int cellIndex = 0;

            NativeArray<int> placedCounts = new NativeArray<int>(cellCount, Allocator.Persistent);
            GraphicsBuffer potentialInstanceCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, cellCount, 4);
            GraphicsBuffer[] potentialInstancesBuffer = new GraphicsBuffer[cellCount];

            for (int i = 0; i < cellCount; i++)
                potentialInstancesBuffer[i] = new GraphicsBuffer(GraphicsBuffer.Target.Append, desiredCountPerCell, UnsafeUtility.SizeOf<LocalTransform>());

            CommandBuffer cmd = CommandBufferPool.Get("Flora: Terrain Fill");

            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    int2 currentCell = new int2(x, y);
                    int currentCellIndex = cellIndex;
                    AxisAlignedBox cellBoundsLS = GetFillCellBounds(currentCell, cellSize, terrainBoundsLS);

                    cmd.SetComputeVectorParam(m_TerrainFillCompute, _TerrainPosition, math.float4(terrainBoundsWS.Min, 1.0f));
                    cmd.SetComputeVectorParam(m_TerrainFillCompute, _TerrainSize, math.float4(terrainBoundsWS.Size, 0.0f));
                    cmd.SetComputeTextureParam(m_TerrainFillCompute, m_TerrainFillKernel, _HeightMap, heightmap);
                    cmd.SetComputeTextureParam(m_TerrainFillCompute, m_TerrainFillKernel, _NormalMap, normalmap);
                    cmd.SetComputeIntParam(m_TerrainFillCompute, _DesiredCount, desiredCountPerCell);

                    SetTerrainPlacementSettingParams(cmd, placementSettings);

                    cmd.SetComputeVectorParam(m_TerrainFillCompute, _CellMinMax, math.float4(cellBoundsLS.Min.xz, cellBoundsLS.Max.xz));
                    cmd.SetComputeIntParam(m_TerrainFillCompute, _HaltonBaseIndex, (int)haltonBaseIndex);
                    unchecked { haltonBaseIndex += desiredCountPerCell; }

                    GraphicsBuffer potentialInstances = potentialInstancesBuffer[currentCellIndex];
                    cmd.SetBufferCounterValue(potentialInstances, 0);
                    cmd.SetComputeBufferParam(m_TerrainFillCompute, m_TerrainFillKernel, _TransformsBuffer, potentialInstances);

                    int3 dispatchGroups = ComputeUtility.WrapDispatchCount(desiredCountPerCell, k_TerrainComputeGroupSize);
                    cmd.DispatchCompute(m_TerrainFillCompute, m_TerrainFillKernel, dispatchGroups);

                    AxisAlignedBox cellBoundsWS = cellBoundsLS;
                    cellBoundsWS.Min += terrainBoundsWS.Min;

                    cmd.CopyCounterValue(potentialInstances, potentialInstanceCountBuffer, (uint)currentCellIndex * sizeof(uint));
                    cmd.RequestAsyncReadback(potentialInstanceCountBuffer, req => OnCountComplete(req, placedCounts, currentCellIndex));
                    cmd.RequestAsyncReadback(potentialInstances, req => OnPlaceComplete(req, prototype, collider, cellBoundsWS, placedCounts, currentCellIndex));

                    cellIndex++;

                    if (cellIndex % k_MaxCellsPerFrame == 0)
                    {
                        cmd.WaitAllAsyncReadbackRequests();
                        Graphics.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        EditorUtility.DisplayProgressBar(k_ProgressTitle, "Constructing cells...", cellIndex / (float)cellCount);
                        EditorApplication.QueuePlayerLoopUpdate();
                        await Task.Yield();
                    }
                }
            }

            cmd.WaitAllAsyncReadbackRequests();
            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            for (int i = 0; i < cellCount; i++)
            {
                potentialInstancesBuffer[i].Release();
            }

            potentialInstanceCountBuffer.Release();
            placedCounts.Dispose();
        }

        static int2 CreateFillGrid(AxisAlignedBox terrainBounds, int cellSize, out int2 minCell, out int2 maxCell)
        {
            minCell = new int2(math.floor(terrainBounds.Min.xz / cellSize));
            maxCell = new int2(math.floor(terrainBounds.Max.xz / cellSize));
            return math.mul(maxCell - minCell + 1, new int2(1, 1));
        }

        static AxisAlignedBox GetFillCellBounds(int2 cell, int cellSize, AxisAlignedBox terrainBounds)
        {
            float3 min = default;
            min.xz = new float2(cell * cellSize);
            min.y  = terrainBounds.Min.y;

            float3 max = default;
            max.xz = min.xz + new float2(cellSize);
            max.y  = terrainBounds.Max.y;

            return new AxisAlignedBox(min, max);
        }

        void SetTerrainPlacementSettingParams(CommandBuffer cmd, InstancePlacementSettings placementSettings)
        {
            float4 scaleMin = math.float4(placementSettings.ScaleX.Min, placementSettings.ScaleY.Min, placementSettings.ScaleZ.Min, (int)placementSettings.ScalingMode);
            float4 scaleMax = math.float4(placementSettings.ScaleX.Max, placementSettings.ScaleY.Max, placementSettings.ScaleZ.Max, 0.0f);
            cmd.SetComputeVectorParam(m_TerrainFillCompute, _MinScale, scaleMin);
            cmd.SetComputeVectorParam(m_TerrainFillCompute, _MaxScale, scaleMax);

            bool alignToSurface = placementSettings.AlignToSurface;
            float alignToSurfaceMaxAngle = math.radians(placementSettings.AlignToSurfaceMaxAngle);
            float randomPitchAngle = math.radians(placementSettings.RandomPitchAngle);
            float4 alignment0 = math.float4(alignToSurface ? 1 : 0, alignToSurfaceMaxAngle, placementSettings.RandomizeYaw ? 1 : 0, randomPitchAngle);
            cmd.SetComputeVectorParam(m_TerrainFillCompute, _AlignmentParams0, alignment0);

            float4 alignment1 = math.float4(placementSettings.VerticalOffset.Min, placementSettings.VerticalOffset.Max, 0.0f, 0.0f);
            cmd.SetComputeVectorParam(m_TerrainFillCompute, _AlignmentParams1, alignment1);

            Interval slopeMask = placementSettings.SlopeMask;
            Interval heightMask = placementSettings.HeightMask;
            float4 alignmentParams = math.float4(math.radians(slopeMask.Min), math.radians(slopeMask.Max), heightMask.Min, heightMask.Max);
            cmd.SetComputeVectorParam(m_TerrainFillCompute, _MaskParams0, alignmentParams);
        }

        void OnCountComplete(AsyncGPUReadbackRequest request, NativeArray<int> placedCount, int index)
        {
            if (request.hasError)
            {
                Debug.LogError("Failed to readback potential instance count.");
                return;
            }

            NativeArray<int> counter = request.GetData<int>();
            int validTransforms = counter[index];
            placedCount[index] = validTransforms;
            counter.Dispose();
        }

        void OnPlaceComplete(AsyncGPUReadbackRequest request, InstancedPrototype prototype, TerrainCollider collider, AxisAlignedBox bounds, NativeArray<int> placedCount, int index)
        {
            if (request.hasError)
            {
                Debug.LogError("Failed to readback placed instances.");
                return;
            }

            NativeArray<LocalTransform> instances = request.GetData<LocalTransform>().GetSubArray(0, placedCount[index]);

            if (instances.Length > 0)
            {
                InstancePlacementUtility.PlaceInstances(prototype, collider.transform, instances.AsReadOnlySpan(), bounds, PlacementOccluders);
            }

            instances.Dispose();
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }
    }
}
