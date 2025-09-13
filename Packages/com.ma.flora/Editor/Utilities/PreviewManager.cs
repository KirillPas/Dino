// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MA.Core.Editor;
using MA.Core.Editor.Bridge;
using UnityEditor.Search;
using UnityEditorInternal;
using UnityEngine;

namespace MA.Flora.Editor
{
    delegate PreviewItem FetchPreviewCallback(object item, object context, FetchPreviewOptions options, Vector2 size);
    delegate void AsyncFetchPreviewCallback(object item, object context, FetchPreviewOptions options, Vector2 size, OnPreviewReady onReadyCallback);
    delegate void OnPreviewReady(object item, object context, PreviewItem previewItem);

    readonly struct PreviewKey : IComparable<PreviewKey>, IEquatable<PreviewKey>
    {
        public readonly int ItemHashCode;
        public readonly FetchPreviewOptions Options;
        public readonly Vector2 Size;

        public PreviewKey(int itemHashCode, FetchPreviewOptions options, in Vector2 size)
        {
            ItemHashCode = itemHashCode;
            Options = options;
            Size = size;
        }

        public PreviewKey(object item, FetchPreviewOptions options, in Vector2 size)
            : this(item.GetHashCode(), options, size)
        {}

        public int CompareTo(PreviewKey other)
        {
            int compare = ItemHashCode.CompareTo(other.ItemHashCode);
            if (compare != 0)
                return compare;
            compare = Options.CompareTo(other.Options);
            if (compare != 0)
                return compare;
            return Size.sqrMagnitude.CompareTo(other.Size.sqrMagnitude);
        }

        public bool Equals(PreviewKey other)
            => ItemHashCode == other.ItemHashCode && Options == other.Options && Size == other.Size;

        public override int GetHashCode()
            => HashCode.Combine(ItemHashCode, Options, Size);
    }

    readonly struct PreviewItem
    {
        public readonly PreviewKey Key;
        public readonly Texture2D Texture;
        public readonly DateTime CreationTime;

        public bool Valid => Texture != null && Texture;

        public static PreviewItem Invalid = new(new PreviewKey(0, FetchPreviewOptions.None, Vector2.zero), null);

        public PreviewItem(in PreviewKey key, Texture2D texture)
            : this(key, texture, DateTime.UtcNow)
        {}

        public PreviewItem(in PreviewKey key, Texture2D texture, in DateTime creationTime)
        {
            Key = key;
            Texture = texture;
            CreationTime = creationTime;
        }

        public PreviewItem(object item, FetchPreviewOptions options, in Vector2 size, Texture2D texture)
            : this(new PreviewKey(item, options, size), texture, DateTime.UtcNow)
        {}

        public PreviewItem(object item, FetchPreviewOptions options, in Vector2 size, Texture2D texture, in DateTime creationTime)
            : this(new PreviewKey(item, options, size), texture, creationTime)
        {}
    }

    class PreviewAsyncFetch
    {
        public readonly Action AsyncCallbackOff;
        public readonly ConcurrentBag<OnPreviewReady> ReadyCallbacks;

        public PreviewAsyncFetch(Action off)
        {
            ReadyCallbacks = new ConcurrentBag<OnPreviewReady>();
            AsyncCallbackOff = off;
        }
    }

    class PreviewManager
    {
        ConcurrentDictionary<PreviewKey, PreviewItem> m_PreviewCollections = new();
        ConcurrentDictionary<PreviewKey, PreviewAsyncFetch> m_FetchPreviewOffs = new();

        public int PoolSize { get; set; }
        public int Count => m_PreviewCollections.Count;

        public PreviewManager(int poolSize)
        {
            PoolSize = poolSize;
        }

        public PreviewManager()
        {
            PoolSize = 50;
        }

        public Action FetchPreview(object item, object context, FetchPreviewOptions options, in Vector2 size, FetchPreviewCallback fetchCallback, OnPreviewReady readyCallback, double delayInSeconds = 0.2d) 
            => FetchPreview(item, context, new PreviewKey(item, options, size), fetchCallback, readyCallback, delayInSeconds);

