// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Collections.Unsafe;
using MA.Core;
using MA.Mathematics;
#if !HAS_PACKAGE_UNITY_COLLECTIONS_2_0_0
using MA.Collections;
#endif
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Plane = MA.Mathematics.Plane;

namespace MA.Flora.Rendering
{
    [AddComponentMenu("")]
    [ExecuteAlways]
    class DebugDisplayGizmos : MonoBehaviour
    {
        static DebugDisplayGizmos s_Instance;
        GizmoMesh m_GizmoMesh;

        public static DebugDisplayGizmos GetOrCreate()
        {
            if (s_Instance != null)
                return s_Instance;

            s_Instance = UnityUtility.FindFirstObjectByType<DebugDisplayGizmos>();
            if (s_Instance != null)
                return s_Instance;

            GameObject go = new GameObject("[Flora: Debug Display Gizmos]")
            {
                hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable | HideFlags.HideInInspector
            };
            s_Instance = go.AddComponent<DebugDisplayGizmos>();
            return s_Instance;
        }

        void OnEnable()
        {
            if (s_Instance != null && s_Instance != this)
            {
                DestroyImmediate(this);
                return;
            }

            m_GizmoMesh = new GizmoMesh();
        }

        void OnDisable()
        {
            if (s_Instance == this)
                s_Instance = null;

            m_GizmoMesh?.Dispose();
            m_GizmoMesh = null;
        }

        void OnDrawGizmos()
        {
            if (!InstancingSystem.IsActive())
                return;

            InstancingContext context = InstancingSystem.Instance.Context;
            if (context == null)
                return;

            if (DebugDisplayData.IsActive())
            {
                // Try to get the relevant camera for the Gizmo
                Camera mainCamera;
#if UNITY_EDITOR
                if (UnityEditor.SceneView.currentDrawingSceneView &&
                    UnityEditor.SceneView.currentDrawingSceneView.camera)
                    mainCamera = UnityEditor.SceneView.currentDrawingSceneView.camera;
                else
#endif
                if (Camera.current)
                    mainCamera = Camera.current;
                else
                    mainCamera = Camera.main;

                if (!mainCamera)
                    return;

                m_GizmoMesh.Clear();

                DebugDisplayData debugDisplayData = DebugDisplayData.Instance;

                Span<Plane> frustumPlanesWorld = stackalloc Plane[6];
                FrustumUtility.InitializeFromViewProjection(frustumPlanesWorld, mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix);

                float3 currentCameraPosition = mainCamera.transform.position;

                if (debugDisplayData.ShowStaticOcclusionSpheres && context.HasStaticOcclusionManager())
                {
                    if (!context.CameraManager.TryGetLastRenderCameraID(mainCamera, out InstancedCameraID mainCameraID))
                    {
                        StaticOcclusionManager staticOcclusionManager = context.GetStaticOcclusionManager();
                        if (staticOcclusionManager.TryGetContext(mainCameraID, out StaticOcclusionContext staticOcclusionContext))
                        {
                            for (int i = 0; i < staticOcclusionManager.CullingSpheres.Length; i++)
                            {
                                BoundingSphere sphere = staticOcclusionManager.CullingSpheres[i];
                                if (sphere.radius <= 0.0f)
                                    continue;

                                if (FrustumUtility.IntersectSphere(frustumPlanesWorld, sphere) == FrustumIntersectResult.Outside)
                                    continue;

                                Color color = staticOcclusionContext.IsVisible(i) ? Color.cyan : Color.red;
                                m_GizmoMesh.AddWireSphere(sphere.position, sphere.radius, color.WithAlpha(0.5f));
                            }
                        }
                    }
                }

                InstancedRendererManager rendererManager = context.RendererManager;
                UnsafeIndirectList<InstancedRendererID> groups = rendererManager.Valid;
                ref readonly InstancedRendererArrays rendererArrays = ref rendererManager.Data;

                if (debugDisplayData.ShowStreamingDebug)
                {
                    const float kStreamingDebugAlpha = 0.65f;

                    foreach (InstancedRendererID id in groups)
                    {
                        bool isVisibleInScene = rendererArrays.IsVisibleInScene[id];
                        if (!isVisibleInScene)
                            continue;

                        AxisAlignedBox bounds = rendererArrays.WorldBounds[id];
                        if (bounds.IsEmpty)
                            continue;

                        bool isInRange = rendererArrays.InRange[id];
                        bool isLoaded = rendererArrays.IsLoaded[id];

                        Color color;
                        if (isInRange && isLoaded)
                            color = Color.white;
                        else if (isLoaded)
                            color = new Color(253.0f / 255.0f, 152.0f / 255.0f, 0.0f  / 255.0f, 1.0f);
                        else
                            color = new Color(132.0f / 255.0f, 10.0f  / 255.0f, 54.0f / 255.0f, 1.0f);

                        m_GizmoMesh.AddWireCube(bounds.Center, bounds.Size, color.WithAlpha(kStreamingDebugAlpha));
                    }

                    EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
                }

                if (debugDisplayData.TreeVisualizationMode != DebugTreeVisualizationMode.None)
                {
                    Color[] subdivisionDebugColors = debugDisplayData.TreeVisualizationMode == DebugTreeVisualizationMode.SubdivisionLevel
                        ? debugDisplayData.TreeSubdivisionColors
                        : debugDisplayData.TreeHeatmapColors;
                    int onlyDepth = debugDisplayData.TreeVisualizationOnlyDepth;

                    foreach (InstancedRendererID id in groups)
                    {
                        bool isVisibleInScene = rendererArrays.IsVisibleInScene[id];
                        if (!isVisibleInScene)
                            continue;

                        ref readonly InstanceRendererTreeData treeData = ref rendererArrays.Tree[id];
                        if (treeData.Count == 0)
                            continue;

                        NativeArray<CullingNode> tree = rendererArrays.NodeStore.GetSubArray(treeData.Offset, treeData.Count);
                        float maxDistance = debugDisplayData.TreeVisualizationMaxDistance;

                        GizmosTraverseBVH(tree.AsReadOnlySpan(), frustumPlanesWorld, 0, 0, onlyDepth, subdivisionDebugColors, currentCameraPosition, maxDistance);
                    }

                    m_GizmoMesh.Matrix = Matrix4x4.identity;
                }

                if (debugDisplayData.ShowRuntimeSpatialGrid)
                {
                    RuntimeSpatialHash.Instance.DrawGizmos(Color.yellow);
                }

                m_GizmoMesh.RenderWireframe(Matrix4x4.identity);
            }
        }

