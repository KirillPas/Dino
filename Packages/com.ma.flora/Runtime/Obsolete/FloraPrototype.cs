// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.InteropServices;
using MA.Core;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Random = Unity.Mathematics.Random;

namespace MA.Flora
{
    [Obsolete]
    public enum FloraScalingMode : byte { Uniform, Free, LockXZ, LockXY, LockYZ, }

    [Obsolete]
    public enum FloraVertexColorChannel : byte { Red, Green, Blue, Alpha, }

    [Serializable, Obsolete]
    public struct FloraVertexColorChannelMask
    {
        /// <summary>Defines the default values for a vertex color channel mask.</summary>
        public static FloraVertexColorChannelMask Default = new FloraVertexColorChannelMask
        {
            Enabled = false,
            InvertThreshold = false,
            Threshold = 0.5f,
        };
        
        public bool Enabled;
        public bool InvertThreshold;
        public float Threshold;
    }

    [Serializable, Obsolete]
    [StructLayout(LayoutKind.Sequential)]
    public struct FloraPlacementSettings
    {
        public static FloraPlacementSettings Default => new FloraPlacementSettings
        {
            Density = 10.0f,
            Radius = 1.0f,
            OverrideSingleInstanceModeRadius = false,
            SingleInstanceModeRadius = 0,
            ScalingMode = FloraScalingMode.Uniform,
            ScaleX = 1.0f,
            ScaleY = 1.0f,
            ScaleZ = 1.0f,
            VerticalOffset = default,
            SlopeAngleRange = new Interval(0.0f, 45.0f),
            HeightRange = new Interval(-2500.0f, 2500.0f),
            AlignToNormal = true,
            MaximumAlignmentAngle = 0,
            AverageNormal = false,
            AverageNormalSingleComponent = true,
            AverageNormalSampleCount = 10,
            RandomizeYaw = true,
            RandomPitchAngle = 0,
            CheckCollisionWithWorld = false,
            CollisionMask = -1,
            CollisionScale = 0.9f * Vector3.one,
            CollisionCheckOverhangs = true,
            VertexColorChannelMaskRed = FloraVertexColorChannelMask.Default,
            VertexColorChannelMaskGreen = FloraVertexColorChannelMask.Default,
            VertexColorChannelMaskBlue = FloraVertexColorChannelMask.Default,
            VertexColorChannelMaskAlpha = FloraVertexColorChannelMask.Default,
        };

        public float Density;
        public float Radius;
        
        public bool OverrideSingleInstanceModeRadius;
        public float SingleInstanceModeRadius;
        
        public FloraScalingMode ScalingMode;
        public Interval ScaleX;
        public Interval ScaleY;
        public Interval ScaleZ;

        public Interval VerticalOffset;
        
        public Interval SlopeAngleRange;
        public Interval HeightRange;
        
        public bool AlignToNormal;
        public float MaximumAlignmentAngle;
        public bool AverageNormal;
        public bool AverageNormalSingleComponent;

        public int AverageNormalSampleCount;
        public bool RandomizeYaw;
        public float RandomPitchAngle;
        
        public bool CheckCollisionWithWorld;
        public bool CollisionCheckOverhangs;
        public LayerMask CollisionMask;
        public Vector3 CollisionScale;
        
        public FloraVertexColorChannelMask VertexColorChannelMaskRed;
        public FloraVertexColorChannelMask VertexColorChannelMaskGreen;
        public FloraVertexColorChannelMask VertexColorChannelMaskBlue;
        public FloraVertexColorChannelMask VertexColorChannelMaskAlpha;
        
