// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections.Unsafe;
using MA.Core;
using MA.Flora.Rendering.Builtin;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

#if HAS_PACKAGE_UNITY_URP_12_0_0
using MA.Flora.Rendering.Universal;
#endif

#if HAS_PACKAGE_UNITY_HDRP_12_0_0
using MA.Flora.Rendering.HDRP;
#endif

#if UNITY_EDITOR
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace MA.Flora.Rendering
{
    enum InstanceRenderPipelineType
    {
        Unknown,
        Builtin,
        Universal,
        HighDefinition,
        Custom,
    }

    sealed class InstancingSystem
    {
        // --- Singleton ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsActive() => s_Instance is not null && s_Instance.m_IsCreated;

        static InstancingSystem s_Instance;
        public static InstancingSystem Instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (s_Instance is { m_IsCreated: false })
                    s_Instance = null;

                return s_Instance;
            }
        }

        // --- Pipeline Events ---

        public static event Action<InstanceRenderPipelineType, InstanceRenderPipelineType> RenderPipelineTypeChanged;

        public InstanceRenderPipelineType ActiveRenderPipelineType { get; private set; } = InstanceRenderPipelineType.Unknown;

        public bool IsScriptableRenderPipelineActive => ActiveRenderPipelineType > InstanceRenderPipelineType.Builtin;

        // --- Group Management ---

        public static void EnsureActive()
        {
            if (!IsActive())
                s_Instance = new InstancingSystem();
        }

        public static void Shutdown()
        {
            s_Instance?.Dispose();
            s_Instance = null;
        }

        public static void ShutdownIfEmpty()
        {
            if (IsActive() && s_Instance.m_Context.RendererManager.Count == 0)
            {
                s_Instance.Dispose();
                s_Instance = null;
            }
        }

        public static InstancedPrototypeID GetPrototypeID(InstancedPrototype prototype)
        {
            return !IsActive() ? InstancedPrototypeID.Null : s_Instance.m_Context.PrototypeManager.Get(prototype);
        }

        public static InstancedPrototype GetPrototype(InstancedPrototypeID id)
        {
            return !IsActive() ? null : s_Instance.m_Context.PrototypeManager.GetPrototype(id);
        }

        public static InstancedPrototypeID RegisterPrototype(InstancedPrototype prototype)
        {
            if (!IsActive())
                s_Instance = new InstancingSystem();

            return s_Instance.m_Context.PrototypeManager.Register(prototype);
        }

        public static void UnregisterPrototype(InstancedPrototypeID id)
        {
            if (!IsActive() || id == InstancedPrototypeID.Null)
                return;

            s_Instance.m_Context.PrototypeManager.Unregister(id);
        }

        public static InstancedRendererID RegisterRenderer(IInstancedRenderer renderer)
        {
            if (!IsActive())
                s_Instance = new InstancingSystem();

            return s_Instance.m_Context.RendererManager.Register(renderer);
        }

        public static void UnregisterRenderer(InstancedRendererID id)
        {
            if (!IsActive() || id == InstancedRendererID.Null)
                return;

            InstancedRendererManager rendererManager = s_Instance.m_Context.RendererManager;
            rendererManager.Unregister(id);
            if (rendererManager.Count == 0)
                s_Instance.Dispose();
        }

        public static void MarkRendererDirty(InstancedRendererID id)
        {
            if (!IsActive() || id == InstancedRendererID.Null)
                return;

            s_Instance.m_Context.RendererManager.SetRendererDirty(id);
        }

        public static void ForceRebuildCullingTree(InstancedRendererID id)
        {
            if (!IsActive() || id == InstancedRendererID.Null)
                return;

            InstancedRendererManager rendererManager = s_Instance.m_Context.RendererManager;
            if (rendererManager.Exists(id))
            {
                rendererManager.Renderers[id].CullingData.TryBuildTree(true, true);
                rendererManager.AddTreeBuild(id);
            }
        }

        // --- Rendering ---

        internal static void UpdateMaterialCache(Material material)
        {
            if (!IsActive())
                return;

            s_Instance.m_Context.MaterialManager.UpdateInstanceProperties(material);
        }

        public bool Enabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_IsCreated && m_Enabled;
            set
            {
                if (m_Enabled != value)
                {
                    m_Enabled = value;
                    if (m_Enabled)
                        Setup();
                    else
                        Teardown();
                }
            }
        }
        bool m_Enabled;

        internal static Action PreFrameUpdate;
        internal static Action PostFrameUpdate;

        internal static bool DisableAutoBuildTrees { get; set; } = false;
        internal static bool DisableDensityCulling { get; set; } = false;
        internal static bool DisableRenderDistance { get; set; } = false;

        internal static bool UseGPUDrivenOcclusion()
        {
#if UNITY_2023_3_OR_NEWER && FLORA_ENABLE_EXPERIMENTAL_GPU_DRIVEN_OCCLUSION_INTEGRATION
            return GPUResidentDrawer.IsInstanceOcclusionCullingEnabled();
#else
            return false;
#endif
        }

        internal InstancingContext Context => m_Context;

        // --- Fields ---

        InstancingContext m_Context;
        JobHandle m_NonRenderJobsHandle;

        bool m_IsCreated;
        bool m_CallbacksRegistered;

