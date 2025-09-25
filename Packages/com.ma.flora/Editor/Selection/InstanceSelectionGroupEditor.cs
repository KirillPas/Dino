// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InstanceSelectionGroup))]
    class InstanceSelectionGroupEditor : UnityEditor.Editor
    {
        internal static InstancedPrototype[] SelectionPrototypes = Array.Empty<InstancedPrototype>();
        internal static Action PrototypesChanged;

        UnityEditor.Editor m_SelectedPrototypeEditor;

        bool m_HasLinkedObjects;
        Texture2D m_LinkedObjectIcon;
        List<UnityEditor.Editor> m_LinkedObjectComponentEditors;

        GameObject[] m_SelectedLinkedObjects = Array.Empty<GameObject>();

        void OnEnable()
        {
            m_HasLinkedObjects = false;

            foreach (UnityObject target in targets)
            {
                if (target is InstanceSelectionGroup group)
                {
                    group.Retain();
                    m_HasLinkedObjects |= group.HasLinkedObjects;
                }
            }

            SelectionPrototypes = targets
                .Cast<InstanceSelectionGroup>()
                .Where(p => !p.IsEmpty)
                .Select(p => p.Container.Prototype)
                .ToArray();
            PrototypesChanged?.Invoke();

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            if (SelectionPrototypes.Length > 0)
            {
                CreateCachedEditor(SelectionPrototypes, typeof(InstancePrototypeEditor), ref m_SelectedPrototypeEditor);
            }

            if (m_HasLinkedObjects)
            {
                m_SelectedLinkedObjects = targets
                    .Cast<InstanceSelectionGroup>()
                    .Where(p => p.HasLinkedObjects)
                    .SelectMany(p => p.LinkedObjects)
                    .Select(p => p.gameObject)
                    .ToArray();

                // Group components by type
                List<IGrouping<Type, Component>> componentGroups = m_SelectedLinkedObjects
                    .SelectMany(p => p.GetComponents<Component>())
                    .GroupBy(c => c.GetType())
                    .ToList();

                m_LinkedObjectComponentEditors = new List<UnityEditor.Editor>();

                foreach (IGrouping<Type, Component> componentGroup in componentGroups)
                {
                    Component[] components = componentGroup.ToArray();
                    UnityEditor.Editor editor = CreateEditor(components);
                    m_LinkedObjectComponentEditors.Add(editor);
                }
            }
        }

        void OnDisable()
        {
            foreach (UnityObject target in targets)
            {
                if (target is InstanceSelectionGroup group)
                    group.Release();
            }

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        bool HasFrameBounds() => target is InstanceSelectionGroup { IsEmpty: false };

        Bounds OnGetFrameBounds() => ((InstanceSelectionGroup)target).CalculateBounds(Space.World);

        void OnBeforeAssemblyReload()
        {
            // After a domain reload, it's impossible to retrieve the instance this editor is referring to. So before the domain is unloaded, we ensure that:
            // 1. The active selection or context is not an InstanceSelectionGroup, so we don't try to re-select it once the domain is reloaded.
            if (Selection.activeObject is InstanceSelectionGroup || Selection.activeContext is InstanceSelectionGroup)
                Selection.activeObject = null; // Note that changing the selection also clears the active context

            // 2. This editor no longer exists, so a locked inspector is not revived with invalid data once the domain is reloaded.
            DestroyImmediate(this);
        }

        public override void OnInspectorGUI()
        {
            if (m_LinkedObjectComponentEditors != null && m_LinkedObjectComponentEditors.Count != 0)
            {
                GUILayout.Space(-4f); // Make the first component flush with the inspector

                for (int i = 0; i < m_LinkedObjectComponentEditors.Count; i++)
                {
                    UnityEditor.Editor editor = m_LinkedObjectComponentEditors[i];
                    if (editor)
                    {
                        bool wasVisible = InternalEditorUtility.GetIsInspectorExpanded(editor);

                        // Use a layout rect with full width
                        Rect titleRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight + 4));
                        titleRect.x = 0;
                        titleRect.width = EditorGUIUtility.currentViewWidth;

                        bool isVisible = EditorGUI.InspectorTitlebar(titleRect, wasVisible, editor);
                        if (wasVisible != isVisible)
                            InternalEditorUtility.SetIsInspectorExpanded(editor, isVisible);

                        if (isVisible)
                            editor.OnInspectorGUI();
                    }
                }
            }
        }
    }

    [CustomPreview(typeof(InstanceSelectionGroup))]
    class InstanceSelectionGroupPreview : DefaultGameObjectPreview
    {
        public override bool HasPreviewGUI() => InstanceSelectionGroupEditor.SelectionPrototypes.Length > 0;

        public override void Initialize(UnityObject[] targets)
        {
            InstanceSelectionGroupEditor.PrototypesChanged += InitSelected;
            InitSelected();
        }

        public override void Cleanup()
        {
            InstanceSelectionGroupEditor.PrototypesChanged -= InitSelected;
            base.Cleanup();
        }

        void InitSelected()
        {
            ResetTarget();
            base.Initialize(InstanceSelectionGroupEditor.SelectionPrototypes.Select(p => p.gameObject).ToArray());
        }
    }
}
