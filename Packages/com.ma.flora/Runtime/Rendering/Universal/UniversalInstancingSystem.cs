// Copyright © Magnetic Arcade. All Rights Reserved.

#if HAS_PACKAGE_UNITY_URP_12_0_0
using System;
using System.Runtime.CompilerServices;
using MA.Flora.Rendering.Occlusion;
using MA.Flora.Rendering.Universal.InternalBridge;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
using OccluderParameters = MA.Flora.Rendering.Occlusion.OccluderParameters;
using OccluderSubviewUpdate = MA.Flora.Rendering.Occlusion.OccluderSubviewUpdate;
using OcclusionCullingSettings = MA.Flora.Rendering.Occlusion.OcclusionCullingSettings;
using OcclusionTest = MA.Flora.Rendering.Occlusion.OcclusionTest;
using SubviewOcclusionTest = MA.Flora.Rendering.Occlusion.SubviewOcclusionTest;
#endif

namespace MA.Flora.Rendering.Universal
{
    sealed class UniversalInstancingSystem : IDisposable
    {
        InstancingContext m_Context;
        BuildInstanceDrawsPass m_BuildInstanceDrawsPass;
        BuildOcclusionDepthPass m_BuildOcclusionDepthPass;
        DebugOccluderDepthOverlayPass m_DebugOccluderDepthOverlayPass;
        DebugOcclusionTestOverlayPass m_DebugOcclusionTestOverlayPass;

        public UniversalInstancingSystem(InstancingContext ctx)
        {
            m_Context = ctx;
            m_BuildInstanceDrawsPass = new BuildInstanceDrawsPass(ctx);
            m_BuildOcclusionDepthPass = new BuildOcclusionDepthPass(ctx);
            m_DebugOccluderDepthOverlayPass = new DebugOccluderDepthOverlayPass(ctx);
            m_DebugOcclusionTestOverlayPass = new DebugOcclusionTestOverlayPass(ctx);
        }

