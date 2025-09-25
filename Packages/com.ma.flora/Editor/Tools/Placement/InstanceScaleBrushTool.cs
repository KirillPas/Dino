// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(InstanceScaleBrushTool))]
    class InstanceScaleBrushToolSettings : InstanceBrushToolSettings
    {
        protected override void AddToolbarElements(OverlayToolbar toolbar, Layout layout) 
        {
            SliderDirection direction = layout == Layout.VerticalToolbar ? SliderDirection.Vertical : SliderDirection.Horizontal;
            toolbar.Add(new BrushStrengthSlider(direction));
            toolbar.Add(new BrushRadiusSlider(direction));
            toolbar.Add(new BrushFalloffSlider(direction));
        }
    }
    
    [FilePath("Library/com.ma.flora/Tools/InstanceScaleBrushTool", FilePathAttribute.Location.ProjectFolder)]
    class InstanceScaleBrushTool : InstanceBrushTool
    {
        [Shortcut("Flora/Instance Scale Tool", typeof(InstanceToolShortcutContext), ShortcutKeys.Scale)]
        public static void Shortcut()
        {
            if (InstanceToolContext.IsActive)
                ToolManager.SetActiveTool<InstanceScaleBrushTool>();
        }
        
        protected override PlacementClutchShortcutMask GetAvailableClutchShortcuts() => PlacementClutchShortcutMask.Strength | PlacementClutchShortcutMask.Size | PlacementClutchShortcutMask.Adjustment;

        protected override string GetBrushGroupName() => "Scale Instances";
        
        protected override void OnBrushPaint()
        {
            float power = Brush.Power;
            if (Event.current.shift)
                power = -power;
                    
            power = math.clamp(power, -1.0f, 1.0f);
            
            List<InstancedPrototype> prototypes = InstanceToolContextShared.ActivePrototypes;
            using NativeList<int> instanceIndices = new NativeList<int>(256, Allocator.TempJob);
            using UnsafeIndirectList<LocalTransform> scaledTransforms = new UnsafeIndirectList<LocalTransform>(256, Allocator.TempJob);
            
            foreach (InstancedPrototype prototype in prototypes)
            {
                if (TryGetContainersOverlappingSphere(prototype, Brush.GetSphere(), out List<InstancedMeshContainer> containers))
                {
                    foreach (InstancedMeshContainer container in containers)
                    {
                        instanceIndices.Clear();
                        
                        container.GetInstancesInsideSphere(Brush.GetSphere(), Space.World, instanceIndices);
                        if (instanceIndices.Length > 0)
                            InstancePlacementUtility.RecordForModify(container);
                        
                        scaledTransforms.Resize(instanceIndices.Length, NativeArrayOptions.UninitializedMemory);
                        
                        for (int i = 0; i < instanceIndices.Length; ++i)
                        {
                            int instanceIndex = instanceIndices[i];
                            
                            float3 worldPosition = container.GetInstancePosition(instanceIndex, Space.World);
                            float distance = math.distance(worldPosition, Brush.Point);
                            float alpha = power * ComputeSmoothFalloff(distance, Brush.Radius, Brush.Falloff);
                            
                            LocalTransform localTransform = container.GetInstanceTransform(instanceIndex, Space.Self);
                            localTransform.Scale *= 1.0f + (alpha * 0.1f);
                            scaledTransforms[i] = localTransform;
                        }
                        
                        container.UpdateInstanceTransforms(instanceIndices.AsReadOnlySpan(), scaledTransforms.AsReadOnlySpan(), Space.Self);
                    }
                }
            }
        }
    }
}