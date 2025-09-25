// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Core;
using MA.Flora.Rendering;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Random = Unity.Mathematics.Random;

namespace MA.Flora.Editor
{
    static class InstancePlacementUtility
    {
        // --- Record ---

        public static readonly HashSet<InstancedMeshContainer> ModifiedContainers = new HashSet<InstancedMeshContainer>();
        static bool s_Editing;
        static string s_EditUndoName;
        static int s_EditUndoGroupID;

        public static void BeginPlacementOperation(string undoName)
        {
            if (!s_Editing)
            {
                s_Editing = true;
                InstancingSystem.DisableAutoBuildTrees = true;
                s_EditUndoName = undoName;

                ModifiedContainers.Clear();

                Undo.IncrementCurrentGroup();
                s_EditUndoGroupID = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
            }
        }

        public static void RecordForModify(InstancedMeshContainer container, bool withUndo = true)
        {
            if (container == null || container.Prototype == null)
                return;

            if (s_Editing)
            {
                if (ModifiedContainers.Add(container))
                {
                    if (withUndo)
                    {
                        if (container.HasLinkedObjects)
                        {
                            UndoUtility.RegisterFullObjectHierarchyUndo(container.gameObject, s_EditUndoName);
                        }
                        else
                        {
                            UndoUtility.RecordObject(container, s_EditUndoName);
                        }
                    }
                }
            }
        }

        public static void EndPlacementOperation()
        {
            if (s_Editing)
            {
                s_Editing = false;
                InstancingSystem.DisableAutoBuildTrees = false;
                Undo.CollapseUndoOperations(s_EditUndoGroupID);

                foreach (InstancedMeshContainer container in ModifiedContainers)
                    InstancingSystem.ForceRebuildCullingTree(container.InstancedRendererID);

                ModifiedContainers.Clear();
            }
        }

        // --- Placement ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PhysicsScene GetActivePhysicsScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            PrefabStage currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            PhysicsScene physicsScene = currentPrefabStage ? currentPrefabStage.scene.GetPhysicsScene() : activeScene.GetPhysicsScene();
            if (!physicsScene.IsValid()) physicsScene = Physics.defaultPhysicsScene;
            return physicsScene;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeDensity(InstancePlacementSettings placementSettings)
        {
            float originalDensity = placementSettings.Density;
            if (placementSettings.Radius <= 0.0f)
                return originalDensity;

            // Area of the hexagon that circumscribes a circle
            float adjustedArea = (3.0f * math.sqrt(3.0f) / 2.0f) * placementSettings.Radius * placementSettings.Radius;
            float adjustedDensity = (10.0f * 10.0f) / adjustedArea;
            return originalDensity <= 0.0f ? adjustedDensity : math.min(adjustedDensity, originalDensity);
        }

        static HashSet<InstancedMeshContainer> s_ContainerSet = new HashSet<InstancedMeshContainer>();
        static ObjectPool<CellInstances> s_CellInstancesPool = new ObjectPool<CellInstances>(c => c.Reset(), null, false);

        class CellInstances
        {
            public AxisAlignedBox Bounds = AxisAlignedBox.Empty;
            public List<LocalTransform> Instances = ListPool<LocalTransform>.Get();

            public static CellInstances Get() => s_CellInstancesPool.Get();
            public static void Release(CellInstances cellInstances) => s_CellInstancesPool.Release(cellInstances);

            public void Add(LocalTransform instance)
            {
                Bounds += instance.Position;
                Instances.Add(instance);
            }

            public void Reset()
            {
                Bounds = AxisAlignedBox.Empty;
                Instances.Clear();
            }
        }

        static Dictionary<int3, CellInstances> s_CellInstancesDict = new Dictionary<int3, CellInstances>();

        static void ResetCellInstances()
        {
            foreach (var (_, cellInstances) in s_CellInstancesDict)
                CellInstances.Release(cellInstances);

            s_CellInstancesDict.Clear();
        }

