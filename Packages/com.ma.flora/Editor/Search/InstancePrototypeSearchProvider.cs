// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.Search;

namespace MA.Flora.Editor
{
    static class InstancePrototypePicker
    {
        public static void Show(List<InstancedPrototype> excludeItems, bool multiselect, Action<InstancedPrototype[]> selectHandler)
        {
            SearchProvider searchProvider = InstancePrototypeSearchProvider.CreateProvider(true, excludeItems);
            searchProvider.showDetails = false;
            searchProvider.showDetailsOptions = ShowDetailsOptions.None;
            searchProvider.trackSelection = null; // Don't track selection

            SearchContext searchContext = SearchService.CreateContext(searchProvider, string.Empty);
            SearchViewState searchViewState = new SearchViewState(searchContext)
            {
                title = L10n.Tr("Instance Prototypes"),
                flags = SearchViewFlags.TableView
            };

            // Bridge required for Unity 2021.3
            SearchViewStateBridge.SetExcludeClearItem(searchViewState, true);
            SearchViewStateBridge.SetHideAllGroup(searchViewState, true);
            SearchViewStateBridge.SetHideTabs(searchViewState, true);
            SearchViewStateBridge.SetSelectHandler(searchViewState, (item, cancelled) =>
            {
                if (!cancelled && item.ToObject())
                {
                    InstancedPrototype[] selectedItems = item.context.selection
                        .Select(selectedItem => selectedItem.ToObject())
                        .Cast<GameObject>()
                        .Select(go => go.GetComponent<InstancedPrototype>())
                        .ToArray();

                    if (selectedItems.Length > 0)
                    {
                        selectHandler(selectedItems);
                    }
                }
            });

            ISearchView activeSearchView = SearchService.ShowPicker(searchViewState);
            activeSearchView.multiselect = multiselect;
        }
    }

    static class InstancePrototypeSearchProvider
    {
        [MenuItem("Window/Search/Instance Prototype", priority = 1300)]
        [MenuItem("Flora/Search/Instance Prototypes", priority = 20)]
        public static void OpenPrototypePicker()
        {
            SearchProvider provider = CreateProvider();
            provider.trackSelection = (item, context) => SetActive(item);

            SearchContext searchContext = SearchService.CreateContext(provider, string.Empty);
            SearchViewState searchViewState = new SearchViewState(searchContext)
            {
                flags = SearchViewFlags.GridView,
                title = L10n.Tr("Instance Prototypes"),
            };

            ISearchView view = SearchService.ShowPicker(searchViewState);
            view.multiselect = true;
        }

        [SearchItemProvider]
        public static SearchProvider CreateProvider() => CreateProvider(true, null);

        public static SearchProvider CreateProvider(bool showResultsAlways, List<InstancedPrototype> excludeItems)
        {
            return new SearchProvider("ip", L10n.Tr("Instance Prototypes"))
            {
                priority = 10,
                showDetails = true,
                fetchItems = (context, items, provider) => FetchItems(context, provider, showResultsAlways, excludeItems),
                fetchThumbnail = (item, context) => SearchUtility.FetchThumbnail(item),
                fetchPreview = SearchUtility.FetchPreview,
                fetchLabel = (item, context) => SearchUtility.FetchLabel(item),
                fetchDescription = (item, context) => SearchUtility.FetchDescription(item),
                toObject = (item, type) => SearchUtility.GetObject(item),
                trackSelection = (item, context) => SearchUtility.TrackSelection(item),
                startDrag = SearchUtility.StartDrag,
            };
        }

        static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider, bool showAlways, List<InstancedPrototype> excluded)
        {
            var searchQuery = context.searchQuery;
            if (!showAlways && string.IsNullOrEmpty(searchQuery))
                yield break;

            InstancedPrototype[] prototypes = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<InstancedPrototype>)
                .Where(instancePrototype => instancePrototype && (excluded == null || !excluded.Contains(instancePrototype)))
                .OrderBy(instancePrototype => instancePrototype.name)
                .ToArray();

            long score = 0;
            List<int> matches = new List<int>();

            foreach (InstancedPrototype prototype in prototypes)
            {
                string path = AssetDatabase.GetAssetPath(prototype);
                if (string.IsNullOrEmpty(context.searchQuery) || FuzzySearch.FuzzyMatch(context.searchQuery, $"{prototype.name}", ref score, matches))
                {
                    var info = new SearchUtility.AssetMetaInfo(path, SearchDocumentFlags.Asset);
                    yield return provider.CreateItem(context, path, ~(int)score, prototype.name, path, null, info);
                }

                score++;
            }
        }

        static void SetActive(in SearchItem item)
        {
            if (item.ToObject())
            {
                GameObject[] selectedItems = item.context.selection
                    .Select(selectedItem => selectedItem.ToObject())
                    .Cast<GameObject>()
                    .ToArray();

                Selection.objects = selectedItems;
            }
        }
    }
}
