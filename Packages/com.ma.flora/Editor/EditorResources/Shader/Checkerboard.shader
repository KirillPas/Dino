Shader "Flora/Editor/Checkerboard"
{
    Properties
    {
        _Frequency ("Frequency", Float) = 1
        _ColorA ("Color A", Color) = (0.1, 0.1, 0.1, 1.0)
        _ColorB ("Color B", Color) = (0.2, 0.2, 0.2, 1.0)
    }
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            
            #include "UnityCG.cginc"
                
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                float4 positionHCS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            float _Frequency;
            float4 _ColorA;
            float4 _ColorB;
            
            // Generates a triangle in homogeneous clip space, s.t.
            
            float4 GetFullScreenTriangleVertexPosition(uint vertexID, float z = UNITY_NEAR_CLIP_VALUE)
            {
                // note: the triangle vertex position coordinates are x2 so the returned UV coordinates are in range -1, 1 on the screen.
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                return float4(uv * 2.0 - 1.0, z, 1.0);
            }
                    
            float3 Checkerboard(float2 uv, float3 colorA, float3 colorB, float2 frequency)
            {
                uv = (uv.xy + 0.5) * frequency;
                float2 distance3 = 4.0 * abs(frac(uv + 0.25) - 0.5) - 1.0;
                float4 derivatives = float4(ddx(uv), ddy(uv));
                float2 duvLength = sqrt(float2(dot(derivatives.xz, derivatives.xz), dot(derivatives.yw, derivatives.yw)));
                float2 scale = 0.35 / duvLength.xy;
                float freqLimiter = sqrt(clamp(1.1f - max(duvLength.x, duvLength.y), 0.0, 1.0));
                float2 vectorAlpha = clamp(distance3 * scale.xy, -1.0, 1.0);
                float alpha = saturate(0.5f + 0.5f * vectorAlpha.x * vectorAlpha.y * freqLimiter);
                return lerp(colorA, colorB, alpha.xxx);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, 0);
                return output;
            }
            
            float4 Frag(Varyings i) : SV_Target
            {
                return float4(Checkerboard(i.positionCS.xy, _ColorA, _ColorB, _Frequency * 0.01), 1);
            }
            ENDCG
        }
    }
}