        void GizmosTraverseBVH(ReadOnlySpan<CullingNode> tree, ReadOnlySpan<Plane> frustumPlanes, int index, int depth, int onlyDepth, ReadOnlySpan<Color> colors, float3 camera, float maxDistance)
        {
            ref readonly CullingNode node = ref tree[index];

            if (!node.Bounds.OverlapsSphere(camera, maxDistance))
                return;

            if (FrustumUtility.IntersectBounds(frustumPlanes, node.Bounds) == FrustumIntersectResult.Outside)
                return;

            bool draw = !(onlyDepth >= 0 && depth != onlyDepth);
            if (draw)
            {
                DrawCell(node.Bounds, camera, colors, depth, maxDistance);
            }

            if (!node.IsLeaf)
            {
                for (int childIndex = node.FirstChild; childIndex <= node.LastChild; ++childIndex)
                {
                    GizmosTraverseBVH(tree, frustumPlanes, childIndex, depth + 1, onlyDepth, colors, camera, maxDistance);
                }
            }
        }

        void DrawCell(AxisAlignedBox bounds, float3 camera, ReadOnlySpan<Color> colors, int depth, float maxDistance)
        {
            const float maxAlpha = 0.8f;
            float distance = math.distance(bounds.Center, camera);
            float distanceAlpha = math.lerp(maxAlpha, 0f, math.saturate(distance / maxDistance));
            m_GizmoMesh.AddWireCube(bounds.Center, bounds.Size, colors[depth].WithAlpha(distanceAlpha));
        }

        static void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(start, end);
        }

        static void DrawPlane(Plane plane, Vector3 origin, float planeSize, Color color)
        {
            Quaternion rotation = Quaternion.LookRotation(plane.Normal);
            Vector3 planeOffset = plane.Normal * plane.Distance;
            Vector3 planeCenter = origin + planeOffset;

            Gizmos.color = color;
            Gizmos.matrix = Matrix4x4.TRS(planeCenter, rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, new Vector3(planeSize, planeSize, 0.001f));
        }
    }
}
