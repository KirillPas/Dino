// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    /// <summary>Global API for managing runtime instances.</summary>
    public static class InstancingAPI
    {
        // --- Runtime Instance Management ---

        /// <summary>Returns true if the specified instance exists.</summary>
        /// <param name="globalID">The global instance ID to check.</param>
        /// <returns>True if the instance exists; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InstanceExists(InstancedGlobalID globalID)
            => RuntimeInstanceManager.Exists(globalID);

        /// <summary>Returns true if the specified instance is enabled.</summary>
        /// <param name="globalID">The global instance ID to check.</param>
        /// <returns>True if the instance is enabled; otherwise, false.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInstanceEnabled(InstancedGlobalID globalID)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(globalID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(globalID);
            return container.IsInstanceEnabled(instanceIndex);
        }

        /// <summary>Sets the specified instance to be enabled or disabled.</summary>
        /// <param name="globalID">The global instance ID to set the enabled state of.</param>
        /// <param name="enabled">True to enable the instance; otherwise, false to disable it.</param>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetInstanceEnabled(InstancedGlobalID globalID, bool enabled)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(globalID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(globalID);
            container.SetInstanceEnabled(instanceIndex, enabled);
        }

        /// <summary>Returns the container of the specified instance.</summary>
        /// <param name="globalID">The global instance ID to get the container of.</param>
        /// <returns>The container of the instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InstancedMeshContainer GetInstanceContainer(InstancedGlobalID globalID)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            return RuntimeInstanceManager.GetInstanceContainer(globalID);
        }

        /// <summary>Returns the world transform of the specified instance.</summary>
        /// <param name="globalID">The global instance ID to get the transform of.</param>
        /// <returns>The world transform of the instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static LocalTransform GetInstanceTransform(InstancedGlobalID globalID)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(globalID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(globalID);
            return container.GetInstanceTransform(instanceIndex, Space.World);
        }

        /// <summary>Returns the world bounds of the specified instance.</summary>
        /// <param name="globalID">The global instance ID to get the bounds of.</param>
        /// <returns>The world bounds of the instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AxisAlignedBox GetInstanceBounds(InstancedGlobalID globalID)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(globalID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(globalID);
            return container.GetInstanceBounds(instanceIndex, Space.World);
        }

        /// <summary>Returns the <see cref="InstancedObjectLink"/> of the specified instance, if it exists.</summary>
        /// <param name="globalID">The global instance ID to get the link of.</param>
        /// <returns>The <see cref="InstancedObjectLink"/> of the instance, or null if the link does not exist.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InstancedObjectLink GetLinkedObject(InstancedGlobalID globalID)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(globalID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(globalID);
            return container.GetLinkedObject(instanceIndex);
        }

        /// <summary>Returns the <see cref="GameObject"/> of the specified instance, if it exists.</summary>
        /// <param name="globalID">The global instance ID to get the <see cref="GameObject"/> of.</param>
        /// <returns>The <see cref="GameObject"/> of the instance, or null if the <see cref="GameObject"/> does not exist.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GameObject GetLinkedGameObject(InstancedGlobalID globalID)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(globalID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(globalID);
            return container.GetLinkedGameObject(instanceIndex);
        }

        /// <summary>Updates the world transform of the specified instance.</summary>
        /// <param name="globalID">The global instance ID to update the transform of.</param>
        /// <param name="transform">The new transform of the instance.</param>
        /// <remarks>Transforms are in world space.</remarks>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateInstanceTransform(InstancedGlobalID globalID, LocalTransform transform)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(globalID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(globalID);
            container.UpdateInstanceTransform(instanceIndex, transform, Space.World);
        }

        /// <summary>Destroys the specified instance.</summary>
        /// <param name="globalID">The global instance ID to destroy.</param>
        /// <exception cref="InvalidOperationException">Thrown if the instance does not exist.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyInstance(InstancedGlobalID globalID)
        {
            if (!InstanceExists(globalID))
                throw new InvalidOperationException($"Instance ({globalID}) does not exist.");

            InstancedMeshContainer container = RuntimeInstanceManager.GetInstanceContainer(globalID);
            int instanceIndex = RuntimeInstanceManager.GetInstanceIndex(globalID);
            container.RemoveInstance(instanceIndex);
        }

        /// <summary>Finds instances within the specified world space bounds.</summary>
        /// <param name="prototype">The prototype of the instances.</param>
        /// <param name="bounds">The bounds to get the instances in.</param>
        /// <param name="allocator">The allocator to use for the returned array.</param>
        /// <returns>The global instance IDs of instances within the specified bounds.</returns>
        public static NativeArray<InstancedGlobalID> FindInstancesOverlappingBounds(InstancedPrototype prototype, AxisAlignedBox bounds, Allocator allocator)
        {
            FindContainersOverlappingBounds(prototype, bounds, s_ContainerBuffer);

            NativeList<InstancedGlobalID> globalIDs = new NativeList<InstancedGlobalID>(256, allocator);
            foreach (InstancedMeshContainer container in s_ContainerBuffer)
            {
                using NativeArray<int> instanceIndices = container.GetInstancesInsideBounds(bounds, Space.World, Allocator.Temp);
                globalIDs.ReserveAdditional(instanceIndices.Length);

                for (int index = 0; index < instanceIndices.Length; index++)
                {
                    int instanceIndex = instanceIndices[index];
                    globalIDs.Add(container.GetGlobalInstancedID(instanceIndex));
                }
            }

            return globalIDs.AsArray();
        }

        /// <summary>Finds instances within the specified world space sphere.</summary>
        /// <param name="prototype">The prototype of the instances.</param>
        /// <param name="center">The center of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        /// <param name="allocator">The allocator to use for the returned array.</param>
        /// <returns>The global instance IDs of instances within the specified sphere.</returns>
        public static NativeArray<InstancedGlobalID> FindInstancesOverlappingSphere(InstancedPrototype prototype, float3 center, float radius, Allocator allocator)
        {
            FindContainersOverlappingSphere(prototype, center, radius, s_ContainerBuffer);
            Sphere sphere = new Sphere(center, radius);

            NativeList<InstancedGlobalID> globalIDs = new NativeList<InstancedGlobalID>(256, allocator);
            foreach (InstancedMeshContainer container in s_ContainerBuffer)
            {
                using NativeArray<int> instanceIndices = container.GetInstancesInsideSphere(sphere, Space.World, Allocator.Temp);
                globalIDs.ReserveAdditional(instanceIndices.Length);

                for (int index = 0; index < instanceIndices.Length; index++)
                {
                    int instanceIndex = instanceIndices[index];
                    globalIDs.Add(container.GetGlobalInstancedID(instanceIndex));
                }
            }

            return globalIDs.AsArray();
        }

        // --- Container Lookup ---

        /// <summary>Finds containers within the specified world space bounds.</summary>
        /// <param name="prototype">The prototype of the containers.</param>
        /// <param name="bounds">The bounds to get the containers in.</param>
        /// <param name="containers">The list to store the containers in.</param>
        /// <returns>The number of containers in the bounds.</returns>
        public static int FindContainersOverlappingBounds(InstancedPrototype prototype, AxisAlignedBox bounds, List<InstancedMeshContainer> containers)
            => RuntimeSpatialHash.Instance.GetOverlappingBounds(prototype, bounds, containers);

        /// <summary>Finds containers within the specified world space sphere.</summary>
        /// <param name="prototype">The prototype of the containers.</param>
        /// <param name="center">The center of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        /// <param name="containers">The list to store the containers in.</param>
        /// <returns>The number of containers in the sphere.</returns>
        public static int FindContainersOverlappingSphere(InstancedPrototype prototype, float3 center, float radius, List<InstancedMeshContainer> containers)
            => RuntimeSpatialHash.Instance.GetOverlappingSphere(prototype, center, radius, containers);

        static List<InstancedMeshContainer> s_ContainerBuffer = new List<InstancedMeshContainer>();
    }
}
