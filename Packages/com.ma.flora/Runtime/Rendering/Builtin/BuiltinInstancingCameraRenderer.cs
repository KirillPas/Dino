// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Flora.Rendering.Occlusion;
using UnityEngine;
using UnityEngine.Rendering;

using OccluderParameters = MA.Flora.Rendering.Occlusion.OccluderParameters;
using OccluderSubviewUpdate = MA.Flora.Rendering.Occlusion.OccluderSubviewUpdate;
using OcclusionCullingSettings = MA.Flora.Rendering.Occlusion.OcclusionCullingSettings;
using OcclusionTest = MA.Flora.Rendering.Occlusion.OcclusionTest;
using SubviewOcclusionTest = MA.Flora.Rendering.Occlusion.SubviewOcclusionTest;

namespace MA.Flora.Rendering.Builtin
{
    sealed class BuiltinInstancingCameraRenderer : IDisposable
    {
        InstancingContext m_Context;
        Camera m_Camera;
        RenderingPath m_RenderingPath;
        DepthTextureMode m_DepthTextureMode;
        InstancedCameraID m_CameraID;

        CameraEvent m_BuildInstanceDrawsEvent;
        CommandBuffer m_BuildInstanceDrawsCommandBuffer;
        bool m_BuildInstanceDrawsRegistered;

        CameraEvent m_BuildOcclusionDepthPassEvent;
        CommandBuffer m_BuildOcclusionDepthCommandBuffer;
        bool m_BuildOcclusionDepthRegistered;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        CommandBuffer m_DebugOcclusionDepthCommandBuffer;
        bool m_DebugOcclusionDepthRegistered;
        CommandBuffer m_DebugOcclusionTestOverlayCommandBuffer;
        bool m_DebugOcclusionTestOverlayRegistered;
#endif

        static readonly RTHandle k_CameraTarget = RTHandles.Alloc(BuiltinRenderTextureType.CameraTarget);
        static readonly RTHandle k_Depth        = RTHandles.Alloc(BuiltinRenderTextureType.Depth);
        static readonly RTHandle k_DepthNormals = RTHandles.Alloc(BuiltinRenderTextureType.DepthNormals);

        const CameraEvent k_DebugOcclusionDepthPassEvent = CameraEvent.AfterImageEffects;
        const CameraEvent k_DebugOccludedOverlayEvent    = CameraEvent.AfterImageEffects;

        public BuiltinInstancingCameraRenderer(InstancingContext context, Camera camera, InstancedCameraID cameraID)
        {
            m_Camera = camera;
            m_RenderingPath = camera.actualRenderingPath;
            m_DepthTextureMode = camera.depthTextureMode;
            m_Context = context;
            m_CameraID = cameraID;

            m_BuildInstanceDrawsCommandBuffer = new CommandBuffer {name = "Flora: Build Instance Draws"};
            m_BuildOcclusionDepthCommandBuffer = new CommandBuffer {name = "Flora: Build Occlusion Depth Pass"};

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_DebugOcclusionDepthCommandBuffer = new CommandBuffer {name = "Flora: Debug Occlusion Pyramid Texture Pass"};
            m_DebugOcclusionTestOverlayCommandBuffer = new CommandBuffer {name = "Flora: Debug Occlusion Test Pass"};
#endif

            UpdateCommandBufferEvents();
        }

        public void Dispose()
        {
            RemoveAllCommandBuffers();

            m_BuildInstanceDrawsCommandBuffer.Dispose();
            m_BuildInstanceDrawsCommandBuffer = null;

            m_BuildOcclusionDepthCommandBuffer.Dispose();
            m_BuildOcclusionDepthCommandBuffer = null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_DebugOcclusionTestOverlayCommandBuffer.Dispose();
            m_DebugOcclusionTestOverlayCommandBuffer = null;


            m_DebugOcclusionDepthCommandBuffer.Dispose();
            m_DebugOcclusionDepthCommandBuffer = null;
#endif
        }

        void RemoveAllCommandBuffers()
        {
            if (m_Camera && m_BuildInstanceDrawsRegistered)
            {
                m_Camera.RemoveCommandBuffer(m_BuildInstanceDrawsEvent, m_BuildInstanceDrawsCommandBuffer);
                m_BuildInstanceDrawsRegistered = false;
            }

            if (m_Camera && m_BuildOcclusionDepthRegistered)
            {
                m_Camera.RemoveCommandBuffer(m_BuildOcclusionDepthPassEvent, m_BuildOcclusionDepthCommandBuffer);
                m_BuildOcclusionDepthRegistered = false;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_Camera && m_DebugOcclusionTestOverlayRegistered)
            {
                m_Camera.RemoveCommandBuffer(k_DebugOccludedOverlayEvent, m_DebugOcclusionTestOverlayCommandBuffer);
                m_DebugOcclusionTestOverlayRegistered = false;
            }

            if (m_Camera && m_DebugOcclusionDepthRegistered)
            {
                m_Camera.RemoveCommandBuffer(k_DebugOcclusionDepthPassEvent, m_DebugOcclusionDepthCommandBuffer);
                m_DebugOcclusionDepthRegistered = false;
            }
#endif
        }

