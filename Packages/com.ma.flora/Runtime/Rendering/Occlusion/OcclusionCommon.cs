// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace MA.Flora.Rendering.Occlusion
{
    [GenerateHLSL]
    enum OcclusionCullingCommonConfig
    {
        MaxOccluderMips             = 8,
        MaxOccluderSilhouettePlanes = 6,
        MaxSubviewsPerView          = 6,
        DebugPyramidOffset          = 4,
    }

    [GenerateHLSL(needAccessors = false)]
    enum OcclusionTestDebugFlag
    {
        AlwaysPass = (1 << 0),
        CountVisible = (1 << 1),
    }

    /// <summary>
    /// The type of occlusion test
    /// </summary>
    public enum OcclusionTest
    {
        /// <summary>No occlusion test, all instances are visible.</summary>
        None,
        /// <summary>Test all instances against the latest occluders.</summary>
        TestAll,
        /// <summary>Only test the culled objects from the previous pass.</summary>
        TestCulled,
    }

    // /// <summary>Extension methods for OcclusionTest.</summary>
    // public static class OcclusionTestMethods
    // {
    //     /// <summary>
    //     /// Converts this occlusion test into a batch layer mask for rendering.
    //     /// This helper function is used to limit the second rendering pass when building
    //     /// occluders to only indirect draw calls, so that only false positives from
    //     /// the first rendering pass are rendered.
    //     /// </summary>
    //     /// <param name="occlusionTest">The occlusion test.</param>
    //     /// <returns>The batch layer mask that should be used to render the results of this occlusion test.</returns>
    //     public static uint GetBatchLayerMask(this OcclusionTest occlusionTest)
    //     {
    //         // limit to indirect batches only when rendering false positives, otherwise render everything
    //         return (occlusionTest == OcclusionTest.TestCulled) ? BatchLayer.InstanceCullingIndirectMask : uint.MaxValue;
    //     }
    // }

    /// <summary>Parameter structure for passing to GPUResidentDrawer.InstanceOcclusionTest.</summary>
    public struct SubviewOcclusionTest
    {
        /// <summary>The split index to read from the CPU culling output.</summary>
        public int CullingSplitIndex;
        /// <summary>The occluder subview to occlusion test against.</summary>
        public int OccluderSubviewIndex;
    }

    /// <summary>Parameter structure for passing to GPUResidentDrawer.InstanceOcclusionTest.</summary>
    public struct OcclusionCullingSettings
    {
        /// <summary>The instance ID of the camera, to identify the culling output and occluders to use.</summary>
        public int ViewInstanceID;
        /// <summary>The occlusion test to use.</summary>
        public OcclusionTest OcclusionTest;
        /// <summary>An instance multiplier to use for the generated indirect draw calls.</summary>
        public int InstanceMultiplier;

        /// <summary>Creates a new structure using the given parameters.</summary>
        /// <param name="viewInstanceID">The instance ID of the camera to find culling output and occluders for.</param>
        /// <param name="occlusionTest">The occlusion test to use.</param>
        public OcclusionCullingSettings(int viewInstanceID, OcclusionTest occlusionTest)
        {
            ViewInstanceID = viewInstanceID;
            OcclusionTest = occlusionTest;
            InstanceMultiplier = 1;
        }
    }

    /// <summary>Parameters structure for passing to GPUResidentDrawer.UpdateInstanceOccluders.</summary>
    public struct OccluderSubviewUpdate
    {
        /// <summary>
        /// The subview index within this camera or light, used to identify these occluders for the occlusion test.
        /// </summary>
        public int SubviewIndex;

        /// <summary>The slice index of the depth data to read.</summary>
        public int DepthSliceIndex;
        /// <summary>The offset in pixels to the start of the depth data to read.</summary>
        public Vector2Int DepthOffset;

        /// <summary>The transform from world space to view space when rendering the depth buffer.</summary>
        public Matrix4x4 ViewMatrix;
        /// <summary>The transform from view space to world space when rendering the depth buffer.</summary>
        public Matrix4x4 InvViewMatrix;
        /// <summary>The GPU projection matrix when rendering the depth buffer.</summary>
        public Matrix4x4 GPUProjMatrix;
        /// <summary>An additional world space offset to apply when moving between world space and view space.</summary>
        public Vector3 ViewOffsetWorldSpace;

        /// <summary>Creates a new structure using the given parameters.</summary>
        /// <param name="subviewIndex">The index of the subview within this occluder.</param>
        public OccluderSubviewUpdate(int subviewIndex)
        {
            SubviewIndex = subviewIndex;
            DepthSliceIndex = 0;
            DepthOffset = Vector2Int.zero;
            ViewMatrix = Matrix4x4.identity;
            InvViewMatrix = Matrix4x4.identity;
            GPUProjMatrix = Matrix4x4.identity;
            ViewOffsetWorldSpace = Vector3.zero;
        }
    }
    
    public struct OccluderParameters
    {
        /// <summary>The instance ID of the camera, used to identify these occluders for the occlusion test.</summary>
        public int ViewInstanceID;
        /// <summary>The total number of subviews for this occluder.</summary>
        public int SubviewCount;

        /// <summary>The depth texture to read.</summary>
        public RTHandle DepthTextureRT;
        /// <summary>The RenderGraph handle of the depth texture to read.</summary>
        public TextureHandle DepthTextureHandle;
        /// <summary>The size in pixels of the area of the depth data to read.</summary>
        public Vector2Int DepthSize;
        /// <summary>True if the depth texture is a texture array, false otherwise.</summary>
        public bool DepthIsArray;

        /// <summary>Creates a new structure using the given parameters.</summary>
        /// <param name="viewInstanceID">The instance ID of the camera to associate with these occluders.</param>
        public OccluderParameters(int viewInstanceID)
        {
            ViewInstanceID = viewInstanceID;
            SubviewCount = 1;
            DepthTextureRT = null;
            DepthTextureHandle = TextureHandle.nullHandle;
            DepthSize = Vector2Int.zero;
            DepthIsArray = false;
        }
    }
    
    struct OccluderDerivedData
    {
        /// <summary></summary>
        public Matrix4x4 ViewProjMatrix; // from view-centered world space
        /// <summary></summary>
        public Vector4 ViewOriginWorldSpace;
        /// <summary></summary>
        public Vector4 RadialDirWorldSpace;
        /// <summary></summary>
        public Vector4 FacingDirWorldSpace;

        public static OccluderDerivedData FromParameters(in OccluderSubviewUpdate occluderSubviewUpdate)
        {
            var origin = occluderSubviewUpdate.ViewOffsetWorldSpace + (Vector3)occluderSubviewUpdate.InvViewMatrix.GetColumn(3); // view origin in world space
            var xViewVec = (Vector3)occluderSubviewUpdate.InvViewMatrix.GetColumn(0); // positive x axis in world space
            var yViewVec = (Vector3)occluderSubviewUpdate.InvViewMatrix.GetColumn(1); // positive y axis in world space
            var towardsVec = (Vector3)occluderSubviewUpdate.InvViewMatrix.GetColumn(2); // positive z axis in world space

            var viewMatrixNoTranslation = occluderSubviewUpdate.ViewMatrix;
            viewMatrixNoTranslation.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            return new OccluderDerivedData
            {
                ViewOriginWorldSpace = origin,
                FacingDirWorldSpace = towardsVec.normalized,
                RadialDirWorldSpace = (xViewVec + yViewVec).normalized,
                ViewProjMatrix = occluderSubviewUpdate.GPUProjMatrix * viewMatrixNoTranslation,
            };
        }
    }
    
    struct OccluderMipBounds
    {
        public Vector2Int Offset;
        public Vector2Int Size;
    }
    
    struct OccluderHandles
    {
        public RTHandle OccluderDepthPyramid;
        public GraphicsBuffer OcclusionDebugOverlay;
    }
    
#if UNITY_2023_3_OR_NEWER
    struct OccluderHandlesRenderGraph
    {
        public TextureHandle OccluderDepthPyramid;
        public BufferHandle OcclusionDebugOverlay;

        public bool IsValid()
        {
            return OccluderDepthPyramid.IsValid();
        }

        public void UseForOcclusionTest(IBaseRenderGraphBuilder builder)
        {
            builder.UseTexture(OccluderDepthPyramid, AccessFlags.Read);
            if (OcclusionDebugOverlay.IsValid())
                builder.UseBuffer(OcclusionDebugOverlay, AccessFlags.ReadWrite);
        }

        public void UseForOccluderUpdate(IBaseRenderGraphBuilder builder)
        {
            builder.UseTexture(OccluderDepthPyramid, AccessFlags.ReadWrite);
            if (OcclusionDebugOverlay.IsValid())
                builder.UseBuffer(OcclusionDebugOverlay, AccessFlags.ReadWrite);
        }
    }
#endif

    struct OcclusionTestComputeShader
    {
        public ComputeShader CS;
        public LocalKeyword OcclusionDebugKeyword;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Init(ComputeShader cs)
        {
            CS = cs;
            OcclusionDebugKeyword = new LocalKeyword(cs, "OCCLUSION_DEBUG");
        }
    }
    
    struct OcclusionCullingDebugOutput
    {
        public RTHandle OccluderDepthPyramid;
        public GraphicsBuffer OcclusionDepthOverlay;
        public OcclusionCullingDebugShaderVariables Constants;
    }

    [GenerateHLSL(needAccessors = false)]
    enum InstanceOcclusionTestDebugCounter
    {
        Occluded,
        NotOccluded,
        Count,
    }

    struct InstanceOcclusionTestSubviewSettings
    {
        public int TestCount;
        public int OccluderSubviewIndices;
        public int OccluderSubviewMask;
        public int CullingSplitIndices;
        public int CullingSplitMask;

        public static InstanceOcclusionTestSubviewSettings FromSpan(ReadOnlySpan<SubviewOcclusionTest> subviewOcclusionTests)
        {
            InstanceOcclusionTestSubviewSettings settings = new InstanceOcclusionTestSubviewSettings();
            for (int testIndex = 0; testIndex < subviewOcclusionTests.Length; ++testIndex)
            {
                SubviewOcclusionTest subviewTest = subviewOcclusionTests[testIndex];
                settings.OccluderSubviewIndices |= subviewTest.OccluderSubviewIndex << (4 * testIndex);
                settings.OccluderSubviewMask |= 1 << subviewTest.OccluderSubviewIndex;
                settings.CullingSplitIndices |= subviewTest.CullingSplitIndex << (4 * testIndex);
                settings.CullingSplitMask |= 1 << subviewTest.CullingSplitIndex;
            }
            settings.TestCount = subviewOcclusionTests.Length;
            return settings;
        }
    }

    struct OccluderContext : IDisposable
    {
        static class ShaderIDs
        {
            public static readonly int _SrcDepth = Shader.PropertyToID("_SrcDepth");
            public static readonly int _DstDepth = Shader.PropertyToID("_DstDepth");
            public static readonly int OccluderDepthPyramidConstants = Shader.PropertyToID("OccluderDepthPyramidConstants");
        }

        public const int FirstDepthMipIndex  = 3; // 8x8 tiles
        public const int MaxOccluderMips     = (int)OcclusionCullingCommonConfig.MaxOccluderMips;
        public const int MaxSilhouettePlanes = (int)OcclusionCullingCommonConfig.MaxOccluderSilhouettePlanes;
        public const int MaxSubviewsPerView  = (int)OcclusionCullingCommonConfig.MaxSubviewsPerView;

        public int Version;
        public Vector2Int DepthBufferSize;

        public NativeArray<OccluderDerivedData> SubviewData;
        public int SubviewCount => SubviewData.Length;
        public int SubviewValidMask;

        public bool IsSubviewValid(int subviewIndex)
        {
            return subviewIndex < SubviewCount && (SubviewValidMask & (1 << subviewIndex)) != 0;
        }
        
        public NativeArray<OccluderMipBounds> OccluderMipBounds;
        public Vector2Int OccluderMipLayoutSize; // total size of 2D layout specified by occluderMipBounds
        public Vector2Int OccluderDepthPyramidSize; // at least the size of N mip layouts tiled vertically (one per subview)
        public RTHandle OccluderDepthPyramid;
        public int OcclusionDebugOverlaySize;
        public GraphicsBuffer OcclusionDebugOverlay;
        public bool DebugNeedsClear;
        public ComputeBuffer ConstantBuffer;
        public NativeArray<OccluderDepthPyramidConstants> ConstantBufferData;

        public Vector2 DepthBufferSizeInOccluderPixels
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                const float occluderPixelSize = (1 << FirstDepthMipIndex);
                return new Vector2(
                    DepthBufferSize.x / occluderPixelSize,
                    DepthBufferSize.y / occluderPixelSize);
            }
        }

        public void Dispose()
        {
            if (SubviewData.IsCreated)
                SubviewData.Dispose();

            if (OccluderMipBounds.IsCreated)
                OccluderMipBounds.Dispose();

            if (OccluderDepthPyramid != null)
            {
                OccluderDepthPyramid.Release();
                OccluderDepthPyramid = null;
            }
            
            if (OcclusionDebugOverlay != null)
            {
                OcclusionDebugOverlay.Release();
                OcclusionDebugOverlay = null;
            }
            
            if (ConstantBuffer != null)
            {
                ConstantBuffer.Release();
                ConstantBuffer = null;
            }

            if (ConstantBufferData.IsCreated)
                ConstantBufferData.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateMipBounds()
        {
            const int occluderPixelSize = 1 << FirstDepthMipIndex;
            Vector2Int topMipSize = (DepthBufferSize + (occluderPixelSize - 1) * Vector2Int.one) / occluderPixelSize;

            Vector2Int totalSize = Vector2Int.zero;
            Vector2Int mipOffset = Vector2Int.zero;
            Vector2Int mipSize = topMipSize;

            if (!OccluderMipBounds.IsCreated)
                OccluderMipBounds = new NativeArray<OccluderMipBounds>(MaxOccluderMips, Allocator.Persistent);

            for (int mipIndex = 0; mipIndex < MaxOccluderMips; ++mipIndex)
            {
                OccluderMipBounds[mipIndex] = new OccluderMipBounds { Offset = mipOffset, Size = mipSize };

                totalSize.x = Mathf.Max(totalSize.x, mipOffset.x + mipSize.x);
                totalSize.y = Mathf.Max(totalSize.y, mipOffset.y + mipSize.y);

                if (mipIndex == 0)
                {
                    mipOffset.x = 0;
                    mipOffset.y += mipSize.y;
                }
                else
                {
                    mipOffset.x += mipSize.x;
                }
                mipSize.x = (mipSize.x + 1) / 2;
                mipSize.y = (mipSize.y + 1) / 2;
            }

            OccluderMipLayoutSize = totalSize;
        }
        
        void AllocateTexturesIfNecessary(bool debugOverlayEnabled)
        {
            Vector2Int minDepthPyramidSize = new Vector2Int(OccluderMipLayoutSize.x, OccluderMipLayoutSize.y * SubviewCount);
            if (OccluderDepthPyramidSize.x < minDepthPyramidSize.x || OccluderDepthPyramidSize.y < minDepthPyramidSize.y)
            {
                OccluderDepthPyramid?.Release();

                OccluderDepthPyramidSize = minDepthPyramidSize;
                OccluderDepthPyramid = RTHandles.Alloc(
                    OccluderDepthPyramidSize.x, OccluderDepthPyramidSize.y,
                    dimension: TextureDimension.Tex2D,
                    colorFormat: GraphicsFormat.R32_SFloat,
                    filterMode: FilterMode.Point,
                    wrapMode: TextureWrapMode.Clamp,
                    enableRandomWrite: true,
                    name: "Occluder Depths");
            }

            int newDebugOverlaySize = debugOverlayEnabled ? (minDepthPyramidSize.x * minDepthPyramidSize.y) : 0;
            if (OcclusionDebugOverlaySize < newDebugOverlaySize)
            {
                OcclusionDebugOverlay?.Release();

                OcclusionDebugOverlaySize = newDebugOverlaySize;
                DebugNeedsClear = true;

                // We use buffer instead of texture, because some platforms don't support atmoic operations for Texture2D<uint>
                OcclusionDebugOverlay = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    OcclusionDebugOverlaySize + (int)OcclusionCullingCommonConfig.DebugPyramidOffset, sizeof(uint));
            }
            
            if (newDebugOverlaySize == 0)
            {
                if (OcclusionDebugOverlay != null)
                {
                    OcclusionDebugOverlay.Release();
                    OcclusionDebugOverlay = null;
                }

                OcclusionDebugOverlaySize = newDebugOverlaySize;
            }

            ConstantBuffer ??= new ComputeBuffer(1, UnsafeUtility.SizeOf<OccluderDepthPyramidConstants>(), ComputeBufferType.Constant);

            if (!ConstantBufferData.IsCreated)
                ConstantBufferData = new NativeArray<OccluderDepthPyramidConstants>(1, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetKeyword(CommandBuffer cmd, ComputeShader cs, in LocalKeyword keyword, bool value)
        {
            if (value)
                cmd.EnableKeyword(cs, keyword);
            else
                cmd.DisableKeyword(cs, keyword);
        }

#if UNITY_2023_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetKeyword(ComputeCommandBuffer cmd, ComputeShader cs, in LocalKeyword keyword, bool value)
        {
            if (value)
                cmd.EnableKeyword(cs, keyword);
            else
                cmd.DisableKeyword(cs, keyword);
        }
#endif
        
        OccluderDepthPyramidConstants SetupFarDepthPyramidConstants(ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates, NativeArray<Plane> silhouettePlanes)
        {
            OccluderDepthPyramidConstants cb = new OccluderDepthPyramidConstants();

            // write globals
            cb._OccluderMipLayoutSizeX = (uint)OccluderMipLayoutSize.x;
            cb._OccluderMipLayoutSizeY = (uint)OccluderMipLayoutSize.y;

            // write per-subview data
            int updateCount = occluderSubviewUpdates.Length;
            for (int updateIndex = 0; updateIndex < updateCount; ++updateIndex)
            {
                ref readonly OccluderSubviewUpdate update = ref occluderSubviewUpdates[updateIndex];

                int subviewIndex = update.SubviewIndex;
                SubviewData[subviewIndex] = OccluderDerivedData.FromParameters(update);
                SubviewValidMask |= 1 << update.SubviewIndex;

                Matrix4x4 viewProjMatrix
                    = update.GPUProjMatrix
                    * update.ViewMatrix
                    * Matrix4x4.Translate(-update.ViewOffsetWorldSpace);
                Matrix4x4 invViewProjMatrix = viewProjMatrix.inverse;

                unsafe
                {
                    for (int j = 0; j < 16; ++j)
                        cb._InvViewProjMatrix[16 * updateIndex + j] = invViewProjMatrix[j];

                    cb._SrcOffset[4 * updateIndex + 0] = (uint)update.DepthOffset.x;
                    cb._SrcOffset[4 * updateIndex + 1] = (uint)update.DepthOffset.y;
                    cb._SrcOffset[4 * updateIndex + 2] = 0;
                    cb._SrcOffset[4 * updateIndex + 3] = 0;
                }

                cb._SrcSliceIndices |= (((uint)update.DepthSliceIndex & 0xf) << (4 * updateIndex));
                cb._DstSubviewIndices |= ((uint)subviewIndex << (4 * updateIndex));
            }

            // TODO: transform these planes from world space into NDC space planes
            for (int i = 0; i < MaxSilhouettePlanes; ++i)
            {
                Plane plane = new Plane(Vector3.zero, 0.0f);
                if (i < silhouettePlanes.Length)
                    plane = silhouettePlanes[i];
                unsafe
                {
                    cb._SilhouettePlanes[4 * i + 0] = plane.normal.x;
                    cb._SilhouettePlanes[4 * i + 1] = plane.normal.y;
                    cb._SilhouettePlanes[4 * i + 2] = plane.normal.z;
                    cb._SilhouettePlanes[4 * i + 3] = plane.distance;
                }
            }
            cb._SilhouettePlaneCount = (uint)silhouettePlanes.Length;

            return cb;
        }
        
        // --- Create Far Depth Pyramid / CommandBuffer API ---

        public void CreateFarDepthPyramid(
            CommandBuffer cmd, in OccluderParameters occluderParams, ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates, in OccluderHandles occluderHandles,
            NativeArray<Plane> silhouettePlanes, ComputeShader occluderDepthPyramidCS, int occluderDepthDownscaleKernel)
        {
            OccluderDepthPyramidConstants cb = SetupFarDepthPyramidConstants(occluderSubviewUpdates, silhouettePlanes);

            var cs = occluderDepthPyramidCS;
            int kernel = occluderDepthDownscaleKernel;
            
            var srcKeyword = new LocalKeyword(cs, "USE_SRC");
            var srcIsArrayKeyword = new LocalKeyword(cs, "SRC_IS_ARRAY");
            var srcIsMsaaKeyword = new LocalKeyword(cs, "SRC_IS_MSAA");

            bool srcIsArray = occluderParams.DepthIsArray;

            RTHandle depthTexture = (RTHandle)occluderParams.DepthTextureRT;
            bool srcIsMsaa = depthTexture?.isMSAAEnabled ?? false;

            const int mipCount = FirstDepthMipIndex + MaxOccluderMips;
            for (int mipIndexBase = 0; mipIndexBase < mipCount - 1; mipIndexBase += 4)
            {
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._DstDepth, occluderHandles.OccluderDepthPyramid);

                bool useSrc = (mipIndexBase == 0);
                SetKeyword(cmd, cs, srcKeyword, useSrc);
                SetKeyword(cmd, cs, srcIsArrayKeyword, useSrc && srcIsArray);
                SetKeyword(cmd, cs, srcIsMsaaKeyword, useSrc && srcIsMsaa);
                if (useSrc)
                    cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._SrcDepth, occluderParams.DepthTextureRT);

                cb._MipCount = (uint)Math.Min(mipCount - 1 - mipIndexBase, 4);

                Vector2Int srcSize = Vector2Int.zero;
                for (int i = 0; i < 5; ++i)
                {
                    Vector2Int offset = Vector2Int.zero;
                    Vector2Int size = Vector2Int.zero;
                    int mipIndex = mipIndexBase + i;
                    if (mipIndex == 0)
                    {
                        size = occluderParams.DepthSize;
                    }
                    else
                    {
                        int occMipIndex = mipIndex - FirstDepthMipIndex;
                        if (occMipIndex is >= 0 and < MaxOccluderMips)
                        {
                            offset = OccluderMipBounds[occMipIndex].Offset;
                            size = OccluderMipBounds[occMipIndex].Size;
                        }
                    }
                    if (i == 0)
                        srcSize = size;
                    unsafe
                    {
                        cb._MipOffsetAndSize[4 * i + 0] = (uint)offset.x;
                        cb._MipOffsetAndSize[4 * i + 1] = (uint)offset.y;
                        cb._MipOffsetAndSize[4 * i + 2] = (uint)size.x;
                        cb._MipOffsetAndSize[4 * i + 3] = (uint)size.y;
                    }
                }

                ConstantBufferData[0] = cb;
                cmd.SetBufferData(ConstantBuffer, ConstantBufferData);
                cmd.SetComputeConstantBufferParam(cs, ShaderIDs.OccluderDepthPyramidConstants, ConstantBuffer, 0, ConstantBuffer.stride);
                
                cmd.DispatchCompute(cs, kernel, (srcSize.x + 15) / 16, (srcSize.y + 15) / 16, occluderSubviewUpdates.Length);
            }
        }
        
        // --- Create Far Depth Pyramid / RenderGraph API ---
