// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering.Editor
{
    [DebugUIDrawer(typeof(DebugUIExt.ValueTuple))]
    sealed class DebugUIDrawerValueTuple : DebugUIDrawer
    {
        public const int FoldoutColumnWidth = 70;

        public override bool OnGUI(DebugUI.Widget widget, DebugState state)
        {
            DebugUIExt.ValueTuple field = Cast<DebugUIExt.ValueTuple>(widget);
            GUIContent label = EditorGUIUtility.TrTextContent(widget.displayName, widget.tooltip);

            Rect labelRect = PrepareControlRect();
            EditorGUI.PrefixLabel(labelRect, label);

            // Following layout should match DebugUIDrawerFoldout to make column labels align
            Rect drawRect = GUILayoutUtility.GetLastRect();

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0; //be at left of rects
            for (int i = 0; i < field.NumElements; i++)
            {
                Rect columnRect = drawRect;
                columnRect.x += EditorGUIUtility.labelWidth + i * FoldoutColumnWidth;
                columnRect.width = FoldoutColumnWidth;
                object value = field.Values[i].GetValue();
                EditorGUI.LabelField(columnRect, field.Format(value));
            }
            EditorGUI.indentLevel = indent;

            return true;
        }
    }
}
