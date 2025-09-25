// Copyright © Magnetic Arcade. All Rights Reserved.

#if HAS_PACKAGE_UNITY_URP_12_0_0
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MA.Flora.Rendering.Universal.InternalBridge
{
    static class URPBridge
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsSinglePassXREnabled(this ref CameraData cameraData)
            => cameraData.xr.enabled && cameraData.xr.singlePassEnabled;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetScaledCameraWidth(this ref CameraData cameraData)
            => (int)(cameraData.camera.pixelWidth * cameraData.renderScale);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetScaledCameraHeight(this ref CameraData cameraData)
            => (int)(cameraData.camera.pixelWidth * cameraData.renderScale);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasDepthPriming(this ScriptableRenderer renderer)
            => renderer.useDepthPriming;

#if UNITY_2022_2_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static CommandBuffer GetCommandBuffer(this ref RenderingData renderingData) => renderingData.commandBuffer;
#endif

        static Dictionary<RenderTargetIdentifier, RTHandle> s_LegacyTargetCache = new Dictionary<RenderTargetIdentifier, RTHandle>();

#if UNITY_2023_3_OR_NEWER
        [System.Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static RTHandle GetDepthTarget(this ScriptableRenderer renderer)
        {
#if UNITY_2022_3_OR_NEWER
            if (renderer is UniversalRenderer universalRenderer)
            {
                return universalRenderer.m_DepthTexture;
            }
            else
            {
                return renderer.cameraDepthTargetHandle;
            }
#else
            if (!s_LegacyTargetCache.TryGetValue(BuiltinRenderTextureType.CameraTarget, out RTHandle depthTarget))
            {
                depthTarget = RTHandles.Alloc(renderer.cameraDepthTarget);
                s_LegacyTargetCache[BuiltinRenderTextureType.CameraTarget] = depthTarget;
            }

            return depthTarget;
#endif
        }

#if UNITY_2023_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsSinglePassXREnabled(this UniversalCameraData cameraData)
            => cameraData.xr.enabled && cameraData.xr.singlePassEnabled;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetScaledCameraWidth(this UniversalCameraData cameraData)
            => (int)(cameraData.camera.pixelWidth * cameraData.renderScale);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetScaledCameraHeight(this UniversalCameraData cameraData)
            => (int)(cameraData.camera.pixelWidth * cameraData.renderScale);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ContextContainer GetFrameData(this ref RenderingData renderingData)
            => renderingData.frameData;
#endif
    }
}
#endif
