// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora
{
    /// <summary>Determines how the main light will be chosen for culling shadow casting instances.</summary>
    public enum InstancingMainLightMode
    {
        /// <summary>Flora will update the main light if its disabled or its transform, intensity, or shadow casting mode changes.</summary>
        [InspectorName("Auto (Default)")]
        Auto,
        /// <summary>Flora will not update the main light automatically. You must set <see cref="InstancingSceneSettings.MainLightOverride"/> manually.</summary>
        [InspectorName("Manual")]
        Manual
    }

    /// <summary>Global scene settings for configuring instancing.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Flora/Instancing Scene Settings")]
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancingSceneSettings Icon.png")]
    [HelpURL("https://flora.magneticarcade.com/components/instancing-scene-settings")]
    public class InstancingSceneSettings : MonoBehaviour
    {
        [SerializeField, Range(0.0f, 1.0f)] float m_GlobalInstanceDensity = 1.0f;
        [SerializeField] InstancingMainLightMode m_MainLightMode = InstancingMainLightMode.Auto;
        [SerializeField] Light m_MainLightOverride;

        /// <summary>Returns the global instance of <see cref="InstancingSceneSettings"/>.</summary>
        /// <remarks>There should be exactly one instance of <see cref="InstancingSceneSettings"/> across all loaded scenes.</remarks>
        public static InstancingSceneSettings Global => s_GlobalInstance == null ? ComponentSingleton<InstancingSceneSettings>.instance : s_GlobalInstance;
        static InstancingSceneSettings s_GlobalInstance;

        /// <summary>Invoked when the global scene settings change.</summary>
        public static event Action GlobalSceneSettingsChanged;

        /// <summary>Gets or sets the density of static instances in the scene [0.0 - 1.0].</summary>
        /// <remarks>Reducing this value can decrease culling work and GPU load, improving performance.</remarks>
        /// <seealso cref="InstancedPrototype.AffectedByGlobalInstanceDensity"/>
        public float GlobalInstanceDensity
        {
            get => m_GlobalInstanceDensity;
            set
            {
                if (m_GlobalInstanceDensity != value)
                {
                    m_GlobalInstanceDensity = Mathf.Clamp01(value);
                    GlobalSceneSettingsChanged?.Invoke();
                }
            }
        }

        /// <summary>Mode for updating the main light.</summary>
        public InstancingMainLightMode MainLightMode
        {
            get => m_MainLightMode;
            set
            {
                if (m_MainLightMode != value)
                {
                    m_MainLightMode = value;
                    GlobalSceneSettingsChanged?.Invoke();
                }
            }
        }

        /// <summary>The main light used for instancing.</summary>
        /// <remarks>Setting this value will override the main light in the scene, and change the mode to <see cref="InstancingMainLightMode.Manual"/>.</remarks>
        public Light MainLightOverride
        {
            get => m_MainLightOverride;
            set
            {
                if (m_MainLightOverride != value)
                {
                    m_MainLightOverride = value;

                    if (m_MainLightOverride)
                        m_MainLightMode = InstancingMainLightMode.Manual;
                    else
                        m_MainLightMode = InstancingMainLightMode.Auto;

                    GlobalSceneSettingsChanged?.Invoke();
                }
            }
        }

        void OnEnable()
        {
            if (s_GlobalInstance)
            {
                Debug.Log($"InstancingSceneSettings: There should be only one instance of `InstancingSceneSettings` across all loaded scenes. Ignoring instance `{name}`.");
                return;
            }

            if (s_GlobalInstance == null)
            {
                s_GlobalInstance = this;
                GlobalSceneSettingsChanged?.Invoke();
            }
        }

        void OnDisable()
        {
            if (s_GlobalInstance == this)
            {
                s_GlobalInstance = null;
                GlobalSceneSettingsChanged?.Invoke();
            }
        }
    }
}
