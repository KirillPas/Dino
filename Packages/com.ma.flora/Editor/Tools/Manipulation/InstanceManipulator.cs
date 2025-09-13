// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Linq;
using System.Runtime.CompilerServices;
using MA.Core.Editor.Bridge;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    static class InstanceManipulator
    {
        struct TransformData
        {
            public static readonly Quaternion[] Alignments = new[]
            {
                Quaternion.LookRotation(Vector3.right, Vector3.up),
                Quaternion.LookRotation(Vector3.right, Vector3.forward),
                Quaternion.LookRotation(Vector3.up, Vector3.forward),
                Quaternion.LookRotation(Vector3.up, Vector3.right),
                Quaternion.LookRotation(Vector3.forward, Vector3.right),
                Quaternion.LookRotation(Vector3.forward, Vector3.up)
            };

            public InstanceSelectionGroup SelectionGroup;
            public int InstanceIndex;
            public Transform TransformParent;
            public Vector3 Position;
            public Vector3 CurrentPosition => SelectionGroup.GetInstancePosition(InstanceIndex, Space.World);
            public Vector3 LocalPosition;
            public Quaternion Rotation;
            public Vector3 LocalScale;
            public Vector2 SizeDelta;

            public static TransformData GetData(InstanceSelectionGroup t, int instanceIndex)
            {
                TransformData data = new TransformData();
                data.SetupTransformValues(t, instanceIndex);
                return data;
            }
            
            static Quaternion GetRefAlignment(Quaternion targetRotation, Quaternion ownRotation)
            {
                float biggestDot = Mathf.NegativeInfinity;
                Quaternion refAlignment = Quaternion.identity;
                for (int i = 0; i < Alignments.Length; i++)
                {
                    float dot = Mathf.Min(
                        Mathf.Abs(Vector3.Dot(targetRotation * Vector3.right, ownRotation * Alignments[i] * Vector3.right)),
                        Mathf.Abs(Vector3.Dot(targetRotation * Vector3.up, ownRotation * Alignments[i] * Vector3.up)),
                        Mathf.Abs(Vector3.Dot(targetRotation * Vector3.forward, ownRotation * Alignments[i] * Vector3.forward)));
                    
                    if (dot > biggestDot)
                    {
                        biggestDot = dot;
                        refAlignment = Alignments[i];
                    }
                }
                return refAlignment;
            }

            void SetupTransformValues(InstanceSelectionGroup group, int instanceIndex)
            {
                SelectionGroup = group;
                InstanceIndex = instanceIndex;
                TransformParent = group.Container.transform;
                Position = group.GetInstancePosition(InstanceIndex, Space.World);
                Rotation = group.GetInstanceRotation(InstanceIndex, Space.World);
                LocalPosition = group.GetInstancePosition(InstanceIndex, Space.Self);
                LocalScale = group.GetInstanceScale(InstanceIndex, Space.Self);
            }

            void UpdateTransformValues()
            {
                TransformParent = SelectionGroup.Container.transform;
                LocalPosition = TransformParent != null ? TransformParent.InverseTransformPoint(Position) : Position;
            }

            void SetScaleValue(Vector3 scale)
            {
                SelectionGroup.UpdateInstanceScale(InstanceIndex, scale, Space.Self);
            }

            public void SetScaleDelta(Vector3 scaleDelta, Vector3 scalePivot, Quaternion scaleRotation)
            {
                SetPosition(scaleRotation * Vector3.Scale(Quaternion.Inverse(scaleRotation) * (Position - scalePivot), scaleDelta) + scalePivot);

                Vector3 minDifference = InstanceManipulationToolUtility.MinDragDifference;
                if (TransformParent != null)
                {
                    minDifference.x /= TransformParent.lossyScale.x;
                    minDifference.y /= TransformParent.lossyScale.y;
                    minDifference.z /= TransformParent.lossyScale.z;
                }

                Quaternion ownRotation = Rotation;
                Quaternion refAlignment = GetRefAlignment(scaleRotation, ownRotation);
                scaleDelta = refAlignment * scaleDelta;
                scaleDelta = Vector3.Scale(scaleDelta, refAlignment * Vector3.one);

                scaleDelta.x = InstanceHandlesUtility.RoundBasedOnMinimumDifference(scaleDelta.x, minDifference.x);
                scaleDelta.y = InstanceHandlesUtility.RoundBasedOnMinimumDifference(scaleDelta.y, minDifference.y);
                scaleDelta.z = InstanceHandlesUtility.RoundBasedOnMinimumDifference(scaleDelta.z, minDifference.z);
                SetScaleValue(Vector3.Scale(LocalScale, scaleDelta));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void SetPosition(Vector3 newPosition) => SetPositionDelta(newPosition - Position, true);

            public void SetPositionDelta(Vector3 positionDelta, bool applySmartRounding)
            {
                if (SelectionGroup.Container.transform != TransformParent)
                    UpdateTransformValues();

                Vector3 localPositionDelta = positionDelta;
                if (TransformParent != null)
                {
                    localPositionDelta = TransformParent.InverseTransformVector(localPositionDelta);

                    if (!applySmartRounding)
                        applySmartRounding = !TransformParent.localRotation.Equals(quaternion.identity);
                }

                // If we are snapping, disable the smart rounding. If not the case, the transform will have the wrong snap value based on distance to screen.
                applySmartRounding &= !(EditorSnapSettingsBridge.incrementalSnapActive || EditorSnapSettingsBridge.gridSnapActive || EditorSnapSettingsBridge.vertexSnapActive);

                bool zeroXDelta = false;
                bool zeroYDelta = false;
                bool zeroZDelta = false;
                Vector3 minDifference = InstanceManipulationToolUtility.MinDragDifference;
                if (applySmartRounding)
                {
                    // For zero delta, we don't want to change the value so we ignore rounding
                    zeroXDelta = MathUtility.NearlyEquals(localPositionDelta.x, 0f);
                    zeroYDelta = MathUtility.NearlyEquals(localPositionDelta.y, 0f);
                    zeroZDelta = MathUtility.NearlyEquals(localPositionDelta.z, 0f);

                    if (TransformParent != null)
                    {
                        minDifference.x /= TransformParent.lossyScale.x;
                        minDifference.y /= TransformParent.lossyScale.y;
                        minDifference.z /= TransformParent.lossyScale.z;
                    }
                }

                Vector3 newLocalPosition = LocalPosition + localPositionDelta;

                if (applySmartRounding)
                {
                    newLocalPosition.x = zeroXDelta ? LocalPosition.x : InstanceHandlesUtility.RoundBasedOnMinimumDifference(newLocalPosition.x, minDifference.x);
                    newLocalPosition.y = zeroYDelta ? LocalPosition.y : InstanceHandlesUtility.RoundBasedOnMinimumDifference(newLocalPosition.y, minDifference.y);
                    newLocalPosition.z = zeroZDelta ? LocalPosition.z : InstanceHandlesUtility.RoundBasedOnMinimumDifference(newLocalPosition.z, minDifference.z);
                }

                SelectionGroup.UpdateInstancePosition(InstanceIndex, newLocalPosition, Space.Self);
            }
        }

        static EventType s_EventTypeBefore = EventType.Ignore;
        static TransformData[] s_MouseDownState;
        static Vector3 s_StartHandlePosition = Vector3.zero;
        static Vector3 s_PreviousHandlePosition = Vector3.zero;
        static Quaternion s_StartHandleRotation = Quaternion.identity;
        public static Vector3 MouseDownHandlePosition => s_StartHandlePosition;
        public static Quaternion MouseDownHandleRotation { get => s_StartHandleRotation; set => s_StartHandleRotation = value; }
        static Vector3 s_StartLocalHandleOffset = Vector3.zero;
        static int s_HotControl;
        static bool s_LockHandle;

        public static bool Active => s_MouseDownState != null;
        public static bool IndividualSpace => InstanceHandles.PivotRotation == PivotRotation.Local && InstanceHandles.PivotMode == PivotMode.Pivot;

        static void BeginEventCheck()
        {
            EventType previousEvent = s_EventTypeBefore;
            s_EventTypeBefore = Event.current.GetTypeForControl(s_HotControl);
            if (!Active || (previousEvent != EventType.MouseDown && s_EventTypeBefore == EventType.MouseDown))
                s_StartHandleRotation = InstanceHandles.HandleRotation;
        }

        static EventType EndEventCheck()
        {
            EventType usedEvent = (s_EventTypeBefore != Event.current.GetTypeForControl(s_HotControl) ? s_EventTypeBefore : EventType.Ignore);
            s_EventTypeBefore = EventType.Ignore;
            if (usedEvent == EventType.MouseDown)
                s_HotControl = GUIUtility.hotControl;
            else if (usedEvent == EventType.MouseUp)
                s_HotControl = 0;
            return usedEvent;
        }

        public static void BeginManipulationHandling(bool lockHandleWhileDragging)
        {
            BeginEventCheck();
            s_LockHandle = lockHandleWhileDragging;
            InstancingSystem.DisableAutoBuildTrees = true; // Disable culling trees while manipulating instances
        }

        public static EventType EndManipulationHandling()
        {
            EventType usedEvent = EndEventCheck();

            if (usedEvent == EventType.MouseDown)
            {
                RecordMouseDownState(Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered));
                s_StartHandlePosition = InstanceHandles.HandlePosition;
                s_PreviousHandlePosition = s_StartHandlePosition;
                s_StartLocalHandleOffset = InstanceHandles.LocalHandleOffset;
                if (s_LockHandle)
                    InstanceHandles.LockHandlePosition();
            }
            else if (s_MouseDownState != null && (usedEvent == EventType.MouseUp || GUIUtility.hotControl != s_HotControl))
            {
                s_StartHandleRotation = InstanceHandles.HandleRotation;
                s_MouseDownState = null;
                if (s_LockHandle)
                    InstanceHandles.UnlockHandlePosition();
                
                InstanceManipulationToolUtility.DisableMinDragDifference();
            }

            return usedEvent;
        }

        static void RecordMouseDownState(InstanceSelectionGroup[] selection)
        {
            int totalInstanceCount = selection.Sum(g => g.InstanceCount);
            s_MouseDownState = new TransformData[totalInstanceCount];
            
            int selectedInstanceIndex = 0;
            for (int i = 0; i < selection.Length; i++)
            {
                InstanceSelectionGroup group = selection[i];
                foreach (int instanceIndex in group.Indices)
                    s_MouseDownState[selectedInstanceIndex++] = TransformData.GetData(group, instanceIndex);
            }
        }

        static void SetLocalHandleOffsetScaleDelta(Vector3 scaleDelta, Quaternion pivotRotation)
        {
            Quaternion refAlignment = Quaternion.Inverse(InstanceHandles.HandleRotation) * pivotRotation;
            InstanceHandles.LocalHandleOffset = Vector3.Scale(Vector3.Scale(s_StartLocalHandleOffset, refAlignment * scaleDelta), refAlignment * Vector3.one);
        }

        public static void SetScaleDelta(Vector3 scaleDelta, Quaternion pivotRotation)
        {
            if (s_MouseDownState == null)
                return;

            SetLocalHandleOffsetScaleDelta(scaleDelta, pivotRotation);

            Vector3 point = InstanceHandles.HandlePosition;
            for (int i = 0; i < s_MouseDownState.Length; i++)
            {
                // Scale about handlePosition or local pivot based on pivotMode
                if (InstanceHandles.PivotMode == PivotMode.Pivot)
                    point = s_MouseDownState[i].Position;
                if (IndividualSpace)
                    pivotRotation = s_MouseDownState[i].Rotation;
                
                s_MouseDownState[i].SetScaleDelta(scaleDelta, point, pivotRotation);
            }
        }

        public static void SetResizeDelta(Vector3 scaleDelta, Vector3 pivotPosition, Quaternion pivotRotation)
        {
            if (s_MouseDownState == null)
                return;

            SetLocalHandleOffsetScaleDelta(scaleDelta, pivotRotation);

            for (int i = 0; i < s_MouseDownState.Length; i++)
                s_MouseDownState[i].SetScaleDelta(scaleDelta, pivotPosition, pivotRotation);
        }

        public static void SetPositionDelta(Vector3 newPosition, Vector3 oldPosition)
        { 
            if (s_MouseDownState == null)
                return;

            s_PreviousHandlePosition = newPosition;
            Vector3 positionDelta = newPosition - oldPosition;
            
            if (s_MouseDownState.Length > 0)
            {
                s_MouseDownState[0].SetPositionDelta(positionDelta, true);
                Vector3 firstDelta = s_MouseDownState[0].CurrentPosition - s_MouseDownState[0].Position;

                for (int i = 1; i < s_MouseDownState.Length; i++)
                    s_MouseDownState[i].SetPositionDelta(firstDelta, false);
            }
        }

        public static bool HandleHasMoved(Vector3 position)
            => position != s_PreviousHandlePosition;
    }
}