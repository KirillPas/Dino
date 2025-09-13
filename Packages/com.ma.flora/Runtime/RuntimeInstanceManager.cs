// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using MA.Collections;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    class RuntimeInstanceManager : IDisposable
    {
        static RuntimeInstanceManager s_Instance = new RuntimeInstanceManager();

        SlotAllocator m_InstanceIDAllocator;
        int[] m_IDToContainer;
        int[] m_IDToInstanceIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeInitialize()
        {
            s_Instance?.Dispose();
            s_Instance = new RuntimeInstanceManager();
        }

        static void CleanupBeforeAssemblyReload()
        {
            s_Instance?.Dispose();
            s_Instance = null;
        }

        public RuntimeInstanceManager()
        {
            m_InstanceIDAllocator = new SlotAllocator(1024, AllocatorManager.Persistent);
            m_InstanceIDAllocator.Allocate();
            m_IDToContainer = new int[1024];
            m_IDToInstanceIndex = new int[1024];
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += CleanupBeforeAssemblyReload;
#endif
        }

        public void Dispose()
        {
            m_InstanceIDAllocator.Dispose();
            m_IDToContainer = Array.Empty<int>();
            m_IDToInstanceIndex = Array.Empty<int>();
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= CleanupBeforeAssemblyReload;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Exists(InstancedGlobalID id)
            => s_Instance != null &&
               id > 0 &&
               s_Instance.m_InstanceIDAllocator.Exists(id) &&
               s_Instance.m_IDToContainer[id] != 0;

        // --- Instance Registration ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InstancedGlobalID RegisterInstance(int containerInstanceID, int instanceIndex)
        {
            if (containerInstanceID == 0 || instanceIndex < 0)
                throw new ArgumentException("Invalid container or instance index.");

            InstancedGlobalID id = new InstancedGlobalID(s_Instance.m_InstanceIDAllocator.Allocate());
            if (s_Instance.m_InstanceIDAllocator.MaxAllocatedSlot >= s_Instance.m_IDToContainer.Length)
            {
                int capacity = math.ceilpow2(s_Instance.m_InstanceIDAllocator.MaxAllocatedSlot + 1);
                Array.Resize(ref s_Instance.m_IDToContainer, capacity);
                Array.Resize(ref s_Instance.m_IDToInstanceIndex, capacity);
            }

            s_Instance.m_IDToContainer[id] = containerInstanceID;
            s_Instance.m_IDToInstanceIndex[id] = instanceIndex;
            return id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateInstanceIndex(InstancedGlobalID id, int instanceIndex)
        {
            if (Exists(id))
                s_Instance.m_IDToInstanceIndex[id] = instanceIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnregisterInstance(InstancedGlobalID id)
        {
            UnregisterInstanceInternal(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnregisterInstances(ReadOnlySpan<InstancedGlobalID> id)
        {
            if (s_Instance == null)
                return;

            for (int i = 0; i < id.Length; ++i)
                UnregisterInstanceInternal(id[i]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void UnregisterInstanceInternal(InstancedGlobalID id)
        {
            if (Exists(id))
            {
                s_Instance.m_InstanceIDAllocator.Free(id);
                s_Instance.m_IDToContainer[id] = 0;
                s_Instance.m_IDToInstanceIndex[id] = 0;
            }
        }

        // --- Instance Lookup ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InstancedMeshContainer GetInstanceContainer(InstancedGlobalID id)
        {
            if (Exists(id))
            {
                int containerInstanceID = s_Instance.m_IDToContainer[id];
                return (InstancedMeshContainer)Resources.InstanceIDToObject(containerInstanceID);
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetInstanceIndex(InstancedGlobalID id)
            => Exists(id) ? s_Instance.m_IDToInstanceIndex[id] : -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetInstanceContainerInstanceID(InstancedGlobalID id)
            => Exists(id) ? s_Instance.m_IDToContainer[id] : 0;
    }
}
