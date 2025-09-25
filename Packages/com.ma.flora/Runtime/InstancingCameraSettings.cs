// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace MA.Flora
{
    /// <summary>
    /// Specifies the instancing camera's occlusion mode.
    /// </summary>
    public enum InstancingOcclusionMode
    {
        /// <summary>Don't use occlusion.</summary>
        [InspectorName("No Occlusion Culling")]
        None,
        /// <summary>Use Unity's CPU-based baked static occlusion system to occlude instances.</summary>
        [InspectorName("CPU Baked")]
        Umbra,
        /// <summary>Use a GPU-based occlusion system to occlude instances.</summary>
        [InspectorName("GPU Hi-Z")]
        HierarchicalDepth
    }

    /// <summary>
    /// The mode for the cross-fade animation duration.
    /// </summary>
    public enum CrossFadeAnimatedDurationMode : byte
    {
        /// <summary>Use the global cross-fade animation duration (LODGroup.crossFadeAnimationDuration).</summary>
        Global,
        /// <summary>Use a custom cross-fade animation duration.</summary>
        Camera
    }

    /// <summary>
    /// An optional component that will affect the how a camera renders instances.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Flora/Instancing Camera Settings")]
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancingCameraSettings Icon.png")]
    [HelpURL("https://flora.magneticarcade.com/components/instancing-camera-settings")]
    public sealed class InstancingCameraSettings : MonoBehaviour, IAdditionalData
    {
        [SerializeField] InstancingOcclusionMode m_OcclusionMode = InstancingOcclusionMode.None;
        [SerializeField, Min(0.00001f)] float m_MinimumScreenSize = 0.00005f;
        [SerializeField] bool m_DisableInstanceRendering;

        [SerializeField, Min(0.01f)] float m_LODBiasScale = 1.0f;
        [FormerlySerializedAs("m_LODCrossFadeDurationMode")] [SerializeField] CrossFadeAnimatedDurationMode m_CrossFadeAnimatedDurationMode = Flora.CrossFadeAnimatedDurationMode.Global;
        [FormerlySerializedAs("m_LODCrossFadeDuration")] [SerializeField, Min(0.1f)] float m_CrossFadeAnimatedDuration = 0.3f;

        /// <summary>The occlusion mode for this camera.</summary>
        public InstancingOcclusionMode OcclusionMode
        {
            get => m_OcclusionMode;
            set => m_OcclusionMode = value;
        }

        /// <summary>The minimum screen size for culling instance renderers.</summary>
        public float MinimumScreenSize
        {
            get => m_MinimumScreenSize;
            set => m_MinimumScreenSize = Mathf.Max(0.00001f, value);
        }

        /// <summary>Determines whether instancing is enabled for this camera.</summary>
        public bool DisableInstanceRendering
        {
            get => m_DisableInstanceRendering;
            set => m_DisableInstanceRendering = value;
        }

        /// <summary>Determines the LOD bias for this camera.</summary>
        public float LODBiasScale
        {
            get => m_LODBiasScale;
            set => m_LODBiasScale = Mathf.Max(0.0f, value);
        }

        /// <summary>The cross-fade animation mode for this camera.</summary>
        public CrossFadeAnimatedDurationMode CrossFadeAnimatedDurationMode
        {
            get => m_CrossFadeAnimatedDurationMode;
            set => m_CrossFadeAnimatedDurationMode = value;
        }

        /// <summary>The cross-fade animation duration for this camera.</summary>
        public float CrossFadeAnimatedDuration
        {
            get => m_CrossFadeAnimatedDuration;
            set => m_CrossFadeAnimatedDuration = Mathf.Max(0.1f, value);
        }

        void OnValidate()
        {
            MinimumScreenSize = Mathf.Max(0.00001f, MinimumScreenSize);
            LODBiasScale = Mathf.Max(0.01f, LODBiasScale);
        }
    }
}
