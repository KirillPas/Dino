// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using MA.Core.Editor.Bridge;
using MA.Core.Editor.InternalBridge;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MA.Flora.Editor
{
#if !UNITY_2022_2_OR_NEWER
    [Flags]
    enum SearchDocumentFlags
    {
        None = 0,
        Asset = 1,
        Object = 2,
        Nested = 4,
        Grouped = 8,
        Resources = Grouped | Asset, // 0x00000009
    }
#endif

    static class SearchUtility
    {
        public static bool HasAny(this SearchFlags flags, SearchFlags f) => (flags & f) != 0;
        public static bool HasAll(this SearchFlags flags, SearchFlags all) => (flags & all) == all;

        public static bool HasAny(this SearchDocumentFlags flags, SearchDocumentFlags f) => (flags & f) != 0;
        public static bool HasAll(this SearchDocumentFlags flags, SearchDocumentFlags all) => (flags & all) == all;

        public static bool HasAny(this SearchItemOptions flags, SearchItemOptions f) => (flags & f) != 0;
        public static bool HasAll(this SearchItemOptions flags, SearchItemOptions all) => (flags & all) == all;

        public static bool HasAny(this FetchPreviewOptions flags, FetchPreviewOptions f) => (flags & f) != 0;
        public static bool HasAll(this FetchPreviewOptions flags, FetchPreviewOptions all) => (flags & all) == all;

        enum IdentifierType { kNullIdentifier = 0, kImportedAsset = 1, kSceneObject = 2, kSourceAsset = 3, kBuiltInAsset = 4 };

        internal struct AssetMetaInfo
        {
            public readonly string path;
            public readonly SearchDocumentFlags flags;

            readonly string gidString;

            GlobalObjectId m_GID;
            public GlobalObjectId gid
            {
                get
                {
                    if (m_GID.assetGUID == default)
                    {
                        if (gidString != null && GlobalObjectId.TryParse(gidString, out m_GID))
                            return m_GID;

                        if (!string.IsNullOrEmpty(path))
                        {
                            m_GID = GetGID(path);
                            return m_GID;
                        }

                        throw new Exception($"Failed to resolve GID for {path}, {gidString}");
                    }

                    return m_GID;
                }
            }

            string m_Source;
            public string source => m_Source ??= AssetDatabase.GUIDToAssetPath(guid);
            public string guid => gid.assetGUID.ToString();

            bool m_HasType;
            Type m_Type;
            public Type type
            {
                get
                {
                    if (!m_HasType)
                    {
                        if (source.EndsWith("prefab", StringComparison.OrdinalIgnoreCase))
                        {
                            m_Type = AssetDatabase.GetTypeFromPathAndFileID(source, (long)gid.targetObjectId);
                        }
                        else if (flags.HasAll(SearchDocumentFlags.Nested | SearchDocumentFlags.Asset))
                        {
                            m_Type = AssetDatabaseBridge.GetTypeFromVisibleGUIDAndLocalFileIdentifier(gid.assetGUID, (long)gid.targetObjectId) ?? obj?.GetType();
                        }

                        m_Type ??= AssetDatabase.GetMainAssetTypeAtPath(source);
                        m_HasType = true;
                    }
                    return m_Type;
                }
            }

            UnityObject m_Object;
            public UnityObject obj
            {
                get
                {
                    if (!m_Object)
                    {
                        if (gid.identifierType == (int)IdentifierType.kBuiltInAsset || flags.HasAll(SearchDocumentFlags.Nested | SearchDocumentFlags.Asset))
                        {
                            m_Object = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                        }
                        else
                        {
                            m_Object = AssetDatabaseBridge.LoadMainAssetAtGUID(gid.assetGUID);
                        }
                    }
                    return m_Object;
                }
            }

            public AssetMetaInfo(string path, SearchDocumentFlags flags)
                : this(path, GetGID(path), flags, type: null)
            {
            }

            public AssetMetaInfo(string path, GlobalObjectId gid, SearchDocumentFlags flags)
                : this(path, gid, flags, type: null)
            {
            }

            public AssetMetaInfo(string path, GlobalObjectId gid, SearchDocumentFlags flags, in Type type)
            {
                this.path = path;
                gidString = null;
                m_GID = gid;
                m_Source = null;
                m_Type = type;
                m_HasType = m_Type != null;
                m_Object = null;
                this.flags = flags;
            }

            public AssetMetaInfo(string path, string gid, SearchDocumentFlags flags)
                : this(path, gid, flags, type: null)
            {
            }

            public AssetMetaInfo(string path, string gid, SearchDocumentFlags flags, in Type type)
            {
                this.path = path;
                gidString = gid;
                m_GID = default;
                m_Source = null;
                m_Type = type;
                m_HasType = m_Type != null;
                m_Object = null;
                this.flags = flags;
            }

            public override string ToString()
            {
                return $"{path} ({m_GID})";
            }
        }

        static QueryEngine<string> m_QueryEngine;
        static QueryEngine<string> queryEngine => m_QueryEngine ??= new QueryEngine<string>(validateFilters: false);

        const string k_NoResultsLimitToggle = "noResultsLimit";

        static int GetItemInstanceId(in SearchItem item)
        {
            var info = GetInfo(item);
            if (info.gid.targetObjectId == 0)
                return AssetDatabaseBridge.GetMainAssetInstanceID(GetInfo(item).source);

            return GlobalObjectId.GlobalObjectIdentifierToInstanceIDSlow(info.gid);
        }

        public static Texture2D FetchPreview(SearchItem item, SearchContext context, Vector2 size, FetchPreviewOptions options)
        {
            var info = GetInfo(item);
            if (info.gid.assetGUID == default)
                return null;

            if (item.preview && item.preview.width >= size.x && item.preview.height >= size.y)
                return item.preview;

            if (info.gid.identifierType == (int)IdentifierType.kSceneObject)
                return AssetDatabase.GetCachedIcon(info.source) as Texture2D;

            var objInstanceId = info.obj ? info.obj.GetInstanceID() : 0;
            var clientId = SearchUtilsBridge.GetClientId(context);

            if (info.gid.identifierType == (int)IdentifierType.kBuiltInAsset)
                return AssetPreviewBridge.GetAssetPreview(objInstanceId, clientId) ?? AssetPreview.GetMiniThumbnail(info.obj);

            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(info.gid);
            if (obj is GameObject go)
                return SearchUtilsBridge.GetSceneObjectPreview(context, go, size, options, item.thumbnail);

            else if (obj && options.HasAny(FetchPreviewOptions.Normal))
            {
                var p = AssetPreviewBridge.GetAssetPreview(obj.GetInstanceID(), clientId);
                if (p)
                    return p;

                if (AssetPreviewBridge.IsLoadingAssetPreview(obj.GetInstanceID(), clientId))
                    return null;
            }

            return SearchUtilsBridge.GetAssetPreviewFromPath(context, info.source, size, options);
        }

        public static Texture2D FetchThumbnail(in SearchItem item)
        {
            var info = GetInfo(item);

            if (item.thumbnail)
                return item.thumbnail;

            if (info.gid.identifierType == (int)IdentifierType.kBuiltInAsset)
                return AssetPreview.GetMiniThumbnail(info.obj);

            return SearchUtilsBridge.GetTypeIcon(info.type ?? typeof(GameObject));
        }

        static string TrimLabel(in string label, in bool trim)
        {
            if (!trim)
                return label;
            var dp = label.LastIndexOfAny(Path.GetInvalidFileNameChars());
            if (dp > 0)
                return label.Substring(dp);
            return label;
        }

        public static string FetchLabel(SearchItem item)
        {
            var info = GetInfo(item);
            var displayCompact = IsDisplayCompact(item);

            if (!string.IsNullOrEmpty(item.label))
                return item.label;

            if (info.gid.identifierType == (int)IdentifierType.kBuiltInAsset && info.obj != null)
            {
                if (displayCompact)
                    return info.obj.name;
                return (item.label = $"{info.obj.name} ({info.obj.GetType()})");
            }

            if (info.flags.HasAny(SearchDocumentFlags.Object))
                return TrimLabel((item.label = info.path), displayCompact);
            item.label = Path.GetFileName(info.path);
            if (string.IsNullOrEmpty(item.label) && info.obj != null)
            {
                item.label = info.obj.name;
            }
            return item.label;
        }

        public static string FetchDescription(SearchItem item)
        {
            var info = GetInfo(item);

            if (info.gid.identifierType == (int)IdentifierType.kBuiltInAsset)
            {
                var desc = info.obj.ToString();
                if (string.IsNullOrEmpty(desc))
                    desc = info.obj.name;
                return desc;
            }

            if (IsDisplayCompact(item))
                return info.path;

            if (!string.IsNullOrEmpty(item.description))
                return item.description;

            if (info.flags.HasAny(SearchDocumentFlags.Asset))
                return (item.description = GetAssetDescription(info.source) ?? info.path);
            return (item.description = $"Source: {GetAssetDescription(info.source) ?? info.path}");
        }

        public static void TrackSelection(in SearchItem item) => EditorGUIUtility.PingObject(GetItemInstanceId(item));

        public static void StartDrag(SearchItem item, SearchContext context)
        {
            if (context.selection.Count > 1)
            {
                var selectedObjects = context.selection.Select(GetObject);
                var paths = context.selection.Select(GetAssetPath).ToArray();
                SearchUtilsBridge.StartDrag(selectedObjects.ToArray(), paths, item.GetLabel(context, true));
            }
            else
                SearchUtilsBridge.StartDrag(new[] { GetObject(item) }, new[] { GetAssetPath(item) }, item.GetLabel(context, true));
        }

        public static bool IsDisplayCompact(in SearchItem item)
        {
            if (item.options.HasAny(SearchItemOptions.Compacted))
                return true;
            return item.context?.searchView?.displayMode == DisplayMode.Grid;
        }

        public static AssetMetaInfo GetInfo(in SearchItem item)
        {
            return (AssetMetaInfo)item.data;
        }

        public static GlobalObjectId GetGID(SearchItem item)
        {
            return GetInfo(item).gid;
        }

        public static int GetInstanceId(SearchItem item)
        {
            var gid = GetGID(item);
            return GlobalObjectId.GlobalObjectIdentifierToInstanceIDSlow(gid);
        }

        static Type GetItemAssetType(in SearchItem item, in Type constrainedType)
        {
            var info = GetInfo(item);

            return info.type;
        }

        public static UnityObject GetObject(SearchItem item)
        {
            return GetObject(item, typeof(UnityEngine.Object));
        }

        public static UnityObject GetObject(SearchItem item, Type type)
        {
            var info = GetInfo(item);

            if (info.gid.identifierType == (int)IdentifierType.kBuiltInAsset)
                return info.obj;

            if (typeof(AssetImporter).IsAssignableFrom(type))
            {
                var importer = AssetImporter.GetAtPath(info.source);
                if (importer)
                    return importer;
            }

            if (info.flags.HasAny(SearchDocumentFlags.Asset))
            {
                if (info.flags.HasAny(SearchDocumentFlags.Nested))
                    return info.obj;
                var assetType = info.type;
                if (!type.IsAssignableFrom(assetType) && !(typeof(Component).IsAssignableFrom(type) && assetType == typeof(GameObject)))
                    return null;
                return AssetDatabase.LoadAssetAtPath(info.source, type);
            }

            return ToObjectType(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(info.gid), type);
        }

        static UnityObject ToObjectType(UnityObject obj, Type type)
        {
            if (!obj)
                return null;

            if (type == null)
                return obj;
            var objType = obj.GetType();
            if (type.IsAssignableFrom(objType))
                return obj;

            if (obj is GameObject go && typeof(Component).IsAssignableFrom(type))
                return go.GetComponent(type);

            return null;
        }

        public static GlobalObjectId GetGID(string assetPath)
        {
            var assetInstanceId = SearchUtilsBridge.GetMainAssetInstanceID(assetPath);
            return GlobalObjectId.GetGlobalObjectIdSlow(assetInstanceId);
        }

        public static SearchItem CreateItem(
            in string tag, in SearchContext context, SearchProvider provider,
            in string gid, in string path, in int itemScore,
            in SearchDocumentFlags flags)
        {
            return CreateItem(tag, context, provider, null, gid, path, itemScore, flags);
        }

        public static SearchItem CreateItem(
            in string tag, in SearchContext context, SearchProvider provider,
            in Type type, in string gid, in string path, in int itemScore,
            in SearchDocumentFlags flags)
        {
            string filename = null;
            if (context.options.HasAny(SearchFlags.Debug) && !string.IsNullOrEmpty(tag))
            {
                filename = Path.GetFileName(path);
                filename += $" ({tag}, {itemScore})";
            }

            if (flags.HasAny(SearchDocumentFlags.Grouped))
                provider = SearchUtilsBridge.CreateGroupProvider(provider, GetProviderGroupName(tag, path), provider.priority, cacheProvider: true);
            else if (flags.HasAny(SearchDocumentFlags.Object))
                provider = SearchUtilsBridge.CreateGroupProvider(provider, "Objects", provider.priority, cacheProvider: true);

            var info = new AssetMetaInfo(path, gid, flags, type);
            return provider.CreateItem(context, gid ?? info.gid.ToString(), itemScore, filename, null, null, info);
        }

        static string GetProviderGroupName(string dbName, string path)
        {
            if (string.IsNullOrEmpty(path))
                return dbName;
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
                return "Packages";
            return dbName;
        }

        public static string GetAssetPath(SearchItem item) => GetInfo(item).source;
        public static string GetAssetPath(string id) => GlobalObjectId.TryParse(id, out var gid) ? AssetDatabase.GUIDToAssetPath(gid.assetGUID) : AssetDatabase.GUIDToAssetPath(id);
        public static string GetAssetPath(SearchResult result) => GetAssetPath(result.id);
        public static string GetAssetPath(SearchDocument doc) => GetAssetPath(doc.id);

        public static string GetAssetDescription(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return assetPath;
            try
            {
                var fi = new FileInfo(assetPath);
                return !fi.Exists
                    ? $"File <i>{assetPath}</i> does not exist anymore."
                    : $"{assetPath} ({EditorUtility.FormatBytes(fi.Length)})";
            }
            catch
            {
                return null;
            }
        }

        public static void SelectItem(SearchItem item)
        {
            var info = GetInfo(item);
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(info.gid);
            if (obj)
            {
                EditorApplication.delayCall += () =>
                {
                    if (!SelectObjectById(info.gid))
                    {
                        if (EditorUtility.DisplayDialog("Container scene is not opened", $"Do you want to open container scene {info.source}?", "Yes", "No"))
                            OpenItem(item);
                    }
                };
            }
            else
                SearchUtilsBridge.FrameAssetFromPath(GetAssetPath(item));

            item.preview = null;
        }

        static void OpenItem(SearchItem item)
        {
            var info = GetInfo(item);
            if (info.gid.identifierType == (int)IdentifierType.kSceneObject)
            {
                var containerAsset = AssetDatabase.LoadAssetAtPath<UnityObject>(info.source);
                if (containerAsset != null)
                {
                    AssetDatabase.OpenAsset(containerAsset);
                    EditorApplication.delayCall += () => SelectObjectById(info.gid);
                }
            }

            var asset = GetObject(item);
            if (asset == null || !AssetDatabase.OpenAsset(asset))
                EditorUtility.OpenWithDefaultApp(GetAssetPath(item));
        }

        static bool SelectObjectById(GlobalObjectId gid)
        {
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            if (obj)
            {
                SearchUtilsBridge.SelectObject(obj);
                return true;
            }
            return false;
        }
    }
}
