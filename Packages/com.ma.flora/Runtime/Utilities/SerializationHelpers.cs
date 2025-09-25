// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
#endif

namespace MA.Flora
{
    [StructLayout(LayoutKind.Sequential)]
    struct CompressedLocalTransform
    {
        public float3 Position;
        public half4 Rotation;
        public half3 Scale;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator CompressedLocalTransform(LocalTransform transform)
        {
            return new CompressedLocalTransform
            {
                Position = transform.Position,
                Rotation = new half4(transform.Rotation.value),
                Scale = new half3(transform.Scale),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator LocalTransform(CompressedLocalTransform transform)
        {
            return new LocalTransform
            {
                Position = transform.Position,
                Rotation = new quaternion(new float4(transform.Rotation)),
                Scale = transform.Scale,
            };
        }
    }

    [BurstCompile]
    static class SerializationHelpers
    {
        internal static unsafe void SerializeListToBytes<T>(List<T> list, ref byte[] bytes) where T : unmanaged
        {
            if (list.Count > 0)
            {
                int byteSize = list.Count * UnsafeUtility.SizeOf<T>();
                if (bytes == null || bytes.Length != byteSize)
                    bytes = new byte[byteSize];

                fixed (T* src = list.GetInternalArray())
                fixed (byte* dst = bytes)
                {
                    UnsafeUtility.MemCpy(dst, src, byteSize);
                }
            }
            else
            {
                bytes = Array.Empty<byte>();
            }
        }

        internal static unsafe void DeserializeBytesToList<T>(ref byte[] bytes, List<T> list) where T : unmanaged
        {
            if (bytes is { Length: > 0 })
            {
                int byteSize = bytes.Length;
                int count = byteSize / UnsafeUtility.SizeOf<T>();
                if (count == 0)
                {
                    Debug.LogError($"Deserialized byte size {byteSize} is less than sizeof({typeof(T).Name})");
                    list.Clear();
                    return;
                }

                list.Resize(count);

                fixed (byte* src = bytes)
                fixed (T* dst = list.GetInternalArray())
                {
                    UnsafeUtility.MemCpy(dst, src, byteSize);
                }
            }
            else
            {
                list.Clear();
            }

            bytes = Array.Empty<byte>();
        }

        internal static unsafe void SerializeArrayToBytes<T>(in NativeArray<T> array, ref byte[] bytes) where T : unmanaged
        {
            if (array.Length > 0)
            {
                int byteSize = array.Length * UnsafeUtility.SizeOf<T>();
                if (bytes == null || bytes.Length != byteSize)
                    bytes = new byte[byteSize];

                fixed (byte* dst = bytes)
                {
                    UnsafeUtility.MemCpy(dst, array.GetUnsafeReadOnlyPtr(), byteSize);
                }
            }
            else
            {
                bytes = Array.Empty<byte>();
            }
        }

        internal static unsafe void DeserializeBytesToArray<T>(ref byte[] bytes, ref NativeArray<T> array, Allocator allocator) where T : unmanaged
        {
            if (bytes is { Length: > 0 })
            {
                int byteSize = bytes.Length;
                int count = byteSize / UnsafeUtility.SizeOf<T>();
                if (count == 0)
                {
                    Debug.LogError($"Deserialized byte size {byteSize} is less than sizeof({typeof(T).Name})");
                    array.Dispose();
                    return;
                }

                array.Resize(count, allocator);

                fixed (byte* src = bytes)
                {
                    UnsafeUtility.MemCpy(array.GetUnsafePtr(), src, byteSize);
                }
            }
            else
            {
                array.Dispose();
            }

            bytes = Array.Empty<byte>();
        }

        internal static unsafe int SerializeTransformsToByteArray(List<LocalTransform> list, ref byte[] compressedBytes)
        {
            if (list.Count > 0)
            {
                int length = list.Count;
                int compressedSize = list.Count * UnsafeUtility.SizeOf<CompressedLocalTransform>();
                if (compressedBytes == null || compressedBytes.Length != compressedSize)
                    compressedBytes = new byte[compressedSize];

                fixed (LocalTransform* src = list.GetInternalArray())
                fixed (byte* dst = compressedBytes)
                {
                    new WriteCompressedTransformsJob
                    {
                        Src = src,
                        Dst = (CompressedLocalTransform*)dst,
                        Length = length,
                    }.Execute();
                }

                return length;
            }
            else
            {
                compressedBytes = Array.Empty<byte>();
                return 0;
            }
        }

        [BurstCompile]
        unsafe struct WriteCompressedTransformsJob : IJob
        {
            [ReadOnly, NoAlias, NativeDisableUnsafePtrRestriction]
            public LocalTransform* Src;

            [NoAlias, NativeDisableUnsafePtrRestriction]
            public CompressedLocalTransform* Dst;

            public int Length;

            public void Execute()
            {
                for (int i = 0; i < Length; i++)
                    Dst[i] = Src[i];
            }
        }

        internal static unsafe void DeserializeByteArrayToTransforms(ref byte[] compressedBytes, int serializedCount, List<LocalTransform> list)
        {
            if (serializedCount > 0 && compressedBytes is { Length: > 0 })
            {
                int compressedCount = compressedBytes.Length / UnsafeUtility.SizeOf<CompressedLocalTransform>();
                if (compressedCount != serializedCount)
                {
                    Debug.LogError($"Serialized count {serializedCount} does not match compressed count {compressedCount}");
                    list.Clear();
                    return;
                }

                list.Resize(serializedCount);

                fixed (byte* src = compressedBytes)
                fixed (LocalTransform* dst = list.GetInternalArray())
                {
                    new ReadCompressedTransformsJob
                    {
                        Src = (CompressedLocalTransform*)src,
                        Dst = dst,
                        Length = serializedCount,
                    }.Execute();
                }
            }
            else
            {
                list.Clear();
            }

            compressedBytes = Array.Empty<byte>();
        }

        [BurstCompile]
        unsafe struct ReadCompressedTransformsJob : IJob
        {
            [ReadOnly, NoAlias, NativeDisableUnsafePtrRestriction]
            public CompressedLocalTransform* Src;

            [NoAlias, NativeDisableUnsafePtrRestriction]
            public LocalTransform* Dst;

            public int Length;

            public void Execute()
            {
                for (int i = 0; i < Length; i++)
                    Dst[i] = Src[i];
            }
        }
    }
}