        void UpdateCommandBufferEvents()
        {
            m_RenderingPath = m_Camera.actualRenderingPath;
            m_DepthTextureMode = m_Camera.depthTextureMode;

            m_BuildInstanceDrawsEvent = m_RenderingPath == RenderingPath.DeferredShading
                ? CameraEvent.BeforeGBuffer
                : CameraEvent.BeforeDepthTexture;

            m_BuildOcclusionDepthPassEvent = m_RenderingPath == RenderingPath.DeferredShading
                ? CameraEvent.AfterGBuffer
                : m_DepthTextureMode == DepthTextureMode.DepthNormals ? CameraEvent.AfterDepthNormalsTexture : CameraEvent.AfterDepthTexture;
        }

        public void OnPreRender()
        {
            RenderingPath cameraRenderingPath = m_Camera.actualRenderingPath;
            DepthTextureMode cameraDepthTextureMode = m_Camera.depthTextureMode;
            if (m_RenderingPath != cameraRenderingPath || m_DepthTextureMode != cameraDepthTextureMode)
            {
                RemoveAllCommandBuffers();
                UpdateCommandBufferEvents();
            }

            bool isOcclusionEnabled = m_Context.HasOcclusionManager() && m_Context.CameraManager.Data.Culling[m_CameraID].Flags.HasGPUOcclusionCulling();
            UpdateBuildInstanceDrawCommands(isOcclusionEnabled);
            UpdateOcclusionDepthPass(isOcclusionEnabled);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool isDebugDisplayActive = DebugDisplayData.IsActive();
            bool occlusionOverlayEnabled = isDebugDisplayActive && DebugDisplayData.Instance.OcclusionOverlayEnabled;
            if (!occlusionOverlayEnabled && m_DebugOcclusionTestOverlayRegistered)
            {
                m_Camera.RemoveCommandBuffer(k_DebugOcclusionDepthPassEvent, m_DebugOcclusionTestOverlayCommandBuffer);
                m_DebugOcclusionTestOverlayRegistered = false;
            }

            bool occlusionDepthOverlayEnabled = isDebugDisplayActive && DebugDisplayData.Instance.OccluderDepthOverlayEnabled;
            if (!occlusionDepthOverlayEnabled && m_DebugOcclusionDepthRegistered)
            {
                m_Camera.RemoveCommandBuffer(k_DebugOccludedOverlayEvent, m_DebugOcclusionDepthCommandBuffer);
                m_DebugOcclusionDepthRegistered = false;
            }

            if (isDebugDisplayActive)
            {
                if (occlusionOverlayEnabled && !m_DebugOcclusionTestOverlayRegistered)
                {
                    m_Camera.AddCommandBuffer(k_DebugOcclusionDepthPassEvent, m_DebugOcclusionTestOverlayCommandBuffer);
                    m_DebugOcclusionTestOverlayRegistered = true;
                }

                if (occlusionOverlayEnabled)
                    UpdateDebugOcclusionOverlayCommands();

                if (occlusionDepthOverlayEnabled && !m_DebugOcclusionDepthRegistered)
                {
                    m_Camera.AddCommandBuffer(k_DebugOccludedOverlayEvent, m_DebugOcclusionDepthCommandBuffer);
                    m_DebugOcclusionDepthRegistered = true;
                }

                if (occlusionDepthOverlayEnabled)
                    UpdateDebugOccluderDepthCommands();
            }
#endif
        }

        void UpdateBuildInstanceDrawCommands(bool occlusionEnabled)
        {
            if (!m_BuildInstanceDrawsRegistered)
            {
                m_Camera.AddCommandBuffer(m_BuildInstanceDrawsEvent, m_BuildInstanceDrawsCommandBuffer);
                m_BuildInstanceDrawsRegistered = true;
            }

            m_BuildInstanceDrawsCommandBuffer.Clear();

            InstanceCuller instanceCuller = m_Context.CameraManager.CullingContexts[m_CameraID];
            if (occlusionEnabled)
            {
                bool isSinglePassXR = m_Camera.stereoEnabled && m_Camera.stereoTargetEye == StereoTargetEyeMask.Both;
                int subviewCount = isSinglePassXR ? 2 : 1;
                OcclusionCullingSettings settings = new OcclusionCullingSettings(m_Camera.GetInstanceID(), OcclusionTest.TestAll)
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

                instanceCuller.BuildInstanceDraws(m_BuildInstanceDrawsCommandBuffer, settings, subviewOcclusionTests);
            }
            else
            {
                instanceCuller.BuildInstanceDraws(m_BuildInstanceDrawsCommandBuffer, default, default);
            }
        }

