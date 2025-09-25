// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using MA.Collections;
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    class DefaultGameObjectPreview : ObjectPreview
    {
        readonly Dictionary<int, PreviewData> m_PreviewInstances = new Dictionary<int, PreviewData>();
        readonly Dictionary<int, Texture> m_PreviewCache = new Dictionary<int, Texture>();
        Vector2 m_PreviewDir;
        Rect m_PreviewRect;
        bool m_HasRenderableParts;

        delegate void InitPreviewDelegate(PreviewRenderUtility pr, Rect r);
        static InitPreviewDelegate s_InitPreviewDelegate;

        public override void Initialize(UnityObject[] targets)
        {
            base.Initialize(targets);

            m_PreviewDir = new Vector2(-120f, 20f);
            m_PreviewRect = default;
            m_HasRenderableParts = false;

            if (target)
            {
                if (EditorSettings.defaultBehaviorMode == EditorBehaviorMode.Mode2D)
                    m_PreviewDir = new Vector2(0, 0);
                else
                {
                    m_PreviewDir = new Vector2(120, -20);

                    //Fix for FogBugz case : 1364821 Inspector Prototype Preview orientation is reversed when Bake Axis Conversion is enabled
                    UnityObject importedObject = PrefabUtility.IsPartOfVariantPrefab(target)
                        ? PrefabUtility.GetCorrespondingObjectFromSource(target) as GameObject
                        : target;

                    ModelImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(importedObject)) as ModelImporter;
                    if (importer && importer.bakeAxisConversion)
                    {
                        m_PreviewDir += new Vector2(180,0);
                    }
                }

                CalculateHasRenderableParts();
            }

            s_InitPreviewDelegate ??= (InitPreviewDelegate)Delegate.CreateDelegate(typeof(InitPreviewDelegate),
                typeof(PreviewRenderUtility).GetMethod("InitPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!);
        }

        public override void Cleanup()
        {
            base.Cleanup();
            Clear();
        }

        public void Clear()
        {
            foreach (PreviewData previewData in m_PreviewInstances.Values)
                previewData.Dispose();
            m_PreviewInstances.Clear();

            ClearPreviewCache();
        }

        public override UnityObject target
            => m_Targets.IsValidIndex(m_ReferenceTargetIndex) ? m_Targets[m_ReferenceTargetIndex] : null;

        public override GUIContent GetPreviewTitle()
            => m_Targets.Length > 0 ? EditorGUIUtility.TrTextContent($"{m_Targets.Length} Objects") : base.GetPreviewTitle();

        public override bool HasPreviewGUI()
        {
            bool isPersistent = target != null && EditorUtility.IsPersistent(target);
            bool hasPreview = HasStaticPreview();
            return isPersistent && hasPreview;
        }

        bool HasStaticPreview()
        {
            if (m_Targets.Length > 1) return true;
            if (target == null) return false;
            return m_HasRenderableParts;
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            PreviewData previewData = GetPreviewData();
            Vector2 direction = PreviewRenderUtilityBridge.Drag2D(m_PreviewDir, r);
            if (direction != m_PreviewDir)
            {
                // None of the preview are valid since the camera position has changed.
                ClearPreviewCache();
                m_PreviewDir = direction;
            }

            if (Event.current.type != EventType.Repaint)
                return;

            if (m_PreviewRect != r)
            {
                ClearPreviewCache();
                m_PreviewRect = r;
            }

            PreviewRenderUtility previewUtility = GetPreviewData().RenderUtility;
            if (m_PreviewCache.TryGetValue(m_ReferenceTargetIndex, out Texture previewTexture))
            {
                PreviewRenderUtilityBridge.DrawPreview(r, previewTexture);
            }
            else
            {
                previewUtility.BeginPreview(r, background);
                DoRenderPreview(r, previewData);
                previewUtility.EndAndDrawPreview(r);
                RenderTexture copy = new RenderTexture(previewUtility.GetRenderTexture());
                RenderTexture previous = RenderTexture.active;
                Graphics.Blit(previewUtility.GetRenderTexture(), copy);
                RenderTexture.active = previous;
                m_PreviewCache.Add(m_ReferenceTargetIndex, copy);
            }
        }

        public Texture2D RenderStaticPreview(int width, int height)
        {
            if (!HasStaticPreview() || !ShaderUtil.hardwareSupportsRectRenderTexture)
                return null;

            PreviewData previewData = GetPreviewData(true);
            Rect r = new Rect(0, 0, width, height);
            previewData.RenderUtility.BeginStaticPreview(r);
            DoRenderPreview(r, previewData);
            return previewData.RenderUtility.EndStaticPreview();
        }

        static Cubemap s_DefaultReflection;
        static Cubemap GetDefaultReflection()
        {
            if (s_DefaultReflection == null)
                s_DefaultReflection = EditorGUIUtility.Load("PrefabMode/DefaultReflectionForPrefabMode.exr") as Cubemap;
            return s_DefaultReflection;
        }

        void DoRenderPreview(Rect r, PreviewData previewData)
        {
            Bounds bounds = previewData.RenderableBounds;
            float halfSize = Mathf.Max(bounds.extents.magnitude, 0.0001f);
            float distance = halfSize * 3.8f;

            Quaternion rot = Quaternion.Euler(-m_PreviewDir.y, -m_PreviewDir.x, 0);
            Vector3 pos = bounds.center - rot * (Vector3.forward * distance);

            // Preview scene settings
            Camera camera = previewData.RenderUtility.camera;
            camera.clearFlags = CameraClearFlags.Depth;
            camera.backgroundColor = new Color(49.0f / 255.0f, 49.0f / 255.0f, 49.0f / 255.0f, 1.0f);
            camera.transform.position = pos;
            camera.transform.rotation = rot;
            camera.nearClipPlane = distance - halfSize * 1.1f;
            camera.farClipPlane = distance + halfSize * 1.1f;

            Light light0 = previewData.RenderUtility.lights[0];
            light0.color = new Color(0.769f, 0.769f, 0.769f, 1);
            light0.intensity = 1.1f;
            light0.transform.rotation = rot * Quaternion.Euler(40f, 40f, 0);

            Light light1 = previewData.RenderUtility.lights[1];
            light1.intensity = .7f;
            light1.transform.rotation = rot * Quaternion.Euler(340, 218, 177);
            previewData.RenderUtility.ambientColor = new Color(.4f, .4f, .45f, 0f) * .7f;

            previewData.RenderUtility.Render(true);
        }

        public override void OnPreviewSettings()
        {
            if (ShaderUtil.hardwareSupportsRectRenderTexture)
                GUI.enabled = true;
        }

        public override void ReloadPreviewInstances()
        {
            foreach ((int index, PreviewData previewData) in m_PreviewInstances)
            {
                if (index > m_Targets.Length)
                    continue;

                if (!previewData.UseStaticAssetPreview)
                {
                    InstancedPrototype prototype = (InstancedPrototype)m_Targets[index];
                    previewData.UpdateGameObject(prototype.gameObject);
                }
            }
            ClearPreviewCache();
        }

        class PreviewData : IDisposable
        {
            public readonly PreviewRenderUtility RenderUtility;
            public GameObject GameObject { get; private set; }
            public string PrefabAssetPath { get; private set; }
            public Bounds RenderableBounds { get; private set; }
            public bool UseStaticAssetPreview { get; set; }

            bool m_Disposed;

            public PreviewData(UnityObject targetObject, bool creatingStaticPreview = false)
            {
                RenderUtility = new PreviewRenderUtility
                {
                    camera =
                    {
                        fieldOfView = 30.0f
                    }
                };

                if (!UseStaticAssetPreview)
                    UpdateGameObject(targetObject);
            }

            public void UpdateGameObject(UnityObject targetObject)
            {
                UnityObject.DestroyImmediate(GameObject);
                GameObject = EditorUtilityBridge.InstantiateForAnimatorPreview(targetObject);
                RenderUtility.AddManagedGameObject(GameObject);
                RenderableBounds = GetRenderableBounds(GameObject);
            }

            public void Dispose()
            {
                if (!m_Disposed)
                {
                    RenderUtility.Cleanup();
                    UnityObject.DestroyImmediate(GameObject);
                    GameObject = null;
                    m_Disposed = true;
                }
            }
        }

        void ClearPreviewCache()
        {
            foreach (Texture tex in m_PreviewCache.Values)
                UnityObject.DestroyImmediate(tex);

            m_PreviewCache.Clear();
        }

        internal void ReloadPreviewInstance(string prefabAssetPath)
        {
            foreach ((int key, PreviewData previewData) in m_PreviewInstances)
            {
                if (key <= m_Targets.Length)
                {
                    if (previewData.PrefabAssetPath == prefabAssetPath)
                    {
                        InstancedPrototype prototype = (InstancedPrototype)m_Targets[key];
                        previewData.UpdateGameObject(prototype.gameObject);
                        ClearPreviewCache();
                        break;
                    }
                }
            }
        }

        PreviewData GetPreviewData(bool creatingStaticPreview = false)
        {
            if (!m_PreviewInstances.TryGetValue(m_ReferenceTargetIndex, out PreviewData previewData))
            {
                previewData = new PreviewData(target, creatingStaticPreview);
                m_PreviewInstances.Add(m_ReferenceTargetIndex, previewData);
            }

            if (!previewData.GameObject && !previewData.UseStaticAssetPreview)
                ReloadPreviewInstances();

            return previewData;
        }

        static readonly List<Renderer> s_RendererComponentsList = new List<Renderer>();

        static bool IsRendererUsableForPreview(Renderer r)
        {
            switch (r)
            {
                case MeshRenderer mr:
                    mr.gameObject.TryGetComponent(out MeshFilter mf);
                    if (mf == null || mf.sharedMesh == null)
                        return false;
                    break;
                case BillboardRenderer billboard:
                    if (billboard.billboard == null || billboard.sharedMaterial == null)
                        return false;
                    break;
            }
            return true;
        }

        void CalculateHasRenderableParts()
        {
            m_HasRenderableParts = HasRenderableParts(target as GameObject);
        }

        static bool HasRenderableParts(GameObject go)
        {
            if (go)
            {
                go.GetComponentsInChildren(s_RendererComponentsList);
                return s_RendererComponentsList.Any(IsRendererUsableForPreview);
            }
            return false;
        }

        static Bounds GetRenderableBounds(GameObject go)
        {
            Bounds b = new Bounds();
            if (go)
            {
                go.GetComponentsInChildren(s_RendererComponentsList);
                foreach (Renderer r in s_RendererComponentsList)
                {
                    if (!IsRendererUsableForPreview(r))
                        continue;
                    if (b.extents == Vector3.zero)
                        b = r.bounds;
                    else
                        b.Encapsulate(r.bounds);
                }
            }
            return b;
        }

        static float GetRenderableCenterRecurse(ref Vector3 center, GameObject go, int depth, int minDepth, int maxDepth)
        {
            if (depth > maxDepth)
                return 0;

            float ret = 0;

            if (depth > minDepth)
            {
                // Do we have a mesh?
                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                MeshFilter filter = go.GetComponent<MeshFilter>();
                BillboardRenderer billboard = go.GetComponent<BillboardRenderer>();

                if (renderer == null && filter == null && billboard == null)
                {
                    ret = 1;
                    center += go.transform.position;
                }
                else if (renderer != null && filter != null)
                {
                    // case 542145, epsilon is too small. Accept up to 1 centimeter before discarding this prototype.
                    if (Vector3.Distance(renderer.bounds.center, go.transform.position) < 0.01F)
                    {
                        ret = 1;
                        center += go.transform.position;
                    }
                }
                else if (billboard != null)
                {
                    if (Vector3.Distance(billboard.bounds.center, go.transform.position) < 0.01F)
                    {
                        ret = 1;
                        center += go.transform.position;
                    }
                }
            }

            depth++;

            // Recurse into children
            foreach (Transform t in go.transform)
            {
                ret += GetRenderableCenterRecurse(ref center, t.gameObject, depth, minDepth, maxDepth);
            }

            return ret;
        }

        static Vector3 GetRenderableCenterRecurse(GameObject go, int minDepth, int maxDepth)
        {
            Vector3 center = Vector3.zero;
            float sum = GetRenderableCenterRecurse(ref center, go, 0, minDepth, maxDepth);
            switch (sum)
            {
                case > 0:
                    center /= sum;
                    break;
                default:
                    center = go.transform.position;
                    break;
            }
            return center;
        }
    }
}
