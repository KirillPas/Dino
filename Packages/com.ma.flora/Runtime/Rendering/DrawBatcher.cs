// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace MA.Flora.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    [DebuggerTypeProxy(typeof(DrawBatcherDebugView))]
    struct DrawBatcher : IDisposable
    {
        /// <summary>Must match the thread group size of the compute shader.</summary>
        public const int ThreadGroupSize = 64;
        /// <summary>The number of bits reserved for the prefix in packed data. (Acts as an index 0-63)</summary>
        /// <remarks>Must match log2(ThreadGroupSize).</remarks>
        public const int PrefixBits = 6;
        /// <summary>A bitmask used to extract the prefix bits from packed data.</summary>
        public const int PrefixBitMask = 63;
        /// <summary>The number of bits reserved for the instance count in packed data. (Extra bit for when count == 64) </summary>
        public const int InstanceCountItemBits = 7;
        /// <summary>A bitmask used to extract the instance count bits from packed data.</summary>
        public const int InstanceCountItemMask = 127;
        /// <summary>The maximum number of packed prefix sums. (6bit * 5 = 30bit)</summary>
        public const int MaxPackedPrefixSumOffsetsPerBatch = 5;

        /// <summary>A struct for storing a packed batch (a range of items).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Batch
        {
            /// <summary>Combined index and instance count data.</summary>
            public uint Index_Count;
            /// <summary>Packed list of prefix sum values 5 * 6bit</summary>
            public uint PackedPrefixSumOffsets;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Batch(int index, int count, int packedPrefixSumOffsets)
            {
                Index_Count = (uint)((index << InstanceCountItemBits) | (count & InstanceCountItemMask));
                PackedPrefixSumOffsets = (uint)packedPrefixSumOffsets;
            }
        }

        /// <summary>A struct for storing packed item data.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct BatchItem
        {
            /// <summary>Combined instance data offset and instance count data.</summary>
            public uint InstanceDataOffset_Count;
            /// <summary>Combined payload and batch prefix offset data.</summary>
            public uint Payload_BatchPrefixOffset;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public BatchItem(int instanceDataOffset, int instanceCount, uint payload, int prefixOffset)
            {
                InstanceDataOffset_Count = (uint)instanceDataOffset << InstanceCountItemBits | (uint)instanceCount & InstanceCountItemMask;
                Payload_BatchPrefixOffset = (payload << PrefixBits) | (uint)prefixOffset & PrefixBitMask;
            }
        }

        UnsafeList<Batch> m_Batches;
        UnsafeList<BatchItem> m_Items;
        int m_TotalInstances;
        int m_CurrentBatchPrefixSum;
        int m_CurrentBatchPackedPrefixSum;
        int m_CurrentBatchItemCount;
        int m_CurrentBatchItemIndex;

        public UnsafeList<Batch> Batches { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_Batches; }

        public UnsafeList<BatchItem> Items { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_Items; }

        public int TotalInstances { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_TotalInstances; }

        public int3 DispatchGroupSize { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ComputeUtility.WrapGroupCount(m_Batches.Length); }

        public bool IsCreated { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_Batches.IsCreated && m_Items.IsCreated; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DrawBatcher(int capacity, AllocatorManager.AllocatorHandle allocator)
        {
            m_Batches = new UnsafeList<Batch>(capacity, allocator);
            m_Items = new UnsafeList<BatchItem>(capacity, allocator);
            m_TotalInstances = 0;
            m_CurrentBatchPrefixSum = 0;
            m_CurrentBatchItemCount = 0;
            m_CurrentBatchItemIndex = 0;
            m_CurrentBatchPackedPrefixSum = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (!IsCreated) return;
            m_Batches.Dispose();
            m_Items.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            m_Batches.Clear();
            m_Items.Clear();
            m_TotalInstances = 0;
            m_CurrentBatchPrefixSum = 0;
            m_CurrentBatchPackedPrefixSum = 0;
            m_CurrentBatchItemCount = 0;
            m_CurrentBatchItemIndex = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddInstances(int instanceDataOffset, int instanceCount, uint payload)
        {
            int instancesAdded = 0;

            while (instancesAdded < instanceCount)
            {
                // Calculate the maximum number of instances that can be added to the current batch
                int maxInstancesThisBatch = ThreadGroupSize - m_CurrentBatchPrefixSum;
                if (maxInstancesThisBatch > 0)
                {
                    // Determine the number of instances to add in this iteration, considering the batch's capacity
                    int itemInstanceCount = math.min(maxInstancesThisBatch, instanceCount - instancesAdded);

                    // Create and add a new packed item to the batch, representing a subset of instances
                    m_Items.Add(new BatchItem(instanceDataOffset + instancesAdded, itemInstanceCount, payload, m_CurrentBatchPrefixSum));

                    // We can pack the prefix sum into the batch if there is enough space
                    if (m_CurrentBatchItemCount <= MaxPackedPrefixSumOffsetsPerBatch)
                        m_CurrentBatchPackedPrefixSum |= (m_CurrentBatchPrefixSum & PrefixBitMask) << (PrefixBits * m_CurrentBatchItemCount);

                    m_CurrentBatchItemCount += 1;
                    m_CurrentBatchPrefixSum += itemInstanceCount;
                    instancesAdded += itemInstanceCount;
                }

                if (maxInstancesThisBatch <= 0 || m_CurrentBatchPrefixSum >= ThreadGroupSize)
                    FinishCurrentBatch();
            }

            m_TotalInstances += instancesAdded;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FinishCurrentBatch()
        {
            if (m_CurrentBatchItemCount > 0)
            {
                m_Batches.Add(new Batch(m_CurrentBatchItemIndex, m_CurrentBatchItemCount, m_CurrentBatchPackedPrefixSum));
                m_CurrentBatchItemIndex = m_Items.Length;
                m_CurrentBatchItemCount = 0;
                m_CurrentBatchPrefixSum = 0;
                m_CurrentBatchPackedPrefixSum = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FinalizeBatches()
        {
            if (m_CurrentBatchItemCount != 0)
            {
                m_Batches.Add(new Batch(m_CurrentBatchItemIndex, m_CurrentBatchItemCount, m_CurrentBatchPackedPrefixSum));
                m_CurrentBatchItemCount = 0;
            }
        }
    }

    class DrawBatcherDebugView
    {
        DrawBatcher m_DrawBatcher;

        public DrawBatcherDebugView(DrawBatcher drawBatcher)
        {
            m_DrawBatcher = drawBatcher;
        }

        public DrawBatcher.Batch[] Batches
        {
            get
            {
                DrawBatcher.Batch[] batches = new DrawBatcher.Batch[m_DrawBatcher.Batches.Length];
                for (int i = 0; i < m_DrawBatcher.Batches.Length; i++)
                {
                    batches[i] = m_DrawBatcher.Batches[i];
                }
                return batches;
            }
        }

        public DrawBatcher.BatchItem[] Items
        {
            get
            {
                DrawBatcher.BatchItem[] items = new DrawBatcher.BatchItem[m_DrawBatcher.Items.Length];
                for (int i = 0; i < m_DrawBatcher.Items.Length; i++)
                {
                    items[i] = m_DrawBatcher.Items[i];
                }
                return items;
            }
        }
    }
}
