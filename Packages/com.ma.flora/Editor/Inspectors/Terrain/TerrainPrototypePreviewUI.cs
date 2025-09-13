// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace MA.Flora.Editor
{
    class TerrainPrototypePreviewUI
    {
        public static HashSet<InstancedPrototype> Selected = new HashSet<InstancedPrototype>();
        public static Action SelectedChanged;

        public int DetailSelectedVersion;
        public int TreeSelectedVersion;
        public bool IsEmpty => Selected.Count == 0;
        public Texture2D Icon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedPrototype Icon.png");

        SerializedInstancePrototype m_SerializedInstancePrototype;

        public void OnGUI(TerrainDetailListUI details, TerrainTreeListUI trees, UnityEditor.Editor owner)
        {
            if (DetailSelectedVersion != details.SelectedVersion ||
                TreeSelectedVersion != trees.SelectedVersion)
            {
                bool detailsChanged = DetailSelectedVersion != details.SelectedVersion;
                DetailSelectedVersion = details.SelectedVersion;
                bool treesChanged = TreeSelectedVersion != trees.SelectedVersion;
                TreeSelectedVersion = trees.SelectedVersion;

                Selected.Clear();
                if (detailsChanged)
                    Selected.UnionWith(details.Selected);
                else if (treesChanged)
                    Selected.UnionWith(trees.Selected);

                SerializedObject serializedObject = new SerializedObject(Selected.ToArray());
                m_SerializedInstancePrototype = new SerializedInstancePrototype(serializedObject);
                SelectedChanged?.Invoke();
            }

            if (m_SerializedInstancePrototype != null && Selected.Count > 0)
            {
                CoreEditorUtils.DrawSplitter();

                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                DrawHeader();
                EditorGUI.indentLevel = indent;

                InstancePrototypeUI.SectionInstancingOnly.Draw(m_SerializedInstancePrototype, owner);
            }
        }

        void DrawHeader()
        {
            const float height = 32f;
            Rect backgroundRect = GUILayoutUtility.GetRect(1f, height);
            backgroundRect.x -= 21;
            backgroundRect.width += 26;

            Color backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(50 / 255f, 50 / 255f, 50 / 255f, 1.0f)
                : new Color(127 / 255f, 127 / 255f, 127 / 255f, 1.0f);

            EditorGUI.DrawRect(backgroundRect, backgroundColor);
            backgroundRect.x += 3;

            Rect iconRect = new Rect(backgroundRect.x + 10, backgroundRect.y + 8, 16, 16);
            GUI.DrawTexture(iconRect, Icon);

            Rect labelRect = new Rect(backgroundRect.x + iconRect.width + 15, backgroundRect.y, backgroundRect.width - iconRect.width - 25, height);
            string title = Selected.Count > 1 ? $"({Selected.Count}) Prototypes" : Selected.First().name;
            EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
        }
    }
}
