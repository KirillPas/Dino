// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MA.Core;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UI;
using Debug = UnityEngine.Debug;
using IntPtr = System.IntPtr;

namespace MA.Flora.Rendering
{
    [GenerateHLSL]
    enum DebugShaderOverrideMode
    {
        None,
        LOD,
        RendererID,
        RenderIndex,
        GlobalID,
        RandomID,
    }

    enum DebugTreeVisualizationMode
    {
        None,
        Heatmap,
        SubdivisionLevel,
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    static class DebugShaderID
    {
        public static readonly int flora_DebugViewMode = Shader.PropertyToID("flora_DebugViewMode");
        public static readonly int flora_DebugLODIndex = Shader.PropertyToID("flora_DebugLODIndex");
    }

    class DebugDisplayData : IDebugData
    {
        // General settings
        public DebugShaderOverrideMode DebugShaderOverrideMode;
        public bool FreezeCulling;
        public bool DisableCulling;

        // LOD settings
        public int ForceLOD = -1;
        public int OnlyLOD = -1;

        // Streaming settings
        public bool ShowStreamingDebug;
        public bool ShowRuntimeSpatialGrid;

        // Static occlusion settings
        public bool ShowStaticOcclusionSpheres;

        // Dynamic occlusion settings
        public bool OcclusionOverlayEnabled;
        public bool OcclusionOverlayCountVisible;
        public bool OcclusionOverrideTestToAlwaysPass;
        public bool OccluderDepthOverlayEnabled;
        public Vector2 OcclusionDepthViewRange;

        // Static tree settings
        public DebugTreeVisualizationMode TreeVisualizationMode;
        public float TreeVisualizationMaxDistance = 100.0f;
        public int TreeVisualizationOnlyDepth = -1;

        // Culling stats
        public bool EnableDispatchCounters;

        // Profiling settings
        public int SampleHistorySize = 30;

        static readonly Lazy<DebugDisplayData> s_Instance = new Lazy<DebugDisplayData>(() =>
        {
            DebugDisplayData instance = new DebugDisplayData();
            instance.Reset();
            return instance;
        });

        public static DebugDisplayData Instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_Instance.Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsActive() => s_Instance.IsValueCreated && s_Instance.Value.m_IsDisplayActive;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetActive(out DebugDisplayData debugData)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (s_Instance.IsValueCreated && s_Instance.Value.m_IsDisplayActive)
            {
                debugData = s_Instance.Value;
                return true;
            }
#endif

            debugData = null;
            return false;
        }

        public const string PanelName = "Flora";
        const string k_FormatString = "{0}";
        const string k_FPS_FormatString = "{0:F1}";
        const string k_MS_FormatString = "{0:F2}ms";
        const float k_RefreshRate = 1f / 5f;

        enum MarkerType
        {
            CPU,
            Job,
            GPU,
        }

        Dictionary<IntPtr, string> m_MarkerDisplayNames = new();
        Dictionary<IntPtr, ProfilerRecorder> m_RecordedMarkersCPU = new();
        Dictionary<IntPtr, ProfilerRecorder> m_RecordedMarkersJob = new();
        Dictionary<IntPtr, ProfilerRecorder> m_RecordedMarkersGPU = new();
        Dictionary<IntPtr, DebugSampleHistory> m_AccumulatedGPUTiming = new();
        Dictionary<IntPtr, DebugSampleHistory> m_AccumulatedCPUTiming = new();
        Dictionary<IntPtr, DebugSampleHistory> m_AccumulatedJobTiming = new();

        bool m_IsRuntimeDisplayEnabled;
        bool m_IsDisplayActive;
        bool m_IsRegistered;
        DebugUI.Widget[] m_DebugItems;
        DebugDisplayGizmos m_DebugDisplayGizmos;

        const int k_MaxCullingTreeDepth = 7;
        public Color[] TreeHeatmapColors { get; } = new Color[k_MaxCullingTreeDepth];
        public Color[] TreeSubdivisionColors { get; } = new Color[k_MaxCullingTreeDepth];