#if UNITY_EDITOR
        PrefabStage m_PrefabStage;
        UnsafeIndirectList<int> m_FrameCameraIDs;
        bool m_FrameUpdateNeeded;
#endif

#if HAS_PACKAGE_UNITY_URP_12_0_0
        UniversalInstancingSystem m_UniversalInstancingSystem;
#endif

#if HAS_PACKAGE_UNITY_HDRP_12_0_0
        HDRPInstancingSystem m_HDRPInstancingSystem;
#endif

        internal static class Profiling
        {
            public static readonly ProfilerMarker InstancingSystem = new ProfilerMarker("Flora.InstancingSystem");
            public static readonly ProfilerMarker Initialization = new ProfilerMarker("InstancingSystem.Initialization");
            public static readonly ProfilerMarker Schedule = new ProfilerMarker("InstancingSystem.Schedule");
            public static readonly ProfilerMarker Render = new ProfilerMarker("InstancingSystem.Render");
            public static readonly ProfilerMarker Camera = new ProfilerMarker("InstancingSystem.Camera");
        }

        // --- Lifecycle ---

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void CleanupSystemBeforeSceneLoad()
        {
            // Calls teardown when entering Play Mode without Domain Reload.
            // RuntimeInitializeOnLoadMethod is called before the new scene is loaded, before Awake and OnEnable of MonoBehaviour.
            s_Instance?.Dispose();
            s_Instance = null;
        }

        InstancingSystem()
        {
            BufferUtility.Initialize();

            if (!IsSupportedOnSystem(out string failReason))
            {
                Debug.LogError($"Flora is not supported on this device: {failReason}");
                return;
            }

            m_Enabled = true;
            m_Context = new InstancingContext();

            Setup();
            m_IsCreated = true;

#if UNITY_EDITOR
            m_PrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            m_FrameCameraIDs = new UnsafeIndirectList<int>(16, AllocatorManager.Persistent);
#endif
        }

        void Dispose()
        {
            if (m_IsCreated)
            {
                m_IsCreated = false;

                FoliageJobManager.CancelAll();
                Teardown();

                m_Context.Dispose();
                s_Instance = null;

#if UNITY_EDITOR
                m_FrameCameraIDs.Dispose();
#endif
            }
        }

        // --- PlayerLoop ---

        struct UpdateInitializationKey { }
        struct UpdatePostLateUpdateKey { }

        void Setup()
        {
            InstanceRenderPipelineType previousRenderPipelineType = ActiveRenderPipelineType;
            if (m_CallbacksRegistered)
                Teardown();

            m_CallbacksRegistered = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            RenderPipelineManager.activeRenderPipelineTypeChanged += OnActiveRenderPipelineTypeChanged;

#if HAS_PACKAGE_UNITY_URP_12_0_0
            m_UniversalInstancingSystem?.Dispose();
#endif

            if (GraphicsSettings.currentRenderPipeline)
            {
                switch (GraphicsSettings.currentRenderPipeline)
                {
#if HAS_PACKAGE_UNITY_URP_12_0_0
                    case UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset:
                        ActiveRenderPipelineType = InstanceRenderPipelineType.Universal;
                        m_UniversalInstancingSystem = new UniversalInstancingSystem(m_Context);
                        break;
#endif
#if HAS_PACKAGE_UNITY_HDRP_12_0_0
                    case UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset:
                        ActiveRenderPipelineType = InstanceRenderPipelineType.HighDefinition;
                        m_HDRPInstancingSystem = new HDRPInstancingSystem(m_Context);
                        break;
#endif
                    default:
                        ActiveRenderPipelineType = InstanceRenderPipelineType.Custom;
                        break;
                }

                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                RenderPipelineManager.endContextRendering += OnEndContextRendering;
            }
            else
            {
                m_BuiltinRenderState.Reset();
                ActiveRenderPipelineType = InstanceRenderPipelineType.Builtin;
                Camera.onPreCull += OnBuiltinCameraPreCull;
                Camera.onPreRender += OnBuiltinCameraPreRender;
                Camera.onPostRender += OnBuiltinCameraPostRender;
            }

#if UNITY_EDITOR
            m_FrameUpdateNeeded = true;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            AssemblyReloadEvents.beforeAssemblyReload += OnAssemblyReload;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplicationBridge.tick += OnEditorTick;
#endif
            PlayerLoopUtility.TryAddToPlayerLoop(UpdateInitialization, typeof(UpdateInitializationKey), typeof(Initialization), PlayerLoopUtility.AddMode.End);
            PlayerLoopUtility.TryAddToPlayerLoop(UpdatePostLateUpdate, typeof(UpdatePostLateUpdateKey), typeof(PostLateUpdate.UpdateAllRenderers), PlayerLoopUtility.AddMode.End);

            RenderPipelineTypeChanged?.Invoke(previousRenderPipelineType, ActiveRenderPipelineType);
        }

        void Teardown()
        {
            if (!m_CallbacksRegistered)
                return;

            m_CallbacksRegistered = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnActiveRenderPipelineTypeChanged;

            if (IsScriptableRenderPipelineActive)
            {
                RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                RenderPipelineManager.endContextRendering -= OnEndContextRendering;

#if HAS_PACKAGE_UNITY_URP_12_0_0
                m_UniversalInstancingSystem?.Dispose();
#endif
#if HAS_PACKAGE_UNITY_HDRP_12_0_0
                m_HDRPInstancingSystem?.Dispose();
#endif
            }
            else
            {
                Camera.onPreCull -= OnBuiltinCameraPreCull;
                Camera.onPreRender -= OnBuiltinCameraPreRender;
                Camera.onPostRender -= OnBuiltinCameraPostRender;
            }

            ActiveRenderPipelineType = InstanceRenderPipelineType.Unknown;

            PlayerLoopUtility.TryRemoveLoopSystem(typeof(UpdateInitializationKey));
            PlayerLoopUtility.TryRemoveLoopSystem(typeof(UpdatePostLateUpdateKey));

#if UNITY_EDITOR
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            AssemblyReloadEvents.beforeAssemblyReload -= OnAssemblyReload;
            EditorApplicationBridge.tick -= OnEditorTick;
#endif
        }

        // --- Events ---

