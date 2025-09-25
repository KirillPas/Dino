// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEngine.UIElements;
#if !UNITY_2022_3_OR_NEWER
using UnityEditor.UIElements;
#endif

namespace MA.Flora.Editor
{
    class SliderValueField : VisualElement
    {
        Slider m_Slider;
        FloatField m_ValueField;
        
        public SliderValueField(string label, float min, float max, float defaultValue)
        {
            style.flexDirection = FlexDirection.Row;
            
            m_Slider = new Slider(label, min, max);
            m_Slider.AddToClassList("slider");
            m_Slider.style.flexGrow = 1;
            m_Slider.RegisterValueChangedCallback(OnSliderValueChanged);
            Add(m_Slider);
            
            m_ValueField = new FloatField();
            m_ValueField.AddToClassList("value-field");
            m_ValueField.value = defaultValue;
            m_ValueField.style.minWidth = 30;
            m_ValueField.RegisterValueChangedCallback(OnValueFieldValueChanged);
            Add(m_ValueField);
        }
        
        void OnSliderValueChanged(ChangeEvent<float> evt)
        {
            m_ValueField.value = evt.newValue;
        }
        
        void OnValueFieldValueChanged(ChangeEvent<float> evt)
        {
            m_Slider.value = evt.newValue;
        }
        
        public Slider Slider => m_Slider;
        
        public FloatField ValueField => m_ValueField;
        
        public float Value
        {
            get => m_Slider.value;
            set
            {
                m_Slider.value = value;
                m_ValueField.value = value;
            }
        }
        
        public bool ShowMixedValue
        {
            get => m_Slider.showMixedValue;
            set
            {
                m_Slider.showMixedValue = value;
                m_ValueField.showMixedValue = value;
            }
        }

        public void SetValue(float value)
        {
            m_Slider.value = value;
            m_ValueField.value = value;
        }
        
        public void SetValueWithoutNotify(float value)
        {
            m_Slider.SetValueWithoutNotify(value);
            m_ValueField.SetValueWithoutNotify(value);
        }
        
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<float>> callback)
        {
            m_Slider.RegisterValueChangedCallback(callback);
            m_ValueField.RegisterValueChangedCallback(callback);
        }
        
        public void UnregisterValueChangedCallback(EventCallback<ChangeEvent<float>> callback)
        {
            m_Slider.UnregisterValueChangedCallback(callback);
            m_ValueField.UnregisterValueChangedCallback(callback);
        }
    }
}