// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Random = Unity.Mathematics.Random;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(InstancePaintTool))]
    class InstancePaintToolSettings : InstanceBrushToolSettings
    {
        protected override void AddToolbarElements(OverlayToolbar toolbar, Layout layout)
        {
            SliderDirection direction = layout == Layout.VerticalToolbar ? SliderDirection.Vertical : SliderDirection.Horizontal;
            toolbar.Add(new BrushDensitySlider(direction));
            toolbar.Add(new BrushRadiusSlider(direction));
        }
    }

    [BurstCompile]
    [FilePath("Library/com.ma.flora/Tools/InstancePaintTool", FilePathAttribute.Location.ProjectFolder)]
    sealed class InstancePaintTool : InstanceBrushTool
    {
        [Shortcut("Flora/Instance Paint Tool", typeof(InstanceToolShortcutContext), ShortcutKeys.Paint)]
        public static void Shortcut()
        {
            if (InstanceToolContext.IsActive)
                ToolManager.SetActiveTool<InstancePaintTool>();
        }

        protected override PlacementClutchShortcutMask GetAvailableClutchShortcuts() => PlacementClutchShortcutMask.Strength | PlacementClutchShortcutMask.Size;

        protected override string GetBrushLabelForClutch(PlacementClutchShortcutType clutchType)
        {
            return clutchType switch
            {
                PlacementClutchShortcutType.Strength => L10n.Tr("Density"),
                _ => base.GetBrushLabelForClutch(clutchType)
            };
        }

        protected override string GetBrushGroupName() => "Paint Instances";

        protected override void OnBrushPaint()
        {
            List<InstancedPrototype> active = InstanceToolContextShared.ActivePrototypes;

            foreach (InstancedPrototype prototype in active)
            {
                float placementDensity = InstancePlacementUtility.ComputeDensity(prototype.PlacementSettings);
                float brushArea = Brush.CalculateArea();

                bool temporaryErase = EditorGUI.actionKey;
                if (temporaryErase)
                {
                    int desiredInstanceCount = (int)math.round(brushArea * placementDensity * (1.0f - Brush.Power) / (10.0f * 10.0f));
                    RemoveInstancesInsideBrush(prototype, desiredInstanceCount);
                }
                else
                {
                    float desiredInstanceCountFloat = brushArea * placementDensity * Brush.Power / (10.0f * 10.0f);
                    int desiredInstanceCount = desiredInstanceCountFloat > 1.0f ? (int)math.round(desiredInstanceCountFloat) : UnityEngine.Random.value < desiredInstanceCountFloat ? 1 : 0;
                    if (desiredInstanceCount > 0)
                        AddInstancesForBrush(prototype, desiredInstanceCount);
                }
            }
        }

        void AddInstancesForBrush(InstancedPrototype prototype, int desiredInstanceCount)
        {
            int existingInstanceCount = 0;

            if (TryGetContainersOverlappingSphere(prototype, Brush.GetSphere(), out List<InstancedMeshContainer> containers))
            {
                foreach (InstancedMeshContainer container in containers)
                {
                    NativeArray<int> existingInstances = container.GetInstancesInsideSphere(Brush.GetSphere(), Space.World, Allocator.Temp);
                    existingInstanceCount += existingInstances.Length;
                    InstancePlacementUtility.RecordForModify(container);
                }
            }

            if (desiredInstanceCount > existingInstanceCount)
            {
                Random random = new Random((uint)DateTime.Now.Ticks);
                UnsafeArray<RaycastCommand> raycastCommands = new UnsafeArray<RaycastCommand>(desiredInstanceCount, AllocatorManager.TempJob);
                UnsafeArray<RaycastHit> raycastHits = new UnsafeArray<RaycastHit>(desiredInstanceCount, AllocatorManager.TempJob);
                UnsafeParallelMultiHashMap<int, RaycastHitInfo> hitsByColliderInstanceID = new UnsafeParallelMultiHashMap<int, RaycastHitInfo>(desiredInstanceCount, AllocatorManager.TempJob);

                PhysicsScene physicsScene = InstancePlacementUtility.GetActivePhysicsScene();
                LayerMask layerMask = PlacementLayerMask;
                QueryParameters queryParameters = new QueryParameters
                {
                    layerMask = layerMask,
                    hitMultipleFaces = false,
                    hitTriggers = QueryTriggerInteraction.Ignore,
                    hitBackfaces = false
                };

                JobHandle raycastHandle;
                if (Brush.Mode == BrushToolMode.Sphere)
                {
                    raycastHandle = new InitRaycastCommandsSphereJob
                    {
                        PhysicsScene = physicsScene,
                        Commands = raycastCommands,
                        BrushPosition = Brush.Point,
                        BrushNormal = Brush.Normal,
                        BrushRadius = Brush.Radius,
                        QueryParameters = queryParameters,
                        Random = random.NextRandom()
                    }.ScheduleBatch(desiredInstanceCount, InitRaycastCommandsSphereJob.BatchSize);
                }
                else
                {
                    raycastHandle = new InitRaycastCommandsCircleJob
                    {
                        PhysicsScene = physicsScene,
                        Commands = raycastCommands,
                        BrushPosition = Brush.Point,
                        BrushNormal = Brush.Normal,
                        BrushRadius = Brush.Radius,
                        QueryParameters = queryParameters,
                        Random = random.NextRandom()
                    }.ScheduleBatch(desiredInstanceCount, InitRaycastCommandsCircleJob.BatchSize);
                }

                raycastHandle = RaycastCommand.ScheduleBatch(raycastCommands.AsNativeArray(), raycastHits.AsNativeArray(), 512, raycastHandle);
                // raycastHandle = new SortRaycastHitsJob { Hits = raycastHits.AsArray() }.Schedule(raycastHandle);
                raycastHandle = new AddColliderHitsJob
                {
                    HitsByColliderInstanceID = hitsByColliderInstanceID.AsParallelWriter(),
                    Hits = raycastHits,
                    Commands = raycastCommands,
                }.ScheduleBatch(desiredInstanceCount, AddColliderHitsJob.BatchSize, raycastHandle);

                raycastHandle = raycastCommands.Dispose(raycastHandle);
                raycastHandle = raycastHits.Dispose(raycastHandle);
                raycastHandle.Complete();

                InstancePlacementHash<int> placementHash = new InstancePlacementHash<int>(7, desiredInstanceCount, AllocatorManager.Temp);
                UnsafeIndirectList<float3> potentialPositions = new UnsafeIndirectList<float3>(desiredInstanceCount, AllocatorManager.Temp);

                Sphere brushSphere = Brush.GetSphere();
                InstancePlacementSettings placementSettings = prototype.PlacementSettings;
                float radius = placementSettings.GetRadius(false);

                AxisAlignedBox bounds = AxisAlignedBox.Empty;
                AxisAlignedBox prototypeBounds = prototype.Bounds;
                UnsafeIndirectList<LocalTransform> transforms = new UnsafeIndirectList<LocalTransform>(desiredInstanceCount, AllocatorManager.Temp);
                int currentColliderInstanceID = -1;
                Collider currentCollider = null;

                NativeArray<int> colliderInstanceIDs = hitsByColliderInstanceID.GetKeyArray(AllocatorManager.Temp);
                int colliderCount = colliderInstanceIDs.Unique();

                for (int i = 0; i < colliderCount; ++i)
                {
                    foreach (RaycastHitInfo hitInfo in hitsByColliderInstanceID.GetValuesForKey(colliderInstanceIDs[i]))
                    {
                        RaycastHit hit = hitInfo.Hit;

                        if (!brushSphere.Contains(hit.point))
                            continue;

                        if (!InstancePlacementUtility.IsValidPlacementHit(placementSettings, hit, radius, containers, placementHash, potentialPositions))
                            continue;

                        if (!InstancePlacementUtility.TryPlaceTransform(prototype, physicsScene, hit, ref random, out LocalTransform transform, out _))
                            continue;

                        if (currentColliderInstanceID != hit.colliderInstanceID)
                        {
                            if (transforms.Length > 0)
                            {
                                InstancePlacementUtility.PlaceInstances(prototype, hit.collider.transform, transforms.AsReadOnlySpan(), bounds);
                                transforms.Clear();
                                bounds = AxisAlignedBox.Empty;
                            }

                            currentColliderInstanceID = hit.colliderInstanceID;
                            currentCollider = hit.collider;
                        }

                        transforms.Add(transform);
                        bounds += prototypeBounds.TransformBy(transform);
                    }
                }

                if (transforms.Length > 0)
                    InstancePlacementUtility.PlaceInstances(prototype, currentCollider.transform, transforms.AsReadOnlySpan(), bounds, PlacementOccluders);

                hitsByColliderInstanceID.Dispose();
            }
        }

        [BurstCompile]
        struct InitRaycastCommandsSphereJob : IJobParallelForBatch
        {
            public const int BatchSize = 512;

            public PhysicsScene PhysicsScene;
            public UnsafeArray<RaycastCommand> Commands;
            public float3 BrushPosition;
            public float3 BrushNormal;
            public float BrushRadius;
            public QueryParameters QueryParameters;
            public Random Random;

            public void Execute(int startIndex, int count)
            {
                BrushNormal.CalculatePerpendicularAxes(out float3 u, out float3 v);
                Random random = Random.CreateFromIndex((uint)startIndex ^ Random.NextUInt() * (uint)count);

                for (int index = 0; index < count; index++)
                {
                    // Find rx and ry inside the unit circle
                    float ru = (2.0f * random.NextFloat() - 1.0f);
                    float rv = (2.0f * random.NextFloat() - 1.0f) * math.sqrt(1.0f - math.lengthsq(ru));

                    // Find a random point in circle through brush location on the same plane to brush location hit surface normal
                    float3 point = ru * u + rv * v;

                    // Find distance to the surface of a sphere from this point
                    float3 rw = math.sqrt(math.max(1.0f - (math.lengthsq(ru) + math.lengthsq(rv)), 0.001f)) * BrushNormal;
                    float3 from = BrushPosition + BrushRadius * (point + rw);
                    float3 end = BrushPosition + BrushRadius * (point - rw);
                    float3 delta = end - from;
                    float3 direction = math.normalize(delta);
                    float distance = math.length(delta);

                    Commands[startIndex + index] = CreateRaycastCommand(PhysicsScene, from, direction, QueryParameters, distance);
                }
            }
        }

        [BurstCompile]
        struct InitRaycastCommandsCircleJob : IJobParallelForBatch
        {
            public const int BatchSize = 512;

            public PhysicsScene PhysicsScene;
            public UnsafeArray<RaycastCommand> Commands;
            public float3 BrushPosition;
            public float3 BrushNormal;
            public float BrushRadius;
            public QueryParameters QueryParameters;
            public Random Random;

            public void Execute(int startIndex, int count)
            {
                BrushNormal.CalculatePerpendicularAxes(out float3 u, out float3 v);

                for (int index = 0; index < count; index++)
                {
                    // Find rx and ry inside the unit circle
                    float ru = (2.0f * Random.NextFloat() - 1.0f);
                    float rv = (2.0f * Random.NextFloat() - 1.0f) * math.sqrt(1.0f - math.lengthsq(ru));

                    // Find a random point in circle through brush location on the same plane to brush location hit surface normal
                    float3 c = ru * u + rv * v;

                    // Find distance to the surface of a circle from this point
                    float3 start = BrushPosition + BrushRadius * c;
                    float3 end = BrushPosition + BrushRadius * c;
                    float3 delta = end - start;
                    float3 direction = math.normalize(delta);
                    float distance = math.length(delta);

                    Commands[startIndex + index] = CreateRaycastCommand(PhysicsScene, start, direction, QueryParameters, distance);
                }
            }
        }

        struct RaycastHitInfo
        {
            public float3 Origin;
            public RaycastHit Hit;
        }

        [BurstCompile]
        struct AddColliderHitsJob : IJobParallelForBatch
        {
            public const int BatchSize = 128;

            public UnsafeParallelMultiHashMap<int, RaycastHitInfo>.ParallelWriter HitsByColliderInstanceID;
            public UnsafeArray<RaycastHit> Hits;
            public UnsafeArray<RaycastCommand> Commands;

            public void Execute(int startIndex, int count)
            {
                for (int index = 0; index < count; index++)
                {
                    RaycastHit hit = Hits[startIndex + index];
                    float3 origin = Commands[startIndex + index].from;
                    HitsByColliderInstanceID.Add(hit.colliderInstanceID, new RaycastHitInfo { Origin = origin, Hit = hit });
                }
            }
        }

        [BurstCompile]
        struct SortRaycastHitsJob : IJob
        {
            public NativeArray<RaycastHit> Hits;

            struct CompareByColliderInstanceID : IComparer<RaycastHit>
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public int Compare(RaycastHit x, RaycastHit y)
                    => x.colliderInstanceID.CompareTo(y.colliderInstanceID);
            }

            public void Execute()
            {
                Hits.Sort(new CompareByColliderInstanceID());
            }
        }
    }
}
