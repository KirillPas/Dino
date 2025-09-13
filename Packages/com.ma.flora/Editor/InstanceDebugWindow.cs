// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering.Editor
{
    static class InstanceDebugWindow
    {
        public static class Styles
        {
            public static GUIContent WindowTitle = EditorGUIUtility.TrTextContent("Rendering Debugger");
        }

        [MenuItem("Flora/Instance Debugger", priority = 10)]
        public static void Init()
        {
#if HAS_NEW_DEBUG_MANAGER
            DebugManager.instance.displayEditorUI = true;
#else
            DebugManager.instance.ToggleEditorUI(true);
#endif
            
            Type debugWindowType = typeof(DebugState).Assembly.GetType("UnityEditor.Rendering.DebugWindow");
            EditorWindow window = EditorWindow.GetWindow(debugWindowType);
            window.titleContent = Styles.WindowTitle;

            if (window)
            {
                int index = PanelIndex(DebugDisplayData.PanelName);
                if (index != -1)
                    DebugManager.instance.RequestEditorWindowPanelIndex(index);
            }
        }

        [MenuItem("Flora/Instance Debugger", priority = 10, validate = true)]
        public static bool ValidateMenuItem()
        {
            if (!InstancingSystem.IsActive())
                return false;
            
            DebugUI.Panel floraPanel = DebugManager.instance.GetPanel(DebugDisplayData.PanelName, true);
            return floraPanel != null;
        }
        
        static int PanelIndex([DisallowNull] string displayName)
        {
            displayName ??= string.Empty;

            var panels = DebugManager.instance.panels;
            for (int i = 0; i < panels.Count; ++i)
            {
                if (displayName.Equals(panels[i].displayName, StringComparison.InvariantCultureIgnoreCase))
                    return i;
            }

            return -1;
        }
    }
}
