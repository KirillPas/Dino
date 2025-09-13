// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor;
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace MA.Flora.Editor
{
    abstract class InstanceToolSettings : CreateToolbarEditor
    {
    }

    abstract class InstanceTool : EditorTool
    {
        public static InstanceTool Active => EditorToolManagerBridge.ActiveTool as InstanceTool;

        public override bool IsAvailable() => ToolManager.activeContextType == typeof(InstanceToolContext);

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView view || !InstanceToolContext.IsActive || !Active)
                return;
            
            if (Tools.hidden || InstanceHandles.ViewToolActive)
                return;
                
            bool inSceneView = view.GetCameraViewport().Contains(Event.current.mousePosition);
            if (!inSceneView)
                return;
            
            ToolGUI(view);
        }

        protected abstract void ToolGUI(SceneView view);
    }
}
