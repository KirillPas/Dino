// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace MA.Flora.Editor
{
    static class FloraShaderMenu
    {
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceID, int line, int column)
        {
            string assetPath = AssetDatabase.GetAssetPath(instanceID);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            if (AssetImporter.GetAtPath(assetPath) is not FloraShaderImporter)
                return false;

            string text = File.ReadAllText(assetPath);
            FloraShaderData metadata = JsonUtility.FromJson<FloraShaderData>(text);

            string sourcePath = AssetDatabase.GUIDToAssetPath(metadata.SourceGUID);
            if (metadata.SourceType == FloraSourceShaderType.Shader)
            {
                Shader sourceShader = AssetDatabase.LoadAssetAtPath<Shader>(sourcePath);
                if (sourceShader)
                {
                    AssetDatabase.OpenAsset(sourceShader);
                    return true;
                }
            }

            return false;
        }

        static Material[] GetSelectedMaterials() => Selection.GetFiltered<Material>(SelectionMode.Assets | SelectionMode.Editable);

        [MenuItem("CONTEXT/Material/Convert Material To Flora", priority = 200)]
        static void ConvertSelectedMaterialsToFloraShaders(MenuCommand command)
        {
            Material[] selectedMaterials = GetSelectedMaterials();

            Dictionary<Shader, List<Material>> originalShaderMap = new Dictionary<Shader, List<Material>>();
            foreach (Material material in selectedMaterials)
            {
                if (material.shader)
                {
                    if (!originalShaderMap.TryGetValue(material.shader, out List<Material> materialList))
                    {
                        materialList = new List<Material>();
                        originalShaderMap[material.shader] = materialList;
                    }

                    materialList.Add(material);
                }
            }

            foreach (Shader shader in originalShaderMap.Keys)
            {
                if (shader)
                {
                    string shaderName = shader.name;
                    if (shaderName.Contains("Flora"))
                        continue;

                    Shader patchedShader = ShaderPatcher.GetOrCreatePatchedShader(shader);
                    if (patchedShader)
                    {
                        foreach (Material material in originalShaderMap[shader])
                        {
                            Undo.RecordObject(material, "Convert To Instanced Shader");
                            material.shader = patchedShader;
                            EditorUtility.SetDirty(material);
                        }
                    }
                }
            }

            AssetDatabase.Refresh();
        }

        [MenuItem("Assets/Convert Material to Flora", true, priority = 0)]
        static bool ConvertMaterialToFloraShaderCommandValidate(MenuCommand context)
        {
            Material[] selectedMaterials = Selection.GetFiltered<Material>(SelectionMode.Assets);
            return selectedMaterials.Length > 0;
        }

        [MenuItem("Assets/Convert Material to Flora", false, priority = 0)]
        static void ConvertMaterialToFloraShaderCommand(MenuCommand context)
        {
            ConvertSelectedMaterialsToFloraShaders(context);
        }

        [MenuItem("Assets/Convert Shader to Flora", true, priority = 0)]
        static bool ConvertShaderToFloraShaderCommandValidate(MenuCommand context)
        {
            Shader[] selectedShaders = Selection.GetFiltered<Shader>(SelectionMode.Assets);
            return selectedShaders.Length > 0;
        }

        [MenuItem("Assets/Convert Shader to Flora", false, priority = 0)]
        static void ConvertShaderToFloraShaderCommand(MenuCommand context)
        {
            Shader[] selectedShaders = Selection.GetFiltered<Shader>(SelectionMode.Assets);

            List<Shader> patchedShaders = new List<Shader>();
            foreach (Shader shader in selectedShaders)
            {
                Shader patchShader = ShaderPatcher.GetOrCreatePatchedShader(shader);
                if (patchShader)
                    patchedShaders.Add(patchShader);
            }

            if (patchedShaders.Count > 0)
            {
                AssetDatabase.Refresh();
                Selection.objects = patchedShaders.ConvertAll(s => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(s))).ToArray();
            }
        }
    }
}
