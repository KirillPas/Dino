// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace MA.Flora.Editor
{
    [InitializeOnLoad]
    static class InstanceHandles
    {
        public static bool ViewToolActive =>
            // todo Tools.viewToolActive should be handling the modifier check, but 2022.2 broke this
            Tools.viewToolActive || Tools.current == Tool.View || (Event.current.modifiers & EventModifiers.Alt) == EventModifiers.Alt;

        static InstanceHandles()
        {
            Selection.selectionChanged += OnSelectionChange;
            Undo.undoRedoPerformed += OnUndoRedo;
            Tools.pivotModeChanged += OnPivotModeChanged;
            Tools.pivotRotationChanged += OnPivotRotationChanged;
            ToolManager.activeToolChanged += OnActiveToolChanged;
        }

        static void OnSelectionChange()
        {
            ResetGlobalHandleRotation();
            InvalidateHandlePosition();
            LocalHandleOffset = Vector3.zero;
        }

        static void OnUndoRedo()
        {
            s_GlobalHandleRotation = Tools.handleRotation;
            OnSelectionChange();
        }

        static void OnPivotModeChanged()
        {
            InvalidateHandlePosition();
            ResetGlobalHandleRotation();
        }

        static void OnPivotRotationChanged()
        {
            // InvalidateHandlePosition();
            ResetGlobalHandleRotation();
        }

        static void OnActiveToolChanged()
        {
            ResetGlobalHandleRotation();
        }
        
        public static void ResetGlobalHandleRotation()
        {
            s_GlobalHandleRotation = Quaternion.identity;
        }

        public static InstanceSelectionGroup ActiveSelectionGroup
        {
            get
            {
                if (Selection.activeObject is InstanceSelectionGroup group && !group.IsEmpty)
                    return group;
                
                return null;
            }
        }

        static Vector3 s_HandlePosition;
        static bool s_HandlePositionComputed;
        public static Vector3 CachedHandlePosition
        {
            get
            {
                if (!s_HandlePositionComputed)
                {
                    s_HandlePosition = GetHandlePosition();
                    s_HandlePositionComputed = true;
                }
                
                return s_HandlePosition;
            }
        }

        public static void InvalidateHandlePosition()
        {
            s_HandlePositionComputed = false;
        }

        public static Vector3 HandlePosition
        {
            get
            {
                if (!ActiveSelectionGroup)
                    return new Vector3(Mathf.Infinity, Mathf.Infinity, Mathf.Infinity);
                
                return s_LockHandlePositionActive ? s_LockHandlePosition : CachedHandlePosition;
            }
        }

        public static Vector3 GetHandlePosition()
        {
            if (!ActiveSelectionGroup)
                return new Vector3(Mathf.Infinity, Mathf.Infinity, Mathf.Infinity);

            Vector3 totalOffset = HandleOffset + HandleRotation * LocalHandleOffset;
            switch (PivotMode)
            {
                case PivotMode.Center:
                    return (Vector3)InstanceSelectionGroup.GetSelectedBounds().Center + totalOffset;
                case PivotMode.Pivot:
                    return (Vector3)ActiveSelectionGroup.GetInstancePosition(ActiveSelectionGroup.ActiveInstanceIndex, Space.World) + totalOffset;
                default:
                    return new Vector3(Mathf.Infinity, Mathf.Infinity, Mathf.Infinity);
            }
        }

        static Vector3 s_LockHandlePosition;
        static bool s_LockHandlePositionActive;
        
        public static void LockHandlePosition(Vector3 position)
        {
            s_LockHandlePosition = position;
            s_LockHandlePositionActive = true;
            HandlesBridge.LockHandlePosition(position);
        }

        public static void LockHandlePosition()
        {
            LockHandlePosition(HandlePosition);
        }

        public static void UnlockHandlePosition()
        {
            s_LockHandlePositionActive = false;
            HandlesBridge.UnlockHandlePosition();
        }
        
        public static PivotMode PivotMode
        {
            get => Tools.pivotMode;
            set => Tools.pivotMode = value;
        }

        public static Quaternion HandleRotation
        {
            get
            {
                switch (PivotRotation)
                {
                    case PivotRotation.Global:
                        return Tools.handleRotation = s_GlobalHandleRotation.normalized;
                    case PivotRotation.Local:
                        return HandleLocalRotation.normalized;
                }
                
                return Quaternion.identity;
            }
            set
            {
                if (PivotRotation == PivotRotation.Global)
                    Tools.handleRotation = s_GlobalHandleRotation = value.normalized;
            }
        }
        
        public static PivotRotation PivotRotation
        {
            get => Tools.pivotRotation;
            set => Tools.pivotRotation = value;
        }
        
        public static Vector3 HandleOffset;
        public static Vector3 LocalHandleOffset;
        
        static Quaternion s_GlobalHandleRotation = Quaternion.identity;

        public static Quaternion HandleLocalRotation
        {
            get
            {
                if (!ActiveSelectionGroup)
                    return Quaternion.identity;
                
                return ActiveSelectionGroup.GetInstanceRotation(ActiveSelectionGroup.ActiveInstanceIndex, Space.World).normalized;
            }
        }
    }
}