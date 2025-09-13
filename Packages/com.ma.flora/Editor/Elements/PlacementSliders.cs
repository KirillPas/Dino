// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [EditorToolbarElement(ID, typeof(SceneView))]
    class BrushRadiusSlider : PlacementClutchSlider
    {
        public const string ID = "Instance Tool Context/Brush Radius";
        
        public BrushRadiusSlider(SliderDirection direction) : base(
            direction,
            PlacementClutchShortcutType.Size,
            L10n.Tr("Radius"), 
            L10n.Tr("Controls the radius of the brush."), 
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Size Icon.png"),
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Size On Icon.png"),
            0.1f, 80.0f,
            GetValue, SetValue)
        {
            AddToClassList("toolbar-element");
        }
        
        static float GetValue() => InstanceTool.Active is InstanceBrushTool brushTool ? brushTool.Brush.Radius : 1.0f;
        static void SetValue(float value)
        {
            if (InstanceTool.Active is InstanceBrushTool brushTool)
                brushTool.Brush.Radius = value;
        }
    }
    
    [EditorToolbarElement(ID, typeof(SceneView))]
    class BrushStrengthSlider : PlacementClutchSlider
    {
        public const string ID = "Instance Tool Context/Brush Strength";
        
        public BrushStrengthSlider(SliderDirection direction) : base(
            direction,
            PlacementClutchShortcutType.Strength,
            L10n.Tr("Strength"), 
            L10n.Tr("Controls the strength of the brush."), 
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Strength Icon.png"),
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Strength On Icon.png"),
            0.0f, 1.0f,
            GetValue, SetValue)
        {
            AddToClassList("toolbar-element");
        }
        
        static float GetValue() => InstanceTool.Active is InstanceBrushTool brushTool ? brushTool.Brush.Strength : 1.0f;
        static void SetValue(float value)
        {
            if (InstanceTool.Active is InstanceBrushTool brushTool)
                brushTool.Brush.Strength = value;
        }
    }
    
    [EditorToolbarElement(ID, typeof(SceneView))]
    class BrushDensitySlider : PlacementClutchSlider
    {
        public const string ID = "Instance Tool Context/Brush Density";
        
        public BrushDensitySlider(SliderDirection direction) : base(
            direction,
            PlacementClutchShortcutType.Strength,
            L10n.Tr("Density"), 
            L10n.Tr("Controls the density of the painted instances."), 
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Density Icon.png"),
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Density On Icon.png"),
            0.0f, 1.0f,
            GetValue, SetValue)
        {
            AddToClassList("toolbar-element");
        }
        
        static float GetValue() => InstanceTool.Active is InstanceBrushTool brushTool ? brushTool.Brush.Strength : 1.0f;
        static void SetValue(float value)
        {
            if (InstanceTool.Active is InstanceBrushTool brushTool)
                brushTool.Brush.Strength = value;
        }
    }
    
    [EditorToolbarElement(ID, typeof(SceneView))]
    class BrushFalloffSlider : PlacementClutchSlider
    {
        public const string ID = "Instance Tool Context/Brush Falloff";
        
        public BrushFalloffSlider(SliderDirection direction) : base(
            direction,
            PlacementClutchShortcutType.Adjustment,
            L10n.Tr("Falloff"), 
            L10n.Tr("Controls the strength of the brush."), 
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Strength Icon.png"),
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Strength On Icon.png"),
            0.0f, 1.0f,
            GetValue, SetValue)
        {
            AddToClassList("toolbar-element");
        }
        
        static float GetValue() => InstanceTool.Active is InstanceBrushTool brushTool ? brushTool.Brush.Falloff : 1.0f;
        static void SetValue(float value)
        {
            if (InstanceTool.Active is InstanceBrushTool brushTool)
                brushTool.Brush.Falloff = value;
        }
    }
    
    [EditorToolbarElement(ID, typeof(SceneView))]
    class FillDensitySlider : PlacementClutchSlider
    {
        public const string ID = "Instance Tool Context/Fill Density";
        
        public FillDensitySlider(SliderDirection direction) : base(
            direction,
            PlacementClutchShortcutType.Strength,
            L10n.Tr("Density"), 
            L10n.Tr("Controls the density of filled instances."), 
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Density Icon.png"),
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Density On Icon.png"),
            0.0f, 1.0f,
            GetValue, SetValue)
        {
            AddToClassList("toolbar-element");
        }
        
        static float GetValue() => InstanceTool.Active is InstanceFillTool fillTool ? fillTool.DensityStrength : 1.0f;
        static void SetValue(float value)
        {
            if (InstanceTool.Active is InstanceFillTool fillTool)
                fillTool.DensityStrength = value;
        }
    }
    
    [EditorToolbarElement(ID, typeof(SceneView))]
    class PlaceScaleSlider : PlacementClutchSlider
    {
        public const string ID = "Instance Tool Context/Placement Scale";
        
        public PlaceScaleSlider(SliderDirection direction) : base(
            direction,
            PlacementClutchShortcutType.Size,
            L10n.Tr("Scale"), 
            L10n.Tr("Controls the scale of the instance."), 
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Size Icon.png"),
            EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Size On Icon.png"),
            0.01f, 5.0f,
            GetValue, SetValue)
        {
            AddToClassList("toolbar-element");
        }
        
        static float GetValue() => InstanceTool.Active is InstancePlaceTool placeTool ? placeTool.Scale : 1.0f;
        static void SetValue(float value)
        {
            if (InstanceTool.Active is InstancePlaceTool placeTool)
                placeTool.Scale = value;
        }
    }
}