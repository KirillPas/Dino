// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MA.Flora.Rendering
{
    [DebuggerDisplay("Current={ScatterIndex}, Size={ScatterSize}")]
    unsafe struct ThreadedScatterData
    {
        public byte* UploadPtr;
        public int UploadBytesPerElement;
        public int UploadSize;

        public uint* ScatterPtr;
        public int ScatterIndex;
        public int ScatterSize;
    }

    unsafe struct ThreadedScatterUploader
    {
        [NativeDisableUnsafePtrRestriction] internal ThreadedScatterData* MappedData;

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MappedData != null && MappedData->ScatterPtr != null && MappedData->UploadPtr != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginScatter(int elementCount, out byte* dataPtr, out uint* scatterPtr)
        {
            // Assert.IsTrue(MappedData->ScatterIndex + elementCount <= MappedData->ScatterSize);
            int scatterIndex = Interlocked.Add(ref MappedData->ScatterIndex, elementCount) - elementCount;
            dataPtr = MappedData->UploadPtr + (scatterIndex * MappedData->UploadBytesPerElement);
            scatterPtr = MappedData->ScatterPtr + scatterIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* AddScatter(int destinationIndex, int elementCount)
        {
            int scatterIndex = Interlocked.Add(ref MappedData->ScatterIndex, elementCount) - elementCount;
            for (int i = 0; i < elementCount; i++)
                MappedData->ScatterPtr[scatterIndex + i] = (uint)(destinationIndex + i);

            return MappedData->UploadPtr + scatterIndex * MappedData->UploadBytesPerElement;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteScatter(int destinationIndex, byte* dataPtr, int elementCount)
        {
            int scatterIndex = Interlocked.Add(ref MappedData->ScatterIndex, elementCount) - elementCount;
            for (int i = 0; i < elementCount; i++)
                MappedData->ScatterPtr[scatterIndex + i] = (uint)(destinationIndex + i);

            UnsafeUtility.MemCpy(MappedData->UploadPtr + scatterIndex * MappedData->UploadBytesPerElement, dataPtr, elementCount * MappedData->UploadBytesPerElement);
        }
    }

    sealed unsafe class BufferScatterUploader : IDisposable
    {
        string m_UploadName;
        string m_ScatterName;
        BufferPool m_Pool;
        int m_UploadBytesPerElement;
        ThreadedScatterData* m_MappedData;
        PooledBufferID m_UploadBufferID;
        PooledBufferID m_ScatterBufferID;
        bool m_Mapped;

        const int k_MaxUploadSize = 1 << 26;

        public BufferScatterUploader(int uploadBytesPerElement, BufferPool pool, string name)
        {
            m_UploadName = $"{name}-UploadBuffer";
            m_ScatterName = $"{name}-ScatterBuffer";
            m_Pool = pool;
            m_UploadBytesPerElement = uploadBytesPerElement;
            m_MappedData = AllocatorManager.Allocate<ThreadedScatterData>(AllocatorManager.Persistent);
            m_UploadBufferID = PooledBufferID.Null;
            m_ScatterBufferID = PooledBufferID.Null;
        }

        public void Dispose()
        {
            if (m_MappedData != null)
            {
                AllocatorManager.Free(AllocatorManager.Persistent, m_MappedData);
                m_MappedData = null;
            }

            m_UploadBufferID = PooledBufferID.Null;
            m_ScatterBufferID = PooledBufferID.Null;
        }

        public void MapUploader(int count, out ThreadedScatterUploader outScatterUploader)
        {
            try
            {
                if (m_Mapped)
                    throw new InvalidOperationException("Uploader is already mapped.");

                int uploadBytes = count * m_UploadBytesPerElement;
                int uploadBufferSize = math.min(math.ceilpow2(uploadBytes), k_MaxUploadSize * m_UploadBytesPerElement);

                BufferDescriptor uploadDescriptor = CreateUploadDescriptor(m_UploadBytesPerElement, uploadBufferSize / m_UploadBytesPerElement);
                m_UploadBufferID = m_Pool.GetBuffer(uploadDescriptor, m_UploadName);
                m_Pool.EnsureAllocatedBuffer(m_UploadBufferID);
                byte* uploadData = m_Pool.LockBufferForWrite(m_UploadBufferID, 0, uploadBytes);

                const int scatterBytesPerElement = sizeof(uint);
                int scatterBytes = count * scatterBytesPerElement;
                int scatterBufferSize = math.min(math.ceilpow2(scatterBytes), k_MaxUploadSize * scatterBytesPerElement);

                BufferDescriptor scatterDescriptor = CreateUploadDescriptor(scatterBytesPerElement, scatterBufferSize / scatterBytesPerElement);
                m_ScatterBufferID = m_Pool.GetBuffer(scatterDescriptor, m_ScatterName);
                m_Pool.EnsureAllocatedBuffer(m_ScatterBufferID);
                byte* scatterData = m_Pool.LockBufferForWrite(m_ScatterBufferID, 0, scatterBytes);

                m_MappedData->ScatterPtr = (uint*)scatterData;
                m_MappedData->UploadPtr = uploadData;
                m_MappedData->UploadSize = uploadBytes;
                m_MappedData->UploadBytesPerElement = m_UploadBytesPerElement;
                m_MappedData->ScatterIndex = 0;
                m_MappedData->ScatterSize = count;
                outScatterUploader = new ThreadedScatterUploader { MappedData = m_MappedData };
                m_Mapped = true;
            }
            catch (Exception e)
            {
                outScatterUploader = default;
                m_UploadBufferID = PooledBufferID.Null;
                m_ScatterBufferID = PooledBufferID.Null;
                m_Mapped = false;
                Debug.LogException(e);
            }
        }
        
        public void DispatchScatter(GraphicsBuffer destination)
        {
            if (m_Mapped)
            {
                m_Mapped = false;
                try
                {
                    if (m_MappedData->ScatterIndex > m_MappedData->ScatterSize)
                        throw new InvalidOperationException("Write count exceeds the scatter buffer capacity.");
                    if (m_ScatterBufferID.IsValid == false || m_UploadBufferID.IsValid == false)
                        throw new InvalidOperationException("Buffers are not valid.");
                    
                    int writtenCount = m_MappedData->ScatterIndex;
                    if (writtenCount > 0)
                    {
                        m_Pool.UnlockBufferAfterWrite(m_UploadBufferID, true, writtenCount * m_MappedData->UploadBytesPerElement);
                        m_Pool.UnlockBufferAfterWrite(m_ScatterBufferID, true, writtenCount * sizeof(uint));
                        
                        ComputeBuffer uploadBuffer = m_Pool.GetBuffer(m_UploadBufferID);
                        ComputeBuffer scatterBuffer = m_Pool.GetBuffer(m_ScatterBufferID);
                        
                        if (destination != null)
                            BufferUtility.Scatter(destination, scatterBuffer, uploadBuffer, m_MappedData->UploadBytesPerElement, writtenCount);
                    }
                    else
                    {
                        m_Pool.UnlockBufferAfterWrite(m_UploadBufferID, true, 0);
                        m_Pool.UnlockBufferAfterWrite(m_ScatterBufferID, true, 0);
                    }
                }
                catch (Exception e)
                {
                    m_Pool.UnlockBufferAfterWrite(m_UploadBufferID, true, 0);
                    m_Pool.UnlockBufferAfterWrite(m_ScatterBufferID, true, 0);
                    Debug.LogException(e);
                }
                finally
                {
                    *m_MappedData = default;
                    m_UploadBufferID = PooledBufferID.Null;
                    m_ScatterBufferID = PooledBufferID.Null;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static BufferDescriptor CreateUploadDescriptor(int bytesPerElement, int count)
        {
            return new BufferDescriptor
            {
                Type = InstanceBufferConfig.ComputeBufferType,
                Mode = ComputeBufferMode.SubUpdates,
                Stride = bytesPerElement,
                Count = count
            };
        }
    }
}