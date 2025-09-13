// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace MA.Flora.Editor
{
    enum FloraSourceShaderType
    {
        Invalid,
        Shader,
        ShaderGraph,
        BetterShader,
        MicroVersePack,
        Unrecognized,
    }

    [Serializable]
    class FloraShaderData
    {
        public int ImporterVersion;
        public FloraSourceShaderType SourceType;
        [FormerlySerializedAs("SourceGuid")]
        public string SourceGUID;
        public bool EnableDebugSymbols;

        public string GetSourceAssetPath() => string.IsNullOrEmpty(SourceGUID) ? "" : AssetDatabase.GUIDToAssetPath(SourceGUID);

        public ShaderPatcher.PatchFlags PatchFlags
        {
            get
            {
                ShaderPatcher.PatchFlags flags = ShaderPatcher.PatchFlags.None;
                if (EnableDebugSymbols)
                    flags |= ShaderPatcher.PatchFlags.DebugSymbols;
                return flags;
            }
        }
    }

    class FloraShaderMetadata : ScriptableObject
    {
        public string AssetPath;
        public FloraShaderData Data;
    }

    class SerializedFloraShaderData
    {
        public SerializedProperty BaseProperty { get; }
        public SerializedProperty ImporterVersion { get; }
        public SerializedProperty SourceType { get; }
        public SerializedProperty SourceGuid { get; }
        public SerializedProperty EnableDebugSymbols { get; }

        public SerializedFloraShaderData(SerializedProperty baseProperty)
        {
            BaseProperty = baseProperty;
            ImporterVersion = BaseProperty.FindPropertyRelative("ImporterVersion");
            SourceType = BaseProperty.FindPropertyRelative("SourceType");
            SourceGuid = BaseProperty.FindPropertyRelative("SourceGuid");
            EnableDebugSymbols = BaseProperty.FindPropertyRelative("EnableDebugSymbols");
        }
    }

    class SerializedFloraShaderMetadata
    {
        public SerializedObject SerializedObject { get; }
        public SerializedFloraShaderData Data { get; }

        public SerializedFloraShaderMetadata(SerializedObject serializedObject)
        {
            SerializedObject = serializedObject;
            Data = new SerializedFloraShaderData(SerializedObject.FindProperty("Data"));
        }

        public void Update()
        {
            SerializedObject.Update();
        }

        public void Apply()
        {
            SerializedObject.ApplyModifiedProperties();
        }
    }
}
