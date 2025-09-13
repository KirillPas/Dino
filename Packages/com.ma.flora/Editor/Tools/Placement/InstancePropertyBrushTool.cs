// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [CustomEditor(typeof(InstancePropertyBrushTool))]
    class InstancePropertyBrushToolSettings : InstancePlacementToolSettings
    {
        protected override void AddToolbarElements(OverlayToolbar toolbar, Layout layout) 
        {
            SliderDirection direction = layout == Layout.VerticalToolbar ? SliderDirection.Vertical : SliderDirection.Horizontal;
            toolbar.Add(new BrushStrengthSlider(direction));
            toolbar.Add(new BrushRadiusSlider(direction));
            toolbar.Add(new BrushFalloffSlider(direction));
        }
    }
    
    [FilePath("Library/com.ma.flora/Tools/InstancePropertyBrushTool", FilePathAttribute.Location.ProjectFolder)]
    class InstancePropertyBrushTool : InstanceBrushTool
    {
        [SerializeField] SerializedDictionary<string, float4x4> m_PaintValues = new SerializedDictionary<string, float4x4>();
        
        [Shortcut("Flora/Instance Property Tool", typeof(InstanceToolShortcutContext), ShortcutKeys.Properties)]
        public static void Shortcut()
        {
            if (InstanceToolContext.IsActive)
                ToolManager.SetActiveTool<InstancePropertyBrushTool>();
        }

        public override bool IsAvailable() => base.IsAvailable() && InstanceToolContextShared.Properties.Count > 0;

        protected override bool IsBrushAvailable() => InstanceToolContextShared.ActiveProperties.Count > 0;
        
        protected override PlacementClutchShortcutMask GetAvailableClutchShortcuts() => PlacementClutchShortcutMask.Strength | PlacementClutchShortcutMask.Size | PlacementClutchShortcutMask.Adjustment;
        
        protected override string GetBrushLabelForClutch(PlacementClutchShortcutType clutchType)
        {
            return clutchType switch
            {
                PlacementClutchShortcutType.Strength => L10n.Tr("Opacity"),
                _ => base.GetBrushLabelForClutch(clutchType)
            };
        }

        protected override string GetBrushGroupName() => "Paint Instanced Property";
        
        public void SetPaintValue(string propertyName, float4x4 value) 
            => m_PaintValues[propertyName] = value;
        
        public void SetPaintValue(string propertyName, float value)
            => m_PaintValues[propertyName] = new float4x4(value, 0, 0, 0);
        
        public void SetPaintValue(string propertyName, float2 value)
            => m_PaintValues[propertyName] = new float4x4(value.xyxx, 0, 0, 0);
        
        public void SetPaintValue(string propertyName, float3 value) 
            => m_PaintValues[propertyName] = new float4x4(value.xyzx, 0, 0, 0);
        
        public void SetPaintValue(string propertyName, Vector4 value)
            => m_PaintValues[propertyName] = new float4x4(value, 0, 0, 0);
        
        public void SetPaintValue(string propertyName, int value) 
            => m_PaintValues[propertyName] = new float4x4(value, 0, 0, 0);
        
        public void SetPaintValue(string propertyName, int2 value)
            => m_PaintValues[propertyName] = new float4x4(value.xyxx, 0, 0, 0);
        
        public void SetPaintValue(string propertyName, int3 value) 
            => m_PaintValues[propertyName] = new float4x4(value.xyzx, 0, 0, 0);
        
        public void SetPaintValue(string propertyName, int4 value) 
            => m_PaintValues[propertyName] = new float4x4(value, 0, 0, 0);

        protected override void OnBrushPaint()
        {
            List<InstancedPropertyDescriptor> activeProperties = InstanceToolContextShared.ActiveProperties;
            List<InstancedPrototype> activePrototypes = InstanceToolContextShared.ActivePrototypes;
            
            foreach (InstancedPrototype prototype in activePrototypes)
                SetPropertiesForBrush(prototype, activeProperties);
        }
        
        HashSet<InstancedPropertyDescriptor> m_ActiveProperties = new HashSet<InstancedPropertyDescriptor>();

        void SetPropertiesForBrush(InstancedPrototype prototype, List<InstancedPropertyDescriptor> descriptors)
        {
            if (prototype.InstancedProperties.Length == 0)
                return;
            
            m_ActiveProperties.Clear();
            foreach (InstancedPropertyDescriptor descriptor in descriptors)
            {
                int indexInPrototype = prototype.InstancedProperties.IndexOf(descriptor);
                if (indexInPrototype != -1)
                    m_ActiveProperties.Add(descriptor);
            }
            
            if (m_ActiveProperties.Count == 0)
                return;

            bool isReset = EditorGUI.actionKey;
            
            if (TryGetContainersOverlappingSphere(prototype, Brush.GetSphere(), out List<InstancedMeshContainer> renderers))
            {
                foreach (InstancedMeshContainer renderer in renderers)
                {
                    NativeArray<int> instanceIndices = renderer.GetInstancesInsideSphere(Brush.GetSphere(), Space.World, Allocator.Temp);
                    if (instanceIndices.Length > 0)
                    {
                        foreach (InstancedPropertyDescriptor property in m_ActiveProperties)
                        {
                            if (!renderer.HasInstancedProperty(property.NameID))
                                continue;
                            
                            InstancePlacementUtility.RecordForModify(renderer);

                            if (isReset || !m_PaintValues.TryGetValue(property.Name, out float4x4 paintValue))
                            {
                                switch (property.Type)
                                {
                                    case InstancedPropertyType.Color:
                                        paintValue = new float4x4(new float4(property.GetDefaultValue<Vector4>()), 0, 0, 0);
                                        break;
                                    case InstancedPropertyType.Float:
                                        paintValue = new float4x4(new float4(property.GetDefaultValue<float>()), 0, 0, 0);
                                        break;
                                    case InstancedPropertyType.Float2:
                                        paintValue = new float4x4(new float4(property.GetDefaultValue<float2>(), 0), 0, 0, 0);
                                        break;
                                    case InstancedPropertyType.Float3:
                                        paintValue = new float4x4(new float4(property.GetDefaultValue<float3>(), 0), 0, 0, 0);
                                        break;
                                    case InstancedPropertyType.Float4:
                                        paintValue = new float4x4(new float4(property.GetDefaultValue<Vector4>()), 0, 0, 0);
                                        break;
                                    case InstancedPropertyType.UInt:
                                        paintValue = new float4x4(new float4(math.asfloat(property.GetDefaultValue<uint>())), 0, 0, 0);
                                        break;
                                    case InstancedPropertyType.UInt2:
                                        paintValue = new float4x4(new float4(math.asfloat(property.GetDefaultValue<uint2>()), 0), 0, 0, 0);
                                        break;
                                    case InstancedPropertyType.UInt3:
                                        paintValue = new float4x4(new float4(math.asfloat(property.GetDefaultValue<uint3>()), 0), 0, 0, 0);
                                        break;
                                    case InstancedPropertyType.UInt4:
                                        paintValue = new float4x4(new float4(math.asfloat(property.GetDefaultValue<uint4>())), 0, 0, 0);
                                        break;
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
            
                            for (int i = 0; i < instanceIndices.Length; i++)
                            {
                                int instanceIndex = instanceIndices[i];
                                float3 instancePosition = renderer.GetInstancePosition(instanceIndex, Space.World);
            
                                float distance = math.distance(instancePosition, Brush.Point);
                                float alpha = ComputeSmoothFalloff(distance, Brush.Radius, Brush.Falloff) * Brush.Power;

                                switch (property.Type)
                                {
                                    case InstancedPropertyType.Color:
                                    case InstancedPropertyType.Float4:
                                    {
                                        float4 oldAttribute = renderer.GetInstancedProperty<float4>(property.NameID, instanceIndex);
                                        float4 newAttribute = math.lerp(oldAttribute, paintValue.c0, alpha);
                                        renderer.SetInstancedProperty(property.NameID, instanceIndex, newAttribute);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
