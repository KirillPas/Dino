// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEngine.UIElements;
#if !UNITY_2022_3_OR_NEWER
using UnityEditor.UIElements;
#endif

namespace MA.Flora.Editor
{
    class BrushToolModeField : VisualElement
    {
        public IconField<EnumField> BrushType { get; private set; }
        
        const string k_SphereIconPath = "Packages/com.ma.flora/Editor/EditorResources/Icon/Brush Sphere Icon.png";
        const string k_CircleIconPath = "Packages/com.ma.flora/Editor/EditorResources/Icon/Brush Circle Icon.png";
        
        public BrushToolModeField()
        {
            var enumField = new EnumField(L10n.Tr("Brush Mode"), BrushToolMode.Sphere);
            BrushType = new IconField<EnumField>(enumField);
            BrushType.Icon.image = EditorGUIUtilityBridge.LoadIconRequired(k_SphereIconPath);
            Add(BrushType);
            
            enumField.RegisterValueChangedCallback(evt =>
            {
                BrushType.Icon.image = EditorGUIUtilityBridge.LoadIconRequired((BrushToolMode)evt.newValue == BrushToolMode.Sphere ? k_SphereIconPath : k_CircleIconPath);
                if (InstanceTool.Active is InstanceBrushTool brushTool)
                    brushTool.Brush.Mode = (BrushToolMode) evt.newValue;
            });
            
            RegisterCallback<AttachToPanelEvent>(evt =>
            {
                UpdateValues();
            });
        }
        
        void UpdateValues()
        {
            if (InstanceTool.Active is InstanceBrushTool brushTool)
            {
                BrushType.Field.value = brushTool.Brush.Mode;
                BrushType.Icon.image = EditorGUIUtilityBridge.LoadIconRequired(brushTool.Brush.Mode == BrushToolMode.Sphere ? k_SphereIconPath : k_CircleIconPath);
            }
        }
    }
}