// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    class FoliageRenderer : IDisposable, IInstancedRenderer
    {
        bool m_IsCreated;
        Transform m_Transform;
        CullingData m_CullingData;
        InstancedPrototype m_Prototype;
        InstancedRendererID m_RendererID;
        NativeArray<LocalTransform> m_InstanceTransforms;
        NativeArray<AxisAlignedBox> m_UnbuiltInstanceBoundsRef;
        int m_InstanceCount;
        int m_InstanceOrderVersion;
        int m_InstanceTransformsVersion;
        bool m_ForceUpdate;

        public void Initialize(Transform transform, InstancedPrototype prototype)
        {
            if (m_IsCreated)
                return;

            m_IsCreated = true;
            m_Transform = transform;
            m_Prototype = prototype;
            m_CullingData = new CullingData();
            m_CullingData.Initialize(this);
            m_CullingData.TreeBuilt += OnCullingDataBuilt;
            m_InstanceTransforms = new NativeArray<LocalTransform>(0, Allocator.Persistent);
            m_UnbuiltInstanceBoundsRef = new NativeArray<AxisAlignedBox>(1, Allocator.Persistent);
            m_InstanceCount = 0;
            m_InstanceOrderVersion = 1;
            m_InstanceTransformsVersion = 1;
            m_RendererID = InstancingSystem.RegisterRenderer(this);
        }

        public void Dispose()
        {
            if (!m_IsCreated)
                return;

            m_IsCreated = false;
            m_CullingData.Dispose();
            m_CullingData.TreeBuilt -= OnCullingDataBuilt;
            m_InstanceTransforms.Dispose();
            m_UnbuiltInstanceBoundsRef.Dispose();
            InstancingSystem.UnregisterRenderer(m_RendererID);
            m_RendererID = InstancedRendererID.Null;
        }

        public bool IsCreated => m_IsCreated;
        public bool IsValid => m_IsCreated && Transform != null;
        public float CullingDistance { get; private set; }
        public Transform Transform => m_Transform;
        public InstancedPrototype Prototype => m_Prototype;
        public CullingData CullingData => m_CullingData;
        public float4x4 LocalToWorldMatrix => Transform.localToWorldMatrix;
        public int InstanceCount => m_InstanceCount;
        public int InstanceOrderVersion => m_InstanceOrderVersion;
        public ReadOnlySpan<LocalTransform> InstanceTransforms => m_InstanceTransforms.AsSpan();
        public int InstanceTransformsVersion => m_InstanceTransformsVersion;
        public bool RequiresUpdate => m_ForceUpdate || (m_InstanceCount > 0 && m_CullingData.OutOfDate);

        void OnCullingDataBuilt(CullingData tree)
        {
            m_InstanceOrderVersion++;
            m_InstanceTransformsVersion++;
            InstancingSystem.MarkRendererDirty(m_RendererID);
        }

        public void UpdatePrototype(InstancedPrototype prototype)
        {
            if (m_Prototype == prototype)
                return;

            m_Prototype = prototype;
            m_CullingData.OutOfDate = true;
        }

        public void UpdateCullingDistance(float distance)
        {
            CullingDistance = distance;
        }

        public void MarkRenderStateDirty()
        {
            InstancingSystem.MarkRendererDirty(m_RendererID);
        }

        public void MarkCullingDataOutOfDate()
        {
            m_CullingData.OutOfDate = true;
        }

        public void ForceUpdate()
        {
            m_ForceUpdate = true;
        }

        public void UpdateTransforms(in FoliageUpdatePacket updatePacket)
        {
            new CombineUnbuiltBoundsJob
            {
                UnbuiltInstanceBounds = updatePacket.Bounds,
                CombinedUnbuiltInstanceBounds = m_UnbuiltInstanceBoundsRef
            }.Run();

            m_InstanceCount = updatePacket.InstanceCount;
            if (m_InstanceCount > 0)
            {
                if (m_InstanceTransforms.Length != m_InstanceCount)
                    m_InstanceTransforms.Resize(m_InstanceCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                m_InstanceTransforms.CopyFrom(updatePacket.Transforms);

                m_CullingData.UnbuiltInstanceBounds.CopyFrom(updatePacket.Bounds);
                m_CullingData.UnbuiltInstanceBoundsCombined = m_UnbuiltInstanceBoundsRef[0];
                m_CullingData.AddUnbuiltIndices(0, m_InstanceCount);
            }

            m_InstanceTransformsVersion++;
            m_InstanceOrderVersion++;
            MarkRenderStateDirty();
        }
    }
}
