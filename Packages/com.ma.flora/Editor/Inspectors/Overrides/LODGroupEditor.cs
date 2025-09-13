// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(LODGroup))]
    class LODGroupEditor : LODGroupEditorInternal
    {
        public override void OnInspectorGUI()
        {
            bool isPersistent = false;
            bool hasLinkedObject = false;

            foreach (UnityObject target in targets)
            {
                isPersistent |= EditorUtility.IsPersistent(target);

                if (target is Component component && component.TryGetComponent(out InstancedObjectLink link) && link.enabled)
                {
                    hasLinkedObject = true;
                    break;
                }
            }

            if (!isPersistent && hasLinkedObject)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox("An `InstancedObjectLink` is managing this component. Open the source prefab to make changes.", MessageType.Info);
                return;
            }

            base.OnInspectorGUI();
        }
    }
}
