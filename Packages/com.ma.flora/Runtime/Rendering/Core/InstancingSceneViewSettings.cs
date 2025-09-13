// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEngine;

namespace MA.Flora.Rendering
{
#if UNITY_EDITOR
    [InitializeOnLoad]
    static class InstancingSceneViewSettings
    {
        static class Styles
        {
            public static readonly GUIContent OcclusionMode = EditorGUIUtility.TrTextContent("Occlusion Mode", "The occlusion mode used for culling instances in the scene view.");
        }

        // Helper class to manage editor preferences with local caching.
        // Only supports bools, floats and ints/enums, so we keep it local for now.
        class CachedEditorPref<T>
        {
            T m_Storage;
            string m_Key;

            public T Value
            {
                // We update the Editor prefs only when writing. Reading goes through the cached local var to ensure that reads have no overhead.
                get => m_Storage;
                set
                {
                    m_Storage = value;
                    SetPref(value);
                }
            }

            // Creates a cached editor preference using the specified key and default value
            public CachedEditorPref(string key, T dafaultValue)
            {
                m_Key = key;
                m_Storage = GetOrCreatePref(dafaultValue);
            }

            T GetOrCreatePref(T defaultValue)
            {
                if (EditorPrefs.HasKey(m_Key))
                {
                    if (typeof(T) == typeof(bool))
                    {
                        return (T)(object)EditorPrefs.GetBool(m_Key);
                    }
                    else if (typeof(T) == typeof(float))
                    {
                        return (T)(object)EditorPrefs.GetFloat(m_Key);
                    }
                    return (T)(object)EditorPrefs.GetInt(m_Key);
                }
                else
                {
                    if (typeof(T) == typeof(bool))
                    {
                        EditorPrefs.SetBool(m_Key, (bool)(object)defaultValue);
                    }
                    else if (typeof(T) == typeof(float))
                    {
                        EditorPrefs.SetFloat(m_Key, (float)(object)defaultValue);
                    }
                    else
                    {
                        EditorPrefs.SetInt(m_Key, (int)(object)defaultValue);
                    }
                    return defaultValue;
                }
            }

            void SetPref(T value)
            {
                if (typeof(T) == typeof(bool))
                    EditorPrefs.SetBool(m_Key, (bool)(object)value);
                else if (typeof(T) == typeof(float))
                    EditorPrefs.SetFloat(m_Key, (float)(object)value);
                else
                    EditorPrefs.SetInt(m_Key, (int)(object)value);
            }
        }

        static CachedEditorPref<InstancingOcclusionMode> s_SceneViewOcclusionMode = new CachedEditorPref<InstancingOcclusionMode>("Flora:SceneViewCamera:OcclusionMode", InstancingOcclusionMode.None);

        public static InstancingOcclusionMode SceneViewOcclusionMode
        {
            get => s_SceneViewOcclusionMode.Value;
            set => s_SceneViewOcclusionMode.Value = value;
        }

        static InstancingSceneViewSettings()
        {
            SceneViewCameraWindow.additionalSettingsGui += DoAdditionalSettings;
        }

        static void DoAdditionalSettings(SceneView sceneView)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Flora", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            SceneViewOcclusionMode = (InstancingOcclusionMode)EditorGUILayout.EnumPopup(Styles.OcclusionMode, SceneViewOcclusionMode);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }
    }
#endif
}
