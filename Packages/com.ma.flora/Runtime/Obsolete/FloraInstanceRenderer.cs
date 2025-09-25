// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using MA.Collections;
using MA.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    [Obsolete]
    public sealed class FloraInstanceRenderer : MonoBehaviour
    {
        enum Version { None, Initial, BoundsFix }

        public GameObject ModelPrefab
        {
            get => m_ModelPrefab;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [SerializeField] GameObject m_ModelPrefab;
        
        public FloraInstanceData InstanceData
        {
            get => m_InstanceData;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [SerializeField] FloraInstanceData m_InstanceData;
        
        public int InstanceCount => m_InstanceData ? m_InstanceData.InstanceCount : 0;
        
        public bool CreatePrefabInstances
        {
            get => m_CreatePrefabInstances;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [SerializeField] bool m_CreatePrefabInstances;
        
        public bool PrefabInstancesContributeGI
        {
            get => m_PrefabInstancesContributeGI;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [SerializeField] bool m_PrefabInstancesContributeGI;
        
        public bool CalculateInterpolatedLightProbes
        {
            get => m_CalculateInterpolatedLightProbes;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [SerializeField] bool m_CalculateInterpolatedLightProbes;
        
        public bool HasValidLightingData => m_InstanceData && m_InstanceData.HasValidLightingData;
        public int AttributeCount => m_InstanceData ? m_InstanceData.PerInstanceAttributeCount : 0;
        public object CullingTree => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public AxisAlignedBox UnbuiltInstanceBoundsCombined => throw new Exception("FloraInstanceRenderer is obsolete.");
        public LeanList<AxisAlignedBox> UnbuiltInstanceBoundsList => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public int InstanceCountToRender => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public IReadOnlyList<Transform> PrefabTransforms => m_PrefabTransforms;
        [SerializeField] List<Transform> m_PrefabTransforms = new List<Transform>();
        
        public float MaxRenderDistance
        {
            get => m_MaxRenderDistance;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [Min(0), SerializeField] float m_MaxRenderDistance;
        
        public float StartFadeDistance
        {
            get => m_StartFadeDistance;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [Min(0), SerializeField] float m_StartFadeDistance;
        
        public bool CullShadowsSeparately
        {
            get => m_CullShadowsSeparately;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [SerializeField] bool m_CullShadowsSeparately = true;
        
        public bool StaticOcclusionCullingEnabled
        {
            get => m_StaticOcclusionCullingEnabled;
            set => throw new Exception("FloraInstanceRenderer is obsolete.");
        }
        [SerializeField] bool m_StaticOcclusionCullingEnabled;
        
        public bool AutoRebuildTreeOnInstanceChanges { get; set; }
        
        public bool IsRenderStateDirty => throw new Exception("FloraInstanceRenderer is obsolete.");
        public bool IsValidForRendering => throw new Exception("FloraInstanceRenderer is obsolete.");
        public GameObject Owner => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        internal object RenderData => throw new Exception("FloraInstanceRenderer is obsolete.");

        [SerializeField] AxisAlignedBox m_CachedModelBounds = AxisAlignedBox.Empty;
#if UNITY_EDITOR
        [SerializeField] List<MeshRenderer> m_GIContributors = new List<MeshRenderer>();
#endif
        
        public static FloraInstanceRenderer Create(FloraInstanceData instanceData = null) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public static FloraInstanceRenderer Create(GameObject gameObject, FloraInstanceData instanceData = null) => throw new Exception("FloraInstanceRenderer is obsolete.");

        public void MarkRenderStateDirty(bool forceRenderDataRebuild = false) => throw new Exception("FloraInstanceRenderer is obsolete.");

        public bool IsValidInstance(int instanceIndex) => m_InstanceData.IsValidInstance(instanceIndex);
        public void ReserveAdditional(int additionalCapacity) => throw new Exception("FloraInstanceRenderer is obsolete.");

        public LocalTransform GetInstanceTransform(int instanceIndex, Space space)
        {
            LocalTransform result = m_InstanceData.GetTransform(instanceIndex);
            if (space == Space.World)
                result = result.Translate(transform.transform.localToWorldMatrix.GetPosition());

            return result;
        }

        public void AddInstance(LocalTransform instanceTransform, Space space) => throw new Exception("FloraInstanceRenderer is obsolete.");

        public void AddInstance(LocalTransform instanceTransform, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void AddInstances(ReadOnlySpan<LocalTransform> instanceTransforms, Space space) => throw new Exception("FloraInstanceRenderer is obsolete.");

        public void AddInstances(ReadOnlySpan<LocalTransform> instanceTransforms, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public JobHandle ScheduleAddInstances(NativeArray<LocalTransform> instanceTransforms, float3 worldToLocalOffset, JobHandle dependsOn = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void UpdateInstanceTransform(int instanceIndex, LocalTransform instanceTransform, Space space) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void UpdateInstanceTransform(int instanceIndex, LocalTransform instanceTransform, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void UpdateInstanceTransforms(int startInstanceIndex, ReadOnlySpan<LocalTransform> instanceTransforms, Space space) => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public void UpdateInstanceTransforms(int startInstanceIndex, ReadOnlySpan<LocalTransform> instanceTransforms, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public JobHandle ScheduleUpdateInstanceTransforms(int startInstanceIndex, NativeArray<LocalTransform> instanceTransforms, float3 worldToLocalOffset, JobHandle dependsOn = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void UpdateInstanceTransforms(ReadOnlySpan<int> instanceIndices, ReadOnlySpan<LocalTransform> instanceTransforms, Space space) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void UpdateInstanceTransforms(ReadOnlySpan<int> instanceIndices, ReadOnlySpan<LocalTransform> instanceTransforms, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public JobHandle ScheduleUpdateInstanceTransforms(NativeArray<int> instanceIndices, NativeArray<LocalTransform> instanceTransforms, float3 worldToLocalOffset, JobHandle dependsOn = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public void RemoveInstance(int instanceIndex) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void RemoveInstances(ReadOnlySpan<int> instancesToRemove) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void RemoveSortedInstances(ReadOnlySpan<int> sortedInstancesToRemove) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public JobHandle ScheduleRemoveInstances(NativeArray<int> instancesToRemove, JobHandle dependsOn = default) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public JobHandle ScheduleRemoveSortedInstances(NativeArray<int> sortedInstancesToRemove, JobHandle dependsOn = default) => throw new Exception("FloraInstanceRenderer is obsolete.");

        public void ClearInstances() => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public void ResetAttribute(int instanceIndex, int attributeIndex) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public float4 GetAttribute(int instanceIndex, int attributeIndex) => m_InstanceData.GetAttribute(instanceIndex, attributeIndex);
        public void SetAttribute(int instanceIndex, int attributeIndex, float4 attribute) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public void SetAttributes(int instanceIndex, ReadOnlySpan<float4> attributes) => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public NativeArray<int> GetInstancesOverlappingBox(AxisAlignedBox bounds, Space space, Allocator allocator) => throw new Exception("FloraInstanceRenderer is obsolete.");
        public NativeArray<int> GetInstancesOverlappingSphere(in Sphere sphere, Space space, Allocator allocator) => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public bool TryBuildCullingTree(bool async, bool force = false) => throw new Exception("FloraInstanceRenderer is obsolete.");
        
        public AxisAlignedBox CalculateBounds(Space space) => throw new Exception("FloraInstanceRenderer is obsolete.");
    }
}