#if UNITY_2023_3_OR_NEWER
        public void CreateFarDepthPyramid(
            ComputeCommandBuffer cmd, in OccluderParameters occluderParams, ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates, in OccluderHandlesRenderGraph occluderHandles,
            NativeArray<Plane> silhouettePlanes, ComputeShader occluderDepthPyramidCS, int occluderDepthDownscaleKernel)
        {
            OccluderDepthPyramidConstants cb = SetupFarDepthPyramidConstants(occluderSubviewUpdates, silhouettePlanes);

            var cs = occluderDepthPyramidCS;
            int kernel = occluderDepthDownscaleKernel;
            
            var srcKeyword = new LocalKeyword(cs, "USE_SRC");
            var srcIsArrayKeyword = new LocalKeyword(cs, "SRC_IS_ARRAY");
            var srcIsMsaaKeyword = new LocalKeyword(cs, "SRC_IS_MSAA");

            bool srcIsArray = occluderParams.DepthIsArray;

            RTHandle depthTexture = (RTHandle)occluderParams.DepthTextureRT;
            bool srcIsMsaa = depthTexture?.isMSAAEnabled ?? false;

            const int mipCount = FirstDepthMipIndex + MaxOccluderMips;
            for (int mipIndexBase = 0; mipIndexBase < mipCount - 1; mipIndexBase += 4)
            {
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._DstDepth, occluderHandles.OccluderDepthPyramid);

                bool useSrc = (mipIndexBase == 0);
                SetKeyword(cmd, cs, srcKeyword, useSrc);
                SetKeyword(cmd, cs, srcIsArrayKeyword, useSrc && srcIsArray);
                SetKeyword(cmd, cs, srcIsMsaaKeyword, useSrc && srcIsMsaa);
                if (useSrc)
                    cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._SrcDepth, occluderParams.DepthTextureHandle);

                cb._MipCount = (uint)Math.Min(mipCount - 1 - mipIndexBase, 4);

                Vector2Int srcSize = Vector2Int.zero;
                for (int i = 0; i < 5; ++i)
                {
                    Vector2Int offset = Vector2Int.zero;
                    Vector2Int size = Vector2Int.zero;
                    int mipIndex = mipIndexBase + i;
                    if (mipIndex == 0)
                    {
                        size = occluderParams.DepthSize;
                    }
                    else
                    {
                        int occMipIndex = mipIndex - FirstDepthMipIndex;
                        if (occMipIndex is >= 0 and < MaxOccluderMips)
                        {
                            offset = OccluderMipBounds[occMipIndex].Offset;
                            size = OccluderMipBounds[occMipIndex].Size;
                        }
                    }
                    if (i == 0)
                        srcSize = size;
                    unsafe
                    {
                        cb._MipOffsetAndSize[4 * i + 0] = (uint)offset.x;
                        cb._MipOffsetAndSize[4 * i + 1] = (uint)offset.y;
                        cb._MipOffsetAndSize[4 * i + 2] = (uint)size.x;
                        cb._MipOffsetAndSize[4 * i + 3] = (uint)size.y;
                    }
                }

                ConstantBufferData[0] = cb;
                cmd.SetBufferData(ConstantBuffer, ConstantBufferData);
                cmd.SetComputeConstantBufferParam(cs, ShaderIDs.OccluderDepthPyramidConstants, ConstantBuffer, 0, ConstantBuffer.stride);
                
                cmd.DispatchCompute(cs, kernel, (srcSize.x + 15) / 16, (srcSize.y + 15) / 16, occluderSubviewUpdates.Length);
            }
        }
