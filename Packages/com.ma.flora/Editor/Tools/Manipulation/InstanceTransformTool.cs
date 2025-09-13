// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    class InstanceTransformTool : InstanceManipulationTool
    {
        static Vector3 s_Scale;
        
        public override GUIContent toolbarIcon => EditorGUIUtility.TrTextContentWithIcon("Transform Tool", "Transform Tool", "TransformTool");

        public override bool gridSnapEnabled => InstanceHandles.PivotRotation == PivotRotation.Global;

        protected override void ToolGUI(SceneView view, Vector3 handlePosition, bool isStatic)
        {
            if (view.camera.transform.position.Equals(handlePosition))
                return;
            
            ResetGlobalHandleRotationIfNeeded();
            
            InstanceManipulator.BeginManipulationHandling(true);
            
            EditorGUI.BeginChangeCheck();

            Vector3 startPosition = handlePosition;
            Vector3 endPosition = startPosition;
            Quaternion startRotation = InstanceHandles.HandleRotation;
            Quaternion endRotation = startRotation;
            Vector3 startScale = s_Scale;
            Vector3 endScale = startScale;
            Handles.TransformHandle(ref endPosition, ref endRotation, ref endScale);
            s_Scale = endScale;

            if (EditorGUI.EndChangeCheck() && !isStatic)
            {
                InstanceSelectionGroup[] selection = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
                RecordSelection("Transform", selection);
                
                if (InstanceManipulator.HandleHasMoved(endPosition))
                {
                    InstanceHandles.UnlockHandlePosition();
                    InstanceManipulationToolUtility.SetMinDragDifferenceForPos(handlePosition);
                    InstanceManipulator.SetPositionDelta(endPosition, InstanceManipulator.MouseDownHandlePosition);
                }

                Quaternion deltaRotation = Quaternion.Inverse(startRotation) * endRotation;
                deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);
                if (!Mathf.Approximately(angle, 0))
                {
                    foreach (InstanceSelectionGroup group in selection)
                        RotateGroup(group, handlePosition, startRotation, axis, angle);
                
                    InstanceHandles.HandleRotation = endRotation;
                }

                if (!endScale.Equals(startScale))
                    InstanceManipulator.SetScaleDelta(endScale, endRotation);
            }
            
            InstanceManipulator.EndManipulationHandling();
        }
    }
}