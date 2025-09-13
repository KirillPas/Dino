// Copyright © Magnetic Arcade. All Rights Reserved.

Shader "Flora Demo/Terrain/TerrainSampler"
{
    Properties
    {
        // Layer count is passed down to guide height-blend enable/disable, due
        // to the fact that heigh-based blend will be broken with multipass.
        [HideInInspector] [PerRendererData] _NumLayersCount ("Total Layer Count", Float) = 1.0
        [HideInInspector] _Control("AlphaMap", 2D) = "" {}

        [HideInInspector] _Diffuse0 ("Layer 0 (R)", 2D) = "white" {}
        [HideInInspector] _Diffuse1 ("Layer 1 (G)", 2D) = "white" {}
        [HideInInspector] _Diffuse2 ("Layer 2 (B)", 2D) = "white" {}
        [HideInInspector] _Diffuse3 ("Layer 3 (A)", 2D) = "white" {}

        [HideInInspector] _DstBlend("DstBlend", Float) = 0.0
    }
    SubShader
    {
        CGINCLUDE
        #pragma target 3.0
        #include "UnityCG.cginc"

        ENDCG

        Pass
        {
            Tags
            {
                "Name" = "_MainTex"
                "Format" = "ARGB32"
                "Size" = "1"
            }

            ZTest Always Cull Off ZWrite Off
            Blend One [_DstBlend]

            CGPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 uvMainAndLM              : TEXCOORD0; // xy: control, zw: lightmap
                float4 uvSplat01                : TEXCOORD1; // xy: splat0, zw: splat1
                float4 uvSplat23                : TEXCOORD2; // xy: splat2, zw: splat3
                float3 positionWS               : TEXCOORD7;
                float4 clipPos                  : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                half _Cutoff;
            CBUFFER_END

            sampler2D _Control;
            float4 _Control_ST;
            float4 _Control_TexelSize;

            sampler2D _Diffuse0;
            half4 _Diffuse0_ST;
            sampler2D _Diffuse1;
            half4 _Diffuse1_ST;
            sampler2D _Diffuse2;
            half4 _Diffuse2_ST;
            sampler2D _Diffuse3;
            half4 _Diffuse3_ST;

            Varyings Vert(Attributes IN)
            {
                Varyings output = (Varyings) 0;

                output.clipPos = UnityObjectToClipPos(IN.positionOS.xyz);

                // NOTE : This is basically coming from the vertex shader in TerrainLitPasses
                // There are other plenty of other values that the original version computes, but for this
                // pass, we are only interested in a few, so I'm just skipping the rest.
                output.uvMainAndLM.xy = IN.texcoord;
                output.uvSplat01.xy = TRANSFORM_TEX(IN.texcoord, _Diffuse0);
                output.uvSplat01.zw = TRANSFORM_TEX(IN.texcoord, _Diffuse1);
                output.uvSplat23.xy = TRANSFORM_TEX(IN.texcoord, _Diffuse2);
                output.uvSplat23.zw = TRANSFORM_TEX(IN.texcoord, _Diffuse3);

                return output;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 splatUV = (IN.uvMainAndLM.xy * (_Control_TexelSize.zw - 1.0f) + 0.5f) * _Control_TexelSize.xy;
                half4 splatControl = tex2D(_Control, splatUV);

                half4 diffAlbedo[4];
                diffAlbedo[0] = tex2D(_Diffuse0, IN.uvSplat01.xy);
                diffAlbedo[1] = tex2D(_Diffuse1, IN.uvSplat01.zw);
                diffAlbedo[2] = tex2D(_Diffuse2, IN.uvSplat23.xy);
                diffAlbedo[3] = tex2D(_Diffuse3, IN.uvSplat23.zw);

                // Now that splatControl has changed, we can compute the final weight and normalize
                half weight = dot(splatControl, 1.0h);

                // Normalize weights before lighting and restore weights in final modifier functions so that the overal
                // lighting result can be correctly weighted.
                splatControl /= (weight + 0.001h);

                half4 mixedDiffuse = 0.0h;
                mixedDiffuse += diffAlbedo[0] * half4(splatControl.rrr, 1.0h);
                mixedDiffuse += diffAlbedo[1] * half4(splatControl.ggg, 1.0h);
                mixedDiffuse += diffAlbedo[2] * half4(splatControl.bbb, 1.0h);
                mixedDiffuse += diffAlbedo[3] * half4(splatControl.aaa, 1.0h);

                return half4(mixedDiffuse.rgb, 1);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
