// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/Instance Icon.png")]
    [Overlay(typeof(SceneView), "instance-inspector", "Instance Inspector", "InstanceInspector"
#if UNITY_2022_3_OR_NEWER
        , defaultDockPosition = DockPosition.Top, defaultDockZone = DockZone.LeftColumn, defaultDockIndex = 15, defaultLayout = Layout.Panel
#endif
        )]
    class InstanceInspectorOverlay : CollapsableOverlayBase, ITransientOverlay
    {
        static event Action ForceUpdateRequested;
        static bool s_FirstUpdateSinceDomainReload = true;
        public static void ForceUpdate() => ForceUpdateRequested?.Invoke();

        InstanceInspectorView m_InstanceInspectorView;

        public bool visible => ToolManager.activeContextType == typeof(InstanceToolContext) && InstanceTool.Active is InstanceManipulationTool;

        public static void UpdateInspectors()
        {
            if (s_FirstUpdateSinceDomainReload)
            {
                s_FirstUpdateSinceDomainReload = false;
                ForceUpdate();
            }
        }

        public override VisualElement CreatePanelContent()
        {
            VisualElement root = new VisualElement();
            m_InstanceInspectorView?.Dispose();
            root.Add(m_InstanceInspectorView = new InstanceInspectorView());
            UpdateInspector();
            return root;
        }

        public override void OnCreated()
        {
            displayedChanged += OnDisplayedChange;
            Selection.selectionChanged += UpdateInspector;
            ForceUpdateRequested += UpdateInspector;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        public override void OnWillBeDestroyed()
        {
            m_InstanceInspectorView?.Dispose();
            displayedChanged -= OnDisplayedChange;
            Selection.selectionChanged -= UpdateInspector;
            ForceUpdateRequested -= UpdateInspector;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        void OnDisplayedChange(bool displayed)
        {
            UpdateInspector();
        }

        void UpdateInspector()
        {
            m_InstanceInspectorView?.UpdateWithSelection();
        }

        void OnUndoRedoPerformed()
        {
            ForceUpdate();
        }
    }
}
