// Copyright © Magnetic Arcade. All Rights Reserved.

// #define SHOW_DEBUG_UI

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MA.Flora.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(FloraShaderImporter))]
    class FloraShaderImporterEditor : AssetImporterEditor
    {
        static class Styles
        {
            public static readonly GUIContent LoadError = EditorGUIUtility.TrTextContent("Load error");
        }

        SerializedFloraShaderMetadata m_SerializedState;
        GUIStyle m_TextStyle;
        Exception m_InitializeException;
        MaterialEditor m_MaterialEditor;

        protected override bool needsApplyRevert => false;

        public override void OnEnable()
        {
            base.OnEnable();

            m_SerializedState = new SerializedFloraShaderMetadata(extraDataSerializedObject);

            AssetImporter importer = (AssetImporter)target;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(importer.assetPath);
            if (material)
                m_MaterialEditor = (MaterialEditor)CreateEditor(material);
        }

        public override void OnDisable()
        {
            base.OnDisable();
            if (m_MaterialEditor)
                DestroyImmediate(m_MaterialEditor);
        }

        public override void OnInspectorGUI()
        {
            if (m_InitializeException != null)
            {
                ShowLoadErrorExceptionGUI(m_InitializeException);
                ApplyRevertGUI();
                return;
            }

            FloraShaderMetadata state = (FloraShaderMetadata)extraDataTarget;

            m_SerializedState.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                m_SerializedState.Data.SourceType.enumValueIndex = (int)FloraShaderImporter.GetShaderSourceType(AssetDatabase.GUIDToAssetPath(state.Data.SourceGUID));
                EditorGUILayout.PropertyField(m_SerializedState.Data.SourceType);
            }

#if SHOW_DEBUG_UI
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedState.Data.EnableDebugSymbols);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Source Path", state.Data.SourcePath);
                    EditorGUILayout.TextField("Source GUID", state.Data.SourceGuid);
                }
            }
#else
            state.Data.EnableDebugSymbols = false;
#endif

            GUILayout.Space(6f);

            m_SerializedState.Apply();

            using (new GUILayout.HorizontalScope("box"))
            {
                string assetName = Path.GetFileNameWithoutExtension(state.AssetPath);
                string path = $"Temp/GeneratedFromFlora-{assetName.Replace(" ", "")}.shader";

                bool alreadyExists = File.Exists(path);
                bool update = false;
                bool open = false;

                if (GUILayout.Button("View Generated Shader"))
                {
                    update = true;
                    open = true;
                }

                if (alreadyExists && GUILayout.Button("Regenerate"))
                    update = true;

                if (update)
                {
                    if (FloraShaderImporter.TryPatchSource(state.Data.SourceGUID, state.Data.PatchFlags, out string modifiedSourceCode))
                    {
                        if (!WriteToFile(path, modifiedSourceCode))
                            open = false;
                    }
                    else
                    {
                        open = false;
                    }
                }

                if (open)
                    OpenScriptFile(path);

                if (GUILayout.Button("Copy"))
                {
                    if (FloraShaderImporter.TryPatchSource(state.Data.SourceGUID, state.Data.PatchFlags, out string modifiedSourceCode))
                    {
                        GUIUtility.systemCopyBuffer = modifiedSourceCode;
                    }
                }
            }

            ApplyRevertGUI();

            if (m_MaterialEditor)
            {
                EditorGUILayout.Space();
                m_MaterialEditor.DrawHeader();
                using (new EditorGUI.DisabledGroupScope(true))
                    m_MaterialEditor.OnInspectorGUI();
            }
        }

        // --- Extra data handling ---

        protected override Type extraDataType => typeof(FloraShaderMetadata);

        protected override void InitializeExtraDataInstance(Object extraTarget, int targetIndex)
        {
            try
            {
                LoadMetadata((FloraShaderMetadata)extraTarget, ((AssetImporter)targets[targetIndex]).assetPath);
                m_InitializeException = null;
            }
            catch (Exception e)
            {
                m_InitializeException = e;
            }
        }

        protected override void Apply()
        {
            base.Apply();

            // Do not write back to the asset if no asset can be found.
            if (targets != null)
                SaveAndUpdateMetadatas(extraDataTargets.Cast<FloraShaderMetadata>().ToArray());
        }

        void ShowLoadErrorExceptionGUI(Exception e)
        {
            m_TextStyle ??= "ScriptText";
            GUILayout.Label(Styles.LoadError, EditorStyles.boldLabel);
            Rect rect = GUILayoutUtility.GetRect(EditorGUIUtility.TrTextContent(e.Message), m_TextStyle);
            EditorGUI.HelpBox(rect, e.Message, MessageType.Error);
        }

        static void LoadMetadata(FloraShaderMetadata state, string path)
        {
            string text = File.ReadAllText(path);
            if (string.IsNullOrEmpty(text))
                return;

            FloraShaderData data = JsonUtility.FromJson<FloraShaderData>(text);
            if (data == null)
                return;

            state.AssetPath = path;
            state.Data = data;
        }

        static void SaveAndUpdateMetadatas(FloraShaderMetadata[] metadatas)
        {
            foreach (FloraShaderMetadata metadata in metadatas)
            {
                SaveMetadata(metadata);
            }
        }

        static void SaveMetadata(FloraShaderMetadata metadata)
        {
            FloraShaderData data = metadata.Data;

            string json = JsonUtility.ToJson(data);
            if (WriteToFile(metadata.AssetPath, json))
            {
                AssetDatabase.ImportAsset(metadata.AssetPath);
            }
        }

        static bool WriteToFile(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return false;
            }
        }

         static void OpenScriptFile(string path)
         {
            string filePath = Path.GetFullPath(path);
            if (!File.Exists(filePath))
            {
                Debug.LogError($"Path {path} doesn't exists");
                return;
            }

            string externalScriptEditor = ScriptEditorUtility.GetExternalScriptEditor();
            if (externalScriptEditor != "internal")
            {
                InternalEditorUtility.OpenFileAtLineExternal(filePath, 0);
            }
            else
            {
                Process p = new Process();
                p.StartInfo.FileName = filePath;
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) =>
                {
                    if (p.ExitCode != 0)
                        Debug.LogWarningFormat("Unable to open {0}: Check external editor in preferences", filePath);
                };
                p.Start();
            }
        }
    }
}