        public static InstancedMeshContainer[] PlaceInstances(InstancedPrototype prototype, Transform parent, ReadOnlySpan<LocalTransform> instances, AxisAlignedBox instancesBounds, PlacementOccluders occluders = null)
        {
            if (parent == null || instances.Length == 0)
                return Array.Empty<InstancedMeshContainer>();

            AxisAlignedBox prototypeBounds = prototype.Bounds;

            if (instancesBounds.IsEmpty && instances.Length > 0)
            {
                foreach (LocalTransform instance in instances)
                    instancesBounds += prototypeBounds.TransformBy(instance);

                if (instancesBounds.IsEmpty)
                    return Array.Empty<InstancedMeshContainer>();
            }

            EditorSpatialHash spatialHash = EditorSpatialHash.Instance;
            bool needsToExcludeColliders = occluders != null && prototype.CreateLinkedObject;

            s_ContainerSet.Clear();
            ResetCellInstances();

            foreach (LocalTransform instance in instances)
            {
                int3 cell = InstancedMeshContainer.GetEditorCell(instance.Position);
                if (!s_CellInstancesDict.ContainsKey(cell))
                    s_CellInstancesDict[cell] = CellInstances.Get();

                s_CellInstancesDict[cell].Add(instance);
            }

            foreach (var (cell, cellInstances) in s_CellInstancesDict)
            {
                bool didAdd = false;
                spatialHash.ForEachIntersectingContainerBreakable(cellInstances.Bounds, container =>
                {
                    container.AddInstances(cellInstances.Instances.AsReadOnlySpan(), Space.World);
                    didAdd = true;
                    return true;
                });

                if (!didAdd)
                {
                    InstancedMeshContainer container = CreateContainer(prototype, parent, cellInstances.Bounds.Center);
                    RecordForModify(container);
                    container.AddInstances(cellInstances.Instances.AsReadOnlySpan(), Space.World);
                    s_ContainerSet.Add(container);
                }
            }

            InstancedMeshContainer[] containers = s_ContainerSet.ToArray();
            if (needsToExcludeColliders)
            {
                foreach (InstancedMeshContainer container in containers)
                    ExcludeLinkedObjects(container, occluders);
            }

            return containers;
        }

        static List<Collider> s_ColliderBuffer = new List<Collider>();

