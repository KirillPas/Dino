// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;

namespace MA.Flora.Editor
{
    class InstancedPrototypeItem : IEquatable<InstancedPrototypeItem>
    {
        public string Name;
        public string Tooltip;
        public string[] AssetLabels;
        public InstancedPrototype Prototype;
        public Texture2D Preview;
        public Texture2D Thumbnail;
        public bool Active;
        public int ViewID;

        public InstancedPrototypeItem(InstancedPrototype prototype, int viewID)
        {
            Name = prototype.name;
            Tooltip = prototype.name;
            AssetLabels = AssetDatabase.GetLabels(prototype);
            Prototype = prototype;
            Preview = null;
            Thumbnail = null;
            Active = false;
            ViewID = viewID;
        }

        public Texture2D GetThumbnail(object context, bool cacheThumbnail = false)
        {
            if (cacheThumbnail && Thumbnail)
                return Thumbnail;

            Texture2D tex = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/InstancedPrototype Icon@512.png");
            bool textureValid = tex && tex.width > 0 && tex.height > 0;
            if (cacheThumbnail && textureValid)
                Thumbnail = tex;

            return textureValid ? tex : null;
        }

        public Texture2D GetPreview(object context, Vector2 size, FetchPreviewOptions options = FetchPreviewOptions.Normal, bool cacheThumbnail = false)
        {
            if (cacheThumbnail && Preview)
                return Preview;

            Texture2D tex = AssetPreviewBridge.GetAssetPreview(Prototype.gameObject.GetInstanceID(), ViewID);
            var textureValid = tex && tex.width > 0 && tex.height > 0;
            if (cacheThumbnail && textureValid)
                Preview = tex;

            return textureValid ? tex : null;
        }

        public override bool Equals(object other)
        {
            return other is InstancedPrototypeItem l && Equals(l);
        }

        public bool Equals(InstancedPrototypeItem other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return ReferenceEquals(Prototype, other.Prototype);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(Prototype);
            hashCode.Add(ViewID);
            return hashCode.ToHashCode();
        }

        public static bool operator ==(InstancedPrototypeItem left, InstancedPrototypeItem right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(InstancedPrototypeItem left, InstancedPrototypeItem right)
        {
            return !Equals(left, right);
        }
    }
}