        public DebugDisplayData()
        {
            TreeHeatmapColors[0] = new Color(158.0f / 255.0f, 228.0f / 255.0f, 251.0f / 255.0f, 1.0f); // #1A1D21
            TreeHeatmapColors[1] = new Color(56.0f  / 255.0f, 243.0f / 255.0f, 176.0f / 255.0f, 1.0f); // #38F3B0
            TreeHeatmapColors[2] = new Color(168.0f / 255.0f, 238.0f / 255.0f, 46.0f  / 255.0f, 1.0f); // #A8EE2E
            TreeHeatmapColors[3] = new Color(255.0f / 255.0f, 214.0f / 255.0f, 0.0f   / 255.0f, 1.0f); // #FFD600
            TreeHeatmapColors[4] = new Color(253.0f / 255.0f, 152.0f / 255.0f, 0.0f   / 255.0f, 1.0f); // #FD9800
            TreeHeatmapColors[5] = new Color(255.0f / 255.0f, 67.0f  / 255.0f, 51.0f  / 255.0f, 1.0f); // #FF4333
            TreeHeatmapColors[6] = new Color(132.0f / 255.0f, 10.0f  / 255.0f, 54.0f  / 255.0f, 1.0f); // #840A36

            TreeSubdivisionColors[0] = new Color(1.0f, 0.0f, 0.0f);
            TreeSubdivisionColors[1] = new Color(0.0f, 1.0f, 0.0f);
            TreeSubdivisionColors[2] = new Color(0.0f, 0.0f, 1.0f);
            TreeSubdivisionColors[3] = new Color(1.0f, 1.0f, 0.0f);
            TreeSubdivisionColors[4] = new Color(1.0f, 0.0f, 1.0f);
            TreeSubdivisionColors[5] = new Color(0.0f, 1.0f, 1.0f);
            TreeSubdivisionColors[6] = new Color(0.5f, 0.5f, 0.5f);
        }

        Action IDebugData.GetReset() => Reset;

        void ResetData()
        {
            DebugShaderOverrideMode = DebugShaderOverrideMode.None;
            FreezeCulling = false;
            DisableCulling = false;
            ForceLOD = -1;
            OnlyLOD = -1;
            ShowStaticOcclusionSpheres = false;
            OcclusionOverlayEnabled = false;
            OcclusionOverlayCountVisible = false;
            OcclusionOverrideTestToAlwaysPass = false;
            OccluderDepthOverlayEnabled = false;
            OcclusionDepthViewRange = new Vector2(0.0f, 1.0f);
            TreeVisualizationMode = DebugTreeVisualizationMode.None;
            TreeVisualizationMaxDistance = 100.0f;
            TreeVisualizationOnlyDepth = -1;
            EnableDispatchCounters = false;
            SampleHistorySize = 30;
        }

