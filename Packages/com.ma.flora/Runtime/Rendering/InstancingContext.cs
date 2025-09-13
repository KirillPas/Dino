// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Flora.Rendering.Occlusion;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora.Rendering
{
    [BurstCompile]
    sealed class InstancingContext : IDisposable
    {
        public float Time;
        public float PreviousTime;
        public float DeltaTime;
        public int FrameCount;

        public bool IsEditor;
        public bool IsPlaying;

        public Light MainLight;
        public int MainLightInstanceID;
        public Transform MainLightTransform;
        public float3x3 MainLightRotation;

        public InstancedCameraManager CameraManager;
        public InstancedSceneData SceneData;
        public InstancedBatchManager BatchManager;
        public InstancedPrototypeManager PrototypeManager;
        public InstancedRendererManager RendererManager;
        public InstancedMeshManager MeshManager;
        public InstancedMaterialManager MaterialManager;
        public ThreadLocalAllocator FrameAllocator;

        int m_MainLightHash;
        StaticOcclusionManager m_StaticOcclusionManager;
        OcclusionManager m_OcclusionManager;

        public InstancingContext()
        {
            Time = 0.0f;
            PreviousTime = 0.0f;
            DeltaTime = 0.0f;
            FrameCount = 0;

            MainLight = null;
            MainLightTransform = null;
            MainLightRotation = float3x3.identity;

            SceneData = new InstancedSceneData(this);
            MeshManager = new InstancedMeshManager(64);
            MaterialManager = new InstancedMaterialManager(64);
            BatchManager = new InstancedBatchManager(64);
            PrototypeManager = new InstancedPrototypeManager(this, 64);
            RendererManager = new InstancedRendererManager(this, 64);
            CameraManager = new InstancedCameraManager(this, 4);

            FrameAllocator = new ThreadLocalAllocator(-1);

            DebugDisplayData.Instance.Register();
        }

        public void Dispose()
        {
            DebugDisplayData.Instance.Unregister();

            SceneData.Dispose();
            MeshManager.Dispose();
            MaterialManager.Dispose();
            BatchManager.Dispose();
            PrototypeManager.Dispose();
            CameraManager.Dispose();
            RendererManager.Dispose();

            DestroyOcclusionManager();
            DestroyStaticOcclusionManager();

            FrameAllocator.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OcclusionManager GetOcclusionManager()
            => m_OcclusionManager;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OcclusionManager EnsureOcclusionManager()
            => m_OcclusionManager ??= new OcclusionManager();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasOcclusionManager()
            => m_OcclusionManager != null;

        public void DestroyOcclusionManager()
        {
            m_OcclusionManager?.Dispose();
            m_OcclusionManager = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StaticOcclusionManager GetStaticOcclusionManager()
            => m_StaticOcclusionManager;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StaticOcclusionManager EnsureStaticOcclusionManager()
            => m_StaticOcclusionManager ??= new StaticOcclusionManager(this);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasStaticOcclusionManager()
            => m_StaticOcclusionManager != null;

        public void DestroyStaticOcclusionManager()
        {
            m_StaticOcclusionManager?.Dispose();
            m_StaticOcclusionManager = null;
        }

        public void UpdateFrame()
        {
            const float kMaximumDeltaTime = 1.0f / 3.0f;
            float newTime, deltaTime;
#if UNITY_EDITOR
            newTime = Application.isPlaying ? UnityEngine.Time.unscaledTime : UnityEngine.Time.realtimeSinceStartup;
            deltaTime = Application.isPlaying ? UnityEngine.Time.unscaledDeltaTime : kMaximumDeltaTime;
#else
            newTime = UnityEngine.Time.unscaledTime;
            deltaTime = UnityEngine.Time.unscaledDeltaTime;
#endif
            deltaTime = math.min(deltaTime, kMaximumDeltaTime);

            PreviousTime = Time;
            Time = newTime;
            DeltaTime = deltaTime;
            FrameCount++;

            IsPlaying = true;
#if UNITY_EDITOR
            IsEditor = !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
            IsPlaying = !UnityEditor.EditorApplication.isPaused;
#endif

            DebugDisplayData.Instance.Update();
            InstanceCuller.ProfilingCounters.NextFrame();
            UpdateMainLight();

            RewindAllocator(ref FrameAllocator);
            CameraManager.NextFrame();
            RendererManager.NextFrame();
            m_StaticOcclusionManager?.NextFrame();
            m_OcclusionManager?.NextFrame();
        }

        public void NextRender()
        {
            SceneData.NextRenderFrame();
            CameraManager.NextRender();
        }

        void UpdateMainLight()
        {
            InstancingSceneSettings sceneSettings = InstancingSceneSettings.Global;
            bool hasMainLight = MainLightInstanceID != 0;
            switch (sceneSettings.MainLightMode)
            {
                case InstancingMainLightMode.Manual:
                    MainLight = sceneSettings.MainLightOverride;
                    if (MainLight)
                    {
                        MainLightTransform = MainLight.transform;
                        MainLightInstanceID = MainLight.GetInstanceID();
                    }
                    else
                    {
                        MainLightTransform = null;
                        MainLightInstanceID = 0;
                    }
                    break;
                default:
                {
                    if (!hasMainLight)
                    {
                        TryFindBestMainLight();
                    }
                    else
                    {
                        int newHash = ComputeMainLightHash();
                        if (newHash != m_MainLightHash)
                            TryFindBestMainLight();
                    }
                    break;
                }
            }

            if (MainLightInstanceID != 0)
                MainLightRotation = new float3x3(MainLightTransform!.localToWorldMatrix);
            else
                MainLightRotation = float3x3.identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void TryFindBestMainLight()
        {
            MainLight = RenderUtility.FindMainLight();
            if (MainLight)
            {
                MainLightInstanceID = MainLight.GetInstanceID();
                MainLightTransform = MainLight.transform;
                m_MainLightHash = ComputeMainLightHash();
            }
            else
            {
                MainLightInstanceID = 0;
                MainLightTransform = null;
                m_MainLightHash = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int ComputeMainLightHash()
        {
            unchecked
            {
                int hash = MainLightInstanceID;
                if (MainLight)
                {
                    hash = hash * 31 + MainLight.enabled.GetHashCode();
                    hash = hash * 31 + MainLight.intensity.GetHashCode();
                    hash = hash * 31 + MainLight.shadows.GetHashCode();
                    hash = hash * 31 + MainLightTransform.rotation.GetHashCode();
                }
                return hash;
            }
        }

        [BurstCompile]
        static void RewindAllocator(ref ThreadLocalAllocator allocator)
        {
            allocator.Rewind();
        }
    }
}
