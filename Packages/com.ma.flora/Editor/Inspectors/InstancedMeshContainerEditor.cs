// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Linq;
using MA.Core;
using MA.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InstancedMeshContainer))]
    class InstancedMeshContainerEditor : UnityEditor.Editor
    {
        InstancedMeshContainer[] m_Containers;

        void OnEnable()
        {
            m_Containers = targets.Cast<InstancedMeshContainer>().ToArray();
            foreach (InstancedMeshContainer container in m_Containers)
                container.SelectAll();

            SceneView.duringSceneGui += OnSceneViewGUI; // Editor OnSceneGUI is not called if Gizmos are disabled
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneViewGUI;

            foreach (InstancedMeshContainer container in m_Containers)
                container.ClearSelection();
        }

        public override void OnInspectorGUI()
        {
            bool hasMultipleTargets = m_Containers.Length > 1;
            bool hasDifferentPrototypes = m_Containers.Any(c => c.Prototype != m_Containers[0].Prototype);
            InstancedMeshContainer container = (InstancedMeshContainer)target;

            EditorGUI.showMixedValue = hasMultipleTargets && hasDifferentPrototypes;
            UnityObject obj = EditorGUILayout.ObjectField(Styles.Prototype, container.Prototype, typeof(GameObject), false);
            if (obj != container.Prototype && obj is GameObject gameObject)
            {
                if (!gameObject.TryGetComponent(out InstancedPrototype newPrototype))
                {
                    bool hasMesh = gameObject.TryGetComponent(out MeshFilter _);
                    bool hasRenderer = gameObject.TryGetComponent(out MeshRenderer _);
                    bool hasLODGroup = gameObject.TryGetComponent(out LODGroup _);
                    if (!hasMesh && !hasRenderer && !hasLODGroup)
                    {
                        Debug.LogError("The prototype prefab must have a MeshFilter, MeshRenderer or LODGroup component.");
                        return;
                    }

                    newPrototype = gameObject.AddComponent<InstancedPrototype>();
                }

                container.Prototype = newPrototype;
                EditorUtility.SetDirty(container);
            }
            EditorGUI.showMixedValue = false;

            using (new EditorGUI.DisabledScope(true))
            {
                int totalInstanceCount = m_Containers.Sum(c => c.InstanceCount);
                EditorGUILayout.TextField(Styles.InstanceCount, StringUtility.FormatLargeNumber(totalInstanceCount));
            }

            bool anyHasLinkedObjects = m_Containers.Any(c => c.HasLinkedObjects);
            if (anyHasLinkedObjects)
            {
                if (GUILayout.Button("Remove Linked Objects"))
                {
                    foreach (InstancedMeshContainer c in m_Containers)
                    {
                        if (!c.HasLinkedObjects)
                            continue;

                        Undo.RegisterFullObjectHierarchyUndo(container.gameObject, "Remove Linked Objects");
                        container.DetachAllLinkedObjects(true, removeInstances: false);
                    }
                }
            }
            else
            {
                if (GUILayout.Button("Add Linked Objects"))
                {
                    foreach (InstancedMeshContainer c in m_Containers)
                    {
                        if (c.HasLinkedObjects)
                            continue;

                        Undo.RegisterFullObjectHierarchyUndo(container.gameObject, "Add Linked Objects");
                        for (int i = 0; i < c.InstanceCount; i++)
                            c.InstantiateLinkedObject(i);
                    }
                }
            }
        }

        void OnSceneViewGUI(SceneView view)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && e.clickCount == 2)
                    {
                        InstanceSelectionGroup picked = InstancePickingUtility.PickInstance(e.mousePosition);
                        if (picked && picked.InstanceCount > 0 && picked.Container == target)
                        {
                            ToolManager.SetActiveContext<InstanceToolContext>();
                            Selection.activeObject = picked;
                            e.Use();
                        }
                    }
                    break;
            }
        }

        bool HasFrameBounds()
        {
            foreach (InstancedMeshContainer container in m_Containers)
            {
                if (container.Prototype && container.InstanceCount > 0)
                    return true;
            }

            return false;
        }

        Bounds OnGetFrameBounds()
        {
            AxisAlignedBox bounds = AxisAlignedBox.Empty;

            foreach (InstancedMeshContainer container in m_Containers)
            {
                if (container.Prototype && container.InstanceCount > 0)
                    bounds += container.CalculateBounds(Space.World);
            }

            return bounds;
        }

        static readonly Color k_BoundsColor =  new Color(251 / 255f, 202 / 255f, 76 / 255f, 0.5f);

        [DrawGizmo(GizmoType.Pickable, typeof(InstancedMeshContainer))]
        static void PickableGizmos(InstancedMeshContainer container, GizmoType gizmoType)
        {
            if (container.Prototype)
            {
                AxisAlignedBox bounds = container.CalculateBounds(Space.Self);
                Gizmos.DrawIcon(bounds.Center, "Packages/com.ma.flora/Editor/Icons/InstancedMeshContainer Icon.png", true);
            }
        }

        [DrawGizmo(GizmoType.Selected, typeof(InstancedMeshContainer))]
        static void SelectedGizmos(InstancedMeshContainer container, GizmoType gizmoType)
        {
            if (container.Prototype)
            {
                AxisAlignedBox bounds = container.CalculateBounds(Space.Self);
                Gizmos.color = k_BoundsColor;
                Gizmos.matrix = container.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(bounds.Center, bounds.Size);
            }
        }

        static class Styles
        {
            public static readonly GUIContent Prototype = EditorGUIUtility.TrTextContent("Prototype", "The prototype prefab that will be instanced.");
            public static readonly GUIContent InstanceCount = EditorGUIUtility.TrTextContent("Instance Count", "The number of instances in the container.");
        }
    }
}
