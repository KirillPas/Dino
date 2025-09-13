// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;

namespace MA.Flora
{
    /// <summary>
    /// A global identifier assigned to instances at runtime representing instances within the scene.
    /// </summary>
    /// <remarks>
    /// This ID is used for runtime instance management and is not serializable.
    /// </remarks>
    public struct InstancedGlobalID : IEquatable<InstancedGlobalID>, IComparable<InstancedGlobalID>
    {
        /// <summary>A null ID.</summary>
        public static readonly InstancedGlobalID Null = new InstancedGlobalID(value: 0);

        /// <summary>The value of the ID.</summary>
        public int Value;

        /// <summary>Whether the ID is created.</summary>
        public bool IsCreated { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Value != 0; }

        /// <summary>Initializes a new instance of the <see cref="InstancedGlobalID"/> struct.</summary>
        /// <param name="value">The value of the ID.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedGlobalID(int value) => Value = value;

        /// <summary>Returns the hash code for this instance.</summary>
        /// <returns>The hash code.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Value;

        /// <summary>Indicates whether this instance and a specified object are equal.</summary>
        /// <param name="obj">The object to compare with this instance.</param>
        /// <returns>True if the object and this instance are equal; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedGlobalID other && Equals(other);

        /// <summary>Indicates whether this instance and another instance are equal.</summary>
        /// <param name="other">The instance to compare with this instance.</param>
        /// <returns>True if the instances are equal; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedGlobalID other) => Value == other.Value;

        /// <summary>
        /// Compares this instance to a specified instance and returns an indication of their relative values.
        /// </summary>
        /// <param name="other">The instance to compare with this instance.</param>
        /// <returns>A signed number indicating the relative values of this instance and the other instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedGlobalID other) => Value.CompareTo(other.Value);

        /// <summary>Implicitly converts an instance ID to an integer.</summary>
        /// <param name="id">The instance ID to convert.</param>
        /// <returns>The integer value of the instance ID.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(InstancedGlobalID id) => id.Value;

        /// <summary>Tests whether two specified instance IDs are equal.</summary>
        /// <param name="a">The first instance ID to compare.</param>
        /// <param name="b">The second instance ID to compare.</param>
        /// <returns>True if the instance IDs are equal; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedGlobalID a, InstancedGlobalID b) => a.Equals(b);

        /// <summary>Tests whether two specified instance IDs are not equal.</summary>
        /// <param name="a">The first instance ID to compare.</param>
        /// <param name="b">The second instance ID to compare.</param>
        /// <returns>True if the instance IDs are not equal; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedGlobalID a, InstancedGlobalID b) => !a.Equals(b);

        /// <summary>Returns a string that represents the current instance.</summary>
        /// <returns>A string that represents the current instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => IsCreated ? $"InstancedGlobalID({Value})" : "InstancedGlobalID.Null";
    }
}
