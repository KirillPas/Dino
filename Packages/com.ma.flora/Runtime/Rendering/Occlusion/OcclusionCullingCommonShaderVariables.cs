// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering.Occlusion
{
    // TODO make consistent with InstanceOcclusionCullerShaderVariables
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    unsafe struct OcclusionCullingCommonShaderVariables
    {
        [HLSLArray(OccluderContext.MaxOccluderMips, typeof(ShaderGenUInt4))]
        public fixed uint _OccluderMipBounds[OccluderContext.MaxOccluderMips * 4];

        [HLSLArray(OccluderContext.MaxSubviewsPerView, typeof(Matrix4x4))]
        public fixed float _ViewProjMatrix[OccluderContext.MaxSubviewsPerView * 16]; // from view-centered world space

        [HLSLArray(OccluderContext.MaxSubviewsPerView, typeof(Vector4))]
        public fixed float _ViewOriginWorldSpace[OccluderContext.MaxSubviewsPerView * 4];

        [HLSLArray(OccluderContext.MaxSubviewsPerView, typeof(Vector4))]
        public fixed float _FacingDirWorldSpace[OccluderContext.MaxSubviewsPerView * 4];

        [HLSLArray(OccluderContext.MaxSubviewsPerView, typeof(Vector4))]
        public fixed float _RadialDirWorldSpace[OccluderContext.MaxSubviewsPerView * 4];

        public Vector4 _DepthSizeInOccluderPixels;
        public Vector4 _OccluderDepthPyramidSize;

        public uint _OccluderMipLayoutSizeX;
        public uint _OccluderMipLayoutSizeY;
        public uint _OcclusionTestDebugFlags;
        public uint _OcclusionCullingCommonPad0;

        public int _OcclusionTestCount;
        public int _OccluderSubviewIndices; // packed 4 bits each
        public int _CullingSplitIndices; // packed 4 bits each
        public int _CullingSplitMask; // only used for early out

        internal OcclusionCullingCommonShaderVariables(
            in OccluderContext occluderCtx,
            in InstanceOcclusionTestSubviewSettings subviewSettings,
            bool occlusionOverlayCountVisible,
            bool overrideOcclusionTestToAlwaysPass)
        {
            for (int i = 0; i < occluderCtx.SubviewCount; ++i)
            { 
                if (occluderCtx.IsSubviewValid(i))
                {
                    unsafe
                    {
                        for (int j = 0; j < 16; ++j)
                            _ViewProjMatrix[16 * i + j] = occluderCtx.SubviewData[i].ViewProjMatrix[j];

                        for (int j = 0; j < 4; ++j)
                        {
                            _ViewOriginWorldSpace[4 * i + j] = occluderCtx.SubviewData[i].ViewOriginWorldSpace[j];
                            _FacingDirWorldSpace[4 * i + j] = occluderCtx.SubviewData[i].FacingDirWorldSpace[j];
                            _RadialDirWorldSpace[4 * i + j] = occluderCtx.SubviewData[i].RadialDirWorldSpace[j];
                        }
                    }
                }
            }
            _OccluderMipLayoutSizeX = (uint)occluderCtx.OccluderMipLayoutSize.x;
            _OccluderMipLayoutSizeY = (uint)occluderCtx.OccluderMipLayoutSize.y;
            _OcclusionTestDebugFlags
                = (overrideOcclusionTestToAlwaysPass ? (uint)OcclusionTestDebugFlag.AlwaysPass : 0)
                | (occlusionOverlayCountVisible ? (uint)OcclusionTestDebugFlag.CountVisible : 0);
            _OcclusionCullingCommonPad0 = 0;

            _OcclusionTestCount = subviewSettings.TestCount;
            _OccluderSubviewIndices = subviewSettings.OccluderSubviewIndices;
            _CullingSplitIndices = subviewSettings.CullingSplitIndices;
            _CullingSplitMask = subviewSettings.CullingSplitMask;

            _DepthSizeInOccluderPixels = occluderCtx.DepthBufferSizeInOccluderPixels;

            Vector2Int textureSize = occluderCtx.OccluderDepthPyramidSize;
            _OccluderDepthPyramidSize = new Vector4(textureSize.x, textureSize.y, 1.0f / textureSize.x, 1.0f / textureSize.y);

            for (int i = 0; i < occluderCtx.OccluderMipBounds.Length; ++i)
            {
                var mipBounds = occluderCtx.OccluderMipBounds[i];
                unsafe
                {
                    _OccluderMipBounds[4*i + 0] = (uint)mipBounds.Offset.x;
                    _OccluderMipBounds[4*i + 1] = (uint)mipBounds.Offset.y;
                    _OccluderMipBounds[4*i + 2] = (uint)mipBounds.Size.x;
                    _OccluderMipBounds[4*i + 3] = (uint)mipBounds.Size.y;
                }
            }
        }
    }
}