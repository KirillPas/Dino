// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering.Occlusion
{
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    unsafe struct OccluderDepthPyramidConstants
    {
        [HLSLArray(OccluderContext.MaxSubviewsPerView, typeof(Matrix4x4))]
        public fixed float _InvViewProjMatrix[OccluderContext.MaxSubviewsPerView * 16];

        [HLSLArray(OccluderContext.MaxSilhouettePlanes, typeof(Vector4))]
        public fixed float _SilhouettePlanes[OccluderContext.MaxSilhouettePlanes * 4];

        [HLSLArray(OccluderContext.MaxSubviewsPerView, typeof(ShaderGenUInt4))]
        public fixed uint _SrcOffset[OccluderContext.MaxSubviewsPerView * 4];

        [HLSLArray(5, typeof(ShaderGenUInt4))]
        public fixed uint _MipOffsetAndSize[5 * 4];

        public uint _OccluderMipLayoutSizeX;
        public uint _OccluderMipLayoutSizeY;
        public uint _OccluderDepthPyramidPad0;
        public uint _OccluderDepthPyramidPad1;

        public uint _SrcSliceIndices; // packed 4 bits each
        public uint _DstSubviewIndices; // packed 4 bits each
        public uint _MipCount;
        public uint _SilhouettePlaneCount;
    }
}