        public void Dispose() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateCamera(Camera camera, InstancedCameraID cameraID)
        {
            ScriptableRenderer scriptableRenderer;
            if (camera.TryGetComponent(out UniversalAdditionalCameraData urpCameraData))
                scriptableRenderer = urpCameraData.scriptableRenderer;
            else
                scriptableRenderer = UniversalRenderPipeline.asset.scriptableRenderer;

            scriptableRenderer.EnqueuePass(m_BuildInstanceDrawsPass);

            AddOcclusionPasses(scriptableRenderer, cameraID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddOcclusionPasses(ScriptableRenderer renderer, InstancedCameraID cameraID)
        {
            bool hasOcclusionCulling = m_Context.HasOcclusionManager() && m_Context.CameraManager.Data.Culling[cameraID].Flags.HasGPUOcclusionCulling();
#if UNITY_2023_3_OR_NEWER && FLORA_ENABLE_EXPERIMENTAL_GPU_DRIVEN_OCCLUSION_INTEGRATION
            hasOcclusionCulling &= !InstancingSystem.UseGPUDrivenOcclusion();
#endif
            if (hasOcclusionCulling)
            {
                renderer.EnqueuePass(m_BuildOcclusionDepthPass);

                if (DebugDisplayData.IsActive())
                {
                    if (DebugDisplayData.Instance.OccluderDepthOverlayEnabled)
                        renderer.EnqueuePass(m_DebugOccluderDepthOverlayPass);

                    if (DebugDisplayData.Instance.OcclusionOverlayEnabled)
                        renderer.EnqueuePass(m_DebugOcclusionTestOverlayPass);
                }
            }
        }

        abstract class InstancingPass : ScriptableRenderPass
        {
            protected InstancingContext Context;

            protected InstancingPass(InstancingContext context, RenderPassEvent renderPassEvent)
            {
                Context = context;
                profilingSampler = new ProfilingSampler($"Flora: {GetType().Name}");
                this.renderPassEvent = renderPassEvent;
            }

#if UNITY_2023_3_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
#endif
            public sealed override void Execute(ScriptableRenderContext renderContext, ref RenderingData renderingData)
            {
#if UNITY_2022_2_OR_NEWER
                CommandBuffer cmd = renderingData.GetCommandBuffer();
#else
                CommandBuffer cmd = CommandBufferPool.Get();
#endif

                using (new ProfilingScope(cmd, profilingSampler))
                {
                    Dispatch(cmd, ref renderingData);
                }

#if !UNITY_2022_2_OR_NEWER
                renderContext.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
#endif
            }

#if UNITY_2023_3_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
#endif
            protected abstract void Dispatch(CommandBuffer cmd, ref RenderingData renderingData);
        }

        sealed class BuildInstanceDrawsPass : InstancingPass
        {
            public BuildInstanceDrawsPass(InstancingContext context) : base(context, RenderPassEvent.BeforeRendering) { }

#if UNITY_2023_3_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
#endif
            protected override void Dispatch(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ref CameraData cameraData = ref renderingData.cameraData;
                if (!Context.CameraManager.TryGetInstancedCamera(cameraData.camera, out InstancedCameraID cameraID))
                    return;

                bool isSinglePassXR = cameraData.IsSinglePassXREnabled();
                int subviewCount = isSinglePassXR ? 2 : 1;
                var settings = new OcclusionCullingSettings(cameraData.camera.GetInstanceID(), OcclusionTest.TestAll)
                {
                    InstanceMultiplier = (isSinglePassXR && !SystemInfo.supportsMultiview) ? 2 : 1,
                };

                Span<SubviewOcclusionTest> subviewOcclusionTests = stackalloc SubviewOcclusionTest[subviewCount];
                for (int subviewIndex = 0; subviewIndex < subviewCount; ++subviewIndex)
                {
                    subviewOcclusionTests[subviewIndex] = new SubviewOcclusionTest
                    {
                        CullingSplitIndex = 0,
                        OccluderSubviewIndex = subviewIndex,
                    };
                }

                InstanceCuller culler = Context.CameraManager.CullingContexts[cameraID];
                culler.BuildInstanceDraws(cmd, settings, subviewOcclusionTests);
            }

#if UNITY_2023_3_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (!Context.CameraManager.TryGetInstancedCamera(cameraData.camera, out InstancedCameraID cameraID))
                    return;

                bool isSinglePassXR = cameraData.IsSinglePassXREnabled();
                int subviewCount = isSinglePassXR ? 2 : 1;
                var settings = new OcclusionCullingSettings(cameraData.camera.GetInstanceID(), OcclusionTest.TestAll)
                {
                    InstanceMultiplier = (isSinglePassXR && !SystemInfo.supportsMultiview) ? 2 : 1,
                };

                Span<SubviewOcclusionTest> subviewOcclusionTests = stackalloc SubviewOcclusionTest[subviewCount];
                for (int subviewIndex = 0; subviewIndex < subviewCount; ++subviewIndex)
                {
                    subviewOcclusionTests[subviewIndex] = new SubviewOcclusionTest
                    {
                        CullingSplitIndex = 0,
                        OccluderSubviewIndex = subviewIndex,
                    };
                }

                InstanceCuller context = Context.CameraManager.CullingContexts[cameraID];
                context.BuildInstanceDraws(renderGraph, settings, subviewOcclusionTests);
            }
#endif
        }

        // --- Occlusion Passes ---

        sealed class BuildOcclusionDepthPass : InstancingPass
        {
            public BuildOcclusionDepthPass(InstancingContext context) : base(context, RenderPassEvent.AfterRenderingPrePasses)
            {
            }

#if UNITY_2023_3_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
#endif
            protected override void Dispatch(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ref CameraData cameraData = ref renderingData.cameraData;

                RTHandle cameraDepthTarget = cameraData.renderer.GetDepthTarget();
                int viewInstanceID = cameraData.camera.GetInstanceID();
                int scaledWidth = cameraData.GetScaledCameraWidth();
                int scaledHeight = cameraData.GetScaledCameraHeight();
                bool isSinglePassXR = cameraData.IsSinglePassXREnabled();

                OccluderParameters occluderParams = new OccluderParameters(viewInstanceID)
                {
                    SubviewCount = isSinglePassXR ? 2 : 1,
                    DepthTextureRT = cameraDepthTarget,
                    DepthSize = new Vector2Int(scaledWidth, scaledHeight),
                    DepthIsArray = isSinglePassXR,
                };

                Span<OccluderSubviewUpdate> occluderSubviewUpdates = stackalloc OccluderSubviewUpdate[occluderParams.SubviewCount];
                for (int subviewIndex = 0; subviewIndex < occluderParams.SubviewCount; ++subviewIndex)
                {
                    var viewMatrix = cameraData.GetViewMatrix(subviewIndex);
                    var projMatrix = cameraData.GetProjectionMatrix(subviewIndex);
                    occluderSubviewUpdates[subviewIndex] = new OccluderSubviewUpdate(subviewIndex)
                    {
                        DepthSliceIndex = subviewIndex,
                        ViewMatrix = viewMatrix,
                        InvViewMatrix = viewMatrix.inverse,
                        GPUProjMatrix = GL.GetGPUProjectionMatrix(projMatrix, true),
                        ViewOffsetWorldSpace = Vector3.zero,
                    };
                }

                OcclusionManager occlusionManager = Context.GetOcclusionManager();
                occlusionManager.UpdateInstanceOccluders(cmd, occluderParams, occluderSubviewUpdates);
            }

#if UNITY_2023_3_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

                int scaledWidth = cameraData.GetScaledCameraWidth();
                int scaledHeight = cameraData.GetScaledCameraHeight();
                bool isSinglePassXR = cameraData.IsSinglePassXREnabled();

                TextureHandle depthTarget = (renderingData.renderingMode == RenderingMode.Deferred) ? resourceData.activeDepthTexture : resourceData.cameraDepthTexture;
                depthTarget = (cameraData.renderer.HasDepthPriming() && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth)) ? resourceData.activeDepthTexture : depthTarget;

                var occluderParams = new OccluderParameters(cameraData.camera.GetInstanceID())
                {
                    SubviewCount = isSinglePassXR ? 2 : 1,
                    DepthTextureHandle = depthTarget,
                    DepthSize = new Vector2Int(scaledWidth, scaledHeight),
                    DepthIsArray = isSinglePassXR,
                };

                Span<OccluderSubviewUpdate> occluderSubviewUpdates = stackalloc OccluderSubviewUpdate[occluderParams.SubviewCount];
                for (int subviewIndex = 0; subviewIndex < occluderParams.SubviewCount; ++subviewIndex)
                {
                    var viewMatrix = cameraData.GetViewMatrix(subviewIndex);
                    var projMatrix = cameraData.GetProjectionMatrix(subviewIndex);
                    occluderSubviewUpdates[subviewIndex] = new OccluderSubviewUpdate(subviewIndex)
                    {
                        DepthSliceIndex = subviewIndex,
                        ViewMatrix = viewMatrix,
                        InvViewMatrix = viewMatrix.inverse,
                        GPUProjMatrix = GL.GetGPUProjectionMatrix(projMatrix, true),
                        ViewOffsetWorldSpace = Vector3.zero,
                    };
                }

                OcclusionManager occlusionManager = Context.GetOcclusionManager();
                occlusionManager.UpdateInstanceOccluders(renderGraph, occluderParams, occluderSubviewUpdates);
            }
#endif
        }