        public Action FetchPreview(object item, object context, FetchPreviewOptions options, in Vector2 size, AsyncFetchPreviewCallback fetchCallback, OnPreviewReady readyCallback, double delayInSeconds = 0.2d)
            => FetchPreview(item, context, new PreviewKey(item, options, size), fetchCallback, readyCallback, delayInSeconds);

        public Action FetchPreview(object item, object context, in PreviewKey key, FetchPreviewCallback fetchCallback, OnPreviewReady readyCallback, double delayInSeconds = 0.2d)
        {
            return FetchPreview(item, context, key, (searchItem, searchContext, options, size, callback) =>
            {
                PreviewItem searchPreview = fetchCallback(searchItem, searchContext, options, size);
                callback(searchItem, searchContext, searchPreview);
            }, readyCallback, delayInSeconds);
        }

        public Action FetchPreview(object item, object context, in PreviewKey key, AsyncFetchPreviewCallback fetchCallback, OnPreviewReady readyCallback, double delayInSeconds = 0.2d)
        {
            if (m_PreviewCollections.TryGetValue(key, out PreviewItem searchPreview) && searchPreview.Valid)
            {
                readyCallback?.Invoke(item, context, searchPreview);
                return () => { };
            }

            return FetchPreviewAsync(item, context, key, fetchCallback, readyCallback, delayInSeconds);
        }

        public PreviewItem FetchPreview(object item, FetchPreviewOptions options, in Vector2 size) 
            => FetchPreview(new PreviewKey(item, options, size));

        public PreviewItem FetchPreview(in PreviewKey key)
        {
            if (!m_PreviewCollections.TryGetValue(key, out PreviewItem searchPreview))
                return PreviewItem.Invalid;
            if (!searchPreview.Valid)
                return PreviewItem.Invalid;
            return searchPreview;
        }

        public void CancelFetch(object item, FetchPreviewOptions options, in Vector2 size)
            => CancelFetch(new PreviewKey(item, options, size));

        public void CancelFetch(in PreviewKey key)
        {
            if (!m_FetchPreviewOffs.TryRemove(key, out PreviewAsyncFetch previewAsyncFetch))
                return;
            previewAsyncFetch.AsyncCallbackOff?.Invoke();
            previewAsyncFetch.ReadyCallbacks.Clear();
        }

        public void ReleasePreview(object item, FetchPreviewOptions options, in Vector2 size)
            => ReleasePreview(new PreviewKey(item, options, size));

        public void ReleasePreview(in PreviewKey key)
        {
            m_PreviewCollections.TryRemove(key, out _);
            CancelFetch(key);
        }

        public void ReleaseOldPreviews(TimeSpan elapsedTime)
        {
            DateTime now = DateTime.UtcNow;
            PreviewKey[] oldPreviews = m_PreviewCollections.Where(pair =>
            {
                TimeSpan lifeTime = now - pair.Value.CreationTime;
                return lifeTime > elapsedTime;
            }).Select(pair => pair.Key).ToArray();
            foreach (PreviewKey oldPreviewKey in oldPreviews)
            {
                ReleasePreview(oldPreviewKey);
            }
        }

        public bool HasPreview(object item, FetchPreviewOptions options, in Vector2 size) 
            => m_PreviewCollections.TryGetValue(new PreviewKey(item, options, size), out _);

        public bool HasPreview(in PreviewKey key) 
            => m_PreviewCollections.TryGetValue(key, out _);

        internal bool IsAnyPreviewRequestedForItem(object item)
        {
            int itemHash = item.GetHashCode();
            foreach (PreviewKey k in m_FetchPreviewOffs.Keys)
                if (k.ItemHashCode == itemHash)
                    return true;
            return false;
        }

        internal bool IsAnyPreviewLoadedForItem(object item)
        {
            int itemHash = item.GetHashCode();
            foreach (PreviewKey k in m_PreviewCollections.Keys)
                if (k.ItemHashCode == itemHash)
                    return true;
            return false;
        }

        public void Clear()
        {
            m_PreviewCollections.Clear();

            // Not really atomic
            KeyValuePair<PreviewKey, PreviewAsyncFetch>[] asyncFetchOffs = m_FetchPreviewOffs.ToArray();
            m_FetchPreviewOffs.Clear();
            foreach (KeyValuePair<PreviewKey, PreviewAsyncFetch> kvp in asyncFetchOffs)
            {
                kvp.Value.AsyncCallbackOff();
            }
        }

