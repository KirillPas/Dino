// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace MA.Flora.Editor
{
    static class SceneHierarchyIcons
    {
        const string k_ContainerIconNormalPath = "Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedMeshContainer Normal Icon.png";
        const string k_ContainerIconPrefabPath = "Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedMeshContainer Prefab Icon.png";

        const string k_LinkedObjectIconNormalPath = "Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedObjectLink Normal Icon.png";
        const string k_LinkedObjectIconBrokenPath = "Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedObjectLink Broken Icon.png";
        const string k_LinkedObjectIconPrefabPath = "Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedObjectLink Prefab Icon.png";

        const string k_InstancingSceneSettingsPath = "Packages/com.ma.flora/Editor/EditorResources/Icon/InstancingSceneSettings Normal Icon.png";

        static Texture2D s_ContainerIcon;
        static Texture2D s_ContainerPrefabIcon;

        static Texture2D s_LinkedObjectIcon;
        static Texture2D s_LinkedObjectBrokenIcon;
        static Texture2D s_LinkedObjectPrefabIcon;

        static Texture2D s_InstancingSceneSettingsIcon;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        static void OnUndoRedoPerformed()
        {
            EditorApplication.delayCall += EditorApplication.RepaintHierarchyWindow;
        }

        static void InitializeIcons()
        {
            s_ContainerIcon = EditorGUIUtilityBridge.LoadIconRequired(k_ContainerIconNormalPath);
            s_ContainerPrefabIcon = EditorGUIUtilityBridge.LoadIconRequired(k_ContainerIconPrefabPath);

            s_LinkedObjectIcon = EditorGUIUtilityBridge.LoadIconRequired(k_LinkedObjectIconNormalPath);
            s_LinkedObjectBrokenIcon = EditorGUIUtilityBridge.LoadIconRequired(k_LinkedObjectIconBrokenPath);
            s_LinkedObjectPrefabIcon = EditorGUIUtilityBridge.LoadIconRequired(k_LinkedObjectIconPrefabPath);

            s_InstancingSceneSettingsIcon = EditorGUIUtilityBridge.LoadIconRequired(k_InstancingSceneSettingsPath);
        }

        static void OnHierarchyItemGUI(int instanceId, Rect rect)
        {
            if (s_ContainerIcon == null)
                InitializeIcons();

            GameObject gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (!gameObject) return;

            EditorWindow window = SceneHierarchyBridge.GetLastHierarchyWindow();
            if (!window) return;

            TreeViewItem item = SceneHierarchyBridge.GetItem(window, instanceId);
            if (item == null) return;

            bool isPrefab = PrefabUtility.IsPartOfAnyPrefab(gameObject);
            bool isPrefabVariant = PrefabUtility.IsPartOfVariantPrefab(gameObject);

            if (gameObject.TryGetComponent(out InstancedMeshContainer _))
            {
                if (isPrefab || isPrefabVariant)
                    item.icon = s_ContainerPrefabIcon;
                else
                    item.icon = s_ContainerIcon;
            }
            else if (gameObject.TryGetComponent(out InstancedObjectLink link))
            {
                if (link.enabled)
                {
                    if (!link.IsLinked)
                    {
                        item.icon = s_LinkedObjectBrokenIcon;
                    }
                    else
                    {
                        if (isPrefab || isPrefabVariant)
                            item.icon = s_LinkedObjectPrefabIcon;
                        else
                            item.icon = s_LinkedObjectIcon;
                    }
                }
                else
                {
                    item.icon = AssetPreview.GetMiniThumbnail(gameObject);
                }
            }
            else if (gameObject.TryGetComponent(out InstancingSceneSettings _))
            {
                item.icon = s_InstancingSceneSettingsIcon;
            }
        }
    }
}
