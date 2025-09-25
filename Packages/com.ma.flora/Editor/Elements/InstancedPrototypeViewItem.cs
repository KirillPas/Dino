// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Core.Editor.Bridge;
using MA.UIElements;
using MA.UIElements.Editor;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{

    class InstancedPrototypeViewItem : VisualElement
    {
        InstancedPrototypeItem m_BindedItem;

        VisualElement m_Content;
        Toggle m_ActiveToggle;
        Image m_Thumbnail;

        PreviewKey m_PreviewKey;
        PreviewManager m_PreviewManager;
        Action m_FetchPreviewOff;
        IVisualElementScheduledItem m_PreviewRefreshCallback;

        public static readonly string GridItemClassName = "instanced-prototype-grid-item";
        public static readonly string ContentClassName = GridItemClassName.WithUssElement("content");
        public static readonly string ThumbnailClassName = ContentClassName.WithUssElement("thumbnail");
        public static readonly string ActiveToggleClassName = ContentClassName.WithUssElement("active-toggle");

        public static event Action<InstancedPrototypeItem, bool> ActiveToggled;
        public static event Action<InstancedPrototypeItem> RemoveRequested;

        public InstancedPrototypeItem BindedItem => m_BindedItem;

        public InstancedPrototypeViewItem(PreviewManager previewManager)
        {
            m_PreviewManager = previewManager;

            AddToClassList(GridItemClassName);

            Add(m_Content = new VisualElement());
            m_Content.AddToClassList(ContentClassName);

            m_Content.Add(m_Thumbnail = new Image());
            m_Thumbnail.AddToClassList(ThumbnailClassName);

            m_Content.Add(m_ActiveToggle = new Toggle());
            m_ActiveToggle.AddToClassList(ActiveToggleClassName);
            m_ActiveToggle.RegisterValueChangedCallback(evt =>
            {
                if (m_BindedItem != null)
                {
                    ActiveToggled?.Invoke(m_BindedItem, evt.newValue);
                    evt.StopImmediatePropagation();
                }
            });

            style.flexDirection = FlexDirection.Column;

            RegisterContextualMenus();
            RegisterCallback<MouseOverEvent>(OnMouseOver);
            RegisterCallback<MouseOutEvent>(OnMouseOut);
            RegisterCallback<ClickEvent>(OnDoubleClick);
        }

        // --- Events ---

        void OnMouseOver(MouseOverEvent evt)
            => this.AddPseudoState(PseudoStates.Hover);

        void OnMouseOut(MouseOutEvent evt)
            => this.RemovePseudoState(PseudoStates.Hover);

        void OnDoubleClick(ClickEvent evt)
        {
            if (evt.clickCount != 2)
                return;

            if (m_BindedItem != null)
            {
                EditorGUIUtility.PingObject(m_BindedItem.Prototype);
            }
        }

        // --- Element ---

        public void SetActiveWithoutNotify(bool value)
        {
            m_ActiveToggle.SetValueWithoutNotify(value);
            if (value) this.AddPseudoState(PseudoStates.Active);
            else       this.RemovePseudoState(PseudoStates.Active);
        }

        void RegisterContextualMenus()
        {
            DropdownMenu menu = new DropdownMenu();
            menu.AppendAction("Activate", MenuActivate, MenuActivateStatus, this);
            menu.AppendAction("Deactivate", MenuDeactivate, MenuDeactivateStatus, this);
            menu.AppendAction("Remove", MenuRemove, DropdownMenuAction.AlwaysEnabled, this);

            this.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Activate", MenuActivate, MenuActivateStatus, this);
                evt.menu.AppendAction("Deactivate", MenuDeactivate, MenuDeactivateStatus, this);
                evt.menu.AppendAction("Remove", MenuRemove, DropdownMenuAction.AlwaysEnabled, this);
            }));

            return;

            void MenuActivate(DropdownMenuAction a)
            {
                if (m_BindedItem == null) return;
                ActiveToggled?.Invoke(m_BindedItem, true);
            }

            DropdownMenuAction.Status MenuActivateStatus(DropdownMenuAction a)
            {
                if (m_BindedItem == null) return DropdownMenuAction.Status.Disabled;
                return InstanceToolContextShared.IsActive(m_BindedItem.Prototype) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            }

            void MenuDeactivate(DropdownMenuAction a)
            {
                if (m_BindedItem == null) return;
                ActiveToggled?.Invoke(m_BindedItem, false);
            }

            DropdownMenuAction.Status MenuDeactivateStatus(DropdownMenuAction a)
            {
                if (m_BindedItem == null) return DropdownMenuAction.Status.Disabled;
                return InstanceToolContextShared.IsActive(m_BindedItem.Prototype) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            }

            void MenuRemove(DropdownMenuAction a)
            {
                if (m_BindedItem == null) return;
                RemoveRequested?.Invoke(m_BindedItem);
            }
        }

        // --- Item ---

        const int k_PreviewFetchCounter = 5;

        public void Bind(InstancedPrototypeItem item)
        {
            name = item.Name;
            tooltip = item.Tooltip;

            m_BindedItem = item;
            SetActiveWithoutNotify(InstanceToolContextShared.IsActive(m_BindedItem.Prototype));

            UpdatePreview();

            int counter = 0;
            m_PreviewRefreshCallback = m_Thumbnail.schedule.Execute(() =>
            {
                if (m_FetchPreviewOff == null)
                    UpdatePreview();
            });
            m_PreviewRefreshCallback.StartingIn(500).Every(500).Until(() =>
            {
                counter++;
                return m_PreviewManager.HasPreview(m_PreviewKey) || counter >= k_PreviewFetchCounter;
            });
        }

        public void Unbind()
        {
            name = "";
            tooltip = "";

            m_Thumbnail.image = null;
            SetActiveWithoutNotify(false);

            if (m_BindedItem != null)
            {
                m_BindedItem.Preview = null;
                m_BindedItem.Thumbnail = null;
                m_BindedItem = null;
            }

            if (m_PreviewRefreshCallback?.isActive == true)
                m_PreviewRefreshCallback.Pause();
        }

        public void Destroy()
        {
            CancelFetchPreview();

            UnregisterCallback<ClickEvent>(OnDoubleClick);
            UnregisterCallback<MouseOverEvent>(OnMouseOver);
            UnregisterCallback<MouseOutEvent>(OnMouseOut);
        }

        // --- Preview ---

        void CancelFetchPreview()
        {
            m_PreviewManager.CancelFetch(m_PreviewKey);
            m_PreviewKey = default;
            if (m_FetchPreviewOff != null)
            {
                m_FetchPreviewOff?.Invoke();
                m_FetchPreviewOff = null;
            }
        }

        static FetchPreviewOptions GetPreviewOptions() => FetchPreviewOptions.Normal | FetchPreviewOptions.Preview2D;

        bool ShouldFetchPreview() => !m_PreviewManager.HasPreview(m_PreviewKey);

        bool IsSizeValid(out Vector2 size)
        {
            size = default;
            if (m_Thumbnail == null)
                return false;

            size.x = m_Thumbnail.resolvedStyle.width;
            if (float.IsNaN(size.x) || size.x <= 0)
                return false;

            size.y = m_Thumbnail.resolvedStyle.height;
            if (float.IsNaN(size.y) || size.y <= 0)
                return false;

            return true;
        }

        bool GetExistingPreview()
        {
            if (m_PreviewManager.HasPreview(m_PreviewKey))
            {
                PreviewItem preview = m_PreviewManager.FetchPreview(m_PreviewKey);
                if (preview.Valid)
                {
                    m_Thumbnail.image = preview.Texture;
                    m_BindedItem.Preview = preview.Texture;
                    return true;
                }
            }

            return false;
        }

        void UpdatePreview()
        {
            if (GetExistingPreview())
                return;

            if (ShouldFetchPreview())
            {
                m_FetchPreviewOff?.Invoke();
                AsyncFetchPreview();
            }

            Texture2D tex = m_BindedItem.GetThumbnail(null, cacheThumbnail: false);
            m_Thumbnail.image = tex;
            m_BindedItem.Thumbnail = tex;
        }

        void AsyncFetchPreview()
        {
            if (m_BindedItem == null)
                return;

            if (IsSizeValid(out Vector2 previewSize))
            {
                m_PreviewKey = new PreviewKey(m_BindedItem, GetPreviewOptions(), previewSize);
                if (GetExistingPreview())
                    return;

                m_FetchPreviewOff = m_PreviewManager.FetchPreview(m_BindedItem, null, m_PreviewKey, FetchPreview, OnPreviewReady);
            }
            else
            {
                m_FetchPreviewOff = EditorApplicationBridge.CallDelayed(AsyncFetchPreview, 0.01d); // To make sure the style is resolved.
            }
        }

        void FetchPreview(object item, object context, FetchPreviewOptions options, Vector2 size, OnPreviewReady onPreviewReady)
        {
            PreviewItem searchPreview;
            if (item == null || m_BindedItem == null)
            {
                searchPreview = PreviewItem.Invalid;
            }
            else
            {
                Texture2D fetchedPreview = m_BindedItem.GetPreview(context, size, options, cacheThumbnail: false);
                searchPreview = new PreviewItem(m_PreviewKey, fetchedPreview);
            }

            onPreviewReady?.Invoke(item, context, searchPreview);
        }

        void OnPreviewReady(object item, object context, PreviewItem preview)
        {
            if (preview.Valid && m_BindedItem != null && m_Thumbnail != null)
            {
                Texture2D fetchedPreview = preview.Texture;
                if (fetchedPreview != null && fetchedPreview.width > 0 && fetchedPreview.height > 0)
                {
                    m_Thumbnail.image = fetchedPreview;
                    m_BindedItem.Preview = fetchedPreview;
                }
            }

            if (m_Thumbnail != null && m_BindedItem != null && m_Thumbnail.image == null)
            {
                Texture2D tex = m_BindedItem.GetThumbnail(context, cacheThumbnail: false);
                m_Thumbnail.image = tex;
                m_BindedItem.Thumbnail = tex;
            }

            m_FetchPreviewOff = null;
        }
    }
}
