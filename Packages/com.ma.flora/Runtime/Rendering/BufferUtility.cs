using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;

namespace MA.Flora.Rendering
{
    static class BufferUtility
    {
        public const int ThreadGroupSize = 64;

        static readonly ProfilerMarker k_MemsetMarker = new ProfilerMarker("BufferUtility.Memset");
        static readonly ProfilerMarker k_MemcpyMarker = new ProfilerMarker("BufferUtility.Memcpy");
        static readonly ProfilerMarker k_ScatterMarker = new ProfilerMarker("BufferUtility.Scatter");
        static readonly ProfilerMarker k_ResizeMarker = new ProfilerMarker("BufferUtility.Resize");
        static readonly ProfilerMarker k_ResizeSOAMarker = new ProfilerMarker("BufferUtility.ResizeSOA");

        static readonly int s_DstBufferID = Shader.PropertyToID("_DstBuffer");
        static readonly int s_SrcBufferID = Shader.PropertyToID("_SrcBuffer");
        static readonly int s_OffsetID = Shader.PropertyToID("_Offset");
        static readonly int s_ValueID = Shader.PropertyToID("_Value");
        static readonly int s_SizeID = Shader.PropertyToID("_Size");
        static readonly int s_SrcOffsetID = Shader.PropertyToID("_SrcOffset");
        static readonly int s_DstOffsetID = Shader.PropertyToID("_DstOffset");
        static readonly int s_ScatterCountID = Shader.PropertyToID("_ScatterCount");
        static readonly int s_ScatterBufferID = Shader.PropertyToID("_ScatterBuffer");
        static readonly int s_UploadBufferID = Shader.PropertyToID("_UploadBuffer");

        static ComputeShader s_BufferUtils;
        static int s_MemsetKernel;
        static int s_MemcpyKernel;
        static int s_ScatterKernel;

        delegate void GraphicsBufferSetDataDelegate(GraphicsBuffer buffer, IntPtr data, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count, int elemSize);
        static GraphicsBufferSetDataDelegate s_GraphicsBufferSetData;

        delegate void ComputeBufferSetDataDelegate(ComputeBuffer buffer, IntPtr data, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count, int elemSize);
        static ComputeBufferSetDataDelegate s_ComputeBufferSetData;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Initialize()
        {
            s_BufferUtils = Resources.Load<ComputeShader>("Compute/BufferUtility");
            Assert.IsNotNull(s_BufferUtils, "BufferUtility.compute not found");

            s_MemsetKernel = s_BufferUtils.FindKernel("MemsetCS");
            s_MemcpyKernel = s_BufferUtils.FindKernel("MemcpyCS");
            s_ScatterKernel = s_BufferUtils.FindKernel("ScatterCS");

            MethodInfo internalSetNativeDataInfo = typeof(GraphicsBuffer).GetMethod("InternalSetNativeData", BindingFlags.Instance | BindingFlags.NonPublic)!;
            s_GraphicsBufferSetData = (GraphicsBufferSetDataDelegate)Delegate.CreateDelegate(typeof(GraphicsBufferSetDataDelegate), internalSetNativeDataInfo);

            internalSetNativeDataInfo = typeof(ComputeBuffer).GetMethod("InternalSetNativeData", BindingFlags.Instance | BindingFlags.NonPublic)!;
            s_ComputeBufferSetData = (ComputeBufferSetDataDelegate)Delegate.CreateDelegate(typeof(ComputeBufferSetDataDelegate), internalSetNativeDataInfo);
        }

