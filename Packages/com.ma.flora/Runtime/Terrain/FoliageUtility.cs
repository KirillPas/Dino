// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace MA.Flora
{
    static class FoliageUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValidTerrain(Terrain terrain)
            => terrain != null && terrain.isActiveAndEnabled && terrain.terrainData != null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasHeightChanges(TerrainChangedFlags flags)
            => (flags & TerrainChangedFlags.FlushEverythingImmediately) != 0 ||
               (flags & TerrainChangedFlags.DelayedHeightmapUpdate) != 0 ||
               (flags & TerrainChangedFlags.Heightmap) != 0 ||
               (flags & TerrainChangedFlags.HeightmapResolution) != 0 ||
               (flags & TerrainChangedFlags.Holes) != 0 ||
               (flags & TerrainChangedFlags.DelayedHolesUpdate) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasTreeChanges(TerrainChangedFlags flags)
            => (flags & TerrainChangedFlags.FlushEverythingImmediately) != 0 ||
               (flags & TerrainChangedFlags.TreeInstances) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasDetailChanges(TerrainChangedFlags flags)
            => (flags & TerrainChangedFlags.FlushEverythingImmediately) != 0 ||
               (flags & TerrainChangedFlags.RemoveDirtyDetailsImmediately) != 0;

        internal static int CalculatePrototypeHashCode(DetailPrototype prototype)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (prototype.prototype == null ? 0 : prototype.prototype.GetInstanceID());
                hash = hash * 23 + prototype.minHeight.GetHashCode();
                hash = hash * 23 + prototype.maxHeight.GetHashCode();
                hash = hash * 23 + prototype.minWidth.GetHashCode();
                hash = hash * 23 + prototype.maxWidth.GetHashCode();
                hash = hash * 23 + prototype.renderMode.GetHashCode();
                hash = hash * 23 + prototype.noiseSpread.GetHashCode();
                hash = hash * 23 + prototype.noiseSeed.GetHashCode();
#if UNITY_2022_2_OR_NEWER
                hash = hash * 23 + prototype.density.GetHashCode();
                hash = hash * 23 + prototype.positionJitter.GetHashCode();
                hash = hash * 23 + prototype.targetCoverage.GetHashCode();
                hash = hash * 23 + prototype.alignToGround.GetHashCode();
#endif
                hash = hash * 23 + prototype.holeEdgePadding.GetHashCode();
                return hash;
            }
        }

        internal static int CalculatePrototypeHashCode(TreePrototype prototype)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (prototype.prefab == null ? 0 : prototype.prefab.GetInstanceID());
                hash = hash * 23 + prototype.bendFactor.GetHashCode();
                return hash;
            }
        }

        internal static bool IsSupportedPrototype(TreePrototype treePrototype)
        {
            if (treePrototype == null)
                return false;
            if (treePrototype.prefab == null)
                return false;
#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfImmutablePrefab(treePrototype.prefab))
                return false;
#endif
            return true;
        }

        internal static bool IsSupportedPrototype(DetailPrototype detailPrototype)
        {
            if (detailPrototype == null)
                return false;
            if (!detailPrototype.usePrototypeMesh)
                return false;
            if (detailPrototype.prototype == null)
                return false;
            if (detailPrototype.prototype.TryGetComponent(out LODGroup group))
                return false;
#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfImmutablePrefab(detailPrototype.prototype))
                return false;
#endif
            return true;
        }

        internal static GameObject GetUnityCompatibleDetailPrefab(DetailPrototype detailPrototype)
            => IsSupportedPrototype(detailPrototype) ? detailPrototype.prototype : GetUnityCompatibleDetailPrefab(detailPrototype.prototype);

        internal static GameObject GetUnityCompatibleDetailPrefab(GameObject prototype)
        {
            if (prototype == null)
                return null;

            if (!prototype.TryGetComponent(out LODGroup group))
                return prototype;

            LOD[] lods = group.GetLODs();
            foreach (LOD lod in lods)
            {
                foreach (Renderer renderer in lod.renderers)
                {
                    if (renderer != null)
                        return renderer.gameObject;
                }
            }
            return null;
        }

        internal static InstancedPrototype GetInstancedPrototype(DetailPrototype detailPrototype, float defaultDistance = 0f)
        {
            GameObject prototypeObject = GetPrototypeRoot(detailPrototype);
            if (prototypeObject == null)
                return null;

            if (prototypeObject.TryGetComponent(out InstancedPrototype instancePrototype))
                return instancePrototype;

            InstancedPrototype prototype = EnsurePrefabHasPrototype(prototypeObject);
            prototype.CullingDistance = defaultDistance;
            return prototype;
        }

        internal static InstancedPrototype GetInstancedPrototype(TreePrototype treePrototype)
        {
            GameObject prototypeObject = GetPrototypeRoot(treePrototype);
            if (prototypeObject == null)
                return null;

            if (prototypeObject.TryGetComponent(out InstancedPrototype instancePrototype))
                return instancePrototype;

            return EnsurePrefabHasPrototype(prototypeObject);
        }

        static InstancedPrototype EnsurePrefabHasPrototype(GameObject prefab)
        {
            if (!prefab.TryGetComponent(out InstancedPrototype prototype))
            {
#if UNITY_EDITOR
                string assetPath = AssetDatabase.GetAssetPath(prefab);
                using (PrefabUtility.EditPrefabContentsScope editingScope = new PrefabUtility.EditPrefabContentsScope(assetPath))
                {
                    Undo.AddComponent<InstancedPrototype>(editingScope.prefabContentsRoot);
                }

                prefab = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(prefab, assetPath);
#endif
                prototype = prefab.GetComponent<InstancedPrototype>();
            }

            return prototype;
        }

        internal static GameObject CreatePrefabIfImmutable(GameObject gameObject)
        {
#if UNITY_EDITOR
            if (gameObject != null && UnityEditor.PrefabUtility.IsPartOfImmutablePrefab(gameObject))
            {
                string originalPath = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);

                string outputDirectory;
                if (originalPath.IndexOf("Assets/", StringComparison.InvariantCultureIgnoreCase) == -1)
                {
                    outputDirectory = "Assets/Flora/Prototypes";
                }
                else
                {
                    outputDirectory = originalPath[..originalPath.LastIndexOf('/')];
                    if (outputDirectory.Length > 0 && outputDirectory[^1] != '/')
                        outputDirectory += "/";
                }

                string outputPrefabPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(outputDirectory + "/" + gameObject.name + ".prefab");
                gameObject = UnityEditor.PrefabUtility.SaveAsPrefabAsset(gameObject, outputPrefabPath);
                AssetDatabase.Refresh();
            }
#endif

            return gameObject;
        }

        internal static GameObject GetPrototypeRoot(GameObject prefab)
            => prefab == null ? null : prefab.transform.root.gameObject;

        internal static GameObject GetPrototypeRoot(DetailPrototype prototype)
            => prototype.prototype == null ? null : prototype.prototype.transform.root.gameObject;

        internal static GameObject GetPrototypeRoot(TreePrototype prototype)
            => prototype.prefab == null ? null : prototype.prefab.transform.root.gameObject;
    }
}
