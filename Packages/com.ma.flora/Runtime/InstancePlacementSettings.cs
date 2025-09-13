// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable RedundantDefaultMemberInitializer

using System;
using System.Runtime.CompilerServices;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    /// <summary>
    /// Determines how instances are scaled.
    /// </summary>
    public enum InstanceScalingMode : byte
    {
        /// <summary>Flora instances will have uniform X, Y and Z scales.</summary>
        Uniform,
        /// <summary>Flora instances will have random X, Y and Z scales.</summary>
        Free,
        /// <summary>Locks the X and Z axis scale.</summary>
        LockXZ,
        /// <summary>Locks the X and Y axis scale.</summary>
        LockXY,
        /// <summary>Locks the Y and Z axis scale.</summary>
        LockYZ,
    }
    
    /// <summary>
    /// Settings that determine how instances are placed.
    /// </summary>
    [Serializable]
    public sealed class InstancePlacementSettings
    {
        // --- Density ---
        
        /// <summary>Instances will be placed at this density, specified in instances per 10x10 unit area.</summary>
        [Range(0f, 1000f)] public float Density = 10f;

        /// <summary>The minimum distance between instances.</summary>
        [Range(0f, 100f)] public float Radius = 0f;
    
        /// <summary>Option to override radius used to detect collision with other instances when painting in single instance mode.</summary>
        public bool OverrideSinglePlacementRadius;

        /// <summary>The radius used in single instance mode to detect collision with other instances.</summary>
        [Range(0f, 100f)] public float SinglePlacementRadius = 1f;
    
        /// <summary>Returns the radius to use when painting, optionally modified by single instance mode.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetRadius(bool placeSingle) => (placeSingle && OverrideSinglePlacementRadius) ? SinglePlacementRadius : Radius;
        
        // --- Scaling ---
        
        /// <summary>Specifies instance scaling behavior when painting.</summary>
        public InstanceScalingMode ScalingMode = InstanceScalingMode.Uniform;
    
        /// <summary>Specifies the range of scale, from minimum to maximum, to apply to an instance's X Scale property.</summary>
        [IntervalMin(0.001f)] public Interval ScaleX = 1.0f;
    
        /// <summary>Specifies the range of scale, from minimum to maximum, to apply to an instance's Y Scale property.</summary>
        [IntervalMin(0.001f)] public Interval ScaleY = 1.0f;
    
        /// <summary>Specifies the range of scale, from minimum to maximum, to apply to an instance's Z Scale property.</summary>
        [IntervalMin(0.001f)] public Interval ScaleZ = 1.0f;
        
        // --- Alignment ---

        /// <summary>Specifies a range from minimum to maximum of the offset to apply to a instance's Y position.</summary>
        public Interval VerticalOffset = 0f;

        /// <summary>If enabled, instances will have a random yaw (Z-Axis) rotation applied.</summary>
        public bool RandomizeYaw = true;

        /// <summary>A random pitch adjustment can be applied to each instance, up to the specified angle in degrees, from the original vertical angle.</summary>
        [Range(0f, 359f)] public float RandomPitchAngle = 0f;
        
        /// <summary>Whether instances should have their angle adjusted away from the Y-axis to match the normal of the surface they're painted on.</summary>
        /// <remarks>If <see cref="AlignToSurface"/> is enabled and <see cref="RandomizeYaw"/> is disabled, the instance will be rotated so that the Z axis points down-slope.</remarks>
        public bool AlignToSurface = true;

        /// <summary>The maximum angle in degrees instances can be rotated away from the surface normal when <see cref="AlignToSurface"/> is enabled.</summary>
        [Range(0f, 359f)] public float AlignToSurfaceMaxAngle = 0f;

        /// <summary>Whether the normal should be averaged on a number of samples around the hit location.</summary>
        /// <remarks>Will average normal based on the instance radius (this has a cost as it will do extra raycasts).</remarks>
        public bool AverageNormal = false;

        /// <summary>Whether <see cref="AverageNormal"/> should only test against the first collider hit by the raycast.</summary>
        public bool AverageNormalSingleComponent = true;

        /// <summary>The amount of rays to cast when averaging the normal.</summary>
        [Min(1)] public int AverageNormalSampleCount = 10;
        
        // --- Masking ---
        
        /// <summary>Instances will only be placed on surfaces sloping in the specified angle range from the horizontal.</summary>
        [IntervalClamp(0f, 359f)] public Interval SlopeMask = new Interval(0f, 45f);

        /// <summary>The valid altitude range where instances will be placed, specified using minimum and maximum world coordinate Y values.</summary>
        public Interval HeightMask = new Interval(-2500.0f, 2500.0f);
        
        // --- Collision ---

        /// <summary>The collision mask used when performing the overlap check.</summary>
        public LayerMask CollisionLayerMask = -1;
        
        /// <summary>If true, will ensure the lowest part of the prototype is not overhanging the surface it is placed on.</summary>
        public bool CheckColliderOverhang = false;

        /// <summary>If checked, an overlap test with existing world colliders is performed before each instance is placed.</summary>
        public bool CheckWorldCollisions = false;

        /// <summary>The instance's collision bounding box will be scaled by the specified amount before performing the overlap check.</summary>
        /// <seealso cref="CheckWorldCollisions"/>
        public float3 CollisionBoundsScale = 0.9f;
        
        /// <summary>Sanitizes the instance placement settings, ensuring all values are within valid ranges.</summary>
        public void Sanitize()
        {
            Density = math.max(Density, 0.0f);
            Radius = math.max(Radius, 0.0f);
            SinglePlacementRadius = math.max(SinglePlacementRadius, 0.0f);
            
            ScaleX.Min = math.max(ScaleX.Min, 0.001f);
            ScaleX.Max = math.max(ScaleX.Max, 0.001f);
            ScaleY.Min = math.max(ScaleY.Min, 0.001f);
            ScaleY.Max = math.max(ScaleY.Max, 0.001f);
            ScaleZ.Min = math.max(ScaleZ.Min, 0.001f);
            ScaleZ.Max = math.max(ScaleZ.Max, 0.001f);

            switch (ScalingMode)
            {
                case InstanceScalingMode.Uniform:
                    ScaleY = ScaleX;
                    ScaleZ = ScaleX;
                    break;
                case InstanceScalingMode.LockXZ:
                    ScaleZ = ScaleX;
                    break;
                case InstanceScalingMode.LockXY:
                    ScaleY = ScaleX;
                    break;
                case InstanceScalingMode.LockYZ:
                    ScaleZ = ScaleY;
                    break;
            }
            
            RandomPitchAngle = MathUtility.Repeat(RandomPitchAngle, 359.0f);
            AlignToSurfaceMaxAngle = MathUtility.Repeat(AlignToSurfaceMaxAngle, 359.0f);
            AverageNormalSampleCount = math.max(1, AverageNormalSampleCount);
            
            SlopeMask.Min = MathUtility.Repeat(SlopeMask.Min, 359.0f);
            SlopeMask.Max = MathUtility.Repeat(SlopeMask.Max, 359.0f);
            
            CollisionBoundsScale = math.max(CollisionBoundsScale, 0.0f);
        }
    }
}