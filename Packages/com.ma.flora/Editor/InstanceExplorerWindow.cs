// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MA.Flora.Editor
{
    [EditorWindowTitle(title = "Instance Explorer", icon = "Packages/com.ma.flora/Editor/EditorResources/Icon/InstancePrototype Icon.png")]
    class InstanceExplorerWindow : EditorWindow
    {
        [MenuItem("Flora/Instance Explorer", priority = 5)]
        static void CreateLightingExplorerWindow()
        {
            InstanceExplorerWindow window = GetWindow<InstanceExplorerWindow>();
            window.minSize = new Vector2(500, 250);
            window.Show();
        }
        
        // --- Instance Info ---
        
        struct InstancedMeshContainerInfo
        {
            public InstancedMeshContainer Container;
            public int InstanceCount;
            public bool IsPrefab;
            public Object PrefabRoot;

            public InstancedMeshContainerInfo(InstancedMeshContainer container, bool isPrefab, Object prefabRoot)
            {
                Container = container;
                InstanceCount = container.InstanceCount;
                IsPrefab = isPrefab;
                PrefabRoot = prefabRoot;
            }
        }

        struct InstancedTerrainFoliageInfo
        {
            public InstancedTerrainFoliage Terrain;
            public bool IsPrefab;
            public Object PrefabRoot;
            
            public InstancedTerrainFoliageInfo(InstancedTerrainFoliage terrain, bool isPrefab, Object prefabRoot)
            {
                Terrain = terrain;
                IsPrefab = isPrefab;
                PrefabRoot = prefabRoot;
            }
        }
        
        // --- Fields ---
        
        InstanceTableView[] m_TableTabs;
        GUIContent[] m_TabTitles;
        int m_SelectedTab;
        
        Dictionary<InstancedMeshContainer, InstancedMeshContainerInfo> m_ContainerInfoPairs = new Dictionary<InstancedMeshContainer, InstancedMeshContainerInfo>();
        Dictionary<InstancedTerrainFoliage, InstancedTerrainFoliageInfo> m_TerrainInfoPairs = new Dictionary<InstancedTerrainFoliage, InstancedTerrainFoliageInfo>();
        
        // --- Unity Events ---
        
        void OnEnable()
        {
            m_TableTabs = new[]
            {
                new InstanceTableView("Containers", GetInstancedMeshContainers, GetInstancedMeshContainerColumns, true),
                // new InstanceTableView("Terrains", GetInstanceTerrains, GetInstanceTerrainColumns, true),
            };
            
            EditorApplication.searchChanged += Repaint;
            Repaint();
        }

        void OnDisable()
        {
            for (int i = 0; i < m_TableTabs.Length; i++)
                m_TableTabs[i].OnDisable();
            
            EditorApplication.searchChanged -= Repaint;
        }

        void OnInspectorUpdate()
        {
            for (int i = 0; i < m_TableTabs.Length; i++)
                m_TableTabs[i].OnInspectorUpdate();
        }

        void OnSelectionChange()
        {
            for (int i = 0; i < m_TableTabs.Length; i++)
                m_TableTabs[i].OnSelectionChange();
            
            Repaint();
        }

        void OnHierarchyChange()
        {
            for (int i = 0; i < m_TableTabs.Length; i++)
                m_TableTabs[i].OnHierarchyChange();
            
            Repaint();
        }

        void OnGUI()
        {
            UpdateTabs();

            EditorGUIUtility.labelWidth = 130;

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            GUILayout.FlexibleSpace();
            if (m_TabTitles != null)
                m_SelectedTab = GUILayout.Toolbar(m_SelectedTab, m_TabTitles, "LargeButton", GUI.ToolbarButtonSize.FitToContents);
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (m_TableTabs != null && m_SelectedTab >= 0 && m_SelectedTab < m_TableTabs.Length)
                m_TableTabs[m_SelectedTab].OnGUI();
            EditorGUILayout.Space();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }
        
        void UpdateTabs()
        {
            m_TabTitles = m_TableTabs?.Select(item => item.Title).ToArray();
        }
        
        // --- Instance Renderers ---

        Object[] GetInstancedMeshContainers()
        {
            InstancedMeshContainer[] containers = GetObjectsForExplorer<InstancedMeshContainer>().ToArray();
            
            foreach (InstancedMeshContainer container in containers)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(container) != null) // We have a prefab
                {
                    m_ContainerInfoPairs[container] = new InstancedMeshContainerInfo(container, true, PrefabUtility.GetCorrespondingObjectFromSource(PrefabUtility.GetOutermostPrefabInstanceRoot(container.gameObject)));
                }
                else
                {
                    m_ContainerInfoPairs[container] = new InstancedMeshContainerInfo(container, false, null);
                }
            }
            
            return containers.Where(o => o != null).Cast<Object>().ToArray();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TryGetTerrainData(SerializedProperty prop, out InstancedTerrainFoliageInfo terrainFoliageInfo)
        {
            InstancedTerrainFoliage container = prop.serializedObject.targetObject as InstancedTerrainFoliage;

            if (container == null || !m_TerrainInfoPairs.TryGetValue(container, out terrainFoliageInfo))
            {
                terrainFoliageInfo = default;
                return false;
            }

            return container != null;
        }
        
        LightingExplorerTableColumn[] GetInstancedMeshContainerColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, new GUIContent("Enabled"), "m_Enabled", 60), 
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, new GUIContent("Object"), "m_Prototype", 200,
                    (r, prop, dep) =>
                    {
                        if (!TryGetRendererData(prop, out InstancedMeshContainerInfo info))
                            return;

                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUI.ObjectField(r, info.Container, typeof(InstancedPrototype), true);
                        }
                    }, 
                    (lprop, rprop) =>
                    {
                        TryGetRendererData(lprop, out InstancedMeshContainerInfo linfo);
                        TryGetRendererData(rprop, out InstancedMeshContainerInfo rinfo);

                        GameObject lgo = linfo.Container.gameObject;
                        GameObject rgo = rinfo.Container.gameObject;
                        
                        if (IsNullComparison(lgo, rgo, out int order))
                            return order;

                        return EditorUtility.NaturalCompare(lgo.name, rgo.name);
                    }),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Custom, new GUIContent("Prototype"), "m_Prototype", 200,
                    (r, prop, dep) =>
                    {
                        if (!TryGetRendererData(prop, out InstancedMeshContainerInfo info))
                            return;

                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUI.ObjectField(r, info.Container.Prototype.gameObject, typeof(GameObject), false);
                        }
                    }, 
                    (lprop, rprop) =>
                    {
                        TryGetRendererData(lprop, out InstancedMeshContainerInfo linfo);
                        TryGetRendererData(rprop, out InstancedMeshContainerInfo rinfo);

                        GameObject lgo = linfo.Container.Prototype.gameObject;
                        GameObject rgo = rinfo.Container.Prototype.gameObject;
                        
                        if (IsNullComparison(lgo, rgo, out int order))
                            return order;

                        return EditorUtility.NaturalCompare(lgo.name, rgo.name);
                    }),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Custom, new GUIContent("Instance Count"), "m_Prototype", 120,
                    (r, prop, dep) =>
                    {
                        if (!TryGetRendererData(prop, out InstancedMeshContainerInfo containerData))
                            return;

                        EditorGUI.LabelField(r, containerData.InstanceCount.ToString());
                    }, 
                    (lprop, rprop) =>
                    {
                        TryGetRendererData(lprop, out InstancedMeshContainerInfo linfo);
                        TryGetRendererData(rprop, out InstancedMeshContainerInfo rinfo);
                        
                        return linfo.InstanceCount.CompareTo(rinfo.InstanceCount);
                    }),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Custom, new GUIContent("Prefab"), "m_Prototype", 100,
                    (r, prop, dep) =>
                    {
                        if (!TryGetRendererData(prop, out InstancedMeshContainerInfo info))
                            return;
                        
                        if (info.IsPrefab)
                        {
                            using (new EditorGUI.DisabledScope(true))
                            {
                                EditorGUI.ObjectField(r, info.PrefabRoot, typeof(GameObject), false);
                            }
                        }
                    }, 
                    (lprop, rprop) =>
                    {
                        TryGetRendererData(lprop, out InstancedMeshContainerInfo linfo);
                        TryGetRendererData(rprop, out InstancedMeshContainerInfo rinfo);
                        
                        if (IsNullComparison(linfo.PrefabRoot, rinfo.PrefabRoot, out int order))
                            return order;

                        return EditorUtility.NaturalCompare(linfo.PrefabRoot.name, rinfo.PrefabRoot.name);
                    }),
            };
        }
        
        // --- Instance Terrains ---

        Object[] GetInstanceTerrains()
        {
            InstancedTerrainFoliage[] terrains = GetObjectsForExplorer<InstancedTerrainFoliage>().ToArray();
            
            foreach (InstancedTerrainFoliage terrain in terrains)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(terrain) != null) // We have a prefab
                {
                    m_TerrainInfoPairs[terrain] = new InstancedTerrainFoliageInfo(terrain, true, PrefabUtility.GetCorrespondingObjectFromSource(PrefabUtility.GetOutermostPrefabInstanceRoot(terrain.gameObject)));
                }
                else
                {
                    m_TerrainInfoPairs[terrain] = new InstancedTerrainFoliageInfo(terrain, false, null);
                }
            }
            
            return terrains.Where(o => o != null).Cast<Object>().ToArray();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TryGetRendererData(SerializedProperty prop, out InstancedMeshContainerInfo instancedMeshContainerInfo)
        {
            InstancedMeshContainer container = prop.serializedObject.targetObject as InstancedMeshContainer;

            if (container == null || !m_ContainerInfoPairs.TryGetValue(container, out instancedMeshContainerInfo))
            {
                instancedMeshContainerInfo = default;
                return false;
            }

            return container != null;
        }
        
        LightingExplorerTableColumn[] GetInstanceTerrainColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, new GUIContent("Enabled"), "m_Enabled", 60), 
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, new GUIContent("Name"), null, 200),
            };
        }
        
        // --- Utility ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static System.Collections.Generic.IEnumerable<T> GetObjectsForExplorer<T>() where T : UnityEngine.Component
        {
            IEnumerable<T> objects = Resources.FindObjectsOfTypeAll<T>()
                .Where(obj => !EditorUtility.IsPersistent(obj) && !obj.hideFlags.HasFlag(HideFlags.HideInHierarchy) && !obj.hideFlags.HasFlag(HideFlags.HideAndDontSave));

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            // No prefab mode.
            if (prefabStage == null)
            {
                // Return all object instances in the scene including prefab instances, but not those that are in prefab assets.
                return objects;
            }
            // In Context prefab mode with Normal rendering mode
            else if (StageNavigationManagerBridge.IsContextModeNormal(prefabStage))
            {
                // Return all object instances in the scene and objects in the opened prefab asset, but not objects in the opened prefab instance.
                return objects.Where(obj => !StageUtilityBridge.IsPrefabInstanceHiddenForInContextEditing(obj.gameObject));
            }
            // All remaining cases, e.g. In Context with Hidden or GrayedOut rendering mode, or In Isolation prefab mode.
            else
            {
                // Return only objects in the opened prefab asset.
                return objects.Where(EditorSceneManager.IsPreviewSceneObject);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsNullComparison<T>(T l, T r, out int order)
        {
            if (l == null)
            {
                order = r == null ? 0 : -1;
                return true;
            }
            else if (r == null)
            {
                order = 1;
                return true;
            }

            order = 0;
            return false;
        }
    }
}