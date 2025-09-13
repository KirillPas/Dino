// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace MA.Flora.Editor
{
    partial class InstanceFillTool
    {
        List<InstancedMeshContainer> m_NearbyContainers = new List<InstancedMeshContainer>();

        void MeshDrawPreview(MeshRenderer meshRenderer, MeshFilter meshFilter, List<InstancedPrototype> prototypes)
        {
            float4 maskParams = GetPreviewMaskParams(prototypes);
            PreviewMaterial.SetPass(1);
            PreviewMaterial.SetVector(_MaskParams0, maskParams);
            Graphics.DrawMeshNow(meshFilter.sharedMesh, meshRenderer.transform.localToWorldMatrix, (int)PreviewPasses.FillMesh);
        }

        void ExecuteMeshFill(List<InstancedPrototype> prototypes, MeshCollider collider)
        {
            if (!collider.TryGetComponent(out MeshRenderer meshRenderer) || !meshRenderer.enabled ||
                !collider.TryGetComponent(out MeshFilter meshFilter) || !meshFilter.sharedMesh)
                return;

            InstancePlacementUtility.BeginPlacementOperation("Fill Mesh");

            Mesh mesh = meshFilter.sharedMesh;
            bool meshHasVertexColors = mesh.HasVertexAttribute(VertexAttribute.Color);
            float4x4 meshLocalToWorld = collider.transform.localToWorldMatrix;
            AxisAlignedBox meshBoundsWS = meshRenderer.bounds;

            int totalTriangleCount = 0;
            for (int submeshIndex = 0; submeshIndex < mesh.subMeshCount; ++submeshIndex)
            {
                SubMeshDescriptor submesh = mesh.GetSubMesh(submeshIndex);
                bool submeshHasTriangles = submesh.indexCount % 3 == 0;
                if (submesh.indexCount == 0 || !submeshHasTriangles)
                    continue;

                int submeshTriangleCount = submesh.indexCount / 3;
                totalTriangleCount += submeshTriangleCount;
            }

            using UnsafeIndirectList<MeshFillTriangle> triangles = new UnsafeIndirectList<MeshFillTriangle>(totalTriangleCount, AllocatorManager.TempJob);
            JobHandle gatherTrianglesJobHandle = default;

            using (Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                for (int i = 0; i < meshDataArray.Length; ++i)
                {
                    Mesh.MeshData meshData = meshDataArray[i];
                    if (meshData.vertexCount == 0)
                        continue;

                    JobHandle meshTriangleJobHandle = default;

                    NativeArray<float3> vertices = new NativeArray<float3>(meshData.vertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    meshData.GetVertices(vertices.Reinterpret<Vector3>());

                    NativeArray<Color> colors = new NativeArray<Color>(meshData.vertexCount, Allocator.TempJob);
                    if (meshHasVertexColors)
                        meshData.GetColors(colors);

                    for (int submeshIndex = 0; submeshIndex < meshData.subMeshCount; ++submeshIndex)
                    {
                        SubMeshDescriptor submesh = meshData.GetSubMesh(submeshIndex);
                        bool submeshHasTriangles = submesh.indexCount % 3 == 0;
                        if (submesh.indexCount == 0 || !submeshHasTriangles)
                            continue;

                        NativeArray<int> indices = new NativeArray<int>(submesh.indexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                        meshData.GetIndices(indices, submeshIndex);

                        int triangleCount = submesh.indexCount / 3;

                        JobHandle subMeshTriangleJobHandle = new MeshBuildTrianglesJob
                        {
                            LocalToWorld = meshLocalToWorld,
                            Vertices = vertices,
                            Indices = indices,
                            Colors = colors,
                            PotentialTriangles = triangles.AsParallelWriter()
                        }.ScheduleBatch(triangleCount, MeshBuildTrianglesJob.BatchSize);
                        subMeshTriangleJobHandle = indices.Dispose(subMeshTriangleJobHandle);
                        meshTriangleJobHandle = JobHandle.CombineDependencies(meshTriangleJobHandle, subMeshTriangleJobHandle);
                    }

                    meshTriangleJobHandle = vertices.Dispose(meshTriangleJobHandle);
                    meshTriangleJobHandle = colors.Dispose(meshTriangleJobHandle);
                    gatherTrianglesJobHandle = JobHandle.CombineDependencies(gatherTrianglesJobHandle, meshTriangleJobHandle);
                }
            }

            gatherTrianglesJobHandle.Complete();

            for (int index = 0; index < prototypes.Count; index++)
            {
                InstancedPrototype prototype = prototypes[index];
                FillMeshTriangles(prototype, collider, triangles, meshBoundsWS);
            }

            InstancePlacementUtility.EndPlacementOperation();
        }

        void FillMeshTriangles(InstancedPrototype prototype, MeshCollider collider, UnsafeIndirectList<MeshFillTriangle> triangles, AxisAlignedBox meshBoundsWS)
        {
            Random random = new Random((uint)DateTime.Now.Ticks);
            int colliderInstanceID = collider.GetInstanceID();

            InstancePlacementSettings placementSettings = prototype.PlacementSettings;
            InstancePlacementHash<int> placementHash = new InstancePlacementHash<int>(1024, AllocatorManager.Temp);
            UnsafeIndirectList<float3> potentialPositions = new UnsafeIndirectList<float3>(1024, AllocatorManager.Temp);
            UnsafeIndirectList<LocalTransform> placedInstances = new UnsafeIndirectList<LocalTransform>(1024, AllocatorManager.Temp);

            RuntimeSpatialHash.Instance.GetOverlappingBounds(meshBoundsWS, m_NearbyContainers);

            float density = InstancePlacementUtility.ComputeDensity(placementSettings) * DensityStrength;
            AxisAlignedBox placedBounds = AxisAlignedBox.Empty;
            AxisAlignedBox prototypeBounds = prototype.Bounds;

            for (int triIndex = 0; triIndex < triangles.Length; triIndex++)
            {
                ref readonly MeshFillTriangle triangle = ref triangles[triIndex];

                int desiredCountPerTriangle = (int)math.ceil(triangle.Area * density / (10.0f * 10.0f));
                if (desiredCountPerTriangle <= 0)
                    continue;

                if (!InstancePlacementUtility.IsValidSlope(triangle.NormalWS, placementSettings.SlopeMask))
                    continue;

                for (int i = 0; i < desiredCountPerTriangle; ++i)
                {
                    triangle.GetRandomPoint(ref random, out float3 position, out _);

                    PlacementHit hit = new PlacementHit { Point = position, Normal = triangle.NormalWS, ColliderInstanceID = colliderInstanceID };
                    if (!InstancePlacementUtility.IsValidPlacementHit(placementSettings, hit, placementSettings.Radius, m_NearbyContainers, placementHash, potentialPositions))
                        continue;

                    if (!InstancePlacementUtility.TryPlaceTransform(prototype, default, hit, ref random, out LocalTransform instance))
                        continue;

                    placedInstances.Add(instance);
                    placedBounds += prototypeBounds.TransformBy(instance);
                }
            }

            if (placedInstances.Length > 0)
            {
                InstancePlacementUtility.PlaceInstances(prototype, collider.transform, placedInstances.AsSpan(), placedBounds, PlacementOccluders);
            }
        }

        [BurstCompile]
        unsafe struct MeshBuildTrianglesJob : IJobParallelForBatch
        {
            public const int BatchSize = 512;

            public float4x4 LocalToWorld;
            [ReadOnly] public NativeArray<float3> Vertices;
            [ReadOnly] public NativeArray<int> Indices;
            [ReadOnly] public NativeArray<Color> Colors;
            [WriteOnly] public UnsafeIndirectList<MeshFillTriangle>.ParallelWriter PotentialTriangles;

            public void Execute(int startIndex, int count)
            {
                NativeArray<MeshFillTriangle> triangles = new NativeArray<MeshFillTriangle>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                for (int triIndex = startIndex; triIndex < count; triIndex++)
                {
                    int index = triIndex * 3;
                    int index0 = Indices[index + 0];
                    int index1 = Indices[index + 1];
                    int index2 = Indices[index + 2];

                    MeshFillTriangle triangle = new MeshFillTriangle(LocalToWorld,
                        Vertices[index0], Vertices[index1], Vertices[index2],
                        Colors[index0], Colors[index1], Colors[index2]);

                    triangles[triIndex] = triangle;
                }

                PotentialTriangles.AddRangeNoResize(triangles.GetUnsafeReadOnlyPtr(), count);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MeshFillTriangle
        {
            public float3 Vertex;
            public float3 Edge1;
            public float3 Edge2;
            public float3 NormalWS;
            public float Area;
            public bool HasColors;

            public Color VertexColor0;
            public Color VertexColor1;
            public Color VertexColor2;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public MeshFillTriangle(in float4x4 localToWorld, float3 vertex0, float3 vertex1, float3 vertex2, Color color0, Color color1, Color color2)
            {
                Vertex = math.transform(localToWorld, vertex0);          // A
                Edge1  = math.transform(localToWorld, vertex1) - Vertex; // B - A
                Edge2  = math.transform(localToWorld, vertex2) - Vertex; // C - A

                VertexColor0 = color0;
                VertexColor1 = color1;
                VertexColor2 = color2;

                NormalWS = math.determinant(localToWorld) >= 0.0f
                    ? math.cross(Edge1, Edge2)
                    : math.cross(Edge2, Edge1);

                float normalLength = math.length(NormalWS);
                if (normalLength > 0.0001f)
                    NormalWS /= normalLength;

                Area = normalLength * 0.5f;
                HasColors = true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public MeshFillTriangle(in float4x4 localToWorld, float3 vertex0, float3 vertex1, float3 vertex2)
                : this(localToWorld, vertex0, vertex1, vertex2, Color.black, Color.black, Color.black)
            {
                HasColors = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void GetRandomPoint(ref Random random, out float3 outPoint, out Color outColor)
            {
                float x = random.NextFloat();
                float y = random.NextFloat();

                // Flip if we're outside the triangle
                if (x + y > 1.0f)
                {
                    x = 1.0f - x;
                    y = 1.0f - y;
                }

                outPoint = Vertex + x * Edge1 + y * Edge2;
                outColor = ((1.0f - x - y) * VertexColor0 + x * VertexColor1 + y * VertexColor2);
            }
        }
    }
}
