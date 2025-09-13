// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Collections.Generic;
using MA.Core;
using MA.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    class InstancedPropertyItem : VisualElement
    {
        Toggle m_Active;
        Label m_Name;
        EnumField m_Type;
        VisualElement m_Value;
        InstancedPropertyDescriptor m_Descriptor;

        static readonly string k_ClassName = "instanced-property-view-item";
        static readonly string k_ActiveClassName = k_ClassName.WithUssElement("active");
        static readonly string k_NameClassName = k_ClassName.WithUssElement("name");
        static readonly string k_TypeClassName = k_ClassName.WithUssElement("type");
        static readonly string k_ValueClassName = k_ClassName.WithUssElement("value");

        public InstancedPropertyItem()
        {
            AddToClassList(k_ClassName);

            m_Active = new Toggle();
            m_Active.AddToClassList(k_ActiveClassName);
            Add(m_Active);

            m_Name = new Label();
            m_Name.AddToClassList(k_NameClassName);
            Add(m_Name);

            m_Type = new EnumField(InstancedPropertyType.Color);
            m_Type.AddToClassList(k_TypeClassName);
            m_Type.SetEnabled(false);
            Add(m_Type);
        }

        public void Bind(InstancedPropertyDescriptor descriptor)
        {
            bool isActive = InstanceToolContextShared.ActiveProperties.Contains(descriptor);

            m_Descriptor = descriptor;
            m_Name.text = descriptor.Name;
            m_Type.value = descriptor.Type;

            if (m_Value != null)
                Remove(m_Value);

            if (InstanceTool.Active is InstancePropertyBrushTool propertyBrush)
            {
                switch (descriptor.Type)
                {
                    case InstancedPropertyType.Color:
                    {
                        ColorField field = new ColorField();
                        field.AddToClassList(k_ValueClassName);
                        field.RegisterValueChangedCallback(OnValueChanged<Color>);
                        Add(m_Value = field);

                        Vector4 paintValue = descriptor.GetDefaultValue<Vector4>();
                        field.SetValueWithoutNotify(paintValue);

                        if (isActive)
                            propertyBrush.SetPaintValue(descriptor.Name, paintValue);

                        break;
                    }
                    case InstancedPropertyType.Float4:
                    {
                        Vector4Field field = new Vector4Field();
                        field.AddToClassList(k_ValueClassName);
                        field.RegisterValueChangedCallback(OnValueChanged<Vector4>);
                        Add(m_Value = field);

                        Vector4 paintValue = descriptor.GetDefaultValue<Vector4>();
                        field.SetValueWithoutNotify(paintValue);

                        if (isActive)
                            propertyBrush.SetPaintValue(descriptor.Name, paintValue);

                        break;
                    }
                }
            }

            m_Value?.SetEnabled(isActive);
            m_Active.SetValueWithoutNotify(isActive);
            m_Active.UnregisterValueChangedCallback(OnActiveChanged);
            m_Active.RegisterValueChangedCallback(OnActiveChanged);
        }

        public void Unbind()
        {
            m_Active.UnregisterValueChangedCallback(OnActiveChanged);

            if (m_Value != null)
            {
                Remove(m_Value);
                m_Value = null;
            }
        }

        public void Destroy()
        {
        }

        void OnActiveChanged(ChangeEvent<bool> evt)
        {
            List<InstancedPropertyDescriptor> activeDescriptors = InstanceToolContextShared.ActiveProperties;
            if (evt.newValue) activeDescriptors.Add(m_Descriptor);
            else activeDescriptors.Remove(m_Descriptor);
            InstanceToolContextShared.ActiveProperties = activeDescriptors;

            if (m_Value != null && evt.newValue)
            {
                m_Value.SetEnabled(evt.newValue);

                if (InstanceTool.Active is InstancePropertyBrushTool propertyBrush)
                {
                    switch (m_Descriptor.Type)
                    {
                        case InstancedPropertyType.Color:
                        {
                            propertyBrush.SetPaintValue(m_Descriptor.Name, ((ColorField)m_Value).value);
                            break;
                        }
                        case InstancedPropertyType.Float4:
                        {
                            propertyBrush.SetPaintValue(m_Descriptor.Name, ((Vector4Field)m_Value).value);
                            break;
                        }
                    }
                }
            }
        }

        void OnValueChanged<T>(ChangeEvent<T> evt)
        {
            if (!m_Active.value)
                return;

            if (InstanceTool.Active is not InstancePropertyBrushTool propertyBrush)
                return;

            switch (m_Descriptor.Type)
            {
                case InstancedPropertyType.Color:
                {
                    propertyBrush.SetPaintValue(m_Descriptor.Name, ((ColorField)m_Value).value);
                    break;
                }
                case InstancedPropertyType.Float4:
                {
                    propertyBrush.SetPaintValue(m_Descriptor.Name, ((Vector4Field)m_Value).value);
                    break;
                }
            }
        }
    }

    class InstancedPropertyView : VisualElement
    {
        VisualElement m_Content;
        ListView m_PropertyList;
        List<InstancedPropertyDescriptor> m_FilteredItems = new List<InstancedPropertyDescriptor>();

        VisualElement m_EmptyView;
        VisualElement m_EmptyIcon;
        VisualElement m_EmptyText;

        static readonly string k_ClassName = "instanced-property-view";
        static readonly string k_ContentClassName = k_ClassName.WithUssElement("content");
        static readonly string k_InstancedPropertiesClassName = k_ContentClassName.WithUssElement("property-list");

        static readonly string k_EmptyViewClassName = k_ClassName.WithUssElement("empty-view");
        static readonly string k_EmptyViewIconClassName = k_EmptyViewClassName.WithUssElement("icon");
        static readonly string k_EmptyLabelClassName = k_EmptyViewClassName.WithUssElement("text");

        public InstancedPropertyView()
        {
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/Common.uss"));
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/InstancedPropertyView.uss"));

            AddToClassList(k_ClassName);

            m_Content = new VisualElement();
            m_Content.AddToClassList(k_ContentClassName);
            Add(m_Content);

            m_FilteredItems.Clear();
            m_FilteredItems.AddRange(InstanceToolContextShared.Properties);

            m_PropertyList = new ListView();
            m_PropertyList.AddToClassList(k_InstancedPropertiesClassName);
            m_PropertyList.makeItem = MakeItem;
            m_PropertyList.bindItem = BindItem;
            m_PropertyList.unbindItem = UnbindItem;
            m_PropertyList.destroyItem = DestroyItem;
            m_PropertyList.itemsSource = m_FilteredItems;
            m_Content.Add(m_PropertyList);

            m_PropertyList.style.flexShrink = 1;

            m_EmptyView = new VisualElement();
            m_EmptyView.AddToClassList(k_EmptyViewClassName);
            m_EmptyView.style.display = DisplayStyle.None;
            m_Content.Add(m_EmptyView);

            m_EmptyIcon = new VisualElement();
            m_EmptyIcon.AddToClassList(k_EmptyViewIconClassName);
            m_EmptyView.Add(m_EmptyIcon);

            m_EmptyText = new VisualElement();
            m_EmptyText.AddToClassList(k_EmptyLabelClassName);
            m_EmptyText.Add(new Label(L10n.Tr("No prototypes contain instanced properties.")));
            m_EmptyView.Add(m_EmptyText);

            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            InstanceToolContextShared.PropertiesChanged += UpdateView;
            UpdateView();
        }

        void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            InstanceToolContextShared.Save();
            InstanceToolContextShared.PropertiesChanged -= UpdateView;
        }

        void UpdateView()
        {
            if (m_FilteredItems.Count == 0)
            {
                m_EmptyView.Show();
                m_PropertyList.Hide();
            }
            else
            {
                m_EmptyView.Hide();
                m_PropertyList.Show();
            }

            m_FilteredItems.Clear();
            m_FilteredItems.AddRange(InstanceToolContextShared.Properties);
            m_PropertyList.Rebuild();
        }

        InstancedPropertyItem MakeItem() => new();

        void BindItem(VisualElement element, int index)
        {
            if (element is InstancedPropertyItem item)
                item.Bind(m_FilteredItems[index]);
        }

        void UnbindItem(VisualElement element, int index)
        {
            InstancedPropertyItem e = (InstancedPropertyItem)element;
            e.Unbind();
        }

        void DestroyItem(VisualElement element)
        {
            InstancedPropertyItem e = (InstancedPropertyItem)element;
            e.Destroy();
        }
    }
}
