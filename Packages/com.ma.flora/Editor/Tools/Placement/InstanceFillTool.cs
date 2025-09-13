// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(InstanceFillTool))]
    class InstanceFillToolSettings : InstancePlacementToolSettings
    {
        protected override void AddToolbarElements(OverlayToolbar toolbar, Layout layout)
        {
            SliderDirection direction = layout == Layout.VerticalToolbar ? SliderDirection.Vertical : SliderDirection.Horizontal;
            toolbar.Add(new FillDensitySlider(direction));
        }
    }

    [FilePath("Library/com.ma.flora/Tools/InstanceFillTool", FilePathAttribute.Location.ProjectFolder)]
    partial class InstanceFillTool : InstancePlacementTool
    {
        [Shortcut("Flora/Instance Fill Tool", typeof(InstanceToolShortcutContext), ShortcutKeys.Fill)]
        public static void Shortcut()
        {
            if (InstanceToolContext.IsActive)
                ToolManager.SetActiveTool<InstanceFillTool>();
        }

        const int k_UndoInstanceCountMax = 100000;

        public float DensityStrength = 1.0f;

        bool m_IsAsyncTaskRunning;
        Vector3 m_MousePoint;

        Vector3 m_HitPoint;
        Collider m_HitCollider;
#pragma warning disable CS0414 // Field is assigned but its value is never used
        bool m_UndoEnabled;
#pragma warning restore CS0414 // Field is assigned but its value is never used

        public override void OnActivated()
        {
            base.OnActivated();
            m_IsAsyncTaskRunning = false;
            m_MousePoint = Vector3.zero;
            m_HitPoint = Vector3.zero;
            m_HitCollider = null;
            m_UndoEnabled = false;
        }

        protected override PlacementClutchShortcutMask GetAvailableClutchShortcuts() => PlacementClutchShortcutMask.Strength;

        protected override void ToolGUI(SceneView view)
        {
            List<InstancedPrototype> prototypes = InstanceToolContextShared.ActivePrototypes;
            if (prototypes.Count == 0)
                return;

            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event evt = Event.current;

            switch (evt.GetTypeForControl(id))
            {
                case EventType.Layout:
                {
                    if (!InstanceHandles.ViewToolActive && !m_IsAsyncTaskRunning)
                        HandleUtility.AddDefaultControl(id);

                    break;
                }
                case EventType.MouseDown:
                {
                    if (HandleUtility.nearestControl == id && evt.button == 0 && m_HitCollider)
                    {
                        GUIUtility.hotControl = id;
                        evt.Use();

                        switch (m_HitCollider)
                        {
                            case MeshCollider meshCollider:
                            {
                                ExecuteMeshFill(prototypes, meshCollider);
                                break;
                            }
                            case TerrainCollider terrainCollider:
                            {
                                if (terrainCollider.TryGetComponent(out Terrain terrain) && terrain && terrain.terrainData)
                                {
                                    ExecuteTerrainFill(prototypes, terrain, terrainCollider);
                                }
                                break;
                            }
                        }
                    }
                    break;
                }
                case EventType.MouseDrag:
                {
                    if (GUIUtility.hotControl == id)
                    {
                        evt.Use();
                    }
                    break;
                }
                case EventType.MouseUp:
                {
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;
                }
                case EventType.Repaint:
                {
                    if (!m_IsAsyncTaskRunning)
                        DrawPreview(prototypes);
                    break;
                }
            }

            if (evt.type == EventType.MouseMove || !m_HitCollider)
            {
                Ray mouseRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                if (Physics.Raycast(mouseRay, out RaycastHit hit, Mathf.Infinity, PlacementLayerMask, QueryTriggerInteraction.Ignore))
                {
                    m_HitPoint = hit.point;

                    switch (hit.collider)
                    {
                        case MeshCollider:
                        case TerrainCollider:
                        {
                            m_HitCollider = hit.collider;
                            break;
                        }
                        default:
                        {
                            m_HitCollider = null;
                            break;
                        }
                    }
                }
            }

            if (ActiveClutchType == PlacementClutchShortcutType.Strength)
            {
                DensityStrength = InstanceGUIUtility.DrawClutchGUI(view, DensityStrength, 0.0f, 1.0f, m_MousePoint, L10n.Tr("Density"));
                NotifyClutchUpdated(PlacementClutchShortcutType.Strength, DensityStrength);
            }
        }

        static readonly int _Heightmap = Shader.PropertyToID("_Heightmap");
        static readonly int _Normalmap = Shader.PropertyToID("_Normalmap");

        void DrawPreview(List<InstancedPrototype> prototypes)
        {
            if (ActiveClutchType == PlacementClutchShortcutType.None)
                m_MousePoint = m_HitPoint;

            if (!m_HitCollider)
            {
                m_HitCollider = null;
                return;
            }

            switch (m_HitCollider)
            {
                case MeshCollider meshCollider:
                {
                    if (meshCollider.TryGetComponent(out MeshRenderer meshRenderer) && meshCollider.TryGetComponent(out MeshFilter meshFilter))
                    {
                        MeshDrawPreview(meshRenderer, meshFilter, prototypes);
                    }
                    break;
                }
                case TerrainCollider terrainCollider:
                {
                    if (terrainCollider.TryGetComponent(out Terrain terrain))
                    {
                        TerrainDrawPreview(terrain, prototypes);
                    }
                    break;
                }
            }
        }

        static float4 GetPreviewMaskParams(List<InstancedPrototype> prototypes)
        {
            float minSlope = float.MaxValue;
            float maxSlope = float.MinValue;

            foreach (InstancedPrototype prototype in prototypes)
            {
                minSlope = Mathf.Min(minSlope, prototype.PlacementSettings.SlopeMask.Min);
                maxSlope = Mathf.Max(maxSlope, prototype.PlacementSettings.SlopeMask.Max);
            }

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            foreach (InstancedPrototype prototype in prototypes)
            {
                minHeight = Mathf.Min(minHeight, prototype.PlacementSettings.HeightMask.Min);
                maxHeight = Mathf.Max(maxHeight, prototype.PlacementSettings.HeightMask.Max);
            }

            return new float4(minSlope, maxSlope, minHeight, maxHeight);
        }
    }
}
