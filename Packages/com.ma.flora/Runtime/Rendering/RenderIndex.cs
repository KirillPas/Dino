// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;

namespace MA.Flora.Rendering
{
    [Flags]
    enum RenderIndexFlags
    {
        None      = 0,
        Destroyed = 0x40000000,
        Selected  = 0x20000000,
    }
    
    static class RenderIndexUtility
    {
        public const uint RenderFlagsMask = 0xF0000000;
        public const uint IndexMask = 0x0FFFFFFF;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PackRenderFlags(int index, RenderIndexFlags flags = RenderIndexFlags.None) => index | (int)flags;
            
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int UnpackIndex(int packedIndex) => (int)(packedIndex & ~RenderFlagsMask);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDestroyed(int packedIndex) => (packedIndex & (int)RenderIndexFlags.Destroyed) != 0;
            
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSelected(int packedIndex) => (packedIndex & (int)RenderIndexFlags.Selected) != 0;
    }

    struct PackedRenderIndex : IEquatable<PackedRenderIndex>
    {
        int m_Value;
        
        public int Index
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RenderIndexUtility.UnpackIndex(m_Value);
        }
        
        public bool IsDestroyed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => RenderIndexUtility.IsDestroyed(m_Value);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PackedRenderIndex(int value, RenderIndexFlags flags = RenderIndexFlags.None) => m_Value = RenderIndexUtility.PackRenderFlags(value, flags);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(PackedRenderIndex other) => m_Value == other.m_Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is PackedRenderIndex other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => m_Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(PackedRenderIndex left, PackedRenderIndex right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(PackedRenderIndex left, PackedRenderIndex right) => !left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator PackedRenderIndex(int i) => new PackedRenderIndex(i);
    }
}