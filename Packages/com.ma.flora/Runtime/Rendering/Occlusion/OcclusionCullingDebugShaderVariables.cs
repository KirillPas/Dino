// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering.Occlusion
{
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    unsafe struct OcclusionCullingDebugShaderVariables
    {
        public Vector4 _DepthSizeInOccluderPixels;

        [HLSLArray(OccluderContext.MaxOccluderMips, typeof(ShaderGenUInt4))]
        public fixed uint _OccluderMipBounds[OccluderContext.MaxOccluderMips * 4];

        public uint _OccluderMipLayoutSizeX;
        public uint _OccluderMipLayoutSizeY;
        public uint _OcclusionCullingDebugPad0;
        public uint _OcclusionCullingDebugPad1;
    }
}