        void Reset()
        {
            ResetData();
            Unregister();
            Register();
            DebugManager.instance.RefreshEditor();
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public void Register()
        {
            if (!m_IsRegistered)
            {
                m_IsRegistered = true;
                AddProfilingRecorders();

                List<DebugUI.Widget> widgets = new List<DebugUI.Widget>
                {
                    CreateGeneralSettings(),
                    CreateLODSettings(),
                    CreateStreamingSettings(),
                    CreateGPUOcclusionSettings(),
                    CreateCPUOcclusionSettings(),
                    CreateCullingSettings(),
                    CreateMemoryStats(),
                    CreateCullingStats(),
                };

                if (Application.isPlaying)
                    widgets.Add(CreateProfilingStats());

                if (widgets.Count > 0)
                {
                    m_DebugItems = widgets.ToArray();
                    DebugUI.Panel panel = DebugManager.instance.GetPanel(PanelName, true);
                    panel.children.Add(m_DebugItems);
                }

                DebugManager.instance.RegisterData(this);
            }
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public void Unregister()
        {
            if (m_IsRegistered)
            {
                m_IsRegistered = false;

                DebugUI.Panel panel = DebugManager.instance.GetPanel(PanelName);
                panel?.children.Remove(m_DebugItems);

                DebugManager.instance.UnregisterData(this);

                if (m_DebugDisplayGizmos)
                {
                    UnityUtility.Destroy(m_DebugDisplayGizmos.gameObject);
                    m_DebugDisplayGizmos = null;
                }

                ClearProfilingRecorders();
                ResetData();
            }
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public void Update()
        {
            if (!m_IsRegistered)
                return;

            bool displayRuntimeUI = DebugManager.instance.displayRuntimeUI;
            m_IsDisplayActive = displayRuntimeUI || DebugManager.instance.displayPersistentRuntimeUI
#if UNITY_EDITOR
                                                 || DebugManager.instance.displayEditorUI
#endif
                ;


            if (m_IsDisplayActive)
            {
                if (!m_DebugDisplayGizmos)
                    m_DebugDisplayGizmos = DebugDisplayGizmos.GetOrCreate();

                // Update profile timings.
                UpdateProfilingTiming(m_RecordedMarkersCPU, m_AccumulatedCPUTiming);
                UpdateProfilingTiming(m_RecordedMarkersJob, m_AccumulatedJobTiming);
                UpdateProfilingTiming(m_RecordedMarkersGPU, m_AccumulatedGPUTiming);

                // Patch the runtime display UI with custom handlers
                if (m_IsRuntimeDisplayEnabled != displayRuntimeUI)
                {
                    m_IsRuntimeDisplayEnabled = displayRuntimeUI;
                    if (m_IsRuntimeDisplayEnabled)
                    {
                        DebugUIHandlerCanvas canvas = UnityUtility.FindFirstObjectByType<DebugUIHandlerCanvas>();
                        DebugUIPrefabBundle prefabBundle = new DebugUIPrefabBundle { type = typeof(DebugUIExt.ValueTuple).AssemblyQualifiedName, prefab = Resources.Load<RectTransform>("DebugUIValueTuple") };
                        Assert.IsNotNull(prefabBundle.prefab);
                        canvas.prefabs.Add(prefabBundle);
                        DebugManager.instance.ReDrawOnScreenDebug();
                    }
                }

                // Enable/Disable the debug shader mode.
                Shader.SetGlobalInteger(DebugShaderID.flora_DebugViewMode, (int)DebugShaderOverrideMode);
            }
            else
            {
                if (m_DebugDisplayGizmos)
                {
                    UnityUtility.Destroy(m_DebugDisplayGizmos.gameObject);
                    m_DebugDisplayGizmos = null;
                }
            }
        }

        DebugUI.Widget CreateGeneralSettings()
        {
            return new DebugUI.Container
            {
                displayName = "General",
                children =
                {
                    new DebugUI.EnumField
                    {
                        displayName = "Instance Override",
                        tooltip = "Set the instance debug shading mode.",
                        autoEnum = typeof(DebugShaderOverrideMode),
                        getter = () => (int)DebugShaderOverrideMode,
                        setter = value => DebugShaderOverrideMode = (DebugShaderOverrideMode)value,
                        getIndex = () => (int)DebugShaderOverrideMode,
                        setIndex = value => DebugShaderOverrideMode = (DebugShaderOverrideMode)value
                    },
                    new DebugUI.BoolField
                    {
                        displayName = "Disable Culling",
                        tooltip = "Disables all culling of Flora instances.",
                        getter = () => DisableCulling,
                        setter = value => DisableCulling = value
                    },
                    new DebugUI.BoolField
                    {
                        displayName = "Freeze Culling",
                        tooltip = "Freezes the culling state of the current active camera.",
                        getter = () => FreezeCulling,
                        setter = value => FreezeCulling = value
                    },
                }
            };
        }

        DebugUI.Widget CreateLODSettings()
        {
            return new DebugUI.Container
            {
                displayName = "LOD",
                children =
                {
                    new DebugUI.IntField
                    {
                        displayName = "Force LOD",
                        tooltip = "Force a specific LOD level.",
                        getter = () => ForceLOD,
                        setter = value => ForceLOD = value,
                        min = () => -1,
                        max = () => 7
                    },
                    new DebugUI.IntField
                    {
                        displayName = "Only LOD",
                        tooltip = "Only renders a specific LOD level.",
                        getter = () => OnlyLOD,
                        setter = value => OnlyLOD = value,
                        min = () => -1,
                        max = () => 7
                    },
                }
            };
        }

        DebugUI.Widget CreateCullingSettings()
        {
            return new DebugUI.Container
            {
                displayName = "Culling Data",
                children =
                {
                    new DebugUI.EnumField
                    {
                        displayName = "BVH Visualization",
                        tooltip = "Draw gizmos for visualizing the static instance trees.",
                        autoEnum = typeof(DebugTreeVisualizationMode),
                        getter = () => (int)TreeVisualizationMode,
                        setter = value => TreeVisualizationMode = (DebugTreeVisualizationMode)value,
                        getIndex = () => (int)TreeVisualizationMode,
                        setIndex = value => TreeVisualizationMode = (DebugTreeVisualizationMode)value,
                    },
                    new DebugUI.FloatField
                    {
                        displayName = "BVH Draw Distance",
                        tooltip = "The maximum distance at which the tree gizmos are drawn.",
                        getter = () => TreeVisualizationMaxDistance,
                        setter = value => TreeVisualizationMaxDistance = value,
                        min = () => 10.0f
                    },
                    new DebugUI.IntField
                    {
                        displayName = "BVH Level",
                        tooltip = "Only renders a specific tree level.",
                        getter = () => TreeVisualizationOnlyDepth,
                        setter = value => TreeVisualizationOnlyDepth = value,
                        min = () => -1,
                        max = () => 7
                    },
                }
            };
        }

        DebugUI.Widget CreateStreamingSettings()
        {
            return new DebugUI.Container
            {
                displayName = "Streaming",
                children =
                {
                    new DebugUI.BoolField
                    {
                        displayName = "Draw Streaming Bounds",
                        tooltip = "Enable the GPU streaming bounds debug gizmos.",
                        getter = () => ShowStreamingDebug,
                        setter = value => ShowStreamingDebug = value
                    },
                    // new DebugUI.BoolField
                    // {
                    //     displayName = "Draw Runtime Spatial Grid",
                    //     tooltip = "Enable the gizmos for the runtime spatial grid.",
                    //     getter = () => ShowRuntimeSpatialGrid,
                    //     setter = value => ShowRuntimeSpatialGrid = value
                    // },
                }
            };
        }

        DebugUI.Widget CreateGPUOcclusionSettings()
        {
            return new DebugUI.Container
            {
                displayName = "GPU Occlusion",
                children =
                {
                    new DebugUI.BoolField
                    {
                        displayName = "Force Never Occlude",
                        tooltip = "Override the occlusion test to always pass.",
                        getter = () => OcclusionOverrideTestToAlwaysPass,
                        setter = value => OcclusionOverrideTestToAlwaysPass = value
                    },
                    new DebugUI.BoolField
                    {
                        displayName = "Occlusion Overlay",
                        tooltip = "Enable the occlusion overlay.",
                        getter = () => OcclusionOverlayEnabled,
                        setter = value => OcclusionOverlayEnabled = value,
                        isHiddenCallback = InstancingSystem.UseGPUDrivenOcclusion
                    },
                    new DebugUI.Container
                    {
                        children =
                        {
                            new DebugUI.BoolField
                            {
                                displayName = "Count Visible",
                                tooltip = "Show the number of visible instances in the occlusion overlay.",
                                getter = () => OcclusionOverlayCountVisible,
                                setter = value => OcclusionOverlayCountVisible = value,
                                isHiddenCallback = () => !OcclusionOverlayEnabled
                            },
                        },
                        isHiddenCallback = InstancingSystem.UseGPUDrivenOcclusion
                    },
                    new DebugUI.BoolField
                    {
                        displayName = "Depth Overlay",
                        tooltip = "Enable the occluder pyramid debug view.",
                        getter = () => OccluderDepthOverlayEnabled,
                        setter = value => OccluderDepthOverlayEnabled = value,
                        isHiddenCallback = InstancingSystem.UseGPUDrivenOcclusion
                    },
                    new DebugUI.Container
                    {
                        children =
                        {
                            new DebugUI.FloatField
                            {
                                displayName = "Range Min",
                                isHiddenCallback = () => !OccluderDepthOverlayEnabled,
                                tooltip = "The minimum range of the occluder debug view.",
                                min = () => 0.0f,
                                max = () => 1.0f,
                                getter = () => OcclusionDepthViewRange.x,
                                setter = value => OcclusionDepthViewRange.x = value
                            },
                            new DebugUI.FloatField
                            {
                                displayName = "Range Max",
                                isHiddenCallback = () => !OccluderDepthOverlayEnabled,
                                tooltip = "The maximum range of the occluder debug view.",
                                min = () => 0.0f,
                                max = () => 1.0f,
                                getter = () => OcclusionDepthViewRange.y,
                                setter = value => OcclusionDepthViewRange.y = value
                            },
                        },
                        isHiddenCallback = InstancingSystem.UseGPUDrivenOcclusion
                    },
                }
            };
        }

        DebugUI.Widget CreateCPUOcclusionSettings()
        {
            return new DebugUI.Container
            {
                displayName = "CPU Occlusion",
                children =
                {
                    new DebugUI.BoolField
                    {
                        displayName = "Draw Bounding Spheres",
                        tooltip = "Enable the static occlusion culling spheres gizmos.",
                        getter = () => ShowStaticOcclusionSpheres,
                        setter = value => ShowStaticOcclusionSpheres = value
                    },
                }
            };
        }

        DebugUI.Widget CreateCullingStats()
        {
            return new DebugUI.Container
            {
                displayName = "Culling Stats",
                children =
                {
                    new DebugUI.BoolField
                    {
                        displayName = "Dispatch Counters",
                        tooltip = "Enable dispatch counters for the GPU culling system.",
                        getter = () => EnableDispatchCounters,
                        setter = value => EnableDispatchCounters = value
                    },
                    new DebugUI.Value
                    {
                        displayName = "Draws",
                        tooltip = "The number of draw calls submitted to Unity.",
                        getter = () => InstanceCuller.ProfilingCounters.Draws.Value,
                        refreshRate = k_RefreshRate
                    },
                    new DebugUI.Value
                    {
                        displayName = "Batches",
                        tooltip = "The number of batches processed.",
                        getter = () => InstanceCuller.ProfilingCounters.Batches.Value,
                        refreshRate = k_RefreshRate
                    },
                    new DebugUI.Container
                    {
                        displayName = "Instances",
                        children =
                        {
                            new DebugUI.Value
                            {
                                displayName = "Submitted",
                                tooltip = "The number of mesh instances submitted to Unity.",
                                getter = () => StringUtility.FormatLargeNumber(InstanceCuller.ProfilingCounters.Submitted.Value),
                                refreshRate = k_RefreshRate
                            },
                            new DebugUI.Value
                            {
                                displayName = "Visible",
                                isHiddenCallback = () => !EnableDispatchCounters,
                                tooltip = "The number of visible mesh instances.",
                                getter = () => StringUtility.FormatLargeNumber(InstanceCuller.ProfilingCounters.Visible.Value),
                                refreshRate = k_RefreshRate
                            },
                            new DebugUI.Value
                            {
                                displayName = "Culled",
                                isHiddenCallback = () => !EnableDispatchCounters,
                                tooltip = "The number of culled mesh instances.",
                                getter = () => StringUtility.FormatLargeNumber(InstanceCuller.ProfilingCounters.Culled.Value),
                                refreshRate = k_RefreshRate
                            },
                            new DebugUI.Value
                            {
                                displayName = "Occluded",
                                isHiddenCallback = () => !EnableDispatchCounters,
                                tooltip = "The number of occluded mesh instances.",
                                getter = () => StringUtility.FormatLargeNumber(InstanceCuller.ProfilingCounters.Occluded.Value),
                                refreshRate = k_RefreshRate
                            },
                        }
                    },
                }
            };
        }

        DebugUI.Widget CreateMemoryStats()
        {
            return new DebugUI.Container
            {
                displayName = "Memory Stats",
                children =
                {
                    new DebugUI.Value
                    {
                        displayName = "Instance Buffer",
                        getter = () => StringUtility.FormatBytes(InstancedSceneData.ProfilingCounters.InstanceBuffer.Value),
                        refreshRate = k_RefreshRate
                    },
                    new DebugUI.Value
                    {
                        displayName = "Upload Buffer Pool",
                        getter = () => StringUtility.FormatBytes(InstancedSceneData.ProfilingCounters.UploadBufferPool.Value),
                        refreshRate = k_RefreshRate
                    },
                }
            };
        }

        DebugUI.Widget CreateProfilingStats()
        {
            return new DebugUI.Container
            {
                displayName = "Profiling Stats",
                children =
                {
                    new DebugUI.IntField
                    {
                        displayName = "Sample History Size",
                        getter = () => SampleHistorySize,
                        setter = value => { SampleHistorySize = value; },
                        min = () => 1,
                        max = () => 100
                    },
                    new DebugUI.Container("Main Thread", CreateProfilingSamplerWidgetList(m_RecordedMarkersCPU.Keys, MarkerType.CPU)),
                    new DebugUI.Container("Jobs", CreateProfilingSamplerWidgetList(m_RecordedMarkersJob.Keys, MarkerType.Job)),
                    new DebugUI.Container("GPU", CreateProfilingSamplerWidgetList(m_RecordedMarkersGPU.Keys, MarkerType.GPU)),
                }
            };
        }

        void AddProfilingRecorders()
        {
            Debug.Assert(m_RecordedMarkersCPU.Count == 0);
            AddCPURecorder(InstancingSystem.Profiling.InstancingSystem, "Main Thread");
            AddCPURecorder(InstancingSystem.Profiling.Initialization, "Initialization");
            AddCPURecorder(InstancingSystem.Profiling.Schedule, "Schedule");
            AddCPURecorder(InstancingSystem.Profiling.Render, "Render");
            AddCPURecorder(InstancingSystem.Profiling.Camera, "Camera");

            Debug.Assert(m_RecordedMarkersJob.Count == 0);
            AddJobRecorder(InstanceCullingJob.CullCameraMarker, "Cull Camera Job");
            AddJobRecorder(InstanceCullingJob.CullLightMarker, "Cull Main Light Job");
            AddJobRecorder(InstanceCuller.Profiling.ProcessBatchesJob, "Combine Job");
        }

        void AddCPURecorder(ProfilerMarker marker, string displayName)
        {
            m_MarkerDisplayNames.Add(marker.Handle, displayName);
            m_RecordedMarkersCPU.Add(marker.Handle, ProfilerRecorder.StartNew(marker));
        }

        void AddJobRecorder(ProfilerMarker marker, string displayName)
        {
            m_MarkerDisplayNames.Add(marker.Handle, displayName);
            m_RecordedMarkersJob.Add(marker.Handle, ProfilerRecorder.StartNew(marker));
        }

        void AddGPURecorder(ProfilerMarker marker, string displayName)
        {
            m_MarkerDisplayNames.Add(marker.Handle, displayName);
            m_RecordedMarkersGPU.Add(marker.Handle, ProfilingUtility.StartGPURecorder(marker));
        }

        void ClearProfilingRecorders()
        {
            m_MarkerDisplayNames.Clear();

            foreach ((IntPtr _, ProfilerRecorder recorder) in m_RecordedMarkersCPU)
            {
                recorder.Stop();
                recorder.Dispose();
            }

            m_RecordedMarkersCPU.Clear();

            foreach ((IntPtr _, ProfilerRecorder recorder) in m_RecordedMarkersJob)
            {
                recorder.Stop();
                recorder.Dispose();
            }

            m_RecordedMarkersJob.Clear();
            m_RecordedMarkersGPU.Clear();

            m_AccumulatedJobTiming.Clear();
            m_AccumulatedCPUTiming.Clear();
            m_AccumulatedGPUTiming.Clear();
        }

        double GetSamplerTiming(ProfilerMarker marker, MarkerType type, DebugSampleHistory.SampleType sampleType)
        {
            Dictionary<IntPtr, DebugSampleHistory> accumulatedDictionary = type switch
            {
                MarkerType.CPU => m_AccumulatedCPUTiming,
                MarkerType.GPU => m_AccumulatedGPUTiming,
                MarkerType.Job => m_AccumulatedJobTiming,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };

            return accumulatedDictionary.TryGetValue(marker.Handle, out DebugSampleHistory accumulatedTiming)
                ? accumulatedTiming.GetSample(sampleType)
                : 0.0;
        }

        DebugUI.Value CreateWidgetForSampler(ProfilerMarker marker, MarkerType type, DebugSampleHistory.SampleType sampleType)
        {
            Dictionary<IntPtr, DebugSampleHistory> accumulatedDictionary = type switch
            {
                MarkerType.CPU => m_AccumulatedCPUTiming,
                MarkerType.GPU => m_AccumulatedGPUTiming,
                MarkerType.Job => m_AccumulatedJobTiming,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };

            if (!accumulatedDictionary.ContainsKey(marker.Handle))
            {
                accumulatedDictionary.Add(marker.Handle, new DebugSampleHistory(SampleHistorySize));
            }

            return new DebugUI.Value
            {
                refreshRate = k_RefreshRate,
                getter = () => GetSamplerTiming(marker, type, sampleType),
            };
        }

        ObservableList<DebugUI.Widget> CreateProfilingSamplerWidgetList(IEnumerable<IntPtr> profilerMarkerNameList, MarkerType markerType)
        {
            ObservableList<DebugUI.Widget> result = new ObservableList<DebugUI.Widget>();

            foreach (IntPtr handle in profilerMarkerNameList)
            {
                if (!m_MarkerDisplayNames.TryGetValue(handle, out string displayName))
                    continue;

                ProfilerMarker marker = PtrAsMarker(handle);
                result.Add(new DebugUIExt.ValueTuple
                {
                    displayName = displayName,
                    FormatString = k_MS_FormatString,
                    Values = new[]
                    {
                        CreateWidgetForSampler(marker, markerType, DebugSampleHistory.SampleType.Average),
                        CreateWidgetForSampler(marker, markerType, DebugSampleHistory.SampleType.Min),
                        CreateWidgetForSampler(marker, markerType, DebugSampleHistory.SampleType.Max),
                    }
                });
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateProfilingTiming(Dictionary<IntPtr, ProfilerRecorder> recorders, Dictionary<IntPtr, DebugSampleHistory> history)
        {
            foreach ((IntPtr handle, ProfilerRecorder recorder) in recorders)
            {
                if (history.TryGetValue(handle, out DebugSampleHistory timing))
                {
                    timing.DiscardOldSamples(SampleHistorySize);
                    timing.Add(recorder.LastValueAsDouble * 1e-6);
                    timing.ComputeAggregateValues();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ProfilerMarker PtrAsMarker(IntPtr handle) => UnsafeUtility.As<IntPtr, ProfilerMarker>(ref handle);
    }
}
