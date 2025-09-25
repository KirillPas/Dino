// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    static class InstanceGUIUtility
    {
        public static readonly Color ElementColor = new Color(255 / 255f, 165 / 255f, 20 / 255f, 1.0f);
        public static readonly Color ElementColorSelected = new Color(255 / 255f, 215 / 255f, 0 / 255f, 1.0f);
        public static readonly Color InvalidElementColor = new Color(255 / 255f, 71 / 255f, 71 / 255f, 1f);

        public static readonly float LineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public delegate bool EqualityComparer<in T>(T a, T b);
        
        public static bool HasMultipleValues<T>(IReadOnlyList<T> elements, EqualityComparer<T> comparer)
        {
            if (elements.Count < 2)
                return false;

            T first = elements[0];
            for (int i = 1; i < elements.Count; ++i)
                if (!comparer.Invoke(first, elements[i]))
                    return true;

            return false;
        }
        
        public static quaternion GetQuaternionValue(SerializedProperty property)
        {
            return new quaternion(
                property.FindPropertyRelative("value.x").floatValue,
                property.FindPropertyRelative("value.y").floatValue,
                property.FindPropertyRelative("value.z").floatValue,
                property.FindPropertyRelative("value.w").floatValue);
        }

        public static void SetQuaternionValue(SerializedProperty property, Quaternion value)
        {
            property.FindPropertyRelative("value.x").floatValue = value.x;
            property.FindPropertyRelative("value.y").floatValue = value.y;
            property.FindPropertyRelative("value.z").floatValue = value.z;
            property.FindPropertyRelative("value.w").floatValue = value.w;
        }

        public static Rect ReserveSpace(float height, ref Rect total)
        {
            Rect current = total;
            current.height = height;
            total.y += height;
            return current;
        }

        public static Rect ReserveSpaceForLine(ref Rect total)
        {
            float height = EditorGUIUtility.wideMode ? LineHeight : 2f * LineHeight;
            return ReserveSpace(height, ref total);
        }
        
        public static float DrawClutchGUI(SceneView view, float currentValue, float min, float max, float3 position, string name)
        {
            Event evt = Event.current;
            float delta = evt.delta.x;
            delta *= evt.shift ? 0.1f : 1.0f;
            
            currentValue += 0.002f * Mathf.Clamp(currentValue, min, max) * delta;
            currentValue = Mathf.Clamp(currentValue, min, max);

            float invPixelsPerPoint = 1.0f / EditorGUIUtility.pixelsPerPoint;
            Vector3 screenPoint = view.camera.WorldToScreenPoint(position) * invPixelsPerPoint;

            Handles.BeginGUI();
            GUI.matrix = Matrix4x4.identity;
            {
                GUIContent label = EditorGUIUtility.TrTempContent($"{name}: {currentValue:0.00}");
                Vector2 labelSize = Styles.SceneLabelStyle.CalcSize(new GUIContent(label));

                float x = screenPoint.x + 10.0f * invPixelsPerPoint;
                float y = Screen.height * invPixelsPerPoint - screenPoint.y - 60.0f * invPixelsPerPoint;
                float w = labelSize.x;
                float h = EditorGUIUtility.singleLineHeight;

                GUI.Label(new Rect(x, y, w, h), label, Styles.SceneLabelStyle);
            }
            Handles.EndGUI();

            view.Repaint();
            
            return currentValue;
        }

        static class Styles
        {
            static GUIStyle s_SceneLabelStyle;

            public static GUIStyle SceneLabelStyle
            {
                get
                {
                    if (s_SceneLabelStyle != null)
                    {
                        return s_SceneLabelStyle;
                    }

                    s_SceneLabelStyle = new GUIStyle
                    {
                        normal = new GUIStyleState
                        {
                            background = Texture2D.whiteTexture
                        },
                        fontSize = 12
                    };

                    return s_SceneLabelStyle;
                }
            }
        }
    }
}