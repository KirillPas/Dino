// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using MA.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    abstract class InstanceManipulationTool : InstanceTool
    {
        bool m_SavedSelection;
        
        internal static readonly GUIContent StaticLabel = EditorGUIUtility.TrTextContent("Static");
        
        protected abstract void ToolGUI(SceneView view, Vector3 handlePosition, bool isStatic);

        protected override void ToolGUI(SceneView view)
        {
            if (Selection.activeObject is not InstanceSelectionGroup selectionGroup || !selectionGroup.Container)
                return;
            
#if UNITY_2022_3_OR_NEWER
            if (StageUtilityBridge.IsGizmoCulledBySceneCullingMasksOrFocusedScene(selectionGroup.Container.gameObject, Camera.current))
                return;
#endif
            
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.Layout:
                    InstanceInspectorOverlay.UpdateInspectors();
                    InstanceHandles.InvalidateHandlePosition(); // Some cases that should invalidate the cached position are not handled correctly yet so we refresh it once per frame
                    break;
                case EventType.MouseDown:
                    m_SavedSelection = false;
                    break;
            }

            bool isDisabled = ShouldToolGUIBeDisabled(out GUIContent disabledLabel);
            using (new EditorGUI.DisabledScope(isDisabled))
            {
                Vector3 handlePosition = InstanceHandles.HandlePosition;
                ToolGUI(view, handlePosition, isDisabled);
                if (isDisabled)
                    HandlesBridge.ShowSceneViewLabel(handlePosition, disabledLabel);
            }
        }
        
        protected void ResetGlobalHandleRotationIfNeeded()
        {
            if (InstanceHandles.PivotRotation == PivotRotation.Global && Event.current.GetTypeForControl(GUIUtility.hotControl) == EventType.MouseUp)
            {
                InstanceHandles.ResetGlobalHandleRotation();
            }
        }

        protected bool RecordSelection(string undoType)
        {
            if (!m_SavedSelection)
            {
                InstanceSelectionGroup[] selection = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
                return RecordSelection(undoType, selection);
            }
            
            return false;
        }

        protected bool RecordSelection(string undoType, InstanceSelectionGroup[] selection)
        {
            if (!m_SavedSelection)
            {
                m_SavedSelection = true;

                UnityObject[] undoObjects = new UnityObject[selection.Length];
                string undoName = "";
                for (int i = 0; i < selection.Length; i++)
                {
                    undoName = selection[i].name;
                    undoObjects[i] = selection[i].Container;
                }

                Undo.RegisterCompleteObjectUndo(undoObjects, $"{undoType} Selected Instances");
                if (undoObjects.Length == 1)
                    Undo.SetCurrentGroupName($"{undoType}" + undoName);
                
                return true;
            }
            
            return false;
        }
        
        protected virtual bool ShouldToolGUIBeDisabled(out GUIContent disabledLabel)
        {
            disabledLabel = StaticLabel;

            if (EditorApplication.isPlaying && !Tools.hidden)
            {
                InstanceSelectionGroup[] selectionGroups = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
                return ContainsMainStageGameObjects(selectionGroups) && ContainsStatic(selectionGroups);
            }
            
            return false;
        }

        protected static void RotateGroup(InstanceSelectionGroup group, Vector3 handlePosition, Quaternion startRotation, Vector3 axis, float angle)
        {
            foreach (int instanceIndex in group.Indices)
            {
                if (InstanceHandles.PivotMode == PivotMode.Center)
                {
                    // Rotate around handlePosition (Global or Local axis).
                    if (InstanceHandles.PivotRotation == PivotRotation.Global)
                    {
                        LocalTransform t = group.GetInstanceTransform(instanceIndex, Space.World);
                        t = t.RotateAround(handlePosition, startRotation * axis, angle * Mathf.Deg2Rad);
                        group.UpdateInstanceTransform(instanceIndex, t, Space.World);
                    }
                    else
                    {
                        LocalTransform t = group.GetInstanceTransform(instanceIndex, Space.Self);
                        t = t.RotateAround(group.Container.transform, handlePosition, axis, angle * Mathf.Deg2Rad);
                        group.UpdateInstanceTransform(instanceIndex, t, Space.Self);
                    }
                }
                else if (InstanceManipulator.IndividualSpace)
                {
                    // Local rotation (Pivot mode with Local axis).
                    LocalTransform t = group.GetInstanceTransform(instanceIndex, Space.Self);
                    t = t.Rotate(axis, angle * Mathf.Deg2Rad);
                    group.UpdateInstanceTransform(instanceIndex, t, Space.Self);
                }
                else
                {
                    // Pivot mode with Global axis.
                    LocalTransform t = group.GetInstanceTransform(instanceIndex, Space.World);
                    t = t.Rotate(startRotation * axis, angle * Mathf.Deg2Rad);
                    group.UpdateInstanceTransform(instanceIndex, t, Space.World);
                }
            }
        }
        
        internal static bool ContainsStatic(InstanceSelectionGroup[] groups)
        {
            if (groups == null || groups.Length == 0)
                return false;
            
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] != null && groups[i].Container != null && groups[i].Container.gameObject.isStatic)
                    return true;
            }
            
            return false;
        }

        internal static bool ContainsMainStageGameObjects(InstanceSelectionGroup[] groups)
        {
            if (groups == null || groups.Length == 0)
                return false;
            
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] != null && groups[i].Container != null && StageUtility.GetStageHandle(groups[i].Container.gameObject).IsMainStage())
                    return true;
            }
            
            return false;
        }
    }
}