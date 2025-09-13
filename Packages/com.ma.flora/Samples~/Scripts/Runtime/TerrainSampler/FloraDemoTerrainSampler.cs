// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MA.Flora.Examples
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Terrain))]
    [AddComponentMenu("Flora Demo/Terrain Sampler")]
    public class FloraDemoTerrainSampler : MonoBehaviour
    {
        public enum DiffuseOutputResolution
        {
            X512 = 512,
            X1024 = 1024,
            X2048 = 2048,
            X4096 = 4096
        }

        [SerializeField] DiffuseOutputResolution m_Resolution = DiffuseOutputResolution.X1024;
        [SerializeField] Shader m_SamplerShader;
        [NonSerialized] Material m_SamplerMaterial;
        [NonSerialized] Terrain m_Terrain;
        [NonSerialized] RenderTexture m_DiffuseRT;

        void OnEnable()
        {
            m_SamplerMaterial = CoreUtils.CreateEngineMaterial(m_SamplerShader);
            TryGetComponent(out m_Terrain);
            TerrainCallbacks.textureChanged += OnTerrainTextureChanged;
        }

        void OnDisable()
        {
            if (m_SamplerMaterial) CoreUtils.Destroy(m_SamplerMaterial);
            if (m_DiffuseRT) m_DiffuseRT.Release();
            m_DiffuseRT = null;
            TerrainCallbacks.textureChanged -= OnTerrainTextureChanged;
        }
        
        void OnTerrainTextureChanged(Terrain terrain, string textureName, RectInt texelRegion, bool synched)
        {
            if (!m_Terrain || !m_Terrain.terrainData || terrain != m_Terrain)
                return;

            ResampleTerrainColors();
        }
        
        static readonly int s_DiffuseEnabled = Shader.PropertyToID("_Terrain_DiffuseEnabled");
        static readonly int s_TerrainMinMax = Shader.PropertyToID("_Terrain_MinMax");
        static readonly int s_TerrainDiffuse = Shader.PropertyToID("_Terrain_Diffuse");

        void Update()
        {
            Shader.SetGlobalInt(s_DiffuseEnabled, 0);
            
            if (!m_Terrain || !m_Terrain.terrainData)
                return;

            if (!m_DiffuseRT || m_DiffuseRT.width != (int)m_Resolution)
                ResampleTerrainColors();

            Shader.SetGlobalInt(s_DiffuseEnabled, 1);
            Shader.SetGlobalTexture(s_TerrainDiffuse, m_DiffuseRT);

            Bounds terrainBounds = m_Terrain.terrainData.bounds;
            terrainBounds.center += m_Terrain.transform.position;
            Shader.SetGlobalVector(s_TerrainMinMax, new Vector4(terrainBounds.min.x, terrainBounds.min.z, terrainBounds.max.x, terrainBounds.max.z));
        }

        static readonly int s_Control = Shader.PropertyToID("_Control");
        static readonly int s_Diffuse0 = Shader.PropertyToID("_Diffuse0");
        static readonly int s_Diffuse1 = Shader.PropertyToID("_Diffuse1");
        static readonly int s_Diffuse2 = Shader.PropertyToID("_Diffuse2");
        static readonly int s_Diffuse3 = Shader.PropertyToID("_Diffuse3");
        
        void ResampleTerrainColors()
        {
            if (!m_SamplerMaterial)
                return;

            int resolution = (int)m_Resolution;
            if (!m_DiffuseRT || m_DiffuseRT.width != resolution)
            {
                m_DiffuseRT?.Release();
                m_DiffuseRT = new RenderTexture(resolution, resolution, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None);
            }

            var terrainData = m_Terrain.terrainData;
            var terrainLayers = terrainData.terrainLayers;

            m_SamplerMaterial.SetTexture(s_Control, terrainData.GetAlphamapTexture(0));
            if (terrainLayers.Length > 0)
                m_SamplerMaterial.SetTexture(s_Diffuse0, terrainLayers[0].diffuseTexture);
            if (terrainLayers.Length > 1)
                m_SamplerMaterial.SetTexture(s_Diffuse1, terrainLayers[1].diffuseTexture);
            if (terrainLayers.Length > 2)
                m_SamplerMaterial.SetTexture(s_Diffuse2, terrainLayers[2].diffuseTexture);
            if (terrainLayers.Length > 3)
                m_SamplerMaterial.SetTexture(s_Diffuse3, terrainLayers[3].diffuseTexture);

            Graphics.SetRenderTarget(m_DiffuseRT);
            Graphics.Blit(null, m_DiffuseRT, m_SamplerMaterial);
        }
    }
}
