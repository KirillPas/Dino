// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_GEOMETRY_UTILITIES_INCLUDED
#define FLORA_GEOMETRY_UTILITIES_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

struct CylinderBound
{
    float3 Center;
    float3 Axis;
    float HalfHeight;
    float CapRadius;
};

struct SphereBound
{
    float3 Center;
    float Radius;
};

float CompleteSinCos(float sinOrCos)
{
    return sqrt(max(1.0f - sinOrCos*sinOrCos, 0.0f));
}

float2 ProjectedHalfLengths(CylinderBound cylinder, float3 planeNormal)
{
    float absCosTheta = abs(dot(planeNormal, cylinder.Axis));
    float sinTheta = CompleteSinCos(absCosTheta);

    float h = cylinder.HalfHeight;
    float r = cylinder.CapRadius;

    float halfLengthAlongNormal = absCosTheta * h + sinTheta * r;
    float halfLengthInPlane = max(sinTheta * h + absCosTheta * r, r); // ellipse, so use max of two axis lengths

    return float2(halfLengthInPlane, halfLengthAlongNormal);
}

struct BoundingObjectData
{
    float3 FrontCenterPosRWS;
    float2 CenterPosNDC;
    float2 RadialPosNDC;
};

BoundingObjectData CalculateBoundingObjectData(SphereBound boundingSphere,
    float4x4 viewProjMatrix,
    float4 viewOriginWorldSpace,
    float4 radialDirWorldSpace,
    float4 facingDirWorldSpace)
{
    const float3 centerPosRWS = boundingSphere.Center - viewOriginWorldSpace.xyz;

    const float3 radialVec = abs(boundingSphere.Radius) * radialDirWorldSpace.xyz;
    const float3 facingVec = abs(boundingSphere.Radius) * facingDirWorldSpace.xyz;

    BoundingObjectData data;
    data.CenterPosNDC = ComputeNormalizedDeviceCoordinates(centerPosRWS, viewProjMatrix);
    data.RadialPosNDC = ComputeNormalizedDeviceCoordinates(centerPosRWS + radialVec, viewProjMatrix);
    data.FrontCenterPosRWS = centerPosRWS + facingVec;
    return data;
}

BoundingObjectData CalculateBoundingObjectData(CylinderBound cylinderBound,
    float4x4 viewProjMatrix,
    float4 viewOriginWorldSpace,
    float4 radialDirWorldSpace,
    float4 facingDirWorldSpace)
{
    const float3 centerPosRWS = cylinderBound.Center - viewOriginWorldSpace.xyz;

    const float2 halfLengths = ProjectedHalfLengths(cylinderBound, facingDirWorldSpace.xyz);
    const float3 radialVec = halfLengths.x * radialDirWorldSpace.xyz;

    BoundingObjectData data;
    data.CenterPosNDC = ComputeNormalizedDeviceCoordinates(centerPosRWS, viewProjMatrix);
    data.RadialPosNDC = ComputeNormalizedDeviceCoordinates(centerPosRWS + radialVec, viewProjMatrix);
    data.FrontCenterPosRWS = centerPosRWS + halfLengths.y * facingDirWorldSpace.xyz;
    return data;
}

#endif // FLORA_GEOMETRY_UTILITIES_INCLUDED