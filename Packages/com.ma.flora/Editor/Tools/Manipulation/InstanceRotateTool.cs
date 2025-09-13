// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    class InstanceRotateTool : InstanceManipulationTool
    {
        public override GUIContent toolbarIcon => EditorGUIUtility.TrTextContentWithIcon("Rotate Instances Tool", "Rotate Instances Tool", "RotateTool");

        protected override void ToolGUI(SceneView view, Vector3 handlePosition, bool isStatic)
        {
            ResetGlobalHandleRotationIfNeeded();
            InstanceManipulator.BeginManipulationHandling(true);
            
            Quaternion before = InstanceHandles.HandleRotation;
            EditorGUI.BeginChangeCheck();
            Quaternion after = Handles.DoRotationHandle(before, handlePosition);
            
            InstanceManipulator.EndManipulationHandling();

            if (EditorGUI.EndChangeCheck() && !isStatic)
            {
                Quaternion delta = Quaternion.Inverse(before) * after;
                delta.ToAngleAxis(out float angle, out Vector3 axis);

                if (!Mathf.Approximately(angle, 0f))
                {
                    InstanceSelectionGroup[] selection = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
                    RecordSelection("Rotate", selection);
                    
                    foreach (InstanceSelectionGroup group in selection)
                        RotateGroup(group, handlePosition, before, axis, angle);
                }
                
                InstanceHandles.HandleRotation = after;
            }
        }
    }
}