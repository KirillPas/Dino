// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_OCCLUSION_COMMON_INCLUDED
#define FLORA_OCCLUSION_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// ----------------------------------------------------------------------------
// Depth utilities

float FarthestDepth(float depthA, float depthB)
{
#if UNITY_REVERSED_Z
    return min(depthA, depthB);
#else
    return max(depthA, depthB);
#endif
}

float FarthestDepth(float4 depths)
{
#if UNITY_REVERSED_Z
    return Min3(depths.x, depths.y, min(depths.z, depths.w));
#else
    return Max3(depths.x, depths.y, max(depths.z, depths.w));
#endif
}

bool IsVisibleAfterOcclusion(float occluderDepth, float queryClosestDepth)
{
#if UNITY_REVERSED_Z
    return queryClosestDepth > occluderDepth;
#else
    return queryClosestDepth < occluderDepth;
#endif
}

#endif