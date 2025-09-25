// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Core;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    [Flags, Obsolete]
    public enum FloraInstanceMetadataFlags : byte
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None            = 0,
        /// <summary>
        /// Was aligned to normal.
        /// </summary>
        AlignToNormal   = 1 << 0,
        /// <summary>
        /// Don't randomize yaw.
        /// </summary>
        NoRandomYaw     = 1 << 1,
        /// <summary>
        /// This instance has been re-adjusted.
        /// </summary>
        Readjusted      = 1 << 2,
        /// <summary>
        /// This instance has been deleted.
        /// </summary>
        InstanceDeleted = 1 << 3,
    }

    [Serializable, Obsolete]
    [StructLayout(LayoutKind.Sequential)]
    public struct FloraInstance
    {
        public float3 Position;
        public EulerRotation Rotation;
        public float3 Scale;
        public EulerRotation PreAlignRotation;
        public float VerticalOffset;
        public FloraInstanceMetadataFlags Flags;
        public FloraParentID ParentId;
        public SerializableGuid ProceduralGUID;
        [NonSerialized] public int ParentGameObjectInstanceId; // Only valid during creation.

        public LocalTransform LocalTransform
        {
            get => new LocalTransform(Position, Rotation.ToQuaternion(), Scale);
            set =>  throw new Exception("FloraInstance is obsolete.");
        }

        public FloraInstance(float3 position, int parentGameObjectInstanceId) => throw new Exception("FloraInstance is obsolete.");
        public FloraInstance(float4x4 matrix, int parentGameObjectInstanceId) => throw new Exception("FloraInstance is obsolete.");
        public FloraInstance(LocalTransform transform, int parentGameObjectInstanceId) => throw new Exception("FloraInstance is obsolete.");
        
        public bool HasFlag(FloraInstanceMetadataFlags flag) => throw new Exception("FloraInstance is obsolete.");
        public void SetFlag(FloraInstanceMetadataFlags flag) => throw new Exception("FloraInstance is obsolete.");
        public void ClearFlag(FloraInstanceMetadataFlags flag) => throw new Exception("FloraInstance is obsolete.");

        public void AlignToNormal(float3 normal, float maxPitchAngle = 0.0f) => throw new Exception("FloraInstance is obsolete.");
    }

    [Obsolete]
    [StructLayout(LayoutKind.Sequential)]
    public struct FloraInstanceIndex
    {
        public int Index;
        public FloraInstance Instance;

        public FloraInstanceIndex(int index, FloraInstance instance)
        {
            Index = index;
            Instance = instance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator FloraInstanceIndex((int, FloraInstance) from) => new FloraInstanceIndex(from.Item1, from.Item2);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator (int, FloraInstance)(FloraInstanceIndex from) => (from.Index, from.Instance);
    }

    [Obsolete]
    public readonly struct FloraGlobalInstanceID : IEquatable<FloraGlobalInstanceID>, IComparable<FloraGlobalInstanceID>
    {
        public static FloraGlobalInstanceID Invalid => new FloraGlobalInstanceID(0, -1);
        
        public readonly int ControllerId;
        public readonly int InstanceIndex;
        
        public FloraGlobalInstanceID(int controllerId, int instanceIndex)
        {
            ControllerId = controllerId;
            InstanceIndex = instanceIndex;
        }
        
        public FloraGlobalInstanceID(FloraInstanceController controller, int instanceIndex) 
            : this(controller.GetInstanceID(), instanceIndex) { }

        public FloraInstanceController Controller => (FloraInstanceController)Resources.InstanceIDToObject(ControllerId);
        public ref FloraInstance Instance => ref Controller.Instances[InstanceIndex];

        public int CompareTo(FloraGlobalInstanceID other) => ControllerId.CompareTo(other.ControllerId) + InstanceIndex.CompareTo(other.InstanceIndex);
        public bool Equals(FloraGlobalInstanceID other) => ControllerId == other.ControllerId && InstanceIndex == other.InstanceIndex;

        public override bool Equals(object obj) => obj is FloraGlobalInstanceID other && Equals(other);
        public override int GetHashCode() => new int2(ControllerId, InstanceIndex).GetHashCode();
        
        public static bool operator ==(FloraGlobalInstanceID left, FloraGlobalInstanceID right) => left.Equals(right);
        public static bool operator !=(FloraGlobalInstanceID left, FloraGlobalInstanceID right) => !left.Equals(right);
    }
}