        sealed class DebugOccluderDepthOverlayPass : InstancingPass
        {
            public DebugOccluderDepthOverlayPass(InstancingContext context) : base(context, RenderPassEvent.AfterRenderingPostProcessing) { }

#if UNITY_2023_3_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
#endif
            protected override void Dispatch(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ref CameraData cameraData = ref renderingData.cameraData;

                int viewInstanceID = cameraData.camera.GetInstanceID();
                int screenWidth = cameraData.GetScaledCameraWidth();
                int screenHeight = cameraData.GetScaledCameraHeight();
                float maxHeight = screenHeight * 0.5f;

                OcclusionManager occlusionManager = Context.GetOcclusionManager();
                occlusionManager.RenderDebugOccluderOverlay(cmd, DebugDisplayData.Instance, viewInstanceID,
                    new Vector2(0.25f * screenWidth, screenHeight - 1.5f * maxHeight), maxHeight);
            }

#if UNITY_2023_3_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                int viewInstanceID = cameraData.camera.GetInstanceID();

                int screenWidth = cameraData.GetScaledCameraWidth();
                int screenHeight = cameraData.GetScaledCameraHeight();
                float maxHeight = screenHeight * 0.5f;

                OcclusionManager occlusionManager = Context.GetOcclusionManager();
                occlusionManager.RenderDebugOccluderOverlay(renderGraph, DebugDisplayData.Instance, viewInstanceID,
                    new Vector2(0.25f * screenWidth, screenHeight - 1.5f * maxHeight), maxHeight, resourceData.activeColorTexture);
            }
#endif
        }

        sealed class DebugOcclusionTestOverlayPass : InstancingPass
        {
            public DebugOcclusionTestOverlayPass(InstancingContext context) : base(context, RenderPassEvent.AfterRenderingPostProcessing) { }

#if UNITY_2023_3_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
#endif
            protected override void Dispatch(CommandBuffer cmd, ref RenderingData renderingData)
            {
                int viewInstanceID = renderingData.cameraData.camera.GetInstanceID();
                OcclusionManager occlusionManager = Context.GetOcclusionManager();
                occlusionManager.RenderDebugOcclusionTestOverlay(cmd, DebugDisplayData.Instance, viewInstanceID);
            }

#if UNITY_2023_3_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                int viewInstanceID = cameraData.camera.GetInstanceID();
                OcclusionManager occlusionManager = Context.GetOcclusionManager();
                occlusionManager.RenderDebugOcclusionTestOverlay(renderGraph, DebugDisplayData.Instance, viewInstanceID, resourceData.activeColorTexture);
            }
#endif
        }
    }
}
#endif
