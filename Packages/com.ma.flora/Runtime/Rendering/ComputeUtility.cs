using System.Runtime.CompilerServices;
using MA.Mathematics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace MA.Flora.Rendering
{
    static class ComputeUtility
    {
        /// <summary>The constant stride used for wrapping large 1D dispatches.</summary>
        /// <remarks>
        /// This value, set to 128, is suitable for mobile.
        /// With a group size of 64:
        ///     A maximum of 128*128*8 * 64 groups (~8.4 million groups).
        ///     A maximum of 128*128*8 * 64 * 64 threads (~536 million threads).
        /// 
        /// </remarks>
        public const int WrappedGroupStride = 128;

        /// <summary>Calculates the wrapped group count based on the target group count.</summary>
        /// <remarks>If the X group count exceeds the maximum work group size, it wraps to the Y dimension.
        /// If the Y group count also exceeds the maximum work group size, it wraps to the Z dimension.
        /// The linear group index can be calculated as follows:
        /// <code>
        /// uint linearGroupId = svGroupId.x + (svGroupId.z * WrappedGroupStride + svGroupId.y) * WrappedGroupStride;
        /// </code>
        /// Note that early exit conditions should be considered, as linearGroupId may exceed the ideal value due to wrapping.</remarks>
        /// <param name="targetGroupCount">The target group count in the X dimension.</param>
        /// <returns>An int3 representing the wrapped group count in the X, Y, and Z dimensions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 WrapGroupCount(int targetGroupCount)
        {
            int maxComputeWorkGroupSizeX = UnityEngine.SystemInfo.maxComputeWorkGroupSizeX;
            int maxComputeWorkGroupSizeY = UnityEngine.SystemInfo.maxComputeWorkGroupSizeY;
            Assert.IsTrue(maxComputeWorkGroupSizeX >= WrappedGroupStride && maxComputeWorkGroupSizeY >= WrappedGroupStride);

            int3 groupCount = new int3(targetGroupCount, 1, 1);

            if (groupCount.x > maxComputeWorkGroupSizeX)
            {
                groupCount.y = MathUtility.DivideAndRoundUp(groupCount.x, WrappedGroupStride);
                groupCount.x = WrappedGroupStride;
            }
            
            if (groupCount.y > maxComputeWorkGroupSizeY)
            {
                groupCount.z = MathUtility.DivideAndRoundUp(groupCount.y, WrappedGroupStride);
                groupCount.y = WrappedGroupStride;
            }

            Assert.IsTrue(targetGroupCount <= groupCount.x * groupCount.y * groupCount.z);

            return groupCount;
        }

        /// <summary>Calculates the wrapped group count based on the total thread count and group size.</summary>
        /// <remarks>If the X group count exceeds the maximum work group size, it wraps to the Y dimension.
        /// If the Y group count also exceeds the maximum work group size, it wraps to the Z dimension.
        /// The linear group index can be calculated as follows:
        /// <code>
        /// uint linearGroupId = svGroupId.x + (svGroupId.z * WrappedGroupStride + svGroupId.y) * WrappedGroupStride;
        /// </code>
        /// Note that early exit conditions should be considered, as linearGroupId may exceed the ideal value due to wrapping.</remarks>
        /// <param name="dispatchThreadCount">The total number of threads.</param>
        /// <param name="groupSize">The size of each thread group.</param>
        /// <returns>An int3 representing the wrapped group count in the X, Y, and Z dimensions.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 WrapDispatchCount(int dispatchThreadCount, int groupSize) 
            => WrapGroupCount(MathUtility.DivideAndRoundUp(dispatchThreadCount, groupSize));

        /// <summary>Converts a wrapped group ID to a linear group ID.</summary>
        /// <param name="wrappedGroupID">The wrapped group ID.</param>
        /// <returns>The linear group ID.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int UnwrapLinearGroupID(int3 wrappedGroupID)
            => wrappedGroupID.x + (wrappedGroupID.z * WrappedGroupStride + wrappedGroupID.y) * WrappedGroupStride;

        /// <summary>Converts a wrapped group ID to a linear dispatch thread ID.</summary>
        /// <param name="wrappedGroupID">The wrapped group ID.</param>
        /// <param name="groupThreadIndex">The index of the thread within the group.</param>
        /// <param name="threadGroupSize">The size of the thread group.</param>
        /// <returns>The linear dispatch thread ID.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int UnwrapLinearDispatchThreadID(int3 wrappedGroupID, int groupThreadIndex, int threadGroupSize) 
            => UnwrapLinearGroupID(wrappedGroupID) * threadGroupSize + groupThreadIndex;

        /// <summary>Add a command to execute a ComputeShader.</summary>
        /// <param name="cmd"></param>
        /// <param name="computeShader">ComputeShader to execute.</param>
        /// <param name="kernelIndex">Kernel index to execute, see ComputeShader.FindKernel.</param>
        /// <param name="threadGroups">Number of work groups in each dimension.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DispatchCompute(this CommandBuffer cmd, ComputeShader computeShader, int kernelIndex, int3 threadGroups) 
            => cmd.DispatchCompute(computeShader, kernelIndex, threadGroups.x, threadGroups.y, threadGroups.z);

#if UNITY_2023_3_OR_NEWER
        /// <summary>Add a command to execute a ComputeShader.</summary>
        /// <param name="cmd"></param>
        /// <param name="computeShader">ComputeShader to execute.</param>
        /// <param name="kernelIndex">Kernel index to execute, see ComputeShader.FindKernel.</param>
        /// <param name="threadGroups">Number of work groups in each dimension.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DispatchCompute(this ComputeCommandBuffer cmd, ComputeShader computeShader, int kernelIndex, int3 threadGroups) 
            => cmd.DispatchCompute(computeShader, kernelIndex, threadGroups.x, threadGroups.y, threadGroups.z);
#endif

        /// <summary>Add a command to execute a ComputeShader.</summary>
        /// <param name="cs">ComputeShader to execute.</param>
        /// <param name="kernelIndex">Kernel index to execute, see ComputeShader.FindKernel.</param>
        /// <param name="threadGroups">Number of work groups in each dimension.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Dispatch(this ComputeShader cs, int kernelIndex, int3 threadGroups) 
            => cs.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, threadGroups.z);
    }
}