// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace MA.Flora
{
    /// <summary>
    /// A multi-hash table that uses quantized positions as keys.
    /// </summary>
    [BurstCompile]
    [DebuggerTypeProxy(typeof(InstancePlacementHashDebugView<>))]
    public struct InstancePlacementHash<TInstance> : IDisposable
        where TInstance : unmanaged, IEquatable<TInstance>
    {
        /// <summary>
        /// Represents a quantized cell position.
        /// </summary>
        public struct Cell : IEquatable<Cell>, IComparable<Cell>
        {
            /// <summary>Quantized X axis.</summary>
            public long X;
            /// <summary>Quantized Y axis.</summary>
            public long Y;
            /// <summary>Quantized Z axis.</summary>
            public long Z;

            /// <summary>The resolution of the quantized position.</summary>
            public const double QuantizeFactor = 100.0;

            /// <summary>Constructs a new <see cref="Cell"/> from the given quantized position.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Cell(long x, long y, long z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            /// <summary>Constructs a key from the given position, using `cellBits` to quantize the position.</summary>
            /// <param name="position">The 3D float position.</param>
            /// <param name="cellBits">The amount of bits to quantize the position by shifting right.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Cell(float3 position, int cellBits)
            {
                X = (int)math.floor(position.x * QuantizeFactor) >> cellBits;
                Y = (int)math.floor(position.y * QuantizeFactor) >> cellBits;
                Z = (int)math.floor(position.z * QuantizeFactor) >> cellBits;
            }

            /// <summary>Compares this key to another key.</summary>
            /// <param name="other">The key to compare to.</param>
            /// <returns>Returns -1 if this key is less than the other key, 0 if they are equal, and 1 if this key is greater.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int CompareTo(Cell other)
            {
                int xComparison = X.CompareTo(other.X);
                if (xComparison != 0) return xComparison;
                int yComparison = Y.CompareTo(other.Y);
                if (yComparison != 0) return yComparison;
                return Z.CompareTo(other.Z);
            }

            /// <summary>Returns true if the given key is equal to this key.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly bool Equals(Cell other) => X == other.X && Y == other.Y && Z == other.Z;

            /// <summary>Returns true if the given object is equal to this key.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly override bool Equals(object o) => o is Cell converted && Equals(converted);

            /// <summary>Returns the hash code for this key.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly override int GetHashCode() => (unchecked((int)(X * 0x4C7F6DD1u + Y * 0x4822A3E9u + Z * 0xAAC3C25Du)));

            /// <summary>Returns a string representation of this key.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly override string ToString() => $"Cell({X}, {Y}, {Z})";

            /// <summary>Converts this key back into a 3D float position.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly float3 GetPosition(int cellBits)
            {
                return new float3(
                    (float)((X << cellBits) / QuantizeFactor),
                    (float)((Y << cellBits) / QuantizeFactor),
                    (float)((Z << cellBits) / QuantizeFactor));
            }
        }

        /// <summary>The amount of bits used to quantize the position.</summary>
        readonly int m_CellBits;
        /// <summary>The multi-hash table containing lists of values per quantized location.</summary>
        internal UnsafeParallelMultiHashMap<Cell, TInstance> m_CellMap;

        /// <summary>The number of bits used for hashing the position. Determines the granularity of the grid.</summary>
        /// <remarks>Cell = (Position * 100.0 / 2^cellBits). For example, using 9 bits results in cells with a size of 5.12m, 7 bits results in 1.28m.</remarks>
        public const int DefaultHashCellBits = 9;

        /// <summary>Constructs a new <see cref="InstancePlacementHash{TInstance}"/>, using `inHashCellBits` to quantize the position.</summary>
        /// <param name="cellBits">Determines the size of the cells used to quantize the position.</param>
        /// <param name="initialCapacity">Initial capacity.</param>
        /// <param name="allocator">The allocator used to allocate this hash.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancePlacementHash(int cellBits, int initialCapacity, AllocatorManager.AllocatorHandle allocator)
        {
            m_CellMap = new UnsafeParallelMultiHashMap<Cell, TInstance>(math.max(initialCapacity, 64), allocator);
            m_CellBits = cellBits;
        }

        /// <summary>Constructs a new <see cref="InstancePlacementHash{TInstance}"/> with <see cref="DefaultHashCellBits"/> used to quantize the position.</summary>
        /// <param name="initialCapacity">Initial capacity.</param>
        /// <param name="allocator">The allocator used to allocate this hash.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancePlacementHash(int initialCapacity, AllocatorManager.AllocatorHandle allocator)
            : this(DefaultHashCellBits, initialCapacity, allocator) { }

        /// <summary>Returns true if this container has been created.</summary>
        public readonly bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_CellMap.IsCreated;
        }

        /// <summary>The number of key-value pairs that fit in the current allocation.</summary>
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => m_CellMap.Capacity;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => m_CellMap.Capacity = value;
        }

        readonly unsafe InstancePlacementHash<TInstance>* Self
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                fixed (InstancePlacementHash<TInstance>* self = &this)
                    return self;
            }
        }

        /// <summary>Disposes this container.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => m_CellMap.Dispose();

        /// <summary>Disposes this container.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle Dispose(JobHandle dependsOn) => m_CellMap.Dispose(dependsOn);

        /// <summary>Returns a bucket for the given position.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Cell GetCell(float3 position) => new Cell(position, m_CellBits);

        // --- Modify ---

        /// <summary>Removes all elements from the map.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => m_CellMap.Clear();

        /// <summary>Adds the given index to the bucket for the given position.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddInstance(float3 position, TInstance instance)
        {
            Cell cell = GetCell(position);
            m_CellMap.Add(cell, instance);
        }

        /// <summary>Removes an instance from the hash.</summary>
        /// <param name="position">The position used to find the bucket.</param>
        /// <param name="instance">The index to remove.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveInstance(float3 position, TInstance instance)
        {
            Cell cell = GetCell(position);
            m_CellMap.Remove(cell, instance);
        }

        /// <summary>Updates the position of an instance in the hash.</summary>
        /// <param name="oldPosition">The old position of the instance.</param>
        /// <param name="newPosition">The new position of the instance.</param>
        /// <param name="instance">The index of the instance to update.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateInstance(float3 oldPosition, float3 newPosition, TInstance instance)
        {
            RemoveInstance(oldPosition, instance);
            AddInstance(newPosition, instance);
        }

        // --- Inside Bounds ---

        /// <summary>Returns the list of values that reside within the given bounding box.</summary>
        /// <param name="bounds">The bounding box to search within.</param>
        /// <param name="allocator">The allocator used to allocate the list.</param>
        /// <returns>The list of values that reside within the given bounding box.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly NativeArray<TInstance> GetInstancesInsideBounds(in AxisAlignedBox bounds, Allocator allocator)
        {
            NativeList<TInstance> resultIndices = new NativeList<TInstance>(256, allocator);
            GetInstancesInsideBounds(bounds, resultIndices);
            return resultIndices.TransferOwnershipToNativeArray();
        }

        /// <summary>Finds all values that reside within the given bounding box and adds them to `values`.</summary>
        /// <param name="bounds">The bounding box to search within.</param>
        /// <param name="instances">The list to add the values to.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly unsafe void GetInstancesInsideBounds(in AxisAlignedBox bounds, NativeList<TInstance> instances) => GetInstancesInsideBounds(bounds, instances.GetUnsafeList());

        /// <summary>Finds all values that reside within the given bounding box and adds them to `values`.</summary>
        /// <param name="bounds">The bounding box to search within.</param>
        /// <param name="instances">The list to add the values to.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly unsafe void GetInstancesInsideBounds(in AxisAlignedBox bounds, [NoAlias] UnsafeList<TInstance>* instances) => GetInstancesInsideBounds(Self, bounds, instances);

        [BurstCompile]
        static unsafe void GetInstancesInsideBounds([NoAlias] InstancePlacementHash<TInstance>* cells, in AxisAlignedBox bounds, [NoAlias] UnsafeList<TInstance>* instances)
        {
            Cell minKey = new Cell(bounds.Min, cells->m_CellBits);
            Cell maxKey = new Cell(bounds.Max, cells->m_CellBits);

            // Calculate the number of cells in the bounding box region.
            long cellsInBounds = (maxKey.X - minKey.X + 1) *
                                 (maxKey.Y - minKey.Y + 1) *
                                 (maxKey.Z - minKey.Z + 1);

            // If the number of cells is greater than twice the total cell count,
            // we're better off just iterating through the entire array of key-value pairs.
            long cellThreshold = 2 * cells->m_CellMap.Count();
            if (cellsInBounds > cellThreshold)
            {
                foreach (KeyValue<Cell, TInstance> kvp in cells->m_CellMap)
                {
                    if ((kvp.Key.X >= minKey.X) && (kvp.Key.X <= maxKey.X) &&
                        (kvp.Key.Y >= minKey.Y) && (kvp.Key.Y <= maxKey.Y) &&
                        (kvp.Key.Z >= minKey.Z) && (kvp.Key.Z <= maxKey.Z))
                    {
                        foreach (TInstance instance in cells->m_CellMap.GetValuesForKey(kvp.Key))
                        {
                            instances->Add(instance);
                        }
                    }
                }
            }
            else
            {
                for (long z = minKey.Z; z <= maxKey.Z; ++z)
                {
                    for (long y = minKey.Y; y <= maxKey.Y; y++)
                    {
                        for (long x = minKey.X; x <= maxKey.X; x++)
                        {
                            foreach (TInstance instance in cells->m_CellMap.GetValuesForKey(new Cell(x, y, z)))
                            {
                                instances->Add(instance);
                            }
                        }
                    }
                }
            }
        }

        // --- CalculateBounds ---

        /// <summary>Returns the bounding box of all positions in this hash.</summary>
        /// <returns>The bounding box of all positions in this hash.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly AxisAlignedBox CalculateBounds()
        {
            AxisAlignedBox bounds = AxisAlignedBox.Empty;

            foreach (KeyValue<Cell, TInstance> pair in m_CellMap)
                bounds += pair.Key.GetPosition(m_CellBits);

            return bounds;
        }

        // --- ReadOnly ---

        /// <summary>A read-only view of a <see cref="InstancePlacementHash{TInstance}"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ReadOnly AsReadOnly() => new ReadOnly(this);

        /// <summary>A read-only view of a UnsafeIndirectList.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ReadOnly
        {
            InstancePlacementHash<TInstance> m_Hash;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ReadOnly(in InstancePlacementHash<TInstance> hash)
                => m_Hash = hash;

            /// <summary>Returns true if this container has been created.</summary>
            public bool IsCreated
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => m_Hash.IsCreated;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public NativeArray<TInstance> GetInstancesInsideBounds(AxisAlignedBox bounds, Allocator allocator)
                => m_Hash.GetInstancesInsideBounds(bounds, allocator);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void GetInstancesInsideBounds(AxisAlignedBox bounds, NativeList<TInstance> resultInstances)
                => m_Hash.GetInstancesInsideBounds(bounds, resultInstances);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public AxisAlignedBox CalculateBounds()
                => m_Hash.CalculateBounds();
        }

        // --- ParallelWriter ---

        /// <summary>Returns a parallel writer for an <see cref="InstancePlacementHash{TInstance}"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ParallelWriter AsParallelWriter() => new(m_CellMap.AsParallelWriter(), m_CellBits);

        /// <summary>A parallel writer for an <see cref="InstancePlacementHash{TInstance}"/>.</summary>
        [NativeContainerIsAtomicWriteOnly]
        [StructLayout(LayoutKind.Sequential)]
        public struct ParallelWriter
        {
            UnsafeParallelMultiHashMap<Cell, TInstance>.ParallelWriter m_Writer;
            readonly int m_CellBits;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ParallelWriter(UnsafeParallelMultiHashMap<Cell, TInstance>.ParallelWriter writer, int cellBits)
            {
                m_Writer = writer;
                m_CellBits = cellBits;
            }

            public int Capacity
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => m_Writer.Capacity;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddInstance(float3 position, TInstance instance)
            {
                Cell cell = new Cell(position, m_CellBits);
                m_Writer.Add(cell, instance);
            }
        }
    }

    class InstancePlacementHashDebugView<TInstance>
        where TInstance : unmanaged, IEquatable<TInstance>
    {
        InstancePlacementHash<TInstance> m_Hash;

        public InstancePlacementHashDebugView(InstancePlacementHash<TInstance> hash)
        {
            m_Hash = hash;
        }

        public int Capacity
        {
            get => m_Hash.Capacity;
            set => m_Hash.Capacity = value;
        }

        public InstancePlacementHash<TInstance>.Cell[] Cells
        {
            get
            {
                var result = new List<InstancePlacementHash<TInstance>.Cell>();
                (var keys, int count) = GetUniqueKeyArray(ref m_Hash.m_CellMap, AllocatorManager.Temp);

                using (keys)
                {
                    for (int i = 0; i < count; i++)
                    {
                        result.Add(keys[i]);
                    }
                }

                return result.ToArray();
            }
        }

        public TInstance[] Instances
        {
            get
            {

                var result = new List<TInstance>();
                (var keys, int count) = GetUniqueKeyArray(ref m_Hash.m_CellMap, AllocatorManager.Temp);

                using (keys)
                {
                    for (int i = 0; i < count; i++)
                    {
                        foreach (var instance in m_Hash.m_CellMap.GetValuesForKey(keys[i]))
                        {
                            result.Add(instance);
                        }
                    }
                }

                return result.ToArray();
            }
        }

        public static (NativeArray<InstancePlacementHash<TInstance>.Cell>, int) GetUniqueKeyArray(ref UnsafeParallelMultiHashMap<InstancePlacementHash<TInstance>.Cell, TInstance> hashMap, AllocatorManager.AllocatorHandle allocator)
        {
            var withDuplicates = hashMap.GetKeyArray(allocator);
            withDuplicates.Sort();
            int uniques = withDuplicates.Unique();
            return (withDuplicates, uniques);
        }
    }
}
