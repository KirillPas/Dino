// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(Terrain))]
    class TerrainInspector : TerrainInspectorInternal
    {
        bool m_Disabled;
        
        public override void OnInspectorGUI()
        {
            if (InstanceToolContext.IsActive)
            {
                EditorGUILayout.HelpBox("Terrain editing is disabled while the Instance Context is active.", MessageType.Info);
                SetDisabled(true);
            }
            else
            {
                SetDisabled(false);
                base.OnInspectorGUI();
            }
        }
    }
}