// Copyright © Magnetic Arcade. All Rights Reserved.

using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    class InstanceScaleTool : InstanceManipulationTool
    {
        static Vector3 s_CurrentScale = Vector3.one;
        
        public override GUIContent toolbarIcon => EditorGUIUtility.TrTextContentWithIcon("Scale Tool", "Scale Tool", "ScaleTool");
        
        protected override void ToolGUI(SceneView view, Vector3 handlePosition, bool isStatic)
        {
            // Allow global space scaling for multi-selection but not for a single object
            InstanceSelectionGroup[] selection = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
            quaternion handleRotation = selection.Length > 1 || selection[0].InstanceCount > 1 
                ? InstanceHandles.HandleRotation 
                : InstanceHandles.HandleLocalRotation;

            InstanceManipulator.BeginManipulationHandling(true);
            
            EditorGUI.BeginChangeCheck();
            Vector3 startScale = s_CurrentScale;
            Vector3 endScale = Handles.ScaleHandle(startScale, handlePosition, handleRotation, HandleUtility.GetHandleSize(handlePosition));
            s_CurrentScale = endScale;
            
            InstanceManipulator.EndManipulationHandling();

            if (EditorGUI.EndChangeCheck() && !isStatic)
            {
                if (!endScale.Equals(startScale))
                {
                    RecordSelection("Scale", selection);
                    InstanceManipulator.SetScaleDelta(endScale, handleRotation);
                }
            }
        }
    }
}