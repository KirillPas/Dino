// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_INSTANCING_INCLUDED
#define FLORA_INSTANCING_INCLUDED

//-----------------------------------------------------------------------------
// Pragmas
//-----------------------------------------------------------------------------

// Note: #include_with_pragmas does not work with #pragma instancing_options
// Use this pragma to enable Flora instancing:
//      #pragma instancing_options procedural:SetupFloraInstancingData forwardadd

// For selection and picking to work, instancing must be enabled with "ScenePickingPass" and "SceneSelectionPass".
//  Use #include_with_pragmas when you include this file in your shader.

#ifdef SCENESELECTIONPASS
#pragma multi_compile _ PROCEDURAL_INSTANCING_ON
#endif

#ifdef SCENEPICKINGPASS
#pragma multi_compile _ PROCEDURAL_INSTANCING_ON
#endif

//-----------------------------------------------------------------------------
// Defines
//-----------------------------------------------------------------------------

// Flora version number (X0X0X0)
#define FLORA_VERSION 200000

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    #define FLORA_PROCEDURAL_INSTANCING_ENABLED
#endif

#if defined(SHADER_STAGE_COMPUTE)
    #define FLORA_COMPUTE_SHADER
#elif defined(UNIVERSAL_PIPELINE_CORE_INCLUDED)
    #define FLORA_UNIVERSAL_PIPELINE
#elif defined(UNITY_MATERIAL_INCLUDED) //TODO: Find a better way to detect HDRP?
    #define FLORA_HDRP_PIPELINE
#elif defined(UNITY_CG_INCLUDED) || defined(BUILTIN_TARGET_API)
    #define FLORA_BUILTIN_PIPELINE
#else
    #define FLORA_CUSTOM_PIPELINE
#endif

#if defined(FLORA_UNIVERSAL_PIPELINE) || defined(FLORA_BUILTIN_PIPELINE)
    #define FLORA_USE_STANDARD_REFLECTION_PROBES
#endif

#include "Packages/com.ma.flora/Config/ShaderConfig.hlsl"
#include "Packages/com.ma.flora/ShaderLibrary/InstancedData.hlsl"
#include "Packages/com.ma.flora/ShaderLibrary/DebugDisplay.hlsl"

//-----------------------------------------------------------------------------
// Procedural Instancing
//-----------------------------------------------------------------------------
#ifdef FLORA_PROCEDURAL_INSTANCING_ENABLED

CBUFFER_START(flora_InstanceGlobalValues)
    float4 flora_ProbesOcclusion;
    float4 flora_SpecCube0_HDR;
    float4 flora_SpecCube1_HDR;
    float4 flora_SHAr;
    float4 flora_SHAg;
    float4 flora_SHAb;
    float4 flora_SHBr;
    float4 flora_SHBg;
    float4 flora_SHBb;
    float4 flora_SHC;
CBUFFER_END

#define FLORA_SETUP_MATERIAL_PROPERTY_CACHES() // No-op by default

// -- Visibility --

#ifndef UNITY_INDIRECT_INCLUDED
uint unity_BaseCommandID;
ByteAddressBuffer unity_IndirectDrawArgs;
#endif
ByteAddressBuffer flora_IndirectInstanceVisibility;

static uint flora_Sampled_IndirectVisibleIndex;
static uint flora_Sampled_Crossfade_InstanceIndex;

void SetupFloraVisibleInstancingData()
{
#if defined(SHADER_STAGE_VERTEX)
    uint drawArgsStartInstanceIndexOffset = unity_BaseCommandID * 20 + 16;
    uint indirectVisibilityOffset = unity_IndirectDrawArgs.Load(drawArgsStartInstanceIndexOffset);

    flora_Sampled_IndirectVisibleIndex = indirectVisibilityOffset + unity_InstanceID;
    flora_Sampled_Crossfade_InstanceIndex = flora_IndirectInstanceVisibility.Load(flora_Sampled_IndirectVisibleIndex << 2);
    SampleFloraInstanceWithPackedLODFade(flora_Sampled_Crossfade_InstanceIndex);

    #undef UNITY_TRANSFER_INSTANCE_ID
    #define UNITY_TRANSFER_INSTANCE_ID(input, output) output.instanceID = flora_Sampled_Crossfade_InstanceIndex
#else
    SampleFloraInstanceWithPackedLODFade(unity_InstanceID);
#endif

    #undef unity_LODFade
    unity_LODFade = flora_SampledInstance.LODFade;
}

// -- Lighting --

static half4 flora_Sampled_SHAr;
static half4 flora_Sampled_SHAg;
static half4 flora_Sampled_SHAb;
static half4 flora_Sampled_SHBr;
static half4 flora_Sampled_SHBg;
static half4 flora_Sampled_SHBb;
static half4 flora_Sampled_SHC;
static half4 flora_Sampled_ProbesOcclusion;

