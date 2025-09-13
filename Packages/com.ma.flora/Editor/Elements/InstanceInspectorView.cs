// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.UIElements;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    class InstanceInspectorView : VisualElement, IDisposable
    {
        static readonly string k_NoSelectionMessage = L10n.Tr("No element selected");
        
        public static bool IgnoreModificationCallbacks = false;
        
        readonly Label m_ErrorMessage;
        
        readonly VisualElement m_Root;
        IconField<SelectionFloat3Field> m_Position;
        IconField<SelectionFloat3Field> m_Rotation;
        IconField<SelectionFloat3Field> m_Scale;
        
        public static readonly string ClassName = "instance-inspector-view";
        public static readonly string PositionClassName = ClassName.WithUssElement("position");
        public static readonly string RotationClassName = ClassName.WithUssElement("rotation");
        public static readonly string ScaleClassName = ClassName.WithUssElement("scale");
        
        public InstanceInspectorView()
        {
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/Common.uss"));
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/InstanceInspectorView.uss"));
            
            Add(m_Root = new VisualElement());
            m_Root.AddToClassList(ClassName);

            var positionField = new SelectionFloat3Field("Position",
                (instance, index) => instance.GetInstancePosition(index, Space.World),
                (instance, index, value) => instance.UpdateInstancePosition(index, value, Space.World));
            m_Root.Add(m_Position = new IconField<SelectionFloat3Field>(positionField));
            m_Position.AddToClassList(PositionClassName);
            m_Position.name = "Position";
            m_Position.tooltip = L10n.Tr("Instance Position");
            m_Position.style.flexDirection = FlexDirection.Row;
            m_Position.style.flexGrow = 1;
            
            var rotationField = new SelectionFloat3Field("Rotation",
                (instance, index) => ((Quaternion)instance.GetInstanceRotation(index, Space.World)).eulerAngles,
                (instance, index, value) => instance.UpdateInstanceRotation(index, Quaternion.Euler(value), Space.World));
            m_Root.Add(m_Rotation = new IconField<SelectionFloat3Field>(rotationField));
            m_Rotation.AddToClassList(RotationClassName);
            m_Rotation.name = "Rotation";
            m_Rotation.tooltip = L10n.Tr("Instance Rotation");
            m_Rotation.style.flexDirection = FlexDirection.Row;
            m_Rotation.style.flexGrow = 1;
            
            var scaleField = new SelectionFloat3Field("Scale",
                (instance, index) => instance.GetInstanceScale(index, Space.World),
                (instance, index, value) => instance.UpdateInstanceScale(index, value, Space.World));
            m_Root.Add(m_Scale = new IconField<SelectionFloat3Field>(scaleField));
            m_Scale.AddToClassList(ScaleClassName);
            m_Scale.name = "Scale";
            m_Scale.tooltip = L10n.Tr("Instance Scale");
            m_Scale.style.flexDirection = FlexDirection.Row;
            m_Scale.style.flexGrow = 1;
            
            Add(m_ErrorMessage = new Label { name = "ErrorMessage"});
            
            UpdateWithSelection();
            InstancedMeshContainer.AfterRendererWasModified += (c) => OnRendererModified();
        }

        public void Dispose()
        {
            InstancedMeshContainer.AfterRendererWasModified -= (c) => OnRendererModified();
        }
        
        public void UpdateWithSelection()
        {
            InstanceSelectionGroup[] selectedInstances = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
            if (selectedInstances.Length < 1)
            {
                ShowErrorMessage(k_NoSelectionMessage);
                m_Position.style.display = DisplayStyle.None;
                m_Rotation.style.display = DisplayStyle.None;
                m_Scale.style.display = DisplayStyle.None;
            }
            else
            {
                HideErrorMessage();
                m_Position.style.display = DisplayStyle.Flex;
                m_Rotation.style.display = DisplayStyle.Flex;
                m_Scale.style.display = DisplayStyle.Flex;
            }
            
            m_Position.Field.Update(selectedInstances);
            m_Rotation.Field.Update(selectedInstances);
            m_Scale.Field.Update(selectedInstances);
        }

        void OnRendererModified()
        {
            if (IgnoreModificationCallbacks)
                return;
            
            InstanceSelectionGroup[] selectedInstances = Selection.GetFiltered<InstanceSelectionGroup>(SelectionMode.Unfiltered);
            m_Position.Field.Update(selectedInstances);
            m_Rotation.Field.Update(selectedInstances);
            m_Scale.Field.Update(selectedInstances);
        }

        void ShowErrorMessage(string error)
        {
            m_ErrorMessage.style.display = DisplayStyle.Flex;
            m_ErrorMessage.text = error;
        }

        void HideErrorMessage()
        {
            m_ErrorMessage.style.display = DisplayStyle.None;
        }
    }
}