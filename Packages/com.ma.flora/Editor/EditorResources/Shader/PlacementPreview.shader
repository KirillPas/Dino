
Shader "Hidden/Flora/PlacementPreview"
{
    SubShader
    {
        ZTest Always Cull Back ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGINCLUDE
        #pragma editor_sync_compilation
        // #pragma enable_d3d11_debug_symbols
        #pragma target 4.5

        #include "UnityCG.cginc"
        #include "UnityInstancing.cginc"
        #include "TerrainPreview.cginc"

        #include "Packages/com.ma.flora/ShaderLibrary/Flora.hlsl"

        #define HIGHLIGHT_OPACITY  0.2
        #define HIGHLIGHT_COLOR    float4(255 / 255.0, 215 / 255.0, 0  / 255.0, HIGHLIGHT_OPACITY)
        #define INVALID_COLOR      float4(255 / 255.0, 71  / 255.0, 71 / 255.0, HIGHLIGHT_OPACITY)

        float4 _MaskParams0;
        #define _SlopeMinMax       float2(_MaskParams0.xy)
        #define _HeightMinMax      float2(_MaskParams0.zw)

        struct Attributes
        {
            float4 PositionOS : POSITION;
            float3 NormalOS   : NORMAL;
            float2 Texcoord0  : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct VaryingFullscreen
        {
            float4 PositionCS : SV_POSITION;
            float2 UV0        : TEXCOORD0;
        };

        VaryingFullscreen VertFullscreen(Attributes input)
        {
            VaryingFullscreen output;
            output.PositionCS = UnityObjectToClipPos(input.PositionOS);
            output.UV0 = UnityStereoTransformScreenSpaceTex(input.Texcoord0);
            return output;
        }

        bool IsHeightValid(float height)
        {
            return height >= _HeightMinMax.x && height <= _HeightMinMax.y;
        }

        bool IsSlopeValid(float normalY, float tolerance = 0.0001f)
        {
            float minNormalAngle = cos(radians(_SlopeMinMax.x));
            float maxNormalAngle = cos(radians(_SlopeMinMax.y));
            return !(maxNormalAngle > (normalY + tolerance) || minNormalAngle < (normalY - tolerance));
        }
        ENDCG

        Pass // 0
        {
            Name "Place Preview"

            CGPROGRAM
            #pragma vertex VertFullscreen
            #pragma fragment FragFront

            sampler2D _MaskTexture;
            float4 _ScreenSize;

            float4 FragFront(VaryingFullscreen input) : SV_Target
            {
                float2 uv = input.PositionCS.xy * _ScreenSize.zw;
                float4 col = tex2D(_MaskTexture, uv);
                if (col.a > 0)
                    return float4(HIGHLIGHT_COLOR.rgb, HIGHLIGHT_OPACITY * col.a * 0.75);

                return 0;
            }
            ENDCG
        }

        Pass // 1
        {
            Name "Fill Mesh Preview"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varying
            {
                float4 PositionCS  : SV_POSITION;
                float2 uv0         : TEXCOORD0;
                float3 PositionWS  : TEXCOORD1;
                float3 normalWorld : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varying Vert(Attributes v)
            {
                Varying o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.PositionCS = UnityObjectToClipPos(v.PositionOS);
                o.uv0 = v.Texcoord0;
                o.PositionWS = mul(unity_ObjectToWorld, v.PositionOS).xyz;
                o.normalWorld = UnityObjectToWorldNormal(v.NormalOS);
                return o;
            }

            float4 Frag(Varying input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 normal = input.normalWorld;
                if (IsSlopeValid(normal.y) && IsHeightValid(input.PositionWS.y))
                {
                    return HIGHLIGHT_COLOR;
                }
                else
                {
                    return INVALID_COLOR;
                }
            }
            ENDCG
        }

        Pass // 2
        {
            Name "Fill Terrain Preview"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varying
            {
                float4 PositionCS : SV_POSITION;
                float3 PositionWS : TEXCOORD0;
                float2 PCPixels   : TEXCOORD2;
                float2 BrushUV    : TEXCOORD3;
            };

            sampler2D _Normalmap;

            Varying Vert(uint vid : SV_VertexID)
            {
                // build a quad mesh, with one vertex per paint context pixel (pcPixel)
                float2 pcPixels = BuildProceduralQuadMeshVertex(vid);

                // compute heightmap UV and sample heightmap
                float2 heightmapUV    = PaintContextPixelsToHeightmapUV(pcPixels);
                float heightmapSample = UnpackHeightmap(tex2Dlod(_Heightmap, float4(heightmapUV, 0, 0)));

                // compute brush UV
                float2 BrushUV = PaintContextPixelsToBrushUV(pcPixels);

                // compute object position (in terrain space) and world position
                float3 positionObject = PaintContextPixelsToObjectPosition(pcPixels, heightmapSample);
                float3 positionWorld  = TerrainObjectToWorldPosition(positionObject);
                positionWorld.y += 0.5;

                Varying o;
                o.PCPixels = pcPixels;
                o.PositionWS = positionWorld;
                o.PositionCS = UnityWorldToClipPos(positionWorld);
                o.BrushUV = BrushUV;
                return o;
            }

            float4 Frag(Varying input) : SV_Target
            {
                float2 heightmapUV = PaintContextPixelsToHeightmapUV(input.PCPixels);

				float3 normal = tex2D(_Normalmap, heightmapUV).rgb * 2.0 - 1.0;
                if (!IsSlopeValid(normal.y) || !IsHeightValid(input.PositionWS.y))
                    return INVALID_COLOR;

                return HIGHLIGHT_COLOR;
            }
            ENDCG
        }
    }
}
