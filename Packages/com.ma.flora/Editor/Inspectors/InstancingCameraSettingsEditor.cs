// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InstancingCameraSettings))]
    class InstancingCameraSettingsEditor : UnityEditor.Editor
    {
        SerializedProperty m_OcclusionMode;
        SerializedProperty m_MinimumScreenSize;
        SerializedProperty m_DisableRendering;

        SerializedProperty m_LODBiasScale;
        SerializedProperty m_CrossFadeAnimatedDurationMode;
        SerializedProperty m_CrossFadeAnimatedDuration;

        void OnEnable()
        {
            m_OcclusionMode = serializedObject.FindProperty("m_OcclusionMode");
            m_MinimumScreenSize = serializedObject.FindProperty("m_MinimumScreenSize");
            m_DisableRendering = serializedObject.FindProperty("m_DisableInstanceRendering");

            m_LODBiasScale = serializedObject.FindProperty("m_LODBiasScale");
            m_CrossFadeAnimatedDurationMode = serializedObject.FindProperty("m_CrossFadeAnimatedDurationMode");
            m_CrossFadeAnimatedDuration = serializedObject.FindProperty("m_CrossFadeAnimatedDuration");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_OcclusionMode, Styles.OcclusionMode);
            EditorGUILayout.PropertyField(m_MinimumScreenSize, Styles.MinimumScreenSize);
            EditorGUILayout.PropertyField(m_DisableRendering, Styles.DisableRendering);

            EditorGUILayout.LabelField(Styles.LODSettings, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_LODBiasScale, Styles.LODBias);
                EditorGUILayout.PropertyField(m_CrossFadeAnimatedDurationMode, Styles.LODCrossFadeMode);
                using (new EditorGUI.IndentLevelScope())
                {
                    using (new EditorGUI.DisabledScope(m_CrossFadeAnimatedDurationMode.enumValueIndex == (int)CrossFadeAnimatedDurationMode.Global))
                        EditorGUILayout.PropertyField(m_CrossFadeAnimatedDuration, Styles.LODCrossFadeDuration);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    static class Styles
    {
        public static GUIContent OcclusionMode = EditorGUIUtility.TrTextContent("Occlusion Mode", "The occlusion mode for this camera.");
        public static GUIContent MinimumScreenSize = EditorGUIUtility.TrTextContent("Minimum Screen Size", "Controls the minimum screen size for an instance.");
        public static GUIContent DisableRendering = EditorGUIUtility.TrTextContent("Disable Rendering", "Disables instance rendering for this camera.");

        public static GUIContent LODSettings = EditorGUIUtility.TrTextContent("LOD Settings");
        public static GUIContent LODBias = EditorGUIUtility.TrTextContent("Bias Scale", "Scales the global LOD bias for instances rendered by this camera.");
        public static GUIContent LODCrossFadeMode = EditorGUIUtility.TrTextContent("Animated Cross-Fade Duration", "Choose between using the global cross-fade animation duration or a custom duration.");
        public static GUIContent LODCrossFadeDuration = EditorGUIUtility.TrTextContent("Duration", "The duration of the cross-fade animation.");
    }
}