        public void Sanitize()
        {
            Density = Mathf.Max(0.0f, Density);
            
            Radius = Mathf.Max(0.0f, Radius);
            SingleInstanceModeRadius = Mathf.Max(0.0f, SingleInstanceModeRadius);
            
            ScaleX.Min = Mathf.Max(ScaleX.Min, 0.001f);
            ScaleX.Max = Mathf.Max(ScaleX.Max, 0.001f);
            ScaleY.Min = Mathf.Max(ScaleY.Min, 0.001f);
            ScaleY.Max = Mathf.Max(ScaleY.Max, 0.001f);
            ScaleZ.Min = Mathf.Max(ScaleZ.Min, 0.001f);
            ScaleZ.Max = Mathf.Max(ScaleZ.Max, 0.001f);

            switch (ScalingMode)
            {
                case FloraScalingMode.Uniform:
                    ScaleY = ScaleX;
                    ScaleZ = ScaleX;
                    break;
                case FloraScalingMode.LockXZ:
                    ScaleZ = ScaleX;
                    break;
                case FloraScalingMode.LockXY:
                    ScaleY = ScaleX;
                    break;
                case FloraScalingMode.LockYZ:
                    ScaleZ = ScaleY;
                    break;
            }
            
            SlopeAngleRange.Min = Mathf.Repeat(SlopeAngleRange.Min, 359.0f);
            SlopeAngleRange.Max = Mathf.Repeat(SlopeAngleRange.Max, 359.0f);
            
            RandomPitchAngle = Mathf.Repeat(RandomPitchAngle, 359.0f);
            MaximumAlignmentAngle = Mathf.Repeat(MaximumAlignmentAngle, 359.0f);
            AverageNormalSampleCount = Mathf.Max(1, AverageNormalSampleCount);
        }

        public readonly float GetRadius(bool singleInstanceMode) => throw new Exception("FloraPlacementSettings is obsolete.");
        public readonly float3 GetRandomScale(ref Random random) => throw new Exception("FloraPlacementSettings is obsolete.");
        public FloraVertexColorChannelMask GetVertexColorChannelMask(FloraVertexColorChannel channel) => throw new Exception("FloraPlacementSettings is obsolete.");
    }

    [Obsolete]
    public sealed class FloraPrototype : ScriptableObject
    {
        public GameObject ModelPrefab
        {
            get => m_ModelPrefab;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [SerializeField] GameObject m_ModelPrefab;
        
        public AxisAlignedBox ModelBounds => m_ModelBounds;
        [SerializeField] AxisAlignedBox m_ModelBounds;
        
        public bool SpawnPrefabInstances
        {
            get => m_SpawnPrefabInstances;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [SerializeField] bool m_SpawnPrefabInstances = false;
        
        public bool PrefabInstancesContributeGI
        {
            get => m_PrefabInstancesContributeGI;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [SerializeField] bool m_PrefabInstancesContributeGI;
        
        public FloraPlacementSettings PlacementSettings
        {
            get => m_PlacementSettings;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [SerializeField] FloraPlacementSettings m_PlacementSettings = FloraPlacementSettings.Default;
        
        public int PerInstanceAttributeCount
        {
            get => m_PerInstanceAttributeCount;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [FormerlySerializedAs("m_AttributeCount")] [SerializeField, Range(0, 8)] int m_PerInstanceAttributeCount;
        
        public float4[] DefaultAttributeValues => m_DefaultAttributeValues;
        [SerializeField] float4[] m_DefaultAttributeValues = Array.Empty<float4>();
        
        public float MaxRenderDistance
        {
            get => m_MaxRenderDistance;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [Min(0), SerializeField] float m_MaxRenderDistance;
        
        public float StartFadeDistance
        {
            get => m_StartFadeDistance;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [Min(0), SerializeField] float m_StartFadeDistance;
        
        public bool CullShadowsSeparately
        {
            get => m_CullShadowsSeparately;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [SerializeField] bool m_CullShadowsSeparately = true;
        public bool EnableDensityScaling
        {
            get => m_EnableDensityScaling;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [SerializeField] bool m_EnableDensityScaling;
        
        public bool CalculateInterpolatedLightProbes
        {
            get => m_CalculateInterpolatedLightProbes;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [SerializeField] bool m_CalculateInterpolatedLightProbes;
        
        public Vector3 InterpolatedLightProbeOffset
        {
            get => m_InterpolatedLightProbeOffset;
            set => throw new Exception("FloraPrototype is obsolete.");
        }
        [SerializeField] Vector3 m_InterpolatedLightProbeOffset;
        
        public SerializableGuid ChangesetGuid => m_ChangesetGuid;
        [SerializeField] SerializableGuid m_ChangesetGuid;
        
        [SerializeField] internal float LowestBoundingRadius;
        [SerializeField] internal float LowestBoundingCenterX;
        [SerializeField] internal float LowestBoundingCenterZ;
    }
}
