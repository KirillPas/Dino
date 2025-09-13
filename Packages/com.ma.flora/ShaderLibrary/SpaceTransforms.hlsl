// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_SPACE_TRANSFORMS_INCLUDED
#define FLORA_SPACE_TRANSFORMS_INCLUDED

#if SHADER_API_MOBILE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_SWITCH
#pragma warning (disable : 3205) // conversion of larger type to smaller
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.ma.flora/ShaderLibrary/InstancedData.hlsl"

float4x4 GetObjectToWorldMatrix_Flora()
{
    return flora_SampledInstance.ObjectToWorld;
}

float4x4 GetWorldToObjectMatrix_Flora()
{
    return flora_SampledInstance.WorldToObject;
}

float4x4 GetPrevObjectToWorldMatrix_Flora()
{
    return flora_SampledInstance.ObjectToWorld;
}

float4x4 GetPrevWorldToObjectMatrix_Flora()
{
    return flora_SampledInstance.WorldToObject;
}

real GetOddNegativeScale_Flora()
{
    // FIXME: We should be able to just return unity_WorldTransformParams.w, but it is not
    // properly set at the moment, when doing ray-tracing; once this has been fixed in cpp,
    // we can revert back to the former implementation.
    return unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0;
}

float3 TransformObjectToWorld_Flora(float3 positionOS)
{
#if defined(SHADER_STAGE_RAY_TRACING)
    return mul(ObjectToWorld3x4(), float4(positionOS, 1.0)).xyz;
#else
    return mul(GetObjectToWorldMatrix_Flora(), float4(positionOS, 1.0)).xyz;
#endif
}

float3 TransformWorldToObject_Flora(float3 positionWS)
{
#if defined(SHADER_STAGE_RAY_TRACING)
    return mul(WorldToObject3x4(), float4(positionWS, 1.0)).xyz;
#else
    return mul(GetWorldToObjectMatrix_Flora(), float4(positionWS, 1.0)).xyz;
#endif
}

float3 TransformObjectToWorldDir_Flora(float3 dirOS, bool doNormalize = true)
{
#ifndef SHADER_STAGE_RAY_TRACING
    float3 dirWS = mul((float3x3)GetObjectToWorldMatrix_Flora(), dirOS);
#else
    float3 dirWS = mul((float3x3)ObjectToWorld3x4(), dirOS);
#endif
    if (doNormalize)
        return SafeNormalize(dirWS);

    return dirWS;
}

float3 TransformWorldToObjectDir_Flora(float3 dirWS, bool doNormalize = true)
{
#ifndef SHADER_STAGE_RAY_TRACING
    float3 dirOS = mul((float3x3)GetWorldToObjectMatrix_Flora(), dirWS);
#else
    float3 dirOS = mul((float3x3)WorldToObject3x4(), dirWS);
#endif
    if (doNormalize)
        return normalize(dirOS);

    return dirOS;
}

float3 TransformObjectToWorldNormal_Flora(float3 normalOS, bool doNormalize = true)
{
#ifdef UNITY_ASSUME_UNIFORM_SCALING
    return TransformObjectToWorldDir_Flora(normalOS, doNormalize);
#else
    // Normal need to be multiply by inverse transpose
    real3 normalWS = mul(normalOS, (float3x3)GetWorldToObjectMatrix_Flora());
    if (doNormalize)
        return SafeNormalize(normalWS);

    return normalWS;
#endif
}

float3 TransformWorldToObjectNormal_Flora(float3 normalWS, bool doNormalize = true)
{
#ifdef UNITY_ASSUME_UNIFORM_SCALING
    return TransformWorldToObjectDir_Flora(normalWS, doNormalize);
#else
    // Normal need to be multiply by inverse transpose
    real3 normalOS = mul(normalWS, (float3x3)GetObjectToWorldMatrix_Flora());
    if (doNormalize)
        return SafeNormalize(normalOS);

    return normalOS;
#endif
}

real3 TransformTangentToObject_Flora(real3 dirTS, real3x3 tangentToWorld)
{
    real3 normalWS = TransformTangentToWorld(dirTS, tangentToWorld);
    return TransformWorldToObjectNormal_Flora(normalWS);
}

real3 TransformObjectToTangent_Flora(real3 dirOS, real3x3 tangentToWorld)
{
    float3 normalWS = TransformObjectToWorldNormal_Flora(dirOS, false);
    return TransformWorldToTangent(normalWS, tangentToWorld);
}

#undef GetObjectToWorldMatrix
#undef GetWorldToObjectMatrix
#undef GetPrevObjectToWorldMatrix
#undef GetPrevWorldToObjectMatrix
#undef TransformObjectToWorld
#undef TransformWorldToObject
#undef TransformObjectToWorldDir
#undef TransformWorldToObjectDir
#undef TransformObjectToWorldNormal
#undef TransformWorldToObjectNormal
#undef TransformTangentToObject
#undef TransformObjectToTangent

#define GetObjectToWorldMatrix       GetObjectToWorldMatrix_Flora
#define GetWorldToObjectMatrix       GetWorldToObjectMatrix_Flora
#define GetPrevObjectToWorldMatrix   GetPrevObjectToWorldMatrix_Flora
#define GetPrevWorldToObjectMatrix   GetPrevWorldToObjectMatrix_Flora
#define TransformObjectToWorld       TransformObjectToWorld_Flora
#define TransformWorldToObject       TransformWorldToObject_Flora
#define TransformObjectToWorldDir    TransformObjectToWorldDir_Flora
#define TransformWorldToObjectDir    TransformWorldToObjectDir_Flora
#define TransformObjectToWorldNormal TransformObjectToWorldNormal_Flora
#define TransformWorldToObjectNormal TransformWorldToObjectNormal_Flora
#define TransformTangentToObject     TransformTangentToObject_Flora
#define TransformObjectToTangent     TransformObjectToTangent_Flora

#if SHADER_API_MOBILE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_SWITCH
#pragma warning (enable : 3205) // conversion of larger type to smaller
#endif

#endif // FLORA_SPACE_TRANSFORMS_INCLUDED