// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Collections.Unsafe;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace MA.Flora
{
    /// <summary>
    /// Determines how instances of terrain foliage are loaded.
    /// </summary>
    public enum TerrainFoliageLoadMode
    {
        /// <summary>Instances are always loaded.</summary>
        AlwaysLoaded,
        /// <summary>Instances are loaded based on the terrain's render distance.</summary>
        OnDemand,
    }

    /// <summary>
    /// Determines how instances of terrain foliage are culled.
    /// </summary>
    public enum TerrainFoliageCullMode
    {
        /// <summary>Instances are culled based on the instance prototype's render distance.</summary>
        FromPrototype,
        /// <summary>Instances are culled based on the distance specified by the component.</summary>
        FromTerrain,
    }

    /// <summary>
    /// Maintains and updates instances of trees and details on a terrain.
    /// </summary>
    /// <remarks>
    /// Loads instances of trees and details from the terrain data and updates them as needed.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Terrain))]
    [AddComponentMenu("Flora/Instanced Terrain Foliage")]
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedTerrainFoliage Icon.png")]
    [HelpURL("https://flora.magneticarcade.com/components/instanced-terrain-foliage")]
    public sealed class InstancedTerrainFoliage : MonoBehaviour
    {
        [SerializeField] TerrainFoliageLoadMode m_DetailsLoadMode = TerrainFoliageLoadMode.OnDemand;
        [FormerlySerializedAs("m_DetailsDistanceMode")] [SerializeField] TerrainFoliageCullMode m_DetailsCullMode = TerrainFoliageCullMode.FromTerrain;

        [SerializeField] TerrainFoliageLoadMode m_TreesLoadMode = TerrainFoliageLoadMode.OnDemand;
        [FormerlySerializedAs("m_TreesDistanceMode")] [SerializeField] TerrainFoliageCullMode m_TreesCullMode = TerrainFoliageCullMode.FromTerrain;
        [FormerlySerializedAs("m_TreePatchesPerEdge")] [SerializeField, Range(2, 64)] int m_TreeGridSize = 8;

        Terrain m_Terrain;

        FoliageScheduler m_Scheduler;
        FoliageTreeManager m_TreeManager = new FoliageTreeManager();
        FoliageDetailManager m_DetailManager = new FoliageDetailManager();

        // --- Events ---

        /// <summary>Occurs when the terrain data has changed.</summary>
        public static event Action<InstancedTerrainFoliage, TerrainChangedFlags> TerrainInstancesChanged;

        // --- Properties ---

        /// <summary>The terrain component that contains the foliage instances.</summary>
        public Terrain Terrain => m_Terrain;

        /// <summary>The terrain data asset that contains the foliage instances.</summary>
        public TerrainData TerrainData => m_Terrain.terrainData;

        /// <summary>Determines how tree instances are loaded from the terrain data.</summary>
        public TerrainFoliageLoadMode TreesLoadMode
        {
            get => m_TreesLoadMode;
            set
            {
                if (m_TreesLoadMode != value)
                {
                    m_TreesLoadMode = value;
                    m_TreeManager.MarkDirty();
                }
            }
        }

        /// <summary>Determines how tree instances choose their render distance.</summary>
        public TerrainFoliageCullMode TreesCullMode
        {
            get => m_TreesCullMode;
            set
            {
                if (m_TreesCullMode != value)
                {
                    m_TreesCullMode = value;
                    m_TreeManager.MarkDirty();
                }
            }
        }

        /// <summary>The custom render distance for tree instances.</summary>
        public float TreesOverrideRenderDistance
        {
            get => m_Terrain.treeDistance;
            set
            {
                m_Terrain.treeDistance = value;
                m_TreeManager.MarkDirty();
            }
        }

        /// <summary>The number of patches per edge of the terrain for tree instances.</summary>
        /// <remarks>Determines how trees are split into patches for rendering and streaming.</remarks>
        public int TreeGridSize
        {
            get => m_TreeGridSize;
            set
            {
                if (m_TreeGridSize != value)
                {
                    m_TreeGridSize = value;
                    m_TreeManager.MarkDirty();
                }
            }
        }

        /// <summary>Determines how detail instances are loaded from the terrain data.</summary>
        public TerrainFoliageLoadMode DetailsLoadMode
        {
            get => m_DetailsLoadMode;
            set
            {
                if (m_DetailsLoadMode != value)
                {
                    m_DetailsLoadMode = value;
                    m_DetailManager.MarkDirty();
                }
            }
        }

        /// <summary>Determines how detail instances choose their render distance.</summary>
        public TerrainFoliageCullMode DetailsCullMode
        {
            get => m_DetailsCullMode;
            set
            {
                if (m_DetailsCullMode != value)
                {
                    m_DetailsCullMode = value;
                    m_DetailManager.MarkDirty();
                }
            }
        }

        /// <summary>The custom render distance for detail instances.</summary>
        public float DetailsOverrideRenderDistance
        {
            get => m_Terrain.detailObjectDistance;
            set
            {
                m_Terrain.detailObjectDistance = value;
                m_DetailManager.MarkDirty();
            }
        }

        // --- Methods ---

        /// <summary>Sets the instances of trees and details as dirty (requests and update from the terrain data).</summary>
        public void MarkAllInstancesDirty()
        {
            m_TreeManager.MarkDirty();
            m_DetailManager.MarkDirty();
        }

        /// <summary>Marks a region of trees as dirty, will update instances in the region.</summary>
        /// <param name="region">The region to mark as dirty. The region is in world space.</param>
        public void MarkTreeRegionDirty(AxisAlignedBox2D region)
        {
            float3 terrainPosition = m_Terrain.transform.position;
            region.Min -= terrainPosition.xz;
            m_TreeManager.MarkRegionDirty(region);
        }

        /// <summary>Marks a region of details as dirty, will update instances in the region.</summary>
        /// <param name="region">The region to mark as dirty. The region is in world space.</param>
        public void MarkDetailRegionDirty(AxisAlignedBox2D region)
        {
            float3 terrainPosition = m_Terrain.transform.position;
            region.Min -= terrainPosition.xz;
            m_DetailManager.MarkRegionDirty(region);
        }

        /// <summary>Marks the transform of the terrain as dirty, will move instances to the new position of the terrain.</summary>
        public void MarkTransformDirty()
        {
            m_TreeManager.MarkRenderStateDirty();
            m_DetailManager.MarkRenderStateDirty();
        }

        // --- Private ---

        void OnEnable()
        {
            TryGetComponent(out m_Terrain);
            if (m_Terrain == null)
                return;

            if (m_Terrain.terrainData == null)
            {
                Debug.LogError($"{nameof(InstancedTerrainFoliage)}: A TerrainData asset must be assigned to the Terrain component.");
                enabled = false;
                return;
            }

            FoliageJobManager.Initialize();

            m_Terrain.drawTreesAndFoliage = false;
            m_Scheduler = new FoliageScheduler(Allocator.Persistent);
            FoliageJobManager.Register(m_Scheduler);

            m_TreeManager.Initialize(new FoliageTreeProvider(this), m_Scheduler);
            m_DetailManager.Initialize(new FoliageDetailProvider(this), m_Scheduler);

            TerrainCallbacks.heightmapChanged += OnHeightmapChanged;

            InstancingSystem.EnsureActive();
            InstancingSystem.PostFrameUpdate += UpdateFoliage;

#if UNITY_EDITOR
            EditorTransformTracker.Track(transform, OnTransformHierarchyChanged);
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorTransformTracker.UnTrack(transform);
#endif

            m_Terrain.drawTreesAndFoliage = true;

            FoliageJobManager.Unregister(m_Scheduler);
            m_Scheduler.Dispose();
            m_TreeManager.Dispose();
            m_DetailManager.Dispose();

            if (FoliageJobManager.IsEmpty)
                FoliageJobManager.Shutdown();

            TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;

            InstancingSystem.ShutdownIfEmpty();
            InstancingSystem.PostFrameUpdate -= UpdateFoliage;
        }

        void OnTransformHierarchyChanged(Transform transform)
        {
            if (transform)
                MarkTransformDirty();
        }

        void OnTerrainChanged(TerrainChangedFlags flags)
        {
            if ((flags & TerrainChangedFlags.DelayedHeightmapUpdate) != 0)
                return; // Ignore delayed heightmap updates, as the user is generally painting

            if (FoliageUtility.HasTreeChanges(flags))
                m_TreeManager.MarkDirty();
            if (FoliageUtility.HasDetailChanges(flags))
                m_DetailManager.MarkDirty();

            TerrainInstancesChanged?.Invoke(this, flags);
        }

        void OnHeightmapChanged(Terrain terrain, RectInt region, bool didSync)
        {
            if (terrain != m_Terrain)
                return;

            TerrainData terrainData = terrain.terrainData;
            float3 terrainSize = terrainData.size;

            float heightmapScale = terrainSize.x / terrainData.heightmapResolution;
            float2 regionMin2D  = new float2(region.x, region.y) * heightmapScale;
            float2 regionSize2D = new float2(region.width, region.height) * heightmapScale;
            AxisAlignedBox2D regionBounds = new AxisAlignedBox2D(regionMin2D, regionMin2D + regionSize2D);

            m_TreeManager.MarkRegionDirty(regionBounds);
            m_DetailManager.MarkRegionDirty(regionBounds);
        }

        void UpdateFoliage()
        {
            float3 position = m_Terrain.transform.position;
            float3 size = m_Terrain.terrainData.size;
            AxisAlignedBox worldBounds = new AxisAlignedBox(position, size);

            if (GetClosestStreamingSource(worldBounds.Center, out FoliageStreamingSource source))
            {
                m_TreeManager.NextFrame(source);
                m_DetailManager.NextFrame(source);
            }
        }

        internal static bool GetClosestStreamingSource(float3 point, out FoliageStreamingSource source)
        {
            source = default;

            if (!InstancingSystem.IsActive())
                return false;

            ref readonly InstancedCameraManager cameraManager = ref InstancingSystem.Instance.Context.CameraManager;
            UnsafeIndirectList<InstancedCameraID> cameraIDs = cameraManager.PrevRenderedCameraIDs;
            if (cameraIDs.Length == 0)
                return false;

            ref readonly InstancedCameraArrays cameraArrays = ref InstancingSystem.Instance.Context.CameraManager.Data;
            float closestDistanceSq = float.MaxValue;

            for (int i = 0; i < cameraIDs.Length; i++)
            {
                InstancedCameraID cameraID = cameraIDs[i];
                if (!InstancingSystem.Instance.Context.CameraManager.Exists(cameraID))
                    continue;

                InstancedCameraFlags flags = cameraArrays.Culling[cameraID].Flags;
                bool validStreamingCamera = flags.IsPersistentCamera();
#if UNITY_EDITOR
                validStreamingCamera |= flags.IsSceneViewCamera();
#endif
                if (!validStreamingCamera)
                    continue;

                float3 origin = cameraArrays.LOD[cameraID].Origin;
                float distanceSq = math.distancesq(origin, point);
                if (distanceSq < closestDistanceSq)
                {
                    closestDistanceSq = distanceSq;
                    source.Center = origin;
                    source.MaxDistance = cameraArrays.Culling[cameraID].FarClipPlane;
                    source.FixedDistanceMoved = cameraArrays.Position[cameraID].FixedDistanceMoved;
                }
            }

            return closestDistanceSq < float.MaxValue;
        }
    }
}