#if UNITY_EDITOR
        static void OnAssemblyReload()
        {
            s_Instance?.Dispose();
            s_Instance = null;
        }

        void OnPrefabStageOpened(PrefabStage stage)
        {
            m_PrefabStage = stage;
        }

        void OnPrefabStageClosing(PrefabStage stage)
        {
            m_PrefabStage = null;
        }
#endif

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (m_IsCreated && mode == LoadSceneMode.Additive)
                m_Context.SceneData.UpdateAmbientProbe(true);
        }

        void OnActiveRenderPipelineTypeChanged()
        {
            if (m_CallbacksRegistered)
                Setup();
        }

        // --- Render Loop ---

#if UNITY_EDITOR
        void OnEditorTick()
        {
            if (!Enabled)
                return;

            m_FrameUpdateNeeded = true;
            UpdateInitialization();
            UpdatePostLateUpdate();
            m_FrameUpdateNeeded = false;
        }
#endif

        void UpdateInitialization()
        {
            if (!Enabled)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying && !m_FrameUpdateNeeded)
                return;
#endif

            using (Profiling.InstancingSystem.Auto())
            using (Profiling.Initialization.Auto())
            {
                m_NonRenderJobsHandle.Complete();
                m_Context.UpdateFrame();
                m_Context.SceneData.BeginUploads();

                m_NonRenderJobsHandle = new ClearLastFrameInRangeJob { GroupWasInRange = m_Context.RendererManager.Data.InRangeLastFrame }
                    .Schedule(m_NonRenderJobsHandle);
            }

            m_BuiltinRenderState.Reset();
