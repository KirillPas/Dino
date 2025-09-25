// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Core;
using Unity.Collections;
using UnityEngine;

namespace MA.Flora.Rendering
{
    [Flags]
    enum InstancedMaterialFlags : byte
    {
        Default     = 0,
        AlphaTest   = 1 << 0,
        Transparent = 1 << 1,
    }

    enum InstancedMaterialVariant : byte
    {
        Instanced,
        LODCrossFade,
        LODCrossFadePercentage,
        Count = LODCrossFadePercentage + 1,
    }

    enum InstancedMaterialEditorVariant : byte
    {
        ScenePicking,
        SceneOutlineFront,
        SceneOutlineBack,
    }

    [DebuggerTypeProxy(typeof(InstancedMaterialIDDebugView))]
    struct InstancedMaterialID : IEquatable<InstancedMaterialID>, IComparable<InstancedMaterialID>
    {
        public static InstancedMaterialID Null => new InstancedMaterialID(Handle.Null);

        public Handle Handle;

        public bool IsCreated { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Handle.IsCreated; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedMaterialID(Handle handle) => Handle = handle;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedMaterialID other) => Handle.Equals(other.Handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedMaterialID other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Handle.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedMaterialID other) => Handle.CompareTo(other.Handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Handle(InstancedMaterialID id) => id.Handle;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedMaterialID left, InstancedMaterialID right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedMaterialID left, InstancedMaterialID right) => !left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            if (InstancingSystem.IsActive() && IsCreated && InstancingSystem.Instance.Context.MaterialManager.TryGetMaterial(this, out Material material))
                return material.name;

