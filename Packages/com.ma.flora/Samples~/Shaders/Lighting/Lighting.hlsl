#ifndef MA_URP_SUBSURFACE_SCATTERING_INCLUDED
#define MA_URP_SUBSURFACE_SCATTERING_INCLUDED

static half4 s_flora_SubsurfaceColor = 0.5;

void floraDemo_SetSubsurfaceScatteringParams_half(half3 BaseColor, half4 SubsurfaceColor, out half3 OutBaseColor)
{
    s_flora_SubsurfaceColor = SubsurfaceColor;
    OutBaseColor = BaseColor;
}

void floraDemo_SetSubsurfaceScatteringParams_float(half3 BaseColor, half4 SubsurfaceColor, out half3 OutBaseColor)
{
    floraDemo_SetSubsurfaceScatteringParams_half(BaseColor, SubsurfaceColor, OutBaseColor);
}

#if !defined(SHADERGRAPH_PREVIEW) && defined(UNIVERSAL_PIPELINE_CORE_INCLUDED)

#ifndef USE_CLUSTERED_LIGHTING
#define USE_CLUSTERED_LIGHTING 0
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

half3 floraDemo_LightingSubsurface(half3 diffuseColor, half3 lightColor, half3 lightDirectionWS, half distanceAttenuation, half shadowAttenuation, half3 normalWS, half3 viewDirectionWS)
{
    half NdotL = saturate(dot(normalWS, lightDirectionWS));
    half3 H = normalize(viewDirectionWS + lightDirectionWS);
    half falloff = distanceAttenuation * shadowAttenuation;

    half3 subsurfaceColor = s_flora_SubsurfaceColor.rgb;
    half opacity = s_flora_SubsurfaceColor.a;

    // To get an effect when you see through the material hard coded pow constant
    half inScatter = pow(saturate(dot(lightDirectionWS, -viewDirectionWS)), 12.0) * lerp(3.0, 0.1, opacity);

    // Wrap around lighting, /(PI*2) to be energy consistent (hack do get some view dependnt and light dependent effect)
    // Opacity of 0 gives no normal dependent lighting, Opacity of 1 gives strong normal contribution
    half normalContribution = saturate(dot(normalWS, H) * opacity + 1.0 - opacity);
    half backScatter = normalContribution / TWO_PI;

    // lerp to never exceed 1 (energy conserving)
    half3 transmittance = subsurfaceColor * lerp(backScatter, 1.0, inScatter) * PI;

    half3 brdf = diffuseColor * NdotL * falloff * lightColor +
                 transmittance * falloff * lightColor;
    return brdf;
}

half3 floraDemo_LightingSubsurface(BRDFData brdfData, Light light, half3 normalWS, half3 viewDirectionWS)
{
    return floraDemo_LightingSubsurface(brdfData.diffuse, light.color, light.direction, light.distanceAttenuation, light.shadowAttenuation, normalWS, viewDirectionWS);
}

///////////////////////////////////////////////////////////////////////////////
//                      Fragment Functions                                   //
//       Used by ShaderGraph and others builtin renderers                    //
///////////////////////////////////////////////////////////////////////////////

////////////////////////////////////////////////////////////////////////////////
/// Subsurface lighting...
////////////////////////////////////////////////////////////////////////////////
half4 floraDemo_FragmentSubsurface(InputData inputData, SurfaceData surfaceData)
{
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);

#if defined(DEBUG_DISPLAY)
    half4 debugColor;
    if (GetFloraDebugColor(debugColor))
    {
        return debugColor;
    }

    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
#endif

    // Clear-coat calculation...
    BRDFData brdfDataClearCoat = (BRDFData)0;
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
#if UNITY_VERSION >= 202230
    uint meshRenderingLayers = GetMeshRenderingLayer();
#else
    uint meshRenderingLayers = GetMeshRenderingLightLayer();
#endif
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);

    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);

    lightingData.giColor = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
                                              inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS
#if UNITY_VERSION >= 202230
                                              , inputData.normalizedScreenSpaceUV
#endif
                                              );

#if defined(_LIGHT_LAYERS)
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = floraDemo_LightingSubsurface(brdfData, mainLight, inputData.normalWS, inputData.viewDirectionWS);
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

#if USE_CLUSTERED_LIGHTING || USE_FORWARD_PLUS
#if USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
#else
    for (uint lightIndex = 0; lightIndex < min(_AdditionalLightsDirectionalCount, MAX_VISIBLE_LIGHTS); lightIndex++)
#endif
    {
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#if defined(_LIGHT_LAYERS)
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += floraDemo_LightingSubsurface(brdfData, light, inputData.normalWS, inputData.viewDirectionWS);
        }
    }
#endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#if defined(_LIGHT_LAYERS)
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += floraDemo_LightingSubsurface(brdfData, light, inputData.normalWS, inputData.viewDirectionWS);
        }
    LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
#endif

#if REAL_IS_HALF
    // Clamp any half.inf+ to HALF_MAX
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}

#undef UniversalFragmentPBR
#define UniversalFragmentPBR floraDemo_FragmentSubsurface

#endif // !SHADERGRAPH_PREVIEW && UNIVERSAL_CORE_INCLUDED
#endif // MA_URP_SUBSURFACE_SCATTERING_INCLUDED