#if UNITY_EDITOR
            m_FrameUpdateNeeded = false;
#endif
        }

        void UpdatePostLateUpdate()
        {
            if (!Enabled)
                return;

            PreFrameUpdate?.Invoke();

            using (Profiling.InstancingSystem.Auto())
            {
                ScheduleEarlyCullingJobs();
            }

            FoliageJobManager.Update();
            PostFrameUpdate?.Invoke();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ScheduleEarlyCullingJobs()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            using (Profiling.Schedule.Auto())
            {
                ref readonly InstancedCameraArrays cameraArrays = ref m_Context.CameraManager.Data;
                bool didSchedule = false;

                foreach (InstancedCameraID cameraID in m_Context.CameraManager.PrevRenderedCameraIDs)
                {
                    bool isPersistentCamera = cameraArrays.Culling[cameraID].Flags.IsPersistentCamera();
                    if (!isPersistentCamera)
                        continue;

                    if (!m_Context.CameraManager.TryUpdateInstancedCamera(cameraID))
                        continue;

                    InstanceCuller instanceCuller = m_Context.CameraManager.CullingContexts[cameraID];
                    instanceCuller.ScheduleCull(m_Context.RendererManager.Loaded);
                    m_NonRenderJobsHandle = ScheduleUpdateInRangeRenderers(cameraID, m_NonRenderJobsHandle);
                    didSchedule = true;
                }

                if (didSchedule)
                    JobHandle.ScheduleBatchedJobs();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CullCamera(Camera camera, InstancedCameraID cameraID)
        {
            UnsafeIndirectList<InstancedRendererID> groups = m_Context.RendererManager.Loaded;
            InstanceCuller instanceCuller = m_Context.CameraManager.CullingContexts[cameraID];

            if (!instanceCuller.HasScheduledCull)
            {
                ModifyRenderersForStage_EditorOnly(ref groups, camera);
                m_NonRenderJobsHandle = ScheduleUpdateInRangeRenderers(cameraID, m_NonRenderJobsHandle);
            }

            instanceCuller.SubmitIndirectRenderCommands(groups);
        }

        // --- SRP Rendering ---

        int m_SRPRenderCameraIndex;

        void OnBeginContextRendering(ScriptableRenderContext renderContext, List<Camera> cameras)
        {
            if (!Enabled)
                return;

            m_SRPRenderCameraIndex = 0;

            using (Profiling.InstancingSystem.Auto())
            using (Profiling.Render.Auto())
            {
#if UNITY_EDITOR
                m_FrameUpdateNeeded = true;
#endif
                m_Context.NextRender();

                for (int cameraIndex = 0; cameraIndex < cameras.Count; cameraIndex++)
                {
                    Camera camera = cameras[cameraIndex];
                    if (m_Context.CameraManager.TryGetInstancedCamera(camera, out InstancedCameraID cameraID))
                    {
                        CullCamera(camera, cameraID);
                    }
                }
            }
        }

        void OnBeginCameraRendering(ScriptableRenderContext renderContext, Camera camera)
        {
            if (!Enabled)
                return;

            if (m_SRPRenderCameraIndex == 0)
                m_Context.SceneData.EndUploads();

            if (m_Context.CameraManager.TryGetRenderingCameraID(camera, out InstancedCameraID cameraID))
            {
#if HAS_PACKAGE_UNITY_URP_12_0_0
                if (ActiveRenderPipelineType == InstanceRenderPipelineType.Universal)
                    m_UniversalInstancingSystem.UpdateCamera(camera, cameraID);
#endif
#if HAS_PACKAGE_UNITY_HDRP_12_0_0
                if (ActiveRenderPipelineType == InstanceRenderPipelineType.HighDefinition)
                    m_HDRPInstancingSystem.UpdateCamera(camera, cameraID);
#endif
            }
        }

        void OnEndContextRendering(ScriptableRenderContext scriptableRenderContext, List<Camera> cameras)
        {
            if (!Enabled)
                return;

            for (int cameraIndex = 0; cameraIndex < cameras.Count; cameraIndex++)
                RenderSelectionUtility.RenderSceneSelection(m_Context, cameras[cameraIndex]);

            m_Context.CameraManager.EndCameraRender();
        }

        // --- Builtin Rendering ---

        class BuiltinRenderState
        {
            public List<Camera> AllCameras = new List<Camera>();
            public HashSet<Camera> RenderedCameras = new HashSet<Camera>();
            public int RenderedCameraCount;
            public bool HasCalledNextRender;
            public bool HasCalledDispatchCamera;

            public void Reset()
            {
                RenderedCameras.Clear();
                RenderedCameraCount = 0;
                HasCalledNextRender = false;
                HasCalledDispatchCamera = false;
            }
        }

        BuiltinRenderState m_BuiltinRenderState = new BuiltinRenderState();

        void OnBuiltinCameraPreCull(Camera camera)
        {
            if (!Enabled)
                return;

            var allCamerasCount = Camera.allCamerasCount;
            if (allCamerasCount != m_BuiltinRenderState.AllCameras.Count)
            {
                m_BuiltinRenderState.AllCameras.Clear();
                m_BuiltinRenderState.AllCameras.AddRange(Camera.allCameras);
            }

            using (Profiling.InstancingSystem.Auto())
            using (Profiling.Render.Auto())
            {
                if (!m_BuiltinRenderState.HasCalledNextRender)
                {
#if UNITY_EDITOR
                    m_FrameUpdateNeeded = true;
#endif
                    m_Context.NextRender();
                    m_BuiltinRenderState.HasCalledNextRender = true;
                }

                if (m_Context.CameraManager.TryGetInstancedCamera(camera, out InstancedCameraID cameraID))
                {
                    CullCamera(camera, cameraID);
                }
            }
        }

        void OnBuiltinCameraPreRender(Camera camera)
        {
            if (!Enabled)
                return;

            using (Profiling.InstancingSystem.Auto())
            using (Profiling.Camera.Auto())
            {
                if (m_BuiltinRenderState is not { HasCalledNextRender: true, HasCalledDispatchCamera: false })
                {
                    m_BuiltinRenderState.HasCalledDispatchCamera = true;
                    m_Context.SceneData.EndUploads();
                }

                if (m_Context.CameraManager.TryGetRenderingCameraID(camera, out InstancedCameraID cameraID))
                {
                    BuiltinInstancingCameraRenderer builtinInstancingRenderer = m_Context.CameraManager.BuiltinRenderer[cameraID];
                    builtinInstancingRenderer.OnPreRender();
                }
            }
        }

        void OnBuiltinCameraPostRender(Camera camera)
        {
            if (!Enabled)
                return;

            RenderSelectionUtility.RenderSceneSelection(m_Context, camera);

            if (m_Context.CameraManager.TryGetRenderingCameraID(camera, out InstancedCameraID cameraID))
                m_Context.CameraManager.EndCameraRender(cameraID);

            if (m_BuiltinRenderState.RenderedCameras.Add(camera))
            {
                m_BuiltinRenderState.RenderedCameraCount++;
                if (m_BuiltinRenderState.RenderedCameraCount >= m_BuiltinRenderState.AllCameras.Count)
                {
                    m_BuiltinRenderState.Reset();
                }
            }
        }

        // --- Support Helpers ---

        static bool IsSupportedOnSystem(out string failReason)
        {
            failReason = string.Empty;

            if (!SystemInfo.supportsComputeShaders)
            {
                failReason = "Compute Shaders are not supported.";
                return false;
            }

            if (!SystemInfo.supportsInstancing)
            {
                failReason = "Instancing is not supported.";
                return false;
            }

            if (SystemInfo.graphicsShaderLevel < 45)
            {
                failReason = "Shader Prototype 4.5 support is required.";
                return false;
            }

#if UNITY_2022_2_OR_NEWER
            if (!SystemInfo.supportsIndirectArgumentsBuffer)
            {
                failReason = "Indirect Arguments Buffers are not supported.";
                return false;
            }
#endif

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore && !CheckGLVersion())
            {
                failReason = "The current graphics device is not supported.";
                return false;
            }

            return true;
        }

        static bool CheckGLVersion()
        {
            char[] delimiterChars = { ' ', '.'};
            string[] arr = SystemInfo.graphicsDeviceVersion.Split(delimiterChars);
            if (arr.Length >= 3)
            {
                int major = int.Parse(arr[1]);
                int minor = int.Parse(arr[2]);
                return major >= 4 && minor >= 3;
            }

            return false;
        }

        // --- Editor Only ---

        [Conditional("UNITY_EDITOR")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe void ModifyRenderersForStage_EditorOnly(ref UnsafeIndirectList<InstancedRendererID> groups, Camera camera)
        {
#if UNITY_EDITOR
            if (m_PrefabStage && camera.cameraType is CameraType.SceneView)
            {
                StageContextRenderMode contextRenderMode = StageUtilityBridge.GetContextRenderMode();
                if (contextRenderMode != StageContextRenderMode.Normal)
                {
                    UnsafeIndirectList<InstancedRendererID> loadedClusters = m_Context.RendererManager.Loaded;
                    groups = new UnsafeIndirectList<InstancedRendererID>(loadedClusters.Length, m_Context.FrameAllocator.GeneralAllocator->Handle);
                    for (int i = 0; i < loadedClusters.Length; i++)
                    {
                        InstancedRendererID id = loadedClusters[i];
                        GameObject gameObject = m_Context.RendererManager.GameObjects[id];

                        bool shouldRenderInStage = true;
                        bool isGameObjectInPrefabScene = m_PrefabStage.scene.handle == 0 || gameObject.scene == m_PrefabStage.scene;

                        switch (contextRenderMode)
                        {
                            case StageContextRenderMode.GreyedOut:
                                if (camera.IsSceneCameraFiltered())
                                    shouldRenderInStage = isGameObjectInPrefabScene;
                                break;
                            case StageContextRenderMode.Hidden:
                                shouldRenderInStage = isGameObjectInPrefabScene;
                                break;
                        }

                        if (shouldRenderInStage)
                            groups.Add(id);
                    }
                }
            }
#endif
        }

        // --- Jobs ---

        [BurstCompile]
        struct ClearLastFrameInRangeJob : IJob
        {
            public UnsafeBitList GroupWasInRange;

            public void Execute()
            {
                GroupWasInRange.SetAll(false);
            }
        }

        [BurstCompile]
        unsafe struct UpdateRendererStreamingJob : IJobParallelForBatchLegacyCompatible
        {
            public const int BatchSize = 128;
            public const int OutOfRangeFrameCount = 120;

            public int FrameCount;
            public bool DisableCullDistance;

            public InstancedCameraID CameraID;
            [ReadOnly] public InstancedCameraArrays.ReadOnly CameraArrays;
            [ReadOnly] public InstancedPrototypeDataArrays.ReadOnly PrototypeArrays;

            [ReadOnly] public UnsafeIndirectList<InstancedRendererID> LoadedRenderers;
            [ReadOnly] public UnsafeArray<InstanceRendererData>.ReadOnly RendererData;
            [ReadOnly] public UnsafeArray<AxisAlignedBox>.ReadOnly Bounds;
            [ReadOnly] public UnsafeBitList IsLoaded;

            public UnsafeArray<int> LastFrameInRange;
            public UnsafeBitList InRange;
            public UnsafeBitList InRangeThisFrame;

            [WriteOnly] public UnsafeIndirectList<InstancedRendererID>.ParallelWriter ToLoad;
            [WriteOnly] public UnsafeIndirectList<InstancedRendererID>.ParallelWriter ToUnload;

            public void Execute(int startIndex, int count)
            {
                InstancedCameraCullingData cameraCullingData = CameraArrays.Culling[CameraID];
                InstancedCameraLODData cameraLODData = CameraArrays.LOD[CameraID];

                for (int i = 0; i < count; i++)
                {
                    InstancedRendererID id = LoadedRenderers[startIndex + i];
                    if (!InRangeThisFrame.IsValidIndex(id))
                        continue;

                    if (InRangeThisFrame[id])
                        continue; // Skip groups that are already calculated this frame

                    InstanceRendererData rendererData = RendererData[id];
                    if (!PrototypeArrays.Exists(rendererData.PrototypeID))
                        continue;

                    float streamDistance = cameraCullingData.FarClipPlane;
                    if (!DisableCullDistance)
                    {
                        if (rendererData.StreamingDistance > 0.0f)
                        {
                            streamDistance = math.min(rendererData.StreamingDistance, streamDistance);
                        }
                        else
                        {
                            InstancedPrototypeLODData prototypeLODData = PrototypeArrays.LOD[rendererData.PrototypeID];
                            float lastLODDistance = LODGroupUtility.CalculateLODDistance(prototypeLODData.LODHeight[prototypeLODData.LODCount - 1], rendererData.LODAverageWorldSpaceSize) * cameraLODData.ScreenRelativeMetric;
                            streamDistance = math.min(streamDistance, lastLODDistance);
                        }
                    }

                    float distanceSq = streamDistance * streamDistance;
                    AxisAlignedBox bounds = Bounds[id];
                    bool inRange = bounds.OverlapsSphereSq(cameraLODData.Origin, distanceSq);
                    bool wasInRange = InRange[id];
                    bool isLoaded = IsLoaded[id];

                    InRangeThisFrame.SetAtomic(id, inRange);
                    if (inRange != wasInRange)
                        InRange.SetAtomic(id, inRange);

                    if (inRange)
                        LastFrameInRange[id] = FrameCount;

                    if (inRange && (!isLoaded || !wasInRange))
                    {
                        ToLoad.AddNoResize(id);
                    }
                    else if (!inRange && isLoaded)
                    {
                        int outOfRangeFrameCount = FrameCount - LastFrameInRange[id];
                        if (outOfRangeFrameCount >= OutOfRangeFrameCount)
                        {
                            ToUnload.AddNoResize(id);
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        JobHandle ScheduleUpdateInRangeRenderers(InstancedCameraID cameraID, JobHandle dependency = default)
        {
            ref readonly InstancedRendererArrays rendererArrays = ref m_Context.RendererManager.Data;
            UnsafeIndirectList<InstancedRendererID> groups = m_Context.RendererManager.Valid;

            UpdateRendererStreamingJob job = new UpdateRendererStreamingJob
            {
                RendererData = rendererArrays.Culling.AsReadOnly(),
                LoadedRenderers = groups,
                Bounds = rendererArrays.WorldBounds.AsReadOnly(),
                IsLoaded = rendererArrays.IsLoaded,
                PrototypeArrays = m_Context.PrototypeManager.Data.AsReadOnly(),
                FrameCount = m_Context.FrameCount,
                CameraID = cameraID,
                DisableCullDistance = DisableRenderDistance,
                CameraArrays = m_Context.CameraManager.Data.AsReadOnly(),
                LastFrameInRange = rendererArrays.LastFrameInRange,
                InRangeThisFrame = rendererArrays.InRangeLastFrame,
                InRange = rendererArrays.InRange,
                ToLoad = m_Context.RendererManager.RequiringLoad.AsParallelWriter(),
                ToUnload = m_Context.RendererManager.RequiringUnload.AsParallelWriter()
            };

            dependency = JobHandle.CombineDependencies(dependency, m_Context.RendererManager.UpdateJobHandle);
            return job.ScheduleBatchByRef(groups.Length, UpdateRendererStreamingJob.BatchSize, dependency);
        }
    }
}
