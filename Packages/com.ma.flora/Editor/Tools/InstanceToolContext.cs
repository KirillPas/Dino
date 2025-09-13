// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Linq;
using MA.Core.Editor;
using MA.Core.Editor.Bridge;
using MA.Flora.Rendering;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    class InstanceToolShortcutContext : ShortcutToolContextBridge
    {
        public override bool active => InstanceToolContext.IsActive &&
                                       InstanceTool.Active &&
                                       EditorWindow.focusedWindow == SceneView.lastActiveSceneView;
    }

    [CustomEditor(typeof(InstanceToolContext))]
    class InstanceToolContextSettings : CreateToolbarEditor
    {
        protected override void AddToolbarElements(OverlayToolbar toolbar, Layout layout)
        {
            toolbar.Add(new InstanceToolContextToolbar());
        }
    }

    [EditorToolContext("Instances")]
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstanceToolContext Icon.png")]
    [FilePath("Library/com.ma.flora/Tools/InstanceToolContext", FilePathAttribute.Location.ProjectFolder)]
    class InstanceToolContext : EditorToolContext
    {
        [Shortcut("Flora/Instance Tool Context", typeof(SceneView), KeyCode.Tab)]
        public static void Shortcut()
        {
            if (IsActive)
                ToolManager.SetActiveContext<GameObjectToolContext>();
            else
                ToolManager.SetActiveContext<InstanceToolContext>();
        }

        public static InstanceToolContext Active => IsActive ? (InstanceToolContext)EditorToolManagerBridge.ActiveToolContext : null;

        public static bool IsActive => EditorToolManagerBridge.ActiveToolContext is InstanceToolContext;

        [SerializeField] bool m_DisableDynamicColliders;
        [SerializeField] bool m_DisableDensityCulling = true;
        [SerializeField] bool m_DisableRenderDistance;

        public bool DisableDynamicColliders
        {
            get => m_DisableDynamicColliders;
            set
            {
                m_DisableDynamicColliders = value;
                if (InstanceTool.Active is InstancePlacementTool placementTool)
                    placementTool.BuildOccluders();
            }
        }

        public bool DisableDensityCulling
        {
            get => m_DisableDensityCulling;
            set
            {
                m_DisableDensityCulling = value;
                InstancingSystem.DisableDensityCulling = value;
            }
        }

        public bool DisableRenderDistance
        {
            get => m_DisableRenderDistance;
            set
            {
                m_DisableRenderDistance = value;
                InstancingSystem.DisableRenderDistance = value;
            }
        }

        InstanceToolShortcutContext m_ToolShortcutContext;
        InstanceSelector m_InstanceSelector = new InstanceSelector();

        protected override Type GetEditorToolType(Tool tool)
        {
            return tool switch
            {
                Tool.Move      => typeof(InstanceMoveTool),
                Tool.Rotate    => typeof(InstanceRotateTool),
                Tool.Scale     => typeof(InstanceScaleTool),
                Tool.Transform => typeof(InstanceTransformTool),
                _              => null
            };
        }

        public override void OnActivated()
        {
            m_ToolShortcutContext ??= new InstanceToolShortcutContext();
            ShortcutIntegrationBridge.RegisterToolContext(m_ToolShortcutContext);

            ToolManager.activeToolChanged += OnToolChanged;

            InstancingSystem.DisableDensityCulling = DisableDensityCulling;
            InstancingSystem.DisableRenderDistance = DisableRenderDistance;
            CullingData.CanEnableDensityScaling = false;

            m_InstanceSelector.Register();
        }

        public override void OnWillBeDeactivated()
        {
            m_InstanceSelector.Unregister();

            CullingData.CanEnableDensityScaling = true;
            InstancingSystem.DisableDensityCulling = false;
            InstancingSystem.DisableRenderDistance = false;

            ToolManager.activeToolChanged -= OnToolChanged;
            RemoveSelectionGroups();

            ShortcutIntegrationBridge.DeregisterToolContext(m_ToolShortcutContext);
            InstanceToolContextShared.Save();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            SceneView view = window as SceneView;
            if (!view) return;

            if (InstanceTool.Active is InstanceManipulationTool)
                m_InstanceSelector.OnGUI(view);
        }

        void OnToolChanged()
        {
            if (!ToolManager.activeToolType.IsSubclassOf(typeof(InstanceManipulationTool)))
                RemoveSelectionGroups();
        }

        static void RemoveSelectionGroups()
        {
            UnityObject[] selectedGroups = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
            UnityObject[] selectedObjects = Selection.objects;
            Selection.objects = selectedObjects.Except(selectedGroups).ToArray();
        }
    }
}
