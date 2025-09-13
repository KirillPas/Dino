// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor.Obsolete
{
    [Obsolete]
    [CustomEditor(typeof(FloraContainer))]
    class FloraContainerEditor : UnityEditor.Editor
    {
        void OnEnable()
        {
            FloraContainer[] containers = targets.Cast<FloraContainer>().ToArray();

            foreach (FloraContainer container in containers)
            {
                FloraCell[] cells = container.GetComponentsInChildren<FloraCell>();
                foreach (FloraCell cell in cells)
                    cell.gameObject.hideFlags = HideFlags.None;
            }
        }

        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(s_IsUpgrading))
            {
                if (GUILayout.Button("Upgrade") && target is FloraContainer container)
                    UpgradeContainer(container, this);
            }
        }

        const string k_ProgressBarTitle = "Upgrade Flora Container";
        const int k_MaxCellsPerFrame = 16;
        static bool s_IsUpgrading;

        static async void UpgradeContainer(FloraContainer container, UnityEditor.Editor editor)
        {
            s_IsUpgrading = true;

            EditorUtility.DisplayProgressBar(k_ProgressBarTitle, "Preparing Cells", 0f);
            EnsureLegacyAssetsFolders();
            await Task.Yield();

            PlacementOccluders occluders = new PlacementOccluders(container.gameObject.scene, false, PlacementObjectMask.Default);
            InstancePlacementUtility.BeginPlacementOperation("Upgrade Flora Container");

            FloraPrototype[] prototypes = container.m_Prototypes.ToArray();
            FloraCell[] cells = container.GetComponentsInChildren<FloraCell>();

            for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                FloraCell cell = cells[cellIndex];
                UpgradeCell(cell, prototypes);

                if (cellIndex > 0 && cellIndex % k_MaxCellsPerFrame == 0)
                    await Task.Yield();

                EditorUtility.DisplayProgressBar(k_ProgressBarTitle, "Converting Cells", (cellIndex / (float)cells.Length) * 0.8f);
            }

            InstancePlacementUtility.EndPlacementOperation();
            occluders.Dispose();

            await Task.Yield();
            EditorUtility.DisplayProgressBar(k_ProgressBarTitle, "Moving Legacy Assets", 0.9f);
            MoveUpgradedAssets();

            await Task.Yield();
            EditorUtility.DisplayProgressBar(k_ProgressBarTitle, "Removing Legacy Container", 1f);
            Undo.DestroyObjectImmediate(container.gameObject);

            await Task.Yield();
            EditorUtility.ClearProgressBar();

            s_IsUpgrading = false;
        }

        static void UpgradeCell(FloraCell cell, FloraPrototype[] prototypes)
        {
            if (cell.m_ParentGameObjectCache == null || cell.m_ParentGameObjectCache.m_SerializedParents.Count == 0)
                return;

            FloraInstanceController[] legacyInstanceControllers = cell.GetComponentsInChildren<FloraInstanceController>();
            Dictionary<FloraPrototype, FloraInstanceController> prototypeToController = new Dictionary<FloraPrototype, FloraInstanceController>();
            foreach (FloraInstanceController legacyController in legacyInstanceControllers)
            {
                if (legacyController.Prototype)
                    prototypeToController.Add(legacyController.Prototype, legacyController);
            }

            UnsafeParallelMultiHashMap<FloraParentID, int> parentInstanceIndices = new UnsafeParallelMultiHashMap<FloraParentID, int>(1024, AllocatorManager.Temp);
            UnsafeList<LocalTransform> transforms = new UnsafeList<LocalTransform>(1024, AllocatorManager.Temp);
            List<(FloraParentID, GameObject)> validParents = new List<(FloraParentID, GameObject)>();

            foreach (FloraPrototype legacyPrototype in prototypes)
            {
                if (!prototypeToController.TryGetValue(legacyPrototype, out FloraInstanceController legacyController))
                    continue;

                InstancedPrototype instancedPrototype = UpdatePrototype(legacyPrototype);
                if (!instancedPrototype)
                    continue;

                FloraInstanceCollection legacyInstanceCollection = legacyController.Instances;
                if (legacyInstanceCollection == null || legacyInstanceCollection.Count == 0)
                    continue;

                s_UpgradedDataAssets.Add(legacyInstanceCollection);

                FloraInstanceRenderer instanceRenderer = legacyController.GetComponent<FloraInstanceRenderer>();
                if (instanceRenderer == null)
                    return;

                FloraInstanceData instanceData = instanceRenderer.InstanceData;
                if (instanceData == null)
                    return;

                s_UpgradedDataAssets.Add(instanceData);

                validParents.Clear();
                foreach (FloraParentIdCache.SerializedParentData parent in cell.m_ParentGameObjectCache.m_SerializedParents)
                {
                    if (parent.Id == FloraParentID.Null || parent.GameObject == null)
                        continue;

                    validParents.Add((parent.Id, parent.GameObject));
                }

                if (validParents.Count == 0)
                    continue;

                parentInstanceIndices.Clear();
                for (int i = 0; i < legacyController.InstanceCount; i++)
                {
                    ref readonly FloraInstance instance = ref legacyInstanceCollection[i];
                    if (instance.ParentId == FloraParentID.Null)
                        continue;

                    parentInstanceIndices.Add(instance.ParentId, i);
                }

                float3 cellPosition = cell.transform.position;

                foreach (var (parentId, parent) in validParents)
                {
                    if (!parent.TryGetComponent(out Collider collider))
                        continue;

                    transforms.Clear();

                    AxisAlignedBox transformsBounds = AxisAlignedBox.Empty;

                    foreach (int instanceIndex in parentInstanceIndices.GetValuesForKey(parentId))
                    {
                        ref FloraInstance instance = ref legacyInstanceCollection[instanceIndex];
                        LocalTransform globalTransform = instance.LocalTransform.Translate(cellPosition);
                        transforms.Add(globalTransform);
                        transformsBounds += globalTransform.Position;
                    }

                    if (transforms.Length > 0)
                    {
                        InstancePlacementUtility.PlaceInstances(instancedPrototype, collider.transform, transforms.AsReadOnlySpan(), transformsBounds);
                    }
                }
            }
        }

        static InstancedPrototype UpdatePrototype(FloraPrototype legacyPrototype)
        {
            GameObject prefab = legacyPrototype.ModelPrefab;
            if (!prefab)
                return null;

            if (!prefab.TryGetComponent(out InstancedPrototype instancePrototype))
            {
                instancePrototype = Undo.AddComponent<InstancedPrototype>(prefab);
                instancePrototype.CullingDistance = legacyPrototype.MaxRenderDistance;
                instancePrototype.SampleLightProbes = legacyPrototype.CalculateInterpolatedLightProbes;
                instancePrototype.SampleLightProbesOffset = legacyPrototype.InterpolatedLightProbeOffset;
                instancePrototype.CreateLinkedObject = legacyPrototype.SpawnPrefabInstances;
                instancePrototype.LinkedObjectContributesToGI = legacyPrototype.PrefabInstancesContributeGI;
                UpgradePlacementSettings(legacyPrototype.PlacementSettings, instancePrototype.PlacementSettings);
            }

            s_UpgradedPrototypeAssets.Add(legacyPrototype);

            return instancePrototype;
        }

        static void UpgradePlacementSettings(in FloraPlacementSettings legacyPlacementSettings, InstancePlacementSettings newPlacementSettings)
        {
            newPlacementSettings.Density = legacyPlacementSettings.Density;
            newPlacementSettings.Radius = legacyPlacementSettings.Radius;
            newPlacementSettings.OverrideSinglePlacementRadius = legacyPlacementSettings.OverrideSingleInstanceModeRadius;
            newPlacementSettings.SinglePlacementRadius = legacyPlacementSettings.SingleInstanceModeRadius;

            newPlacementSettings.ScalingMode = (InstanceScalingMode)legacyPlacementSettings.ScalingMode;
            newPlacementSettings.ScaleX = legacyPlacementSettings.ScaleX;
            newPlacementSettings.ScaleY = legacyPlacementSettings.ScaleY;
            newPlacementSettings.ScaleZ = legacyPlacementSettings.ScaleZ;

            newPlacementSettings.VerticalOffset = legacyPlacementSettings.VerticalOffset;
            newPlacementSettings.RandomizeYaw = legacyPlacementSettings.RandomizeYaw;
            newPlacementSettings.RandomPitchAngle = legacyPlacementSettings.RandomPitchAngle;
            newPlacementSettings.AlignToSurface = legacyPlacementSettings.AlignToNormal;
            newPlacementSettings.AlignToSurfaceMaxAngle = legacyPlacementSettings.MaximumAlignmentAngle;
            newPlacementSettings.AverageNormal = legacyPlacementSettings.AverageNormal;
            newPlacementSettings.AverageNormalSingleComponent = legacyPlacementSettings.AverageNormalSingleComponent;
            newPlacementSettings.AverageNormalSampleCount = legacyPlacementSettings.AverageNormalSampleCount;

            newPlacementSettings.SlopeMask = legacyPlacementSettings.SlopeAngleRange;
            newPlacementSettings.HeightMask = legacyPlacementSettings.HeightRange;

            newPlacementSettings.CheckWorldCollisions = legacyPlacementSettings.CheckCollisionWithWorld;
            newPlacementSettings.CheckColliderOverhang = legacyPlacementSettings.CollisionCheckOverhangs;
            newPlacementSettings.CollisionLayerMask = legacyPlacementSettings.CollisionMask;
            newPlacementSettings.CollisionBoundsScale = legacyPlacementSettings.CollisionScale;
        }

        static HashSet<FloraPrototype> s_UpgradedPrototypeAssets = new HashSet<FloraPrototype>();
        static HashSet<ScriptableObject> s_UpgradedDataAssets = new HashSet<ScriptableObject>();

        const string k_LegacyAssetsFolderName = "Flora Legacy Assets";
        const string k_LegacyAssetsPrototypesSubfolderName = "Prototypes";
        const string k_LegacyAssetsInstanceDataSubfolderName = "InstanceData";
        static readonly string k_LegacyAssetsPath = Path.Combine("Assets", k_LegacyAssetsFolderName);

        static void MoveUpgradedAssets()
        {
            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (FloraPrototype prototypeAsset in s_UpgradedPrototypeAssets)
                    MoveUpgradedAsset(prototypeAsset, k_LegacyAssetsPrototypesSubfolderName);

                foreach (ScriptableObject dataAsset in s_UpgradedDataAssets)
                    MoveUpgradedAsset(dataAsset, k_LegacyAssetsInstanceDataSubfolderName);

                s_UpgradedPrototypeAssets.Clear();
                s_UpgradedDataAssets.Clear();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        static void MoveUpgradedAsset(ScriptableObject asset, string legacySubfolderFolderName)
        {
            string finalLegacyFolderPath = Path.Combine(k_LegacyAssetsPath, legacySubfolderFolderName);
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
                return;

            string assetExtension = Path.GetExtension(assetPath);
            string assetFilename = asset.name;
            string newPath = Path.Combine(finalLegacyFolderPath, $"{assetFilename}{assetExtension}");

            if (!string.IsNullOrEmpty(newPath) && !newPath.Equals(assetPath))
            {
                newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
                string result = AssetDatabase.MoveAsset(assetPath, newPath);

                if (!string.IsNullOrEmpty(result))
                    Debug.LogError($"FloraContainerUpgrade: Failed to move asset: {result}");
            }
        }

        static void EnsureLegacyAssetsFolders()
        {
            if (!AssetDatabase.IsValidFolder(k_LegacyAssetsPath))
            {
                if (AssetDatabase.CreateFolder("Assets", k_LegacyAssetsFolderName) == null)
                {
                    Debug.LogError("Failed to create main legacy asset folder.");
                    return;
                }

                AssetDatabase.Refresh();
            }

            EnsureLegacyAssetsSubfolder(k_LegacyAssetsInstanceDataSubfolderName);
            EnsureLegacyAssetsSubfolder(k_LegacyAssetsPrototypesSubfolderName);
        }

        static void EnsureLegacyAssetsSubfolder(string subfolderName)
        {
            string finalLegacyFolderPath = Path.Combine(k_LegacyAssetsPath, subfolderName);
            if (!AssetDatabase.IsValidFolder(finalLegacyFolderPath))
            {
                if (AssetDatabase.CreateFolder(k_LegacyAssetsPath, subfolderName) == null)
                {
                    Debug.LogError("Failed to create subfolder for legacy assets.");
                    return;
                }

                AssetDatabase.Refresh();
            }
        }
    }
}
