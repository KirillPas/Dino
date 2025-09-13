// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    struct BufferDescriptor : IEquatable<BufferDescriptor>
    {
        public ComputeBufferType Type;
        public ComputeBufferMode Mode;
        public int Stride;
        public int Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            unchecked
            {
                int result = (int)Type;
                result = (result * 397) ^ (int)Mode;
                result = (result * 397) ^ Stride;
                result = (result * 397) ^ Count;
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(BufferDescriptor rhs) => Type == rhs.Type && Mode == rhs.Mode && Stride == rhs.Stride && Count == rhs.Count;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object o) => o is BufferDescriptor converted && Equals(converted);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(BufferDescriptor lhs, BufferDescriptor rhs) => lhs.Equals(rhs);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(BufferDescriptor lhs, BufferDescriptor rhs) => !lhs.Equals(rhs);
    }
    
    struct PooledBufferID : IEquatable<PooledBufferID>
    {
        public static readonly PooledBufferID Null = new PooledBufferID(index: 0);
        
        public int Index;
        
        public bool IsValid { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Index > 0; }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PooledBufferID(int index) => Index = index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(PooledBufferID other) => Index == other.Index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is PooledBufferID other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(PooledBufferID id) => id.Index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(PooledBufferID left, PooledBufferID right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(PooledBufferID left, PooledBufferID right) => !left.Equals(right);
    }

    sealed unsafe class BufferPool
    {
        public enum Alignment : byte
        {
            None,
            Page,
            PowerOfTwo
        }
        
        const int k_PageSize           = 16 * 1024;
        const int k_FramesUntilRelease = 30;
        const int k_InUseMarker        = -1;

        SlotAllocator m_SlotAllocator;
        ComputeBuffer[] m_Buffers;
        AsyncGPUReadbackRequest[] m_Fences;
        bool[] m_WasMapped;
        BufferDescriptor[] m_InUseDescriptors;
        BufferDescriptor[] m_BufferDescriptors;
        int[] m_Hashes;
        int[] m_LastUsedFrame;
        int[] m_MappedSizes;
        IntPtr[] m_MappedPtr;
        
        bool m_SupportsFence;
        int m_AllocatedSizeInBytes;
        int m_Frame;
        
        public int AllocatedCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_SlotAllocator.MaxAllocatedSlot;
        }

        public int AllocatedSizeInBytes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_AllocatedSizeInBytes;
        }

        public int FrameCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_Frame;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferPool(int initialCapacity)
        {
            m_SlotAllocator = new SlotAllocator(initialCapacity, AllocatorManager.Persistent);
            m_SlotAllocator.Allocate(); // Null bufferID
            m_Buffers = new ComputeBuffer[initialCapacity];
            m_Fences = new AsyncGPUReadbackRequest[initialCapacity];
            m_WasMapped = new bool[initialCapacity];
            m_InUseDescriptors = new BufferDescriptor[initialCapacity];
            m_BufferDescriptors = new BufferDescriptor[initialCapacity];
            m_Hashes = new int[initialCapacity];
            m_LastUsedFrame = new int[initialCapacity];
            m_MappedSizes = new int[initialCapacity];
            m_MappedPtr = new IntPtr[initialCapacity];
            m_AllocatedSizeInBytes = 0;
            m_Frame = 0;
            m_SupportsFence = SystemInfo.supportsAsyncGPUReadback;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            foreach (int allocated in m_SlotAllocator)
            {
                UnlockBufferAfterWrite(new PooledBufferID(allocated), false);
                m_Buffers[allocated]?.Dispose();
            }
            
            m_SlotAllocator.Dispose();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureCapacity()
        {
            int requiredCapacity = m_SlotAllocator.MaxAllocatedSlot + 1;
            if (requiredCapacity > m_Buffers.Length)
            {
                int newCapacity = MathUtility.NextMultipleOf(requiredCapacity, 16);
                Array.Resize(ref m_Buffers, newCapacity);
                Array.Resize(ref m_Fences, newCapacity);
                Array.Resize(ref m_WasMapped, newCapacity);
                Array.Resize(ref m_InUseDescriptors, newCapacity);
                Array.Resize(ref m_BufferDescriptors, newCapacity);
                Array.Resize(ref m_Hashes, newCapacity);
                Array.Resize(ref m_LastUsedFrame, newCapacity);
                Array.Resize(ref m_MappedSizes, newCapacity);
                Array.Resize(ref m_MappedPtr, newCapacity);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DisposeBuffer(int index)
        {
            PooledBufferID bufferID = new PooledBufferID(index);
            EnsureAllocatedBuffer(bufferID);
            m_SlotAllocator.Free(bufferID);
            m_Buffers[index].Dispose();
            m_Buffers[index] = null;
            m_Fences[index] = default;
            m_WasMapped[index] = false;
            m_InUseDescriptors[index] = default;
            m_BufferDescriptors[index] = default;
            m_Hashes[index] = 0;
            m_LastUsedFrame[index] = 0;
            m_MappedSizes[index] = 0;
            m_MappedPtr[index] = IntPtr.Zero;
        }
        
        public int InQueueFrames
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_SupportsFence ? 0 : RenderUtility.NumFramesInFlight + 1;
        }
        
        public int MaxFramesUntilRelease
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => k_FramesUntilRelease + InQueueFrames;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsBufferAvailable(int bufferID)
        {
            if (m_MappedPtr[bufferID] != IntPtr.Zero)
                return false;

            if (m_WasMapped[bufferID])
            {
                if (m_SupportsFence)
                    return m_Fences[bufferID].done;
                else
                    return (m_Frame - m_LastUsedFrame[bufferID]) > InQueueFrames;
            }
            
            return m_LastUsedFrame[bufferID] != k_InUseMarker;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsBufferStale(int bufferID) => (m_Frame - m_LastUsedFrame[bufferID]) > MaxFramesUntilRelease;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void NextFrame()
        {
            m_AllocatedSizeInBytes = 0;
            
            foreach (int allocatedIndex in m_SlotAllocator)
            {
                if (allocatedIndex == 0)
                    continue;
                
                ComputeBuffer buffer = m_Buffers[allocatedIndex];
                int bufferAllocatedSizeInBytes = buffer.stride * buffer.count;
                m_AllocatedSizeInBytes += bufferAllocatedSizeInBytes;
                
                if (IsBufferAvailable(allocatedIndex) && IsBufferStale(allocatedIndex))
                {
                    DisposeBuffer(allocatedIndex);
                    m_AllocatedSizeInBytes -= bufferAllocatedSizeInBytes;
                }
            }
            
            m_Frame++;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PooledBufferID GetBuffer(BufferDescriptor descriptor, string name, Alignment alignment = Alignment.None)
        {
            EnsureValidDescriptor(descriptor);

            BufferDescriptor alignedDescriptor = descriptor;
            alignedDescriptor.Count = alignment switch
            {
                Alignment.Page       => MathUtility.NextMultipleOf(alignedDescriptor.Stride * alignedDescriptor.Count, k_PageSize) / alignedDescriptor.Stride,
                Alignment.PowerOfTwo => math.ceilpow2(alignedDescriptor.Stride * alignedDescriptor.Count) / alignedDescriptor.Stride,
                _                    => alignedDescriptor.Count
            };

            int hash = alignedDescriptor.GetHashCode();
            foreach (int allocated in m_SlotAllocator)
            {
                if (allocated == 0)
                    continue;
                
                if (m_Hashes[allocated] != hash) 
                    continue;

                if (IsBufferAvailable(allocated))
                {
                    m_LastUsedFrame[allocated] = k_InUseMarker;
                    m_InUseDescriptors[allocated].Count = descriptor.Count;
                    m_Buffers[allocated].name = name;
                    m_WasMapped[allocated] = false;
                    m_Fences[allocated] = default;
                    return new PooledBufferID(allocated);
                }
            }

            PooledBufferID newSlot = new PooledBufferID(m_SlotAllocator.Allocate());
            EnsureCapacity();
            
            ComputeBuffer newBuffer = new ComputeBuffer(alignedDescriptor.Count, alignedDescriptor.Stride, alignedDescriptor.Type, alignedDescriptor.Mode);
            newBuffer.name = name;
            
            m_Buffers[newSlot] = newBuffer;
            m_BufferDescriptors[newSlot] = alignedDescriptor;
            m_InUseDescriptors[newSlot] = descriptor;
            m_Hashes[newSlot] = hash;
            m_LastUsedFrame[newSlot] = k_InUseMarker;
            m_MappedPtr[newSlot] = IntPtr.Zero;
            m_WasMapped[newSlot] = false;
            m_Fences[newSlot] = default;
            
            return newSlot;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComputeBuffer GetBuffer(PooledBufferID bufferID)
        {
            EnsureAllocatedBuffer(bufferID);
            return m_Buffers[bufferID];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* LockBufferForWrite(PooledBufferID bufferID, int startOffsetInBytes, int sizeInBytes)
        {
            EnsureNotMapped(bufferID);
            
            // Assert.IsTrue(startOffsetInBytes + sizeInBytes <= m_Buffers[bufferID].count * m_Buffers[bufferID].stride);
            byte* mappedPtr = m_Buffers[bufferID].BeginWrite<byte>(startOffsetInBytes, sizeInBytes).GetUnsafePtrT();
            m_MappedPtr[bufferID] = (IntPtr)mappedPtr;
            m_MappedSizes[bufferID] = sizeInBytes;
            return mappedPtr;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnlockBufferAfterWrite(PooledBufferID bufferID, bool release, int lengthWrittenInBytes = -1)
        {
            if (m_MappedPtr[bufferID] != IntPtr.Zero)
            {
                if (lengthWrittenInBytes == -1)
                    lengthWrittenInBytes = m_MappedSizes[bufferID];

                // Assert.IsTrue(lengthWrittenInBytes <= m_Buffers[bufferID].count * m_Buffers[bufferID].stride);
                m_Buffers[bufferID].EndWrite<byte>(lengthWrittenInBytes);
                m_MappedPtr[bufferID] = IntPtr.Zero;
                m_MappedSizes[bufferID] = 0;
                m_WasMapped[bufferID] = true;

                if (m_SupportsFence)
                {
                    m_Fences[bufferID] = AsyncGPUReadback.Request(m_Buffers[bufferID]);
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                    m_Fences[bufferID].forcePlayerLoopUpdate = !UnityEditor.EditorApplication.isPlaying;
#endif
                }
                
                if (release)
                    m_LastUsedFrame[bufferID] = m_Frame;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReleaseBuffer(PooledBufferID bufferID)
        {
            EnsureAllocatedBuffer(bufferID);
            m_LastUsedFrame[bufferID] = m_Frame;
        }
        
        [Conditional("UNITY_ASSERTIONS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureValidDescriptor(in BufferDescriptor descriptor)
        {
            if (descriptor.Stride <= 0)
                throw new ArgumentException("Buffer stride must be greater than zero.");
            if (descriptor.Count < 0)
                throw new ArgumentException("Buffer count must be greater than or equal to zero.");
        }
        
        [Conditional("UNITY_ASSERTIONS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureValidSlot(PooledBufferID bufferID)
        {
            if (!m_SlotAllocator.Exists(bufferID))
                throw new IndexOutOfRangeException("Buffer bufferID is not valid for this pool.");
        }
        
        [Conditional("UNITY_ASSERTIONS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void EnsureAllocatedBuffer(PooledBufferID bufferID)
        {
            EnsureValidSlot(bufferID);
            if (m_Buffers[bufferID] == null)
                throw new InvalidOperationException("Buffer is not allocated.");
        }
        
        [Conditional("UNITY_ASSERTIONS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureNotMapped(PooledBufferID bufferID)
        {
            EnsureAllocatedBuffer(bufferID);
            if (m_MappedPtr[bufferID] != IntPtr.Zero)
                throw new InvalidOperationException("Buffer is already mapped.");
        }
        
        [Conditional("UNITY_ASSERTIONS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void EnsureMapped(PooledBufferID bufferID)
        {
            EnsureAllocatedBuffer(bufferID);
            if (m_MappedPtr[bufferID] == IntPtr.Zero)
                throw new InvalidOperationException("Buffer is not mapped.");
        }
    }
}