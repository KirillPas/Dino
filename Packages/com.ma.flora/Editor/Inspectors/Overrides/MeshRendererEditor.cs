// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(MeshRenderer))]
    class MeshRendererEditor : MeshRendererInternal
    {
        public override void OnInspectorGUI()
        {
            bool isPersistent = false;
            bool hasInstancedLink = false;

            foreach (UnityObject target in targets)
            {
                isPersistent |= EditorUtility.IsPersistent(target);

                if (target is MeshRenderer renderer)
                {
                    InstancedObjectLink parentObjectLink = renderer.GetComponentInParent<InstancedObjectLink>();
                    hasInstancedLink = parentObjectLink != null && parentObjectLink.enabled;
                    break;
                }
            }

            if (!isPersistent && hasInstancedLink)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox("An `InstancedObjectLink` is managing this component. Open the source prefab to make changes.", MessageType.Info);
                return;
            }

            base.OnInspectorGUI();
        }
    }
}
