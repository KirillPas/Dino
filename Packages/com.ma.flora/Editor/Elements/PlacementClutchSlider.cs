// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.UIElements.Editor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{ 
    class PlacementClutchSlider : CondensedSlider
    {
        OverlayToolbar m_ParentToolbar;
        PlacementClutchShortcutType m_ClutchType;
        float m_MinValue;
        float m_MaxValue;
        Texture2D m_ActiveIcon;
        Action<float> m_SetClutchValue;
        Func<float> m_GetClutchValue;
        
        public void UpdateOverlayDirection(Layout l)
        {
            UpdateDirection(l == Layout.VerticalToolbar ? SliderDirection.Vertical : SliderDirection.Horizontal, m_MinValue, m_MaxValue);
            LabelFormatting = (f, s, d) =>
            {
                if (Direction == SliderDirection.Vertical)
                    return $"{f:F2}";
                return $"{s} {f:F2}";
            };
            UpdateValues();
        }

        public PlacementClutchSlider(
            SliderDirection direction, PlacementClutchShortcutType type, string label, string tooltip, Texture2D icon, Texture2D iconActive, 
            float min, float max, Func<float> getClutchValue, Action<float> setClutchValue)
            : base(label, icon, min, max, direction)
        {
            m_ClutchType = type;
            m_MinValue = min;
            m_MaxValue = max;
            m_ActiveIcon = iconActive;
            m_SetClutchValue = setClutchValue;
            m_GetClutchValue = getClutchValue;
            LabelFormatting = (f, s, _) => Direction == SliderDirection.Vertical ? $"{f:F2}" : $"{s} {f:F2}";
            this.tooltip = tooltip;

            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
            
            if (Direction == SliderDirection.Horizontal)
                ContentWidth = 110;

            UpdateValues();
        }
        
        void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            m_ParentToolbar = null;
            
            VisualElement currentParent = parent;
            while (m_ParentToolbar == null && currentParent != null)
            {
                m_ParentToolbar = parent as OverlayToolbar;
                if (m_ParentToolbar != null)
                    break;
                    
                currentParent = currentParent.parent;
            }

            if (m_ParentToolbar != null)
            {
                
            }
            
            ToolManager.activeToolChanged += UpdateValues;
            ToolManager.activeContextChanged += UpdateValues;
            InstancePlacementTool.ClutchValueUpdated += OnClutchValueUpdated;
            InstancePlacementTool.ClutchStageChanged += OnClutchStageUpdated;

            DragContainer.RegisterCallback<MouseDownEvent>(_ => { UpdateActive(true); });
            DragContainer.RegisterCallback<PointerDownEvent>(_ => { UpdateActive(true); });
            DragContainer.RegisterCallback<MouseUpEvent>(_ => { UpdateActive(false); });
            DragContainer.RegisterCallback<PointerUpEvent>(_ => { UpdateActive(true); });

            this.RegisterValueChangedCallback(e =>
            {
                m_SetClutchValue(e.newValue);
            });

            if (Direction == SliderDirection.Horizontal)
                ContentWidth = 110;

            UpdateValues();
        }
        
        void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            ToolManager.activeToolChanged -= UpdateValues;
            ToolManager.activeContextChanged -= UpdateValues;
            InstancePlacementTool.ClutchValueUpdated -= OnClutchValueUpdated;
            InstancePlacementTool.ClutchStageChanged -= OnClutchStageUpdated;
        }

        void OnClutchValueUpdated(PlacementClutchShortcutType type, float f)
        {
            if (type == m_ClutchType)
                UpdateValues();
        }

        void OnClutchStageUpdated(PlacementClutchShortcutType type, ShortcutStage stage)
        {
            if (type == m_ClutchType)
                UpdateActive(stage == ShortcutStage.Begin);
        }

        void UpdateValues()
        {
            value = m_GetClutchValue();
        }

        void UpdateActive(bool active)
        {
            if (active)
            {
                DragContainer.AddPseudoState(PseudoStates.Active);
                Image.style.backgroundImage = m_ActiveIcon;
            }
            else
            {
                DragContainer.RemovePseudoState(PseudoStates.Active);
                Image.style.backgroundImage = Icon;
            }
        }
    }
}