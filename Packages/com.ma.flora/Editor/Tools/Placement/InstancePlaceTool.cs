// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using MA.Core.Editor;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Random = Unity.Mathematics.Random;
using Color = UnityEngine.Color;

namespace MA.Flora.Editor
{
    enum PlaceToolMode : byte
    {
        Single,
        Cycle,
    }

    [CustomEditor(typeof(InstancePlaceTool))]
    class InstancePlaceToolSettings : InstancePlacementToolSettings
    {
        protected override void AddToolbarElements(OverlayToolbar toolbar, Layout layout)
        {
            SliderDirection direction = layout == Layout.VerticalToolbar ? SliderDirection.Vertical : SliderDirection.Horizontal;
            toolbar.Add(new PlaceScaleSlider(direction));
        }
    }

    [FilePath("Library/com.ma.flora/Tools/InstancePlaceTool", FilePathAttribute.Location.ProjectFolder)]
    class InstancePlaceTool : InstancePlacementTool
    {
        [Shortcut("Flora/Instance Place Tool", typeof(InstanceToolShortcutContext), ShortcutKeys.Place)]
        public static void Shortcut()
        {
            if (InstanceToolContext.IsActive)
                ToolManager.SetActiveTool<InstancePlaceTool>();
        }

        struct InstancePlaceInfo
        {
            public bool IsCreated => ColliderID != 0;
            public bool IsValid;
            public int ColliderID;
            public Collider Collider => (Collider)Resources.InstanceIDToObject(ColliderID);
            public float VerticalOffset;
            public LocalTransform Transform;
            public float3 OriginalScale;
        }

        public PlaceToolMode Mode = PlaceToolMode.Single;
        public float Scale = 1.0f;

        Dictionary<InstancedPrototype, InstancePlaceInfo> m_InstancesToPlace = new Dictionary<InstancedPrototype, InstancePlaceInfo>();
        int m_ActivePrototypeHash;
        int m_PlacementCycleIndex = -1;

        bool m_HitValid;
        PlacementHit m_Hit;
        bool m_AllPlacementsValid;
        PlacementHit m_PlaceHit;

        // --- Tool ---

        protected override PlacementClutchShortcutMask GetAvailableClutchShortcuts() => PlacementClutchShortcutMask.Size;

        public override void OnActivated()
        {
            base.OnActivated();
            m_InstancesToPlace.Clear();
            Scale = 1.0f;
        }

        protected override void ToolGUI(SceneView view)
        {
            Event evt = Event.current;
            List<InstancedPrototype> prototypes = InstanceToolContextShared.ActivePrototypes;
            int currentHash = InstanceToolContextShared.CalculateActiveHash();
            if (currentHash != m_ActivePrototypeHash)
            {
                m_PlacementCycleIndex = 0;
                m_ActivePrototypeHash = currentHash;
                m_InstancesToPlace.Clear();
            }

            if (prototypes.Count == 0)
                return;

            bool updatePlacementInfo = !m_PlaceHit.IsValid || evt.isScrollWheel;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            switch (evt.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    if (!InstanceHandles.ViewToolActive)
                        HandleUtility.AddDefaultControl(controlId);
                    break;
                case EventType.MouseMove:
                    if (HandleUtility.nearestControl == controlId)
                        HandleUtility.Repaint();

                    updatePlacementInfo = true;
                    break;
                case EventType.MouseDown:
                    if (evt.type == EventType.MouseDown && evt.button == 0)
                    {
                        GUIUtility.hotControl = controlId;
                        updatePlacementInfo = true;

                        if (m_PlaceHit.IsValid)
                            PlaceInstances(prototypes);

                        evt.Use();
                    }
                    break;
                case EventType.MouseDrag:
                {
                    if (GUIUtility.hotControl == controlId)
                    {
                        updatePlacementInfo = true;

                        if (m_PlaceHit.IsValid)
                            PlaceInstances(prototypes);

                        evt.Use();
                    }
                    break;
                }
                case EventType.ScrollWheel:
                {
                    if (evt.modifiers == EventModifiers.None)
                    {
                        m_InstancesToPlace.Clear();
                        evt.Use();
                    }
                    break;
                }
                case EventType.KeyDown:
                {
                    if (evt.keyCode == KeyCode.Tab && Mode == PlaceToolMode.Cycle)
                    {
                        IncrementCycleIndex(prototypes.Count);
                        evt.Use();
                    }
                    break;
                }
                case EventType.Repaint:
                {
                    DrawPreview(view, prototypes);
                    break;
                }
            }

            if (ActiveClutchType == PlacementClutchShortcutType.Size)
            {
                Scale = InstanceGUIUtility.DrawClutchGUI(view, Scale, 0.01f, 5.0f, m_Hit.Point, L10n.Tr("Scale"));
                NotifyClutchUpdated(PlacementClutchShortcutType.Size, Scale);
            }

            if (updatePlacementInfo)
            {
                if (Mode == PlaceToolMode.Cycle)
                {
                    UpdatePlacementInfo(prototypes[m_PlacementCycleIndex]);
                }
                else
                {
                    foreach (InstancedPrototype prototype in prototypes)
                        UpdatePlacementInfo(prototype);
                }
            }
        }

