// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    [Icon("Packages/com.ma.flora/Editor/EditorResources/Icon/InstanceToolContext Settings Icon.png")]
    [EditorToolbarElement(ID, typeof(SceneView))]
    public class InstanceToolContextToolbar : VisualElement
    {
        public const string ID = "Instance Tool Context/Instance Toolbar";
        
        EditorToolbarDropdown m_Settings;
        EditorToolbarToggle m_Place;
        EditorToolbarToggle m_Paint;
        EditorToolbarToggle m_Erase;
        EditorToolbarToggle m_Fill;
        EditorToolbarToggle m_Scale;
        EditorToolbarToggle m_Properties;
        
        static readonly string k_SettingsTooltip = L10n.Tr("Settings for instances while editing them.");
        static readonly string k_PlaceTooltip = L10n.Tr("Place instances.\n\nHold control/cmd to erase instances.");
        static readonly string k_PaintTooltip = L10n.Tr("Paint instances.\n\nHold control/cmd to erase instances.");
        static readonly string k_EraseTooltip = L10n.Tr("Erase instances.");
        static readonly string k_FillTooltip = L10n.Tr("Fill instances.");
        static readonly string k_ScaleTooltip = L10n.Tr("Scale instances.");
        static readonly string k_PropertiesTooltip = L10n.Tr("Paint instanced properties.\n\nHold control/cmd to reset properties to their default value.");
        
        public const string singleToolbarButtonClassName = "unity-editor-toolbar__button-strip-element--alone";
        
        public InstanceToolContextToolbar()
        {
            name = "InstanceToolContextToolbar";
            
            SceneViewToolbarStyles.AddStyleSheets(this);
            AddToClassList("toolbar-contents");
            
            Add(m_Settings = new EditorToolbarDropdown
            {
                text = L10n.Tr("Settings"),
                tooltip = k_SettingsTooltip,
            });
            m_Settings.AddToClassList(singleToolbarButtonClassName);
            m_Settings.clicked += OnSettingsClicked;
            
            Add(m_Place = new EditorToolbarToggle
            {
                tooltip = k_PlaceTooltip,
                onIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Place On Icon.png"),
                offIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Place Icon.png"),
            });
            m_Place.RegisterValueChangedCallback(OnPlaceToggled);

            Add(m_Paint = new EditorToolbarToggle
            {
                tooltip = k_PaintTooltip,
                onIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Paint On Icon.png"),
                offIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Paint Icon.png"),
            });
            m_Paint.RegisterValueChangedCallback(OnPaintToggled);
            
            Add(m_Erase = new EditorToolbarToggle
            {
                tooltip = k_EraseTooltip,
                onIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Erase On Icon.png"),
                offIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Erase Icon.png"),
            });
            m_Erase.RegisterValueChangedCallback(OnEraseToggled);
            
            Add(m_Fill = new EditorToolbarToggle
            {
                tooltip = k_FillTooltip,
                onIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Fill On Icon.png"),
                offIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Fill Icon.png"),
            });
            m_Fill.RegisterValueChangedCallback(OnFillToggled);
            
            Add(m_Scale = new EditorToolbarToggle
            {
                tooltip = k_ScaleTooltip,
                onIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Scale On Icon.png"),
                offIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Scale Icon.png"),
            });
            m_Scale.RegisterValueChangedCallback(OnScaleToggled);
            
            Add(m_Properties = new EditorToolbarToggle
            {
                tooltip = k_PropertiesTooltip,
                onIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Properties On Icon.png"),
                offIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Properties Icon.png"),
            });
            m_Properties.RegisterValueChangedCallback(OnPropertiesToggled);
            
            EditorToolbarUtility.SetupChildrenAsButtonStrip(this);
            
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        void OnSettingsClicked()
        {
            InstanceToolContextSettingsWindow.Show(m_Settings.worldBound);
        }

        void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            ToolManager.activeToolChanged += UpdateState;
            ToolManager.activeContextChanged += UpdateState;
            SceneViewMotionBridge.viewToolActiveChanged += UpdateState;
            UpdateState();
        }

        void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            ToolManager.activeToolChanged -= UpdateState;
            ToolManager.activeContextChanged -= UpdateState;
            SceneViewMotionBridge.viewToolActiveChanged -= UpdateState;
        }

        void UpdateState()
        {
            m_Place.SetValueWithoutNotify(ToolManager.activeToolType == typeof(InstancePlaceTool));
            m_Paint.SetValueWithoutNotify(ToolManager.activeToolType == typeof(InstancePaintTool));
            m_Erase.SetValueWithoutNotify(ToolManager.activeToolType == typeof(InstanceEraseTool));
            m_Fill.SetValueWithoutNotify(ToolManager.activeToolType == typeof(InstanceFillTool));
            m_Scale.SetValueWithoutNotify(ToolManager.activeToolType == typeof(InstanceScaleBrushTool));
            m_Properties.SetValueWithoutNotify(ToolManager.activeToolType == typeof(InstancePropertyBrushTool));
        }

        static void OnPlaceToggled(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
                ToolManager.SetActiveTool<InstancePlaceTool>();
            else 
                ToolManager.RestorePreviousPersistentTool();
        }
        
        static void OnPaintToggled(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
                ToolManager.SetActiveTool<InstancePaintTool>();
            else 
                ToolManager.RestorePreviousPersistentTool();
        }
        
        static void OnEraseToggled(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
                ToolManager.SetActiveTool<InstanceEraseTool>();
            else 
                ToolManager.RestorePreviousPersistentTool();
        }

        static void OnFillToggled(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
                ToolManager.SetActiveTool<InstanceFillTool>();
            else 
                ToolManager.RestorePreviousPersistentTool();
        }

        static void OnScaleToggled(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
                ToolManager.SetActiveTool<InstanceScaleBrushTool>();
            else 
                ToolManager.RestorePreviousPersistentTool();
        }
        
        static void OnPropertiesToggled(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
                ToolManager.SetActiveTool<InstancePropertyBrushTool>();
            else 
                ToolManager.RestorePreviousPersistentTool();
        }
    }
}