// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/PlacementOptionsView Icon.png")]
    [Overlay(typeof(SceneView), "placement-options", "Placement Options", "PlacementOptions"
#if UNITY_2022_3_OR_NEWER
        , defaultDockPosition = DockPosition.Top, defaultDockZone = DockZone.LeftColumn, defaultDockIndex = 20, defaultLayout = Layout.Panel
#endif
        )]
    class PlacementOptionsOverlay : CollapsableOverlayBase, ITransientOverlay
    {
        public bool visible => ToolManager.activeContextType == typeof(InstanceToolContext) && InstanceTool.Active is InstancePlacementTool;

        public override VisualElement CreatePanelContent()
        {
            VisualElement root = new VisualElement();
            root.Add(new PlacementOptionsView());
            return root;
        }
    }
}