void SetupFloraInstanceSHCoeffs()
{
#ifndef FLORA_CONFIG_DISABLE_LEGACY_LIGHT_PROBES
    if (flora_InstanceLightProbeMetadata)
    {
        uint address = ComputeFloraInstanceDataAddressOverridden(flora_InstanceLightProbeMetadata, 8 * 16);
        flora_Sampled_SHAr = half4(asfloat(FloraInstanceData_Load4(address + 0 * 16)));
        flora_Sampled_SHAg = half4(asfloat(FloraInstanceData_Load4(address + 1 * 16)));
        flora_Sampled_SHAb = half4(asfloat(FloraInstanceData_Load4(address + 2 * 16)));
        flora_Sampled_SHBr = half4(asfloat(FloraInstanceData_Load4(address + 3 * 16)));
        flora_Sampled_SHBg = half4(asfloat(FloraInstanceData_Load4(address + 4 * 16)));
        flora_Sampled_SHBb = half4(asfloat(FloraInstanceData_Load4(address + 5 * 16)));
        flora_Sampled_SHC  = half4(asfloat(FloraInstanceData_Load4(address + 6 * 16)));
        flora_Sampled_ProbesOcclusion = half4(asfloat(FloraInstanceData_Load4(address + 7 * 16)));
    }
    else
#endif
    {
        flora_Sampled_SHAr = half4(flora_SHAr);
        flora_Sampled_SHAg = half4(flora_SHAg);
        flora_Sampled_SHAb = half4(flora_SHAb);
        flora_Sampled_SHBr = half4(flora_SHBr);
        flora_Sampled_SHBg = half4(flora_SHBg);
        flora_Sampled_SHBb = half4(flora_SHBb);
        flora_Sampled_SHC  = half4(flora_SHC);
        flora_Sampled_ProbesOcclusion = half4(flora_ProbesOcclusion);
    }
}

void SetupFloraInstanceLighting()
{
    SetupFloraInstanceSHCoeffs();

    #undef unity_SHAr
    #undef unity_SHAg
    #undef unity_SHAb
    #undef unity_SHBr
    #undef unity_SHBg
    #undef unity_SHBb
    #undef unity_SHC
    #undef unity_ProbesOcclusion

    unity_SHAr = flora_Sampled_SHAr;
    unity_SHAg = flora_Sampled_SHAg;
    unity_SHAb = flora_Sampled_SHAb;
    unity_SHBr = flora_Sampled_SHBr;
    unity_SHBg = flora_Sampled_SHBg;
    unity_SHBb = flora_Sampled_SHBb;
    unity_SHC  = flora_Sampled_SHC;
    unity_ProbesOcclusion = flora_Sampled_ProbesOcclusion;

    #if defined(FLORA_USE_STANDARD_REFLECTION_PROBES)
        #undef unity_SpecCube0_HDR
        #undef unity_SpecCube1_HDR

        unity_SpecCube0_HDR = flora_SpecCube0_HDR;
        unity_SpecCube1_HDR = flora_SpecCube1_HDR;
    #endif
}

// -- Matrices --

void SetupFloraInstanceMatrices()
{
    #undef unity_ObjectToWorld
    #undef unity_WorldToObject
    #undef unity_MatrixPreviousM
    #undef unity_MatrixPreviousMI

    unity_ObjectToWorld = flora_SampledInstance.ObjectToWorld;
    unity_WorldToObject = flora_SampledInstance.WorldToObject;

    #define unity_MatrixPreviousM  flora_SampledInstance.ObjectToWorld;
    #define unity_MatrixPreviousMI flora_SampledInstance.WorldToObject;
}

// -- Initialization --

void SetupFloraInstancingData()
{
    SetupFloraVisibleInstancingData();
    SetupFloraInstanceMatrices();
    SetupFloraInstanceLighting();
}

#ifdef FLORA_PROCEDURAL_INSTANCING_ENABLED
    #if defined(UNITY_SETUP_INSTANCE_ID)
        #undef UNITY_SETUP_INSTANCE_ID
        #define UNITY_SETUP_INSTANCE_ID(input) {\
            DEFAULT_UNITY_SETUP_INSTANCE_ID(input);\
            FLORA_SETUP_MATERIAL_PROPERTY_CACHES(); }
    #endif
#endif

// -- Selection --

#if UNITY_VERSION >= 202220
#undef unity_SelectionID
#define unity_SelectionID LoadFloraInstancedData_SelectionValue()
#else
#define _SelectionID LoadFloraInstancedData_SelectionValue()
#define unity_SelectionID LoadFloraInstancedData_SelectionValue()
#endif

#else // FLORA_PROCEDURAL_INSTANCING_ENABLED

#ifndef unity_SelectionID
#define unity_SelectionID _SelectionID
#endif

void SetupFloraInstancingData()
{
    // Disabled
}

#endif // FLORA_PROCEDURAL_INSTANCING_ENABLED

// Legacy setup
void FloraInstancingSetup()
{
    SetupFloraInstancingData();
}

#endif // FLORA_INSTANCING_INCLUDED
