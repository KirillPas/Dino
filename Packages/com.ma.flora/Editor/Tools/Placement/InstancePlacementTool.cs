// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MA.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MA.Flora.Editor
{
    enum PlacementClutchShortcutType
    {
        None       = 0,
        Size       = 1,
        Strength   = 2,
        Adjustment = 3
    }

    [Flags]
    enum PlacementClutchShortcutMask : byte
    {
        None       = 0,
        Size       = 1 << PlacementClutchShortcutType.Size,
        Strength   = 1 << PlacementClutchShortcutType.Strength,
        Adjustment = 1 << PlacementClutchShortcutType.Adjustment,
    }

    [Flags]
    enum PlacementObjectMask : byte
    {
        None           = 0,
        Mesh           = 1 << 0,
        Terrain        = 1 << 1,
        LinkedObject   = 1 << 2,
        Default        = Mesh | Terrain,
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PlacementHit
    {
        public float3 Point;
        public float3 Normal;
        public uint FaceID;
        public float Distance;
        public float2 UV;
        public int ColliderInstanceID;
        public Collider Collider => (Collider)Resources.InstanceIDToObject(ColliderInstanceID);
        public bool IsValid => ColliderInstanceID != 0;

        public static implicit operator RaycastHit(PlacementHit hit) => UnsafeUtility.As<PlacementHit, RaycastHit>(ref hit);
        public static implicit operator PlacementHit(RaycastHit hit) => UnsafeUtility.As<RaycastHit, PlacementHit>(ref hit);
    }

    abstract class InstancePlacementToolSettings : InstanceToolSettings
    {
    }

    abstract partial class InstancePlacementTool : InstanceTool
    {
        protected static class ShortcutKeys
        {
            // Tool Shortcuts
            public const KeyCode Place      = KeyCode.F1;
            public const KeyCode Paint      = KeyCode.F2;
            public const KeyCode Erase      = KeyCode.F3;
            public const KeyCode Fill       = KeyCode.F4;
            public const KeyCode Scale      = KeyCode.F5;
            public const KeyCode Properties = KeyCode.F6;

            // Clutch Shortcuts
            public const KeyCode Strength   = KeyCode.A;
            public const KeyCode Size       = KeyCode.S;
            public const KeyCode Adjustment = KeyCode.D;
        }

        [ClutchShortcut("Flora/Tool Strength", typeof(InstanceToolShortcutContext), ShortcutKeys.Strength)]
        public static void StrengthClutchShortcut(ShortcutArguments arguments)
        {
            if (Active is InstancePlacementTool placementTool && placementTool.HasClutchShortcut(PlacementClutchShortcutType.Strength))
                placementTool.UpdateClutchStage(arguments.stage, PlacementClutchShortcutType.Strength);
        }

        [ClutchShortcut("Flora/Tool Size", typeof(InstanceToolShortcutContext), ShortcutKeys.Size)]
        public static void SizeClutchShortcut(ShortcutArguments arguments)
        {
            if (Active is InstancePlacementTool placementTool && placementTool.HasClutchShortcut(PlacementClutchShortcutType.Size))
                placementTool.UpdateClutchStage(arguments.stage, PlacementClutchShortcutType.Size);
        }

        [ClutchShortcut("Flora/Tool Adjustment", typeof(InstanceToolShortcutContext), ShortcutKeys.Adjustment)]
        public static void AdjustmentClutchShortcut(ShortcutArguments arguments)
        {
            if (Active is InstancePlacementTool placementTool && placementTool.HasClutchShortcut(PlacementClutchShortcutType.Adjustment))
                placementTool.UpdateClutchStage(arguments.stage, PlacementClutchShortcutType.Adjustment);
        }

        // --- Properties ---

        /// <summary>The layer mask used for placement.</summary>
        public LayerMask PlacementLayerMask = -1;

        /// <summary>The mask used to filter objects for placement.</summary>
        public PlacementObjectMask PlacementObjectMask
        {
            get => m_PlacementObjectMask;
            set
            {
                if (m_PlacementObjectMask != value)
                {
                    m_PlacementObjectMask = value;
                    BuildOccluders();
                }
            }
        }
        [SerializeField] PlacementObjectMask m_PlacementObjectMask = PlacementObjectMask.Default;

        // --- EditorTool Overrides ---

        public override bool IsAvailable() => base.IsAvailable() && InstanceToolContextShared.ActivePrototypes.Count > 0;

        public override void OnActivated()
        {
            base.OnActivated();

            Selection.objects = Array.Empty<Object>();

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            PrefabStage.prefabSaving += OnPrefabSaving;
            PrefabStage.prefabSaved += OnPrefabSaved;

            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved += OnSceneSaved;

            BuildOccluders();
        }

        public override void OnWillBeDeactivated()
        {
            ClearOccluders();

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            PrefabStage.prefabSaving -= OnPrefabSaving;
            PrefabStage.prefabSaved -= OnPrefabSaved;

            EditorSceneManager.sceneClosing -= OnSceneClosing;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaved -= OnSceneSaved;

            base.OnWillBeDeactivated();
        }

        // --- UI Elements ---

        public virtual IEnumerable<VisualElement> OverlayOptionalElements
        {
            get
            {
                yield break;
            }
        }

        // --- Clutch Management ---

        public static event Action<PlacementClutchShortcutType, float> ClutchValueUpdated;
        public static event Action<PlacementClutchShortcutType, ShortcutStage> ClutchStageChanged;

        protected PlacementClutchShortcutType ActiveClutchType { get; private set; }

        protected virtual PlacementClutchShortcutMask GetAvailableClutchShortcuts() => PlacementClutchShortcutMask.None;

        protected bool HasClutchShortcut(PlacementClutchShortcutType type) => (GetAvailableClutchShortcuts() & (PlacementClutchShortcutMask)(1 << (int)type)) != 0;

        protected void NotifyClutchUpdated(PlacementClutchShortcutType type, float value)
        {
            ClutchValueUpdated?.Invoke(type, value);
        }

        void UpdateClutchStage(ShortcutStage stage, PlacementClutchShortcutType type)
        {
            if (type != ActiveClutchType && ActiveClutchType != PlacementClutchShortcutType.None)
                UpdateClutchStage(ShortcutStage.End, ActiveClutchType);

            if (stage == ShortcutStage.Begin)
            {
                ActiveClutchType = type;
                OnClutchBegan();
                ClutchStageChanged?.Invoke(ActiveClutchType, ShortcutStage.Begin);
            }
            else if (stage == ShortcutStage.End)
            {
                ClutchStageChanged?.Invoke(ActiveClutchType, ShortcutStage.End);
                OnClutchWillEnd();
                ActiveClutchType = PlacementClutchShortcutType.None;
            }
        }

        protected virtual void OnClutchBegan() { }

        protected virtual void OnClutchWillEnd() { }

        // --- Occluder Management ---

        PlacementOccluders m_PlacementOccluders;
        protected PlacementOccluders PlacementOccluders => m_PlacementOccluders;

        internal void BuildOccluders()
        {
            m_PlacementOccluders?.Dispose();
            m_PlacementOccluders = null;

            if (InstanceToolContext.IsActive)
            {
                PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                Scene currentScene = prefabStage ? prefabStage.scene : SceneManager.GetActiveScene();
                m_PlacementOccluders = new PlacementOccluders(currentScene, InstanceToolContext.Active.DisableDynamicColliders, m_PlacementObjectMask);
            }
        }

        void ClearOccluders()
        {
            m_PlacementOccluders?.Dispose();
            m_PlacementOccluders = null;
        }

        // --- Play Mode Events ---

        void OnPlayModeStateChanged(PlayModeStateChange playModeState)
        {
            switch (playModeState)
            {
                case PlayModeStateChange.ExitingEditMode:
                    ClearOccluders();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    BuildOccluders();
                    break;
            }
        }

        // --- Prefab Stage Events ---

        void OnPrefabStageOpened(PrefabStage stage) => BuildOccluders();
        void OnPrefabStageClosing(PrefabStage stage) => ClearOccluders();

        void OnPrefabSaving(GameObject gameObject) => ClearOccluders();
        void OnPrefabSaved(GameObject gameObject) => BuildOccluders();

        // --- Scene Events ---

        void OnSceneClosing(Scene scene, bool removingScene) => ClearOccluders();
        void OnSceneSaving(Scene scene, string path) => ClearOccluders();
        void OnSceneSaved(Scene scene) => BuildOccluders();

        // --- Placement ---

        static Collider[] s_RendererParentColliders = new Collider[64];
        static List<InstancedMeshContainer> s_Renderers = new List<InstancedMeshContainer>();
        protected static List<InstancedMeshContainer> s_ContainerBuffer = new List<InstancedMeshContainer>();

        protected static bool TryGetContainersOverlappingSphere(InstancedPrototype prototype, Sphere sphere, out List<InstancedMeshContainer> renderers)
        {
            renderers = s_Renderers;
            s_Renderers.Clear();
            return RuntimeSpatialHash.Instance.GetOverlappingSphere(prototype, sphere.Center, sphere.Radius, s_Renderers) > 0;
        }

        protected static List<InstancedMeshContainer> GetContainersOverlappingSphere(InstancedPrototype prototype, Sphere sphere)
        {
            List<InstancedMeshContainer> renderers = new List<InstancedMeshContainer>();
            RuntimeSpatialHash.Instance.GetOverlappingSphere(prototype, sphere.Center, sphere.Radius, renderers);
            return renderers;
        }

        // --- Preview ---

        protected enum PreviewPasses : int
        {
            PlaceInstance = 0,
            FillMesh      = 1,
            FillTerrain   = 2
        }

        static Material s_PreviewMaterial;
        protected Material PreviewMaterial
        {
            get
            {
                if (!s_PreviewMaterial) s_PreviewMaterial = new Material(Shader.Find("Hidden/Flora/PlacementPreview"));
                return s_PreviewMaterial;
            }
        }
    }

#if !UNITY_2022_3_OR_NEWER
    public struct QueryParameters
    {
        public int layerMask;
        public bool hitMultipleFaces;
        public QueryTriggerInteraction hitTriggers;
        public bool hitBackfaces;
        public QueryParameters(
            int layerMask = -5,
            bool hitMultipleFaces = false,
            QueryTriggerInteraction hitTriggers = QueryTriggerInteraction.UseGlobal,
            bool hitBackfaces = false)
        {
            this.layerMask = layerMask;
            this.hitMultipleFaces = hitMultipleFaces;
            this.hitTriggers = hitTriggers;
            this.hitBackfaces = hitBackfaces;
        }

        public static QueryParameters Default => new QueryParameters();
    }
#endif

    partial class InstancePlacementTool
    {
        protected static RaycastCommand CreateRaycastCommand(PhysicsScene physicsScene, Vector3 origin, Vector3 direction, QueryParameters queryParameters, float distance)
        {
#if UNITY_2022_3_OR_NEWER
            return new RaycastCommand(physicsScene, origin, direction, queryParameters, distance);
#else
            return new RaycastCommand(physicsScene, origin, direction, distance, queryParameters.layerMask);
#endif
        }
    }
}
