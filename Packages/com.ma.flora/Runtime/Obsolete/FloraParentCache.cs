// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [Obsolete]
    public struct FloraParentID : IEquatable<FloraParentID>
    {
        public static readonly FloraParentID Null = new FloraParentID { Value = 0 };

        public int Value;
        
        public override int GetHashCode() => Value.GetHashCode();
        public override bool Equals(object obj) => obj is FloraParentID converted && Equals(converted);
        public bool Equals(FloraParentID other) => Value == other.Value;
        public int CompareTo(FloraParentID other) => Value.CompareTo(other.Value);
        public static bool operator ==(FloraParentID a, FloraParentID b) => a.Equals(b);
        public static bool operator !=(FloraParentID a, FloraParentID b) => !a.Equals(b);
    }
    
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [Obsolete]
    struct FloraCachedTransform
    {
        public int GameObjectInstanceId;
        public float4x4 LocalToWorld;
        public int TransformHash;
    }

    [Serializable]
    [Obsolete]
    sealed class FloraParentIdCache
    {
        enum Version { None, CachedParentTransform, }
    
        public int Count => throw new Exception("FloraParentIdCache is obsolete.");
        public bool IsInitialized => throw new Exception("FloraParentIdCache is obsolete.");
    
        [Serializable]
        internal struct SerializedParentData
        {
            public FloraParentID Id;
            public GameObject GameObject;
            public FloraCachedTransform CachedTransform;
        }
        [SerializeField] internal List<SerializedParentData> m_SerializedParents = new List<SerializedParentData>();
    
        public FloraParentID GetOrAddId(int gameObjectInstanceId) => throw new Exception("FloraParentIdCache is obsolete.");
        public bool TryGetId(GameObject gameObject, out FloraParentID id) => throw new Exception("FloraParentIdCache is obsolete.");
        public bool TryGetId(int gameObjectInstanceId, out FloraParentID id) => throw new Exception("FloraParentIdCache is obsolete.");
        public bool TryGetCachedTransform(FloraParentID id, out FloraCachedTransform cachedTransform) => throw new Exception("FloraParentIdCache is obsolete.");
        public bool TryUpdateCachedTransform(FloraParentID id, out FloraCachedTransform cachedTransform) => throw new Exception("FloraParentIdCache is obsolete.");
        public void RemoveParentId(FloraParentID id) => throw new Exception("FloraParentIdCache is obsolete.");
        public void UpdateAllCachedTransforms() => throw new Exception("FloraParentIdCache is obsolete.");
        public void GetMovedParentIds(List<FloraParentID> outMovedIds) => throw new Exception("FloraParentIdCache is obsolete.");
        public void GetInvalidParentIds(List<FloraParentID> outInvalidIds) => throw new Exception("FloraParentIdCache is obsolete.");
    }
}
