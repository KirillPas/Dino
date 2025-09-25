// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace MA.Flora.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InstancedObjectLink))]
    class InstancedObjectLinkEditor : UnityEditor.Editor
    {
        [InitializeOnLoadMethod]
        static void InitializeDeletionHook()
        {
            SceneView.duringSceneGui += OnGlobalSceneGUI;
        }

        InstancedObjectLink[] m_Links;

        SerializedProperty m_Container;
        SerializedProperty m_InstanceIndex;

        void OnEnable()
        {
            m_Container = serializedObject.FindProperty("m_Container");
            m_InstanceIndex = serializedObject.FindProperty("m_InstanceIndex");

            m_Links = targets.Cast<InstancedObjectLink>().ToArray();
            foreach (InstancedObjectLink link in m_Links)
                link.SetSelected(true);

            SceneView.duringSceneGui += OnSceneViewGUI; // Editor OnSceneGUI is not called if Gizmos are disabled
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneViewGUI;

            foreach (InstancedObjectLink link in m_Links)
                link.SetSelected(false);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(m_Container, Styles.Container);
                EditorGUILayout.PropertyField(m_InstanceIndex, Styles.InstanceIndex);
            }

            serializedObject.ApplyModifiedProperties();
        }

        [DrawGizmo(GizmoType.Pickable, typeof(InstancedObjectLink))]
        static void PickableGizmos(InstancedObjectLink link, GizmoType gizmoType)
        {
            if (link.IsLinked)
            {
                Gizmos.DrawIcon(link.transform.position, "Packages/com.ma.flora/Editor/Icons/InstancedObjectLink Icon.png", true);
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
                        if (picked != null && picked.InstanceCount > 0)
                        {
                            InstancedObjectLink pickedLink = picked.LinkedObjects[picked.ActiveInstanceIndex];
                            int indexOfPicked = Array.IndexOf(m_Links, pickedLink);
                            if (indexOfPicked != -1)
                            {
                                ToolManager.SetActiveContext<InstanceToolContext>();
                                Selection.activeObject = picked;
                                e.Use();
                            }
                        }
                    }
                    break;
            }
        }

        static HashSet<InstancedMeshContainer> s_LinkedContainers = new HashSet<InstancedMeshContainer>();

        static void OnGlobalSceneGUI(SceneView view)
        {
            Event evt = Event.current;
            if (evt.type == EventType.ExecuteCommand)
            {
                switch (evt.commandName)
                {
                    case "Delete":
                    case "SoftDelete":
                    {
                        InstancedObjectLink[] selectedLinkedObjects = Selection.GetFiltered<InstancedObjectLink>(SelectionMode.Unfiltered);
                        if (selectedLinkedObjects.Length > 0)
                        {
                            foreach (InstancedObjectLink link in selectedLinkedObjects)
                            {
                                if (link.IsLinked)
                                    s_LinkedContainers.Add(link.Container);
                            }

                            InstancedMeshContainer[] containers = s_LinkedContainers.ToArray();
                            Undo.RegisterCompleteObjectUndo(containers, "Delete Selected Linked Objects");
                        }
                        break;
                    }
                }
            }
        }

        static class Styles
        {
            public static readonly GUIContent Container = EditorGUIUtility.TrTextContent("Container", "The instanced mesh container that the instance is linked to.");
            public static readonly GUIContent InstanceIndex = EditorGUIUtility.TrTextContent("Instance Index", "The index of the instance in the container.");
        }
    }
}
