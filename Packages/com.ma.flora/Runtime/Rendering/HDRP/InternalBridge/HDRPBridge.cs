// Copyright © Magnetic Arcade. All Rights Reserved.

#if HAS_PACKAGE_UNITY_HDRP_12_0_0
using System.Runtime.CompilerServices;
using UnityEngine.Rendering.HighDefinition;

namespace MA.Flora.Rendering.HDRP.InternalBridge
{
    static class HDRPBridge
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsSinglePassXREnabled(this HDCamera hdCamera)
        {
            return hdCamera.xr.enabled && hdCamera.xr.singlePassEnabled;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static HDCamera.ViewConstants[] GetXRViewConstants(this HDCamera hdCamera)
        {
            return hdCamera.m_XRViewConstants;
        }
    }
}
#endif