#endif
        
        // --- RenderGraph API ---
#if UNITY_2023_3_OR_NEWER
        public OccluderHandlesRenderGraph Import(RenderGraph renderGraph)
        {
            RenderTargetInfo rtInfo = new RenderTargetInfo
            {
                width = OccluderDepthPyramidSize.x,
                height = OccluderDepthPyramidSize.y,
                volumeDepth = 1,
                msaaSamples = 1,
                format = GraphicsFormat.R32_SFloat,
                bindMS = false,
            };
            
            OccluderHandlesRenderGraph occluderHandles = new OccluderHandlesRenderGraph
            {
                OccluderDepthPyramid = renderGraph.ImportTexture(OccluderDepthPyramid, rtInfo)
            };
            
            if (OcclusionDebugOverlay != null)
                occluderHandles.OcclusionDebugOverlay = renderGraph.ImportBuffer(OcclusionDebugOverlay);
            
            return occluderHandles;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrepareOccluders(in OccluderParameters occluderParams)
        {
            if (SubviewCount != occluderParams.SubviewCount)
            {
                if (SubviewData.IsCreated)
                    SubviewData.Dispose();

                SubviewData = new NativeArray<OccluderDerivedData>(occluderParams.SubviewCount, Allocator.Persistent);
                SubviewValidMask = 0;
            }
            DepthBufferSize = occluderParams.DepthSize;

            // enable debug counters for cameras when the overlay is enabled
            bool debugOverlayEnabled = DebugDisplayData.IsActive() && DebugDisplayData.Instance.OcclusionOverlayEnabled;
            UpdateMipBounds();
            AllocateTexturesIfNecessary(debugOverlayEnabled);
        }

        internal OcclusionCullingDebugOutput GetDebugOutput()
        {
            var debugOutput = new OcclusionCullingDebugOutput
            {
                OccluderDepthPyramid = OccluderDepthPyramid,
                OcclusionDepthOverlay = OcclusionDebugOverlay,
            };

            debugOutput.Constants._DepthSizeInOccluderPixels = DepthBufferSizeInOccluderPixels;
            debugOutput.Constants._OccluderMipLayoutSizeX = (uint)OccluderMipLayoutSize.x;
            debugOutput.Constants._OccluderMipLayoutSizeY = (uint)OccluderMipLayoutSize.y;
            
            for (int i = 0; i < OccluderMipBounds.Length; ++i)
            {
                var mipBounds = OccluderMipBounds[i];
                unsafe
                {
                    debugOutput.Constants._OccluderMipBounds[4 * i + 0] = (uint)mipBounds.Offset.x;
                    debugOutput.Constants._OccluderMipBounds[4 * i + 1] = (uint)mipBounds.Offset.y;
                    debugOutput.Constants._OccluderMipBounds[4 * i + 2] = (uint)mipBounds.Size.x;
                    debugOutput.Constants._OccluderMipBounds[4 * i + 3] = (uint)mipBounds.Size.y;
                }
            }

            return debugOutput;
        }
    }
}