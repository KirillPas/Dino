// Copyright © Magnetic Arcade. All Rights Reserved.

#if UNITY_2022_3_OR_NEWER
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    static class InstanceSelectionGroupUtility
    {
         [InitializeOnLoadMethod]
        static void Subscribe()
        {
            HandleUtility.getEntitiesForAuthoringObject += GetEntitiesForAuthoringObject;
            HandleUtility.getAuthoringObjectForEntity += GetAuthoringObjectForEntity;
        }

        static IEnumerable<int> GetEntitiesForAuthoringObject(UnityObject obj)
        {
            switch (obj)
            {
                case InstancedMeshContainer container:
                {
                    for (int i = 0; i < container.InstanceCount; i++)
                        yield return container.GetGlobalInstancedID(i);
                    break;
                }
                case GameObject gameObject when gameObject.TryGetComponent(out InstancedMeshContainer objContainer):
                {
                    for (int i = 0; i < objContainer.InstanceCount; i++)
                        yield return objContainer.GetGlobalInstancedID(i);
                    break;
                }
                case InstancedObjectLink { IsLinked: true } link:
                    yield return link.GlobalID;
                    break;
                case InstanceSelectionGroup selectionGroup:
                    for (int i = 0; i < selectionGroup.InstanceCount; i++)
                        yield return selectionGroup.GlobalIDs[i];
                    break;
            }
        }

        static UnityObject GetAuthoringObjectForEntity(int entity)
        {
            InstancedGlobalID instanceID = new InstancedGlobalID(entity);
            UnityObject authoringObject = RuntimeInstanceManager.GetInstanceContainer(instanceID);

            // If we did not find the container associated with this entity, try to find it in the current selection.
            // We don't want to create a new EntitySelectionProxy for an Entity that is already selected. Otherwise some features like Ctrl+click to deselect an Entity won't work.
            // For example, Ctrl+click is basically checking if the newly picked object is already in the Selection.objects in list. If this is the case, then it deselects it.
            if (authoringObject == null && Selection.objects != null)
            {
                foreach (UnityObject obj in Selection.objects)
                {
                    InstanceSelectionGroup group = obj as InstanceSelectionGroup;
                    if (group != null)
                    {
                        for (int i = 0; i < group.InstanceCount; i++)
                        {
                            if (group.GlobalIDs[i] == entity)
                            {
                                authoringObject = group;
                                break;
                            }
                        }
                    }
                }
            }

            if (authoringObject == null)
                authoringObject = InstanceSelectionGroup.Create(instanceID);

            return authoringObject;
        }
    }
}
#endif
