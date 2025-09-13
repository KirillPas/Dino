// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Runtime.CompilerServices;
using MA.Mathematics;
using MA.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

#if HAS_PACKAGE_UNITY_URP_12_0_0
using UnityEngine.Rendering.Universal;
#endif

#if HAS_PACKAGE_UNITY_HDRP_12_0_0
using UnityEngine.Rendering.HighDefinition;
#endif

namespace MA.Flora.Rendering
{
    static class RenderUtility
    {
        public static Light FindMainLight()
        {
            Light mainLight = null;

            foreach (Light light in UnityUtility.FindObjectsByType<Light>())
            {
                if (!light.enabled || light.type != LightType.Directional)
                    continue;

                if (light.shadows == LightShadows.None)
                    continue;

                if (mainLight == null)
                    mainLight = light;
                else if (light.intensity > mainLight.intensity)
                    mainLight = light;
            }

            return mainLight;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSceneCullingMaskFromCamera(Camera camera)
        {
#if UNITY_EDITOR
            if (camera.overrideSceneCullingMask != 0)
                return camera.overrideSceneCullingMask;

            if (camera.scene.IsValid())
                return UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(camera.scene);

            switch (camera.cameraType)
            {
                case CameraType.SceneView:
                    return UnityEditor.SceneManagement.SceneCullingMasks.MainStageSceneViewObjects;
                default:
                    return UnityEditor.SceneManagement.SceneCullingMasks.GameViewObjects;
            }
#else
            return 0;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetMaximumShadowDistance(Camera camera)
        {
            float shadowDistance = RenderPipelineManager.currentPipeline switch
            {
#if HAS_PACKAGE_UNITY_URP_12_0_0
                UniversalRenderPipeline => UniversalRenderPipeline.asset.shadowDistance,
#endif
#if HAS_PACKAGE_UNITY_HDRP_12_0_0
                HDRenderPipeline        => HDCamera.GetOrCreate(camera).volumeStack.GetComponent<HDShadowSettings>().maxShadowDistance.value,
#endif
                _ => QualitySettings.shadowDistance
            };
            return shadowDistance;
        }

        public enum StaticLightingMode
        {
            None = 0,
            LightMapped = 1,
            LightProbes = 2,
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StaticLightingMode StaticLightingModeFromRenderer(Renderer renderer)
        {
            StaticLightingMode staticLightingMode = StaticLightingMode.None;
            switch (renderer.lightmapIndex)
            {
                case >= 65534:
                case < 0:
                    staticLightingMode = StaticLightingMode.LightProbes;
                    break;
                case >= 0:
                    staticLightingMode = StaticLightingMode.LightMapped;
                    break;
            }

            return staticLightingMode;
        }

        public static int MaxConstantBufferSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if UNITY_2022_2_OR_NEWER
                return SystemInfo.maxConstantBufferSize;
#else
                switch (Application.platform)
                {
                    case RuntimePlatform.Android when SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan:
                        return 16 * 1024;
                    default:
                        return 64 * 1024;
                }
#endif
            }
        }

        public static int NumFramesInFlight
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // The number of frames in flight at the same time
                // depends on the Graphics device that we are using.
                // This number tells how long we need to keep the buffers
                // for a given frame alive. For example, if this is 4,
                // we can reclaim the buffers for a frame after 4 frames have passed.
                int numFrames = 0;

                switch (SystemInfo.graphicsDeviceType)
                {
                    case GraphicsDeviceType.Vulkan:
                    case GraphicsDeviceType.Direct3D11:
                    case GraphicsDeviceType.Direct3D12:
                    case GraphicsDeviceType.PlayStation4:
                    case GraphicsDeviceType.PlayStation5:
                    case GraphicsDeviceType.XboxOne:
                    case GraphicsDeviceType.GameCoreXboxOne:
                    case GraphicsDeviceType.GameCoreXboxSeries:
                    case GraphicsDeviceType.OpenGLCore:
#if !UNITY_2023_1_OR_NEWER
                    // OpenGL ES 2.0 is no longer supported in Unity 2023.1 and later
                    case GraphicsDeviceType.OpenGLES2:
#endif
                    case GraphicsDeviceType.OpenGLES3:
                    case GraphicsDeviceType.PlayStation5NGGC:
                        numFrames = 3;
                        break;
                    case GraphicsDeviceType.Switch:
                    case GraphicsDeviceType.Metal:
                    default:
                        numFrames = 4;
                        break;
                }

                // Use at least as many frames as the quality settings have, but use a platform
                // specific lower limit in any case.
                numFrames = math.max(numFrames, QualitySettings.maxQueuedFrames);

                return numFrames;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4x4 ComputeDirectionalLightViewMatrix(in float3 direction, in float3 up, in float3 position = default)
        {
            float3x3 rot = float3x3.LookRotationSafe(direction, up);
            float4x4 m = new float4x4
            {
                c0 = new float4(rot.c0, 0),
                c1 = new float4(rot.c1, 0),
                c2 = new float4(rot.c2, 0),
                c3 = new float4(position, 1),
            };

            return RigidTransformInverse(m);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4x4 RigidTransformInverse(in float4x4 m)
        {
            // The inverse of a rigid transform can be computed from the transpose
            float3x3 rotation = math.transpose(m.UpperLeft());
            // Rotate the translation
            float3 translation = math.mul(rotation, m[3].xyz);

            return new float4x4
            {
                c0 = new float4(rotation.c0, 0),
                c1 = new float4(rotation.c1, 0),
                c2 = new float4(rotation.c2, 0),
                c3 = new float4(-translation, 1),
            };
        }

        public static void CalculateBillboardProperties(
            in float4x4 worldToCameraMatrix,
            out float3 billboardTangent,
            out float3 billboardNormal,
            out float cameraXZAngle)
        {
            float4x4 cameraToWorldMatrix = math.transpose(worldToCameraMatrix);
            float3 cameraToWorldMatrixAxisX = cameraToWorldMatrix.c0.xyz;
            float3 cameraToWorldMatrixAxisY = cameraToWorldMatrix.c1.xyz;
            float3 cameraToWorldMatrixAxisZ = cameraToWorldMatrix.c2.xyz;

            float3 front = cameraToWorldMatrixAxisZ;
            float3 worldUp = math.up();
            float3 cross = math.cross(front, worldUp);
            billboardTangent = !MathUtility.NearlyEquals(math.lengthsq(cross), 0.0f)
                ? math.normalize(cross)
                : cameraToWorldMatrixAxisX;

            billboardNormal = math.cross(worldUp, billboardTangent);
            billboardNormal = !MathUtility.NearlyEquals(math.lengthsq(billboardNormal), 0.0f)
                ? math.normalize(billboardNormal)
                : cameraToWorldMatrixAxisY;

            // SpeedTree generates billboards starting from looking towards X- and rotates counter clock-wisely
            float3 worldRight = new float3(0, 0, 1);
            // signed angle is calculated on X-Z plane
            float s = worldRight.x * billboardTangent.z - worldRight.z * billboardTangent.x;
            float c = worldRight.x * billboardTangent.x + worldRight.z * billboardTangent.z;
            cameraXZAngle = math.atan2(s, c);

            // convert to [0,2PI)
            if (cameraXZAngle < 0)
                cameraXZAngle += 2 * math.PI;
        }
    }
}
