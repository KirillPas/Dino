// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
#if UNITY_2023_1_OR_NEWER
using MA.Core.Editor.Bridge;
#endif
#if !UNITY_2022_2_OR_NEWER
using UnityEditor.UIElements;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    class CondensedSlider : VisualElement, INotifyValueChanged<float>
    {
        /// <summary>
        /// this is a variable of type func which takes in a float, string, Slider direction and returns a string
        /// the internal stuff is what it starts out as
        /// so maybe declare this as a property and give it a get; set; and set the value in the constructor like line 55 or something
        /// </summary>
        public Func<float, string, SliderDirection, string> LabelFormatting
        {
            get;
            set;
        }

        public Slider Slider => m_Slider;
        Slider m_Slider;

        public VisualElement Image => m_Image;
        VisualElement m_Image;

        public VisualElement DragContainer => m_DragContainer;
        VisualElement m_DragContainer;

        public string Label => m_Label;
        string m_Label;

        public Label LabelField => m_LabelField;
        Label m_LabelField;

        public Texture2D Icon => m_Icon;
        Texture2D m_Icon;

        /// <see cref="Slider.value"/>
        public float value
        {
            get => m_Slider.value;
            set => SetValue(value);
        }

        /// <see cref="IStyle.width"/>
        public StyleLength ContentWidth
        {
            get => m_Slider.style.width;
            set => m_Slider.style.width = value;
        }

        /// <see cref="IStyle.height"/>
        public StyleLength ContentHeight
        {
            get => m_Slider.style.height;
            set => m_Slider.style.height = value;
        }

        /// <see cref="BaseSlider{t}.direction"/>
        public SliderDirection Direction
        {
            get =>  m_Slider.direction;
            set => m_Slider.direction = value;
        }

        public CondensedSlider(float min, float max, SliderDirection direction = SliderDirection.Horizontal)
            : this(null, null, min, max, direction)
        {
        }

        public CondensedSlider(string label, float min, float max, SliderDirection direction = SliderDirection.Horizontal)
            : this(label, null, min, max, direction)
        {
        }

        public CondensedSlider(Texture2D icon, float min, float max, SliderDirection direction = SliderDirection.Horizontal)
            : this(null, icon, min, max, direction)
        {
        }

        public CondensedSlider(string label, Texture2D icon, float min, float max, SliderDirection direction = SliderDirection.Horizontal)
        {
            m_Label = label;
            tooltip = label;
            m_Icon = icon;
            CreateSlider(min, max, direction);
        }

        void UpdateValue(float newValue, bool withoutNotify)
        {
            if (withoutNotify)
                m_Slider.SetValueWithoutNotify(newValue);
            else
                m_Slider.value = newValue;

            float size = (newValue - m_Slider.lowValue) / (m_Slider.highValue - m_Slider.lowValue);
            if (m_Slider.direction == SliderDirection.Horizontal)
                m_Slider.Q("unity-tracker").style.width = new StyleLength(new Length(size * 100, LengthUnit.Percent));
            else
            {
                m_Slider.Q("unity-tracker").style.height = new StyleLength(new Length(size * 100, LengthUnit.Percent));
                m_Slider.Q("unity-tracker").style.top = new StyleLength(new Length((1 - size) * 100, LengthUnit.Percent));
            }

            m_LabelField.text = LabelFormatting(m_Slider.value, m_Label, Direction);
        }

        public void SetHighValueWithoutNotify(float highValue)
        {
            m_Slider.highValue = highValue;
            UpdateValue(m_Slider.value, true);
        }

        public void SetLowValueWithoutNotify(float lowValue)
        {
            m_Slider.lowValue = lowValue;
            UpdateValue(m_Slider.value, true);
        }

        void SetValue(float newValue)
        {
            UpdateValue(newValue, false);
        }

        public void SetValueWithoutNotify(float newValue)
        {
            UpdateValue(newValue, true);
        }

        public static VisualElement GetDragContainer(Slider slider)
        {
            return slider.Q("unity-drag-container");
        }

        static StyleSheet s_CondensedSliderCommonStyleSheet;

        public void CreateSlider(float min, float max, SliderDirection direction)
        {
            LabelFormatting = (value, label, direction)
                => direction == SliderDirection.Horizontal ? $"{label} {value:F0}" : $"{value:F0}";

#if UNITY_2023_1_OR_NEWER
            EditorToolbarUtilityBridge.LoadStyleSheets("CondensedSlider", this);
#else
            if (s_CondensedSliderCommonStyleSheet == null)
                s_CondensedSliderCommonStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/CondensedSliderCommon.uss");

            styleSheets.Add(s_CondensedSliderCommonStyleSheet);
#endif
            string directionClassSuffix = direction == SliderDirection.Horizontal ? "horizontal" : "vertical";

            AddToClassList("condensed-slider");
            AddToClassList("condensed-slider--"+directionClassSuffix);

            m_Slider = new Slider("", min, max, direction);
            m_Slider.AddToClassList("condensed-slider__slider");
            m_Slider.AddToClassList("condensed-slider__slider--"+directionClassSuffix);
            Add(m_Slider);

            m_DragContainer = GetDragContainer(Slider);

            VisualElement slider = m_Slider.Q("unity-tracker");
            slider.ClearClassList();
            slider.AddToClassList("condensed-slider__slider-tracker");

            m_Slider.Q("unity-dragger").style.display = DisplayStyle.None;
            m_Slider.Q("unity-dragger-border").style.display = DisplayStyle.None;

            VisualElement content = new VisualElement();
            content.name = "content";
            content.AddToClassList("condensed-slider__content--"+directionClassSuffix);
            content.pickingMode = PickingMode.Ignore;
            m_Slider.Add(content);

            m_Image = new VisualElement();
            m_Image.AddToClassList("condensed-slider__image");
            m_Image.AddToClassList("condensed-slider__image--"+directionClassSuffix);
            m_Image.pickingMode = PickingMode.Ignore;
            if (m_Image == null)
                m_Image.style.display = DisplayStyle.None;
            else
                m_Image.style.backgroundImage = m_Icon;
            content.Add(m_Image);

            m_LabelField = new Label(LabelFormatting(m_Slider.value, m_Label, direction));
            m_LabelField.AddToClassList("condensed-slider__label");
            m_LabelField.AddToClassList("condensed-slider__label--"+directionClassSuffix);
            m_LabelField.pickingMode = PickingMode.Ignore;
            content.Add(m_LabelField);

            VisualElement contentTextField = new VisualElement();
            contentTextField.name = "contentTextField";
            contentTextField.AddToClassList("condensed-slider__content-textfield--"+directionClassSuffix);
            contentTextField.pickingMode = PickingMode.Ignore;
            m_Slider.Add(contentTextField);

            FloatField textField = new FloatField();
            textField.name = "textField";
            textField.AddToClassList("condensed-slider__label");
            textField.AddToClassList("condensed-slider__textfield--"+directionClassSuffix);
            textField.style.display = DisplayStyle.None;
            if (m_Icon != null)
            {
                if (direction == SliderDirection.Horizontal)
                    textField.style.marginLeft = 23;
                else
                    textField.style.marginTop = 34;
            }

            contentTextField.Add(textField);

            RegisterCallback<AttachToPanelEvent>(RegisterCallbacks);
            RegisterCallback<DetachFromPanelEvent>(UnregisterCallbacks);
        }

        void RegisterCallbacks(AttachToPanelEvent e)
        {
            FloatField textField = m_Slider.Q("textField") as FloatField;
            VisualElement contentTextField = m_Slider.Q("contentTextField");
            m_Slider.RegisterCallback<GeometryChangedEvent>(OnSliderRectChange);
            m_Slider.RegisterCallback<GeometryChangedEvent>(OnSliderWidthChange);
            textField.RegisterValueChangedCallback(TextFieldValueChange);
            m_Slider.RegisterValueChangedCallback(SliderSetValue);
            // Register Mouse Up/Down in TrickleDown to stop propagation before triggering ContextualMenu
            m_Slider.RegisterCallback<MouseDownEvent>(SliderMouseDownEvent, TrickleDown.TrickleDown);
            m_Slider.RegisterCallback<MouseUpEvent>(SliderMouseUpEvent, TrickleDown.TrickleDown);
            contentTextField.RegisterCallback<MouseDownEvent>(TextFieldMouseDownEvent);
            textField.RegisterCallback<KeyDownEvent>(TextFieldKeyDownEvent);
        }

        void UnregisterCallbacks(DetachFromPanelEvent e)
        {
            FloatField textField = m_Slider.Q("textField") as FloatField;
            VisualElement contentTextField = m_Slider.Q("contentTextField");
            m_Slider.UnregisterCallback<GeometryChangedEvent>(OnSliderRectChange);
            m_Slider.UnregisterCallback<GeometryChangedEvent>(OnSliderWidthChange);
            textField.UnregisterValueChangedCallback(TextFieldValueChange);
            m_Slider.UnregisterValueChangedCallback(SliderSetValue);
            m_Slider.UnregisterCallback<MouseDownEvent>(SliderMouseDownEvent, TrickleDown.TrickleDown);
            m_Slider.UnregisterCallback<MouseUpEvent>(SliderMouseUpEvent, TrickleDown.TrickleDown);
            contentTextField.UnregisterCallback<MouseDownEvent>(TextFieldMouseDownEvent);
            textField.UnregisterCallback<KeyDownEvent>(TextFieldKeyDownEvent);
        }

        void OnSliderRectChange(GeometryChangedEvent e)
        {
            VisualElement slider = m_Slider.Q("unity-tracker");
            if (Direction == SliderDirection.Horizontal)
                slider.style.height = e.newRect.height;
            else
                slider.style.width = e.newRect.width;
        }

        void OnSliderWidthChange(GeometryChangedEvent e)
        {
            VisualElement content = m_Slider.Q("content");
            content.style.width = e.newRect.width;
        }

        void TextFieldValueChange(ChangeEvent<float> e)
        {
            UpdateValue(e.newValue, false);
        }

        void SliderSetValue(ChangeEvent<float> e)
        {
            SetValueWithoutNotify(e.newValue);
        }

        void SliderMouseDownEvent(MouseDownEvent e)
        {
            if (e.button == (int)MouseButton.RightMouse)
            {
                e.StopPropagation();

                FloatField textField = m_Slider.Q("textField") as FloatField;
                VisualElement contentTextField = m_Slider.Q("contentTextField");

                if (textField != null)
                {
                    textField.value = m_Slider.value;
                    textField.style.display = DisplayStyle.Flex;
                }

                m_LabelField.style.display = DisplayStyle.None;
                contentTextField.pickingMode = PickingMode.Position;
            }
        }

        void SliderMouseUpEvent(MouseUpEvent e)
        {
            if (e.button == (int)MouseButton.RightMouse)
            {
                e.StopPropagation();
            }
        }

        void TextFieldMouseDownEvent(MouseDownEvent e)
        {
            FloatField textField = m_Slider.Q("textField") as FloatField;
            VisualElement contentTextField = m_Slider.Q("contentTextField");

            contentTextField.pickingMode = PickingMode.Ignore;
            if (textField != null)
            {
                if (textField.style.display == DisplayStyle.Flex)
                {
                    textField.style.display = DisplayStyle.None;
                    m_LabelField.style.display = DisplayStyle.Flex;
                }
            }
        }

        void TextFieldKeyDownEvent(KeyDownEvent e)
        {
            FloatField textField = m_Slider.Q("textField") as FloatField;
            VisualElement contentTextField = m_Slider.Q("contentTextField");

            if (e.keyCode is KeyCode.Escape or KeyCode.Return)
            {
                contentTextField.pickingMode = PickingMode.Ignore;

                if (textField != null)
                {
                    if (textField.style.display == DisplayStyle.Flex)
                    {
                        textField.style.display = DisplayStyle.None;
                        m_LabelField.style.display = DisplayStyle.Flex;
                    }
                }
            }
        }

        public void UpdateDirection(SliderDirection newDirection, float min, float max)
        {
            if (Direction == newDirection) return; // no new direction
            Direction = newDirection;
            Clear();
            ClearClassList();
            CreateSlider(min, max, newDirection);
        }
    }

    class CondensedSliderDropdown : CondensedSlider
    {
        Button m_Dropdown;

        public event Action Clicked
        {
            add => m_Dropdown.clicked += value;
            remove => m_Dropdown.clicked -= value;
        }

        public CondensedSliderDropdown(float min, float max, Action clicked, SliderDirection direction = SliderDirection.Horizontal)
            : this(null, null, min, max, clicked, direction)
        { }

        public CondensedSliderDropdown(string label, float min, float max, Action clicked, SliderDirection direction = SliderDirection.Horizontal)
            : this(label, null, min, max, clicked, direction)
        { }

        public CondensedSliderDropdown(Texture2D image, float min, float max, Action clicked, SliderDirection direction = SliderDirection.Horizontal)
            : this(null, image, min, max, clicked, direction)
        { }

        public CondensedSliderDropdown(string label, Texture2D icon, float min, float max, Action clicked, SliderDirection direction = SliderDirection.Horizontal)
            : base(label, icon, min, max, direction)
        {
            string directionClassSuffix = direction == SliderDirection.Horizontal ? "horizontal" : "vertical";

            m_Dropdown = new Button(clicked);
            Add(m_Dropdown);
            m_Dropdown.ClearClassList();
            m_Dropdown.AddToClassList("unity-base-popup-field__arrow");
            m_Dropdown.AddToClassList("condensed-slider__dropdown--"+directionClassSuffix);
        }

        public void DropdownUpdateDirection(SliderDirection newDirection, Action clicked, float min, float max)
        {
            Remove(m_Dropdown);
            UpdateDirection(newDirection, min, max);

            string directionClassSuffix = Direction == SliderDirection.Horizontal ? "horizontal" : "vertical";

            m_Dropdown = new Button(clicked);
            Add(m_Dropdown);
            m_Dropdown.ClearClassList();
            m_Dropdown.AddToClassList("unity-base-popup-field__arrow");
            m_Dropdown.AddToClassList("condensed-slider__dropdown--"+directionClassSuffix);
        }
    }
}
