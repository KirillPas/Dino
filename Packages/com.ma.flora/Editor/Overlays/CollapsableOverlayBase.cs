// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.InternalBridge;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    abstract class CollapsableOverlayBase : Overlay
    {
        VisualElement m_CollapsedContent;

        public override void OnCreated()
        {
            layoutChanged += OnLayoutChanged;
            UpdateCollapsedContent();
        }

        public override void OnWillBeDestroyed()
        {
            layoutChanged -= OnLayoutChanged;
        }

        protected virtual void OnLayoutChanged(Layout layout)
        {
            UpdateCollapsedContent();
        }

        void UpdateCollapsedContent()
        {
            bool collapsed = this.IsCollapsed();
            if (collapsed)
            {
                m_CollapsedContent = this.GetRootVisualElement().Q<VisualElement>("overlay-collapsed-content");
                if (m_CollapsedContent != null)
                {
                    m_CollapsedContent.AddToClassList("unity-editor-toolbar-element");
                    Label icon = this.GetRootVisualElement().Q<Label>("unity-overlay-collapsed-dropdown__icon");
                    icon?.AddToClassList("unity-editor-toolbar-element__icon");
                }
            }
        }
    }
}
