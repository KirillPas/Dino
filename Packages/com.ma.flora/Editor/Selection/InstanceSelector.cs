// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    class InstanceSelector
    {
        enum SelectionType { Normal, Additive, Subtractive }
        
        Vector2 m_SelectMousePoint;
        Vector2 m_StartPoint;
        
        SelectionType m_CurrentSelectionType;
        
        InstanceSelectionGroup[] m_SelectionStart;
        InstanceSelectionGroup[] m_CurrentSelection;
        
        bool m_IsNearestControl;
        int m_RectSelectionID;
        SceneView m_View;
        
        const string k_ModifierKeysChanged = "ModifierKeysChanged";
        const string k_PickCommandName = "InstancePick";
        const string k_StartRectSelectionCommandName = "StartInstanceRectSelection";

        public void Register()
        {
            m_CurrentSelectionType = SelectionType.Normal;
            EditorApplication.modifierKeysChanged += SendCommandsOnModifierKeys;
        }
        
        public void Unregister()
        {
            EditorApplication.modifierKeysChanged -= SendCommandsOnModifierKeys;
            CompleteRectSelection();
        }
        
        void SendCommandsOnModifierKeys()
        {
            // When rect selecting, we update the selected objects based on which modifier keys are currently held down, so the window needs to repaint
            if (m_View) m_View.SendEvent(EditorGUIUtility.CommandEvent(k_ModifierKeysChanged));
        }
        
        public void OnGUI(SceneView view)
        {
            Event evt = Event.current;
            HandleSelectionCommands(view, evt);
            
            m_View = view;
            m_RectSelectionID = GUIUtility.GetControlID(FocusType.Passive);

            Handles.BeginGUI();

            switch (evt.GetTypeForControl(m_RectSelectionID))
            {
                case EventType.Layout:
                case EventType.MouseMove:
                    if (!InstanceHandles.ViewToolActive)
                        HandleUtility.AddDefaultControl(m_RectSelectionID);
                    break;
                case EventType.MouseDown:
                    if (evt.button == 0 && !evt.alt)
                    {
                        HandleMouseDown(evt);
                    
                        if (m_IsNearestControl)
                            DelayPicking(view, evt);
                    }
                    break;
                case EventType.MouseUp:
                    HandleMouseUp();
                    break;
                case EventType.MouseDrag:
                    if (m_IsNearestControl && evt.button == 0 && (GUIUtility.hotControl == 0 || GUIUtility.hotControl == m_RectSelectionID))
                        StartRectSelection(view, evt);
                    
                    if (GUIUtility.hotControl == m_RectSelectionID && m_IsNearestControl)
                    {
                        m_SelectMousePoint = evt.mousePosition;
                        InstanceSelectionGroup[] rectObjs = InstancePickingUtility.PickRectInstances(view, m_StartPoint, m_SelectMousePoint);
                        m_CurrentSelection = rectObjs;
                        UpdateSelection(m_SelectionStart, rectObjs, m_CurrentSelectionType, true);
                        evt.Use();
                    }
                    break;
                case EventType.KeyDown: // Escape
                    if (evt.keyCode == KeyCode.Escape && GUIUtility.hotControl == m_RectSelectionID)
                    {
                        CompleteRectSelection();
                        GUIUtility.hotControl = 0;
                        Selection.objects = m_SelectionStart;
                        HandleMouseUp();
                    }
                    break;
                case EventType.Repaint:
                    if (GUIUtility.hotControl == m_RectSelectionID && m_IsNearestControl && m_StartPoint != m_SelectMousePoint)
                        EditorStyles.selectionRect.Draw(InstancePickingUtility.FromToRect(m_StartPoint, m_SelectMousePoint),
                            GUIContent.none, false, false, false, false);
                    break;
                case EventType.ExecuteCommand:
                    switch (evt.commandName)
                    {
                        case k_ModifierKeysChanged:
                        {
                            UpdateSelectionType(evt);
                            if (m_RectSelectionID == GUIUtility.hotControl && !InstanceHandles.ViewToolActive)
                                UpdateSelection(m_SelectionStart, m_CurrentSelection, SelectionType.Normal, true);
                        
                            evt.Use();
                            break;
                        }
                        case k_StartRectSelectionCommandName:
                            GUIUtility.hotControl = m_RectSelectionID;
                            evt.Use();
                            break;
                        case k_PickCommandName when m_IsNearestControl:
                            if (!InstanceHandles.ViewToolActive)
                                Pick(m_CurrentSelectionType, evt.mousePosition);
                            evt.Use();
                            break;
                    }
                    break;
            }

            Handles.EndGUI();
        }
        
        // --- Mouse Handling ---
        
        void UpdateSelectionType(Event evt)
        {
            if (evt.shift)
                m_CurrentSelectionType = SelectionType.Additive;
            else if (EditorGUI.actionKey)
                m_CurrentSelectionType = SelectionType.Subtractive;
            else
                m_CurrentSelectionType = SelectionType.Normal;
        }

        void HandleMouseDown(Event evt)
        {
            if (m_IsNearestControl)
                m_IsNearestControl = false;

            if (GUIUtility.hotControl == 0 && HandleUtility.nearestControl == m_RectSelectionID)
            {
                m_StartPoint = evt.mousePosition;
                m_SelectMousePoint = m_StartPoint;
                m_IsNearestControl = true;
            }

            m_SelectionStart = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
            m_CurrentSelection = null;
        }

        void HandleMouseUp()
        {
            if (GUIUtility.hotControl == m_RectSelectionID)
            {
                CompleteRectSelection();
                m_IsNearestControl = false;
                GUIUtility.hotControl = 0;
            }
        }
        
        // --- Command Handling ---

        void HandleSelectionCommands(SceneView view, Event evt)
        {
            if (evt.type == EventType.ValidateCommand)
            {
                switch (evt.commandName)
                {
                    case "Delete":
                    case "SoftDelete":
                    case "FrameSelected":
                        evt.Use();
                        break;
                }
            }
            else if (evt.type == EventType.ExecuteCommand)
            {
                switch (evt.commandName)
                {
                    case "Delete":
                    case "SoftDelete":
                        DeleteSelected();
                        evt.Use();
                        break;
                    case "FrameSelected":
                        FrameSelected(view);
                        evt.Use();
                        break;
                }
            }
        }

        static void FrameSelected(SceneView view)
        {
            AxisAlignedBox selectedBounds = InstanceSelectionGroup.GetSelectedBounds();
            float newSize = math.length(selectedBounds.Size);
            if (!float.IsInfinity(newSize))
            {
                view.Frame(selectedBounds, false);
            }
        }

        static void DeleteSelected()
        {
            InstanceSelectionGroup[] selection = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
            if (selection.Length > 0)
            {
                Undo.RegisterCompleteObjectUndo(Selection.objects, "Delete Selected Instances");
                
                foreach (InstanceSelectionGroup group in selection)
                {
                    Undo.RegisterCompleteObjectUndo(group.Container, "Delete Instances");
                    group.Container.RemoveInstances(group.Indices);
                }
                
                Selection.objects = Array.Empty<UnityEngine.Object>();
            }
        }
        
        // --- Picking ---
        
        void DelayPicking(SceneView view, Event evt)
        {
            UpdateSelectionType(evt);
            view.SendEvent(EditorGUIUtility.CommandEvent(k_PickCommandName));
        }
        
        void Pick(SelectionType selectionType, Vector2 mousePos)
        {
            if (selectionType is SelectionType.Subtractive or SelectionType.Additive)
            {
                // For shift, we check if EXACTLY the active GO is hovered by mouse and then subtract. Otherwise additive.
                // For control/cmd, we check if ANY of the selected GO is hovered by mouse and then subtract. Otherwise additive.
                // Control/cmd takes priority over shift.
                InstanceSelectionGroup hovered = InstancePickingUtility.PickInstance(mousePos);
                if (hovered)
                {
                    bool handledIt = false;

                    // shift-click deselects only if the active GO is exactly what we clicked on
                    InstanceSelectionGroup active = Selection.activeObject as InstanceSelectionGroup;
                    if (selectionType != SelectionType.Subtractive && active && active.Container == hovered.Container && active.Indices.Contains(hovered.Indices[0]))
                    {
                        UpdateSelection(m_SelectionStart, hovered, SelectionType.Subtractive, false);
                        handledIt = true;
                    }

                    // ctrl-click deselects everything up to prefab root, that is already selected
                    if (!handledIt && selectionType == SelectionType.Subtractive)
                    {
                        UpdateSelection(m_SelectionStart, hovered, SelectionType.Subtractive, false);
                        handledIt = true;
                    }

                    // we did not deselect anything, so add the new thing into selection instead
                    if (!handledIt)
                    {
                        InstanceSelectionGroup picked = InstancePickingUtility.PickInstance(m_SelectMousePoint);
                        UpdateSelection(m_SelectionStart, picked, SelectionType.Additive, false);
                    }
                }
            }
            else // With no modifier keys, we do the "cycle through overlapped" picking logic in SceneViewPicking.cs
            {
                InstanceSelectionGroup picked = InstancePickingUtility.PickInstance(m_SelectMousePoint);
                UpdateSelection(m_SelectionStart, picked, SelectionType.Normal, false);
            }
        }
        
        // --- Rect Selection ---

        void CompleteRectSelection()
        {
            m_SelectionStart = Array.Empty<InstanceSelectionGroup>();
        }
        
        void StartRectSelection(SceneView view, Event evt)
        {
            UpdateSelectionType(evt);
            // The hot control needs to be set in an OnGUI call.
            view.SendEvent(EditorGUIUtility.CommandEvent(k_StartRectSelectionCommandName));
            // This is needed to update the selection in case the modifier keys changed.
            UpdateSelection(m_SelectionStart, m_CurrentSelection, m_CurrentSelectionType, true);
        }

        static void UpdateSelection(InstanceSelectionGroup[] existingSelection, InstanceSelectionGroup newGroup, SelectionType type, bool isRectSelection)
        {
            InstanceSelectionGroup[] objs;
            if (newGroup == null)
            {
                objs = Array.Empty<InstanceSelectionGroup>();
            }
            else
            {
                objs = new InstanceSelectionGroup[1];
                objs[0] = newGroup;
            }

            UpdateSelection(existingSelection, objs, type, isRectSelection);
        }
        
        // --- Selection Update ---
        
        static Dictionary<InstancedMeshContainer, HashSet<int>> s_GroupHash = new Dictionary<InstancedMeshContainer, HashSet<int>>();
        
        static void UpdateSelection(InstanceSelectionGroup[] existingSelection, InstanceSelectionGroup[] newGroups, SelectionType type, bool isRectSelection)
        {
            if (existingSelection == null || newGroups == null)
                return;

            InstanceSelectionGroup[] newSelection;

            switch (type)
            {
                case SelectionType.Additive:
                    if (newGroups.Length > 0)
                    {
                        s_GroupHash.Clear();
                        
                        foreach (InstanceSelectionGroup group in existingSelection)
                        {
                            if (!s_GroupHash.ContainsKey(group.Container))
                                s_GroupHash.Add(group.Container, new HashSet<int>(group.Indices));
                            else
                                s_GroupHash[group.Container].UnionWith(group.Indices);
                        }
                        
                        foreach (InstanceSelectionGroup group in newGroups)
                        {
                            if (!s_GroupHash.ContainsKey(group.Container))
                                s_GroupHash.Add(group.Container, new HashSet<int>(group.Indices));
                            else
                                s_GroupHash[group.Container].UnionWith(group.Indices);
                        }
                        
                        newSelection = s_GroupHash.Select(pair => InstanceSelectionGroup.Create(pair.Key, pair.Value.ToArray())).ToArray();
                        Selection.objects = newSelection;
                    }
                    else
                    {
                        Selection.objects = existingSelection;
                    }
                    break;

                case SelectionType.Subtractive:
                    if (newGroups.Length > 0)
                    {
                        s_GroupHash.Clear();
                        
                        foreach (InstanceSelectionGroup group in existingSelection)
                        {
                            if (!s_GroupHash.ContainsKey(group.Container))
                                s_GroupHash.Add(group.Container, new HashSet<int>(group.Indices));
                            else
                                s_GroupHash[group.Container].UnionWith(group.Indices);
                        }
                        
                        foreach (InstanceSelectionGroup group in newGroups)
                        {
                            if (s_GroupHash.ContainsKey(group.Container))
                                s_GroupHash[group.Container].ExceptWith(group.Indices);
                        }
                        
                        newSelection = s_GroupHash.Select(pair => InstanceSelectionGroup.Create(pair.Key, pair.Value.ToArray())).ToArray();
                        Selection.objects = newSelection;
                    }
                    else
                    {
                        Selection.objects = existingSelection;
                    }
                    break;

                case SelectionType.Normal:
                default:
                    Selection.objects = newGroups;
                    break;
            }
        }
    }
}