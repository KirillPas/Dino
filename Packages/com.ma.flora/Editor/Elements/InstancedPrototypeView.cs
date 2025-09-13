// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MA.Core;
using MA.Core.Editor;
using MA.Core.Editor.Bridge;
using MA.UIElements;
using MA.UIElements.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    class InstancedPrototypeView : VisualElement
    {
        class AssetLabel : VisualElement
        {
            VisualElement m_Content;
            Label m_Label;

            public static readonly string ClassName = "asset-label";
            public static readonly string ContentClassName = ClassName.WithUssElement("content");
            public static readonly string LabelClassName = ContentClassName.WithUssElement("label");

            public event Action<AssetLabel> Clicked
            {
                add => m_Label.RegisterCallback<MouseDownEvent>(_ => value?.Invoke(this));
                remove => m_Label.UnregisterCallback<MouseDownEvent>(_ => value?.Invoke(this));
            }

            public AssetLabel(string label)
            {
                AddToClassList(ClassName);

                m_Content = new VisualElement();
                m_Content.AddToClassList(ContentClassName);
                m_Content.RegisterCallback<MouseOverEvent>(_ => m_Content.AddPseudoState(PseudoStates.Hover));
                m_Content.RegisterCallback<MouseOutEvent>(_ => m_Content.RemovePseudoState(PseudoStates.Hover));
                Add(m_Content);

                m_Label = new Label(label);
                m_Label.tooltip = $"Asset Label: {label}";
                m_Label.AddToClassList(LabelClassName);
                m_Content.Add(m_Label);
            }

            public string Text
            {
                get => m_Label.text;
                set
                {
                    m_Label.text = value;
                    m_Label.tooltip = $"Asset Label: {value}";
                }
            }

            public void SetActive(bool active)
            {
                if (active)
                    m_Content.AddPseudoState(PseudoStates.Active);
                else
                    m_Content.RemovePseudoState(PseudoStates.Active);
            }
        }

        readonly VisualElement m_Content;
        VisualElement m_Header;
        ToolbarSearchField m_SearchField;
        Image m_AddButton;
        VisualElement m_LabelContainer;
        List<AssetLabel> m_LabelElements = new List<AssetLabel>();
        VisualElement m_EmptyView;
        VisualElement m_EmptyIcon;
        VisualElement m_EmptyText;
        GridView m_GridView;
        VisualElement m_Footer;
        Image m_ResizeButton;
        Slider m_ItemSizeSlider;

        Delayer m_UpdateThrottler;
        Delayer m_SearchFieldDelayer;
        bool m_Searching;

        EditorWindow m_ContainerWindow;
        PreviewManager m_PreviewManager;
        int m_TextureCacheSize;
        int m_ViewID;

        List<string> m_AssetLabelNames = new List<string>();
        List<string> m_ActiveAssetLabelNames = new List<string>();

        Vector2 m_Size;
        float m_ItemSize;

        Vector2 m_InitialMousePosition;
        Vector2 m_InitialSize;
        bool m_Resizing;

        List<InstancedPrototypeItem> m_PreSearchItems = new List<InstancedPrototypeItem>();
        List<InstancedPrototypeItem> m_FilteredItems = new List<InstancedPrototypeItem>();
        HashSet<InstancedPrototypeItem> m_ActiveItems = new HashSet<InstancedPrototypeItem>();

        static readonly Color k_SearchPlaceholderColor = new Color(130 / 255f, 130 / 255f, 130 / 255f, 1);
        static readonly string k_SearchFieldPlaceholder = L10n.Tr("Prototypes");

        const string k_ItemSizePrefKey = "MA.Flora.PrototypeGridView.ItemSize";
        const string k_OverlaySizeXPrefKey = "MA.Flora.PrototypeGridView.SizeX";
        const string k_OverlaySizeYPrefKey = "MA.Flora.PrototypeGridView.SizeY";

        const float k_DefaultItemSize = 64;
        const float k_DefaultSizeX = 320;
        const float k_DefaultSizeY = 192;

        public static readonly string ClassName = "instanced-prototype-view";
        public static readonly string ContentClassName = ClassName.WithUssElement("content");

        public static readonly string HeaderClassName = ClassName.WithUssElement("header");
        public static readonly string SearchFieldClassName = HeaderClassName.WithUssElement("search-field");
        public static readonly string AddButtonClassName = HeaderClassName.WithUssElement("add-button");
        public static readonly string LabelContainerClassName = ClassName.WithUssElement("label-container");

        public static readonly string EmptyViewClassName = ClassName.WithUssElement("empty-view");
        public static readonly string EmptyViewIconClassName = EmptyViewClassName.WithUssElement("icon");
        public static readonly string EmptyLabelClassName = EmptyViewClassName.WithUssElement("text");

        public static readonly string GridViewClassName = ClassName.WithUssElement("grid-view");

        public static readonly string FooterClassName = ClassName.WithUssElement("footer");
        public static readonly string ItemSizeSliderClassName = FooterClassName.WithUssElement("item-size-slider");
        public static readonly string ResizeButtonClassName = FooterClassName.WithUssElement("resize-button");

        public PreviewManager PreviewManager => m_PreviewManager;
        public int ViewID => m_ViewID;

        public InstancedPrototypeView(InstancedPrototypeOverlay overlay)
        {
            m_ContainerWindow = overlay.containerWindow;
            m_ViewID = m_ContainerWindow.GetInstanceID();
            m_PreviewManager = new PreviewManager();

            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/Common.uss"));
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.ma.flora/Editor/EditorResources/USS/InstancedPrototypeView.uss"));

            AddToClassList(ClassName);

            m_ItemSize = EditorPrefs.GetFloat(k_ItemSizePrefKey, k_DefaultItemSize);
            m_Size.x = EditorPrefs.GetFloat(k_OverlaySizeXPrefKey, k_DefaultSizeX);
            m_Size.y = EditorPrefs.GetFloat(k_OverlaySizeYPrefKey, k_DefaultSizeY);

            m_Content = new VisualElement();
            m_Content.AddToClassList(ContentClassName);
            Add(m_Content);

            m_Header = new VisualElement();
            m_Header.AddToClassList(HeaderClassName);
            m_Content.Add(m_Header);

            m_SearchFieldDelayer = Delayer.Throttle(o => UpdateSearchFilter((string)o));
            m_SearchField = new ToolbarSearchField();
            m_SearchField.AddToClassList(SearchFieldClassName);
            m_SearchField.tooltip = "Filter prototypes by name or label.";
            m_SearchField.RegisterValueChangedCallback(evt => m_SearchFieldDelayer.Execute(evt.newValue));
            m_Header.Add(m_SearchField);

            m_AddButton = new Image();
            m_AddButton.AddToClassList(AddButtonClassName);
            m_AddButton.RegisterCallback<MouseOverEvent>(_ => m_AddButton.AddPseudoState(PseudoStates.Hover));
            m_AddButton.RegisterCallback<MouseOutEvent>(_ => m_AddButton.RemovePseudoState(PseudoStates.Hover));
            m_AddButton.RegisterCallback<MouseDownEvent>(_ =>
            {
                List<GameObject> excludedItems = m_FilteredItems.Select(im => im.Prototype.gameObject).ToList();
                ModelPicker.Show(excludedItems, true, selectedGameObjects =>
                {
                    foreach (GameObject gameObject in selectedGameObjects)
                    {
                        if (!gameObject)
                            continue;

                        if (!gameObject.TryGetComponent(out InstancedPrototype prototype))
                            prototype = Undo.AddComponent<InstancedPrototype>(gameObject);

                        if (m_FilteredItems.Any(p => p.Prototype == prototype))
                            continue;

                        m_FilteredItems.Add(new InstancedPrototypeItem(prototype, m_ViewID));
                    }

                    SyncItems();
                });
            });
            m_Header.Add(m_AddButton);

            m_LabelContainer = new VisualElement();
            m_LabelContainer.AddToClassList(LabelContainerClassName);
            m_LabelContainer.Hide();
            m_Content.Add(m_LabelContainer);

            m_EmptyView = new VisualElement();
            m_EmptyView.AddToClassList(EmptyViewClassName);
            m_EmptyView.style.display = DisplayStyle.None;
            m_Content.Add(m_EmptyView);

            m_EmptyIcon = new VisualElement();
            m_EmptyIcon.AddToClassList(EmptyViewIconClassName);
            m_EmptyView.Add(m_EmptyIcon);

            m_EmptyText = new VisualElement();
            m_EmptyText.AddToClassList(EmptyLabelClassName);
            m_EmptyText.Add(new Label("Drag prototypes here or use the add button."));
            m_EmptyView.Add(m_EmptyText);

            m_FilteredItems.AddRange(InstanceToolContextShared.Prototypes.Select(p => new InstancedPrototypeItem(p, m_ViewID)));
            m_ActiveItems.UnionWith(InstanceToolContextShared.ActivePrototypes.Select(p => new InstancedPrototypeItem(p, m_ViewID)));

            m_GridView = new GridView(m_FilteredItems, m_ItemSize, m_ItemSize, MakeItem, BindItem);
            m_GridView.AddToClassList(GridViewClassName);
            m_GridView.UnbindItem = UnbindItem;
            m_GridView.DestroyItem = DestroyItem;
            m_GridView.SelectionType = SelectionType.Multiple;
            m_GridView.RegisterCallback<KeyUpEvent>(evt =>
            {
                if (Application.platform != RuntimePlatform.OSXEditor && evt.keyCode == KeyCode.Delete ||
                    Application.platform == RuntimePlatform.OSXEditor && evt.keyCode == KeyCode.Backspace && evt.modifiers.HasFlag(EventModifiers.Command))
                {
                    foreach (int i in m_GridView.SelectedIndices.OrderByDescending(i => i))
                    {
                        InstancedPrototypeItem item = m_FilteredItems[i];
                        RemoveItemFromEverything(item);
                    }

                    SyncItems();
                }

                if ((Application.platform != RuntimePlatform.OSXEditor && evt.keyCode == KeyCode.F && evt.modifiers.HasFlag(EventModifiers.Control)) ||
                    (Application.platform == RuntimePlatform.OSXEditor && evt.keyCode == KeyCode.F && evt.modifiers.HasFlag(EventModifiers.Command)))
                {
                    m_SearchField.Focus();
                    // Line below is required to make sure focus is in textfield
                    m_SearchField.Q<TextField>()?.Q<VisualElement>(className: TextField.inputUssClassName)?.Focus();
                }
            });
            m_Content.Add(m_GridView);

            m_Footer = new VisualElement();
            m_Footer.AddToClassList(FooterClassName);
            m_Content.Add(m_Footer);

            m_ItemSizeSlider = new Slider();
            m_ItemSizeSlider.AddToClassList(ItemSizeSliderClassName);
            m_ItemSizeSlider.lowValue = 32;
            m_ItemSizeSlider.highValue = 128;
            m_ItemSizeSlider.value = m_ItemSize;
            m_ItemSizeSlider.showInputField = false;
            m_ItemSizeSlider.tooltip = "Item Size";
            m_Footer.Add(m_ItemSizeSlider);

            m_ResizeButton = new Image();
            m_ResizeButton.AddToClassList(ResizeButtonClassName);
            m_Footer.Add(m_ResizeButton);

            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
            RegisterCallback<DragUpdatedEvent>(_ =>
            {
                GameObject[] gameObjects = DragAndDrop.objectReferences.OfType<GameObject>().ToArray();
                HashSet<GameObject> dragPrefabs = GetFilteredPrefabsFrom(gameObjects);
                DragAndDrop.visualMode = dragPrefabs.Count > 0 ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            });
            RegisterCallback<DragPerformEvent>(_ =>
            {
                DragAndDrop.AcceptDrag();

                GameObject[] gameObjects = DragAndDrop.objectReferences.OfType<GameObject>().ToArray();
                HashSet<GameObject> dragPrefabs = GetFilteredPrefabsFrom(gameObjects);

                if (dragPrefabs.Count > 0)
                {
                    foreach (GameObject gameObject in dragPrefabs)
                    {
                        if (!gameObject)
                            continue;

                        if (!gameObject.TryGetComponent(out InstancedPrototype prototype))
                            prototype = Undo.AddComponent<InstancedPrototype>(gameObject);

                        m_FilteredItems.Add(new InstancedPrototypeItem(prototype, m_ViewID));
                    }

                    SyncItems();
                }
            });

            RebuildAssetLabels();
            UpdateView();
        }

        HashSet<GameObject> GetFilteredPrefabsFrom(GameObject[] gameObjects)
        {
            HashSet<GameObject> prefabs = new HashSet<GameObject>(gameObjects.Length);

            foreach (GameObject gameObject in gameObjects)
            {
                GameObject prefab = gameObject;
                if (!PrefabUtility.IsPartOfPrefabAsset(prefab))
                    prefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);

                if (m_FilteredItems.Any(im => im.Prototype.gameObject == prefab))
                    continue;

                prefabs.Add(prefab);
            }

            return prefabs;
        }

        void Refresh()
        {
            // We are throttling the update of the view so that we don't call
            // RefreshItems on the list view too often. We chose a throttle
            // delay of 50ms (20fps), which we believe still gives a good enough visual
            // feedback. If the throttler is not currently throttling, the execution will go through
            // which means there is no delay.
            m_UpdateThrottler?.Execute();
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        void UpdateView()
        {
            if (m_FilteredItems.Count == 0)
            {
                m_EmptyView.Show();
                m_GridView.Hide();
            }
            else
            {
                m_EmptyView.Hide();
                m_GridView.Show();
            }

            m_FilteredItems.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            m_GridView.FixedItemWidth = m_ItemSize;
            m_GridView.FixedItemHeight = m_ItemSize;
            m_GridView.ComputeGridSize();
            m_GridView.RefreshItems();
            UpdatePreviewManagerCacheSize();
        }

        void Rebuild()
        {
            m_GridView.FixedItemWidth = m_ItemSize;
            m_GridView.FixedItemHeight = m_ItemSize;
            m_GridView.Rebuild();
        }

        void RemoveItemFromEverything(InstancedPrototypeItem item)
        {
            m_FilteredItems.Remove(item);
            m_ActiveItems.Remove(item);
            m_PreSearchItems.Remove(item);
        }

        // --- Events ---

        const double k_ResultViewUpdateThrottleDelay = 0.05d;

        void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            m_UpdateThrottler = Delayer.Throttle(o =>
            {
                UpdateView();
            }, TimeSpan.FromSeconds(k_ResultViewUpdateThrottleDelay), true);

            InstancedPrototypeViewItem.ActiveToggled += OnItemActiveToggled;
            InstancedPrototypeViewItem.RemoveRequested += OnItemRemoveRequested;
            Undo.undoRedoPerformed += OnUndoRedo;

            m_GridView.SelectedIndicesChanged += OnGridSelectionChanged;

            m_ItemSizeSlider.RegisterValueChangedCallback(OnItemSizeSliderValueChanged);
            m_ResizeButton.RegisterCallback<MouseOverEvent>(OnResizeButtonMouseOver);
            m_ResizeButton.RegisterCallback<MouseOutEvent>(OnResizeButtonMouseOut);
            m_ResizeButton.RegisterCallback<MouseDownEvent>(OnResizeButtonMouseDown);

            RegisterCallback<MouseUpEvent>(OnMouseUp);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            RebuildAssetLabels();
            m_Content.style.width = m_Size.x;
            m_Content.style.height = m_Size.y;
        }

        void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            m_UpdateThrottler?.Dispose();
            m_SearchFieldDelayer.Abort();

            m_GridView.SelectedIndicesChanged -= OnGridSelectionChanged;

            m_ItemSizeSlider.UnregisterValueChangedCallback(OnItemSizeSliderValueChanged);
            m_ResizeButton.UnregisterCallback<MouseOverEvent>(OnResizeButtonMouseOver);
            m_ResizeButton.UnregisterCallback<MouseOutEvent>(OnResizeButtonMouseOut);
            m_ResizeButton.UnregisterCallback<MouseDownEvent>(OnResizeButtonMouseDown);

            UnregisterCallback<MouseUpEvent>(OnMouseUp);
            UnregisterCallback<PointerDownEvent>(OnPointerDown);
            UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            EditorPrefs.SetFloat(k_ItemSizePrefKey, m_ItemSize);
            EditorPrefs.SetFloat(k_OverlaySizeXPrefKey, m_Size.x);
            EditorPrefs.SetFloat(k_OverlaySizeYPrefKey, m_Size.y);

            Undo.undoRedoPerformed -= OnUndoRedo;
            InstancedPrototypeViewItem.ActiveToggled -= OnItemActiveToggled;
            InstancedPrototypeViewItem.RemoveRequested -= OnItemRemoveRequested;

            if (!m_Searching)
            {
                InstanceToolContextShared.Prototypes = m_FilteredItems.Select(item => item.Prototype).ToList();
                InstanceToolContextShared.ActivePrototypes = m_ActiveItems.Select(item => item.Prototype).ToList();
            }

            if (m_ViewID != 0)
                AssetPreviewBridge.DeletePreviewTextureManagerByID(m_ViewID);
        }

        void OnUndoRedo()
        {
            m_FilteredItems.Clear();
            m_FilteredItems.AddRange(InstanceToolContextShared.Prototypes.Select(p => new InstancedPrototypeItem(p, m_ViewID)));

            m_ActiveItems.Clear();
            m_ActiveItems.UnionWith(InstanceToolContextShared.ActivePrototypes.Select(p => new InstancedPrototypeItem(p, m_ViewID)));

            RebuildAssetLabels();
            Refresh();
        }

        void OnMouseUp(MouseUpEvent evt)
        {
            EndResize();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.clickCount != 1 && evt.button != 0)
                return;

            if (evt.target is not VisualElement ve)
                return;

            if (ve is not InstancedPrototypeViewItem && ve.GetFirstAncestorOfType<InstancedPrototypeViewItem>() == null)
                m_GridView.ClearSelection();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            Refresh();
        }

        void OnItemSizeSliderValueChanged(ChangeEvent<float> evt)
        {
            float newSize = Mathf.Round(evt.newValue);
            if (m_ItemSize != newSize)
            {
                m_ItemSize = newSize;
                m_GridView.FixedItemWidth = newSize;
                m_GridView.FixedItemHeight = newSize;
                m_GridView.Rebuild();
            }
        }

        // --- Grid View ---

        InstancedPrototypeViewItem MakeItem() => new(m_PreviewManager);

        void BindItem(VisualElement element, int index)
        {
            InstancedPrototypeViewItem e = (InstancedPrototypeViewItem)element;
            if (index >= 0 && index < m_FilteredItems.Count)
                e.Bind(m_FilteredItems[index]);
        }

        void UnbindItem(VisualElement element, int index)
        {
            InstancedPrototypeViewItem e = (InstancedPrototypeViewItem)element;
            e.Unbind();
        }

        void DestroyItem(VisualElement element)
        {
            InstancedPrototypeViewItem e = (InstancedPrototypeViewItem)element;
            e.Destroy();
        }

        void SyncItems()
        {
            InstanceToolContextShared.Prototypes = m_FilteredItems.Select(item => item.Prototype).ToList();
            RebuildAssetLabels();
            Refresh();
        }

        void OnGridSelectionChanged(IEnumerable<int> selectedIndices)
        {
            Selection.objects = m_FilteredItems.Count > 0
                ? selectedIndices.Select(i => m_FilteredItems[i].Prototype.gameObject).ToArray()
                : Array.Empty<UnityEngine.Object>();
        }

        void OnItemActiveToggled(InstancedPrototypeItem item, bool active)
        {
            Undo.RecordObject(InstanceToolContextShared.instance, "Toggle Active Prototype");

            int[] selectedIndices = m_GridView.SelectedIndices.ToArray();
            HashSet<InstancedPrototypeItem> activeSelected = selectedIndices.Select(i => m_FilteredItems[i]).ToHashSet();

            if (active)
            {
                m_ActiveItems.Add(item);
                switch (selectedIndices.Length)
                {
                    case > 1:
                        m_ActiveItems.UnionWith(activeSelected);
                        break;
                    case 0:
                        m_GridView.SetSelection(m_FilteredItems.IndexOf(item));
                        break;
                }
            }
            else
            {
                m_ActiveItems.Remove(item);
                switch (selectedIndices.Length)
                {
                    case > 1:
                        m_ActiveItems.ExceptWith(activeSelected);
                        break;
                    case 1:
                        m_GridView.ClearSelection();
                        break;
                }
            }

            InstanceToolContextShared.ActivePrototypes = m_ActiveItems.Select(i => i.Prototype).ToList();
            foreach (GridView.ReusableGridViewItem gridItem in m_GridView.ActiveItems)
            {
                InstancedPrototypeViewItem viewItem = (InstancedPrototypeViewItem)gridItem.BindableElement;
                viewItem.SetActiveWithoutNotify(m_ActiveItems.Contains(viewItem.BindedItem));
            }

            Refresh();
        }

        void OnItemRemoveRequested(InstancedPrototypeItem item)
        {
            Undo.RecordObject(InstanceToolContextShared.instance, "Remove Prototype");

            int[] selectedIndices = m_GridView.SelectedIndices.ToArray();
            foreach (int i in selectedIndices.OrderByDescending(i => i))
                m_FilteredItems.RemoveAt(i);

            m_FilteredItems.Remove(item);
            m_ActiveItems.Remove(item);
            SyncItems();
        }

        // --- Preview Manager ---

        int ComputeVisibleItemCapacity(float width, float height)
        {
            // Approximation of how many we can fit.
            width /= m_ItemSize;
            height /= m_ItemSize;
            return (int)(width * height);
        }

        void UpdatePreviewManagerCacheSize()
        {
            float width = worldBound.width;
            float height = worldBound.height;
            if (width <= 0 || float.IsNaN(width) || height <= 0 || float.IsNaN(height))
                return;

            // Note: We approximate how many items could be displayed in the current Rect. We cannot rely on the ResultView to have
            // an exact list of visibleItems since get updated AFTER our resize handler and we need to update the Cache size so preview are properly generated.

            int potentialVisibleItems = Mathf.Min(ComputeVisibleItemCapacity(width, height), m_FilteredItems.Count);
            int newTextureCacheSize = Mathf.Max(potentialVisibleItems * 2 + 30, 128);
            if (potentialVisibleItems == 0 || newTextureCacheSize <= m_TextureCacheSize)
                return;

            m_PreviewManager.PoolSize = newTextureCacheSize;
            m_TextureCacheSize = newTextureCacheSize;
            AssetPreviewBridge.SetPreviewTextureCacheSize(m_TextureCacheSize, m_ViewID);
        }

        // --- Asset Labels ---

        void RebuildAssetLabels()
        {
            m_AssetLabelNames.Clear();
            HashSet<string> labelSet = new HashSet<string>();
            foreach (string label in InstanceToolContextShared.Prototypes.Select(AssetDatabase.GetLabels).SelectMany(labels => labels))
                labelSet.Add(label);

            foreach (string label in labelSet)
                m_AssetLabelNames.Add(label);

            if (m_AssetLabelNames.Count > 0)
                m_AssetLabelNames.Sort((a, b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase));

            m_LabelContainer.style.display = m_AssetLabelNames.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (m_LabelElements.Count < m_AssetLabelNames.Count)
            {
                int count = m_AssetLabelNames.Count - m_LabelElements.Count;
                for (int i = 0; i < count; ++i)
                {
                    AssetLabel assetLabel = new AssetLabel(string.Empty);
                    assetLabel.Clicked += OnAssetLabelClicked;
                    m_LabelContainer.Add(assetLabel);
                    m_LabelElements.Add(assetLabel);
                }
            }
            else if (m_LabelElements.Count > m_AssetLabelNames.Count)
            {
                int count = m_LabelElements.Count - m_AssetLabelNames.Count;
                for (int i = count - 1; i >= 0; --i)
                {
                    AssetLabel assetLabel = m_LabelElements[^1];
                    assetLabel.Clicked -= OnAssetLabelClicked;
                    m_LabelContainer.Remove(assetLabel);
                    m_LabelElements.Remove(assetLabel);
                }
            }

            for (int i = 0; i < m_LabelElements.Count; i++)
                m_LabelElements[i].Text = m_AssetLabelNames[i];
        }

        void OnAssetLabelClicked(AssetLabel assetLabel)
        {
            int index = m_AssetLabelNames.IndexOf(assetLabel.Text);
            if (index >= 0)
            {
                if (m_ActiveAssetLabelNames.Contains(assetLabel.Text))
                {
                    m_ActiveAssetLabelNames.Remove(assetLabel.Text);
                    assetLabel.SetActive(false);
                }
                else
                {
                    m_ActiveAssetLabelNames.Add(assetLabel.Text);
                    assetLabel.SetActive(true);
                }

                UpdateSearchFilter(m_SearchField.value);
                EditorApplication.delayCall += Rebuild;
            }
        }

        // --- Resize ---

        bool m_ResizeInitialSaved;

        void OnResizeButtonMouseOver(MouseOverEvent evt)
        {
            m_ResizeButton.AddPseudoState(PseudoStates.Active);
        }

        void OnResizeButtonMouseOut(MouseOutEvent evt)
        {
            if (!m_Resizing)
                m_ResizeButton.RemovePseudoState(PseudoStates.Active);
        }

        void OnResizeButtonMouseDown(MouseDownEvent evt)
        {
            BeginResize();
        }

        void BeginResize()
        {
            if (!m_Resizing)
            {
                m_Resizing = true;
                m_ResizeInitialSaved = false;
                m_ResizeButton.AddPseudoState(PseudoStates.Active);
                Refresh();
                SceneView.duringSceneGui += OnSceneGUI;
            }
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (m_Resizing)
            {
                Event evt = Event.current;
                ResizeWithMouse(evt.mousePosition);
                if (evt.type == EventType.MouseUp)
                    EndResize();

                if (evt.type is not (EventType.Layout or EventType.Repaint))
                    evt.Use();

                Refresh();
            }
        }

        void ResizeWithMouse(Vector2 mousePosition)
        {
            if (m_Resizing)
            {
                if (!m_ResizeInitialSaved)
                {
                    m_InitialMousePosition = mousePosition;
                    m_InitialSize = m_Size;
                    m_ResizeInitialSaved = true;
                }

                Vector2 delta = mousePosition - m_InitialMousePosition;
                m_Size = Vector2.Max(m_InitialSize + delta, Vector3.one * 128);
                m_Content.style.width = m_Size.x;
                m_Content.style.height = m_Size.y;
                Refresh();
            }
        }

        void EndResize()
        {
            if (m_Resizing)
            {
                m_Resizing = false;
                m_ResizeButton.RemovePseudoState(PseudoStates.Active);
                Refresh();
                SceneView.duringSceneGui -= OnSceneGUI;
            }
        }

        // --- Search ---

        HashSet<InstancedPrototypeItem> m_UniqueItems = new HashSet<InstancedPrototypeItem>();

        void UpdateSearchFilter(string searchText)
        {
            if (string.IsNullOrEmpty(searchText) && m_ActiveAssetLabelNames.Count == 0)
            {
                // No search text or labels
                m_UniqueItems.Clear();
                m_UniqueItems.UnionWith(m_PreSearchItems);
                m_UniqueItems.UnionWith(InstanceToolContextShared.Prototypes.Select(p => new InstancedPrototypeItem(p, m_ViewID)));

                m_FilteredItems.Clear();
                m_FilteredItems.AddRange(m_UniqueItems);

                m_PreSearchItems.Clear();
                m_Searching = false;
                Rebuild();
                return;
            }

            if (!m_Searching)
            {
                m_PreSearchItems.Clear();
                m_PreSearchItems.AddRange(m_FilteredItems);
            }

            // Extract labels from the search text
            searchText ??= string.Empty;
            StringBuilder searchTextBuilder = new StringBuilder(searchText);
            HashSet<string> labelsInSearch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int labelIndex = searchText.IndexOf("l:", StringComparison.OrdinalIgnoreCase);
            while (labelIndex >= 0)
            {
                int nextSpaceIndex = searchText.IndexOf(' ', labelIndex);
                if (nextSpaceIndex < 0)
                    nextSpaceIndex = searchText.Length;

                string label = searchText.Substring(labelIndex + 2, nextSpaceIndex - labelIndex - 2);
                if (m_AssetLabelNames.Contains(label))
                    labelsInSearch.Add(label);

                searchTextBuilder.Remove(labelIndex, nextSpaceIndex - labelIndex);
                labelIndex = searchTextBuilder.ToString().IndexOf("l:", StringComparison.OrdinalIgnoreCase);
            }

            // Search prototypes based on the modified search text
            string searchTextWithoutLabels = searchTextBuilder.ToString();
            bool hasSearchString = !string.IsNullOrEmpty(searchTextWithoutLabels);
            m_FilteredItems.Clear();
            foreach (InstancedPrototype prototype in InstanceToolContextShared.Prototypes)
            {
                if (hasSearchString && prototype.name.IndexOf(searchTextWithoutLabels, StringComparison.OrdinalIgnoreCase) >= 0)
                    m_FilteredItems.Add(new InstancedPrototypeItem(prototype, m_ViewID));

                // Add prototypes that have the searched labels
                string[] prototypeLabels = AssetDatabase.GetLabels(prototype);
                if (prototypeLabels.Length > 0)
                {
                    if (labelsInSearch.Any(label => prototypeLabels.Contains(label, StringComparer.OrdinalIgnoreCase)))
                        m_FilteredItems.Add(new InstancedPrototypeItem(prototype, m_ViewID));

                    if (m_ActiveAssetLabelNames.Any(label => prototypeLabels.Contains(label, StringComparer.OrdinalIgnoreCase)))
                        m_FilteredItems.Add(new InstancedPrototypeItem(prototype, m_ViewID));
                }
            }

            m_FilteredItems.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            m_Searching = true;
            m_GridView.ClearSelection();
            Refresh();
        }
    }
}
