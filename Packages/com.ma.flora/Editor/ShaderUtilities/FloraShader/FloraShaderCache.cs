// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace MA.Flora.Editor
{
    [FilePath("Library/com.ma.flora/ShaderCache", FilePathAttribute.Location.ProjectFolder)]
    class FloraShaderCache : ScriptableSingleton<FloraShaderCache>, ISerializationCallbackReceiver
    {
        [SerializeField] List<string> m_PatchedAssets = new List<string>();
        [SerializeField] List<FloraShaderImporter> m_PatchedImporters = new List<FloraShaderImporter>();

        Dictionary<string, FloraShaderImporter> m_PatchedShaderMap = new Dictionary<string, FloraShaderImporter>();

        public void Register(Shader shader, FloraShaderImporter importer)
        {
            if (shader && importer)
            {
                string assetGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(shader));
                if (!string.IsNullOrEmpty(assetGUID))
                    Register(assetGUID, importer);
            }
        }

        public void Register(string guid, FloraShaderImporter importer)
        {
            if (!m_PatchedAssets.Contains(guid))
            {
                m_PatchedAssets.Add(guid);
                m_PatchedImporters.Add(importer);
            }
        }

        public void Unregister(Shader shader)
        {
            string assetGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(shader));
            if (!string.IsNullOrEmpty(assetGUID))
                Unregister(assetGUID);
        }

        public void Unregister(string guid)
        {
            if (m_PatchedShaderMap.TryGetValue(guid, out FloraShaderImporter importer))
            {
                m_PatchedAssets.Remove(guid);
                m_PatchedImporters.Remove(importer);
            }
        }

        public bool TryGetImporter(Shader shader, out FloraShaderImporter importer)
        {
            string assetGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(shader));
            if (!string.IsNullOrEmpty(assetGUID))
                return TryGetImporter(assetGUID, out importer);

            importer = null;
            return false;
        }

        public bool TryGetImporter(string guid, out FloraShaderImporter importer)
        {
            if (m_PatchedShaderMap.TryGetValue(guid, out importer) && importer)
                return importer;

            m_PatchedShaderMap.Remove(guid);
            return false;
        }

        public void SaveToLibrary()
        {
            EditorUtility.SetDirty(this);
            Save(true);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            m_PatchedAssets.Clear();
            m_PatchedImporters.Clear();

            foreach (KeyValuePair<string, FloraShaderImporter> pair in m_PatchedShaderMap)
            {
                if (string.IsNullOrEmpty(pair.Key) || !pair.Value)
                    continue;

                string assetPath = AssetDatabase.GUIDToAssetPath(pair.Key);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                m_PatchedAssets.Add(pair.Key);
                m_PatchedImporters.Add(pair.Value);
            }
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            m_PatchedShaderMap.Clear();

            for (int i = 0; i < m_PatchedAssets.Count; i++)
            {
                if (m_PatchedImporters[i])
                    m_PatchedShaderMap[m_PatchedAssets[i]] = m_PatchedImporters[i];
            }
        }
    }
}
