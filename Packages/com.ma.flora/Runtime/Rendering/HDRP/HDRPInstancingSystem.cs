// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

#if HAS_PACKAGE_UNITY_HDRP_12_0_0
using System;
using MA.Core;
using MA.Flora.Rendering.HDRP.InternalBridge;
using MA.Flora.Rendering.Occlusion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

using OccluderParameters = MA.Flora.Rendering.Occlusion.OccluderParameters;
using OccluderSubviewUpdate = MA.Flora.Rendering.Occlusion.OccluderSubviewUpdate;
using OcclusionCullingSettings = MA.Flora.Rendering.Occlusion.OcclusionCullingSettings;
using OcclusionTest = MA.Flora.Rendering.Occlusion.OcclusionTest;
using SubviewOcclusionTest = MA.Flora.Rendering.Occlusion.SubviewOcclusionTest;

namespace MA.Flora.Rendering.HDRP
{
    sealed class HDRPInstancingSystem : IDisposable
    {
        InstancingContext m_Context;
        BuildInstanceDrawsPass m_BuildInstanceDrawsPass;
        BuildOcclusionDepthPass m_BuildOcclusionDepthPass;
        DebugPyramidTexturePass m_DebugPyramidTexturePass;
        DebugOcclusionTestPass m_DebugOcclusionTestPass;

        GameObject m_CustomPassVolumeGameObject;
        CustomPassVolume m_BeforeEverythingVolume;
        CustomPassVolume m_PrepassVolume;
        CustomPassVolume m_AfterPostProcessVolume;

        public HDRPInstancingSystem(InstancingContext ctx)
        {
            HDRenderPipelineAsset hdrpAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (hdrpAsset == null)
            {
                Debug.Log("Flora HDRPInstancingSystem: HDRP asset is not set in GraphicsSettings.");
                return;
            }

            if (!hdrpAsset.currentPlatformRenderPipelineSettings.supportCustomPass)
            {
                Debug.Log("Flora HDRPInstancingSystem: Custom pass is not enabled in HDRP asset. Flora will enable it.");
                RenderPipelineSettings currentPlatformRenderPipelineSettings = hdrpAsset.currentPlatformRenderPipelineSettings;
                currentPlatformRenderPipelineSettings.supportCustomPass = true;
                hdrpAsset.currentPlatformRenderPipelineSettings = currentPlatformRenderPipelineSettings;
            }

            m_Context = ctx;
            m_BuildInstanceDrawsPass = new BuildInstanceDrawsPass(ctx);
            m_BuildOcclusionDepthPass = new BuildOcclusionDepthPass(ctx);
            m_DebugPyramidTexturePass = new DebugPyramidTexturePass(ctx);
            m_DebugOcclusionTestPass = new DebugOcclusionTestPass(ctx);

            m_BuildInstanceDrawsPass.enabled = true;
            m_BuildOcclusionDepthPass.enabled = false;
            m_DebugPyramidTexturePass.enabled = false;
            m_DebugOcclusionTestPass.enabled = false;

            m_CustomPassVolumeGameObject = new GameObject("Flora_HDRP") { hideFlags = HideFlags.HideAndDontSave };

            m_BeforeEverythingVolume = m_CustomPassVolumeGameObject.AddComponent<CustomPassVolume>();
            m_BeforeEverythingVolume.injectionPoint = CustomPassInjectionPoint.BeforeRendering;
            m_BeforeEverythingVolume.isGlobal = true;
            m_BeforeEverythingVolume.customPasses.Add(m_BuildInstanceDrawsPass);

            m_PrepassVolume = m_CustomPassVolumeGameObject.AddComponent<CustomPassVolume>();
            m_PrepassVolume.injectionPoint = CustomPassInjectionPoint.AfterOpaqueDepthAndNormal;
            m_PrepassVolume.isGlobal = true;
            m_PrepassVolume.customPasses.Add(m_BuildOcclusionDepthPass);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_AfterPostProcessVolume = m_CustomPassVolumeGameObject.AddComponent<CustomPassVolume>();
            m_AfterPostProcessVolume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            m_AfterPostProcessVolume.isGlobal = true;
            m_AfterPostProcessVolume.customPasses.Add(m_DebugPyramidTexturePass);
            m_AfterPostProcessVolume.customPasses.Add(m_DebugOcclusionTestPass);
#endif
        }

        public void Dispose()
        {
            if (m_CustomPassVolumeGameObject != null)
            {
                UnityUtility.Destroy(m_CustomPassVolumeGameObject);
                m_CustomPassVolumeGameObject = null;
            }
        }

