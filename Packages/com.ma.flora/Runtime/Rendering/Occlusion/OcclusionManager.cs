// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

#if !UNITY_2022_3_OR_NEWER
using MA.Collections;
#endif

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace MA.Flora.Rendering.Occlusion
{
    struct SilhouettePlaneCache : IDisposable
    {
        struct Slot
        {
            public bool IsActive;
            public int ViewInstanceID;
            public int PlaneCount;  // planeIndex = slotIndex * kMaxSilhouettePlanes
            public int LastUsedFrameIndex;

            public Slot(int viewInstanceID, int planeCount, int frameIndex)
            {
                IsActive = true;
                ViewInstanceID = viewInstanceID;
                PlaneCount = planeCount;
                LastUsedFrameIndex = frameIndex;
            }
        }

        const int k_MaxSilhouettePlanes = (int)OcclusionCullingCommonConfig.MaxOccluderSilhouettePlanes;
        NativeParallelHashMap<int, int> m_SubviewIDToIndexMap;
        NativeList<int> m_SlotFreeList;
        NativeList<Slot> m_Slots;
        NativeList<Plane> m_PlaneStorage;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Init()
        {
            m_SubviewIDToIndexMap = new NativeParallelHashMap<int, int>(16, Allocator.Persistent);
            m_SlotFreeList = new NativeList<int>(16, Allocator.Persistent);
            m_Slots = new NativeList<Slot>(16, Allocator.Persistent);
            m_PlaneStorage = new NativeList<Plane>(16 * k_MaxSilhouettePlanes, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            m_SubviewIDToIndexMap.Dispose();
            m_SlotFreeList.Dispose();
            m_Slots.Dispose();
            m_PlaneStorage.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(int viewInstanceID, ReadOnlySpan<Plane> planes, int frameIndex)
        {
            int planeCount = math.min(planes.Length, k_MaxSilhouettePlanes);

            if (!m_SubviewIDToIndexMap.TryGetValue(viewInstanceID, out int slotIndex))
            {
                if (m_SlotFreeList.Length > 0)
                {
                    // take a free slot from the free list
                    slotIndex = m_SlotFreeList[^1];
                    m_SlotFreeList.Length -= 1;
                }
                else
                {
                    // ensure we have capacity for a few more
                    if (m_Slots.Length == m_Slots.Capacity)
                    {
                        int newCapacity = m_Slots.Length + 8;
                        m_Slots.SetCapacity(newCapacity);
                        m_PlaneStorage.SetCapacity(newCapacity * k_MaxSilhouettePlanes);
                    }

                    // use the next slot in storage
                    slotIndex = m_Slots.Length;
                    int newSlotCount = slotIndex + 1;
                    m_Slots.ResizeUninitialized(newSlotCount);
                    m_PlaneStorage.ResizeUninitialized(newSlotCount * k_MaxSilhouettePlanes);
                }

                // associate with this view ID
                m_SubviewIDToIndexMap.Add(viewInstanceID, slotIndex);
            }

            m_Slots[slotIndex] = new Slot(viewInstanceID, planeCount, frameIndex);
            planes.CopyTo(m_PlaneStorage.AsArray().GetSubArray(slotIndex * k_MaxSilhouettePlanes, planeCount).AsSpan());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FreeUnusedSlots(int frameIndex, int maximumAge)
        {
            for (int slotIndex = 0; slotIndex < m_Slots.Length; ++slotIndex)
            {
                Slot slot = m_Slots[slotIndex];
                if (!slot.IsActive)
                    continue;

                if ((frameIndex - slot.LastUsedFrameIndex) > maximumAge)
                {
                    slot.IsActive = false;
                    m_Slots[slotIndex] = slot;
                    m_SubviewIDToIndexMap.Remove(slot.ViewInstanceID);
                    m_SlotFreeList.Add(slotIndex);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NativeArray<Plane> GetSubArray(int viewInstanceID)
        {
            int planeOffset = 0;
            int planeCount = 0;
            if (m_SubviewIDToIndexMap.TryGetValue(viewInstanceID, out int slotIndex))
            {
                planeOffset = slotIndex * k_MaxSilhouettePlanes;
                planeCount = m_Slots[slotIndex].PlaneCount;
            }
            return m_PlaneStorage.AsArray().GetSubArray(planeOffset, planeCount);
        }
    }

    class OcclusionManager : IDisposable
    {
        struct OccluderContextSlot
        {
            public bool Valid;
            public int LastUsedFrameIndex;
            public int ViewInstanceID;
        }

        static readonly int s_MaxContextGCFrame = 8; // Allow a few frames for alternate frame shadow updates before cleanup

        Material m_DebugOcclusionTestMaterial;
        Material m_OccluderDebugViewMaterial;

        ComputeShader m_OcclusionDebugCS;
        int m_ClearOcclusionDebugKernel;

        ComputeShader m_OccluderDepthPyramidCS;
        int m_OccluderDepthDownscaleKernel;
        int m_FrameIndex;

        SilhouettePlaneCache m_SilhouettePlaneCache;

        NativeParallelHashMap<int, int> m_ViewIDToIndexMap;
        List<OccluderContext> m_OccluderContextData;
        NativeList<OccluderContextSlot> m_OccluderContextSlots;
        NativeList<int> m_FreeOccluderContexts;

        public NativeArray<OcclusionCullingCommonShaderVariables> m_CommonShaderVariables;
        public ComputeBuffer m_CommonConstantBuffer;
        NativeArray<OcclusionCullingDebugShaderVariables> m_DebugShaderVariables;
        ComputeBuffer m_DebugConstantBuffer;

        ProfilingSampler m_ProfilingSamplerUpdateOccluders;
        ProfilingSampler m_ProfilingSamplerOcclusionTestOverlay;
        ProfilingSampler m_ProfilingSamplerOccluderOverlay;

        static class ShaderIDs
        {
            public static readonly int OcclusionCullingCommonShaderVariables = Shader.PropertyToID("OcclusionCullingCommonShaderVariables");
            public static readonly int OccluderDepthPyramid = Shader.PropertyToID("_OccluderDepthPyramid");
            public static readonly int OcclusionDebugOverlay = Shader.PropertyToID("_OcclusionDebugOverlay");
            public static readonly int OcclusionCullingDebugShaderVariables = Shader.PropertyToID("OcclusionCullingDebugShaderVariables");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OcclusionManager()
        {
            m_OccluderDepthPyramidCS = Resources.Load<ComputeShader>("Compute/BuildOcclusionDepth");
            m_OccluderDepthDownscaleKernel = m_OccluderDepthPyramidCS.FindKernel("OccluderDepthDownscale");

            m_OcclusionDebugCS = Resources.Load<ComputeShader>("Debug/DebugOcclusion");
            m_ClearOcclusionDebugKernel = m_OcclusionDebugCS.FindKernel("ClearOcclusionDebug");

            m_DebugOcclusionTestMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Flora/DebugOcclusionTest"));
            m_OccluderDebugViewMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Flora/DebugOccluder"));

            m_SilhouettePlaneCache.Init();

            m_ViewIDToIndexMap = new NativeParallelHashMap<int, int>(64, Allocator.Persistent);
            m_OccluderContextData = new List<OccluderContext>();
            m_OccluderContextSlots = new NativeList<OccluderContextSlot>(64, Allocator.Persistent);
            m_FreeOccluderContexts = new NativeList<int>(64, Allocator.Persistent);

            m_ProfilingSamplerUpdateOccluders = new ProfilingSampler("Flora:UpdateOccluders");
            m_ProfilingSamplerOcclusionTestOverlay = new ProfilingSampler("Flora:OcclusionTestOverlay");
            m_ProfilingSamplerOccluderOverlay = new ProfilingSampler("Flora:OccluderOverlay");

            m_CommonShaderVariables = new NativeArray<OcclusionCullingCommonShaderVariables>(1, Allocator.Persistent);
            m_CommonConstantBuffer = new ComputeBuffer(1, UnsafeUtility.SizeOf<OcclusionCullingCommonShaderVariables>(), ComputeBufferType.Constant);
            m_DebugShaderVariables = new NativeArray<OcclusionCullingDebugShaderVariables>(1, Allocator.Persistent);
            m_DebugConstantBuffer = new ComputeBuffer(1, UnsafeUtility.SizeOf<OcclusionCullingDebugShaderVariables>(), ComputeBufferType.Constant);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            for (int i = 0; i < m_OccluderContextData.Count; ++i)
            {
                if (m_OccluderContextSlots[i].Valid)
                    m_OccluderContextData[i].Dispose();
            }

            m_SilhouettePlaneCache.Dispose();

            m_ViewIDToIndexMap.Dispose();
            m_FreeOccluderContexts.Dispose();
            m_OccluderContextData.Clear();
            m_OccluderContextSlots.Dispose();

            m_CommonShaderVariables.Dispose();
            m_CommonConstantBuffer.Release();
            m_DebugShaderVariables.Dispose();
            m_DebugConstantBuffer.Release();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool UseOcclusionDebug(in OccluderContext occluderCtx)
        {
            return occluderCtx.OcclusionDebugOverlaySize != 0;
        }

        // --- Prepare Occlusion Test / CommandBuffer Rendering ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrepareCulling(
            CommandBuffer cmd, in OccluderContext occluderCtx,  in OcclusionCullingSettings settings, in InstanceOcclusionTestSubviewSettings subviewSettings,
            in OcclusionTestComputeShader shader, bool useOcclusionDebug)
        {
            OccluderContext.SetKeyword(cmd, shader.CS, shader.OcclusionDebugKeyword, useOcclusionDebug);

            bool debugOverlayCountVisible = false;
            bool overrideOcclusionTestToAlwaysPass = false;
            if (DebugDisplayData.TryGetActive(out DebugDisplayData debugData))
            {
                debugOverlayCountVisible = debugData.OcclusionOverlayCountVisible;
                overrideOcclusionTestToAlwaysPass = debugData.OcclusionOverrideTestToAlwaysPass;
            }

            m_CommonShaderVariables[0] = new OcclusionCullingCommonShaderVariables(
                in occluderCtx,
                subviewSettings,
                debugOverlayCountVisible,
                overrideOcclusionTestToAlwaysPass);
            cmd.SetBufferData(m_CommonConstantBuffer, m_CommonShaderVariables);

            cmd.SetComputeConstantBufferParam(shader.CS, ShaderIDs.OcclusionCullingCommonShaderVariables, m_CommonConstantBuffer, 0, m_CommonConstantBuffer.stride);

            DispatchDebugClear(cmd, settings.ViewInstanceID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetDepthPyramid(CommandBuffer cmd, in OcclusionTestComputeShader shader, int kernel, in OccluderHandles occluderHandles)
        {
            cmd.SetComputeTextureParam(shader.CS, kernel, ShaderIDs.OccluderDepthPyramid, occluderHandles.OccluderDepthPyramid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetDebugPyramid(CommandBuffer cmd, in OcclusionTestComputeShader shader, int kernel, in OccluderHandles occluderHandles)
        {
            cmd.SetComputeBufferParam(shader.CS, kernel, ShaderIDs.OcclusionDebugOverlay, occluderHandles.OcclusionDebugOverlay);
        }

        // --- Prepare Occlusion Test / RenderGraph Rendering ---
#if UNITY_2023_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PrepareCulling(
            ComputeCommandBuffer cmd, in OccluderContext occluderCtx,  in OcclusionCullingSettings settings,
            in InstanceOcclusionTestSubviewSettings subviewSettings, in OcclusionTestComputeShader shader, bool useOcclusionDebug)
        {
            OccluderContext.SetKeyword(cmd, shader.CS, shader.OcclusionDebugKeyword, useOcclusionDebug);

            bool debugOverlayCountVisible = false;
            bool overrideOcclusionTestToAlwaysPass = false;
            if (DebugDisplayData.TryGetActive(out DebugDisplayData debugData))
            {
                debugOverlayCountVisible = debugData.OcclusionOverlayCountVisible;
                overrideOcclusionTestToAlwaysPass = debugData.OcclusionOverrideTestToAlwaysPass;
            }

            m_CommonShaderVariables[0] = new OcclusionCullingCommonShaderVariables(
                in occluderCtx,
                subviewSettings,
                debugOverlayCountVisible,
                overrideOcclusionTestToAlwaysPass);
            cmd.SetBufferData(m_CommonConstantBuffer, m_CommonShaderVariables);

            cmd.SetComputeConstantBufferParam(shader.CS, ShaderIDs.OcclusionCullingCommonShaderVariables, m_CommonConstantBuffer, 0, m_CommonConstantBuffer.stride);

            DispatchDebugClear(cmd, settings.ViewInstanceID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetDepthPyramid(ComputeCommandBuffer cmd, in OcclusionTestComputeShader shader, int kernel, in OccluderHandlesRenderGraph occluderHandles)
        {
            cmd.SetComputeTextureParam(shader.CS, kernel, ShaderIDs.OccluderDepthPyramid, occluderHandles.OccluderDepthPyramid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetDebugPyramid(ComputeCommandBuffer cmd, in OcclusionTestComputeShader shader, int kernel, in OccluderHandlesRenderGraph occluderHandles)
        {
            cmd.SetComputeBufferParam(shader.CS, kernel, ShaderIDs.OcclusionDebugOverlay, occluderHandles.OcclusionDebugOverlay);
        }
#endif

        // --- Occlusion Debug Test Overlay / CommandBuffer Rendering ---

        public void RenderDebugOcclusionTestOverlay(CommandBuffer cmd, DebugDisplayData debugSettings, int viewInstanceID)
        {
            if (debugSettings is not { OcclusionOverlayEnabled: true })
                return;

            OcclusionCullingDebugOutput debugOutput = GetOcclusionTestDebugOutput(viewInstanceID);
            if (debugOutput.OcclusionDepthOverlay == null)
                return;

            m_DebugShaderVariables[0] = debugOutput.Constants;
            cmd.SetBufferData(m_DebugConstantBuffer, m_DebugShaderVariables);
            m_DebugOcclusionTestMaterial.SetConstantBuffer(ShaderIDs.OcclusionCullingDebugShaderVariables, m_DebugConstantBuffer, 0, m_DebugConstantBuffer.stride);

            // cmd.SetRenderTarget(colorBuffer);
            cmd.SetGlobalBuffer(ShaderIDs.OcclusionDebugOverlay, debugOutput.OcclusionDepthOverlay);
            CoreUtils.DrawFullScreen(cmd, m_DebugOcclusionTestMaterial);
        }

        // --- Occlusion Debug Test Overlay / RenderGraph Rendering ---
#if UNITY_2023_3_OR_NEWER
        class OcclusionTestOverlaySetupPassData
        {
            public OcclusionCullingDebugShaderVariables Constants;
        }

        class OcclusionTestOverlayPassData
        {
            public BufferHandle DebugPyramid;
        }

        public void RenderDebugOcclusionTestOverlay(RenderGraph renderGraph, DebugDisplayData debugSettings, int viewInstanceID, TextureHandle colorBuffer)
        {
            if (debugSettings is not { OcclusionOverlayEnabled: true })
                return;

            OcclusionCullingDebugOutput debugOutput = GetOcclusionTestDebugOutput(viewInstanceID);
            if (debugOutput.OcclusionDepthOverlay == null)
                return;

            using (var builder = renderGraph.AddComputePass<OcclusionTestOverlaySetupPassData>("OcclusionTestOverlay", out var passData, m_ProfilingSamplerOcclusionTestOverlay))
            {
                builder.AllowPassCulling(false);

                passData.Constants = debugOutput.Constants;

                builder.SetRenderFunc(
                    (OcclusionTestOverlaySetupPassData data, ComputeGraphContext ctx) =>
                    {
                        var occ = InstancingSystem.Instance.Context.GetOcclusionManager();

                        occ.m_DebugShaderVariables[0] = data.Constants;
                        ctx.cmd.SetBufferData(occ.m_DebugConstantBuffer, occ.m_DebugShaderVariables);

                        occ.m_DebugOcclusionTestMaterial.SetConstantBuffer(
                            ShaderIDs.OcclusionCullingDebugShaderVariables,
                            occ.m_DebugConstantBuffer,
                            0,
                            occ.m_DebugConstantBuffer.stride);
                    });
            }

            using (var builder = renderGraph.AddRasterRenderPass<OcclusionTestOverlayPassData>("OcclusionTestOverlay", out var passData, m_ProfilingSamplerOcclusionTestOverlay))
            {
                builder.AllowGlobalStateModification(true);

                passData.DebugPyramid = renderGraph.ImportBuffer(debugOutput.OcclusionDepthOverlay);

                builder.SetRenderAttachment(colorBuffer, 0);
                builder.UseBuffer(passData.DebugPyramid);

                builder.SetRenderFunc(
                    (OcclusionTestOverlayPassData data, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.SetGlobalBuffer(ShaderIDs.OcclusionDebugOverlay, data.DebugPyramid);
                        CoreUtils.DrawFullScreen(ctx.cmd, m_DebugOcclusionTestMaterial);
                    });
            }
        }
#endif

        // --- Occlusion Debug Occluder Overlay / CommandBuffer Rendering ---

        static ObjectPool<MaterialPropertyBlock> s_MaterialPropertyBlockPool = new ObjectPool<MaterialPropertyBlock>(null, l => l.Clear());

        public void RenderDebugOccluderOverlay(CommandBuffer cmd, DebugDisplayData debugSettings, int viewInstanceID, Vector2 screenPos, float maxHeight)
        {
            if (debugSettings is not { OccluderDepthOverlayEnabled: true })
                return;

            RTHandle occluderTexture = GetOcclusionTestDebugOutput(viewInstanceID).OccluderDepthPyramid;
            if (occluderTexture == null)
                return;

            Material debugMaterial = m_OccluderDebugViewMaterial;
            int passIndex = debugMaterial.FindPass("DebugOccluder");

            Vector2 outputSize = occluderTexture.referenceSize;
            float scaleFactor = maxHeight / outputSize.y;
            outputSize *= scaleFactor;
            Rect viewport = new Rect(screenPos.x, screenPos.y, outputSize.x, outputSize.y);

            MaterialPropertyBlock mpb = s_MaterialPropertyBlockPool.Get();
            mpb.SetTexture("_OccluderTexture", occluderTexture);
            mpb.SetVector("_ValidRange", debugSettings.OcclusionDepthViewRange);
            cmd.SetViewport(viewport);
            cmd.DrawProcedural(Matrix4x4.identity, debugMaterial, passIndex, MeshTopology.Triangles, 3, 1, mpb);
            s_MaterialPropertyBlockPool.Release(mpb);
        }

        // --- Occlusion Debug Occluder Overlay / RenderGraph Rendering ---
#if UNITY_2023_3_OR_NEWER
        struct DebugOccluderViewData
        {
            public int PassIndex;
            public Rect Viewport;
            public bool Valid;
        }

        class OccluderOverlayPassData
        {
            public Material DebugMaterial;
            public RTHandle OccluderTexture;
            public Rect Viewport;
            public int PassIndex;
            public Vector2 ValidRange;
        }

        public void RenderDebugOccluderOverlay(RenderGraph renderGraph, DebugDisplayData debugSettings, int viewInstanceID, Vector2 screenPos, float maxHeight, TextureHandle colorBuffer)
        {
            if (debugSettings is not { OccluderDepthOverlayEnabled: true })
                return;

            var occluderTexture = GetOcclusionTestDebugOutput(viewInstanceID).OccluderDepthPyramid;
            if (occluderTexture == null)
                return;

            Material debugMaterial = m_OccluderDebugViewMaterial;
            int passIndex = debugMaterial.FindPass("DebugOccluder");

            Vector2 outputSize = occluderTexture.referenceSize;
            float scaleFactor = maxHeight / outputSize.y;
            outputSize *= scaleFactor;
            Rect viewport = new Rect(screenPos.x, screenPos.y, outputSize.x, outputSize.y);

            using (var builder = renderGraph.AddRasterRenderPass<OccluderOverlayPassData>("OccluderOverlay", out var passData, m_ProfilingSamplerOccluderOverlay))
            {
                builder.AllowGlobalStateModification(true);

                builder.SetRenderAttachment(colorBuffer, 0);

                passData.DebugMaterial = debugMaterial;
                passData.OccluderTexture = occluderTexture;
                passData.Viewport = viewport;
                passData.PassIndex = passIndex;
                passData.ValidRange = debugSettings.OcclusionDepthViewRange;

                builder.SetRenderFunc(
                    (OccluderOverlayPassData data, RasterGraphContext ctx) =>
                    {
                        var mpb = ctx.renderGraphPool.GetTempMaterialPropertyBlock();

                        mpb.SetTexture("_OccluderTexture", data.OccluderTexture);
                        mpb.SetVector("_ValidRange", data.ValidRange);

                        ctx.cmd.SetViewport(data.Viewport);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.DebugMaterial, data.PassIndex, MeshTopology.Triangles, 3, 1, mpb);
                    });
            }
        }
#endif

        // --- Occlusion Debug Clear / CommandBuffer Rendering ---

        public void DispatchDebugClear(CommandBuffer cmd, int viewInstanceID)
        {
            if (!m_ViewIDToIndexMap.TryGetValue(viewInstanceID, out int contextIndex))
                return;

            OccluderContext occluderCtx = m_OccluderContextData[contextIndex];

            if (UseOcclusionDebug(in occluderCtx) && occluderCtx.DebugNeedsClear)
            {
                ComputeShader cs = m_OcclusionDebugCS;
                int kernel = m_ClearOcclusionDebugKernel;

                cmd.SetComputeConstantBufferParam(cs, ShaderIDs.OcclusionCullingCommonShaderVariables, m_CommonConstantBuffer, 0, m_CommonConstantBuffer.stride);
                cmd.SetComputeBufferParam(cs, kernel, ShaderIDs.OcclusionDebugOverlay, occluderCtx.OcclusionDebugOverlay);

                Vector2Int mip0Size = occluderCtx.OccluderMipBounds[0].Size;
                cmd.DispatchCompute(cs, kernel, (mip0Size.x + 7) / 8, (mip0Size.y + 7) / 8, occluderCtx.SubviewCount);

                // mark as cleared in the dictionary
                occluderCtx.DebugNeedsClear = false;
                m_OccluderContextData[contextIndex] = occluderCtx;
            }
        }

        // --- Occlusion Debug Clear / RenderGraph Rendering ---
#if UNITY_2023_3_OR_NEWER
        public void DispatchDebugClear(ComputeCommandBuffer cmd, int viewInstanceID)
        {
            if (!m_ViewIDToIndexMap.TryGetValue(viewInstanceID, out int contextIndex))
                return;

            OccluderContext occluderCtx = m_OccluderContextData[contextIndex];

            if (UseOcclusionDebug(in occluderCtx) && occluderCtx.DebugNeedsClear)
            {
                ComputeShader cs = m_OcclusionDebugCS;
                int kernel = m_ClearOcclusionDebugKernel;

                cmd.SetComputeConstantBufferParam(cs, ShaderIDs.OcclusionCullingCommonShaderVariables, m_CommonConstantBuffer, 0, m_CommonConstantBuffer.stride);
                cmd.SetComputeBufferParam(cs, kernel, ShaderIDs.OcclusionDebugOverlay, occluderCtx.OcclusionDebugOverlay);

                Vector2Int mip0Size = occluderCtx.OccluderMipBounds[0].Size;
                cmd.DispatchCompute(cs, kernel, (mip0Size.x + 7) / 8, (mip0Size.y + 7) / 8, occluderCtx.SubviewCount);

                // mark as cleared in the dictionary
                occluderCtx.DebugNeedsClear = false;
                m_OccluderContextData[contextIndex] = occluderCtx;
            }
        }
#endif

        // --- Prepare Occluders / Command Buffer Rendering ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        OccluderHandles PrepareOccluders(CommandBuffer cmd, in OccluderParameters occluderParams)
        {
            OccluderHandles occluderHandles = new OccluderHandles();
            if (occluderParams.DepthTextureRT != null)
            {
                if (!m_ViewIDToIndexMap.TryGetValue(occluderParams.ViewInstanceID, out int contextIndex))
                    contextIndex = NewContext(occluderParams.ViewInstanceID);

                OccluderContext ctx = m_OccluderContextData[contextIndex];
                ctx.PrepareOccluders(occluderParams);
                occluderHandles.OccluderDepthPyramid = ctx.OccluderDepthPyramid;
                m_OccluderContextData[contextIndex] = ctx;
            }
            else
            {
                DeleteContext(occluderParams.ViewInstanceID);
            }
            return occluderHandles;
        }

        // --- Prepare Occluders / RenderGraph Rendering ---
#if UNITY_2023_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        OccluderHandlesRenderGraph PrepareOccluders(RenderGraph renderGraph, in OccluderParameters occluderParams)
        {
            OccluderHandlesRenderGraph occluderHandles = new OccluderHandlesRenderGraph();
            if (occluderParams.DepthTextureHandle.IsValid())
            {
                if (!m_ViewIDToIndexMap.TryGetValue(occluderParams.ViewInstanceID, out var contextIndex))
                    contextIndex = NewContext(occluderParams.ViewInstanceID);

                OccluderContext ctx = m_OccluderContextData[contextIndex];
                ctx.PrepareOccluders(occluderParams);
                occluderHandles = ctx.Import(renderGraph);
                m_OccluderContextData[contextIndex] = ctx;
            }
            else
            {
                DeleteContext(occluderParams.ViewInstanceID);
            }
            return occluderHandles;
        }
#endif

        // --- Create Far Depth Pyramid / CommandBuffer Rendering ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CreateFarDepthPyramid(CommandBuffer cmd, in OccluderParameters occluderParams, ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates, in OccluderHandles occluderHandles)
        {
            if (!m_ViewIDToIndexMap.TryGetValue(occluderParams.ViewInstanceID, out int contextIndex))
                return;

            NativeArray<Plane> silhouettePlanes = m_SilhouettePlaneCache.GetSubArray(occluderParams.ViewInstanceID);

            OccluderContext ctx = m_OccluderContextData[contextIndex];
            ctx.CreateFarDepthPyramid(cmd, occluderParams, occluderSubviewUpdates, occluderHandles, silhouettePlanes, m_OccluderDepthPyramidCS, m_OccluderDepthDownscaleKernel);
            ctx.Version++;
            m_OccluderContextData[contextIndex] = ctx;

            OccluderContextSlot slot = m_OccluderContextSlots[contextIndex];
            slot.LastUsedFrameIndex = m_FrameIndex;
            m_OccluderContextSlots[contextIndex] = slot;
        }

        // --- Create Far Depth Pyramid / RenderGraph Rendering ---
#if UNITY_2023_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CreateFarDepthPyramid(ComputeCommandBuffer cmd, in OccluderParameters occluderParams, ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates, in OccluderHandlesRenderGraph occluderHandles)
        {
            if (!m_ViewIDToIndexMap.TryGetValue(occluderParams.ViewInstanceID, out int contextIndex))
                return;

            NativeArray<Plane> silhouettePlanes = m_SilhouettePlaneCache.GetSubArray(occluderParams.ViewInstanceID);

            OccluderContext ctx = m_OccluderContextData[contextIndex];
            ctx.CreateFarDepthPyramid(cmd, occluderParams, occluderSubviewUpdates, occluderHandles, silhouettePlanes, m_OccluderDepthPyramidCS, m_OccluderDepthDownscaleKernel);
            ctx.Version++;
            m_OccluderContextData[contextIndex] = ctx;

            OccluderContextSlot slot = m_OccluderContextSlots[contextIndex];
            slot.LastUsedFrameIndex = m_FrameIndex;
            m_OccluderContextSlots[contextIndex] = slot;
        }
#endif

        // --- Update Occluders / CommandBuffer Rendering ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool PrepareInstanceOccluders(CommandBuffer cmd, in OccluderParameters occluderParams, ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates)
        {
            OccluderHandles occluderHandles = PrepareOccluders(cmd, occluderParams);
            if (occluderHandles.OccluderDepthPyramid == null)
                return false;

            if (!m_ViewIDToIndexMap.TryGetValue(occluderParams.ViewInstanceID, out int contextIndex))
                return false;

            OccluderContext ctx = m_OccluderContextData[contextIndex];
            ctx.Version++;
            m_OccluderContextData[contextIndex] = ctx;

            OccluderContextSlot slot = m_OccluderContextSlots[contextIndex];
            slot.LastUsedFrameIndex = m_FrameIndex;
            m_OccluderContextSlots[contextIndex] = slot;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool UpdateInstanceOccluders(CommandBuffer cmd, in OccluderParameters occluderParams, ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates)
        {
            OccluderHandles occluderHandles = PrepareOccluders(cmd, occluderParams);
            if (occluderHandles.OccluderDepthPyramid == null)
                return false;

            // int subviewMask = 0;
            // for (int i = 0; i < occluderSubviewUpdates.Length; ++i)
            //     subviewMask |= 1 << occluderSubviewUpdates[i].SubviewIndex;

            CreateFarDepthPyramid(cmd, occluderParams, occluderSubviewUpdates, occluderHandles);
            // batcher.instanceCullingBatcher.InstanceOccludersUpdated(data.occluderParams.viewInstanceID, subviewMask);

            return true;
        }

        // --- Update Occluders / RenderGraph Rendering ---
#if UNITY_2023_3_OR_NEWER
        class UpdateOccludersPassData
        {
            public OccluderParameters OccluderParams;
            public List<OccluderSubviewUpdate> OccluderSubviewUpdates;
            public OccluderHandlesRenderGraph OccluderHandles;
        }

        public bool PrepareInstanceOccluders(RenderGraph renderGraph, in OccluderParameters occluderParams, ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates)
        {
            var occluderHandles = PrepareOccluders(renderGraph, occluderParams);
            if (!occluderHandles.OccluderDepthPyramid.IsValid())
                return false;

            if (!m_ViewIDToIndexMap.TryGetValue(occluderParams.ViewInstanceID, out int contextIndex))
                return false;

            OccluderContext ctx = m_OccluderContextData[contextIndex];
            ctx.Version++;
            m_OccluderContextData[contextIndex] = ctx;

            OccluderContextSlot slot = m_OccluderContextSlots[contextIndex];
            slot.LastUsedFrameIndex = m_FrameIndex;
            m_OccluderContextSlots[contextIndex] = slot;
            return true;
        }

        public bool UpdateInstanceOccluders(RenderGraph renderGraph, in OccluderParameters occluderParams, ReadOnlySpan<OccluderSubviewUpdate> occluderSubviewUpdates)
        {
            var occluderHandles = PrepareOccluders(renderGraph, occluderParams);
            if (!occluderHandles.OccluderDepthPyramid.IsValid())
                return false;

            using (var builder = renderGraph.AddComputePass<UpdateOccludersPassData>("Update Occluders", out var passData, m_ProfilingSamplerUpdateOccluders))
            {
                builder.AllowGlobalStateModification(true);

                passData.OccluderParams = occluderParams;
                if (passData.OccluderSubviewUpdates is null)
                    passData.OccluderSubviewUpdates = new List<OccluderSubviewUpdate>();
                else
                    passData.OccluderSubviewUpdates.Clear();
                for (int i = 0; i < occluderSubviewUpdates.Length; ++i)
                    passData.OccluderSubviewUpdates.Add(occluderSubviewUpdates[i]);
                passData.OccluderHandles = occluderHandles;

                builder.UseTexture(passData.OccluderParams.DepthTextureHandle);
                passData.OccluderHandles.UseForOccluderUpdate(builder);

                builder.SetRenderFunc(
                    (UpdateOccludersPassData data, ComputeGraphContext context) =>
                    {
                        Span<OccluderSubviewUpdate> occluderSubviewUpdates = stackalloc OccluderSubviewUpdate[data.OccluderSubviewUpdates.Count];
                        int subviewMask = 0;
                        for (int i = 0; i < data.OccluderSubviewUpdates.Count; ++i)
                        {
                            occluderSubviewUpdates[i] = data.OccluderSubviewUpdates[i];
                            subviewMask |= 1 << data.OccluderSubviewUpdates[i].SubviewIndex;
                        }

                        var ctx = InstancingSystem.Instance.Context.GetOcclusionManager();
                        ctx.CreateFarDepthPyramid(context.cmd, in data.OccluderParams, occluderSubviewUpdates, in data.OccluderHandles);
                    });
            }

            return true;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateSilhouettePlanes(int viewInstanceID, ReadOnlySpan<Plane> planes)
        {
            m_SilhouettePlaneCache.Update(viewInstanceID, planes, m_FrameIndex);
        }

        internal OcclusionCullingDebugOutput GetOcclusionTestDebugOutput(int viewInstanceID)
        {
            if (m_ViewIDToIndexMap.TryGetValue(viewInstanceID, out int contextIndex) && m_OccluderContextSlots[contextIndex].Valid)
                return m_OccluderContextData[contextIndex].GetDebugOutput();

            return new OcclusionCullingDebugOutput();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasOccluderContext(int viewInstanceID)
        {
            return m_ViewIDToIndexMap.ContainsKey(viewInstanceID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetOccluderContext(int viewInstanceID, out OccluderContext occluderContext)
        {
            if (m_ViewIDToIndexMap.TryGetValue(viewInstanceID, out int contextIndex) && m_OccluderContextSlots[contextIndex].Valid)
            {
                occluderContext = m_OccluderContextData[contextIndex];
                return true;
            }

            occluderContext = new OccluderContext();
            return false;
        }

        public void NextFrame()
        {
            for (int i = 0; i < m_OccluderContextData.Count; ++i)
            {
                if (!m_OccluderContextSlots[i].Valid)
                    continue;

                OccluderContext occluderCtx = m_OccluderContextData[i];
                OccluderContextSlot slot = m_OccluderContextSlots[i];
                //Garbage collect unused contexts for a long time:
                if ((m_FrameIndex - slot.LastUsedFrameIndex) >= s_MaxContextGCFrame)
                {
                    DeleteContext(slot.ViewInstanceID);
                    continue;
                }

                occluderCtx.DebugNeedsClear = true;
                m_OccluderContextData[i] = occluderCtx;
            }

            m_SilhouettePlaneCache.FreeUnusedSlots(m_FrameIndex, s_MaxContextGCFrame);
            ++m_FrameIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int NewContext(int viewInstanceID)
        {
            int newSlot;
            OccluderContextSlot newCtxSlot = new OccluderContextSlot { Valid = true, ViewInstanceID = viewInstanceID, LastUsedFrameIndex = m_FrameIndex };
            OccluderContext newCtx = new OccluderContext();
            if (m_FreeOccluderContexts.Length > 0)
            {
                newSlot = m_FreeOccluderContexts[^1];
                m_FreeOccluderContexts.RemoveAt(m_FreeOccluderContexts.Length - 1);
                m_OccluderContextData[newSlot] = newCtx;
                m_OccluderContextSlots[newSlot] = newCtxSlot;
            }
            else
            {
                newSlot = m_OccluderContextData.Count;
                m_OccluderContextData.Add(newCtx);
                m_OccluderContextSlots.Add(newCtxSlot);
            }

            m_ViewIDToIndexMap.Add(viewInstanceID, newSlot);
            return newSlot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DeleteContext(int viewInstanceID)
        {
            if (!m_ViewIDToIndexMap.TryGetValue(viewInstanceID, out int contextIndex) || !m_OccluderContextSlots[contextIndex].Valid)
                return;

            m_OccluderContextData[contextIndex].Dispose();
            m_OccluderContextSlots[contextIndex] = new OccluderContextSlot { Valid = false };
            m_FreeOccluderContexts.Add(contextIndex);
            m_ViewIDToIndexMap.Remove(viewInstanceID);
        }
    }
}
