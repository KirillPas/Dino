// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(InstanceEraseTool))]
    class InstanceEraseBrushSettings : InstanceBrushToolSettings
    {
        protected override void AddToolbarElements(OverlayToolbar toolbar, Layout layout) 
        {
            SliderDirection direction = layout == Layout.VerticalToolbar ? SliderDirection.Vertical : SliderDirection.Horizontal;
            toolbar.Add(new BrushDensitySlider(direction));
            toolbar.Add(new BrushRadiusSlider(direction));
        }
    }
    
    [FilePath("Library/com.ma.flora/Tools/InstanceEraseTool", FilePathAttribute.Location.ProjectFolder)]
    sealed class InstanceEraseTool : InstanceBrushTool
    {
        [Shortcut("Flora/Instance Erase Tool", typeof(InstanceToolShortcutContext), ShortcutKeys.Erase)]
        public static void Shortcut()
        {
            if (InstanceToolContext.IsActive)
            {
                ToolManager.SetActiveTool<InstanceEraseTool>();
            }
        }
        
        protected override PlacementClutchShortcutMask GetAvailableClutchShortcuts() => PlacementClutchShortcutMask.Strength | PlacementClutchShortcutMask.Size;

        protected override string GetBrushGroupName() => "Erase Instances";

        protected override void OnBrushPaint()
        {
            float brushArea = Brush.CalculateArea();
            List<InstancedPrototype> instancePrototypes = InstanceToolContextShared.ActivePrototypes;

            foreach (InstancedPrototype prototype in instancePrototypes)
            {
                int desiredInstanceCount = (int)math.round(brushArea * prototype.PlacementSettings.Density * (1.0f - Brush.Power) / (10.0f * 10.0f));
                RemoveInstancesInsideBrush(prototype, desiredInstanceCount);
            }
        }
    }
}
