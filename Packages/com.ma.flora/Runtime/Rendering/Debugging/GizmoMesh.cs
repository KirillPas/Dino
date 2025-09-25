// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MA.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    class GizmoMesh : IDisposable
    {
        public Matrix4x4 Matrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_Matrix;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => m_Matrix = value;
        }
        
        Matrix4x4 m_Matrix;
        
        readonly Mesh m_Mesh;
        readonly List<Vector3> m_Vertices;
        readonly List<int> m_Indices;
        readonly List<Color> m_Colors;
        readonly Material m_WireMaterial;
        readonly Material m_DottedWireMaterial;
        readonly Material m_SolidMaterial;
        
        const int k_VertexCountPerCube = 24;
        const float k_TransparentFactor = 0.1f;
        
        static readonly int k_HandleZTest = Shader.PropertyToID("_HandleZTest");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GizmoMesh(int capacity = 0)
        {
            m_Matrix = Matrix4x4.identity;
            m_Vertices = new List<Vector3>(capacity);
            m_Indices = new List<int>(capacity);
            m_Colors = new List<Color>(capacity);
            m_Mesh = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
#if UNITY_EDITOR
            m_WireMaterial = (Material)UnityEditor.EditorGUIUtility.LoadRequired("SceneView/HandleLines.mat");
            m_DottedWireMaterial = (Material)UnityEditor.EditorGUIUtility.LoadRequired("SceneView/HandleDottedLines.mat");
            m_SolidMaterial = UnityEditor.HandleUtility.handleMaterial;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            CoreUtils.Destroy(m_Mesh);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            m_Vertices.Clear();
            m_Indices.Clear();
            m_Colors.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RenderWireframe(Matrix4x4 trs, CompareFunction depthTest = CompareFunction.LessEqual, string gizmoName = null)
        {
            DrawMesh(trs, m_WireMaterial, MeshTopology.Lines, depthTest, m_Colors, gizmoName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DrawMesh(Matrix4x4 trs, Material mat, MeshTopology topology, CompareFunction depthTest, List<Color> colors, string gizmoName)
        {
            m_Mesh.Clear();
            m_Mesh.SetVertices(m_Vertices);
            m_Mesh.SetColors(colors);
            m_Mesh.SetIndices(m_Indices, topology, 0);

            mat.SetFloat(k_HandleZTest, (int)depthTest);

            CommandBuffer cmd = CommandBufferPool.Get(gizmoName ?? "Mesh Gizmo Rendering");
            cmd.DrawMesh(m_Mesh, trs, mat, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddVertices(Span<Vector3> vertices, in Color color, Span<int> indices)
        {
            foreach (Vector3 vertex in vertices)
            {
                m_Vertices.Add(m_Matrix.MultiplyPoint(vertex));
                m_Colors.Add(color);
            }
            
            foreach (int index in indices)
            {
                m_Indices.Add(index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWireCube(in Vector3 center, in Vector3 size, in Color color)
        {
            m_Vertices.Reserve(m_Vertices.Count + k_VertexCountPerCube);
            m_Indices.Reserve(m_Indices.Count + k_VertexCountPerCube);
            m_Colors.Reserve(m_Colors.Count + k_VertexCountPerCube);
            
            Vector3 halfSize = size / 2.0f;
            Vector3 p0 = new Vector3( halfSize.x,  halfSize.y,  halfSize.z);
            Vector3 p1 = new Vector3(-halfSize.x,  halfSize.y,  halfSize.z);
            Vector3 p2 = new Vector3(-halfSize.x, -halfSize.y,  halfSize.z);
            Vector3 p3 = new Vector3( halfSize.x, -halfSize.y,  halfSize.z);
            Vector3 p4 = new Vector3( halfSize.x,  halfSize.y, -halfSize.z);
            Vector3 p5 = new Vector3(-halfSize.x,  halfSize.y, -halfSize.z);
            Vector3 p6 = new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
            Vector3 p7 = new Vector3( halfSize.x, -halfSize.y, -halfSize.z);

            AddEdge(center + p0, center + p1, color);
            AddEdge(center + p1, center + p2, color);
            AddEdge(center + p2, center + p3, color);
            AddEdge(center + p3, center + p0, color);

            AddEdge(center + p4, center + p5, color);
            AddEdge(center + p5, center + p6, color);
            AddEdge(center + p6, center + p7, color);
            AddEdge(center + p7, center + p4, color);

            AddEdge(center + p0, center + p4, color);
            AddEdge(center + p1, center + p5, color);
            AddEdge(center + p2, center + p6, color);
            AddEdge(center + p3, center + p7, color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWireRect(float3 center, float2 size, Color color)
        {
            float2 halfSize = size / 2.0f;
            float3 p0 = new float3( halfSize.x, 0,  halfSize.y);
            float3 p1 = new float3(-halfSize.x, 0,  halfSize.y);
            float3 p2 = new float3(-halfSize.x, 0, -halfSize.y);
            float3 p3 = new float3( halfSize.x, 0, -halfSize.y);

            AddEdge(center + p0, center + p1, color);
            AddEdge(center + p1, center + p2, color);
            AddEdge(center + p2, center + p3, color);
            AddEdge(center + p3, center + p0, color);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddWireSphere(in Vector3 center, float radius, in Color color, int segments = 16)
        {
            m_Vertices.Reserve(m_Vertices.Count + segments * 6);
            m_Indices.Reserve(m_Indices.Count + segments * 6);
            m_Colors.Reserve(m_Colors.Count + segments * 6);

            float step = 360.0f / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle0 = i * step;
                float angle1 = (i + 1) * step;
                
                float sin0 = Mathf.Sin(angle0 * Mathf.Deg2Rad);
                float cos0 = Mathf.Cos(angle0 * Mathf.Deg2Rad);
                float sin1 = Mathf.Sin(angle1 * Mathf.Deg2Rad);
                float cos1 = Mathf.Cos(angle1 * Mathf.Deg2Rad);

                AddSphereSegment(center, radius, color, sin0, cos0, sin1, cos1);
            }
        }
        
        static readonly int[] s_FrustumIndices = new int[24]
        {
            0, 1, 1, 3, 3, 2, 2, 0,
            4, 5, 5, 7, 7, 6, 6, 4, 
            0, 4, 1, 5, 2, 6, 3, 7
        };
        static readonly Vector3[] s_TmpFrustumVertices = new Vector3[8];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddFrustum(in Matrix4x4 viewProjectionMatrix, in Color color)
        {
            Matrix4x4 invViewProj = viewProjectionMatrix.inverse;
            
            for (int i = 0; i < s_TmpFrustumVertices.Length; i++)
            {
                s_TmpFrustumVertices[i] = invViewProj.MultiplyPoint(
                    new Vector3(
                        (i & 1) == 0 ? -1 : 1,
                        (i & 2) == 0 ? -1 : 1,
                        (i & 4) == 0 ? -1 : 1));
            }
            
            for (int i = 0; i < s_FrustumIndices.Length; i += 2)
                AddEdge(s_TmpFrustumVertices[s_FrustumIndices[i]], s_TmpFrustumVertices[s_FrustumIndices[i + 1]], color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEdge(in Vector3 a, in Vector3 b, in Color color)
        {
            AddPoint(a, color);
            AddPoint(b, color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddPoint(in Vector3 point, in Color color)
        {
            m_Vertices.Add(m_Matrix.MultiplyPoint(point));
            m_Indices.Add(m_Indices.Count);
            m_Colors.Add(color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddSphereSegment(Vector3 center, float radius, Color color, float sin0, float cos0, float sin1, float cos1)
        {
            Vector3 p0 = center + new Vector3(cos0 * radius, sin0 * radius, 0);
            Vector3 p1 = center + new Vector3(cos1 * radius, sin1 * radius, 0);
            AddEdge(p0, p1, color);

            Vector3 p2 = center + new Vector3(0, cos0 * radius, sin0 * radius);
            Vector3 p3 = center + new Vector3(0, cos1 * radius, sin1 * radius);
            AddEdge(p2, p3, color);

            Vector3 p4 = center + new Vector3(cos0 * radius, 0, sin0 * radius);
            Vector3 p5 = center + new Vector3(cos1 * radius, 0, sin1 * radius);
            AddEdge(p4, p5, color);
        }
    }
}