        // --- Placement ---

        void IncrementCycleIndex(int max)
        {
            if (max == 0)
                m_PlacementCycleIndex = 0;
            else
                m_PlacementCycleIndex = (m_PlacementCycleIndex + 1) % max;
        }

        void PlaceInstances(List<InstancedPrototype> prototypes)
        {
            if (Mode == PlaceToolMode.Cycle)
            {
                PlaceInstance(prototypes[m_PlacementCycleIndex]);
                IncrementCycleIndex(prototypes.Count);
            }
            else
            {
                foreach (InstancedPrototype prototype in prototypes)
                    PlaceInstance(prototype);
            }
        }

        void PlaceInstance(InstancedPrototype prototype)
        {
            if (m_InstancesToPlace.TryGetValue(prototype, out InstancePlaceInfo instance) && instance.IsValid)
            {
                InstancePlacementUtility.PlaceInstance(prototype, instance.Collider.transform, instance.Transform);
                m_InstancesToPlace.Remove(prototype);
            }
        }

        void UpdatePlacementInfo(InstancedPrototype prototype)
        {
            float2 mousePosition = Event.current.mousePosition;
            bool isClutchActive = ActiveClutchType == PlacementClutchShortcutType.Size;

            InstancePlaceInfo instance = m_InstancesToPlace.GetValueOrDefault(prototype);
            bool randomize = !isClutchActive && (!instance.IsCreated || Event.current.isScrollWheel);
            if (randomize) instance = default;

            m_HitValid = false;
            m_PlaceHit.ColliderInstanceID = 0;
            float3 prevPlaceNormal = m_PlaceHit.Normal;

            if (InstanceHandlesUtility.PlaceObject(mousePosition, out var hit))
            {
                m_HitValid = true;
                if (isClutchActive)
                {
                    hit.point = m_Hit.Point;
                    hit.normal = m_Hit.Normal;
                    m_Hit = hit;
                }
                else
                {
                    m_Hit = hit;
                }

                PhysicsScene physicsScene = InstancePlacementUtility.GetActivePhysicsScene();
                InstancePlacementSettings placementSettings = prototype.PlacementSettings;
                float placementRadius = prototype.PlacementSettings.GetRadius(true);
                List<InstancedMeshContainer> containers = GetContainersOverlappingSphere(prototype, new Sphere(m_Hit.Point, placementRadius));

                if (InstancePlacementUtility.IsValidPlacementHit(placementSettings, hit, placementRadius, containers))
                {
                    m_PlaceHit = hit;

                    Random random = new Random((uint)DateTime.Now.Ticks);
                    if (randomize)
                    {
                        if (InstancePlacementUtility.TryPlaceTransform(prototype, physicsScene, hit, ref random, out LocalTransform transform, out float verticalOffset))
                        {
                            instance = new InstancePlaceInfo
                            {
                                IsValid = true,
                                ColliderID = hit.colliderInstanceID,
                                VerticalOffset = verticalOffset,
                                Transform = transform.ApplyScale(Scale),
                                OriginalScale = transform.Scale,
                            };
                        }
                    }
                    else
                    {
                        instance.IsValid = true;
                        instance.ColliderID = m_Hit.ColliderInstanceID;
                        instance.Transform.Position = m_Hit.Point;
                        instance.Transform.Scale = instance.OriginalScale * Scale;

                        if (placementSettings.AlignToSurface && !prevPlaceNormal.NearlyEquals(m_PlaceHit.Normal))
                        {
                            if (placementSettings.RandomizeYaw)
                                instance.Transform = InstancePlacementUtility.RandomizeYaw(instance.Transform, ref random);

                            if (placementSettings.AverageNormal)
                                InstancePlacementUtility.AverageHitNormal(ref m_Hit, physicsScene, instance.Transform.Position, placementSettings.CollisionLayerMask,
                                    placementSettings.AverageNormalSampleCount, placementSettings.AverageNormalSingleComponent, prototype.LowBoundingSphere.Radius);

                            if (placementSettings.AlignToSurface)
                                instance.Transform = InstancePlacementUtility.AlignToSurface(instance.Transform, m_Hit.Normal, placementSettings.AlignToSurfaceMaxAngle);
                        }

                        if (placementSettings.CheckWorldCollisions)
                        {
                            AxisAlignedBox collisionBounds = prototype.Bounds.TransformBy(instance.Transform);
                            collisionBounds.Size *= placementSettings.CollisionBoundsScale;

                            if (!InstancePlacementUtility.CheckWorld(instance.Transform, collisionBounds, physicsScene, placementSettings.CollisionLayerMask))
                            {
                                instance.IsValid = false;
                            }

                            if (instance.ColliderID != 0 && placementSettings.CheckColliderOverhang &&
                                !InstancePlacementUtility.CheckOverhang(
                                    instance.Transform, hit, physicsScene,
                                    placementSettings.CollisionLayerMask, placementSettings.AlignToSurface,
                                    instance.VerticalOffset, prototype.LowBoundingSphere))
                            {
                                instance.IsValid = false;
                            }
                        }
                    }
                }
                else
                {
                    instance.IsValid = false;
                }
            }
            else
            {
                instance.IsValid = false;
            }

            m_AllPlacementsValid = m_InstancesToPlace.Values.Any(i => i.IsValid);
            m_InstancesToPlace[prototype] = instance;
        }

