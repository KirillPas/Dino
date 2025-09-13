// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedPrototype Icon.png")]
    [Overlay(typeof(SceneView), "instanced-prototypes", "Instanced Prototypes", "InstancedPrototypes"
#if UNITY_2022_3_OR_NEWER
        , defaultDockPosition = DockPosition.Top, defaultDockZone = DockZone.LeftColumn, defaultDockIndex = 30, defaultLayout = Layout.Panel
#endif
        )]
    class InstancedPrototypeOverlay : CollapsableOverlayBase, ITransientOverlay
    {
        public bool visible => ToolManager.activeContextType == typeof(InstanceToolContext) && InstanceTool.Active is InstancePlacementTool;


        public override VisualElement CreatePanelContent()
        {
            VisualElement root = new VisualElement();
            root.Add(new InstancedPrototypeView(this));
            return root;
        }
    }
}
