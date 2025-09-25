// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using Unity.Collections;
using UnityEngine;

namespace MA.Flora.Rendering
{
    [DebuggerTypeProxy(typeof(InstancedMeshIDDebugView))]
    struct InstancedMeshID : IEquatable<InstancedMeshID>, IComparable<InstancedMeshID>
    {
        public static InstancedMeshID Null => new InstancedMeshID(Handle.Null);

        public Handle Handle;

        public bool IsCreated { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Handle.IsCreated; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedMeshID(Handle handle) => Handle = handle;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedMeshID other) => Handle.Equals(other.Handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedMeshID other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Handle.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedMeshID other) => Handle.CompareTo(other.Handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Handle(InstancedMeshID id) => id.Handle;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedMeshID left, InstancedMeshID right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedMeshID left, InstancedMeshID right) => !left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            if (InstancingSystem.IsActive() && IsCreated && InstancingSystem.Instance.Context.MeshManager.TryGetMesh(this, out Mesh mesh))
                return mesh.name;

            return Handle.ToString();
        }
    }

    sealed class InstancedMeshManager : IDisposable
    {
        int m_MeshCount;
        int m_MeshCapacity;
        HandlePool<int> m_HandlePool;
        int[] m_ReferenceCount;
        int[] m_InstanceID;
        InstancedMeshID[] m_MeshID;
        Mesh[] m_Mesh;
        Dictionary<int, InstancedMeshID> m_InstanceIDToMeshID;

        public InstancedMeshManager(int capacity)
        {
            m_MeshCount = 0;
            m_MeshCapacity = capacity;
            m_HandlePool = new HandlePool<int>(capacity, AllocatorManager.Persistent);
            m_ReferenceCount = new int[capacity];
            m_InstanceID = new int[capacity];
            m_MeshID = new InstancedMeshID[capacity];
            m_Mesh = new Mesh[capacity];
            m_InstanceIDToMeshID = new Dictionary<int, InstancedMeshID>(capacity) { [0] = InstancedMeshID.Null };
        }

        public void Dispose()
        {
            m_HandlePool.Dispose();
        }

        public InstancedMeshID Register(Mesh mesh)
        {
            if (mesh == null)
                return InstancedMeshID.Null;

            int instanceID = mesh.GetHashCode();
            if (instanceID == 0)
                return InstancedMeshID.Null;

            if (m_InstanceIDToMeshID.TryGetValue(instanceID, out InstancedMeshID id))
            {
                m_ReferenceCount[m_HandlePool.GetIndex(id)]++;
                return id;
            }

            int index = m_MeshCount++;
            if (index >= m_MeshCapacity)
            {
                int newCapacity = index + 8;
                Array.Resize(ref m_ReferenceCount, newCapacity);
                Array.Resize(ref m_InstanceID, newCapacity);
                Array.Resize(ref m_MeshID, newCapacity);
                Array.Resize(ref m_Mesh, newCapacity);
                m_MeshCapacity = newCapacity;
            }

            id = new InstancedMeshID(m_HandlePool.Allocate(index));
            m_ReferenceCount[index] = 1;
            m_InstanceID[index] = instanceID;
            m_MeshID[index] = id;
            m_Mesh[index] = mesh;

            m_InstanceIDToMeshID.Add(instanceID, id);

            return id;
        }

        public void Unregister(in InstancedMeshID id)
        {
            if (!id.IsCreated)
                return;

            if (!m_HandlePool.TryGetIndex(id, out int index))
                return;

            if (--m_ReferenceCount[index] > 0)
                return;

            m_InstanceIDToMeshID.Remove(m_InstanceID[index]);
            m_HandlePool.Free(id);

            int lastIndex = m_MeshCount - 1;
            if (index != lastIndex)
            {
                m_HandlePool.UpdateIndex(m_MeshID[lastIndex], index);
                m_MeshID[index] = m_MeshID[lastIndex];
                m_InstanceID[index] = m_InstanceID[lastIndex];
                m_Mesh[index] = m_Mesh[lastIndex];
            }

            m_MeshID[lastIndex] = InstancedMeshID.Null;
            m_InstanceID[lastIndex] = 0;
            m_Mesh[lastIndex] = null;

            m_MeshCount--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Mesh GetMesh(in InstancedMeshID id) => !m_HandlePool.TryGetIndex(id, out int index) ? null : m_Mesh[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetMesh(in InstancedMeshID id, out Mesh mesh)
        {
            if (!m_HandlePool.TryGetIndex(id, out int index))
            {
                mesh = null;
                return false;
            }

            mesh = m_Mesh[index];
            return true;
        }
    }
}
