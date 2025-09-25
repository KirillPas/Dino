// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
     static class InstanceManipulationToolUtility
    {
       public static Vector3 MinDragDifference { get; set; }
       
        public static void SetMinDragDifferenceForPos(Vector3 position)
            => MinDragDifference = Vector3.one * (HandleUtility.GetHandleSize(position) / 80f);

        public static void SetMinDragDifferenceForPos(Vector3 position, float multiplier)
            => MinDragDifference = Vector3.one * (HandleUtility.GetHandleSize(position) * multiplier / 80f);

        public static void SetMinDragDifferenceForPos(Vector3 position, float multiplier, float max) 
            => MinDragDifference = Vector3.one * Math.Min(HandleUtility.GetHandleSize(position) * multiplier / 80f, max);

        public static void DisableMinDragDifference() 
            => MinDragDifference = Vector3.zero;

        public static void DisableMinDragDifferenceForAxis(int axis)
        {
            Vector2 diff = MinDragDifference;
            diff[axis] = 0;
            MinDragDifference = diff;
        }

        public static void DisableMinDragDifferenceBasedOnSnapping(Vector3 positionBeforeSnapping, Vector3 positionAfterSnapping)
        {
            for (int axis = 0; axis < 3; axis++)
                if (positionBeforeSnapping[axis] != positionAfterSnapping[axis])
                    DisableMinDragDifferenceForAxis(axis);
        }

        public delegate void HandleDragChangeDelegate(string handleName, bool dragging);
        public static HandleDragChangeDelegate HandleDragChange;
        
        public static void BeginDragging(string handleName) 
            => HandleDragChange?.Invoke(handleName, true);

        public static void EndDragging(string handleName) 
            => HandleDragChange?.Invoke(handleName, false);

        public static void DetectDraggingBasedOnMouseDownUp(string handleName, EventType typeBefore)
        {
            if (typeBefore == EventType.MouseDrag && Event.current.type != EventType.MouseDrag)
                BeginDragging(handleName);
            else if (typeBefore == EventType.MouseUp && Event.current.type != EventType.MouseUp)
                EndDragging(handleName);
        }
    }
}