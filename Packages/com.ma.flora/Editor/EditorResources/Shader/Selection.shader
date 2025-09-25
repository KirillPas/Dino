// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Hidden/Flora/Selection"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.01
    }

    SubShader
    {
        CGINCLUDE

        #pragma target 4.5
        #pragma editor_sync_compilation
        // #pragma enable_d3d11_debug_symbols

        #pragma multi_compile_instancing
        #pragma instancing_options procedural:FloraInstancingSetup

        #include "UnityCG.cginc"
        #include "UnityInstancing.cginc"
        #include "Packages/com.ma.flora/ShaderLibrary/Flora.hlsl"

        struct Input
        {
            float4 PositionOS : POSITION;
            float2 UV0        : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varying
        {
            float4 PositionCS : SV_POSITION;
            float2 UV0        : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varying VertFullscreen(Input input)
        {
            Varying output;
            output.PositionCS = UnityObjectToClipPos(input.PositionOS);
            output.UV0 = UnityStereoTransformScreenSpaceTex(input.UV0);
            return output;
        }

        Varying VertInstanced(Input input)
        {
            Varying output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            output.PositionCS = UnityObjectToClipPos(input.PositionOS);
            output.UV0 = input.UV0;
            return output;
        }
        ENDCG

        Tags
        {
            "RenderType"="Opaque"
        }

        //-----------------------------------------------------------------------------
        // 0: Picking Pass
        //-----------------------------------------------------------------------------

        Pass
        {
            ZTest LEqual
            Cull Off
            ZWrite On

            Name "ScenePickingPass"
            Tags { "LightMode" = "Picking" }

            CGPROGRAM
                #pragma vertex VertInstanced
                #pragma fragment FragPicking

                uint _SelectionID;

                float4 FragPicking(Varying input) : SV_Target
                {
                    UNITY_SETUP_INSTANCE_ID(input);
                    return unity_SelectionID;
                }
            ENDCG
        }

        //-----------------------------------------------------------------------------
        // 1: Selection All Pass
        //-----------------------------------------------------------------------------
        // All the selected, including the ones that fail the depth test. Additive blend, 0 in green (visibility), 1 in blue (selected), 1 in alpha.

        Pass
        {
            Blend One One
            BlendOp Max
            ZTest Always
            ZWrite Off
            Cull Off
            ColorMask GBA
            // push towards camera a bit, so that coord mismatch due to dynamic batching is not affecting us
            Offset -0.02, 0

            CGPROGRAM
            #pragma vertex VertInstanced
            #pragma fragment FragFront

            float4 FragFront(Varying input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return float4(0, 0, 1, 1);
            }
            ENDCG
        }

        //-----------------------------------------------------------------------------
        // 2: Selection Visibility Pass
        //-----------------------------------------------------------------------------
        // Things that are visible (pass depth). 1 in alpha, 1 in red, 1 in green (visibility), 1 in blue (selected)

        Pass
        {
            Blend One Zero
            ZTest LEqual
            Cull Off
            ZWrite Off
            // push towards camera a bit, so that coord mismatch due to dynamic batching is not affecting us
            Offset -0.02, 0

            CGPROGRAM
            #pragma vertex VertInstanced
            #pragma fragment FragFront

            sampler2D _SelectionMask;
            float4 _ScreenSize;

            float4 FragFront(Varying input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return float4(1, 1, 1, 1);
            }
            ENDCG
        }

        //-----------------------------------------------------------------------------
        // 3: Final Post Processing Pass
        //-----------------------------------------------------------------------------

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex VertFullscreen
            #pragma fragment FragPost

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            #define SELECTION_OUTLINE_COLOR float3(251 / 255.0, 202 / 255.0, 76 / 255.0)

            half4 FragPost(Varying i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.UV0);
                float alpha = saturate(col.b * 10);

                bool isSelected = col.a > 0.9;
                if (isSelected)
                {
                    // outline color alpha controls how much tint the whole object gets
                    alpha = 0;
                    if (any(i.UV0 - _MainTex_TexelSize.xy * 2 < 0) || any(i.UV0 + _MainTex_TexelSize.xy * 2 > 1))
                        alpha = 1;
                }

                bool inFront = col.g > 0.0;
                if (!inFront)
                {
                    alpha *= 0.3;
                    if (isSelected) // no tinting at all for occluded selection
                        alpha = 0;
                }

                return float4(SELECTION_OUTLINE_COLOR, alpha);
            }
            ENDCG
        }

        //-----------------------------------------------------------------------------
        // 4: Separable Blur Pass
        //-----------------------------------------------------------------------------
        // Horizontal/Vertical

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex VertFullscreen
            #pragma fragment FragBlur
            #pragma target 4.5

            float2 _BlurDirection;
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            // 9-tap Gaussian kernel, that blurs green & blue channels,
            // keeps red & alpha intact.
            static const half4 kCurveWeights[9] = {
                half4(0, 0.0204001988, 0.0204001988, 0),
                half4(0, 0.0577929595, 0.0577929595, 0),
                half4(0, 0.1215916882, 0.1215916882, 0),
                half4(0, 0.1899858519, 0.1899858519, 0),
                half4(1, 0.2204586031, 0.2204586031, 1),
                half4(0, 0.1899858519, 0.1899858519, 0),
                half4(0, 0.1215916882, 0.1215916882, 0),
                half4(0, 0.0577929595, 0.0577929595, 0),
                half4(0, 0.0204001988, 0.0204001988, 0)
            };

            half4 FragBlur(Varying i) : SV_Target
            {
                float2 step = _MainTex_TexelSize.xy * _BlurDirection;
                float2 uv = i.UV0 - step * 4;
                half4 col = 0;
                for (int tap = 0; tap < 9; ++tap)
                {
                    col += tex2D(_MainTex, uv) * kCurveWeights[tap];
                    uv += step;
                }
                return col;
            }
            ENDCG
        }

        //-----------------------------------------------------------------------------
        // 5: ID Pass
        //-----------------------------------------------------------------------------

        Pass
        {
            ZTest Always
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex VertFullscreen
            #pragma fragment FragCompare
            #pragma target 4.5

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            // 8 tap search around the current pixel to
            // see if it borders with an object that has a
            // different object id
            static const half2 kOffsets[8] = {
                half2(-1, -1),
                half2(0, -1),
                half2(1, -1),
                half2(-1, 0),
                half2(1, 0),
                half2(-1, 1),
                half2(0, 1),
                half2(1, 1)
            };

            half4 FragCompare(Varying i) : SV_Target
            {
                float4 currentTexel = tex2D(_MainTex, i.UV0);
                if (currentTexel.r == 0)
                    return currentTexel;

                // if the current texel borders with a
                // texel that has a differnt object id
                // set the alpha to 0. This implies an
                // edge.
                for (int tap = 0; tap < 8; ++tap)
                {
                    float id = tex2D(_MainTex, i.UV0 + (kOffsets[tap] * _MainTex_TexelSize.xy)).r;
                    if (id != 0 && id - currentTexel.r != 0)
                    {
                        currentTexel.a = 0;
                    }
                }
                return currentTexel;
            }
            ENDCG
        }
    }
}
