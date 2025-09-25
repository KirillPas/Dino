// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using MA.Collections;
using MA.Flora.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Plane = MA.Mathematics.Plane;

namespace MA.Flora.Editor
{
    static class InstancePickingUtility
    {
        static List<InstancedMeshContainer> s_RendererBuffer = new List<InstancedMeshContainer>();
        static List<InstanceSelectionGroup> s_SelectionBuffer = new List<InstanceSelectionGroup>();

        [InitializeOnLoadMethod]
        static void InitializeCustomPicking()
        {
            HandleUtility.pickGameObjectCustomPasses += OnPickGameObjectCustomPasses;
        }

        static GameObject OnPickGameObjectCustomPasses(Camera cam, int layers, Vector2 screenPixelCoordinate, GameObject[] ignore, GameObject[] filter, out int materialindex)
        {
            materialindex = -1;

            InstanceSelectionGroup instance = PickInstance(cam, screenPixelCoordinate);
            if (!instance || instance.IsEmpty)
                return null;

            InstancedMeshContainer container = instance.Container;
            InstancedObjectLink link = container.GetLinkedObject(instance.Indices[0]);
            GameObject gameObject = link ? link.gameObject : container.gameObject;
            return gameObject;
        }

        static readonly int2[] s_PixelOffsets = new int2[8]
        {
            new int2(-1, -1), new int2( 0, -1),
            new int2( 1, -1), new int2(-1,  0),
            new int2( 1,  0), new int2(-1,  1),
            new int2( 0,  1), new int2( 1,  1)
        };

        static float RenderingViewHeight => Camera.current == null ? Screen.height : Camera.current.pixelHeight;

        public static InstanceSelectionGroup PickInstance(float2 mousePosition)
        {
            Vector2 screenPosition = HandleUtility.GUIPointToScreenPixelCoordinate(mousePosition);
            return PickInstance(Camera.current, screenPosition);
        }

        static InstanceSelectionGroup PickInstance(Camera camera, float2 screenPixelCoordinate)
        {
            if (!InstancingSystem.IsActive())
                return null;

            if (!camera && SceneView.lastActiveSceneView)
                camera = SceneView.lastActiveSceneView.camera;

            if (!camera)
            {
                Debug.LogError("Failed to find a valid camera for picking.");
                return null;
            }

            if (!InstancingSystem.Instance.Context.CameraManager.TryGetInstancedCamera(camera, out InstancedCameraID cameraID))
                return null;

            int pixelWidth = camera.pixelWidth;
            int pixelHeight = camera.pixelHeight;

            CommandBuffer cmd = CommandBufferPool.Get();
            RenderTexture rt = RenderTexture.GetTemporary(pixelWidth, pixelHeight, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            RenderSelectionUtility.AddPickingPass(cmd, cameraID, rt, pixelWidth, pixelHeight);

            InstancedGlobalID pickedGlobalID = InstancedGlobalID.Null;
            cmd.RequestAsyncReadback(rt, 0, request =>
            {
                if (request.hasError)
                {
                    Debug.LogError("Failed to readback async GPU request.");
                    return;
                }

                int2 pixel = new int2(screenPixelCoordinate);
                NativeArray<int> pixels = request.GetData<int>();
                int pixelIndex = pixel.y * rt.width + pixel.x;
                if (!pixels.IsValidIndex(pixelIndex))
                    return;

                int hitId = pixels[pixelIndex];
                if (hitId == 0)
                {
                    for (int i = 0; i < s_PixelOffsets.Length; ++i)
                    {
                        int2 adjacent = pixel + s_PixelOffsets[i];
                        int adjacentIndex = adjacent.y * rt.width + adjacent.x;
                        if (!pixels.IsValidIndex(adjacentIndex))
                            continue;

                        hitId = pixels[adjacentIndex];
                        if (hitId > 0)
                            break;
                    }
                }
                if (hitId > 0)
                    pickedGlobalID = new InstancedGlobalID(hitId);
            });

            cmd.WaitAllAsyncReadbackRequests();

            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            return pickedGlobalID == InstancedGlobalID.Null
                ? null
                : InstanceSelectionGroup.Create(pickedGlobalID);
        }

        public static InstanceSelectionGroup[] PickRectInstances(SceneView view, float2 start, float2 end)
        {
            Rect rect = FromToRect(start, end);
            return PickRectInstances(view, rect);
        }

        public static InstanceSelectionGroup[] PickRectInstances(SceneView view, Rect rect)
        {
            if (rect.width <= 1 || rect.height <= 1)
                return Array.Empty<InstanceSelectionGroup>();

            using NativeArray<Plane> rectFrustum = CreateRectFrustum(view.camera, rect, Allocator.TempJob);
            Vector3 cameraPosition = view.camera.transform.position;
            float cameraRadius = view.camera.farClipPlane;

            s_RendererBuffer.Clear();
            RuntimeSpatialHash.Instance.GetOverlappingSphere(cameraPosition, cameraRadius, s_RendererBuffer);

            using NativeList<int> rectSelectionIndices = new NativeList<int>(128, Allocator.TempJob);

            s_SelectionBuffer.Clear();
            foreach (InstancedMeshContainer container in s_RendererBuffer)
            {
                if (!container || !container.isActiveAndEnabled || container.InstanceCount == 0)
                    continue;

                rectSelectionIndices.Clear();
                container.GetInstancesOverlappingFrustum_EditorOnly(rectFrustum, true, false, rectSelectionIndices);
                if (rectSelectionIndices.Length > 0)
                {
                    InstanceSelectionGroup selection = InstanceSelectionGroup.Create(container, rectSelectionIndices.AsArray().ToArray());
                    if (selection)
                        s_SelectionBuffer.Add(selection);
                }
            }

            return s_SelectionBuffer.ToArray();
        }

        public static Rect FromToRect(float2 a, float2 b)
        {
            float2 min = math.min(a, b);
            float2 max = math.max(a, b);
            return new Rect(min, max - min);
        }

        static UnityEngine.Plane[] s_Planes = new UnityEngine.Plane[6];

        public static NativeArray<Plane> CreateRectFrustum(Camera camera, Rect rect, Allocator allocator)
        {
            float3 position = camera.transform.position;
            float top = camera.pixelHeight - rect.yMin;
            float bottom = camera.pixelHeight - rect.yMax;

            float3 tl = camera.ScreenToWorldPoint(new Vector3(rect.xMin, bottom, camera.nearClipPlane));
            float3 tr = camera.ScreenToWorldPoint(new Vector3(rect.xMax, bottom, camera.nearClipPlane));
            float3 br = camera.ScreenToWorldPoint(new Vector3(rect.xMax, top, camera.nearClipPlane));
            float3 bl = camera.ScreenToWorldPoint(new Vector3(rect.xMin, top, camera.nearClipPlane));

            Plane l = new Plane(bl, tl, position);
            Plane r = new Plane(tr, br, position);
            Plane b = new Plane(br, bl, position);
            Plane t = new Plane(tl, tr, position);

            GeometryUtility.CalculateFrustumPlanes(camera, s_Planes);

            NativeArray<Plane> result = new NativeArray<Plane>(6, allocator);
            result[0] = l;
            result[1] = r;
            result[2] = b;
            result[3] = t;
            result[4] = s_Planes[4];
            result[5] = s_Planes[5];
            return result;
        }
    }
}
