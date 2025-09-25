// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MA.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MA.Flora.Editor
{
    sealed class PlacementOccluders : IDisposable
    {
        readonly List<MeshCollider> m_AddedColliders;
        readonly HashSet<MeshRenderer> m_ProcessedLODRenderers;
        readonly HashSet<Collider> m_ExcludedColliders;

        bool m_ForceStaticOccluders;
        Scene m_TargetScene;
        PlacementObjectMask m_PlacementMask;

        static List<Collider> s_TempColliders = new List<Collider>();
        static List<LODGroup> s_TempLODGroups = new List<LODGroup>();
        static List<MeshRenderer> s_TempMeshRenderers = new List<MeshRenderer>();

        public PlacementOccluders(Scene targetScene, bool forceStaticOccluders, PlacementObjectMask mask)
        {
            m_TargetScene = targetScene;
            m_ForceStaticOccluders = forceStaticOccluders;
            m_PlacementMask = mask;

            m_AddedColliders = new List<MeshCollider>();
            m_ProcessedLODRenderers = new HashSet<MeshRenderer>();
            m_ExcludedColliders = new HashSet<Collider>();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            List<GameObject> gameObjects = targetScene.GetRootGameObjects().ToList();
            ProcessGameObjects(gameObjects);


            List<Collider> addedColliders = new List<Collider>();
            bool ignoreMeshes = (m_PlacementMask & PlacementObjectMask.Mesh) == 0;
            if (!ignoreMeshes)
            {
                foreach (GameObject gameObject in gameObjects)
                {
                    if (!gameObject || !gameObject.activeInHierarchy)
                        continue;

                    gameObject.GetComponentsInChildren(s_TempColliders);
                    addedColliders.AddRange(s_TempColliders);
                }
            }

            addedColliders.AddRange(UnityUtility.FindObjectsByType<Collider>());
            FindExcludedColliders(addedColliders);
        }

        public void ExcludeCollider(Collider collider)
        {
            collider.enabled = false;
            m_ExcludedColliders.Add(collider);
        }

        public void ExcludeColliders(List<Collider> colliders)
        {
            foreach (Collider collider in colliders)
            {
                collider.enabled = false;
                m_ExcludedColliders.Add(collider);
            }
        }

        void ProcessGameObjects(List<GameObject> gameObjects)
        {
            foreach (GameObject gameObject in gameObjects)
            {
                if (!gameObject || !gameObject.activeInHierarchy)
                    continue;

                gameObject.GetComponentsInChildren(s_TempLODGroups);
                foreach (LODGroup lodGroup in s_TempLODGroups)
                {
                    if (!lodGroup || !lodGroup.enabled)
                        continue;

                    LOD[] lods = lodGroup.GetLODs();

                    for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                    {
                        LOD lod = lods[lodIndex];

                        foreach (Renderer r in lod.renderers)
                        {
                            if (r is MeshRenderer mr)
                            {
                                if (!mr || !mr.enabled)
                                    continue;

                                m_ProcessedLODRenderers.Add(mr);

                                if (lodIndex == 0)
                                {
                                    // Only add mesh colliders to the highest LOD
                                    if (!mr.TryGetComponent(out MeshCollider _))
                                    {
                                        AddOccluder(mr.gameObject);
                                    }
                                }
                                else
                                {
                                    // Add non LOD0 mesh renderer's collider to the excluded list
                                    if (mr.TryGetComponent(out Collider collider))
                                    {
                                        collider.enabled = false;
                                        m_ExcludedColliders.Add(collider);
                                    }
                                }
                            }
                        }
                    }
                }

                gameObject.GetComponentsInChildren(s_TempMeshRenderers);
                foreach (MeshRenderer mr in s_TempMeshRenderers)
                {
                    if (!mr || !mr.enabled)
                        continue;

                    if (m_ForceStaticOccluders && !mr.gameObject.isStatic)
                        continue;

                    if (m_ProcessedLODRenderers.Contains(mr))
                        continue;

                    if (!mr.TryGetComponent(out MeshCollider _))
                    {
                        mr.GetComponents(s_TempColliders); // BoxCollider, SphereCollider, CapsuleCollider, etc.
                        if (s_TempColliders.Count > 0)
                        {
                            foreach (Collider collider in s_TempColliders)
                            {
                                collider.enabled = false;
                                m_ExcludedColliders.Add(collider);
                            }
                        }

                        AddOccluder(mr.gameObject);
                    }
                }
            }
        }

        void FindExcludedColliders(List<Collider> colliders)
        {
            bool ignoreMeshes = (m_PlacementMask & PlacementObjectMask.Mesh) == 0;
            bool ignoreTerrains = (m_PlacementMask & PlacementObjectMask.Terrain) == 0;
            bool ignoreLinkedObjects = (m_PlacementMask & PlacementObjectMask.LinkedObject) == 0;

            foreach (Collider collider in colliders)
            {
                if (!collider.enabled || m_ExcludedColliders.Contains(collider))
                    continue;

                GameObject colliderGameObject = collider.gameObject;
                bool colliderIsStatic = !m_ForceStaticOccluders || colliderGameObject.isStatic;
                bool isInTargetScene = colliderGameObject.scene == m_TargetScene;

                bool isCompatibleCollider = false;
                if (collider.TryGetComponent(out MeshCollider _) && !ignoreMeshes)
                    isCompatibleCollider = true;
                else if (collider.TryGetComponent(out TerrainCollider _) && !ignoreTerrains)
                    isCompatibleCollider = true;
                else if (collider.TryGetComponentInParent(out InstancedObjectLink _) && !ignoreLinkedObjects)
                    isCompatibleCollider = true;

                bool excludeCollider = !colliderIsStatic || !isInTargetScene || !isCompatibleCollider;
                if (excludeCollider)
                {
                    collider.enabled = false;
                    m_ExcludedColliders.Add(collider);
                }
            }
        }

        public void Dispose()
        {
            m_AddedColliders.ForEach(Object.DestroyImmediate);

            foreach (Collider collider in m_ExcludedColliders)
                if (collider != null) collider.enabled = true;

            m_AddedColliders.Clear();
            m_ExcludedColliders.Clear();
            m_ProcessedLODRenderers.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddOccluder(GameObject gameObject)
        {
            MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.hideFlags |= HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.HideInInspector | HideFlags.NotEditable;
            m_AddedColliders.Add(meshCollider);
        }
    }
}
