// Copyright © Magnetic Arcade. All Rights Reserved.

using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    class InstanceMoveTool : InstanceManipulationTool
    {
        public override GUIContent toolbarIcon => EditorGUIUtility.TrTextContentWithIcon("Instance Move Tool", "Move Instances", "MoveTool");

        public override bool gridSnapEnabled => Tools.pivotRotation == PivotRotation.Global;

        protected override void ToolGUI(SceneView view, Vector3 handlePosition, bool isStatic)
        {
            if (view.camera.transform.position.Equals(handlePosition))
                return;
            
            InstanceSelectionGroup[] selection = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
            InstanceManipulator.BeginManipulationHandling(false);

            EditorGUI.BeginChangeCheck();
            float3 positionHandle = Handles.DoPositionHandle(handlePosition, InstanceManipulator.MouseDownHandleRotation);
            if (EditorGUI.EndChangeCheck() && !isStatic && InstanceManipulator.HandleHasMoved(positionHandle))
            {
                if (RecordSelection("Move", selection))
                {
                    foreach (InstanceSelectionGroup group in selection)
                        group.BeginBatchMove();
                }
                
                InstanceManipulationToolUtility.SetMinDragDifferenceForPos(handlePosition);

                // if (Tools.vertexDragging)
                    // ManipulationToolUtility.DisableMinDragDifference();

                InstanceManipulator.SetPositionDelta(positionHandle, InstanceManipulator.MouseDownHandlePosition);
            }

            InstanceManipulator.EndManipulationHandling();
            
            if (Event.current.type == EventType.MouseUp)
            {
                foreach (InstanceSelectionGroup group in selection)
                    group.EndBatchMove();
            }
        }
    }
}