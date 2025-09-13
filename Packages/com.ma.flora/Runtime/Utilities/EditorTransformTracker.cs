// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MA.Flora
{
    static class EditorTransformTracker
    {
        class TrackedTransform
        {
            public int RefCount;
            public int InstanceId;
            public Transform Transform;
            public event Action<Transform> TransformChanged;
            public void SetChanged() => TransformChanged?.Invoke(Transform);
        }
        
        static Dictionary<int, TrackedTransform> s_TrackedTransforms = new Dictionary<int, TrackedTransform>();

        [Conditional("UNITY_EDITOR")]
        internal static void Track(Transform transform, Action<Transform> action)
        {
            if (transform == null)
                return;
            
            int transformId = transform.GetInstanceID();
            if (s_TrackedTransforms.TryGetValue(transformId, out TrackedTransform trackedTransform))
            {
                trackedTransform.RefCount++;
                trackedTransform.TransformChanged += action;
                s_TrackedTransforms[transformId] = trackedTransform;
            }
            else
            {
                trackedTransform = new TrackedTransform
                {
                    RefCount = 1,
                    InstanceId = transformId,
                    Transform = transform,
                };
                trackedTransform.TransformChanged += action;
                s_TrackedTransforms.Add(transformId, trackedTransform);
            }
            
            if (transform.parent != null)
                Track(transform.parent, action);
        }

        [Conditional("UNITY_EDITOR")]
        internal static void UnTrack(Transform transform)
        {
            if (transform == null)
                return;

            int transformId = transform.GetInstanceID();
            if (!s_TrackedTransforms.TryGetValue(transformId, out TrackedTransform trackedTransform))
                return;
            
            trackedTransform.RefCount--;
            if (trackedTransform.RefCount == 0)
                s_TrackedTransforms.Remove(transformId);
            else
                s_TrackedTransforms[transformId] = trackedTransform;

            if (transform.parent != null)
                UnTrack(transform.parent);
        }

#if UNITY_EDITOR
        static EditorTransformTracker()
        {
            ObjectChangeEvents.changesPublished += OnObjectChangeEventsChangesPublished;
        }
        
        static void OnObjectChangeEventsChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i != stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs evt);
                        if (s_TrackedTransforms.TryGetValue(evt.instanceId, out TrackedTransform trackedTransform))
                            trackedTransform.SetChanged();
                        break;   
                    }
                    case ObjectChangeKind.ChangeGameObjectParent:
                    {
                        stream.GetChangeGameObjectParentEvent(i, out ChangeGameObjectParentEventArgs evt);
                        if (s_TrackedTransforms.TryGetValue(evt.instanceId, out TrackedTransform trackedTransform))
                            trackedTransform.SetChanged();
                        break;
                    }
                    
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        stream.GetDestroyGameObjectHierarchyEvent(i, out DestroyGameObjectHierarchyEventArgs evt);
                        s_TrackedTransforms.Remove(evt.instanceId, out TrackedTransform trackedTransform);
                        break;
                    }
                }
            }
        }
#endif
    }
}