// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Assertions;

namespace MA.Flora
{
    struct SlotMap<TKey> : IDisposable
        where TKey : unmanaged, System.IEquatable<TKey>
    {
        AllocatorManager.AllocatorHandle m_Allocator;
        SlotAllocator m_SlotAllocator;
        UnsafeArray<TKey> m_Keys;
        UnsafeParallelHashMap<TKey, int> m_Map;

        public int MaxAllocatedSlot
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_SlotAllocator.MaxAllocatedSlot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SlotMap(int capacity, AllocatorManager.AllocatorHandle allocator)
        {
            m_Allocator = allocator;
            m_SlotAllocator = new SlotAllocator(capacity, m_Allocator);
            m_SlotAllocator.Allocate();
            m_Keys = new UnsafeArray<TKey>(capacity, m_Allocator);
            m_Map = new UnsafeParallelHashMap<TKey, int>(capacity, m_Allocator);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SlotMap(ReadOnlySpan<TKey> keys, ReadOnlySpan<int> slots, AllocatorManager.AllocatorHandle allocator)
        {
            if (keys.Length != slots.Length)
                throw new ArgumentException("Keys and slots must have the same length.");

            m_Allocator = allocator;
            m_SlotAllocator = new SlotAllocator(slots, m_Allocator);
            m_SlotAllocator.Allocate();
            m_Keys = new UnsafeArray<TKey>(m_SlotAllocator.MaxAllocatedSlot, m_Allocator);
            m_Map = new UnsafeParallelHashMap<TKey, int>(keys.Length, m_Allocator);

            for (int i = 0; i < keys.Length; i++)
            {
                TKey key = keys[i];
                int slot = slots[i];
                m_Map.Add(key, slot);
                m_Keys[slot] = key;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            m_SlotAllocator.Dispose();
            m_Keys.Dispose();
            m_Map.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void GrowKeys()
        {
            m_Keys.Resize(m_SlotAllocator.MaxAllocatedSlot + 1, m_Allocator);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            m_SlotAllocator.Clear();
            m_Map.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reserve(int capacity)
        {
            m_SlotAllocator.Reserve(capacity);

            if (m_Keys.Length < capacity)
                m_Keys.Resize(capacity, m_Allocator);

            if (m_Map.Capacity < capacity)
                m_Map.Capacity = capacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Exists(TKey key) => m_Map.ContainsKey(key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Exists(int slot) => slot > 0 && m_SlotAllocator.Exists(slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Allocate(TKey key)
        {
            if (m_Map.TryGetValue(key, out int slot))
                return slot;

            slot = m_SlotAllocator.Allocate();
            if (slot >= m_Keys.Length)
                GrowKeys();

            m_Map.Add(key, slot);
            m_Keys[slot] = key;
            return slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAllocate(TKey key, out int slot)
        {
            if (!m_Map.TryGetValue(key, out slot))
            {
                slot = m_SlotAllocator.Allocate();
                if (slot >= m_Keys.Length)
                    GrowKeys();

                m_Map.Add(key, slot);
                m_Keys[slot] = key;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetSlot(TKey key) => m_Map.TryGetValue(key, out int slot) ? slot : default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetSlot(TKey key, out int slot) => m_Map.TryGetValue(key, out slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TKey GetKey(int slot)
        {
            Assert.IsTrue(Exists(slot), "SlotMap: Slot is not allocated.");
            return m_Keys[slot];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetKey(int slot, out TKey key)
        {
            if (Exists(slot))
            {
                key = m_Keys[slot];
                return true;
            }

            key = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Free(TKey key)
        {
            if (m_Map.TryGetValue(key, out int slot))
            {
                m_Map.Remove(key);
                m_SlotAllocator.Free(slot);
                m_Keys[slot] = default;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Free(int slot)
        {
            if (Exists(slot))
            {
                TKey key = m_Keys[slot];
                m_Map.Remove(key);
                m_SlotAllocator.Free(slot);
                m_Keys[slot] = default;
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UnsafeParallelHashMap<TKey, int>.Enumerator GetEnumerator() => m_Map.GetEnumerator();
    }
}
