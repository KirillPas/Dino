// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Demo
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Flora Demo/Demo Scene Settings")]
    public class FloraDemoSceneSettings : MonoBehaviour
    {
        [Serializable]
        public class RenderPipelineSettings
        {
            [Tooltip("The lighting game object to use for the demo.")]
            public GameObject Lighting;
            [Tooltip("The terrain material to use for the demo.")]
            public Material Terrain;
            [Tooltip("The sun light to use for the demo.")]
            public Light Sun;
            [Tooltip("The sky material to use for the demo.")]
            public Material Sky;
            [Tooltip("The ambient mode to use for the demo.")]
            public AmbientMode AmbientMode = AmbientMode.Skybox;

            public void SetActive(bool active)
            {
                if (Lighting) Lighting.SetActive(active);
                if (Sky) RenderSettings.skybox = Sky;
                RenderSettings.ambientMode = AmbientMode;
            }
        }

        [Header("Lighting (URP/Built-in)")]
        [ColorUsage(false, true)]
        [Tooltip("The fog color to use for the demo.")]
        public Color FogColor = Color.gray;
        [Range(0.0f, 0.01f)]
        [Tooltip("The fog density to use for the demo.")]
        public float FogDensity = 0.002f;
        [Range(0.0f, 2.0f)]
        [Tooltip("The skybox intensity to use for the demo.")]
        public float AmbientIntensity = 1.0f;
        [Range(0.0f, 2.0f)]
        [Tooltip("The environment reflection intensity to use for the demo.")]
        public float ReflectionsIntensity = 1.0f;

        [Header("Wind")]
        [Tooltip("The wind zone to use for the demo.")]
        public WindZone WindZone;
        [Tooltip("The wind noise texture to use for the demo.")]
        public Texture2D WindNoise;

        [Header("Render Pipelines")]
        [Tooltip("The settings to use when using the built-in render pipeline.")]
        public RenderPipelineSettings BuiltinSettings = new RenderPipelineSettings();
        [Tooltip("The settings to use when using the Universal render pipeline.")]
        public RenderPipelineSettings UniversalSettings = new RenderPipelineSettings();
        [Tooltip("The settings to use when using the High Definition render pipeline.")]
        public RenderPipelineSettings HighDefinitionSettings = new RenderPipelineSettings();

        Vector3 m_WindOffset;
        bool m_NeedsRenderPipelineUpdate;
        RenderPipelineAsset m_LastRenderPipeline;
        RenderPipelineSettings m_CurrentPipelineSettings;

        void OnEnable()
        {
            m_LastRenderPipeline = GetRenderPipelineAsset();
            UpdateRenderPipeline();
            RenderPipelineManager.activeRenderPipelineTypeChanged -= UpdateRenderPipeline;
            RenderPipelineManager.activeRenderPipelineTypeChanged += UpdateRenderPipeline;
        }

        void OnDisable()
        {
            RenderPipelineManager.activeRenderPipelineTypeChanged -= UpdateRenderPipeline;
        }

        void OnValidate()
        {
            m_NeedsRenderPipelineUpdate = true;
            HighDefinitionSettings.Sky = null;
        }
        
        static readonly int k_GlobalWindParams0 = Shader.PropertyToID("_FloraDemo_GlobalWindParams0");
        static readonly int k_GlobalWindParams1 = Shader.PropertyToID("_FloraDemo_GlobalWindParams1");
        static readonly int k_WindNoiseTexture = Shader.PropertyToID("_FloraDemo_WindNoiseTexture");
        static readonly int k_SunDirection = Shader.PropertyToID("_FloraDemo_SunDirection");

        void Update()
        {
            if (m_NeedsRenderPipelineUpdate)
            {
                m_NeedsRenderPipelineUpdate = false;
                UpdateRenderPipeline();
            }
            
            if (WindZone)
            {
                float strength = WindZone.windMain;
                float speed = WindZone.windPulseFrequency;
                float turbulence = WindZone.windTurbulence;
                Vector3 direction = transform.rotation * Vector3.forward;
                m_WindOffset += Time.deltaTime * speed * direction;

                Shader.SetGlobalVector(k_GlobalWindParams0, new Vector4(direction.x, direction.z, m_WindOffset.x, m_WindOffset.z));
                Shader.SetGlobalVector(k_GlobalWindParams1, new Vector4(speed, strength, turbulence, 0));
                Shader.SetGlobalTexture(k_WindNoiseTexture, WindNoise);
            }

            Shader.SetGlobalVector(k_SunDirection, m_CurrentPipelineSettings.Sun ? -m_CurrentPipelineSettings.Sun.transform.forward : Vector3.down);
        }

        void UpdateRenderPipeline()
        {
            BuiltinSettings.SetActive(false);
            UniversalSettings.SetActive(false);
            HighDefinitionSettings.SetActive(false);

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientIntensity = AmbientIntensity;
            RenderSettings.reflectionIntensity = ReflectionsIntensity;

            RenderPipelineAsset asset = GetRenderPipelineAsset();
            if (asset == null)
            {
                BuiltinSettings.SetActive(true);
                SetTerrainMaterials(BuiltinSettings.Terrain);
                m_CurrentPipelineSettings = BuiltinSettings;
            }
            else if (asset.GetType().Name.Contains("Universal"))
            {
                UniversalSettings.SetActive(true);
                SetTerrainMaterials(UniversalSettings.Terrain);
                m_CurrentPipelineSettings = UniversalSettings;
            }
            else if (asset.GetType().Name.Contains("HD"))
            {
                HighDefinitionSettings.SetActive(true);
                SetTerrainMaterials(HighDefinitionSettings.Terrain);
                m_CurrentPipelineSettings = HighDefinitionSettings;
            }

#if UNITY_EDITOR
            if (asset != m_LastRenderPipeline)
            {
                UnityEditor.Lightmapping.BakeAsync();
            }
#endif
            m_LastRenderPipeline = asset;
        }

        static RenderPipelineAsset GetRenderPipelineAsset()
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (!asset) asset = GraphicsSettings.defaultRenderPipeline;
            return asset;
        }

        static void SetTerrainMaterials(Material material)
        {
            if (material)
            {
                Terrain[] terrains = Terrain.activeTerrains;
                foreach (Terrain terrain in terrains)
                    terrain.materialTemplate = material;
            }
        }
    }
}
