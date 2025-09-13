// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    struct InstancingGlobalShaderVariables
    {
        public float4 ProbesOcclusion;
        public float4 SpecCube0_HDR;
        public float4 SpecCube1_HDR;
        public float4 SHAr;
        public float4 SHAg;
        public float4 SHAb;
        public float4 SHBr;
        public float4 SHBg;
        public float4 SHBb;
        public float4 SHC;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateAmbientSH(in SphericalHarmonicsL2 ambientProbe)
        {
            ProbesOcclusion = Vector4.one;
            SpecCube0_HDR = ReflectionProbe.defaultTextureHDRDecodeValues;
            SpecCube1_HDR = SpecCube0_HDR;

            SHCoefficients coefficients = new SHCoefficients(ambientProbe);
            SHAr = coefficients.SHAr;
            SHAg = coefficients.SHAg;
            SHAb = coefficients.SHAb;
            SHBr = coefficients.SHBr;
            SHBg = coefficients.SHBg;
            SHBb = coefficients.SHBb;
            SHC = coefficients.SHC;
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    static class InstancingShaderID
    {
        public static readonly int flora_InstanceGlobalValues = Shader.PropertyToID("flora_InstanceGlobalValues");
        public static readonly int flora_RendererData = Shader.PropertyToID("flora_RendererData");
        public static readonly int flora_InstanceData = Shader.PropertyToID("flora_InstanceData");
        public static readonly int flora_IndirectInstanceVisibility = Shader.PropertyToID("flora_IndirectInstanceVisibility");
    }
}