        public void UpdateCamera(Camera camera, InstancedCameraID cameraID)
        {
            if (m_CustomPassVolumeGameObject == null)
                return;

            bool isOcclusionEnabled = m_Context.HasOcclusionManager() && m_Context.CameraManager.Data.Culling[cameraID].Flags.HasGPUOcclusionCulling();
            m_BuildOcclusionDepthPass.enabled = isOcclusionEnabled;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_AfterPostProcessVolume && m_BuildOcclusionDepthPass.enabled && DebugDisplayData.IsActive())
            {
                m_DebugPyramidTexturePass.enabled = DebugDisplayData.Instance.OccluderDepthOverlayEnabled;
                m_DebugOcclusionTestPass.enabled = DebugDisplayData.Instance.OcclusionOverlayEnabled;
            }
#endif
        }

        class BuildInstanceDrawsPass : CustomPass
        {
            InstancingContext m_Context;

            public BuildInstanceDrawsPass(InstancingContext instancingContext)
            {
                m_Context = instancingContext;
                name = $"Flora: {nameof(BuildInstanceDrawsPass)}";
            }

            protected override void Execute(CustomPassContext ctx)
            {
                HDCamera hdCamera = ctx.hdCamera;
                if (!m_Context.CameraManager.TryGetInstancedCamera(hdCamera.camera, out InstancedCameraID cameraID))
                    return;

                bool isSinglePassXR = hdCamera.IsSinglePassXREnabled();
                int subviewCount = isSinglePassXR ? 2 : 1;
                var settings = new OcclusionCullingSettings(hdCamera.camera.GetInstanceID(), OcclusionTest.TestAll)
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

                InstanceCuller context = m_Context.CameraManager.CullingContexts[cameraID];
                context.BuildInstanceDraws(ctx.cmd, settings, subviewOcclusionTests);
            }
        }

        class BuildOcclusionDepthPass : CustomPass
        {
            InstancingContext m_Context;

            public BuildOcclusionDepthPass(InstancingContext instancingContext)
            {
                m_Context = instancingContext;
                name = $"Flora: {nameof(BuildOcclusionDepthPass)}";
            }

            protected override void Execute(CustomPassContext ctx)
            {
                HDCamera hdCamera = ctx.hdCamera;
                bool isSinglePassXR = hdCamera.IsSinglePassXREnabled();
                OccluderParameters occluderParams = new OccluderParameters(hdCamera.camera.GetInstanceID())
                {
                    SubviewCount = isSinglePassXR ? 2 : 1,
                    DepthTextureRT = ctx.cameraDepthBuffer,
                    DepthSize = new Vector2Int(hdCamera.actualWidth, hdCamera.actualHeight),
                    DepthIsArray = TextureXR.useTexArray,
                };

                HDCamera.ViewConstants[] xrViewConstants = hdCamera.GetXRViewConstants();
                Span<OccluderSubviewUpdate> occluderSubviewUpdates = stackalloc OccluderSubviewUpdate[occluderParams.SubviewCount];
                for (int subviewIndex = 0; subviewIndex < occluderParams.SubviewCount; ++subviewIndex)
                {
                    occluderSubviewUpdates[subviewIndex] = new OccluderSubviewUpdate(subviewIndex)
                    {
                        DepthSliceIndex = subviewIndex,
                        ViewMatrix = xrViewConstants[subviewIndex].viewMatrix,
                        InvViewMatrix = xrViewConstants[subviewIndex].invViewMatrix,
                        GPUProjMatrix = xrViewConstants[subviewIndex].projMatrix,
                        ViewOffsetWorldSpace = xrViewConstants[subviewIndex].worldSpaceCameraPos,
                    };
                }

                OcclusionManager occlusionManager = m_Context.GetOcclusionManager();
                occlusionManager.UpdateInstanceOccluders(ctx.cmd, occluderParams, occluderSubviewUpdates);
            }
        }

        class DebugPyramidTexturePass : CustomPass
        {
            InstancingContext m_Context;

            public DebugPyramidTexturePass(InstancingContext instancingContext)
            {
                m_Context = instancingContext;
                name = $"Flora: {nameof(DebugPyramidTexturePass)}";
            }

            protected override void Execute(CustomPassContext ctx)
            {
                HDCamera hdCamera = ctx.hdCamera;
                float screenHeight = hdCamera.actualHeight;
                float maxHeight = screenHeight * 0.5f;
                OcclusionManager occlusionManager = m_Context.GetOcclusionManager();
                occlusionManager.RenderDebugOccluderOverlay(ctx.cmd,
                    DebugDisplayData.Instance, hdCamera.camera.GetInstanceID(),
                    new Vector2(0.0f, screenHeight - maxHeight), maxHeight);
            }
        }

        class DebugOcclusionTestPass : CustomPass
        {
            InstancingContext m_Context;

            public DebugOcclusionTestPass(InstancingContext instancingContext)
            {
                m_Context = instancingContext;
                name = $"Flora: {nameof(DebugOcclusionTestPass)}";

            }

            protected override void Execute(CustomPassContext ctx)
            {
                OcclusionManager occlusionManager = m_Context.GetOcclusionManager();
                occlusionManager.RenderDebugOcclusionTestOverlay(ctx.cmd, DebugDisplayData.Instance, ctx.hdCamera.camera.GetInstanceID());
            }
        }
    }
}
#endif
