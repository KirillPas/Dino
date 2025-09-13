// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Core;
using MA.Core.Editor;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Random = Unity.Mathematics.Random;

namespace MA.Flora.Editor
{
    enum BrushToolMode : byte { Sphere, Circle }

    [Serializable]
    struct BrushProperties
    {
        public static BrushProperties Default => new BrushProperties
        {
            Mode = BrushToolMode.Sphere,
            Point = float3.zero,
            Normal = math.up(),
            HitColliderID = 0,
            Radius = 1.0f,
            Strength = 1.0f,
            Pressure = 1.0f,
            Falloff = 0.5f
        };

        public BrushToolMode Mode;
        public float3 Point;
        public float3 Normal;
        public int HitColliderID;
        public float Radius;
        public float Strength;
        public float Pressure;
        public float Falloff;

        public bool IsValid => HitColliderID != 0 && Radius > 0.0f;
        public float Power => Pressure * Strength;

        public float CalculateArea() => Sphere.CalculateArea(Radius);
        public Sphere GetSphere() => new Sphere(Point, Radius);

        public void UpdatePlacement(in RaycastHit hit)
        {
            Point = hit.point;
            Normal = hit.normal;
            HitColliderID = hit.colliderInstanceID;
            Radius = math.max(Radius, 0.01f);
        }

        public void InvalidatePlacement()
        {
            Point = float3.zero;
            Normal = math.up();
            HitColliderID = 0;
        }
    }

    [CustomEditor(typeof(InstanceBrushTool))]
    abstract class InstanceBrushToolSettings : InstancePlacementToolSettings
    {
    }

    abstract class InstanceBrushTool : InstancePlacementTool
    {
        public BrushProperties Brush = BrushProperties.Default;

        Mesh m_SphereMesh;
        Material m_SphereMaterial;
        MaterialPropertyBlock m_SphereProperties;

        public override IEnumerable<VisualElement> OverlayOptionalElements
        {
            get
            {
                yield return new BrushToolModeField();
            }
        }

        public override void OnActivated()
        {
            base.OnActivated();

            m_SphereMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Packages/com.ma.flora/Editor/EditorResources/Mesh/BrushSphere.fbx");
            m_SphereMaterial = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.ma.flora/Editor/EditorResources/Material/Brush.mat");

            m_SphereProperties = new MaterialPropertyBlock();
            SetBrushMaterialProperties(false);

            NotifyClutchUpdated(PlacementClutchShortcutType.Size, Brush.Radius);
            NotifyClutchUpdated(PlacementClutchShortcutType.Strength, Brush.Strength);
            NotifyClutchUpdated(PlacementClutchShortcutType.Adjustment, Brush.Falloff);

            InstanceToolContextShared.ActivePrototypesChanged += OnActivePrototypesChanged;
        }

        public override void OnWillBeDeactivated()
        {
            base.OnWillBeDeactivated();
            InstanceToolContextShared.ActivePrototypesChanged -= OnActivePrototypesChanged;
        }

        void OnActivePrototypesChanged()
        {
            var activePrototypes = InstanceToolContextShared.ActivePrototypes;
            if (activePrototypes.Count == 0)
                return;

            float minRadius = float.MaxValue;
            foreach (InstancedPrototype prototype in activePrototypes)
                minRadius = math.min(prototype.PlacementSettings.GetRadius(false), minRadius);

            if (Brush.Radius < minRadius)
                Brush.Radius = minRadius;
        }

        protected abstract string GetBrushGroupName();

        protected virtual bool IsBrushAvailable() => InstanceToolContextShared.ActivePrototypes.Count > 0;

        protected virtual void OnBrushBegin()
        {
            InstancePlacementUtility.BeginPlacementOperation(GetBrushGroupName());
            OnBrushPaint();
        }

        protected virtual void OnBrushEnd()
        {
            OnBrushPaint();
            InstancePlacementUtility.EndPlacementOperation();
        }

        protected virtual void OnBrushMove() { }

        protected abstract void OnBrushPaint();

        protected virtual string GetBrushLabelForClutch(PlacementClutchShortcutType clutchType)
        {
            switch (clutchType)
            {
                case PlacementClutchShortcutType.Size:
                    return L10n.Tr("Radius");
                case PlacementClutchShortcutType.Strength:
                    return L10n.Tr("Strength");
                case PlacementClutchShortcutType.Adjustment:
                    return L10n.Tr("Falloff");
                default: return string.Empty;
            }
        }

