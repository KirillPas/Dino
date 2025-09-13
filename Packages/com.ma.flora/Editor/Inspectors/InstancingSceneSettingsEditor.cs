// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(InstancingSceneSettings))]
    class InstancingSceneSettingsEditor : UnityEditor.Editor
    {
        [MenuItem("GameObject/Flora/Instancing Scene Settings", false, 10)]
        static void CreateInstancingSceneSettingsCommand()
        {
            var instancingSceneSettings = UnityUtility.FindFirstObjectByType<InstancingSceneSettings>();
            if (instancingSceneSettings == null)
            {
                var go = new GameObject("Instancing Scene Settings");
                go.AddComponent<InstancingSceneSettings>();
                Selection.activeGameObject = go;
            }
            else
            {
                Selection.activeGameObject = instancingSceneSettings.gameObject;
            }
        }

        SerializedProperty m_GlobalInstanceDensity;
        SerializedProperty m_MainLightMode;
        SerializedProperty m_MainLightOverride;

        void OnEnable()
        {
            m_GlobalInstanceDensity = serializedObject.FindProperty("m_GlobalInstanceDensity");
            m_MainLightMode = serializedObject.FindProperty("m_MainLightMode");
            m_MainLightOverride = serializedObject.FindProperty("m_MainLightOverride");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InstancingSceneSettings instancingSceneSettings = (InstancingSceneSettings)target;
            EditorGUI.BeginChangeCheck();
            float instanceStaticDensity = EditorGUILayout.Slider(Styles.GlobalInstanceDensity, instancingSceneSettings.GlobalInstanceDensity, 0.0f, 1.0f);
            if (EditorGUI.EndChangeCheck())
            {
                instancingSceneSettings.GlobalInstanceDensity = instanceStaticDensity;
                m_GlobalInstanceDensity.floatValue = Mathf.Clamp01(m_GlobalInstanceDensity.floatValue);
            }

            EditorGUILayout.PropertyField(m_MainLightMode, Styles.MainLightMode);
            using (new EditorGUI.IndentLevelScope())
            {
                switch (m_MainLightMode.enumValueIndex)
                {
                    case (int)InstancingMainLightMode.Manual:
                        EditorGUILayout.PropertyField(m_MainLightOverride, Styles.MainLight);
                        break;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        static class Styles
        {
            public static readonly GUIContent GlobalInstanceDensity = EditorGUIUtility.TrTextContent("Global Instance Density", "Sets the global density of instances in the scene, affecting culling and GPU load for instances of prototypes with `AffectedByGlobalInstanceDensity` enabled.");
            public static readonly GUIContent MainLightMode = EditorGUIUtility.TrTextContent("Main Light Mode", "Determines how the main light will be chosen for culling shadow casting instances.");
            public static readonly GUIContent MainLight = EditorGUIUtility.TrTextContent("Main Light", "A manual override for the main light used for instancing.");
        }
    }
}
