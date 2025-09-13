// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Collections.Unsafe;
using MA.Flora.Rendering;
using MA.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    /// <summary>
    /// Defines an instance renderer that can be registered with the instancing system.
    /// </summary>
    interface IInstancedRenderer
    {
        bool IsValid { get; }

        /// <summary>The owning transform of the renderer.</summary>
        Transform Transform { get; }

        /// <summary>The instance prototype used to render instances in the renderer.</summary>
        InstancedPrototype Prototype { get; }

        /// <summary>The tree used to cull instances contained in the renderer.</summary>
        CullingData CullingData { get; }

        /// <summary>If greater than zero, the renderer will be culled using the specified distance.</summary>
        /// <remarks>This overrides the distance specified by the instance prototype.</remarks>
        float CullingDistance => 0;

        /// <summary>If greater than zero, the renderer will be streamed using the specified distance.</summary>
        /// <remarks>This overrides the distance specified by the instance prototype.</remarks>
        float StreamingDistance => 0;

        /// <summary>Marks the render state of the renderer as dirty, causing the render system to re-evaluate its state.</summary>
        void MarkRenderStateDirty();

        /// <summary>Calculates the bounds of the renderer in the specified space.</summary>
        /// <param name="space">The space to calculate the bounds in.</param>
        /// <returns>A bounds that represents the bounds of the renderer.</returns>
        AxisAlignedBox CalculateBounds(Space space) => CullingData.CalculateBounds(space);

        // --- Instance Transforms ---

        /// <summary>The instance transforms of the renderer, in local space.</summary>
        ReadOnlySpan<LocalTransform> InstanceTransforms { get; }

        /// <summary>The number of instances in the renderer.</summary>
        int InstanceCount => InstanceTransforms.Length;

        /// <summary>The version of the instance transforms of the renderer.</summary>
        /// <remarks>This value should be incremented whenever an instance transform is added, removed, or changed.</remarks>
        int InstanceTransformsVersion { get; }

        /// <summary>A version used to determine if the order of instances has changed.</summary>
        /// <remarks>This value should be incremented whenever the order of instances changes.</remarks>
        int InstanceOrderVersion { get; }

        // --- Runtime Instance IDs ---

        /// <summary>The global instance IDs of the renderer.</summary>
        ReadOnlySpan<InstancedGlobalID> GlobalIDs => default;

        // --- Instance Properties ---

        /// <summary>The instanced properties of the renderer.</summary>
        InstancedPropertyArrays InstancePropertyArrays => null;

        // --- Enabled Instances ---

        /// <summary>The enabled instances version of the renderer.</summary>
        int InstancesEnabledVersion => 0;

        /// <summary>The enabled instances of the renderer.</summary>
        UnsafeBitList InstancesEnabled => default;
    }

    interface IInstancedRendererEditorData
    {
#if UNITY_EDITOR
        int InstanceSelectionVersion { get; }
        UnsafeBitList InstanceSelection { get; }
#endif
    }
}