        void UpdateOcclusionDepthPass(bool isOcclusionEnabled)
        {
            if (!isOcclusionEnabled)
            {
                if (m_BuildOcclusionDepthRegistered)
                {
                    m_Camera.RemoveCommandBuffer(m_BuildOcclusionDepthPassEvent, m_BuildOcclusionDepthCommandBuffer);
                    m_BuildOcclusionDepthRegistered = false;
                }
                return;
            }

            if (!m_BuildOcclusionDepthRegistered)
            {
                m_Camera.AddCommandBuffer(m_BuildOcclusionDepthPassEvent, m_BuildOcclusionDepthCommandBuffer);
                m_BuildOcclusionDepthRegistered = true;
            }

            m_BuildOcclusionDepthCommandBuffer.Clear();

            int scaledWidth = m_Camera.scaledPixelWidth;
            int scaledHeight = m_Camera.scaledPixelHeight;
            bool isSinglePassXR = m_Camera.stereoEnabled && m_Camera.stereoTargetEye == StereoTargetEyeMask.Both;
            RTHandle depthTexture = m_DepthTextureMode == DepthTextureMode.DepthNormals ? k_DepthNormals : k_Depth;

            OccluderParameters occluderParams = new OccluderParameters(m_Camera.GetInstanceID())
            {
                SubviewCount = isSinglePassXR ? 2 : 1,
                DepthTextureRT = depthTexture,
                DepthSize = new Vector2Int(scaledWidth, scaledHeight),
                DepthIsArray = isSinglePassXR,
            };

            Span<OccluderSubviewUpdate> occluderSubviewUpdates = stackalloc OccluderSubviewUpdate[occluderParams.SubviewCount];
            if (isSinglePassXR)
            {
                for (int subviewIndex = 0; subviewIndex < occluderParams.SubviewCount; ++subviewIndex)
                {
                    Matrix4x4 viewMatrix = m_Camera.GetStereoViewMatrix((Camera.StereoscopicEye)subviewIndex);
                    Matrix4x4 projMatrix = m_Camera.GetStereoProjectionMatrix((Camera.StereoscopicEye)subviewIndex);
                    occluderSubviewUpdates[subviewIndex] = new OccluderSubviewUpdate(subviewIndex)
                    {
                        DepthSliceIndex = subviewIndex,
                        ViewMatrix = viewMatrix,
                        InvViewMatrix = viewMatrix.inverse,
                        GPUProjMatrix = GL.GetGPUProjectionMatrix(projMatrix, true),
                        ViewOffsetWorldSpace = Vector3.zero,
                    };
                }
            }
            else
            {
                occluderSubviewUpdates[0] = new OccluderSubviewUpdate(0)
                {
                    DepthSliceIndex = 0,
                    ViewMatrix = m_Camera.worldToCameraMatrix,
                    InvViewMatrix = m_Camera.worldToCameraMatrix.inverse,
                    GPUProjMatrix = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, true),
                    ViewOffsetWorldSpace = Vector3.zero,
                };
            }

            OcclusionManager occlusionManager = InstancingSystem.Instance.Context.GetOcclusionManager();
            occlusionManager.UpdateInstanceOccluders(m_BuildOcclusionDepthCommandBuffer, occluderParams, occluderSubviewUpdates);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void UpdateDebugOccluderDepthCommands()
        {
            m_DebugOcclusionDepthCommandBuffer.Clear();

            int screenWidth = m_Camera.scaledPixelWidth;
            float screenHeight = m_Camera.scaledPixelHeight;
            float maxHeight = screenHeight * 0.5f;
            OcclusionManager occlusionManager = m_Context.GetOcclusionManager();
            occlusionManager.RenderDebugOccluderOverlay(m_DebugOcclusionDepthCommandBuffer, DebugDisplayData.Instance, m_Camera.GetInstanceID(),
                new Vector2(0.25f * screenWidth, screenHeight - 1.5f * maxHeight), maxHeight);
        }

        void UpdateDebugOcclusionOverlayCommands()
        {
            m_DebugOcclusionTestOverlayCommandBuffer.Clear();

            OcclusionManager occlusionManager = m_Context.GetOcclusionManager();
            occlusionManager.RenderDebugOcclusionTestOverlay(m_DebugOcclusionTestOverlayCommandBuffer, DebugDisplayData.Instance, m_Camera.GetInstanceID());
        }
#endif
    }
}