            return Handle.ToString();
        }
    }

    sealed class InstancedMaterialManager : IDisposable
    {
        [DebuggerDisplay("Parent={Parent.name}")]
        sealed class InstancedMaterial : IDisposable
        {
            public Material Parent;
            public Material[] Variants;
            public bool[] DebugDisplayEnabled;
            public int CRC;

            public InstancedMaterial(Material material)
            {
                Parent = material;
                Variants = new Material[(int)InstancedMaterialVariant.Count];
                DebugDisplayEnabled = new bool[(int)InstancedMaterialVariant.Count];
                CRC = material.ComputeCRC();
            }

            public void Dispose()
            {
                for (int i = 0; i < Variants.Length; i++)
                {
                    if (Variants[i] == null) continue;
                    UnityUtility.Destroy(Variants[i]);
                    Variants[i] = null;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void UpdateInstances(int crc)
            {
                if (crc != CRC)
                {
                    CRC = crc;
                    for (int i = 0; i < Variants.Length; i++)
                    {
                        if (Variants[i] != null)
                            Variants[i].CopyPropertiesFromMaterial(Parent);
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Material GetVariant(InstancedMaterialVariant variant, bool debugDisplay)
            {
                if (Variants[(int)variant] != null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (debugDisplay != DebugDisplayEnabled[(int)variant])
                    {
                        DebugDisplayEnabled[(int)variant] = debugDisplay;
                        if (debugDisplay)
                            Variants[(int)variant].EnableKeyword("DEBUG_DISPLAY");
                        else
                            Variants[(int)variant].DisableKeyword("DEBUG_DISPLAY");
                    }
#endif

                    return Variants[(int)variant];
                }

                Material newInstance = new Material(Parent)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = $"{Parent.name} ({variant})",
                };
#if UNITY_2022_1_OR_NEWER && UNITY_EDITOR
                newInstance.parent = Parent;
#endif
                newInstance.enableInstancing = true;

                switch (variant)
                {
                    case InstancedMaterialVariant.LODCrossFade:
                        newInstance.EnableKeyword("LOD_FADE_CROSSFADE");
                        break;
                    case InstancedMaterialVariant.LODCrossFadePercentage:
                        newInstance.EnableKeyword("LOD_FADE_PERCENTAGE");
                        break;
                }

                Variants[(int)variant] = newInstance;

                return newInstance;
            }
        }

        HandlePool<int> m_HandlePool;
        int m_MaterialCount;
        int m_MaterialCapacity;
        int[] m_ReferenceCount;
        InstancedMaterialID[] m_MaterialID;
        int[] m_InstanceID;
        Material[] m_BaseMaterial;
        Shader[] m_Shader;
        InstancedMaterial[] m_InstancedMaterials;
        InstancedMaterial[] m_InstancedEditorMaterials;
        InstancedMaterialFlags[] m_MaterialFlag;
        int[] m_CRCHashes;
        Dictionary<int, InstancedMaterialID> m_InstanceIDToMaterialID;

        public InstancedMaterialManager(int capacity)
        {
            m_MaterialCount = 0;
            m_MaterialCapacity = capacity;
            m_ReferenceCount = new int[capacity];
            m_HandlePool = new HandlePool<int>(capacity, Allocator.Persistent);
            m_InstanceID = new int[capacity];
            m_MaterialID = new InstancedMaterialID[capacity];
            m_BaseMaterial = new Material[capacity];
            m_Shader = new Shader[capacity];
            m_InstancedMaterials = new InstancedMaterial[capacity];
            m_InstancedEditorMaterials = new InstancedMaterial[capacity];
            m_MaterialFlag = new InstancedMaterialFlags[capacity];
            m_CRCHashes = new int[capacity];
            m_InstanceIDToMaterialID = new Dictionary<int, InstancedMaterialID>(capacity) { [0] = InstancedMaterialID.Null };
        }

        public void Dispose()
        {
            for (int i = 0; i < m_MaterialCount; i++)
                m_InstancedMaterials[i].Dispose();

            m_HandlePool.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Material GetMaterial(InstancedMaterialID id)
            => !m_HandlePool.TryGetIndex(id, out int index) ? null : m_BaseMaterial[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetMaterial(InstancedMaterialID id, out Material material)
        {
            if (!m_HandlePool.TryGetIndex(id, out int index))
            {
                material = null;
                return false;
            }

            material = m_BaseMaterial[index];
            return true;
        }

        public InstancedMaterialID Register(Material material)
        {
            if (material == null)
                return InstancedMaterialID.Null;

            int instanceID = material.GetHashCode();
            if (instanceID == 0)
                return InstancedMaterialID.Null;

            if (m_InstanceIDToMaterialID.TryGetValue(instanceID, out InstancedMaterialID id))
            {
                m_ReferenceCount[m_HandlePool.GetIndex(id)]++;
                return id;
            }

            int index = m_MaterialCount++;
            if (index >= m_MaterialCapacity)
            {
                int newCapacity = m_MaterialCapacity * 2;
                Array.Resize(ref m_ReferenceCount, newCapacity);
                Array.Resize(ref m_InstanceID, newCapacity);
                Array.Resize(ref m_MaterialID, newCapacity);
                Array.Resize(ref m_BaseMaterial, newCapacity);
                Array.Resize(ref m_Shader, newCapacity);
                Array.Resize(ref m_InstancedMaterials, newCapacity);
                Array.Resize(ref m_InstancedEditorMaterials, newCapacity);
                Array.Resize(ref m_CRCHashes, newCapacity);
                Array.Resize(ref m_MaterialFlag, newCapacity);
                m_MaterialCapacity = newCapacity;
            }

            id = new InstancedMaterialID(m_HandlePool.Allocate(index));

            m_ReferenceCount[index] = 1;
            m_InstanceID[index] = instanceID;
            m_MaterialID[index] = id;
            m_BaseMaterial[index] = material;
            m_Shader[index] = material.shader;
            m_InstancedMaterials[index] = new InstancedMaterial(material);
            m_InstancedEditorMaterials[index] = new InstancedMaterial(material);
            m_CRCHashes[index] = material.ComputeCRC();

            m_MaterialFlag[index] = InstancedMaterialFlags.Default;
            if (material.shaderKeywords.Contains("_ALPHATEST_ON"))
                m_MaterialFlag[index] |= InstancedMaterialFlags.AlphaTest;
            if (material.shaderKeywords.Contains("_SURFACE_TYPE_TRANSPARENT"))
                m_MaterialFlag[index] |= InstancedMaterialFlags.Transparent;

            m_InstanceIDToMaterialID.Add(instanceID, id);

            return id;
        }

        public void Unregister(InstancedMaterialID id)
        {
            if (!id.IsCreated)
                return;

            if (!m_HandlePool.TryGetIndex(id, out int index))
                return;

            if (--m_ReferenceCount[index] > 0)
                return;

            m_InstanceIDToMaterialID.Remove(m_InstanceID[index]);
            m_HandlePool.Free(id);

            m_InstanceID[index] = 0;
            m_MaterialID[index] = default;
            m_BaseMaterial[index] = null;
            m_Shader[index] = null;
            m_InstancedMaterials[index].Dispose();
            m_InstancedMaterials[index] = null;
            m_InstancedEditorMaterials[index].Dispose();
            m_InstancedEditorMaterials[index] = null;
            m_MaterialFlag[index] = InstancedMaterialFlags.Default;

            int lastIndex = m_MaterialCount - 1;
            if (lastIndex != index)
            {
                m_HandlePool.UpdateIndex(m_MaterialID[lastIndex], index);
                m_InstanceID[index] = m_InstanceID[lastIndex];
                m_MaterialID[index] = m_MaterialID[lastIndex];
                m_BaseMaterial[index] = m_BaseMaterial[lastIndex];
                m_InstancedMaterials[index] = m_InstancedMaterials[lastIndex];
                m_MaterialFlag[index] = m_MaterialFlag[lastIndex];
            }

            m_MaterialCount--;
        }

        public void RemoveNullMaterials()
        {
            for (int i = 0; i < m_MaterialCount; i++)
            {
                if (m_BaseMaterial[i] == null)
                    Unregister(m_MaterialID[i]);
            }
        }

        void Destroy(InstancedMaterialID id)
        {
            if (m_HandlePool.TryGetIndex(id, out int index))
            {
                m_ReferenceCount[index] = 0;
                Unregister(id);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedMaterialFlags GetMaterialFlags(InstancedMaterialID id)
        {
            if (!m_HandlePool.TryGetIndex(id, out int index))
                return InstancedMaterialFlags.Default;

            return m_MaterialFlag[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetMaterialVariant(in InstancedMaterialID id, InstancedMaterialVariant variant, bool debugDisplay, out Material material)
        {
            if (!m_HandlePool.TryGetIndex(id, out int index))
            {
                material = null;
                return false;
            }

            if (!m_BaseMaterial[index])
            {
                Destroy(id);
                material = null;
                return false;
            }

#if UNITY_EDITOR && !UNITY_2022_1_OR_NEWER
            if (!UnityEditor.EditorApplication.isPlaying)
                UpdateInstanceProperties(index);
#endif

            material = m_InstancedMaterials[index].GetVariant(variant, debugDisplay);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetEditorMaterialVariant(in InstancedMaterialID id, InstancedMaterialVariant variant, bool debugDisplay, out Material material)
        {
            if (!m_HandlePool.TryGetIndex(id, out int index))
            {
                material = null;
                return false;
            }

            if (!m_BaseMaterial[index])
            {
                Destroy(id);
                material = null;
                return false;
            }

#if UNITY_EDITOR && !UNITY_2022_1_OR_NEWER
            if (!UnityEditor.EditorApplication.isPlaying)
                UpdateInstanceProperties(index);
#endif

            material = m_InstancedEditorMaterials[index].GetVariant(variant, debugDisplay);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceProperties(Material material)
        {
            if (material == null) return;
            int instanceID = material.GetHashCode();
            if (m_InstanceIDToMaterialID.TryGetValue(instanceID, out InstancedMaterialID id))
                UpdateInstanceProperties(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstanceProperties(InstancedMaterialID id)
        {
            if (m_HandlePool.TryGetIndex(id, out int index))
                UpdateInstanceProperties(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateInstanceProperties(int index)
        {
            Material baseMaterial = m_BaseMaterial[index];
            if (baseMaterial)
            {
                int materialCRC = baseMaterial.ComputeCRC();
                if (m_CRCHashes[index] == materialCRC) return;
                m_CRCHashes[index] = materialCRC;
                m_InstancedMaterials[index].UpdateInstances(materialCRC);
#if UNITY_EDITOR
                m_InstancedEditorMaterials[index].UpdateInstances(materialCRC);
#endif
            }
        }
    }
}