        Action FetchPreviewAsync(object item, object context, in PreviewKey key, AsyncFetchPreviewCallback fetchCallback, OnPreviewReady readyCallback, double delayInSeconds)
        {
            PreviewAsyncFetch created = null;
            PreviewKey localKey = key;
            PreviewAsyncFetch asyncFetch = m_FetchPreviewOffs.GetOrAdd(key, previewKey =>
            {
                // I don't think we will often be in a situation where we try to fetch multiple previews for the same key
                // therefore I think this is fine.
                Action asyncCallbackOff = UnityEditorUtility.CallDelayed(() => OnAsyncFetch(item, context, localKey, fetchCallback), delayInSeconds);
                created = new PreviewAsyncFetch(asyncCallbackOff);
                return created;
            });
            if (created != null && !ReferenceEquals(created, asyncFetch))
                created.AsyncCallbackOff();

            asyncFetch.ReadyCallbacks.Add(readyCallback);

            return asyncFetch.AsyncCallbackOff;
        }

        void OnAsyncFetch(object item, object context, in PreviewKey key, AsyncFetchPreviewCallback fetchCallback)
        {
            if (!m_FetchPreviewOffs.ContainsKey(key))
                return; // If we were already canceled, return
            
            PreviewKey localKey = key;
            fetchCallback(item, context, key.Options, key.Size, (searchItem, searchContext, preview) => OnAsyncFetchDone(searchItem, searchContext, preview, localKey));
        }

        void OnAsyncFetchDone(object item, object context, in PreviewItem previewItem, in PreviewKey originalKey)
        {
            if (previewItem.Valid)
                AddSearchPreview(previewItem);
            
            if (!m_FetchPreviewOffs.Remove(originalKey, out PreviewAsyncFetch asyncFetch))
                return; // Might have been canceled and removed already

            if (InternalEditorUtility.CurrentThreadIsMainThread())
            {
                DispatchFetchPreviewResult(asyncFetch.ReadyCallbacks, item, context, previewItem);
            }
            else
            {
                PreviewItem localPreview = previewItem;
                DispatcherBridge.Enqueue(() =>
                {
                    DispatchFetchPreviewResult(asyncFetch.ReadyCallbacks, item, context, localPreview);
                });
            }
        }

        static void DispatchFetchPreviewResult(ConcurrentBag<OnPreviewReady> readyCallbacks, object item, object context, in PreviewItem previewItem)
        {
            while (readyCallbacks.TryTake(out OnPreviewReady readyCallback))
            {
                readyCallback?.Invoke(item, context, previewItem);
            }
        }

        void AddSearchPreview(in PreviewItem previewItem)
        {
            // This block is not atomic. There is a chance that we add multiple previews and check the size at the same time.
            // This means that we might end up removing more than we needed.
            // Also, this doesn't protect against releases that might happen at the same time, once again leading to more
            // previews being removed than necessary.
            // But there is a guarantee that we will never have more than poolsize item.
            PreviewItem localPreview = previewItem;
            m_PreviewCollections.AddOrUpdate(previewItem.Key, localPreview, (_, _) => localPreview);
            while (m_PreviewCollections.Count > PoolSize)
            {
                PreviewKey oldestKey = FindOldestPreview();
                if (m_PreviewCollections.TryRemove(oldestKey, out _))
                    return;
                // Since Count and TryRemove already spins for access, we don't need to spin before retrying.
            }
        }

        PreviewKey FindOldestPreview()
        {
            PreviewKey oldestKey = new();
            TimeSpan oldestLifeTime = new TimeSpan(0);
            DateTime now = DateTime.UtcNow;
            foreach ((PreviewKey key, PreviewItem preview) in m_PreviewCollections)
            {
                TimeSpan previewLifeTime = now - preview.CreationTime;
                if (previewLifeTime > oldestLifeTime)
                {
                    oldestKey = key;
                    oldestLifeTime = previewLifeTime;
                }
            }

            return oldestKey;
        }
    }
}