        // --- Preview ---

        void DrawPreview(SceneView view, List<InstancedPrototype> prototypes)
        {
            if (!m_HitValid)
                return;

            Color color = m_PlaceHit.IsValid ? InstanceGUIUtility.ElementColor : InstanceGUIUtility.InvalidElementColor;
            using (new HandlesColorScope(color))
            {
                float radius = 0f;
                if (prototypes.Any())
                {
                    using (new Handles.DrawingScope(color))
                    {
                        if (Mode == PlaceToolMode.Cycle)
                            radius = prototypes[m_PlacementCycleIndex].PlacementSettings.GetRadius(true);
                        else
                            radius = prototypes.Max(p => p.PlacementSettings.GetRadius(true));
                    }
                }

                m_Hit.Normal.CalculatePerpendicularAxes(out float3 right, out float3 up);
                Handles.DrawSolidArc(m_Hit.Point, m_Hit.Normal, -right, 360f, 0.25f);

                if (radius > 0.25f)
                    HandlesUtility.DrawAAWireDisc(m_Hit.Point, m_Hit.Normal, radius, 3.0f);
            }

            for (int i = 0; i < prototypes.Count; i++)
            {
                InstancedPrototype prototype = prototypes[i];

                if (Mode == PlaceToolMode.Cycle && i != m_PlacementCycleIndex)
                    continue;
                if (!m_InstancesToPlace.TryGetValue(prototype, out InstancePlaceInfo instance))
                    continue;
                if (!instance.IsCreated)
                    continue;

                DrawPreviewMesh(view, prototype, instance.Transform, color);
            }
        }

        static readonly int k_PreviewTexture = Shader.PropertyToID("_PreviewTexture");
        static readonly int k_MaskTexture = Shader.PropertyToID("_MaskTexture");
        static readonly int k_Color = Shader.PropertyToID("_Color");

        void DrawPreviewMesh(SceneView view, InstancedPrototype prototype, LocalTransform transform, Color color)
        {
            int targetWidth = view.camera.pixelWidth;
            int targetHeight = view.camera.pixelHeight;
            transform.Position = m_Hit.Point;

            CommandBuffer cmd = CommandBufferPool.Get();

            cmd.GetTemporaryRT(k_PreviewTexture, targetWidth, targetHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            cmd.SetRenderTarget(k_PreviewTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, view.camera.targetTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetGlobalVector("_ScreenSize", new Vector4(targetWidth, targetHeight, 1.0f / targetWidth, 1.0f / targetHeight));

            MeshRenderer[] containers = prototype.GetLOD0MeshRenderers();
            foreach (MeshRenderer container in containers)
            {
                if (!container.TryGetComponent(out MeshFilter meshFilter))
                    continue;

                Material[] materials = container.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];

                    int forwardPass = -1;
                    for (int passIndex = 0; passIndex < material.passCount; ++passIndex)
                    {
                        if (material.GetPassName(passIndex).Contains("Forward"))
                        {
                            forwardPass = passIndex;
                            break;
                        }
                    }

                    if (forwardPass == -1)
                        continue;

                    material.SetPass(forwardPass);
                    cmd.DrawMesh(meshFilter.sharedMesh, transform.ToMatrix(), material, i, forwardPass);
                }
            }

            cmd.SetRenderTarget(view.camera.targetTexture);
            cmd.SetGlobalColor(k_Color, color);
            cmd.SetGlobalTexture(k_MaskTexture, k_PreviewTexture);
            cmd.Blit(k_PreviewTexture, view.camera.targetTexture, PreviewMaterial, (int)PreviewPasses.PlaceInstance);
            cmd.ReleaseTemporaryRT(k_PreviewTexture);

            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
