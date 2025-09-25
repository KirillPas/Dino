// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MA.Core.Editor.Bridge;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/Instance Icon.png")]
    class InstanceSelectionGroup : ScriptableObject
    {
        static InstancedMeshContainer[] s_LastSelectedRenderers;
        static InstanceSelectionGroup[] s_LastSelected;

        [InitializeOnLoadMethod]
        static void RefCountGlobalSelection()
        {
            Selection.selectionChanged += () =>
            {
                if (s_LastSelected != null)
                {
                    foreach (InstancedMeshContainer renderer in s_LastSelectedRenderers)
                        renderer.ClearSelection();

                    foreach (InstanceSelectionGroup group in s_LastSelected)
                        group.Release();
                }

                InstanceSelectionGroup[] selected = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
                InstancedMeshContainer[] selectedRenderers = selected != null ? new InstancedMeshContainer[selected.Length] : Array.Empty<InstancedMeshContainer>();

                if (selected != null && selected.Length != 0)
                {
                    for (int i = 0; i < selected.Length; i++)
                    {
                        selected[i].Retain();
                        selectedRenderers[i] = selected[i].Container;
                    }
                }

                s_LastSelected = selected;
                s_LastSelectedRenderers = selectedRenderers;
            };

            EditorApplication.playModeStateChanged += state =>
            {
                if (state is PlayModeStateChange.ExitingPlayMode or PlayModeStateChange.ExitingEditMode)
                {
                    if (s_LastSelected != null)
                    {
                        foreach (InstancedMeshContainer renderer in s_LastSelectedRenderers)
                            renderer.ClearSelection();

                        foreach (InstanceSelectionGroup group in s_LastSelected)
                        {
                            SelectionBridge.Remove(group.GetInstanceID());
                            group.Release();
                        }
                    }

                    s_LastSelected = null;
                    s_LastSelectedRenderers = null;
                }
            };

            Undo.undoRedoPerformed += () =>
            {
                if (s_LastSelectedRenderers != null)
                {
                    foreach (InstancedMeshContainer renderer in s_LastSelectedRenderers)
                        renderer.ClearSelection();
                }

                InstanceSelectionGroup[] selected = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
                InstancedMeshContainer[] selectedRenderers = selected != null ? new InstancedMeshContainer[selected.Length] : Array.Empty<InstancedMeshContainer>();

                if (selected != null && selected.Length != 0)
                {
                    for (int i = 0; i < selected.Length; i++)
                    {
                        selected[i].ValidateAndReselect();
                        selectedRenderers[i] = selected[i].Container;
                    }
                }

                s_LastSelected = selected;
                s_LastSelectedRenderers = selectedRenderers;
            };
        }

        public static InstanceSelectionGroup Create(InstancedGlobalID instanceID)
        {
            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(instanceID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(instanceID);
            return !InstanceExistsAndIsValid(container, instanceIndex) ? null : Create(container, new[] { instanceIndex });
        }

        public static InstanceSelectionGroup Create(InstancedMeshContainer container, int[] indices)
        {
            if (container == null || indices == null || indices.Length == 0)
                return null;

            InstanceSelectionGroup group = CreateGroup();
            InitializeGroup(group, container, indices);
            return group;
        }

        public static InstanceSelectionGroup RemoveInstance(InstanceSelectionGroup group, InstancedGlobalID instance)
        {
            if (group == null) return null;

            int index = Array.IndexOf(group.m_GlobalIDs, instance);
            if (index == -1) return null;

            HashSet<int> indices = new HashSet<int>(group.m_Indices);
            indices.Remove(index);
            return Create(group.m_Container, indices.ToArray());
        }

        public static AxisAlignedBox GetSelectedBounds()
        {
            AxisAlignedBox bounds = AxisAlignedBox.Empty;

            foreach (UnityObject obj in Selection.objects)
            {
                if (obj is InstanceSelectionGroup group && !group.IsEmpty)
                {
                    foreach (int index in group.Indices)
                    {
                        AxisAlignedBox selectedInstanceBounds = group.Container.GetInstanceBounds(index, Space.World);
                        bounds += selectedInstanceBounds;
                    }
                }
            }
            return bounds;
        }

        public static InstancedMeshContainer[] GetSelectedRenderers()
        {
            HashSet<InstancedMeshContainer> instancedModelRenderers = new HashSet<InstancedMeshContainer>();

            if (Selection.objects != null)
            {
                foreach (UnityObject obj in Selection.objects)
                {
                    if (obj is InstancedMeshContainer renderer)
                    {
                        instancedModelRenderers.Add(renderer);
                    }
                }
            }

            return instancedModelRenderers.ToArray();
        }

        static bool InstanceExistsAndIsValid(InstancedMeshContainer container, int instanceIndex)
        {
            return container != null && container.Exists(instanceIndex);
        }

        static InstanceSelectionGroup CreateGroup()
        {
            InstanceSelectionGroup group = CreateInstance<InstanceSelectionGroup>();
            group.hideFlags = HideFlags.DontSaveInBuild |
                              HideFlags.DontSaveInEditor;
            return group;
        }

        static void InitializeGroup(InstanceSelectionGroup group, InstancedMeshContainer container, int[] indices)
        {
            group.Initialize(container, indices);
            int undoGroup = Undo.GetCurrentGroup();
            Undo.RegisterCreatedObjectUndo(group, "Create Instance Selection Proxy");
            Undo.CollapseUndoOperations(undoGroup);
        }

        [SerializeField] InstancedMeshContainer m_Container;
        [SerializeField] int[] m_Indices = Array.Empty<int>();

        [NonSerialized] InstancedGlobalID[] m_GlobalIDs = Array.Empty<InstancedGlobalID>();
        [NonSerialized] InstancedObjectLink[] m_LinkedObjects = Array.Empty<InstancedObjectLink>();

        int m_RefCount;
        bool m_HasLinkedObjects;

        public InstancedMeshContainer Container => m_Container;

        public int InstanceCount => m_Indices.Length;

        public int ActiveInstanceIndex => m_Indices[0];

        public bool IsEmpty => m_Container == null || m_Indices.Length == 0;

        public int[] Indices => m_Indices;

        public InstancedGlobalID[] GlobalIDs => m_GlobalIDs;

        public bool HasLinkedObjects => m_HasLinkedObjects;

        public InstancedObjectLink[] LinkedObjects => m_LinkedObjects;

        public bool IsCompatible(InstanceSelectionGroup other)
            => other != null && m_Container == other.m_Container;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginBatchMove()
            => m_Container.BeginBatchMove(Indices);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndBatchMove()
            => m_Container.EndBatchMove();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LocalTransform GetInstanceTransform(int instanceIndex, Space space)
            => m_Container.GetInstanceTransform(instanceIndex, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 GetInstancePosition(int instanceIndex, Space space)
            => m_Container.GetInstancePosition(instanceIndex, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion GetInstanceRotation(int instanceIndex, Space space)
            => m_Container.GetInstanceRotation(instanceIndex, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 GetInstanceScale(int instanceIndex, Space space)
            => m_Container.GetInstanceScale(instanceIndex, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Bounds GetInstanceBounds(int instanceIndex, Space space)
            => m_Container.GetInstanceBounds(instanceIndex, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceTransform(int instanceIndex, in LocalTransform transform, Space space)
            => m_Container.UpdateInstanceTransform(instanceIndex, transform, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceTransform(int instanceIndex, float3 position, quaternion rotation, float3 scale, Space space)
            => m_Container.UpdateInstanceTransform(instanceIndex, position, rotation, scale, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstancePosition(int instanceIndex, float3 position, Space space)
            => m_Container.UpdateInstancePosition(instanceIndex, position, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceRotation(int instanceIndex, quaternion rotation, Space space)
            => m_Container.UpdateInstanceRotation(instanceIndex, rotation, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceScale(int instanceIndex, float3 scale, Space space)
            => m_Container.UpdateInstanceScale(instanceIndex, scale, space);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AxisAlignedBox CalculateBounds(Space space)
        {
            AxisAlignedBox bounds = AxisAlignedBox.Empty;
            foreach (int index in m_Indices)
            {
                AxisAlignedBox instanceBounds = m_Container.GetInstanceBounds(index, space);
                bounds += instanceBounds;
            }
            return bounds;
        }


        internal void Retain()
        {
            m_RefCount++;

            if (m_Container)
                m_Container.SetSelectedIndices(true, m_Indices);
        }

        internal void Release(bool destroy = false)
        {
            if (this && (destroy || --m_RefCount <= 0))
            {
                int undoGroup = Undo.GetCurrentGroup();
                string undoGroupName = Undo.GetCurrentGroupName();

                if (m_Container)
                    m_Container.ClearSelection();

                Undo.DestroyObjectImmediate(this);
                Undo.SetCurrentGroupName(undoGroupName);
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        static HashSet<int> s_IndicesSet = new HashSet<int>();

        internal void ValidateAndReselect()
        {
            if (m_Container)
            {
                m_Container.ClearSelection();

                s_IndicesSet.Clear();
                for (int i = 0; i < m_Indices.Length; i++)
                {
                    if (InstanceExistsAndIsValid(m_Container, m_Indices[i]))
                        s_IndicesSet.Add(m_Indices[i]);
                }

                m_Indices = s_IndicesSet.ToArray();
                m_LinkedObjects = new InstancedObjectLink[m_Indices.Length];
                m_GlobalIDs = new InstancedGlobalID[m_Indices.Length];

                for (int i = 0; i < m_Indices.Length; i++)
                {
                    int instanceIndex = m_Indices[i];
                    m_LinkedObjects[i] = m_Container.GetLinkedObject(instanceIndex);
                    m_GlobalIDs[i] = m_Container.GetGlobalInstancedID(instanceIndex);;
                }

                m_Container.SetSelectedIndices(true, m_Indices);
            }
            else
            {
                Release(destroy: true);
            }
        }

        void Initialize(InstancedMeshContainer container, int[] indices)
        {
            m_Container = container;
            m_Indices = indices;
            m_LinkedObjects = new InstancedObjectLink[m_Indices.Length];
            m_GlobalIDs = new InstancedGlobalID[m_Indices.Length];

            for (int i = 0; i < m_Indices.Length; i++)
            {
                int instanceIndex = m_Indices[i];
                m_LinkedObjects[i] = m_Container.GetLinkedObject(instanceIndex);
                m_HasLinkedObjects |= m_LinkedObjects[i] != null;
                m_GlobalIDs[i] = m_Container.GetGlobalInstancedID(instanceIndex);
            }
        }
    }
}
