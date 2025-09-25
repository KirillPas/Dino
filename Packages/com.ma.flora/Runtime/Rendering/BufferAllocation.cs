// // Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;

namespace MA.Flora.Rendering
{
    public struct BufferAllocation : IEquatable<BufferAllocation>, IComparable<BufferAllocation>
    {
        public static readonly BufferAllocation Null = default;

        public int Offset;
        public int Length;

        public int End
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Offset + Length;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Length > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Offset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(BufferAllocation other) => Offset - other.Offset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(BufferAllocation other) => Offset == other.Offset && Length == other.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is BufferAllocation converted && Equals(converted);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(BufferAllocation lhs, BufferAllocation rhs) => lhs.Equals(rhs);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(BufferAllocation lhs, BufferAllocation rhs) => !lhs.Equals(rhs);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => Equals(Null) ? "BufferAllocation.Null" : $"BufferAllocation(({Offset}-{Length}))";
    }
}
