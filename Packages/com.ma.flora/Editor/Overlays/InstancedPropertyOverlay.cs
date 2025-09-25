// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/PropertyView Icon.png")]
    [Overlay(typeof(SceneView), "instanced-properties", "Instanced Properties", "InstancedProperties"
#if UNITY_2022_3_OR_NEWER
        , defaultDockPosition = DockPosition.Top, defaultDockZone = DockZone.LeftColumn, defaultDockIndex = 25, defaultLayout = Layout.Panel
#endif
        )]
    class InstancedPropertyOverlay : CollapsableOverlayBase, ITransientOverlay
    {
        public bool visible => ToolManager.activeContextType == typeof(InstanceToolContext) && InstanceTool.Active is InstancePropertyBrushTool;

        public override VisualElement CreatePanelContent()
        {
            VisualElement root = new VisualElement();
            root.Add(new InstancedPropertyView());
            return root;
        }
    }
}
