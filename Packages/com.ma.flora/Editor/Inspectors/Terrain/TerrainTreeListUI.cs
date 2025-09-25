// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using MA.Core.Bridge;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MA.Flora.Editor
{
    class TerrainTreeListUI
    {
        public InstancedTerrainFoliage TerrainFoliage;
        public Terrain Terrain;
        public TerrainData TerrainData;
        public ReorderableList List;
        public List<TreePrototype> Prototypes;
        public List<InstancedPrototype> Selected = new List<InstancedPrototype>();
        public int SelectedVersion;

        public enum NavMeshLodIndex
        {
            First,
            Last,
            Custom
        }

        public const int NavMeshLodFirst = -1;
        public const int NavMeshLodLast = int.MaxValue;

        public TerrainTreeListUI(InstancedTerrainFoliage terrain)
        {
            TerrainFoliage = terrain;
            Terrain = terrain.Terrain;
            TerrainData = terrain.TerrainData;
            Prototypes = new List<TreePrototype>(TerrainData.treePrototypes);
            List = new ReorderableList(Prototypes, typeof(TreePrototype), true, true, true, true)
            {
                drawHeaderCallback = OnDrawHeader,
                onSelectCallback = OnSelected,
                elementHeightCallback = OnGetElementHeight,
                drawElementCallback = OnDrawElement,
                onAddCallback = OnAddElement,
                onRemoveCallback = OnRemoveElement,
                multiSelect = true,
            };
        }

        void OnDrawHeader(Rect rect)
        {
            GUI.Label(rect, "Tree Prototypes", EditorStyles.boldLabel);
        }

        void OnSelected(ReorderableList list)
        {
            Selected.Clear();
            foreach (var i in list.selectedIndices)
            {
                if (i >= 0 && i < Prototypes.Count)
                {
                    GameObject prefab = Prototypes[i].prefab;
                    if (prefab != null && prefab.TryGetComponent(out InstancedPrototype instancePrototype))
                        Selected.Add(instancePrototype);
                }
            }

            SelectedVersion++;
        }

        float OnGetElementHeight(int index) => EditorGUIUtility.singleLineHeight + 4;

        void OnDrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index >= Prototypes.Count) return;

            TreePrototype prototype = Prototypes[index];
            EditorGUI.BeginChangeCheck();

            // Initial setup for drawing
            rect.height = EditorGUIUtility.singleLineHeight;
            rect.x += 11;
            rect.width -= 11;
            rect.y += 2;

            GameObject prefab = (GameObject)EditorGUI.ObjectField(rect, prototype.prefab, typeof(GameObject), false);
            prefab = ValidatePrototype(prefab);

            if (prefab && EditorGUI.EndChangeCheck())
            {
                Prototypes[index] = prototype;
                Undo.RegisterCompleteObjectUndo(TerrainFoliage, "Change Tree Prototype");
                Undo.RegisterCompleteObjectUndo(TerrainData, "Change Tree Prototype");
                TerrainData.treePrototypes = Prototypes.ToArray();
            }
        }

        void OnAddElement(ReorderableList list)
        {
            ModelPicker.Show(Prototypes.Select(p => p.prefab).ToList(), true, OnSelectModels);
        }

        void OnSelectModels(GameObject[] obj)
        {
            foreach (GameObject prototype in obj)
            {
                GameObject validatedPrototype = ValidatePrototype(prototype);
                if (validatedPrototype == null)
                    continue;

                TreePrototype newPrototype = new TreePrototype
                {
                    prefab = validatedPrototype,
                    bendFactor = 0.0f,
                    navMeshLod = NavMeshLodLast,
                };

                Prototypes.Add(newPrototype);
            }

            Undo.RegisterCompleteObjectUndo(TerrainFoliage, "Add Tree Prototype");
            Undo.RegisterCompleteObjectUndo(TerrainData, "Add Tree Prototype");
            TerrainData.treePrototypes = Prototypes.ToArray();
        }

        void OnRemoveElement(ReorderableList list)
        {
            Undo.RegisterCompleteObjectUndo(TerrainFoliage, "Remove Tree Prototype");
            Undo.RegisterCompleteObjectUndo(TerrainData, "Remove Tree Prototype");

            int[] reversedIndices = list.selectedIndices.OrderByDescending(i => i).ToArray();
            foreach (var index in reversedIndices)
            {
                Prototypes.RemoveAt(index);
                TerrainData.RemoveTreePrototypeBridged(index);
            }

            list.ClearSelection();
            TerrainPrototypePreviewUI.Selected.Clear();
        }

        static GameObject ValidatePrototype(GameObject prototype)
        {
            if (prototype == null)
                return null;

            if (prototype.GetComponentInChildren<MeshRenderer>() == null &&
                prototype.GetComponent<LODGroup>() == null)
            {
                Debug.Log("Selected object must have a MeshRenderer or LODGroup component.");
                return null;
            }

            if (!prototype.TryGetComponent(out InstancedPrototype _))
                Undo.AddComponent<InstancedPrototype>(prototype);

            return prototype;
        }

        public void OnGUI()
        {
            List.DoLayoutList();
        }
    }
}
