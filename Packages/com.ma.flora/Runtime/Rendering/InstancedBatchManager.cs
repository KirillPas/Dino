// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora.Rendering
{
    [DebuggerTypeProxy(typeof(InstancedBatchIDDebugView))]
    struct InstancedBatchID : IEquatable<InstancedBatchID>, IComparable<InstancedBatchID>
    {
        public static readonly InstancedBatchID Null = new InstancedBatchID(value: 0);

        public int Value;

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value != 0;
        }

        public InstancedBatchID(int value) => Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Value.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedBatchID other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedBatchID other) => (int)Value == (int)other.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedBatchID other) => Value.CompareTo(other.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(InstancedBatchID id) => id.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedBatchID a, InstancedBatchID b) => a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedBatchID a, InstancedBatchID b) => !a.Equals(b);
    }

    [Flags]
    enum InstancedBatchFlags
    {
        None             = 0,
        Transform        = 1 << 0,
        SHCoefficients   = 1 << 1,
        EditorData       = 1 << 2,
        CustomProperties = 1 << 3,
    }

    struct InstancedBatchPropertyInfo : IEquatable<InstancedBatchPropertyInfo>, IComparable<InstancedBatchPropertyInfo>
    {
        public static readonly InstancedBatchPropertyInfo Null = new InstancedBatchPropertyInfo(0, 0);

        public int MetadataNameID;
        public int SizeInBytes;

        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => SizeInBytes > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedBatchPropertyInfo(int metadataNameID, int sizeInBytes)
        {
            MetadataNameID = metadataNameID;
            SizeInBytes = sizeInBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedBatchPropertyInfo other)
        {
            int nameIDComparison = MetadataNameID.CompareTo(other.MetadataNameID);
            if (nameIDComparison != 0) return nameIDComparison;
            return SizeInBytes.CompareTo(other.SizeInBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedBatchPropertyInfo other) => MetadataNameID == other.MetadataNameID && SizeInBytes == other.SizeInBytes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedBatchPropertyInfo other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => unchecked((MetadataNameID * 397) ^ SizeInBytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedBatchPropertyInfo left, InstancedBatchPropertyInfo right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedBatchPropertyInfo left, InstancedBatchPropertyInfo right) => !left.Equals(right);
    }

    [DebuggerDisplay("BuiltinFlags = {Flags}, InstancedProperties = {PropertyInfoArray.Length}")]
    unsafe struct InstancedBatchDescriptor : IEquatable<InstancedBatchDescriptor>, IDisposable
    {
        public InstancedBatchFlags Flags;
        public UnsafeArray<InstancedBatchPropertyInfo> PropertyInfoArray;
        public int SizeInBytes;
        public int AlignedSizeInBytes;
        public int HashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasBuiltinFlag(InstancedBatchFlags flag) => (Flags & flag) != 0;

        public InstancedBatchDescriptor(IInstancedRenderer renderer, AllocatorManager.AllocatorHandle allocator)
        {
            Flags = InstancedBatchFlags.Transform;

            int sizeInBytes = sizeof(float4x2); // ObjectToWorld

            bool wantsLightProbeData = renderer.Prototype?.SampleLightProbes ?? false;
            bool hasLightProbeData = LightmapSettings.lightProbes?.count > 0;
            if (wantsLightProbeData && hasLightProbeData)
            {
                Flags |= InstancedBatchFlags.SHCoefficients;
                sizeInBytes += sizeof(float4) * 8; // SHCoefficients
            }

            if (renderer.InstancePropertyArrays != null)
            {
                InstancedPropertyArrays propertyArrays = renderer.InstancePropertyArrays;
                int activeCount = propertyArrays.GetActiveArrayCount();
                if (activeCount > 0)
                {
                    Flags |= InstancedBatchFlags.CustomProperties;

                    int arrayCount = propertyArrays.PropertyCount;
                    PropertyInfoArray = new UnsafeArray<InstancedBatchPropertyInfo>(arrayCount, allocator, NativeArrayOptions.UninitializedMemory);

                    ReadOnlySpan<RuntimeInstancedProperty> properties = propertyArrays.RuntimeProperties;
                    ReadOnlySpan<UnsafeUntypedList> arrays = propertyArrays.DataArrays;

                    for (int i = 0; i < arrayCount; i++)
                    {
                        if (arrays[i].IsCreated)
                        {
                            PropertyInfoArray[i] = new InstancedBatchPropertyInfo(properties[i].FloraMetadataNameID, properties[i].SizeInBytes);
                            sizeInBytes += properties[i].SizeInBytes;
                        }
                        else
                        {
                            PropertyInfoArray[i] = InstancedBatchPropertyInfo.Null;
                        }
                    }

                    NativeSortExtension.Sort(PropertyInfoArray.Ptr, arrayCount);
                }
                else
                {
                    PropertyInfoArray = default;
                }
            }
            else
            {
                PropertyInfoArray = default;
            }

#if UNITY_EDITOR
            if (renderer is IInstancedRendererEditorData)
            {
                Flags |= InstancedBatchFlags.EditorData;
                sizeInBytes += sizeof(uint); // EditorData
            }
#endif

            SizeInBytes = sizeInBytes;
            AlignedSizeInBytes = MathUtility.NextMultipleOf(sizeInBytes, 16);

            unchecked
            {
                HashCode = (int)Flags;
                HashCode = (HashCode * 397) ^ PropertyInfoArray.Length;
                for (int i = 0; i < PropertyInfoArray.Length; i++)
                    HashCode = (HashCode * 397) ^ PropertyInfoArray[i].GetHashCode();
            }
        }

        public InstancedBatchDescriptor(in InstancedBatchDescriptor other, AllocatorManager.AllocatorHandle allocator)
        {
            Flags = other.Flags;
            HashCode = other.HashCode;
            SizeInBytes = other.SizeInBytes;
            AlignedSizeInBytes = other.AlignedSizeInBytes;

            if (other.PropertyInfoArray.Length == 0)
            {
                PropertyInfoArray = default;
            }
            else
            {
                PropertyInfoArray = new UnsafeArray<InstancedBatchPropertyInfo>(other.PropertyInfoArray.Length, allocator, NativeArrayOptions.UninitializedMemory);
                PropertyInfoArray.CopyFrom(other.PropertyInfoArray);
            }
        }

        public void Dispose()
        {
            PropertyInfoArray.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedBatchDescriptor other)
        {
            if (Flags != other.Flags)
                return false;

            if (PropertyInfoArray.Length != other.PropertyInfoArray.Length)
                return false;

            for (int i = 0; i < PropertyInfoArray.Length; i++)
            {
                if (PropertyInfoArray[i] != other.PropertyInfoArray[i])
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedBatchDescriptor other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => HashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedBatchDescriptor left, InstancedBatchDescriptor right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedBatchDescriptor left, InstancedBatchDescriptor right) => !left.Equals(right);
    }

    class InstancedBatchManager : IDisposable
    {
        SlotAllocator m_IDAllocator;
        int[] m_RefCounts;
        InstancedBatchID[] m_BatchIDs;
        InstancedBatchDescriptor[] m_BatchDescriptions;
        Dictionary<InstancedBatchDescriptor, InstancedBatchID> m_BatchDescriptionToID;
        List<InstancedBatchID> m_ActiveBatchIDs;

        public InstancedBatchManager(int capacity)
        {
            m_IDAllocator = new SlotAllocator(capacity, AllocatorManager.Persistent);
            m_IDAllocator.Allocate(); // Reserve the first slot for the null batch ID
            m_RefCounts = new int[capacity];
            m_BatchIDs = new InstancedBatchID[capacity];
            m_BatchDescriptions = new InstancedBatchDescriptor[capacity];
            m_BatchDescriptionToID = new Dictionary<InstancedBatchDescriptor, InstancedBatchID>(capacity);
            m_ActiveBatchIDs = new List<InstancedBatchID>(capacity);
        }

        public void Dispose()
        {
            for (int i = 0; i < m_ActiveBatchIDs.Count; i++)
                m_BatchDescriptions[m_ActiveBatchIDs[i]].Dispose();

            m_ActiveBatchIDs.Clear();
            m_BatchDescriptionToID.Clear();
            m_BatchDescriptions = Array.Empty<InstancedBatchDescriptor>();
            m_BatchIDs = Array.Empty<InstancedBatchID>();
            m_RefCounts = Array.Empty<int>();
            m_IDAllocator.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedBatchDescriptor GetBatchDescription(InstancedBatchID id)
        {
            if (id == InstancedBatchID.Null)
                return default;

            return m_IDAllocator.Exists(id) ? m_BatchDescriptions[id] : default;
        }

        public InstancedBatchID RegisterBatch(InstancedBatchDescriptor descriptor)
        {
            if (m_BatchDescriptionToID.TryGetValue(descriptor, out InstancedBatchID id))
            {
                m_RefCounts[id]++;
                return id;
            }

            id = new InstancedBatchID { Value = m_IDAllocator.Allocate() };
            if (id == InstancedBatchID.Null)
                return InstancedBatchID.Null;

            if (m_IDAllocator.MaxAllocatedSlot >= m_RefCounts.Length)
            {
                int newCapacity = math.max(MathUtility.NextMultipleOf(m_IDAllocator.MaxAllocatedSlot + 1, 8), 8);
                Array.Resize(ref m_RefCounts, newCapacity);
                Array.Resize(ref m_BatchIDs, newCapacity);
                Array.Resize(ref m_BatchDescriptions, newCapacity);
            }

            m_BatchDescriptionToID[descriptor] = id;
            m_RefCounts[id] = 1;
            m_BatchIDs[id] = id;
            m_BatchDescriptions[id] = new InstancedBatchDescriptor(descriptor, AllocatorManager.Persistent);
            m_ActiveBatchIDs.Add(id);

            return id;
        }

        public void UnregisterBatch(InstancedBatchID id)
        {
            if (id == InstancedBatchID.Null)
                return;

            if (m_IDAllocator.Exists(id))
            {
                m_RefCounts[id]--;
                if (m_RefCounts[id] > 0)
                    return;

                m_IDAllocator.Free(id);
                m_BatchDescriptionToID.Remove(m_BatchDescriptions[id]);

                m_RefCounts[id] = 0;
                m_BatchIDs[id] = InstancedBatchID.Null;
                m_BatchDescriptions[id].Dispose();
                m_BatchDescriptions[id] = default;
                m_ActiveBatchIDs.Remove(id);
            }
        }
    }
}
