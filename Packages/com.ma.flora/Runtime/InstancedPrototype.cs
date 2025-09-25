// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Core;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace MA.Flora
{
    /// <summary>
    /// Determines the density of instances based on the distance from the camera.
    /// </summary>
    [Serializable]
    public struct InstancedDynamicDensitySettings
    {
        /// <summary>Default values for dynamic density settings.</summary>
        public static readonly InstancedDynamicDensitySettings Default = new InstancedDynamicDensitySettings
        {
            Density = 1.0f,
            Falloff = 1.0f,
            Range = new Interval(0.0f, 1.0f)
        };

        /// <summary>The dynamic density of instances at the maximum render distance, starting at the distance specified by <see cref="Range"/>.</summary>
        /// <remarks>Defaults to 1 (disabled).</remarks>
        [Range(0.0f, 1.0f)] public float Density;

        /// <summary>Controls the falloff curve of the dynamic density.</summary>
        /// <remarks>Defaults to 1.</remarks>
        [Min(0.0f)] public float Falloff;

        /// <summary>The percentage of the maximum render distance where dynamic density starts.</summary>
        [IntervalRange(0.0f, 1.0f)] public Interval Range;

        /// <summary>Whether dynamic density is enabled.</summary>
        public bool Enabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Density < 1.0f && Range is { Length: > 0.0f };
        }

        /// <summary>Sanitizes the dynamic density settings.</summary>
        public void Sanitize()
        {
            Density = math.clamp(Density, 0.0f, 1.0f);
            Falloff = math.max(Falloff, 0.0f);
            Range = Range.Clamped(0.0f, 1.0f);
        }
    }

    /// <summary>
    /// Specifies the mode for the instance's layer mask.
    /// </summary>
    public enum InstancedLayerMask : byte
    {
        /// <summary>Use the layer mask from the scene GameObject.</summary>
        FromGameObject,
        /// <summary>Use the layer mask from the prototype GameObject.</summary>
        FromPrefab,
    }

    /// <summary>
    /// Specifies the shadow material mode.
    /// </summary>
    public enum InstancedShadowOverrideMode : byte
    {
        /// <summary>Don't override the shadow material.</summary>
        None,
        /// <summary>Use the first submesh material for all submeshes.</summary>
        SharedMesh,
        /// <summary>Use a custom material for all submeshes.</summary>
        SharedCustom
    }

    /// <summary>Controls how the maximum render distance is calculated.</summary>
    public enum InstancedCullingMode
    {
        /// <summary>Use the maximum render distance calculated from the LODGroup settings.</summary>
        LODGroup,
        /// <summary>Use a custom maximum render distance.</summary>
        Override,
    }

    /// <summary>Determines the distance at which instances are streamed in and out from the GPU.</summary>
    public enum InstancedStreamingMode
    {
        /// <summary>Will stream in and out instances based on the calculated render distance.</summary>
        Auto,
        /// <summary>Will stream in and out instances based on the custom stream distance.</summary>
        /// <remarks>This value must be greater than or equal to the render distance.</remarks>
        Override,
    }

    /// <summary>
    /// Used to determine the rendering settings for instances of a model prefab.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Flora/Instanced Prototype")]
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedPrototype Icon.png")]
    [HelpURL("https://flora.magneticarcade.com/components/instanced-prototype")]
    public sealed class InstancedPrototype : MonoBehaviour, ISerializationCallbackReceiver
    {
        enum Version { Initial = 1 }

        [SerializeField] SerializableGuid m_PrefabGuid = SerializableGuid.Empty;
#pragma warning disable CS0414
        [SerializeField] Version m_Version = Version.Initial;
#pragma warning restore CS0414
        [SerializeField] InstancedLayerMask m_LayerMask = InstancedLayerMask.FromGameObject;
        [SerializeField] InstancedCullingMode m_CullingMode = InstancedCullingMode.LODGroup;
        [FormerlySerializedAs("m_RenderDistance")] [SerializeField, Min(0)] float m_CullingDistance;
        [SerializeField] InstancedStreamingMode m_StreamingMode = InstancedStreamingMode.Auto;
        [FormerlySerializedAs("m_StreamDistance")] [SerializeField, Min(0)] float m_StreamingDistance;

        [SerializeField] bool m_AffectedByGlobalInstanceDensity;
        [SerializeField] InstancedDynamicDensitySettings m_DynamicDensitySettings = InstancedDynamicDensitySettings.Default;

        [SerializeField, Min(0)] float m_ShadowDistance;
        [SerializeField, IntervalClamp(0, 7)] IntervalInt m_ShadowLODRange = new IntervalInt(0, 7);
        [SerializeField] InstancedShadowOverrideMode m_ShadowOverrideMode = InstancedShadowOverrideMode.None;
        [SerializeField] Material m_ShadowCustomMaterial;

        [SerializeField] bool m_SampleLightProbes;
        [SerializeField] Vector3 m_SampleLightProbesOffset;

        [FormerlySerializedAs("m_CreateInstancedProxy")] [SerializeField] bool m_CreateLinkedObject;
        [FormerlySerializedAs("m_InstancedProxyContributesToGI")] [SerializeField] bool m_LinkedObjectContributesToGI;

        [SerializeField] CullingTreeSettings m_CullingTreeSettings = Rendering.CullingTreeSettings.Default;

        [SerializeField] List<InstancedPropertyDescriptor> m_InstancedProperties = new List<InstancedPropertyDescriptor>();
        [SerializeField] InstancePlacementSettings m_PlacementSettings = new InstancePlacementSettings();

        [NonSerialized] bool m_IsInitialized;
        [NonSerialized] bool m_IsPrefab = true;

        AxisAlignedBox m_Bounds = AxisAlignedBox.Empty;
        Sphere m_LowBoundingSphere;
        int[] m_LODVertexCounts = Array.Empty<int>();

        /// <summary>Occurs when the prototype's settings change.</summary>
        public event Action Changed;

        /// <summary>The bounds of the prototype.</summary>
        public AxisAlignedBox Bounds
        {
            get
            {
                if (m_Bounds.IsEmpty)
                    UpdateCache();

                return m_Bounds;
            }
        }

        /// <summary>The culling mode for this prototype.</summary>
        public InstancedCullingMode CullingMode
        {
            get => m_CullingMode;
            set
            {
                if (value != m_CullingMode)
                {
                    m_CullingMode = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The render distance for this prototype.</summary>
        public float CullingDistance
        {
            get => m_CullingDistance;
            set
            {
                if (value != m_CullingDistance)
                {
                    m_CullingDistance = math.max(value, 0);
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The streaming mode for this prototype.</summary>
        public InstancedStreamingMode StreamingMode
        {
            get => m_StreamingMode;
            set
            {
                if (value != m_StreamingMode)
                {
                    m_StreamingMode = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The streaming range for this prototype.</summary>
        public float StreamingDistance
        {
            get => m_StreamingDistance;
            set
            {
                if (value != m_StreamingDistance)
                {
                    m_StreamingDistance = math.max(value, m_CullingDistance);
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Whether the layer mask is inherited from the scene GameObject or the prototype prefab.</summary>
        public InstancedLayerMask LayerMask
        {
            get => m_LayerMask;
            set
            {
                if (value != m_LayerMask)
                {
                    m_LayerMask = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Whether the static density is inherited from the global settings.</summary>
        /// <remarks>When enabled, the static density value from the global settings will be used when building the instance culling tree.</remarks>
        /// <seealso cref="InstancingSceneSettings.GlobalInstanceDensity"/>
        public bool AffectedByGlobalInstanceDensity
        {
            get => m_AffectedByGlobalInstanceDensity;
            set
            {
                if (value != m_AffectedByGlobalInstanceDensity)
                {
                    m_AffectedByGlobalInstanceDensity = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The dynamic density for this prototype.</summary>
        public InstancedDynamicDensitySettings DynamicDensitySettings
        {
            get => m_DynamicDensitySettings;
            set
            {
                if (!value.Equals(m_DynamicDensitySettings))
                {
                    m_DynamicDensitySettings = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The shadow distance for this prototype.</summary>
        public float ShadowDistance
        {
            get => m_ShadowDistance;
            set
            {
                if (value != m_ShadowDistance)
                {
                    m_ShadowDistance = math.max(value, 0);
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The shadow LOD bias scale for this prototype.</summary>
        public IntervalInt ShadowLODRange
        {
            get => m_ShadowLODRange;
            set
            {
                if (value != m_ShadowLODRange)
                {
                    m_ShadowLODRange = value.Clamped(0, 7).Sorted();
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The shadow material mode for this prototype.</summary>
        public InstancedShadowOverrideMode ShadowOverrideMode
        {
            get => m_ShadowOverrideMode;
            set
            {
                if (value != m_ShadowOverrideMode)
                {
                    m_ShadowOverrideMode = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The custom shadow material for this prototype.</summary>
        public Material ShadowCustomMaterial
        {
            get => m_ShadowCustomMaterial;
            set
            {
                if (value != m_ShadowCustomMaterial)
                {
                    m_ShadowCustomMaterial = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Whether light probes should be sampled for this prototype.</summary>
        public bool SampleLightProbes
        {
            get => m_SampleLightProbes;
            set
            {
                if (value != m_SampleLightProbes)
                {
                    m_SampleLightProbes = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The offset applied to the sample position when calculating interpolated light probes.</summary>
        public Vector3 SampleLightProbesOffset
        {
            get => m_SampleLightProbesOffset;
            set
            {
                if (value != m_SampleLightProbesOffset)
                {
                    m_SampleLightProbesOffset = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Whether to create an instanced proxy for this prototype.</summary>
        public bool CreateLinkedObject
        {
            get => m_CreateLinkedObject;
            set
            {
                if (value != m_CreateLinkedObject)
                {
                    m_CreateLinkedObject = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Whether the instanced proxy contributes to global illumination.</summary>
        public bool LinkedObjectContributesToGI
        {
            get => m_LinkedObjectContributesToGI;
            set
            {
                if (value != m_LinkedObjectContributesToGI)
                {
                    m_LinkedObjectContributesToGI = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>Settings that determine how the instance culling tree is built.</summary>
        public CullingTreeSettings CullingTreeSettings
        {
            get => m_CullingTreeSettings;
            set
            {
                if (!value.Equals(m_CullingTreeSettings))
                {
                    m_CullingTreeSettings = value;
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The placement options for this prototype.</summary>
        public InstancePlacementSettings PlacementSettings => m_PlacementSettings;

        /// <summary>The vertex count for each LOD level.</summary>
        public ReadOnlySpan<int> LODVertexCounts
        {
            get
            {
                if (m_LODVertexCounts.Length == 0)
                    UpdateCache();

                return m_LODVertexCounts;
            }
        }

        // --- Instance Properties ---

        internal static event Action AnyInstancedPropertyChanged;
        internal event Action InstancedPropertyArrayChanged;
        internal event Action<InstancedPropertyDescriptor, InstancedPropertyDescriptor> InstancedPropertyUpdated;

        /// <summary>The instance property descriptors for this prototype.</summary>
        public ReadOnlySpan<InstancedPropertyDescriptor> InstancedProperties => m_InstancedProperties.AsReadOnlySpan();

        /// <summary>The number of instance properties for this prototype.</summary>
        public int InstancedPropertyCount => m_InstancedProperties.Count;

        /// <summary>Whether this prototype has any instance properties.</summary>
        public bool HasInstancedProperties => InstancedPropertyCount > 0;

        /// <summary>Whether this prototype has an instance property with the specified name.</summary>
        /// <param name="propertyName">The name of the instance property.</param>
        public bool HasInstancedProperty(string propertyName) => IndexOfInstancedProperty(propertyName) >= 0;

        /// <summary>Gets the index of the instance property with the specified name.</summary>
        /// <param name="propertyName">The name of the instance property.</param>
        public int IndexOfInstancedProperty(string propertyName)
        {
            for (int i = 0; i < m_InstancedProperties.Count; i++)
                if (m_InstancedProperties[i].Name == propertyName)
                    return i;

            return -1;
        }

        /// <summary>Gets the index of the instance property with the specified descriptor.</summary>
        /// <param name="descriptor">The instance property descriptor.</param>
        /// <returns>The index of the instance property, or -1 if not found.</returns>
        public int IndexOfInstancedProperty(InstancedPropertyDescriptor descriptor)
        {
            for (int i = 0; i < m_InstancedProperties.Count; i++)
                if (m_InstancedProperties[i].Equals(descriptor))
                    return i;

            return -1;
        }

        /// <summary>Gets the instance property descriptor at the specified index.</summary>
        ///
        public int AddInstancedProperty(InstancedPropertyDescriptor descriptor)
        {
            int propertyIndex = IndexOfInstancedProperty(descriptor);
            if (propertyIndex >= 0)
                return propertyIndex;

            propertyIndex = m_InstancedProperties.Count;
            m_InstancedProperties.Add(descriptor);
            InstancedPropertyArrayChanged?.Invoke();
            AnyInstancedPropertyChanged?.Invoke();
            return propertyIndex;
        }

        /// <summary>Removes the instance property with the specified name.</summary>
        /// <param name="propertyName">The name of the instance property.</param>
        public void RemoveInstancedProperty(string propertyName)
        {
            int propertyIndex = IndexOfInstancedProperty(propertyName);
            if (propertyIndex < 0)
                return;

            m_InstancedProperties.RemoveAt(propertyIndex);
            InstancedPropertyArrayChanged?.Invoke();
            AnyInstancedPropertyChanged?.Invoke();
        }

        /// <summary>Removes the instance property at the specified index.</summary>
        internal void SwapInstancedProperties(int indexA, int indexB)
        {
            if (indexA < 0 || indexA >= m_InstancedProperties.Count ||
                indexB < 0 || indexB >= m_InstancedProperties.Count)
                return;

            (m_InstancedProperties[indexA], m_InstancedProperties[indexB]) = (m_InstancedProperties[indexB], m_InstancedProperties[indexA]);
            InstancedPropertyArrayChanged?.Invoke();
            AnyInstancedPropertyChanged?.Invoke();
        }

        internal void UpdateInstancedProperty(int propertyIndex, InstancedPropertyDescriptor newDescriptor)
        {
            if (propertyIndex < 0 || propertyIndex >= m_InstancedProperties.Count)
                return;

            InstancedPropertyDescriptor oldPropertyDescriptor = m_InstancedProperties[propertyIndex];
            m_InstancedProperties[propertyIndex] = newDescriptor;
            InstancedPropertyUpdated?.Invoke(oldPropertyDescriptor, newDescriptor);
            AnyInstancedPropertyChanged?.Invoke();
        }

        // --- Private ---

        internal SerializableGuid PrefabGuid => m_PrefabGuid;

        internal void ClearCache()
        {
            m_Bounds = AxisAlignedBox.Empty;
            m_LowBoundingSphere = default;
            m_LODVertexCounts = Array.Empty<int>();
            Changed?.Invoke();
        }

        void Awake()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.PrefabStage prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject);
            if (prefabStage == null && gameObject.scene.IsValid())
            {
                hideFlags = HideFlags.NotEditable |
                            HideFlags.DontSaveInBuild |
                            HideFlags.DontSaveInEditor;
                m_IsPrefab = false;
            }
            else
            {
                hideFlags = HideFlags.None;
            }
            EnsurePrefabGuid();
#endif
            UpdateCache();
        }

        void OnValidate()
        {
            if (!m_IsPrefab) return;
            m_CullingDistance = math.max(m_CullingDistance, 0);
            m_DynamicDensitySettings.Sanitize();
            m_ShadowDistance = math.max(m_ShadowDistance, 0);
            m_ShadowLODRange = m_ShadowLODRange.Clamped(0, 7).Sorted();
            m_CullingTreeSettings.Sanitize();
            m_PlacementSettings.Sanitize();
            UpdateCache();
            EnsurePrefabGuid();
        }

        [Conditional("UNITY_EDITOR")]
        void EnsurePrefabGuid()
        {
#if UNITY_EDITOR
            if (!this || !gameObject)
                return;

            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(m_PrefabGuid.EditorGUID);
            if (!m_PrefabGuid.IsValid || string.IsNullOrEmpty(assetPath))
            {
                if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(gameObject))
                {
                    string prefabPath = UnityEditor.AssetDatabase.GetAssetPath(gameObject);
                    m_PrefabGuid = UnityEditor.AssetDatabase.GUIDFromAssetPath(prefabPath);
                }
                else
                {
                    GameObject originalPrefab = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                    if (originalPrefab)
                    {
                        string prefabPath = UnityEditor.AssetDatabase.GetAssetPath(originalPrefab);
                        m_PrefabGuid = UnityEditor.AssetDatabase.GUIDFromAssetPath(prefabPath);
                    }
                }

                if (m_PrefabGuid.IsValid)
                    UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            Span<int> invalidIndices = stackalloc int[m_InstancedProperties.Count];
            int invalidCount = 0;
            for (int i = m_InstancedProperties.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(m_InstancedProperties[i].Name) || m_InstancedProperties[i].SizeInBytes == 0 || m_InstancedProperties[i].NameID == 0)
                    invalidIndices[invalidCount++] = i;
            }

            for (int i = 0; i < invalidCount; i++)
            {
                int invalidIndex = invalidIndices[i];
                m_InstancedProperties.RemoveAt(invalidIndex);
            }

#if UNITY_EDITOR
            if (!m_PrefabGuid.IsValid)
                UnityEditor.EditorApplication.delayCall += () => EnsurePrefabGuid();
#endif
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
        }

        // --- Cache ---

        internal Sphere LowBoundingSphere
        {
            get
            {
                if (m_Bounds.IsEmpty)
                    UpdateCache();

                return m_LowBoundingSphere;
            }
        }

        internal MeshRenderer[] GetLOD0MeshRenderers()
        {
            List<MeshRenderer> meshRenderers = new List<MeshRenderer>(4);
            if (TryGetComponent(out LODGroup lodGroup) && lodGroup.lodCount > 0)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length == 0)
                    return Array.Empty<MeshRenderer>();

                foreach (Renderer renderer in lods[0].renderers)
                    if (renderer is MeshRenderer meshRenderer)
                        meshRenderers.Add(meshRenderer);
            }
            else
            {
                MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
                foreach (MeshRenderer meshRenderer in renderers)
                    meshRenderers.Add(meshRenderer);
            }

            return meshRenderers.ToArray();
        }

        float CalculateMeshRendererMaxDistance()
        {
            if (!TryGetComponent(out LODGroup _))
            {
                float screenMetric = 2.0f * LODGroupUtility.CalculateFOVHalfAngle(60.0f);
                float worldSize = m_Bounds.Radius * math.cmax(math.abs(transform.lossyScale));
                float renderDistance = LODGroupUtility.CalculateLODDistance(0.01f, worldSize) * screenMetric;
                return math.clamp(MathUtility.NextMultipleOfNonPow2((int)renderDistance, 50), 50, 5000);
            }

            return 0.0f;
        }

        void UpdateCache()
        {
            if (!m_IsPrefab || !this)
                return;

            m_Bounds = AxisAlignedBox.Empty;

            List<Mesh> lod0Meshes = new List<Mesh>(4);
            List<MeshRenderer> lod0Renderers = new List<MeshRenderer>(4);

            if (TryGetComponent(out LODGroup lodGroup) && lodGroup.lodCount > 0)
            {
                LOD[] lods = lodGroup.GetLODs();
                m_LODVertexCounts = new int[lods.Length];

                for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                {
                    LOD lod = lods[lodIndex];
                    int vertexCount = 0;

                    foreach (Renderer renderer in lod.renderers)
                    {
                        if (renderer is MeshRenderer meshRenderer &&
                            renderer.TryGetComponent(out MeshFilter meshFilter))
                        {
                            Mesh rendererMesh = meshFilter.sharedMesh;
                            if (rendererMesh)
                            {
                                vertexCount += rendererMesh.vertexCount;

                                if (lodIndex == 0)
                                {
                                    m_Bounds += rendererMesh.bounds;
                                    lod0Meshes.Add(rendererMesh);
                                    lod0Renderers.Add(meshRenderer);
                                }
                            }
                        }
                    }

                    m_LODVertexCounts[lodIndex] = vertexCount;
                }
            }
            else
            {
                MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>();
                if (meshRenderers.Length == 0)
                    return;

                m_LODVertexCounts = new int[1];
                int vertexCount = 0;

                foreach (MeshRenderer meshRenderer in meshRenderers)
                {
                    if (!meshRenderer.enabled || !meshRenderer.TryGetComponent(out MeshFilter meshFilter) || !meshFilter.sharedMesh)
                        continue;

                    m_Bounds += meshFilter.sharedMesh.bounds;
                    lod0Meshes.Add(meshFilter.sharedMesh);
                    lod0Renderers.Add(meshRenderer);
                    vertexCount += meshFilter.sharedMesh.vertexCount;
                }

                m_LODVertexCounts[0] = vertexCount;
            }

            if (m_CullingDistance <= 0 && !TryGetComponent(out LODGroup _))
            {
                m_CullingDistance = CalculateMeshRendererMaxDistance();
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

#if UNITY_EDITOR
            m_LOD0MeshCache.Clear();
            m_LOD0MeshCache.AddRange(lod0Meshes);
            m_LowBoundingSphere = new Sphere();
            if (lod0Meshes.Count > 0)
            {
                using Mesh.MeshDataArray meshDataArray = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(lod0Meshes);
                float minX = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxZ = float.MinValue;

                for (int i = 0; i < meshDataArray.Length; i++)
                {
                    Mesh.MeshData meshData = meshDataArray[i];
                    using NativeArray<Vector3> vertices = new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp);
                    meshData.GetVertices(vertices);

                    float3 meshScale = lod0Renderers[i].transform.lossyScale;
                    AxisAlignedBox bounds = AxisAlignedBox.Empty;
                    for (int subMeshIndex = 0; subMeshIndex < meshData.subMeshCount; subMeshIndex++)
                    {
                        SubMeshDescriptor subMesh = meshData.GetSubMesh(subMeshIndex);
                        bounds += subMesh.bounds;
                    }

                    bounds.Extents *= meshScale;

                    // Calculate the bottom 10% of the mesh (configurable?)
                    AxisAlignedBox meshLowerBounds = bounds;
                    meshLowerBounds.Max.y = meshLowerBounds.Min.y + (meshLowerBounds.Max.y - meshLowerBounds.Min.y) * 0.1f;

                    // Iterate over all vertices and find the min/max X and Z values in the lower 10% of the mesh
                    for (int vertexIndex = 0; vertexIndex < vertices.Length; ++vertexIndex)
                    {
                        Vector3 vertex = vertices[vertexIndex] * meshScale;
                        if (vertex.y < meshLowerBounds.Max.y)
                        {
                            minX = math.min(vertex.x, minX);
                            maxX = math.max(vertex.x, maxX);

                            minZ = math.min(vertex.z, minZ);
                            maxZ = math.max(vertex.z, maxZ);
                        }
                    }
                }

                // Store the radius and center of the lowest part of the prototype
                m_LowBoundingSphere.Radius = math.sqrt(math.lengthsq(maxX - minX) + math.lengthsq(maxZ - minZ)) * 0.5f;
                m_LowBoundingSphere.Center = new float3((minX + maxX) * 0.5f, m_Bounds.Min.y, (minZ + maxZ) * 0.5f);
            }
#endif
        }

#if UNITY_EDITOR
        List<Mesh> m_LOD0MeshCache = new List<Mesh>(4);

        internal Mesh.MeshDataArray AcquireLOD0Data_EditorOnly()
        {
            if (m_LOD0MeshCache.Count == 0)
                UpdateCache();

            return UnityEditor.MeshUtility.AcquireReadOnlyMeshData(m_LOD0MeshCache);
        }

        internal bool Validate(out string errorMessage, out UnityEditor.MessageType errorType)
        {
            errorMessage = "";
            errorType = UnityEditor.MessageType.None;

            if (TryGetComponent(out LODGroup lodGroup) && lodGroup.lodCount > 0)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length == 0)
                {
                    errorMessage = "LODGroup has no LODs.";
                    errorType = UnityEditor.MessageType.Error;
                    return false;
                }

                for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                {
                    LOD lod = lods[lodIndex];
                    if (lod.renderers.Length == 0)
                    {
                        errorMessage = $"LOD '{lodIndex}' has no renderers.";
                        errorType = UnityEditor.MessageType.Error;
                        return false;
                    }

                    foreach (Renderer renderer in lod.renderers)
                        if (renderer is MeshRenderer meshRenderer && !ValidateMeshRenderer(meshRenderer, out errorMessage, out errorType))
                            return false;
                }
            }
            else
            {
                MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>();
                if (meshRenderers.Length == 0)
                {
                    errorMessage = "Prototype has no MeshRenderer(s) or LODGroup.";
                    errorType = UnityEditor.MessageType.Error;
                    return false;
                }

                foreach (MeshRenderer meshRenderer in meshRenderers)
                    if (!ValidateMeshRenderer(meshRenderer, out errorMessage, out errorType))
                        return false;
            }

            return true;
        }

        internal static bool ValidateMeshRenderer(MeshRenderer meshRenderer, out string errorMessage, out UnityEditor.MessageType errorType)
        {
            errorMessage = "";
            errorType = UnityEditor.MessageType.None;

            if (!meshRenderer.TryGetComponent(out MeshFilter meshFilter) || !meshFilter.sharedMesh)
            {
                errorMessage = $"Renderer {meshRenderer.name} has no mesh.";
                errorType = UnityEditor.MessageType.Warning;
                return false;
            }

            if (meshRenderer.transform.localPosition != Vector3.zero ||
                meshRenderer.transform.localRotation != Quaternion.identity ||
                meshRenderer.transform.localScale != Vector3.one)
            {
                errorMessage = $"Renderer {meshRenderer.name} has non-identity transform.";
                errorType = UnityEditor.MessageType.Warning;
                return false;
            }

            return true;
        }
#endif
    }
}
