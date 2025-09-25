// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MA.Flora.Rendering
{
    [Serializable]
    [DebuggerDisplay("Layer={Layer}, Motion={MotionMode}, Shadows={ShadowCastingMode}, ReceiveShadows={ReceiveShadows}, StaticShadowCaster={StaticShadowCaster}, LightProbes={LightProbeUsage}, ReflectionProbes={ReflectionProbeUsage}")]
    [StructLayout(LayoutKind.Sequential)]
    struct RenderFilterSettings : IEquatable<RenderFilterSettings>
    {
        public uint RenderingLayerMask;
        public uint Packed;

        public int Layer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (byte)((Packed >> 24) & 0xFF);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Packed = (Packed & 0x00FFFFFF) | ((uint)value << 24);
        }
        
        public uint LayerMask
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 1u << Layer;
        }
        
        public MotionVectorGenerationMode MotionMode
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (MotionVectorGenerationMode)((Packed >> 16) & 0xFF);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Packed = (Packed & 0xFF00FFFF) | ((uint)value << 16);
        }
        
        public ShadowCastingMode ShadowCastingMode
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ShadowCastingMode)((Packed >> 8) & 0xFF);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Packed = (Packed & 0xFFFF00FF) | ((uint)value << 8);
        }
        
        public bool ReceiveShadows
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ((Packed >> 7) & 0x01) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Packed = (Packed & 0xFFFFFF7F) | ((value ? 1u : 0u) << 7);
        }
        
        public bool StaticShadowCaster
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ((Packed >> 6) & 0x01) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Packed = (Packed & 0xFFFFFFBF) | ((value ? 1u : 0u) << 6);
        }
        
        public LightProbeUsage LightProbeUsage
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (LightProbeUsage)((Packed >> 5) & 0x01);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Packed = (Packed & 0xFFFFFFDF) | ((value > 0 ? 1u : 0u) << 5);
        }
        
        public ReflectionProbeUsage ReflectionProbeUsage
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ReflectionProbeUsage)((Packed >> 4) & 0x03);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Packed = (Packed & 0xFFFFFFEF) | ((uint)value << 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RenderFilterSettings(Renderer renderer)
        {
            Debug.Assert(renderer != null, "Must have a non-null Renderer to create MassCullingSettings.");
            
            // Can't use per-object motion vectors with Flora (objects are static).
            MotionVectorGenerationMode motionVectorGenerationMode = renderer.motionVectorGenerationMode;
            if (motionVectorGenerationMode < MotionVectorGenerationMode.ForceNoMotion)
                motionVectorGenerationMode = MotionVectorGenerationMode.Camera;

            RenderingLayerMask = renderer.renderingLayerMask;
            Packed = 0;
            Layer = renderer.gameObject.layer;
            MotionMode = motionVectorGenerationMode;
            ShadowCastingMode = renderer.shadowCastingMode;
            ReceiveShadows = renderer.receiveShadows;
            StaticShadowCaster = renderer.staticShadowCaster;
            ReflectionProbeUsage = renderer.reflectionProbeUsage;
            LightProbeUsage = renderer.lightProbeUsage;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(RenderFilterSettings rhs) => RenderingLayerMask == rhs.RenderingLayerMask && Packed == rhs.Packed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object o) => o is RenderFilterSettings converted && Equals(converted);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + RenderingLayerMask.GetHashCode();
                hash = hash * 23 + Packed.GetHashCode();
                return hash;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(RenderFilterSettings lhs, RenderFilterSettings rhs) => lhs.Equals(rhs);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(RenderFilterSettings lhs, RenderFilterSettings rhs) => !lhs.Equals(rhs);
    }
}
