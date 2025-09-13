// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using MA.Core.Bridge;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MA.Flora.Editor
{
    class TerrainDetailListUI
    {
        public InstancedTerrainFoliage TerrainFoliage;
        public Terrain Terrain;
        public TerrainData TerrainData;
        public ReorderableList List;
        public List<DetailPrototype> Prototypes;
        public List<InstancedPrototype> Selected = new List<InstancedPrototype>();
        public int SelectedVersion;

        Dictionary<int, bool> m_FoldoutStates = new Dictionary<int, bool>();

        public TerrainDetailListUI(InstancedTerrainFoliage terrain)
        {
            TerrainFoliage = terrain;
            Terrain = terrain.Terrain;
            TerrainData = terrain.TerrainData;
            Prototypes = new List<DetailPrototype>(TerrainData.detailPrototypes);
            List = new ReorderableList(Prototypes, typeof(DetailPrototype), true, true, true, true)
            {
                drawHeaderCallback = OnDrawHeader,
                elementHeightCallback = OnGetElementHeight,
                drawElementCallback = OnDrawElement,
                onSelectCallback = OnSelected,
                onAddCallback = OnAddElement,
                onRemoveCallback = OnRemoveElement,
                multiSelect = true,
            };
        }

        void OnDrawHeader(Rect rect)
        {
            GUI.Label(rect, "Detail Prototypes", EditorStyles.boldLabel);
        }

        void OnSelected(ReorderableList list)
        {
            Selected.Clear();
            foreach (var i in list.selectedIndices)
            {
                if (i >= 0 && i < Prototypes.Count)
                {
                    GameObject prefab = FoliageUtility.GetPrototypeRoot(Prototypes[i]);
                    if (prefab != null && prefab.TryGetComponent(out InstancedPrototype instancePrototype))
                    {
                        Selected.Add(instancePrototype);
                    }
                }
            }

            SelectedVersion++;
        }

        float OnGetElementHeight(int index)
        {
            return m_FoldoutStates.ContainsKey(index) && m_FoldoutStates[index] ?
                   EditorGUIUtility.singleLineHeight * 11 + 4 :
                   EditorGUIUtility.singleLineHeight + 4;
        }

        void OnDrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index >= Prototypes.Count) return;

            DetailPrototype prototype = Prototypes[index];
            EditorGUI.BeginChangeCheck();

            // Initial setup for drawing
            rect.height = EditorGUIUtility.singleLineHeight;
            rect.x += 11;
            rect.width -= 11;
            rect.y += 2;

            const float fieldPadding = 2;
            float currentY = rect.y;

            // Draw a foldout for the entire element
            m_FoldoutStates.TryAdd(index, false);

            // Create a rect for the foldout toggle and object field
            const float foldoutWidth = 6;
            Rect foldoutRect = new Rect(rect.x, currentY, foldoutWidth, rect.height);
            Rect objectFieldRect = new Rect(rect.x + foldoutWidth, currentY, rect.width - foldoutWidth, rect.height);

            bool foldoutState = m_FoldoutStates[index];
            m_FoldoutStates[index] = EditorGUI.Foldout(foldoutRect, foldoutState, GUIContent.none, true);
            rect.x += 2;
            rect.width -= 2;

            GameObject prefab = FoliageUtility.GetPrototypeRoot(prototype.prototype);
            prefab = (GameObject)EditorGUI.ObjectField(objectFieldRect, prefab, typeof(GameObject), false);
            prefab = ConvertPrototype(prefab);
            currentY += rect.height + fieldPadding;

            if (m_FoldoutStates[index])
            {
                rect.x += 11;
                rect.width -= 11;

#if UNITY_2022_3_OR_NEWER
                float alignToGround = EditorGUI.Slider(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Align to Ground (%)"), prototype.alignToGround * 100f, 0f, 100f);
                currentY += rect.height + fieldPadding;

                GUI.enabled = !QualitySettings.useLegacyDetailDistribution;
                float positionJitter = EditorGUI.Slider(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Position Jitter (%)"), prototype.positionJitter * 100f, 0f, 100f);
                currentY += rect.height + fieldPadding;
                GUI.enabled = true;
#endif

                Vector2 widthRange = new Vector2(prototype.minWidth, prototype.maxWidth);
                DrawMinMaxSliderWithInputFields(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Width Range"), ref widthRange, 0.1f, 10f);
                currentY += rect.height + fieldPadding;

                Vector2 heightRange = new Vector2(prototype.minHeight, prototype.maxHeight);
                DrawMinMaxSliderWithInputFields(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Height Range"), ref heightRange, 0.1f, 10f);
                currentY += rect.height + fieldPadding;

                int noiseSeed = EditorGUI.IntField(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Noise Seed"), prototype.noiseSeed);
                currentY += rect.height + fieldPadding;

                float noiseSpread = EditorGUI.FloatField(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Noise Spread"), prototype.noiseSpread);
                currentY += rect.height + fieldPadding;

                float holeEdgePadding = EditorGUI.Slider(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Hole Edge Padding (%)"), prototype.holeEdgePadding * 100f, 0f, 100f);
                currentY += rect.height + fieldPadding;

#if UNITY_2022_3_OR_NEWER
                float detailDensity = EditorGUI.Slider(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Detail Density"), prototype.density, 0f, 5f);
                currentY += rect.height + fieldPadding;

                bool affectedByDensityScale = EditorGUI.Toggle(new Rect(rect.x, currentY, rect.width, rect.height), L10n.Tr("Affected by Density Scale"), prototype.useDensityScaling);
                currentY += rect.height + fieldPadding;
#endif

                if (EditorGUI.EndChangeCheck())
                {
                    if (prefab != null)
                        prototype.prototype = prefab;

#if UNITY_2022_3_OR_NEWER
                    prototype.alignToGround = alignToGround / 100f;
                    if (!QualitySettings.useLegacyDetailDistribution)
                        prototype.positionJitter = positionJitter / 100f;
#endif
                    prototype.minWidth = widthRange.x;
                    prototype.maxWidth = widthRange.y;
                    prototype.minHeight = heightRange.x;
                    prototype.maxHeight = heightRange.y;
                    prototype.noiseSeed = noiseSeed;
                    prototype.noiseSpread = noiseSpread;
                    prototype.holeEdgePadding = holeEdgePadding / 100f;
#if UNITY_2022_3_OR_NEWER
                    prototype.density = detailDensity;
                    prototype.useDensityScaling = affectedByDensityScale;
#endif

                    Prototypes[index] = prototype;

                    Undo.RegisterCompleteObjectUndo(TerrainData, "Change Detail Prototype");
                    TerrainData.detailPrototypes = Prototypes.ToArray();
                }
            }
        }

        void DrawMinMaxSliderWithInputFields(Rect rect, string label, ref Vector2 range, float minLimit, float maxLimit, float fieldWidth = 50f, float padding = 5f)
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 75f;
            EditorGUI.LabelField(new Rect(rect.x, rect.y, labelWidth, rect.height), label);
            range.x = EditorGUI.FloatField(new Rect(rect.x + labelWidth, rect.y, fieldWidth, rect.height), range.x);
            EditorGUI.MinMaxSlider(new Rect(rect.x + labelWidth + fieldWidth + padding, rect.y, rect.width - labelWidth - 2 * fieldWidth - 2 * padding, rect.height), ref range.x, ref range.y, minLimit, maxLimit);
            range.y = EditorGUI.FloatField(new Rect(rect.x + rect.width - fieldWidth, rect.y, fieldWidth, rect.height), range.y);
            EditorGUIUtility.labelWidth = labelWidth;
        }

        void OnAddElement(ReorderableList list)
        {
            ModelPicker.Show(Prototypes.Select(p => p.prototype).ToList(), true, OnSelectModels);
        }

        void OnSelectModels(GameObject[] obj)
        {
            foreach (GameObject prototype in obj)
            {
                GameObject validatedPrototype = ConvertPrototype(prototype);
                if (validatedPrototype == null)
                    continue;

                DetailPrototype newPrototype = new DetailPrototype
                {
                    prototype = validatedPrototype,
                    minWidth = 1, maxWidth = 2,
                    minHeight = 1, maxHeight = 2,
                    noiseSeed = UnityEngine.Random.Range(1, int.MaxValue),
                    noiseSpread = 0.1f,
                    healthyColor = Color.white,
                    dryColor = Color.white,
                    renderMode = DetailRenderMode.VertexLit,
                    usePrototypeMesh = true,
                    useInstancing = true,
                    holeEdgePadding = 0,
#if UNITY_2022_3_OR_NEWER
                    useDensityScaling = true,
                    density = 1,
                    targetCoverage = 1,
                    alignToGround = 0,
                    positionJitter = 0,
#endif
                };

                Prototypes.Add(newPrototype);
            }

            Undo.RegisterCompleteObjectUndo(TerrainFoliage, "Add Detail Prototype");
            Undo.RegisterCompleteObjectUndo(TerrainData, "Add Detail Prototype");
            TerrainData.detailPrototypes = Prototypes.ToArray();
        }

        void OnRemoveElement(ReorderableList list)
        {
            Undo.RegisterCompleteObjectUndo(TerrainFoliage, "Remove Detail Prototype");
            Undo.RegisterCompleteObjectUndo(TerrainData, "Remove Detail Prototype");

            int[] reversedIndices = list.selectedIndices.OrderByDescending(i => i).ToArray();
            foreach (var index in reversedIndices)
            {
                Prototypes.RemoveAt(index);
                TerrainData.RemoveDetailPrototypeBridged(index);
            }

            list.ClearSelection();
            TerrainPrototypePreviewUI.Selected.Clear();
        }

        static GameObject ConvertPrototype(GameObject prototype)
        {
            if (prototype == null)
                return null;

            prototype = FoliageUtility.GetPrototypeRoot(prototype);

            if (prototype.GetComponentInChildren<MeshRenderer>() == null &&
                prototype.GetComponent<LODGroup>() == null)
            {
                Debug.Log("Selected object must have a MeshRenderer or LODGroup component.");
                return null;
            }

            if (!prototype.TryGetComponent(out InstancedPrototype _))
                Undo.AddComponent<InstancedPrototype>(prototype);

            return FoliageUtility.GetUnityCompatibleDetailPrefab(prototype);
        }

        public void OnGUI()
        {
            List.DoLayoutList();
        }
    }
}
