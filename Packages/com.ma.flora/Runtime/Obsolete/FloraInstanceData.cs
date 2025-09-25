// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Collections;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace MA.Flora
{
    [PreferBinarySerialization]
    [Obsolete]
    public sealed class FloraInstanceData : ScriptableObject
    {
        public int InstanceCount
        {
            get => m_LocalTransformData.Count;
            set => throw new Exception("FloraInstanceData is obsolete.");
        }
        
        public LeanList<LocalTransform> TransformData => m_LocalTransformData;
        [SerializeField] LeanList<LocalTransform> m_LocalTransformData = new LeanList<LocalTransform>();
        
        public int PerInstanceAttributeCount
        {
            get => m_PerInstanceAttributeCount;
            set => throw new Exception("FloraInstanceData is obsolete.");
        }
        [FormerlySerializedAs("m_AttributeCount")] [SerializeField] int m_PerInstanceAttributeCount;
        
        public LeanList<float4> AttributeData => m_AttributeData;
        [SerializeField] LeanList<float4> m_AttributeData = new LeanList<float4>();
        
        public float4[] DefaultAttributeValues
        {
            get => m_DefaultAttributeValues;
            set => throw new Exception("FloraInstanceData is obsolete.");
        }
        [SerializeField] float4[] m_DefaultAttributeValues = Array.Empty<float4>();
        
        public bool AttributesEnabled => m_PerInstanceAttributeCount > 0;
        public bool AttributesValid => m_AttributeData.Count == m_PerInstanceAttributeCount * m_LocalTransformData.Count;

        public LeanList<SHCoefficients> LightingData => m_LightingData;
        [SerializeField] LeanList<SHCoefficients> m_LightingData = new LeanList<SHCoefficients>();
        
        public bool HasValidLightingData => m_LightingDataValid && m_LightingData.Count == m_LocalTransformData.Count;
        [SerializeField] bool m_LightingDataValid;
        
        public bool IsValidInstance(int instanceIndex) => m_LocalTransformData.IsValidIndex(instanceIndex);

        public void ReserveAdditional(int additionalCapacity) => throw new Exception("FloraInstanceData is obsolete.");
        public void Resize(int newInstanceCount, NativeArrayOptions options = NativeArrayOptions.ClearMemory) => throw new Exception("FloraInstanceData is obsolete.");
        public void AddInstance(LocalTransform instanceTransform, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceData is obsolete.");

        public void AddInstances(ReadOnlySpan<LocalTransform> newTransforms, float3 worldToLocalOffset = default) => throw new Exception("FloraInstanceData is obsolete.");
        public LocalTransform GetTransform(int instanceIndex) => m_LocalTransformData[instanceIndex];
        public void SetTransform(int instanceIndex, LocalTransform newTransform) => throw new Exception("FloraInstanceData is obsolete.");
        public void SetTransforms(int startInstanceIndex, ReadOnlySpan<LocalTransform> newTransforms) => throw new Exception("FloraInstanceData is obsolete.");
        public void RemoveInstance(int instanceIndex) => throw new Exception("FloraInstanceData is obsolete.");
        public void RemoveInstances(ReadOnlySpan<int> instancesToRemove) => throw new Exception("FloraInstanceData is obsolete.");
        public void RemoveInstanceAtSwapBack(int instanceIndex) => throw new Exception("FloraInstanceData is obsolete.");
        public void ClearInstances() => throw new Exception("FloraInstanceData is obsolete.");

        public void ResizeAttributes(int newAttributeCount) => throw new Exception("FloraInstanceData is obsolete.");
        public void ResetAttributes() => throw new Exception("FloraInstanceData is obsolete.");
        public void ResetAttribute(int instanceIndex, int attributeIndex) => throw new Exception("FloraInstanceData is obsolete.");
        public void ResetAttributes(int instanceIndex) => throw new Exception("FloraInstanceData is obsolete.");
        public float4 GetAttribute(int instanceIndex, int attributeIndex) => m_AttributeData[instanceIndex * m_PerInstanceAttributeCount + attributeIndex];
        public void SetAttribute(int instanceIndex, int attributeIndex, float4 attribute) => throw new Exception("FloraInstanceData is obsolete.");
        public void SetAttributes(int instanceIndex, ReadOnlySpan<float4> newAttributes) => throw new Exception("FloraInstanceData is obsolete.");

        public void InvalidateLightingData() => throw new Exception("FloraInstanceData is obsolete.");
        public SHCoefficients GetLightingData(int instanceIndex) => LightingData[instanceIndex];

        public bool TryBuildLightingData(float3 worldOffset, float3 localOffset) => throw new Exception("FloraInstanceData is obsolete.");
    }
}
