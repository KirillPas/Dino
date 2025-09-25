// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using MA.Core.Editor.Bridge;
using MA.UIElements;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    class PlacementOptionsView : VisualElement
    {
        IconField<LayerMaskField> m_LayerMask;
        IconField<MaskField> m_ObjectMask;

        VisualElement m_ToolOptions;

        public static readonly string ClassName = "placement-options-view";
        public static readonly string LayerMaskFieldClassName = ClassName.WithUssElement("layer-mask");
        public static readonly string ObjectMaskFieldClassName = ClassName.WithUssElement("object-mask");

        public static readonly string ToolOptionsClassName = ClassName.WithUssElement("tool-options");

        static List<string> s_ObjectMaskChoices = new List<string>
        {
            "Mesh",
            "Terrain",
            "Linked Objects",
        };

        static List<int> s_ObjectMaskValues = new List<int>
        {
            (int) PlacementObjectMask.Mesh,
            (int) PlacementObjectMask.Terrain,
            (int) PlacementObjectMask.LinkedObject,
        };

        public PlacementOptionsView()
        {
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/Common.uss"));
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/PlacementOptionsView.uss"));

            AddToClassList(ClassName);

            var layerMaskField = new LayerMaskField(L10n.Tr("Layer Mask"), -1);
            m_LayerMask = new IconField<LayerMaskField>(layerMaskField);
            m_LayerMask.AddToClassList(LayerMaskFieldClassName);
            m_LayerMask.Icon.image = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/LayerMask Icon.png");
            Add(m_LayerMask);

            var objectMaskField = new MaskField(L10n.Tr("Object Mask"), s_ObjectMaskChoices, (int)PlacementObjectMask.Default);
            objectMaskField.choicesMasks = s_ObjectMaskValues;

            m_ObjectMask = new IconField<MaskField>(objectMaskField);
            m_ObjectMask.AddToClassList(ObjectMaskFieldClassName);
            m_ObjectMask.Icon.image = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/ObjectMask Icon.png");
            Add(m_ObjectMask);

            m_ToolOptions = new VisualElement();
            m_ToolOptions.AddToClassList(ToolOptionsClassName);
            Add(m_ToolOptions);

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            m_LayerMask.Field.RegisterValueChangedCallback(OnLayerMaskChanged);
            m_ObjectMask.Field.RegisterValueChangedCallback(OnObjectMaskChanged);
            UpdateValues();

            ToolManager.activeToolChanged += OnToolChanged;
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            ToolManager.activeToolChanged -= OnToolChanged;

            m_LayerMask.Field.UnregisterValueChangedCallback(OnLayerMaskChanged);
            m_ObjectMask.Field.UnregisterValueChangedCallback(OnObjectMaskChanged);
        }

        void OnToolChanged()
        {
            m_ToolOptions.Clear();

            if (InstanceTool.Active is InstancePlacementTool placementTool)
            {
                foreach (VisualElement option in placementTool.OverlayOptionalElements)
                    m_ToolOptions.Add(option);
            }
        }

        void OnLayerMaskChanged(ChangeEvent<int> evt)
        {
            if (InstanceTool.Active is InstancePlacementTool placementTool)
            {
                placementTool.PlacementLayerMask = evt.newValue;
            }
        }

        void OnObjectMaskChanged(ChangeEvent<int> evt)
        {
            if (InstanceTool.Active is InstancePlacementTool placementTool)
            {
                placementTool.PlacementObjectMask = (PlacementObjectMask)evt.newValue;
            }
        }

        void UpdateValues()
        {
            if (InstanceTool.Active is InstancePlacementTool placementTool)
            {
                m_LayerMask.Field.SetValueWithoutNotify(placementTool.PlacementLayerMask);
                m_ObjectMask.Field.SetValueWithoutNotify((int)placementTool.PlacementObjectMask);
            }
        }
    }
}
