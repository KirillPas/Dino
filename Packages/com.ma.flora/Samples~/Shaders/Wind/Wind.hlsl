// Copyright © Magnetic Arcade. All Rights Reserved.

// x: windDirection.x, y: windDirection.z, z: windOffset.x, w: windOffset.z
float4 _FloraDemo_GlobalWindParams0;
#define _FloraDemo_GlobalWindDirection _FloraDemo_GlobalWindParams0.xy
#define _FloraDemo_GlobalWindOffset _FloraDemo_GlobalWindParams0.zw

// x: windSpeed, y: windStrength, z: windTurbulence, w: unused
float4 _FloraDemo_GlobalWindParams1;
#define _FloraDemo_GlobalWindSpeed _FloraDemo_GlobalWindParams1.x
#define _FloraDemo_GlobalWindStrength _FloraDemo_GlobalWindParams1.y
#define _FloraDemo_GlobalWindTurbulence _FloraDemo_GlobalWindParams1.z

Texture2D _FloraDemo_WindNoiseTexture;
SamplerState _FloraDemo_WindNoiseTextureSampler;

float4 CubicSmooth(float4 x)
{
    return x * x * (3.0 - 2.0 * x);
}

float4 TriangleWave(float4 x)
{
    return abs((frac(x + 0.5) * 2.0) - 1.0);
}

float4 TrigApproximate(float4 x)
{
    return (CubicSmooth(TriangleWave(x)) - 0.5) * 2.0;
}

void floraDemo_GetWindDirection_float(out float3 outWindDirection)
{
    outWindDirection = float3(_FloraDemo_GlobalWindParams0.x, 0, _FloraDemo_GlobalWindParams0.y);
}

void floraDemo_GetLocalWindSpeed_float(out float outWindSpeed)
{
    outWindSpeed = _FloraDemo_GlobalWindParams1.x;
}

void floraDemo_GlobalWind_float(
    float3 position, float time, float height, float heightExp, float distance, float strength,
    out float3 outPosition)
{
    time *= _FloraDemo_GlobalWindSpeed;
    strength *= _FloraDemo_GlobalWindStrength;

    float3 windDirection = float3(_FloraDemo_GlobalWindParams0.x, 0, _FloraDemo_GlobalWindParams0.y);
    float3 pivot = GetAbsolutePositionWS(float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w));

    // Need to store the original length for later
    float originalLength = length(position.xyz);

    // Compute how much the height contributes
    float adjust = max(position.y - (1.0 / height) * 0.25, 0.0) * height;
    if (adjust != 0.0)
        adjust = pow(abs(adjust), heightExp);

    // Primary oscillation
    float4 oscillations = TrigApproximate(float4(pivot.x + time, pivot.y + time * 0.8, 0.0, 0.0));
    float osc = oscillations.x + (oscillations.y * oscillations.y);
    float amount = distance * osc;

    // Move a minimum amount based on direction adherence
    amount += strength / height;

    // Adjust based on how high up the tree this vertex is
    amount *= adjust;

    // XZ component
    position.xz += windDirection.xz * amount;

    // Fix stretching
    position.xyz = normalize(position.xyz) * originalLength;

    // Output
    outPosition = position;
}
