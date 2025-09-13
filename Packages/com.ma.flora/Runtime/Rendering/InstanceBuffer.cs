// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    enum InstanceBufferType
    {
        Raw,
        Float4,
    }

    enum InstanceTransformPackingMode
    {
        Disabled,
        Float4x2,
    }

    static class InstanceBufferConfig
    {
#if FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_FLOAT4
        public const InstanceBufferType    BufferType             = InstanceBufferType.Float4;
        public const ComputeBufferType     ComputeBufferType      = UnityEngine.ComputeBufferType.Structured;
        public const GraphicsBuffer.Target GraphicsBufferType     = UnityEngine.GraphicsBuffer.Target.Structured;
        public const int                   BufferAlignment        = 16;
        public const int                   BufferStride           = 16;
        public const int                   ScatterStride          = 16;
        public const int                   MemCpyStride           = 16;
#else
        public const InstanceBufferType    BufferType             = InstanceBufferType.Raw;
        public const ComputeBufferType     ComputeBufferType      = UnityEngine.ComputeBufferType.Raw;
        public const GraphicsBuffer.Target GraphicsBufferType     = UnityEngine.GraphicsBuffer.Target.Raw;
        public const int                   BufferAlignment        = 16;
        public const int                   BufferStride           = 4;
        public const int                   ScatterStride          = 16;
        public const int                   MemCpyStride           = 4;
#endif

#if FLORA_CONFIG_INSTANCE_DATA_TRANSFORM_PACKING_DISABLED
        public const InstanceTransformPackingMode PackingMode = InstanceTransformPackingMode.Disabled;
#else
        public const InstanceTransformPackingMode PackingMode = InstanceTransformPackingMode.Float4x2;
#endif

        public const int TransformElements            = 2;
        public const int TransformElementStride       = 16;
        public const int TransformStride              = TransformElements * TransformElementStride;

        public const int SHCoefficientsElements       = 8;
        public const int SHCoefficientsElementStride  = 16;
        public const int SHCoefficientsStride         = SHCoefficientsElements * SHCoefficientsElementStride;

        public const int EditorDataElements           = 1;
        public const int EditorDataElementStride      = 4;
        public const int EditorDataStride             = EditorDataElements * EditorDataElementStride;
    }

    struct BuiltinBatchOffsets
    {
        public int TransformsOffset;
        public int SHCoefficientsOffset;
        public int RendererIDOffset;
        public int EditorDataOffset;
    }

    struct BuiltinPropertyMetadataUniversal
    {
        public uint unity_ObjectToWorld;
        public uint unity_WorldToObject;
        public uint unity_LODFade;
        public uint unity_RenderingLayer;
        public uint unity_SpecCube0_HDR;
        public uint unity_LightmapST;
        public uint unity_LightmapIndex;
        public uint unity_DynamicLightmapST;
        public uint unity_MatrixPreviousM;
        public uint unity_MatrixPreviousMI;
        public uint unity_SHCoefficients;
        public uint unity_EntityId;
    }

    struct BuiltinPropertyMetadataHDRP
    {
        public uint unity_ObjectToWorld;
        public uint unity_WorldToObject;
        public uint unity_LightmapST;
        public uint unity_LightmapIndex;
        public uint unity_DynamicLightmapST;
        public uint unity_MatrixPreviousM;
        public uint unity_MatrixPreviousMI;
        public uint unity_SHCoefficients;
        public uint unity_EntityId;
    }

    struct InstancedPropertyMetadata : IEquatable<InstancedPropertyMetadata>, IComparable<InstancedPropertyMetadata>
    {
        public static readonly InstancedPropertyMetadata Null = default;

        public int NameID;
        public int Offset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedPropertyMetadata(int nameID, int offset)
        {
            NameID = nameID;
            Offset = offset;
        }

        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Offset > 0;
        }

        public int PerInstanceOffset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedPropertyMetadata other)
        {
            int nameIDComparison = NameID.CompareTo(other.NameID);
            if (nameIDComparison != 0) return nameIDComparison;
            return Offset.CompareTo(other.Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedPropertyMetadata other) => NameID == other.NameID && Offset == other.Offset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedPropertyMetadata other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => unchecked((NameID * 397) ^ Offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedPropertyMetadata left, InstancedPropertyMetadata right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedPropertyMetadata left, InstancedPropertyMetadata right) => !left.Equals(right);
    }

    sealed unsafe class InstanceBuffer : IDisposable
    {
        // Size is 256 bytes, the minimum constant buffer offset alignment.
        // Forces the C# array layout to match the constant buffer layout.
        [StructLayout(LayoutKind.Sequential, Size = 256)]
        struct GPUBuiltinPropertyMetadata
        {
            public uint flora_RendererDataStrideSOA;
            public uint flora_InstanceMetadata;
            public uint flora_SHCoefficientsMetadata;
            public uint flora_EditorDataMetadata;
        }

        static class ShaderIDs
        {
            public static readonly int flora_BuiltinPropertyMetadataID = Shader.PropertyToID("flora_BuiltinPropertyMetadata");
            public static readonly int unity_BuiltinPropertyMetadataID = Shader.PropertyToID("BuiltinPropertyMetadata");
        }

        public const uint PerInstanceDataBit = 0x80000000;
        public const uint AddressMask        = 0x7fffffff;

        GraphicsBuffer m_Buffer;

        int[] m_ReferenceCounts;
        ElementAllocator[] m_BatchInstanceAllocators;
        InstancedBatchDescriptor[] m_BatchDescriptions;
        int[] m_InstanceCapacity;

        BuiltinBatchOffsets[] m_BatchBuiltinMetadata;
        UnsafeArray<InstancedPropertyMetadata>[] m_BatchPropertyMetadataArrays;

        UnsafeArray<GPUBuiltinPropertyMetadata> m_GPUPropertyMetadatas;
        GraphicsBuffer[] m_PropertyMetadataBuffers = new GraphicsBuffer[1];
        int m_MinConstantBufferOffsetAlignment;
        bool m_SupportsConstantBufferAlignment;
        int m_RendererDataStrideSOA;

        UnsafeIndirectList<InstancedBatchID> m_ActiveBatchIDs;
        UnsafeIndirectList<InstancedBatchID> m_GPUBatchIDs;
        bool m_AllocationsDirty;

        public static implicit operator GraphicsBuffer(InstanceBuffer buffer) => buffer.m_Buffer;

        public InstanceBuffer()
        {
            m_ReferenceCounts = new int[8];
            m_BatchInstanceAllocators = new ElementAllocator[8];
            m_BatchDescriptions = new InstancedBatchDescriptor[8];
            m_InstanceCapacity = new int[8];

            m_BatchBuiltinMetadata = new BuiltinBatchOffsets[8];
            m_BatchPropertyMetadataArrays = new UnsafeArray<InstancedPropertyMetadata>[8];

            m_GPUPropertyMetadatas = new UnsafeArray<GPUBuiltinPropertyMetadata>(8, AllocatorManager.Persistent);
            m_MinConstantBufferOffsetAlignment = SystemInfo.constantBufferOffsetAlignment;
            m_SupportsConstantBufferAlignment = m_MinConstantBufferOffsetAlignment > 0;

            m_ActiveBatchIDs = new UnsafeIndirectList<InstancedBatchID>(8, AllocatorManager.Persistent);
            m_GPUBatchIDs = new UnsafeIndirectList<InstancedBatchID>(8, AllocatorManager.Persistent);
        }

        public void Dispose()
        {
            m_Buffer?.Dispose();

            for (int i = 0; i < m_BatchInstanceAllocators.Length; i++)
                m_BatchInstanceAllocators[i].Dispose();

            for (int i = 0; i < m_BatchDescriptions.Length; i++)
                m_BatchDescriptions[i].Dispose();

            for (int i = 0; i < m_BatchPropertyMetadataArrays.Length; i++)
                m_BatchPropertyMetadataArrays[i].Dispose();

            for (int i = 0; i < m_PropertyMetadataBuffers.Length; i++)
                m_PropertyMetadataBuffers[i]?.Dispose();

            m_GPUPropertyMetadatas.Dispose();
            m_ActiveBatchIDs.Dispose();
            m_GPUBatchIDs.Dispose();
        }

        public int AllocatedSizeInBytes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_Buffer == null ? 0 : m_Buffer.count * m_Buffer.stride;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasBatch(InstancedBatchID batchID)
            => m_BatchInstanceAllocators.Length > batchID && m_BatchInstanceAllocators[batchID].IsCreated;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValidBatch(InstancedBatchID batchID)
            => HasBatch(batchID) && m_InstanceCapacity[batchID] > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BuiltinBatchOffsets GetBuiltinBatchOffsets(InstancedBatchID batchID)
            => m_BatchBuiltinMetadata[batchID];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UnsafeArray<InstancedPropertyMetadata> GetPropertyMetadataArray(InstancedBatchID batchID)
            => m_BatchPropertyMetadataArrays[batchID];

        public void RegisterBatch(InstancedBatchID batchID, in InstancedBatchDescriptor descriptor)
        {
            if (m_BatchInstanceAllocators.Length <= batchID)
            {
                int newCapacity = math.max(math.ceilpow2(batchID + 1), 8);
                Array.Resize(ref m_ReferenceCounts, newCapacity);
                Array.Resize(ref m_BatchInstanceAllocators, newCapacity);
                Array.Resize(ref m_BatchDescriptions, newCapacity);
                Array.Resize(ref m_InstanceCapacity, newCapacity);
                Array.Resize(ref m_BatchBuiltinMetadata, newCapacity);
                Array.Resize(ref m_BatchPropertyMetadataArrays, newCapacity);
                m_GPUPropertyMetadatas.Resize(newCapacity, AllocatorManager.Persistent);
            }

            if (!m_BatchInstanceAllocators[batchID].IsCreated)
            {
                m_ReferenceCounts[batchID] = 1;
                m_BatchInstanceAllocators[batchID] = new ElementAllocator(256, AllocatorManager.Persistent);
                m_BatchDescriptions[batchID] = new InstancedBatchDescriptor(descriptor, AllocatorManager.Persistent);
                m_InstanceCapacity[batchID] = 0;
                m_BatchBuiltinMetadata[batchID] = default;

                int propertyCount = descriptor.PropertyInfoArray.Length;
                m_BatchPropertyMetadataArrays[batchID] = propertyCount > 0 ? new UnsafeArray<InstancedPropertyMetadata>(propertyCount, AllocatorManager.Persistent) : default;
                m_ActiveBatchIDs.AddSorted(batchID);
            }
            else
            {
                m_ReferenceCounts[batchID]++;
            }
        }

        public void UnregisterBatch(InstancedBatchID batchID)
        {
            if (m_BatchInstanceAllocators.Length <= batchID)
                return;

            if (m_BatchInstanceAllocators[batchID].IsCreated)
            {
                m_ReferenceCounts[batchID]--;
                if (m_ReferenceCounts[batchID] <= 0)
                {
                    m_BatchInstanceAllocators[batchID].Dispose();
                    m_BatchInstanceAllocators[batchID] = default;
                    m_BatchDescriptions[batchID].Dispose();
                    m_BatchDescriptions[batchID] = default;
                    m_BatchBuiltinMetadata[batchID] = default;
                    m_BatchPropertyMetadataArrays[batchID].Dispose();
                    m_BatchPropertyMetadataArrays[batchID] = default;
                    m_InstanceCapacity[batchID] = 0;
                    m_ActiveBatchIDs.Remove(batchID);
                    m_AllocationsDirty = true;
                }
            }
        }

        public BufferAllocation AllocateInstances(InstancedBatchID batchID, int instanceCount)
        {
            if (m_BatchInstanceAllocators.Length <= batchID || !m_BatchInstanceAllocators[batchID].IsCreated)
                return default;

            BufferAllocation allocation = new BufferAllocation { Offset = m_BatchInstanceAllocators[batchID].Allocate(instanceCount), Length = instanceCount };
            m_AllocationsDirty = true;
            return allocation;
        }

        public void FreeInstances(InstancedBatchID batchID, BufferAllocation allocation)
        {
            if (m_BatchInstanceAllocators.Length <= batchID)
                return;

            if (m_BatchInstanceAllocators[batchID].IsCreated)
            {
                m_BatchInstanceAllocators[batchID].Free(allocation.Offset, allocation.Length);
                m_AllocationsDirty = true;
            }
        }

        public void UpdateGroupDataStrideSOA(int groupDataStrideSOA)
        {
            if (m_RendererDataStrideSOA != groupDataStrideSOA)
            {
                for (int i = 0; i < m_GPUBatchIDs.Length; i++)
                {
                    InstancedBatchID batchID = m_GPUBatchIDs[i];
                    m_GPUPropertyMetadatas.Ptr[batchID].flora_RendererDataStrideSOA = (uint)groupDataStrideSOA;
                }

                m_RendererDataStrideSOA = groupDataStrideSOA;
                m_AllocationsDirty = true;
            }
        }

        public void SetGlobalMetadata(InstancedBatchID batchID)
        {
            if (m_BatchInstanceAllocators.Length <= batchID || !m_BatchInstanceAllocators[batchID].IsCreated)
                return;

            int size = UnsafeUtility.SizeOf<GPUBuiltinPropertyMetadata>();
            if (m_SupportsConstantBufferAlignment)
            {
                int offset = math.max(batchID * size, m_MinConstantBufferOffsetAlignment * batchID);
                Shader.SetGlobalConstantBuffer(ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[0], offset, size);
            }
            else
            {
                Shader.SetGlobalConstantBuffer(ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[batchID], 0, size);
            }

            if (m_BatchPropertyMetadataArrays[batchID].IsCreated)
            {
                UnsafeArray<InstancedPropertyMetadata> properties = m_BatchPropertyMetadataArrays[batchID];
                for (int i = 0; i < properties.Length; i++)
                {
                    InstancedPropertyMetadata metadata = properties[i];
                    if (!metadata.IsCreated)
                        continue;

                    Shader.SetGlobalInteger(metadata.NameID, metadata.PerInstanceOffset);
                }
            }
        }

        public void SetGlobalMetadata(InstancedBatchID batchID, CommandBuffer cmd)
        {
            if (m_BatchInstanceAllocators.Length <= batchID || !m_BatchInstanceAllocators[batchID].IsCreated)
                return;

            int size = UnsafeUtility.SizeOf<GPUBuiltinPropertyMetadata>();
            if (m_SupportsConstantBufferAlignment)
            {
                int offset = math.max(batchID * size, m_MinConstantBufferOffsetAlignment * batchID);
                cmd.SetGlobalConstantBuffer(m_PropertyMetadataBuffers[0], ShaderIDs.flora_BuiltinPropertyMetadataID, offset, size);
            }
            else
            {
                cmd.SetGlobalConstantBuffer(m_PropertyMetadataBuffers[batchID], ShaderIDs.flora_BuiltinPropertyMetadataID, 0, size);
            }

            if (m_BatchPropertyMetadataArrays[batchID].IsCreated)
            {
                UnsafeArray<InstancedPropertyMetadata> properties = m_BatchPropertyMetadataArrays[batchID];
                for (int i = 0; i < properties.Length; i++)
                {
                    InstancedPropertyMetadata metadata = properties[i];
                    if (!metadata.IsCreated)
                        continue;

                    cmd.SetGlobalInteger(metadata.NameID, metadata.PerInstanceOffset);
                }
            }
        }

        public void SetComputeMetadata(InstancedBatchID batchID, CommandBuffer cmd, ComputeShader cs)
        {
            if (m_BatchInstanceAllocators.Length <= batchID || !m_BatchInstanceAllocators[batchID].IsCreated)
                return;

            int size = UnsafeUtility.SizeOf<GPUBuiltinPropertyMetadata>();
            if (m_SupportsConstantBufferAlignment)
            {
                int offset = math.max(batchID * size, m_MinConstantBufferOffsetAlignment * batchID);
                cmd.SetComputeConstantBufferParam(cs, ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[0], offset, size);
            }
            else
            {
                cmd.SetComputeConstantBufferParam(cs, ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[batchID], 0, size);
            }

            if (m_BatchPropertyMetadataArrays[batchID].IsCreated)
            {
                UnsafeArray<InstancedPropertyMetadata> properties = m_BatchPropertyMetadataArrays[batchID];
                for (int i = 0; i < properties.Length; i++)
                {
                    InstancedPropertyMetadata metadata = properties[i];
                    if (!metadata.IsCreated)
                        continue;

                    cmd.SetComputeIntParam(cs, metadata.NameID, metadata.PerInstanceOffset);
                }
            }
        }

#if UNITY_2023_3_OR_NEWER
        public void SetComputeMetadata(InstancedBatchID batchID, ComputeCommandBuffer cmd, ComputeShader cs)
        {
            if (m_BatchInstanceAllocators.Length <= batchID || !m_BatchInstanceAllocators[batchID].IsCreated)
                return;

            int size = UnsafeUtility.SizeOf<GPUBuiltinPropertyMetadata>();
            if (m_SupportsConstantBufferAlignment)
            {
                int offset = math.max(batchID * size, m_MinConstantBufferOffsetAlignment * batchID);
                cmd.SetComputeConstantBufferParam(cs, ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[0], offset, size);
            }
            else
            {
                cmd.SetComputeConstantBufferParam(cs, ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[batchID], 0, size);
            }

            if (m_BatchPropertyMetadataArrays[batchID].IsCreated)
            {
                UnsafeArray<InstancedPropertyMetadata> properties = m_BatchPropertyMetadataArrays[batchID];
                for (int i = 0; i < properties.Length; i++)
                {
                    InstancedPropertyMetadata metadata = properties[i];
                    if (!metadata.IsCreated)
                        continue;

                    cmd.SetComputeIntParam(cs, metadata.NameID, metadata.PerInstanceOffset);
                }
            }
        }
#endif

        public void SetMaterialMetadata(InstancedBatchID batchID, Material material)
        {
            if (m_BatchInstanceAllocators.Length <= batchID || !m_BatchInstanceAllocators[batchID].IsCreated)
                return;

            int size = UnsafeUtility.SizeOf<GPUBuiltinPropertyMetadata>();
            if (m_SupportsConstantBufferAlignment)
            {
                int offset = math.max(batchID * size, m_MinConstantBufferOffsetAlignment * batchID);
                material.SetConstantBuffer(ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[0], offset, size);
            }
            else
            {
                material.SetConstantBuffer(ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[batchID], 0, size);
            }

            if (m_BatchPropertyMetadataArrays[batchID].IsCreated)
            {
                UnsafeArray<InstancedPropertyMetadata> properties = m_BatchPropertyMetadataArrays[batchID];
                for (int i = 0; i < properties.Length; i++)
                {
                    InstancedPropertyMetadata metadata = properties[i];
                    if (!metadata.IsCreated)
                        continue;

                    material.SetInteger(metadata.NameID, metadata.PerInstanceOffset);
                }
            }
        }

        public void SetMaterialMetadata(InstancedBatchID batchID, MaterialPropertyBlock mpb)
        {
            if (m_BatchInstanceAllocators.Length <= batchID || !m_BatchInstanceAllocators[batchID].IsCreated)
                return;

            int size = UnsafeUtility.SizeOf<GPUBuiltinPropertyMetadata>();
            if (m_SupportsConstantBufferAlignment)
            {
                int offset = math.max(batchID * size, m_MinConstantBufferOffsetAlignment * batchID);
                mpb.SetConstantBuffer(ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[0], offset, size);
            }
            else
            {
                mpb.SetConstantBuffer(ShaderIDs.flora_BuiltinPropertyMetadataID, m_PropertyMetadataBuffers[batchID], 0, size);
            }

            if (m_BatchPropertyMetadataArrays[batchID].IsCreated)
            {
                UnsafeArray<InstancedPropertyMetadata> properties = m_BatchPropertyMetadataArrays[batchID];
                for (int i = 0; i < properties.Length; i++)
                {
                    InstancedPropertyMetadata metadata = properties[i];
                    if (!metadata.IsCreated)
                        continue;

                    mpb.SetInteger(metadata.NameID, metadata.PerInstanceOffset);
                }
            }
        }

        public void RebuildLayoutIfNeeded()
        {
            if (!m_AllocationsDirty)
                return;

            m_AllocationsDirty = false;
            bool bufferChanged = false;
            int activeBatchCount = m_ActiveBatchIDs.Length;

            const int kNullBytes = 64;
            int newBufferSize = kNullBytes;

            Span<int> oldInstanceCapacities = stackalloc int[activeBatchCount];
            Span<InstancedBatchID> oldBatchIDs = stackalloc InstancedBatchID[activeBatchCount];

            for (int i = 0; i < activeBatchCount; i++)
            {
                InstancedBatchID activeBatchID = m_ActiveBatchIDs[i];
                m_BatchInstanceAllocators[activeBatchID].MergeFree();

                // Calculate the new allocated size and stride for the batch
                // If any of these values don't match, we need to rebuild the buffer

                int oldBatchIndex = m_GPUBatchIDs.IndexOf(activeBatchID);
                InstancedBatchID oldBatchID = oldBatchIndex >= 0 ? m_GPUBatchIDs[oldBatchIndex] : InstancedBatchID.Null;
                oldInstanceCapacities[i] = oldBatchIndex >= 0 ? m_InstanceCapacity[oldBatchIndex] : 0;
                oldBatchIDs[i] = oldBatchID;

                int newInstanceCapacity = math.ceilpow2(m_BatchInstanceAllocators[activeBatchID].MaxAllocatedSize);
                bufferChanged |= !oldBatchID.IsValid || m_InstanceCapacity[oldBatchID] != newInstanceCapacity;
                m_InstanceCapacity[i] = newInstanceCapacity;

                int batchSize = newInstanceCapacity * m_BatchDescriptions[activeBatchID].AlignedSizeInBytes;
                newBufferSize += batchSize;
            }

            int newCapacityBytes = math.max(MathUtility.NextMultipleOf(math.ceilpow2(newBufferSize), InstanceBufferConfig.BufferAlignment), 256);
            GraphicsBuffer oldBuffer = m_Buffer;
            GraphicsBuffer newBuffer;

            if (oldBuffer == null || oldBuffer.count < newCapacityBytes || bufferChanged)
            {
                newBuffer = new GraphicsBuffer(InstanceBufferConfig.GraphicsBufferType, newCapacityBytes / InstanceBufferConfig.BufferStride, InstanceBufferConfig.BufferStride);
                newBuffer.name = InstanceBufferConfig.BufferType == InstanceBufferType.Float4 ? "Flora Instance Data Float4" : "Flora Instance Data Raw";
                BufferUtility.Memset(newBuffer, 0, 0, kNullBytes / InstanceBufferConfig.BufferStride);
                bufferChanged = true;
            }
            else
            {
                newBuffer = oldBuffer;
            }

            if (newBufferSize > 0 && bufferChanged)
            {
                // When the layout changes, we need to copy the data from the old buffer to the new buffer
                // Since each batch is stored in a SOA layout, we need to copy each batch's data array separately
                m_GPUBatchIDs.Clear();

                // The start of the buffer is always 4 zeroed UInt4s
                int globalOffset = kNullBytes;

                for (int validIndex = 0; validIndex < activeBatchCount; validIndex++)
                {
                    InstancedBatchID newBatchID = m_ActiveBatchIDs[validIndex];
                    InstancedBatchID oldBatchID = oldBatchIDs[validIndex];

                    InstancedBatchDescriptor newBatchDescriptor = m_BatchDescriptions[newBatchID];
                    BuiltinBatchOffsets oldBuiltinOffsets = oldBatchID != InstancedBatchID.Null ? m_BatchBuiltinMetadata[oldBatchID] : default;
                    BuiltinBatchOffsets newBuiltinOffsets = default;

                    int oldInstanceCapacity = oldInstanceCapacities[validIndex];
                    int newInstanceCapacity = m_InstanceCapacity[validIndex];
                    int copyCount = math.min(oldInstanceCapacity, newInstanceCapacity);

                    // Transforms (always present)

                    if (oldBuiltinOffsets.TransformsOffset > 0)
                    {
                        int srcOffset = oldBuiltinOffsets.TransformsOffset / InstanceBufferConfig.MemCpyStride;
                        int dstOffset = globalOffset / InstanceBufferConfig.MemCpyStride;
                        int elementCount = copyCount * InstanceBufferConfig.TransformStride / InstanceBufferConfig.MemCpyStride;
                        BufferUtility.Memcpy(newBuffer, oldBuffer, srcOffset, dstOffset, elementCount);
                    }

                    newBuiltinOffsets.TransformsOffset = globalOffset;
                    globalOffset += newInstanceCapacity * InstanceBufferConfig.TransformStride;
                    // globalByteOffset = MathUtility.NextMultipleOf(globalByteOffset, InstanceBufferConfig.BufferAlignment); // Not needed for transforms

                    // Light probes

                    if (newBatchDescriptor.HasBuiltinFlag(InstancedBatchFlags.SHCoefficients))
                    {
                        if (oldBuiltinOffsets.SHCoefficientsOffset > 0)
                        {
                            int srcOffset = oldBuiltinOffsets.SHCoefficientsOffset / InstanceBufferConfig.MemCpyStride;
                            int dstOffset = globalOffset / InstanceBufferConfig.MemCpyStride;
                            int elementCount = copyCount * InstanceBufferConfig.TransformStride / InstanceBufferConfig.MemCpyStride;
                            BufferUtility.Memcpy(newBuffer, oldBuffer, srcOffset, dstOffset, elementCount);
                        }

                        newBuiltinOffsets.SHCoefficientsOffset = globalOffset;
                        globalOffset += newInstanceCapacity * InstanceBufferConfig.TransformStride;
                        // globalByteOffset = MathUtility.NextMultipleOf(globalByteOffset, InstanceBufferConfig.BufferAlignment); // Not needed for SH coefficients
                    }
                    else
                    {
                        newBuiltinOffsets.SHCoefficientsOffset = 0;
                    }

                    // Editor data

                    if (newBatchDescriptor.HasBuiltinFlag(InstancedBatchFlags.EditorData))
                    {
                        if (oldBuiltinOffsets.EditorDataOffset > 0)
                        {
                            int srcOffset = oldBuiltinOffsets.EditorDataOffset / InstanceBufferConfig.MemCpyStride;
                            int dstOffset = globalOffset / InstanceBufferConfig.MemCpyStride;
                            int elementCount = copyCount * InstanceBufferConfig.EditorDataStride / InstanceBufferConfig.MemCpyStride;
                            BufferUtility.Memcpy(newBuffer, oldBuffer, srcOffset, dstOffset, elementCount);
                        }

                        newBuiltinOffsets.EditorDataOffset = globalOffset;
                        globalOffset += newInstanceCapacity * InstanceBufferConfig.EditorDataElementStride;
                        globalOffset = MathUtility.NextMultipleOf(globalOffset, InstanceBufferConfig.BufferAlignment);
                    }
                    else
                    {
                        newBuiltinOffsets.EditorDataOffset = 0;
                    }

                    m_BatchBuiltinMetadata[newBatchID] = newBuiltinOffsets;

                    // Custom properties

                    if (newBatchDescriptor.HasBuiltinFlag(InstancedBatchFlags.CustomProperties))
                    {
                        UnsafeArray<InstancedBatchPropertyInfo> propertyInfoArray = newBatchDescriptor.PropertyInfoArray;
                        UnsafeArray<InstancedPropertyMetadata> oldPropertyDataArray = m_BatchPropertyMetadataArrays[newBatchID];
                        UnsafeArray<InstancedPropertyMetadata> newPropertyDataArray = new UnsafeArray<InstancedPropertyMetadata>(propertyInfoArray.Length, AllocatorManager.Persistent);

                        for (int propertyIndex = 0; propertyIndex < propertyInfoArray.Length; propertyIndex++)
                        {
                            if (propertyInfoArray[propertyIndex].IsCreated)
                            {
                                int propertyStrideBytes = propertyInfoArray[propertyIndex].SizeInBytes;
                                int oldPropertyOffset = oldPropertyDataArray[propertyIndex].Offset;
                                if (oldPropertyOffset > 0)
                                {
                                    int srcOffset = oldPropertyOffset / InstanceBufferConfig.MemCpyStride;
                                    int dstOffset = globalOffset / InstanceBufferConfig.MemCpyStride;
                                    int elementCount = propertyStrideBytes * copyCount / InstanceBufferConfig.MemCpyStride;
                                    BufferUtility.Memcpy(newBuffer, oldBuffer, srcOffset, dstOffset, elementCount);
                                }

                                newPropertyDataArray[propertyIndex] = new InstancedPropertyMetadata(propertyInfoArray[propertyIndex].MetadataNameID, globalOffset);
                                globalOffset += newInstanceCapacity * propertyStrideBytes;
                                globalOffset = MathUtility.NextMultipleOf(globalOffset, InstanceBufferConfig.BufferAlignment);
                            }
                            else
                            {
                                newPropertyDataArray[propertyIndex] = InstancedPropertyMetadata.Null;
                            }
                        }

                        m_BatchPropertyMetadataArrays[newBatchID].Dispose();
                        m_BatchPropertyMetadataArrays[newBatchID] = newPropertyDataArray;
                    }

                    // Update the batch metadata with the new offsets (in bytes)

                    m_GPUPropertyMetadatas[newBatchID] = new GPUBuiltinPropertyMetadata
                    {
                        flora_RendererDataStrideSOA = (uint)m_RendererDataStrideSOA,
                        flora_InstanceMetadata = (uint)newBuiltinOffsets.TransformsOffset,
                        flora_SHCoefficientsMetadata = (uint)newBuiltinOffsets.SHCoefficientsOffset,
                        flora_EditorDataMetadata = (uint)newBuiltinOffsets.EditorDataOffset,
                    };

                    m_GPUBatchIDs.Add(newBatchID);
                }
            }

            // Assign the new buffer if it was reallocated

            if (oldBuffer != newBuffer)
            {
                oldBuffer?.Release();
                m_Buffer = newBuffer;
            }

            // Copy the built-in property metadata to the constant buffer

            UpdateConstantMetadataVariables();
        }

        void UpdateConstantMetadataVariables()
        {
            int count = m_GPUPropertyMetadatas.Length;
            int size = UnsafeUtility.SizeOf<GPUBuiltinPropertyMetadata>();

            if (m_SupportsConstantBufferAlignment)
            {
                if (m_PropertyMetadataBuffers[0] == null || m_PropertyMetadataBuffers[0].count < count)
                {
                    m_PropertyMetadataBuffers[0]?.Dispose();
                    m_PropertyMetadataBuffers[0] = new GraphicsBuffer(GraphicsBuffer.Target.Constant, count, size);
                }

                m_PropertyMetadataBuffers[0].SetDataUnsafe(m_GPUPropertyMetadatas.Ptr, count);
            }
            else
            {
                // When constant buffer alignment is not supported, we need to create a separate buffer for each batch

                if (m_PropertyMetadataBuffers.Length < count)
                    Array.Resize(ref m_PropertyMetadataBuffers, count);

                for (int i = 0; i < count; i++)
                {
                    m_PropertyMetadataBuffers[i] ??= new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, size);
                    m_PropertyMetadataBuffers[i].SetDataUnsafe(m_GPUPropertyMetadatas.Ptr + i, 1);
                }
            }
        }
    }
}