        protected override void ToolGUI(SceneView view)
        {
            if (!IsBrushAvailable())
                return;

            Event evt = Event.current;
            if (evt.pressure > 0)
                Brush.Pressure = evt.pressure;

            PhysicsScene physicsScene = InstancePlacementUtility.GetActivePhysicsScene();
            string label = GetBrushLabelForClutch(ActiveClutchType);

            switch (ActiveClutchType)
            {
                case PlacementClutchShortcutType.None:
                {
                    Ray mouseRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                    if (physicsScene.Raycast(mouseRay.origin, mouseRay.direction, out RaycastHit mouseHit, Mathf.Infinity, PlacementLayerMask, QueryTriggerInteraction.Ignore))
                    {
                        Brush.UpdatePlacement(mouseHit);
                    }
                    else
                    {
                        Brush.InvalidatePlacement();
                    }
                    break;
                }
                case PlacementClutchShortcutType.Size:
                {
                    Brush.Radius = InstanceGUIUtility.DrawClutchGUI(view, Brush.Radius, 0.1f, 80.0f, Brush.Point, label);
                    NotifyClutchUpdated(PlacementClutchShortcutType.Size, Brush.Radius);
                    break;
                }
                case PlacementClutchShortcutType.Strength:
                {
                    Brush.Strength = InstanceGUIUtility.DrawClutchGUI(view, Brush.Strength, 0.0f, 1.0f, Brush.Point, label);
                    NotifyClutchUpdated(PlacementClutchShortcutType.Strength, Brush.Strength);
                    break;
                }
                case PlacementClutchShortcutType.Adjustment:
                {
                    Brush.Falloff = InstanceGUIUtility.DrawClutchGUI(view, Brush.Falloff, 0.0f, 1.0f, Brush.Point, label);
                    NotifyClutchUpdated(PlacementClutchShortcutType.Adjustment, Brush.Falloff);
                    break;
                }
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (evt.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    if (!InstanceHandles.ViewToolActive)
                        HandleUtility.AddDefaultControl(controlId);
                    break;
                case EventType.MouseMove:
                    OnBrushMove();
                    if (HandleUtility.nearestControl == controlId)
                    {
                        HandleUtility.Repaint();
                        // evt.Use();
                    }
                    break;
                case EventType.MouseDown:
                    if (evt.type == EventType.MouseDown && evt.button == 0)
                    {
                        GUIUtility.hotControl = controlId;
                        if (Brush.IsValid)
                            OnBrushBegin();
                        evt.Use();
                    }
                    break;
                case EventType.MouseDrag:
                {
                    if (GUIUtility.hotControl == controlId)
                    {
                        if (Brush.IsValid)
                            OnBrushPaint();
                        evt.Use();
                    }
                    break;
                }
                case EventType.MouseUp:
                {
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        OnBrushEnd();
                        evt.Use();
                    }
                    break;
                }
                case EventType.Repaint when Brush.IsValid:
                {
                    switch (Brush.Mode)
                    {
                        case BrushToolMode.Sphere:
                        {
                            SetBrushMaterialProperties(GUIUtility.hotControl == controlId);
                            Matrix4x4 matrix = Matrix4x4.TRS(Brush.Point, Quaternion.identity, Vector3.one * Brush.Radius * 2);
                            Graphics.DrawMesh(m_SphereMesh, matrix, m_SphereMaterial, 0, view.camera, 0, m_SphereProperties, false, false);
                            break;
                        }
                        case BrushToolMode.Circle:
                        {
                            using (new HandlesColorScope(InstanceGUIUtility.ElementColor))
                            {
                                Brush.Normal.CalculatePerpendicularAxes(out float3 right, out float3 _);
                                HandlesUtility.DrawAAWireDisc(Brush.Point, Brush.Normal, Brush.Radius, 3.0f);

                                using (new HandlesColorScope(InstanceGUIUtility.ElementColor.WithAlpha(0.25f)))
                                    Handles.DrawSolidArc(Brush.Point, Brush.Normal, right, 360f, Brush.Radius);
                            }
                            break;
                        }
                    }
                    break;
                }
            }
        }

        static readonly int k_ControlID = nameof(InstanceBrushTool).GetHashCode();
        static readonly int k_ColorID = Shader.PropertyToID("_Color");
        static readonly int k_CircleFillAmountID = Shader.PropertyToID("_Circle_Fill_Amount");
        static readonly int k_SphereOpacityID = Shader.PropertyToID("_Sphere_Opacity");

        void SetBrushMaterialProperties(bool isActive)
        {
            if (m_SphereProperties == null)
                return;

            m_SphereProperties.SetColor(k_ColorID, InstanceGUIUtility.ElementColor * 1.25f);
            if (isActive)
            {
                m_SphereProperties.SetFloat(k_CircleFillAmountID, 0.25f);
                m_SphereProperties.SetFloat(k_SphereOpacityID, 1.0f);
            }
            else
            {
                m_SphereProperties.SetFloat(k_CircleFillAmountID, 0.1f);
                m_SphereProperties.SetFloat(k_SphereOpacityID, 0.6f);
            }
        }

        protected void RemoveInstancesInsideBrush(InstancedPrototype prototype, int desiredInstanceCount)
        {
            if (TryGetContainersOverlappingSphere(prototype, Brush.GetSphere(), out List<InstancedMeshContainer> containers))
            {
                Random random = new Random((uint)DateTime.Now.Ticks);

                foreach (InstancedMeshContainer container in containers)
                {
                    if (container.InstanceCount <= desiredInstanceCount)
                    {
                        container.ClearInstances();
                        continue;
                    }

                    NativeList<int> potentialInstancesToRemove = new NativeList<int>(desiredInstanceCount, Allocator.Temp);
                    container.GetInstancesInsideSphere(Brush.GetSphere(), Space.World, potentialInstancesToRemove);

                    int instancesToRemove = (int)math.round((potentialInstancesToRemove.Length - desiredInstanceCount) * Brush.Power);
                    if (instancesToRemove <= 0)
                        return;

                    int instancesToKeep = potentialInstancesToRemove.Length - instancesToRemove;
                    if (instancesToKeep > 0)
                    {
                        for (int i = 0; i < instancesToKeep; i++)
                            potentialInstancesToRemove.RemoveAtSwapBack(random.NextInt(0, int.MaxValue) % potentialInstancesToRemove.Length);
                    }

                    if (potentialInstancesToRemove.Length > 0)
                    {
                        InstancePlacementUtility.RecordForModify(container);
                        container.RemoveInstances(potentialInstancesToRemove.AsReadOnlySpan());
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static float ComputeSmoothFalloff(float distance, float radius, float falloff)
        {
            float normalizedDistance = math.saturate(distance / radius);

            if (normalizedDistance > 1.0f - falloff)
            {
                float t = (normalizedDistance - (1.0f - falloff)) / falloff;
                return Mathf.SmoothStep(1.0f, 0.0f, t);
            }

            return 1.0f;
        }
    }
}
