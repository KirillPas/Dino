// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Collections;
using UnityEngine;

namespace MA.Flora.Rendering
{
    sealed class StaticOcclusionContext : IDisposable
    {
        CullingGroup m_CullingGroup;
        UnsafeArray<bool> m_Culled;

        public UnsafeArray<bool> Culled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_Culled;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StaticOcclusionContext(Camera camera)
        {
            m_CullingGroup = new CullingGroup
            {
                targetCamera = camera,
                enabled = false,
                onStateChanged = OnStateChanged
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            m_CullingGroup?.Dispose();
            m_CullingGroup = null;
            m_Culled.Dispose();
            m_Culled = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsVisible(int index) => !m_Culled[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBoundingSpheres(BoundingSphere[] cullingSpheres, int count)
        {
            if (count == 0)
            {
                m_Culled.Dispose();
                m_Culled = default;
                m_CullingGroup.SetBoundingSphereCount(0);
                m_CullingGroup.enabled = false;
            }
            else
            {
                if (!m_Culled.IsCreated)
                    m_Culled = new UnsafeArray<bool>(cullingSpheres.Length, AllocatorManager.Persistent);
                else
                    m_Culled.Resize(cullingSpheres.Length, AllocatorManager.Persistent);

                m_CullingGroup.SetBoundingSpheres(cullingSpheres);
                m_CullingGroup.SetBoundingSphereCount(count);
                m_CullingGroup.enabled = true;
            }
        }

        void OnStateChanged(CullingGroupEvent sphere)
        {
            m_Culled[sphere.index] = !sphere.isVisible;
        }
    }

    sealed class StaticOcclusionManager : IDisposable
    {
        InstancingContext m_Context;
        ElementAllocator m_SphereAllocator;
        NativeArray<BoundingSphere> m_SphereData;
        BoundingSphere[] m_SphereArray;
        StaticOcclusionContext[] m_Contexts;
        int m_UsedCameraCount;
        bool m_NeedsUpdate;

        public BoundingSphere[] CullingSpheres
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_SphereArray;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_Contexts is { Length: > 0 } && m_SphereArray is { Length: > 0 };
        }

        public bool NeedsUpdate
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_NeedsUpdate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StaticOcclusionManager(InstancingContext context)
        {
            m_Context = context;
            m_SphereAllocator = new ElementAllocator(64, AllocatorManager.Persistent);
            m_SphereData = new NativeArray<BoundingSphere>(64, Allocator.Persistent);
            m_SphereArray = Array.Empty<BoundingSphere>();
            m_Contexts = Array.Empty<StaticOcclusionContext>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            foreach (StaticOcclusionContext context in m_Contexts)
                context?.Dispose();

            m_Contexts = Array.Empty<StaticOcclusionContext>();
            m_SphereArray = Array.Empty<BoundingSphere>();
            m_SphereData.Dispose();
            m_SphereAllocator.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetNeedsUpdate()
        {
            m_NeedsUpdate = true;
        }

        public void NextFrame()
        {
            m_SphereAllocator.MergeFree();

            if (m_Contexts.Length > 0 && m_NeedsUpdate)
                UpdateContextSpheres();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetContext(InstancedCameraID cameraID, out StaticOcclusionContext staticOcclusionContext)
        {
            staticOcclusionContext = null;
            if (!m_Contexts.IsValidIndex(cameraID))
                return false;

            staticOcclusionContext = m_Contexts[cameraID];
            return staticOcclusionContext is { Culled: { IsCreated: true } };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterCamera(InstancedCameraID cameraID, Camera camera)
        {
            if (!m_Contexts.IsValidIndex(cameraID))
                Array.Resize(ref m_Contexts, cameraID + 1);

            if (m_Contexts[cameraID] == null)
            {
                m_Contexts[cameraID] = new StaticOcclusionContext(camera);
                m_NeedsUpdate = true;
                m_UsedCameraCount++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnregisterCamera(InstancedCameraID cameraID)
        {
            if (m_Contexts.IsValidIndex(cameraID))
            {
                if (m_Contexts[cameraID] != null)
                {
                    m_Contexts[cameraID].Dispose();
                    m_Contexts[cameraID] = null;
                    m_UsedCameraCount--;
                    if (m_UsedCameraCount == 0)
                        m_Context.DestroyStaticOcclusionManager();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NativeArray<BoundingSphere> GetBoundingSpheres(int offset, int count) => m_SphereData.GetSubArray(offset, count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Allocate(int count)
        {
            if (count == 0) return -1;

            int offset = m_SphereAllocator.Allocate(count);
            int newCapacity = MathUtility.NextMultipleOf(m_SphereAllocator.MaxAllocatedSize, 64);
            if (newCapacity != m_SphereData.Length)
            {
                if (newCapacity == 0)
                {
                    m_SphereData.Dispose();
                    m_SphereData = default;
                }
                else
                {
                    if (!m_SphereData.IsCreated)
                        m_SphereData = new NativeArray<BoundingSphere>(newCapacity, Allocator.Persistent);
                    else
                        m_SphereData.Resize(newCapacity, Allocator.Persistent);
                }
            }

            m_NeedsUpdate = true;

            return offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Free(int offset, int count)
        {
            if (count == 0) return;
            m_SphereAllocator.Free(offset, count);
            for (int i = offset; i < offset + count; i++)
                m_SphereData[i] = default;
            m_NeedsUpdate = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateContextSpheres()
        {
            if (m_NeedsUpdate)
            {
                m_NeedsUpdate = false;

                // Ensure the managed array is the correct size
                if (m_SphereArray.Length != m_SphereData.Length)
                    Array.Resize(ref m_SphereArray, m_SphereData.Length);

                // Copy the unmanaged sphere store to the managed array
                m_SphereData.GetSubArray(0, m_SphereData.Length).CopyTo(m_SphereArray);

                // Update the bounding spheres for each context
                foreach (StaticOcclusionContext context in m_Contexts)
                    context?.SetBoundingSpheres(m_SphereArray, m_SphereAllocator.MaxAllocatedSize);
            }
        }
    }
}