        enum SupportedBufferType
        {
            Unsupported,
            Float,
            Float4,
            Raw,
            Raw4
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SetTypeKeywords(SupportedBufferType type)
        {
            switch (type)
            {
                case SupportedBufferType.Float:
                    s_BufferUtils.EnableKeyword("TYPE_FLOAT");
                    s_BufferUtils.DisableKeyword("TYPE_FLOAT4");
                    s_BufferUtils.DisableKeyword("TYPE_RAW");
                    s_BufferUtils.DisableKeyword("TYPE_RAW4");
                    break;
                case SupportedBufferType.Float4:
                    s_BufferUtils.EnableKeyword("TYPE_FLOAT4");
                    s_BufferUtils.DisableKeyword("TYPE_FLOAT");
                    s_BufferUtils.DisableKeyword("TYPE_RAW");
                    s_BufferUtils.DisableKeyword("TYPE_RAW4");
                    break;
                case SupportedBufferType.Raw:
                    s_BufferUtils.EnableKeyword("TYPE_RAW");
                    s_BufferUtils.DisableKeyword("TYPE_RAW4");
                    s_BufferUtils.DisableKeyword("TYPE_FLOAT4");
                    s_BufferUtils.DisableKeyword("TYPE_FLOAT");
                    break;
                case SupportedBufferType.Raw4:
                    s_BufferUtils.EnableKeyword("TYPE_RAW4");
                    s_BufferUtils.DisableKeyword("TYPE_RAW");
                    s_BufferUtils.DisableKeyword("TYPE_FLOAT4");
                    s_BufferUtils.DisableKeyword("TYPE_FLOAT");
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetDataUnsafe(this GraphicsBuffer buffer, void* ptr, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count, int stride)
            => s_GraphicsBufferSetData(buffer, (IntPtr)ptr, nativeBufferStartIndex, graphicsBufferStartIndex, count, stride);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetDataUnsafe(this ComputeBuffer buffer, void* ptr, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count, int stride)
            => s_ComputeBufferSetData(buffer, (IntPtr)ptr, nativeBufferStartIndex, graphicsBufferStartIndex, count, stride);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetDataUnsafe(this GraphicsBuffer buffer, void* ptr, int count, int stride)
            => s_GraphicsBufferSetData(buffer, (IntPtr)ptr, 0, 0, count, stride);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetDataUnsafe(this ComputeBuffer buffer, void* ptr, int count, int stride)
            => s_ComputeBufferSetData(buffer, (IntPtr)ptr, 0, 0, count, stride);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetDataUnsafe<T>(this GraphicsBuffer buffer, T* elements, int count) where T : unmanaged
            => s_GraphicsBufferSetData(buffer, (IntPtr)elements, 0, 0, count, UnsafeUtility.SizeOf<T>());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetDataUnsafe<T>(this ComputeBuffer buffer, T* elements, int count) where T : unmanaged
            => s_ComputeBufferSetData(buffer, (IntPtr)elements, 0, 0, count, UnsafeUtility.SizeOf<T>());

        public static void Memset(GraphicsBuffer buffer, int offset, int value, int size)
        {
            if (size == 0)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
#endif
            using (k_MemsetMarker.Auto())
            {
                SupportedBufferType type = buffer.target == GraphicsBuffer.Target.Raw ? SupportedBufferType.Raw : SupportedBufferType.Float4;
                SetTypeKeywords(type);

                int elementsPerThread = buffer.target == GraphicsBuffer.Target.Raw ? 4 : 1;
                int threadCount = math.max(size / elementsPerThread, 1);

                s_BufferUtils.SetBuffer(s_MemsetKernel, s_DstBufferID, buffer);
                s_BufferUtils.SetInt(s_OffsetID, offset);
                s_BufferUtils.SetInt(s_ValueID, value);
                s_BufferUtils.SetInt(s_SizeID, size);

                int3 groupCount = ComputeUtility.WrapDispatchCount(threadCount, ThreadGroupSize);
                s_BufferUtils.Dispatch(s_MemsetKernel, groupCount);
            }
        }

        public static void Memcpy(GraphicsBuffer dstBuffer, GraphicsBuffer srcBuffer, int srcOffset, int dstOffset, int count)
        {
            if (count == 0)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (dstBuffer == null)
                throw new ArgumentNullException(nameof(dstBuffer));
            if (srcBuffer == null)
                throw new ArgumentNullException(nameof(srcBuffer));
            if (srcBuffer.target != dstBuffer.target)
                throw new ArgumentException("Source and destination buffers must have the same target.");
#endif

            using (k_MemcpyMarker.Auto())
            {
                SupportedBufferType type = dstBuffer.target == GraphicsBuffer.Target.Raw
                    ? SupportedBufferType.Raw
                    : SupportedBufferType.Float4;
                SetTypeKeywords(type);

                int elementsPerThread = dstBuffer.target == GraphicsBuffer.Target.Raw ? 4 : 1;
                int threadCount = math.max(count / elementsPerThread, 1);

                s_BufferUtils.SetBuffer(s_MemcpyKernel, s_SrcBufferID, srcBuffer);
                s_BufferUtils.SetBuffer(s_MemcpyKernel, s_DstBufferID, dstBuffer);
                s_BufferUtils.SetInt(s_SrcOffsetID, srcOffset);
                s_BufferUtils.SetInt(s_DstOffsetID, dstOffset);
                s_BufferUtils.SetInt(s_SizeID, count);

                int3 groupCount = ComputeUtility.WrapDispatchCount(threadCount, ThreadGroupSize);
                s_BufferUtils.Dispatch(s_MemcpyKernel, groupCount);
            }
        }

        public static void Scatter(GraphicsBuffer dstBuffer, ComputeBuffer scatterBuffer, ComputeBuffer uploadBuffer, int bytesPerElement, int scatterCount, int elementsPerScatter = -1)
        {
            if (scatterCount == 0)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (dstBuffer == null)
                throw new ArgumentNullException(nameof(dstBuffer));
            if (scatterBuffer == null)
                throw new ArgumentNullException(nameof(scatterBuffer));
            if (uploadBuffer == null)
                throw new ArgumentNullException(nameof(uploadBuffer));
#endif

            using (k_ScatterMarker.Auto())
            {
                int bytesPerThread;
                if (elementsPerScatter == -1)
                {
                    bytesPerThread = (bytesPerElement & 15) == 0 ? 16 : 4;
                    elementsPerScatter = math.max(1, bytesPerElement / bytesPerThread);
                }
                else
                {
                    bytesPerThread = bytesPerElement;
                }

                SupportedBufferType type = dstBuffer.target == GraphicsBuffer.Target.Raw
                    ? bytesPerThread == 4 ? SupportedBufferType.Raw : SupportedBufferType.Raw4
                    : bytesPerThread == 4 ? SupportedBufferType.Float : SupportedBufferType.Float4;
                SetTypeKeywords(type);

                s_BufferUtils.SetBuffer(s_ScatterKernel, s_DstBufferID, dstBuffer);
                s_BufferUtils.SetBuffer(s_ScatterKernel, s_ScatterBufferID, scatterBuffer);
                s_BufferUtils.SetBuffer(s_ScatterKernel, s_UploadBufferID, uploadBuffer);
                s_BufferUtils.SetInt(s_ScatterCountID, scatterCount);
                s_BufferUtils.SetInt(s_SizeID, elementsPerScatter);
                s_BufferUtils.SetInt(s_SrcOffsetID, 0);
                s_BufferUtils.SetInt(s_DstOffsetID, 0);

                int threadCount = scatterCount * elementsPerScatter;
                int3 groupCount = ComputeUtility.WrapDispatchCount(threadCount, ThreadGroupSize);
                s_BufferUtils.Dispatch(s_ScatterKernel, groupCount);
            }
        }

        public static bool ResizeIfNeeded(ref GraphicsBuffer buffer, int strideInBytes, int sizeInBytes, GraphicsBuffer.Target target, string debugName)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (strideInBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(strideInBytes), "Stride must be greater than 0.");
            if (sizeInBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "Size must be greater than 0.");
            if (sizeInBytes % strideInBytes != 0)
                throw new ArgumentException("Size must be a multiple of stride.", nameof(sizeInBytes));
            if (buffer != null && buffer.count * buffer.stride % strideInBytes != 0)
                throw new ArgumentException("Buffer size must be a multiple of stride.", nameof(buffer));
#endif

            using (k_ResizeMarker.Auto())
            {
                int newCount = sizeInBytes / strideInBytes;
                int oldCount = buffer != null ? (buffer.count * buffer.stride) / strideInBytes : 0;

                if (buffer == null)
                {
                    buffer = new GraphicsBuffer(target, newCount, strideInBytes);
                    buffer.name = debugName;
                    return true;
                }
                else if (newCount != oldCount)
                {
                    GraphicsBuffer newBuffer = new GraphicsBuffer(target, newCount, strideInBytes);
                    newBuffer.name = debugName;

                    Memcpy(newBuffer, buffer, 0, 0, math.min(newCount, oldCount));

                    buffer.Dispose();
                    buffer = newBuffer;
                    return true;
                }
            }

            return false;
        }

        public static bool ResizeSOAIfNeeded(ref GraphicsBuffer buffer, int strideInBytes, int sizeInBytes, int arrayCount, GraphicsBuffer.Target target, string debugName)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (strideInBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(strideInBytes), "Stride must be greater than 0.");
            if (sizeInBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "Size must be greater than 0.");
            if (sizeInBytes % strideInBytes != 0)
                throw new ArgumentException("Size must be a multiple of stride.", nameof(sizeInBytes));
            if (buffer != null && buffer.count * buffer.stride % strideInBytes != 0)
                throw new ArgumentException("Buffer size must be a multiple of stride.", nameof(buffer));
#endif

            using (k_ResizeSOAMarker.Auto())
            {
                int newCount = sizeInBytes / strideInBytes;
                int oldCount = buffer != null ? (buffer.count * buffer.stride) / strideInBytes : 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (newCount % arrayCount != 0)
                    throw new ArgumentException("Size must be a multiple of arrayCount.", nameof(sizeInBytes));
                if (oldCount % arrayCount != 0)
                    throw new ArgumentException("Buffer size must be a multiple of arrayCount.", nameof(buffer));
#endif

                if (buffer == null)
                {
                    buffer = new GraphicsBuffer(target, newCount, strideInBytes);
                    buffer.name = debugName;
                    return true;
                }
                else if (oldCount != newCount)
                {
                    GraphicsBuffer newBuffer = new GraphicsBuffer(target, newCount, strideInBytes);
                    newBuffer.name = debugName;

                    int oldArrayCount = oldCount / arrayCount;
                    int newArrayCount = newCount / arrayCount;
                    int copyCount = math.min(oldArrayCount, newArrayCount);

                    for (int i = 0; i < arrayCount; i++)
                    {
                        Memcpy(newBuffer, buffer, i * oldArrayCount, i * newArrayCount, copyCount);
                    }

                    buffer.Dispose();
                    buffer = newBuffer;
                    return true;
                }
            }

            return false;
        }
    }
}
