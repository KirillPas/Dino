using MA.Flora.Rendering;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace MA.Flora.Editor
{
    using CED = CoreEditorDrawer<SerializedInstancedTerrainFoliage>;

    [CanEditMultipleObjects]
    [CustomEditor(typeof(InstancedTerrainFoliage))]
    class InstancedTerrainFoliageEditor : UnityEditor.Editor
    {
        bool m_PrevDisableDensityScaling;
        bool m_IsDrawingUI;
        SerializedInstancedTerrainFoliage m_SerializedInstancedTerrainFoliage;

        void OnEnable()
        {
            m_PrevDisableDensityScaling = InstancingSystem.DisableDensityCulling;
            InstancingSystem.DisableDensityCulling = false;
            InstancedTerrainFoliage.TerrainInstancesChanged += OnTerrainInstancesChange;
        }

        void OnDisable()
        {
            InstancedTerrainFoliage.TerrainInstancesChanged -= OnTerrainInstancesChange;
            InstancingSystem.DisableDensityCulling = m_PrevDisableDensityScaling;
        }

        public override void OnInspectorGUI()
        {
            m_SerializedInstancedTerrainFoliage ??= new SerializedInstancedTerrainFoliage(serializedObject);
            m_SerializedInstancedTerrainFoliage.Update();

            EditorGUI.BeginChangeCheck();
            m_IsDrawingUI = true;
            InstancedTerrainFoliageUI.Inspector.Draw(m_SerializedInstancedTerrainFoliage, this);
            m_IsDrawingUI = false;
            if (EditorGUI.EndChangeCheck())
            {

            }

            m_SerializedInstancedTerrainFoliage.ApplyModifiedProperties();
        }

        void OnTerrainInstancesChange(InstancedTerrainFoliage instancedTerrain, TerrainChangedFlags changeFlags)
        {
            if (!m_IsDrawingUI)
            {
                m_SerializedInstancedTerrainFoliage = null; // Force a refresh of the prototype list
                Repaint();
            }
        }
    }

    static class InstancedTerrainFoliageUI
    {
        enum Expandable
        {
            Trees      = 1 << 0,
            Details    = 1 << 1,
            Default    = Trees | Details
        }

        static readonly ExpandedState<Expandable, InstancedTerrainFoliageEditor> k_ExpandedState = new(Expandable.Default, "MA.Flora");

        public static readonly CED.IDrawer SectionTrees =
            CED.FoldoutGroup(Styles.TreesSection, Expandable.Trees, k_ExpandedState, FoldoutOption.None,
                CED.Group(DrawTreesSection));

        public static readonly CED.IDrawer SectionDetails =
            CED.FoldoutGroup(Styles.DetailsSection, Expandable.Details, k_ExpandedState, FoldoutOption.None,
                CED.Group(DrawDetailsSection));

        public static readonly CED.IDrawer SectionPrototypes =
            CED.Conditional(ShouldShowPrototypes, CED.Group(DrawPrototypes));

        public static readonly CED.IDrawer[] Inspector =
        {
            SectionTrees,
            SectionDetails,
            SectionPrototypes,
        };

        public static void DrawDetailsSection(SerializedInstancedTerrainFoliage serialized, UnityEditor.Editor owner)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(serialized.DetailsLoadMode, Styles.DetailsLoadingMode);
            EditorGUILayout.PropertyField(serialized.DetailsCullMode, Styles.DetailsCullingMode);
            if (serialized.DetailsCullMode.intValue == (int)TerrainFoliageCullMode.FromTerrain)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.BeginChangeCheck();
                    float detailObjectDistance = EditorGUILayout.FloatField(Styles.DetailsCullingDistance, serialized.Terrain.detailObjectDistance);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RegisterCompleteObjectUndo(serialized.Terrain, "Change Terrain Detail Distance");
                        serialized.Terrain.detailObjectDistance = detailObjectDistance;
                    }
                }
            }

            EditorGUI.BeginChangeCheck();
            int detailResolution = EditorGUILayout.IntField(Styles.DetailResolution, serialized.TerrainData.detailResolution);
            int detailResolutionPerPatch = EditorGUILayout.IntField(Styles.DetailResolutionPerPatch, serialized.TerrainData.detailResolutionPerPatch);
            if (EditorGUI.EndChangeCheck())
            {
                detailResolution = Mathf.Clamp(detailResolution, 0, 4048);
                detailResolutionPerPatch = Mathf.Clamp(detailResolutionPerPatch, 8, 128);

                Undo.RegisterCompleteObjectUndo(serialized.InstancedTerrainFoliage, "Change Detail Resolution");
                Undo.RegisterCompleteObjectUndo(serialized.TerrainData, "Change Detail Resolution");
                serialized.TerrainData.SetDetailResolution(detailResolution, detailResolutionPerPatch);
            }

            EditorGUI.BeginChangeCheck();
            float detailObjectDensity = EditorGUILayout.Slider(Styles.DetailObjectDensity, serialized.Terrain.detailObjectDensity, 0.0f, 1.0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(serialized.InstancedTerrainFoliage, "Change Detail Object Density");
                Undo.RegisterCompleteObjectUndo(serialized.Terrain, "Change Detail Object Density");
                serialized.Terrain.detailObjectDensity = detailObjectDensity;
            }

            serialized.DisableDistanceScalingWhileEditing.value = EditorGUILayout.Toggle("Disable Distance Scaling", serialized.DisableDistanceScalingWhileEditing.value);
            InstancingSystem.DisableDensityCulling = serialized.DisableDistanceScalingWhileEditing.value;

            EditorGUILayout.Space(8);
            serialized.DetailListUI.OnGUI();
        }

        public static void DrawTreesSection(SerializedInstancedTerrainFoliage serialized, UnityEditor.Editor owner)
        {
            EditorGUILayout.Space(4);

            EditorGUILayout.PropertyField(serialized.TreesLoadMode, Styles.TreesLoadingMode);
            EditorGUILayout.PropertyField(serialized.TreesCullMode, Styles.TreesCullingMode);
            if (serialized.TreesCullMode.intValue == (int)TerrainFoliageCullMode.FromTerrain)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.BeginChangeCheck();
                    float treeDistance = EditorGUILayout.FloatField(Styles.TreesCullingDistance, serialized.Terrain.treeDistance);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RegisterCompleteObjectUndo(serialized.Terrain, "Change Terrain Tree Distance");
                        serialized.Terrain.treeDistance = treeDistance;
                    }
                }
            }

            EditorGUILayout.PropertyField(serialized.TreeGridSize, Styles.TreeGridSize);

            EditorGUILayout.Space(8);
            serialized.TreeListUI.OnGUI();
        }

        static bool ShouldShowPrototypes(SerializedInstancedTerrainFoliage serialized, UnityEditor.Editor owner)
        {
            return serialized.DetailListUI.Selected.Count > 0 ||
                   serialized.TreeListUI.Selected.Count > 0;
        }

        static void DrawPrototypes(SerializedInstancedTerrainFoliage serialized, UnityEditor.Editor owner)
        {
            serialized.PrototypePreviewUI.OnGUI(serialized.DetailListUI, serialized.TreeListUI, owner);
        }

        static class Styles
        {
            public static readonly GUIContent DetailsSection = EditorGUIUtility.TrTextContent("Details", "Settings for terrain details.");
            public static readonly GUIContent DetailResolution = EditorGUIUtility.TrTextContent("Resolution", "The resolution of the detail cells.");
            public static readonly GUIContent DetailResolutionPerPatch = EditorGUIUtility.TrTextContent("Resolution Per Patch", "The resolution of the details per cell.");
            public static readonly GUIContent DetailObjectDensity = EditorGUIUtility.TrTextContent("Density", "The overall density of the details.");
            public static readonly GUIContent DetailsCullingMode = EditorGUIUtility.TrTextContent("Culling Mode", "Determines how the details are culled.");
            public static readonly GUIContent DetailsCullingDistance = EditorGUIUtility.TrTextContent("Culling Distance", "The maximum distance at which details are rendered.");
            public static readonly GUIContent DetailsLoadingMode = EditorGUIUtility.TrTextContent("Loading Mode", "Determines how the details are loaded.");
            public static readonly GUIContent DetailsLoadDistance = EditorGUIUtility.TrTextContent("Loading Distance", "The distance at which details are loaded from the terrain.");

            public static readonly GUIContent TreesSection = EditorGUIUtility.TrTextContent("Trees", "Settings for terrain trees.");
            public static readonly GUIContent TreeGridSize = EditorGUIUtility.TrTextContent("Patches Per Edge", "The number of tree patch cells per terrain edge.");
            public static readonly GUIContent TreesCullingMode = EditorGUIUtility.TrTextContent("Culling Mode", "Determines how the trees are culled.");
            public static readonly GUIContent TreesCullingDistance = EditorGUIUtility.TrTextContent("Culling Distance", "The maximum distance at which trees are rendered.");
            public static readonly GUIContent TreesLoadingMode = EditorGUIUtility.TrTextContent("Loading Mode", "Determines how the trees are loaded.");
            public static readonly GUIContent TreesLoadDistance = EditorGUIUtility.TrTextContent("Loading Distance", "The distance at which trees are loaded from the terrain.");
        }
    }
}
