// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MA.Flora.Editor
{
    static class InstancedMeshContainerMenuItems
    {
        // --- Convert To Container ---

        [DebuggerDisplay("Prefab={SourcePrefab.name}, Instance={Instance.name}")]
        struct PrefabInstanceInfo
        {
            public Transform Parent;
            public GameObject SourcePrefab;
            public InstancedPrototype Prototype;
            public GameObject Instance;
            public float4x4 LocalToWorldMatrix;
        }

        struct PrefabInstanceInfoComparer : IComparer<PrefabInstanceInfo>
        {
            public int Compare(PrefabInstanceInfo x, PrefabInstanceInfo y)
            {
                int prefabComparison = CompareGameObjects(x.SourcePrefab, y.SourcePrefab);
                if (prefabComparison != 0) return prefabComparison;

                int parentComparison = CompareParents(x.Parent, y.Parent);
                if (parentComparison != 0) return parentComparison;

                return string.Compare(x.Instance.name, y.Instance.name, StringComparison.Ordinal);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int CompareParents(Transform xParent, Transform yParent)
            {
                if (xParent == yParent) return 0;
                if (xParent == null) return -1;
                if (yParent == null) return 1;
                return xParent.GetInstanceID().CompareTo(yParent.GetInstanceID());
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int CompareGameObjects(GameObject xObject, GameObject yObject)
            {
                if (xObject == yObject) return 0;
                if (xObject == null) return -1;
                if (yObject == null) return 1;
                return xObject.GetInstanceID().CompareTo(yObject.GetInstanceID());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static InstancedMeshContainer CreateContainer(Transform parent, GameObject sourcePrefab, List<GameObject> instances, AxisAlignedBox instancesBounds)
        {
            string name = GameObjectUtility.GetUniqueNameForSibling(parent, $"{sourcePrefab.name} (Instanced)");
            GameObject containerGameObject = new GameObject(name, typeof(InstancedMeshContainer))
            {
                layer = parent != null ? parent.gameObject.layer : 0,
                isStatic = true,
                transform =
                {
                    parent = parent,
                    position = instancesBounds.Center,
                }
            };

            InstancedMeshContainer container = containerGameObject.GetComponent<InstancedMeshContainer>();
            container.Prefab = sourcePrefab;

            Undo.RegisterCreatedObjectUndo(containerGameObject, "Convert to Instances (Flora)");

            foreach (GameObject instance in instances)
            {
                instance.transform.SetParent(containerGameObject.transform, true);
                container.AddLinkedObject(instance);
            }

            return container;
        }

        [MenuItem("GameObject/Convert to Instances (Flora)", true, 0)]
        static bool CreateContainerCommandValidate()
        {
            GameObject[] selectedInstances = Selection.GetFiltered<GameObject>(SelectionMode.TopLevel | SelectionMode.Editable);
            if (selectedInstances.Length == 0)
                return false;

            foreach (GameObject instance in selectedInstances)
                if (SelectionHierarchyContainsConvertableGameObjects(instance))
                    return true;

            return false;
        }

        [MenuItem("GameObject/Convert to Instances (Flora)", false, 0)]
        static void CreateContainerCommand()
        {
            GameObject[] topLevelInstances = Selection.GetFiltered<GameObject>(SelectionMode.TopLevel | SelectionMode.Editable);
            if (topLevelInstances.Length == 0)
                return;

            foreach (GameObject instance in topLevelInstances)
            {
                if (!SelectionHierarchyContainsConvertableGameObjects(instance))
                    continue;

                Undo.RegisterFullObjectHierarchyUndo(instance, "Convert to Instances (Flora)");
            }

            HashSet<GameObject> uniqueInstances = new HashSet<GameObject>();
            foreach (GameObject topLevelInstance in topLevelInstances)
                GetUniqueRenderableRootPrefabInstancesRecursively(topLevelInstance, uniqueInstances);

            List<PrefabInstanceInfo> prefabInstances = new List<PrefabInstanceInfo>(topLevelInstances.Length);
            foreach (GameObject instance in uniqueInstances)
            {
                GameObject prefab = (GameObject)AssetDatabase.LoadMainAssetAtPath(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance));
                if (prefab == null)
                    continue; // Has no source prefab

                InstancedPrototype prefabPrototype = EnsurePrefabHasPrototype(ref prefab);

                prefabInstances.Add(new PrefabInstanceInfo
                {
                    Parent = instance.transform.parent,
                    SourcePrefab = prefab,
                    Prototype = prefabPrototype,
                    Instance = instance,
                    LocalToWorldMatrix = instance.transform.localToWorldMatrix
                });
            }

            prefabInstances.Sort(new PrefabInstanceInfoComparer());

            Transform currentParent = null;
            GameObject currentSourcePrefab = null;
            AxisAlignedBox currentPrototypeBounds = AxisAlignedBox.Empty;
            List<InstancedMeshContainer> createdContainers = new List<InstancedMeshContainer>();

            List<GameObject> instancesToAdd = new List<GameObject>();
            AxisAlignedBox instancesToAddBounds = AxisAlignedBox.Empty;

            foreach (PrefabInstanceInfo helper in prefabInstances)
            {
                if (currentParent != helper.Parent || currentSourcePrefab != helper.SourcePrefab)
                {
                    if (instancesToAdd.Count > 0)
                    {
                        if (!instancesToAddBounds.IsEmpty)
                        {
                            InstancedMeshContainer container = CreateContainer(currentParent, currentSourcePrefab, instancesToAdd, instancesToAddBounds);
                            createdContainers.Add(container);
                        }

                        instancesToAdd.Clear();
                        instancesToAddBounds = AxisAlignedBox.Empty;
                    }

                    currentParent = helper.Parent;
                    currentSourcePrefab = helper.SourcePrefab;
                    currentPrototypeBounds = helper.Prototype.Bounds;
                }

                instancesToAdd.Add(helper.Instance);
                instancesToAddBounds += currentPrototypeBounds.TransformBy(helper.LocalToWorldMatrix);
            }

            if (instancesToAdd.Count > 0)
            {
                if (!instancesToAddBounds.IsEmpty)
                {
                    InstancedMeshContainer container = CreateContainer(currentParent, currentSourcePrefab, instancesToAdd, instancesToAddBounds);
                    createdContainers.Add(container);
                }

                instancesToAdd.Clear();
            }

            GameObject[] newSelection = createdContainers.ConvertAll(container => container.gameObject).ToArray();
            if (newSelection.Length > 0)
            {
                Selection.objects = newSelection;
                Selection.activeGameObject = newSelection[0];
            }
        }

        // --- Split Container ---

        [MenuItem("GameObject/Split Container(s) (Flora)", true, 0)]
        static bool SplitContainerCommandValidate()
        {
            return Selection.GetFiltered<InstancedMeshContainer>(SelectionMode.TopLevel | SelectionMode.Editable).Length > 0;
        }

        [MenuItem("GameObject/Split Container(s) (Flora)", false, 0)]
        static void SplitContainerCommand()
        {
            InstancedMeshContainer[] containersToSplit = Selection.GetFiltered<InstancedMeshContainer>(SelectionMode.TopLevel | SelectionMode.Editable);
            if (containersToSplit.Length == 0)
                return;

            List<InstancedMeshContainer> splitContainers = new List<InstancedMeshContainer>();

            foreach (InstancedMeshContainer container in containersToSplit)
            {
                AxisAlignedBox selectedContainerBounds = container.CalculateBounds(Space.World);
                if (selectedContainerBounds.IsEmpty)
                {
                    Undo.DestroyObjectImmediate(container.gameObject);
                    continue;
                }

                Undo.RegisterFullObjectHierarchyUndo(container.gameObject, "Split Instanced Mesh Container");

                float3 min = selectedContainerBounds.Min;
                float3 max = selectedContainerBounds.Max;
                float3 center = selectedContainerBounds.Center;

                AxisAlignedBox topLeftBounds = new AxisAlignedBox(new float3(min.x, min.y, min.z), new float3(center.x, max.y, center.z));
                if (SplitInstancedMeshContainer(container, topLeftBounds, out InstancedMeshContainer topLeftContainer))
                    splitContainers.Add(topLeftContainer);

                AxisAlignedBox topRightBounds = new AxisAlignedBox(new float3(center.x, min.y, min.z), new float3(max.x, max.y, center.z));
                if (SplitInstancedMeshContainer(container, topRightBounds, out InstancedMeshContainer topRightContainer))
                    splitContainers.Add(topRightContainer);

                AxisAlignedBox bottomLeftBounds = new AxisAlignedBox(new float3(min.x, min.y, center.z), new float3(center.x, max.y, max.z));
                if (SplitInstancedMeshContainer(container, bottomLeftBounds, out InstancedMeshContainer bottomLeftContainer))
                    splitContainers.Add(bottomLeftContainer);

                AxisAlignedBox bottomRightBounds = new AxisAlignedBox(new float3(center.x, min.y, center.z), new float3(max.x, max.y, max.z));
                if (SplitInstancedMeshContainer(container, bottomRightBounds, out InstancedMeshContainer bottomRightContainer))
                    splitContainers.Add(bottomRightContainer);

                Undo.DestroyObjectImmediate(container.gameObject);
            }

            GameObject[] newSelection = splitContainers.ConvertAll(split => split.gameObject).ToArray();
            if (newSelection.Length > 0)
            {
                Selection.objects = newSelection;
                Selection.activeGameObject = newSelection[0];
            }
        }

        static bool SplitInstancedMeshContainer(InstancedMeshContainer source, AxisAlignedBox bounds, out InstancedMeshContainer splitContainer)
        {
            splitContainer = null;

            NativeList<int> instancesInBounds = new NativeList<int>(64, Allocator.Temp);
            source.GetInstancesInsideBounds(bounds, Space.World, instancesInBounds);
            if (instancesInBounds.Length == 0)
                return false;

            string name = GameObjectUtility.GetUniqueNameForSibling(source.transform.parent, source.name);
            GameObject splitGameObject = new GameObject(name);
            splitGameObject.transform.parent = source.transform.parent;
            splitGameObject.transform.position = bounds.Center;
            splitGameObject.transform.localRotation = source.transform.localRotation;
            splitGameObject.transform.localScale = source.transform.localScale;
            Undo.RegisterCreatedObjectUndo(splitGameObject, "Split Instanced Mesh Container");

            InstancedMeshContainer container = Undo.AddComponent<InstancedMeshContainer>(splitGameObject);
            container.Prototype = source.Prototype;
            MoveInstancesToContainer(source, instancesInBounds.AsArray(), container);

            splitContainer = container;
            return true;
        }

        // --- Combine Container ---

        [MenuItem("GameObject/Combine Containers (Flora)", true, 0)]
        static bool CombineContainerCommandValidate()
        {
            if (Selection.activeGameObject == null)
                return false;

            InstancedMeshContainer activeContainer = Selection.activeGameObject.GetComponent<InstancedMeshContainer>();
            if (activeContainer == null)
                return false;

            GameObject[] selectedInstances = Selection.GetFiltered<GameObject>(SelectionMode.TopLevel | SelectionMode.Editable);
            if (selectedInstances.Length < 2)
                return false;

            InstancedPrototype activePrototype = activeContainer.Prototype;

            InstancedMeshContainer[] instancedMeshContainers = new InstancedMeshContainer[selectedInstances.Length];
            for (int i = 0; i < selectedInstances.Length; i++)
            {
                if (!selectedInstances[i].TryGetComponent(out instancedMeshContainers[i]))
                    return false;

                if (instancedMeshContainers[i].Prototype != activePrototype)
                    return false;
            }

            return true;
        }

        [MenuItem("GameObject/Combine Containers (Flora)", false, 0)]
        static void CombineContainerCommand()
        {
            if (Selection.activeGameObject == null)
                return;

            InstancedMeshContainer activeContainer = Selection.activeGameObject.GetComponent<InstancedMeshContainer>();
            if (activeContainer == null)
                return;

            GameObject[] selectedInstances = Selection.GetFiltered<GameObject>(SelectionMode.TopLevel | SelectionMode.Editable);
            if (selectedInstances.Length < 2)
                return;

            InstancedMeshContainer[] instancedMeshContainers = new InstancedMeshContainer[selectedInstances.Length];
            for (int i = 0; i < selectedInstances.Length; i++)
            {
                if (!selectedInstances[i].TryGetComponent(out instancedMeshContainers[i]))
                    return;
            }

            Undo.RegisterFullObjectHierarchyUndo(activeContainer.gameObject, "Combine Instances");

            foreach (InstancedMeshContainer instancedMeshContainer in instancedMeshContainers)
            {
                if (instancedMeshContainer == activeContainer)
                    continue;

                Undo.RegisterFullObjectHierarchyUndo(instancedMeshContainer.gameObject, "Combine Instances");

                for (int i = 0; i < instancedMeshContainer.InstanceCount; i++)
                    MoveInstanceToContainer(instancedMeshContainer, i, activeContainer);

                Undo.DestroyObjectImmediate(instancedMeshContainer.gameObject);
            }
        }

        // --- Convert Instances To GameObjects ---

        [MenuItem("GameObject/Convert Instances To GameObjects (Flora)", false, 0)]
        static void BreakContainerCommand()
        {
            InstancedMeshContainer[] containersToBreak = Selection.GetFiltered<InstancedMeshContainer>(SelectionMode.TopLevel | SelectionMode.Editable);
            int totalInstanceCount = containersToBreak.Sum(container => container.InstanceCount);
            bool undoAvailable = totalInstanceCount <= 100000;
            if (!undoAvailable)
            {
                if (!EditorUtility.DisplayDialog("Convert To GameObjects",
                        "This operation will be irreversible due to the large number of instances in the container. Are you sure you want to continue?",
                        "Continue", "Cancel"))
                {
                    return;
                }
            }

            foreach (InstancedMeshContainer container in containersToBreak)
            {
                if (!container || !container.Prototype)
                    continue;

                if (undoAvailable)
                    Undo.RegisterFullObjectHierarchyUndo(container.gameObject, "Convert To GameObjects (Flora)");

                for (int instanceIndex = container.InstanceCount - 1; instanceIndex >= 0; instanceIndex--)
                {
                    InstancedObjectLink instancedObjectLink = container.GetLinkedObject(instanceIndex);
                    if (instancedObjectLink != null)
                    {
                        Object.DestroyImmediate(instancedObjectLink);
                    }
                    else
                    {
                        GameObject prefab = container.Prefab;
                        if (prefab)
                        {
                            LocalTransform instanceTransform = container.GetInstanceTransform(instanceIndex, Space.World);
                            GameObject prefabInstance = (GameObject)PrefabUtility.InstantiatePrefab(container.Prefab, container.transform);
                            prefabInstance.transform.SetTransform(instanceTransform, Space.World);

                            if (undoAvailable)
                                Undo.RegisterCreatedObjectUndo(prefabInstance, "Convert To GameObjects (Flora)");
                        }
                    }
                }

                Object.DestroyImmediate(container);
            }
        }

        [MenuItem("GameObject/Convert Instances To GameObjects (Flora)", true, 0)]
        static bool BreakContainerCommandValidate()
        {
            return Selection.GetFiltered<InstancedMeshContainer>(SelectionMode.TopLevel | SelectionMode.Editable).Length > 0;
        }

        // --- Helper Methods ---

        static InstancedPrototype EnsurePrefabHasPrototype(ref GameObject prefab)
        {
            if (!prefab.TryGetComponent(out InstancedPrototype prototype))
            {
                string assetPath = AssetDatabase.GetAssetPath(prefab);
                using (PrefabUtility.EditPrefabContentsScope editingScope = new PrefabUtility.EditPrefabContentsScope(assetPath))
                {
                    Undo.AddComponent<InstancedPrototype>(editingScope.prefabContentsRoot);
                }

                prefab = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(prefab, assetPath);
                prototype = prefab.GetComponent<InstancedPrototype>();
            }

            return prototype;
        }

        static void MoveInstancesToContainer(InstancedMeshContainer source, NativeArray<int> sourceInstances, InstancedMeshContainer destination)
        {
            foreach (int instanceIndex in sourceInstances)
                MoveInstanceToContainer(source, instanceIndex, destination);
        }

        static void MoveInstanceToContainer(InstancedMeshContainer source, int sourceInstanceIndex, InstancedMeshContainer destination)
        {
            InstancedObjectLink instancedObjectLink = source.GetLinkedObject(sourceInstanceIndex);
            if (instancedObjectLink != null)
            {
                source.DetachLinkedObject(sourceInstanceIndex, false);
                destination.AddLinkedObject(instancedObjectLink);
                Undo.SetTransformParent(instancedObjectLink.transform, destination.transform, "Move Linked Object");
            }
            else
            {
                LocalTransform instanceTransform = source.GetInstanceTransform(sourceInstanceIndex, Space.World);
                destination.AddInstance(instanceTransform, Space.World);
            }
        }

        static void GetUniqueRenderableRootPrefabInstancesRecursively(GameObject obj, HashSet<GameObject> uniqueRootInstances)
        {
            if (obj == null)
                return;

            bool isContainer = obj.TryGetComponent(out InstancedMeshContainer _);
            if (isContainer)
                return; // Skip containers and their children

            bool isInstance = obj.TryGetComponent(out InstancedObjectLink _);
            if (isInstance)
                return; // Skip instances

            if (PrefabUtility.IsPartOfPrefabInstance(obj))
            {
                GameObject rootObj = PrefabUtility.GetOutermostPrefabInstanceRoot(obj); // Can't convert nested prefabs
                if (rootObj)
                {
                    if (rootObj.TryGetComponent(out LODGroup _) || rootObj.GetComponentInChildren<MeshRenderer>() != null)
                    {
                        uniqueRootInstances.Add(rootObj);
                        return;
                    }
                }
            }

            foreach (Transform child in obj.transform)
            {
                GetUniqueRenderableRootPrefabInstancesRecursively(child.gameObject, uniqueRootInstances);
            }
        }

        static bool SelectionHierarchyContainsConvertableGameObjects(GameObject obj)
        {
            if (obj == null)
                return false;

            bool isContainer = obj.TryGetComponent(out InstancedMeshContainer _);
            if (isContainer)
                return false;

            if (PrefabUtility.IsPartOfPrefabInstance(obj))
            {
                GameObject rootObj = PrefabUtility.GetOutermostPrefabInstanceRoot(obj); // Can't convert nested prefabs
                if (rootObj)
                {
                    bool isInstance = rootObj.TryGetComponent(out InstancedObjectLink _);
                    if (!isInstance) // Skip instances
                    {
                        if (rootObj.TryGetComponent(out LODGroup _) || rootObj.GetComponentInChildren<MeshRenderer>() != null)
                            return true;
                    }
                }
            }

            foreach (Transform child in obj.transform)
            {
                if (SelectionHierarchyContainsConvertableGameObjects(child.gameObject))
                    return true;
            }

            return false;
        }
    }
}
