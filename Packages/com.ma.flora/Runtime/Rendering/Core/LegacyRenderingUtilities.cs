// Copyright © Magnetic Arcade. All Rights Reserved.

#if !UNITY_2023_3_OR_NEWER
using System.Runtime.CompilerServices;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    static class CommandBufferHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static CommandBuffer GetComputeCommandBuffer(CommandBuffer cmd) => cmd;
    }
}
#endif