// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MA.Core;
using MA.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MA.Flora
{
    /// <summary>
    /// A component that links a prefab model instance in the scene to an instanced mesh container.
    /// </summary>
    /// <remarks>
    /// This component manages a linked container instance.
    /// Use this component to attach logic to your container instances.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Flora/Instanced Object Link")]
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedObjectLink Icon.png")]
    [HelpURL("https://flora.magneticarcade.com/components/instanced-object-link")]
    public sealed class InstancedObjectLink : MonoBehaviour, ISerializationCallbackReceiver
    {
        enum Version { Initial = 1 }

        #pragma warning disable CS0414
        [SerializeField] Version m_Version = Version.Initial;
        #pragma warning restore CS0414
        [SerializeField] InstancedMeshContainer m_Container;
        [SerializeField] int m_InstanceIndex = -1;
        [SerializeField] LocalTransform m_CachedTransform;
        [SerializeField] Space m_TransformSpace;

        Transform m_Transform;
        InstancedPrototype m_Prototype;
        bool m_IsContainerParent;
        bool m_IsRenderable;

        List<MeshRenderer> m_Renderers = new List<MeshRenderer>();
#if UNITY_EDITOR
        List<MeshFilter> m_Filters = new List<MeshFilter>();
        LODGroup m_LODGroup;
#endif

        /// <summary>Gets the container that this instance belongs to.</summary>
        public InstancedMeshContainer Container => m_Container;

        /// <summary>Gets the index of this instance in the container.</summary>
        public int InstanceIndex => m_InstanceIndex;

        /// <summary>Gets a value indicating whether the link points to a valid instance in a container.</summary>
        public bool IsLinked => m_Prototype && m_Container && m_Container.GetLinkedObject(m_InstanceIndex) == this;

        /// <summary>Gets a value indicating whether the link is only used for logic and does not render.</summary>
        public bool IsLogicOnly => !m_Prototype || !m_IsRenderable;

        /// <summary>Gets a value indicating whether the link is renderable.</summary>
        public bool IsRenderable => m_IsRenderable;

        /// <summary>Attaches the link to a container and sets up instance management.</summary>
        /// <param name="container">The container to add the link to.</param>
        /// <param name="instanceIndex">The index of the instance in the container, or -1 to add the instance at the current transform of the link.</param>
        /// <remarks>
        /// Adds an instance to the container at the current transform of the link.
        /// If the link is already added to a container, it will be removed from the current container.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if the specified container is invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the link is already attached to a valid instance.</exception>
        public void AttachToContainer(InstancedMeshContainer container, int instanceIndex = -1)
        {
            if (!container)
                throw new ArgumentException($"{nameof(InstancedObjectLink)}: The specified container is invalid.");
            if (m_Container && m_Container.HasLinkedObject(m_InstanceIndex))
                throw new InvalidOperationException($"{nameof(InstancedObjectLink)}: The link is already attached to a valid instance. Please detach it first.");

            if (m_Container != container || m_InstanceIndex != instanceIndex)
            {
                if (IsLinked)
                    DetachFromContainer();

                m_Container = container;
                m_InstanceIndex = instanceIndex;

                if (m_InstanceIndex >= 0 && m_Container.Exists(m_InstanceIndex))
                {
                    m_Container.RegisterLinkedObjectInternal(m_InstanceIndex, this, teleportToInstance: true);
                }
                else
                {
                    m_InstanceIndex = m_Container.AddLinkedObject(this);
                }
            }
        }

        /// <summary>Detaches the link from the container and stops instance management.</summary>
        /// <param name="removeInstanceFromContainer">True to remove the instance from the container; otherwise, false to keep the instance in the container.</param>
        public void DetachFromContainer(bool removeInstanceFromContainer = true)
        {
            if (IsLinked)
            {
                m_Container.UnregisterLinkedObjectInternal(m_InstanceIndex, removeInstanceFromContainer, false);
                m_InstanceIndex = -1;
                m_Container = null;
            }
        }

        /// <summary>Updates the instance's transform to match the link's current transform.</summary>
        /// <remarks>At runtime, this method must be called manually after changing the transform of the link.</remarks>
        public void MarkTransformAsDirty()
        {
            if (!IsLinked) return;
            CacheTransform();
            m_Container.UpdateInstanceTransformInternal(m_InstanceIndex, m_CachedTransform, m_TransformSpace, false);
        }

        /// <summary>Refreshes the link's transform to align with its instance.</summary>
        /// <remarks>Generally, this method is called automatically when the instance is updated in the container.</remarks>
        public void TeleportToInstance()
        {
            if (!IsLinked) return;
            UpdateTransformSpace();
            m_CachedTransform = m_Container.GetInstanceTransform(m_InstanceIndex, m_TransformSpace);
            m_Transform.SetTransform(m_CachedTransform, m_TransformSpace);
        }

        /// <summary>Returns the bounds of the link in the specified space.</summary>
        /// <param name="space">The space to calculate the bounds in.</param>
        /// <returns>The bounds of the link in the specified space.</returns>
        public AxisAlignedBox GetBounds(Space space)
            => m_Container ? m_Container.GetInstanceBounds(m_InstanceIndex, space) : AxisAlignedBox.Empty;

        // --- Private ---

        internal bool IsContainerParent => m_IsContainerParent;

        internal InstancedGlobalID GlobalID => IsLinked ? m_Container.GetGlobalInstancedID(m_InstanceIndex) : InstancedGlobalID.Null;

        internal SerializableGuid PrefabGuid => m_Prototype ? m_Prototype.PrefabGuid : SerializableGuid.Empty;

        internal void UpdateInstanceIndexInternal(int instanceIndex)
        {
            m_InstanceIndex = instanceIndex;
        }

        internal void InitializeInternal(InstancedMeshContainer container, int instanceIndex)
        {
            m_Container = container;
            m_InstanceIndex = instanceIndex;
            m_Prototype = GetComponent<InstancedPrototype>();
        }

        // --- Events ---

        void OnEnable()
        {
            m_Transform = transform;
            m_Prototype = GetComponent<InstancedPrototype>();
            m_IsRenderable = m_Prototype != null;

            CacheTransform();

            if (m_IsRenderable)
            {
                GetComponentsInChildren(m_Renderers);
                m_IsRenderable = m_Renderers.Count > 0;
            }

#if UNITY_EDITOR
            UnityEditor.SceneManagement.PrefabStage prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject);
            if ((prefabStage || !gameObject.scene.isLoaded) && m_Transform.parent == null)
                return;
#endif

            if (IsLinked)
            {
#if UNITY_EDITOR
                if (m_Container.GetLinkedObject(m_InstanceIndex) != this && !UndoUtility.IsProcessing)
                {
                    UndoUtility.RecordObject(m_Container, "Duplicate Linked Object");
                    m_InstanceIndex = m_Container.AddInstance(m_CachedTransform, m_TransformSpace);
                    m_Container.AttachLinkedObject(m_InstanceIndex, this, teleportToInstance: true);
                }

                m_Container.SetSelected(UnityEditor.Selection.Contains(gameObject), m_InstanceIndex);

                bool contributesToGI = m_Prototype.LinkedObjectContributesToGI;
                if (contributesToGI)
                    UnityEditor.Lightmapping.bakeStarted += OnLightmapBakingStart;
#endif

                m_Container.SetInstanceEnabled(m_InstanceIndex, true);
                TeleportToInstance();
            }

#if UNITY_EDITOR
            TryGetComponent(out m_LODGroup);
            GetComponentsInChildren(m_Filters);
            EditorTransformTracker.Track(transform, OnTransformHierarchyChanged);
#endif
            SetUnityRenderersEnabled(false);
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorTransformTracker.UnTrack(transform);
            UnityEditor.Lightmapping.bakeStarted -= OnLightmapBakingStart;
#endif
            if (IsLinked && gameObject.scene.isLoaded)
            {
                if (m_Container.enabled)
                {
                    m_Container.SetInstanceEnabled(m_InstanceIndex, false);
                    MarkTransformAsDirty();
                }
            }

            SetUnityRenderersEnabled(true);
        }

        void OnDestroy()
        {
            if (IsLinked && gameObject.scene.isLoaded)
            {
                // Undo is registered by the InstanceMeshProxyEditor, this way references are correctly restored
                // UndoUtility.RecordObject(m_Container, "Destroy Proxy Instance");
                m_Container.UnregisterLinkedObjectInternal(m_InstanceIndex, true, false);
            }
        }

        void OnTransformHierarchyChanged(Transform transform)
        {
            if (transform && IsLinked)
            {
                UndoUtility.RecordObject(m_Container, "Move Linked Object");
                MarkTransformAsDirty();
            }
        }

        void OnTransformParentChanged()
        {
            InstancedMeshContainer container = GetComponentInParent<InstancedMeshContainer>();
            if (transform &&
                container &&
                container.transform == transform.parent &&
                container != m_Container)
            {
                if (!m_IsRenderable || container.Prototype.PrefabGuid == m_Prototype.PrefabGuid)
                {
                    UndoUtility.RecordObject(this, "Change Linked Object Parent");

                    // Moved to another parent container
                    if (m_Container)
                    {
                        UndoUtility.RecordObject(m_Container, "Change Linked Object Parent");
                        m_Container.UnregisterLinkedObjectInternal(m_InstanceIndex, true, false);
                    }

                    UndoUtility.RecordObject(container, "Change Linked Object Parent");
                    m_Container = container;
                    m_InstanceIndex = m_Container.AddLinkedObject(this);
                }
            }
        }

        // --- Utility ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetUnityRenderersEnabled(bool enabled)
        {
            if (!m_IsRenderable)
                return;

            HideFlags hideFlags = enabled
                ? HideFlags.None
                : HideFlags.NotEditable | HideFlags.DontSaveInBuild;

            foreach (MeshRenderer container in m_Renderers)
            {
                container.enabled = enabled;
                container.hideFlags = hideFlags;
            }

#if UNITY_EDITOR
            foreach (MeshFilter filter in m_Filters)
            {
                filter.hideFlags = hideFlags;
            }

            if (m_LODGroup)
            {
                m_LODGroup.enabled = enabled;
                m_LODGroup.hideFlags = hideFlags;
            }
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CacheTransform()
        {
            UpdateTransformSpace();
            m_CachedTransform = transform.GetTransform(m_TransformSpace);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateTransformSpace()
        {
            m_Transform = transform;
            m_IsContainerParent = m_Container && m_Transform.parent && m_Container.transform == m_Transform.parent;
            m_TransformSpace = m_IsContainerParent ? Space.Self : Space.World;
        }

        // --- Editor Methods ---

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
#if UNITY_EDITOR
            EditorApplication.delayCall += MarkTransformAsDirty;
#endif
        }

#if UNITY_EDITOR
        internal void SetSelected(bool selected)
        {
            if (IsLinked)
                m_Container.SetSelected(selected, m_InstanceIndex);
        }

        void OnLightmapBakingStart()
        {
            SetUnityRenderersEnabled(true);
            UnityEditor.EditorApplication.update += WaitForLightmapBaking;
        }

        void WaitForLightmapBaking()
        {
            if (UnityEditor.Lightmapping.isRunning)
                return;

            UnityEditor.EditorApplication.update -= WaitForLightmapBaking;
            SetUnityRenderersEnabled(false);
        }
#endif
    }
}