        static void ExcludeLinkedObjects(InstancedMeshContainer container, PlacementOccluders occluders)
        {
            s_ColliderBuffer.Clear();
            container.GetComponentsInChildren(s_ColliderBuffer);
            if (s_ColliderBuffer.Count == 0)
                return;

            occluders.ExcludeColliders(s_ColliderBuffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void PlaceInstance(InstancedPrototype prototype, Transform parent, LocalTransform instance)
            => PlaceInstances(prototype, parent, new ReadOnlySpan<LocalTransform>(&instance, 1), prototype.Bounds.TransformBy(instance));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InstancedMeshContainer CreateContainer(InstancedPrototype prototype, Transform parent, Vector3 position, string id = null)
        {
            InstancedMeshContainer container = CreateContainerWithoutUndo(prototype, parent, position, id);
            Undo.RegisterCreatedObjectUndo(container.gameObject, "Create Instanced Mesh Container");
            return container;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InstancedMeshContainer CreateContainerWithoutUndo(InstancedPrototype prototype, Transform parent, Vector3 position, string id = null)
        {
            string name = string.IsNullOrEmpty(id) ? prototype.name : $"{prototype.name} ({id})";

            GameObject imcGameObject = new GameObject(name, typeof(InstancedMeshContainer));
            imcGameObject.isStatic = true;
            imcGameObject.layer = parent ? parent.gameObject.layer : 0;
            imcGameObject.transform.position = position;
            imcGameObject.transform.SetParent(parent, true);

            InstancedMeshContainer container = imcGameObject.GetComponent<InstancedMeshContainer>();
            container.Prototype = prototype;
            return container;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetCellNameID(int3 cell)
        {
            char xSign = cell.x >= 0 ? 'P' : 'N';
            char ySign = cell.y >= 0 ? 'P' : 'N';
            char zSign = cell.z >= 0 ? 'P' : 'N';

            string xString = math.abs(cell.x).ToString("00");
            string yString = math.abs(cell.y).ToString("00");
            string zString = math.abs(cell.z).ToString("00");

            return $"{xSign}{xString}{ySign}{yString}{zSign}{zString}";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetCellNameID(int2 cell) => $"{cell.x:000}{cell.y:000}";

        // --- Placement Validation ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPlaceTransform(InstancedPrototype prototype, PhysicsScene physicsScene, PlacementHit hit, ref Random random, out LocalTransform transform, out float verticalOffset)
        {
            InstancePlacementSettings placementSettings = prototype.PlacementSettings;

            transform = LocalTransform.FromPosition(hit.Point);
            transform = RandomizeScale(transform, placementSettings, ref random);

            if (placementSettings.RandomizeYaw)
                transform = RandomizeYaw(transform, ref random);

            if (placementSettings.AverageNormal)
                AverageHitNormal(ref hit, physicsScene, transform.Position, placementSettings.CollisionLayerMask,
                    placementSettings.AverageNormalSampleCount, placementSettings.AverageNormalSingleComponent, prototype.LowBoundingSphere.Radius);

            if (placementSettings.AlignToSurface)
                transform = AlignToSurface(transform, hit.Normal, placementSettings.AlignToSurfaceMaxAngle);

            verticalOffset = placementSettings.VerticalOffset.Interpolate(random.NextFloat());
            transform.Position = transform.TransformPoint(new float3(0, verticalOffset, 0));

            if (physicsScene.IsValid())
            {
                if (placementSettings.CheckWorldCollisions)
                {
                    AxisAlignedBox collisionBounds = prototype.Bounds.TransformBy(transform);
                    collisionBounds.Size *= placementSettings.CollisionBoundsScale;

                    if (!CheckWorld(transform, collisionBounds, physicsScene, placementSettings.CollisionLayerMask))
                        return false;
                }

                if (placementSettings.CheckColliderOverhang &&
                    !CheckOverhang(transform, hit, physicsScene, placementSettings.CollisionLayerMask, placementSettings.AlignToSurface, verticalOffset, prototype.LowBoundingSphere))
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPlaceTransform(InstancedPrototype prototype, PhysicsScene physicsScene, PlacementHit hit, ref Random random, out LocalTransform transform)
            => TryPlaceTransform(prototype, physicsScene, hit, ref random, out transform, out float _);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidPlacementHit(
            InstancePlacementSettings placementSettings,
            in PlacementHit hit,
            float radius,
            ReadOnlySpan<InstancedMeshContainer> containers,
            InstancePlacementHash<int> placementHash,
            UnsafeIndirectList<float3> placementPositions)
        {
            if (hit.ColliderInstanceID == 0)
                return false;

            if (!IsValidHeight(hit.Point, placementSettings.HeightMask))
                return false;

            if (!IsValidSlope(hit.Normal, placementSettings.SlopeMask))
                return false;

            if (radius > 0.0f)
            {
                Sphere sphere = new Sphere(hit.Point, radius);
                float radiusSq = math.lengthsq(radius);

                if (containers != null)
                {
                    foreach (InstancedMeshContainer container in containers)
                    {
                        if (container && container.AnyInstancesInsideSphere(sphere, Space.World))
                            return false;
                    }
                }

                if (placementHash.IsCreated)
                {
                    using NativeArray<int> placedIndicesInBounds = placementHash.GetInstancesInsideBounds(sphere.Bounds, Allocator.Temp);
                    foreach (int index in placedIndicesInBounds)
                    {
                        if (math.lengthsq(placementPositions[index] - (float3)hit.Point) < radiusSq)
                            return false;
                    }

                    placementHash.AddInstance(hit.Point, placementPositions.Length);
                    placementPositions.Add(hit.Point);
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidPlacementHit(
            InstancePlacementSettings placementSettings, in PlacementHit hit, float radius, List<InstancedMeshContainer> containers,
            InstancePlacementHash<int> placementHash, UnsafeIndirectList<float3> placementPositions)
            => IsValidPlacementHit(placementSettings, hit, radius, containers.AsSpan(), placementHash, placementPositions);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidPlacementHit(InstancePlacementSettings placementSettings, in PlacementHit hit, float radius, List<InstancedMeshContainer> containers)
            => IsValidPlacementHit(placementSettings, hit, radius, containers.AsSpan(), default, default);

        static InstancedMeshContainer[] s_SingleContainer = new InstancedMeshContainer[1];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidPlacementHit(
            InstancePlacementSettings placementSettings, in PlacementHit hit, float radius, InstancedMeshContainer container,
            InstancePlacementHash<int> placementHash, UnsafeIndirectList<float3> placementPositions)
        {
            s_SingleContainer[0] = container;
            return IsValidPlacementHit(placementSettings, hit, radius, s_SingleContainer, placementHash, placementPositions);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidPlacementHit(InstancePlacementSettings placementSettings, in PlacementHit hit, float radius, InstancedMeshContainer container)
            => IsValidPlacementHit(placementSettings, hit, radius, container, default, default);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LocalTransform RandomizeScale(in LocalTransform transform, Interval rangeX, Interval rangeY, Interval rangeZ, InstanceScalingMode mode, ref Random random)
        {
            float3 scale = 1.0f;

            switch (mode)
            {
                case InstanceScalingMode.Uniform:
                    scale = rangeX.Interpolate(random.NextFloat());
                    break;

                case InstanceScalingMode.Free:
                    scale.x = rangeX.Interpolate(random.NextFloat());
                    scale.y = rangeY.Interpolate(random.NextFloat());
                    scale.z = rangeZ.Interpolate(random.NextFloat());
                    break;

                case InstanceScalingMode.LockXY:
                    scale.xy = rangeX.Interpolate(random.NextFloat());
                    scale.z = rangeZ.Interpolate(random.NextFloat());
                    break;

                case InstanceScalingMode.LockXZ:
                    scale.xz = rangeX.Interpolate(random.NextFloat());
                    scale.y = rangeY.Interpolate(random.NextFloat());
                    break;

                case InstanceScalingMode.LockYZ:
                    scale.yz = rangeY.Interpolate(random.NextFloat());
                    scale.x = rangeX.Interpolate(random.NextFloat());
                    break;
            }

            return new LocalTransform { Position = transform.Position, Rotation = transform.Rotation, Scale = scale };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LocalTransform RandomizeScale(in LocalTransform transform, InstancePlacementSettings settings, ref Random random)
            => RandomizeScale(transform, settings.ScaleX, settings.ScaleY, settings.ScaleZ, settings.ScalingMode, ref random);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LocalTransform RandomizeYaw(in LocalTransform transform, ref Random random)
        {
            return new LocalTransform { Position = transform.Position, Rotation = quaternion.RotateY(random.NextFloat(MathConstants.TwoPI)), Scale = transform.Scale };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LocalTransform AlignToSurface(in LocalTransform transform, float3 normal, float maxAlignmentAngle = 0.0f)
        {
            quaternion rotation = Quaternion.FromToRotation(math.up(), normal);

            if (maxAlignmentAngle > 0.0f)
            {
                float maxAlignmentAngleRad = math.radians(maxAlignmentAngle);
                float currentAngle = math.acos(math.dot(normal, math.up()));
                if (currentAngle > maxAlignmentAngleRad)
                {
                    float3 rotationAxis = math.normalize(math.cross(math.up(), normal));
                    rotation = quaternion.AxisAngle(rotationAxis, maxAlignmentAngleRad);
                }
            }

            return new LocalTransform { Position = transform.Position, Rotation = math.mul(rotation, transform.Rotation), Scale = transform.Scale };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetRandomSeed(float2 location) => new int2(math.round(location * 100.0f)).GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetRandomSeed(float3 location) => GetRandomSeed(location.xz);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidHeight(float3 location, Interval heightRange)
            => heightRange.Contains(location.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidSlope(float3 normal, Interval minMaxAngle, float tolerance = MathConstants.ZeroTolerance)
        {
            float minNormalAngle = math.cos(math.radians(minMaxAngle.Min));
            float maxNormalAngle = math.cos(math.radians(minMaxAngle.Max));
            return !(maxNormalAngle > (normal.y + tolerance) || minNormalAngle < (normal.y - tolerance));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AverageHitNormal(
            ref PlacementHit hit, PhysicsScene physicsScene, float3 origin, LayerMask layerMask,
            int sampleCount, bool singleComponent, float lowBoundingRadius)
        {
            // Generate a random stream for the averaging
            int pointSeed = GetRandomSeed(hit.Point);
            Random localRandom = new Random(math.asuint(pointSeed + 1));
            float3 cumulativeNormal = math.float3(hit.Normal);

            for (int sampleIndex = 0; sampleIndex < sampleCount; ++sampleIndex)
            {
                float angle = localRandom.NextFloat(0, math.PI * 2.0f);
                float sqrtRadius = math.sqrt(localRandom.NextFloat()) * lowBoundingRadius;
                float3 offset = math.float3(sqrtRadius * math.cos(angle), 0.0f, sqrtRadius * math.sin(angle));

                float3 from = (float3)origin + offset;
                float3 to = (float3)hit.Point + offset;
                float3 dir = math.normalizesafe(to - from);
                float distance = math.length(to - from);

                if (physicsScene.Raycast(from, dir, out RaycastHit normalHit, distance, layerMask, QueryTriggerInteraction.Ignore))
                {
                    if (!singleComponent || normalHit.colliderInstanceID == hit.ColliderInstanceID)
                    {
                        cumulativeNormal += (float3)normalHit.normal;
                    }
                }
            }

            hit.Normal = math.normalizesafe(cumulativeNormal);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckOverhang(
            in LocalTransform transform, in PlacementHit hit, PhysicsScene physicsScene, LayerMask layerMask,
            bool alignToSurface, float verticalOffset, Sphere sphere)
        {
            LocalTransform transformNoRotation = new LocalTransform { Position = transform.Position, Rotation = quaternion.identity, Scale = transform.Scale };

            // Overhang sample positions in local space (check each corner of the lowest part of the bounding box)
            Span<float3> sphereSamples = stackalloc float3[] { math.float3(sphere.Radius, 0, 0), math.float3(-sphere.Radius, 0, 0), math.float3(0, 0, sphere.Radius), math.float3(0, 0, -sphere.Radius) };

            float3 sampleCenter = new float3(sphere.Center.x, sphere.Radius, sphere.Center.z);

            for (int i = 0; i < sphereSamples.Length; ++i)
            {
                float3 sample = transformNoRotation.TransformPoint(sampleCenter + sphereSamples[i]);
                float radius = (sphere.Radius + sphere.Radius) * math.max(transform.Scale.x, transform.Scale.z);
                float3 normal = alignToSurface ? hit.Normal : math.mul(transform.Rotation, math.up());
                float3 to = sample - normal * radius;
                float3 dir = math.normalizesafe(to - sample);
                float distance = math.length(to - sample);

                bool foundLedge = false;
                if (physicsScene.Raycast(sample, dir, out RaycastHit ledgeHit, distance, layerMask, QueryTriggerInteraction.Ignore))
                {
                    float3 localHit = transform.InverseTransformPoint(ledgeHit.point);
                    if (localHit.y - verticalOffset < sphere.Radius && ledgeHit.colliderInstanceID == hit.ColliderInstanceID)
                    {
                        foundLedge = true;
                    }
                }

                if (!foundLedge)
                    return false; // Failed to find a ledge
            }

            return true;
        }

        static readonly Collider[] s_OverlapColliders = new Collider[1];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckWorld(in LocalTransform transform, in AxisAlignedBox prototypeBounds, PhysicsScene physicsScene, LayerMask layerMask)
        {
            AxisAlignedBox worldBounds = prototypeBounds.TransformBy(transform);
            if (worldBounds.IsEmpty)
                return false;

            int hitCount = physicsScene.OverlapBox(worldBounds.Center, worldBounds.Extents, s_OverlapColliders, transform.Rotation, layerMask, QueryTriggerInteraction.Ignore);
            return hitCount > 0;
        }
    }
}
