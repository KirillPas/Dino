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
    static class ModelPicker
    {
        public static void Show(List<GameObject> excludeItems, bool multiselect, Action<GameObject[]> selectHandler)
        {
            SearchProvider searchProvider = ModelSearchProvider.CreateProvider(true, excludeItems);
            searchProvider.showDetails = false;
            searchProvider.showDetailsOptions = ShowDetailsOptions.None;
            searchProvider.trackSelection = null; // Don't track selection

            SearchContext searchContext = SearchService.CreateContext(searchProvider, string.Empty);
            SearchViewState searchViewState = new SearchViewState(searchContext)
            {
                title = L10n.Tr("Models"),
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
                    GameObject[] selectedItems = item.context.selection
                        .Select(selectedItem => selectedItem.ToObject())
                        .Cast<GameObject>()
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

    static class ModelSearchProvider
    {
        [SearchItemProvider]
        public static SearchProvider CreateProvider() => CreateProvider(false, null);

        public static SearchProvider CreateProvider(bool showResultsAlways, List<GameObject> excludeItems)
        {
            return new SearchProvider("model", L10n.Tr("Models"))
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

        static bool FilterGameObject(GameObject go, List<GameObject> excluded)
        {
            return go
                   && (excluded == null || !excluded.Contains(go))
                   && PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.Model
                   && (go.GetComponent<MeshRenderer>() || go.GetComponent<LODGroup>());
        }

        static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider, bool showAlways, List<GameObject> excluded)
        {
            if (!showAlways && string.IsNullOrEmpty(context.searchQuery) && context.filterId == null)
                yield break;

            GameObject[] gameObjects = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(go => FilterGameObject(go, excluded))
                .OrderBy(go => go.name)
                .ToArray();

            long score = 0;
            List<int> matches = new List<int>();

            foreach (GameObject go in gameObjects)
            {
                string path = AssetDatabase.GetAssetPath(go);
                if (string.IsNullOrEmpty(context.searchQuery) || FuzzySearch.FuzzyMatch(context.searchQuery, $"{go.name}", ref score, matches))
                {
                    var info = new SearchUtility.AssetMetaInfo(path, SearchDocumentFlags.Asset);
                    yield return provider.CreateItem(context, path, ~(int)score, go.name, path, null, info);
                }

                score++;
            }
        }
    }
}
