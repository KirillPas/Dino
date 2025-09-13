// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_DEBUG_DISPLAY_INCLUDED
#define FLORA_DEBUG_DISPLAY_INCLUDED

#if defined(DEBUG_DISPLAY) && defined(FLORA_PROCEDURAL_INSTANCING_ENABLED)
#define FLORA_DEBUG_DISPLAY_ENABLED
#endif

#if !defined(FLORA_BUILTIN_PIPELINE)
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#endif

#include "Packages/com.ma.flora/Runtime/Rendering/Debugging/DebugDisplayData.cs.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

#define FLORA_MAX_LOD_COUNT (8)

static const float4 flora_LODDebugColors[FLORA_MAX_LOD_COUNT] =
{
    float4(1.0, 1.0, 1.0, 1.0), // LOD 0 - White
    float4(1.0, 0.0, 0.0, 1.0), // LOD 1 - Red
    float4(0.0, 1.0, 0.0, 1.0), // LOD 2 - Green
    float4(0.0, 0.0, 1.0, 1.0), // LOD 3 - Blue
    float4(1.0, 1.0, 0.0, 1.0), // LOD 4 - Yellow
    float4(1.0, 0.0, 1.0, 1.0), // LOD 5 - Fuchsia
    float4(0.0, 1.0, 1.0, 1.0), // LOD 6 - Cyan
    float4(0.5, 0.0, 0.5, 1.0)  // LOD 7 - Purple
};

#if defined(FLORA_DEBUG_DISPLAY_ENABLED)
    int flora_DebugViewMode; // The debug view mode to use
    int flora_DebugLODIndex; // LOD index to display
#endif

float3 FloraColorFromIndex(uint index)
{
    float3 color = float3(float(index & 0xFFu), float((index >> 8u) & 0xFFu), float((index >> 16u) & 0xFFu)) / 255.0;
    return color;
}

float3 FloraDebugRandomColorFromIndex(uint index)
{
    uint h = JenkinsHash(index);
    float3 color = FloraColorFromIndex(h);
    return color;
}

bool GetFloraDebugColor(out half4 debugColor)
{
#if defined(FLORA_DEBUG_DISPLAY_ENABLED)
    if (flora_DebugViewMode == DEBUGSHADEROVERRIDEMODE_LOD)
    {
        debugColor = flora_LODDebugColors[flora_DebugLODIndex];
        return true;
    }
    else if (flora_DebugViewMode == DEBUGSHADEROVERRIDEMODE_RENDERER_ID)
    {
        uint rendererID = flora_SampledInstance.RendererID;
        debugColor = half4(FloraDebugRandomColorFromIndex(rendererID), 1.0);
        return true;
    }
    else if (flora_DebugViewMode == DEBUGSHADEROVERRIDEMODE_RENDER_INDEX)
    {
        uint indexInGroup = flora_SampledInstance.RenderIndex;
        debugColor = half4(FloraColorFromIndex(indexInGroup), 1.0);
        return true;
    }
    else if (flora_DebugViewMode == DEBUGSHADEROVERRIDEMODE_GLOBAL_INSTANCE_ID)
    {
        debugColor = LoadFloraInstancedData_SelectionValue();
        return true;
    }
    else if (flora_DebugViewMode == DEBUGSHADEROVERRIDEMODE_RANDOM_ID)
    {
        uint randomID = asuint(flora_SampledInstance.RandomID);
        debugColor = half4(FloraColorFromIndex(randomID), 1.0);
        return true;
    }
#endif
    debugColor = half4(0.0, 0.0, 0.0, 1.0);
    return false;
}

//-----------------------------------------------------------------------------
// Override Fragment Functions
//-----------------------------------------------------------------------------
#if defined(FLORA_DEBUG_DISPLAY_ENABLED)

//-----------------------------------------------------------------------------
// URP
#if defined(FLORA_UNIVERSAL_PIPELINE)
#ifndef UNIVERSAL_DEBUGGING_COMMON_INCLUDED
#error "Flora DebugDisplay.hlsl must be included after Universal's `DebuggingCommon.hlsl`"
#endif
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// URP - PBR
half4 FloraDebugFragmentURP(InputData inputData, SurfaceData surfaceData)
{
    half4 debugColor;
    if (GetFloraDebugColor(debugColor))
    {
        return debugColor;
    }

    return UniversalFragmentPBR(inputData, surfaceData);
}

#undef UniversalFragmentPBR
#define UniversalFragmentPBR FloraDebugFragmentURP

// URP - Baked Lit
half4 FloraDebugFragmentBakedLit(InputData inputData, SurfaceData surfaceData)
{
    half4 debugColor;
    if (GetFloraDebugColor(debugColor))
    {
        return debugColor;
    }

    return UniversalFragmentBakedLit(inputData, surfaceData);
}

#undef UniversalFragmentBakedLit
#define UniversalFragmentBakedLit FloraDebugFragmentBakedLit

// URP - Blinn-Phong
half4 FloraDebugFragmentBlinnPhong(InputData inputData, SurfaceData surfaceData)
{
    half4 debugColor;
    if (GetFloraDebugColor(debugColor))
    {
        return debugColor;
    }

    return FloraDebugFragmentBlinnPhong(inputData, surfaceData);
}

#undef UniversalFragmentBlinnPhong
#define UniversalFragmentBlinnPhong FloraDebugFragmentBlinnPhong

// URP - Deferred Rendering
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"

FragmentOutput FloraDebugBRDFDataToGbuffer(BRDFData brdfData, InputData inputData, half smoothness, half3 globalIllumination, half occlusion = 1.0)
{
    half4 debugColor;
    if (GetFloraDebugColor(debugColor))
    {
        globalIllumination = debugColor.rgb;
    }

    return BRDFDataToGbuffer(brdfData, inputData, smoothness, globalIllumination, occlusion);
}

#undef BRDFDataToGbuffer
#define BRDFDataToGbuffer FloraDebugBRDFDataToGbuffer

//-----------------------------------------------------------------------------
// HDRP
#elif defined(FLORA_HDRP_PIPELINE)

void FloraApplyDebugToSurfaceData(float3x3 tangentToWorld, inout SurfaceData surfaceData)
{
    ApplyDebugToSurfaceData(tangentToWorld, surfaceData);

    half4 debugColor;
    if (GetFloraDebugColor(debugColor))
    {
        surfaceData.baseColor = debugColor.rgb;
    }
}

#undef ApplyDebugToSurfaceData
#define ApplyDebugToSurfaceData FloraApplyDebugToSurfaceData

//-----------------------------------------------------------------------------
// Builtin TODO
#elif defined(FLORA_BUILTIN_PIPELINE)

// half4 FloraOutputForward(half4 output, half alphaFromSurface)
// {
//     output = OutputForward(output, alphaFromSurface);
//
//     half4 debugColor;
//     if (GetFloraDebugColor(debugColor))
//     {
//         output.rgb = debugColor.rgb;
//     }
//
//     return output;
// }
// #undef OutputForward
// #define OutputForward FloraOutputForward

#endif // FLORA_UNIVERSAL_PIPELINE/FLORA_HDRP_PIPELINE
#endif // FLORA_DEBUG_DISPLAY
#endif // FLORA_DEBUG_DISPLAY_INCLUDED
