// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    static class LODGroupUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateFOVHalfAngle(float fieldOfView) 
            => math.tan(math.radians(fieldOfView) * 0.5f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateScreenRelativeMetric(in LODParameters lodParams) 
            => CalculateScreenRelativeMetric(lodParams, CalculateFOVHalfAngle(lodParams.fieldOfView), QualitySettings.lodBias);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateScreenRelativeMetric(in LODParameters lodParams, float halfAngle) 
            => CalculateScreenRelativeMetric(lodParams, halfAngle, QualitySettings.lodBias);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateScreenRelativeMetric(in LODParameters lodParams, float halfAngle, float lodBias)
        {
            float screenRelativeMetric;
            if (lodParams.isOrthographic)
            {
                screenRelativeMetric = 2.0f * lodParams.orthoSize;
            }
            else
            {
                // Half angle at 90 degrees is 1.0 (So we skip halfAngle / 1.0 calculation)
                screenRelativeMetric = 2.0f * halfAngle;
            }

            return screenRelativeMetric / lodBias;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculatePerspectiveDistance(float3 objPosition, float3 camPosition, float sqrScreenRelativeMetric) 
            => math.sqrt(CalculatePerspectiveDistanceSq(objPosition, camPosition, sqrScreenRelativeMetric));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculatePerspectiveDistanceSq(float3 objPosition, float3 camPosition, float sqrScreenRelativeMetric) 
            => math.lengthsq(objPosition - camPosition) * sqrScreenRelativeMetric;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateLODDistance(float relativeScreenHeight, float worldSize) 
            => worldSize / relativeScreenHeight;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 GetWorldReferencePoint(this LODGroup lodGroup) 
            => lodGroup.transform.TransformPoint(lodGroup.localReferencePoint);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetWorldSpaceScale(this LODGroup lodGroup) 
            => math.cmax(math.abs(lodGroup.transform.lossyScale));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetWorldSpaceSize(this LODGroup lodGroup)
            => lodGroup.GetWorldSpaceScale() * lodGroup.size;
    }
}