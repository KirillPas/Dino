// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace MA.Flora.Editor
{
    class SerializedInstancedTerrainFoliage
    {
        public SerializedObject SerializedObject;
        public InstancedTerrainFoliage InstancedTerrainFoliage;
        public Terrain Terrain;
        public TerrainData TerrainData;

        public EditorPrefBool DisableDistanceScalingWhileEditing;
        public SerializedProperty DetailsLoadMode;
        public SerializedProperty DetailsCullMode;
        public TerrainDetailListUI DetailListUI;

        public SerializedProperty TreesLoadMode;
        public SerializedProperty TreesCullMode;
        public SerializedProperty TreeGridSize;
        public TerrainTreeListUI TreeListUI;

        public TerrainPrototypePreviewUI PrototypePreviewUI;

        public SerializedInstancedTerrainFoliage(SerializedObject serializedObject)
        {
            SerializedObject = serializedObject;
            InstancedTerrainFoliage = (InstancedTerrainFoliage)serializedObject.targetObject;
            Terrain = InstancedTerrainFoliage.GetComponent<Terrain>();
            TerrainData = Terrain.terrainData;

            DisableDistanceScalingWhileEditing = new EditorPrefBool("MA.Flora.Editor.DisableDistanceScalingWhileEditing", false);
            DetailsLoadMode = serializedObject.FindProperty("m_DetailsLoadMode");
            DetailsCullMode = serializedObject.FindProperty("m_DetailsCullMode");
            DetailListUI = new TerrainDetailListUI(InstancedTerrainFoliage);

            TreesLoadMode = serializedObject.FindProperty("m_TreesLoadMode");
            TreesCullMode = serializedObject.FindProperty("m_TreesCullMode");
            TreeGridSize = serializedObject.FindProperty("m_TreeGridSize");
            TreeListUI = new TerrainTreeListUI(InstancedTerrainFoliage);

            PrototypePreviewUI = new TerrainPrototypePreviewUI();
        }

        public void Update()
        {
            SerializedObject.Update();
        }

        public void ApplyModifiedProperties()
        {
            SerializedObject.ApplyModifiedProperties();
        }
    }
}
