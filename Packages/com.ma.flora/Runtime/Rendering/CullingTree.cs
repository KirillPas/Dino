// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace MA.Flora.Rendering
{
    [Serializable]
    public struct CullingTreeSettings : IEquatable<CullingTreeSettings>
    {
        public static readonly CullingTreeSettings Default = new CullingTreeSettings
        {
            BranchingFactor = 16,
            MinVerticesPerCluster = 8192,
            MinOcclusionQueries = 6,
            MaxOcclusionQueries = 16,
            MinInstancesPerOcclusionQuery = 256
        };

        // --- Tree Structure ---

        /// <summary>Determines the internal branching factor of the tree.</summary>
        [Min(2)] public int BranchingFactor;
        /// <summary>Determines the maximum number of instances per leaf node in the tree.</summary>
        [Min(32)] public int MinVerticesPerCluster;

        // --- Occlusion Culling ---

        /// <summary>Determines the minimum number of occlusion queries used for culling.</summary>
        [Min(2)] public int MinOcclusionQueries;
        /// <summary>Determines the maximum number of occlusion queries used for culling.</summary>
        [Min(16)] public int MaxOcclusionQueries;
        /// <summary>Determines the minimum number of instances per occlusion query.</summary>
        [Min(128)] public int MinInstancesPerOcclusionQuery;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Sanitize()
        {
            BranchingFactor = math.max(BranchingFactor, 2);
            MinVerticesPerCluster = math.max(MinVerticesPerCluster, 32);
            MinOcclusionQueries = math.max(MinOcclusionQueries, 2);
            MaxOcclusionQueries = math.max(MaxOcclusionQueries, 16);
            MinInstancesPerOcclusionQuery = math.max(MinInstancesPerOcclusionQuery, 128);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int CalculateMaxInstancesPerLeafNode(InstancedPrototype prototype)
        {
            int lod0VertexCount = prototype.LODVertexCounts.Length > 0 ? prototype.LODVertexCounts[0] : 0;
            return lod0VertexCount > 0 ? math.clamp(MinVerticesPerCluster / lod0VertexCount, 1, 1024) : 16;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(CullingTreeSettings other)
        {
            return BranchingFactor == other.BranchingFactor &&
                   MinVerticesPerCluster == other.MinVerticesPerCluster &&
                   MinOcclusionQueries == other.MinOcclusionQueries &&
                   MaxOcclusionQueries == other.MaxOcclusionQueries &&
                   MinInstancesPerOcclusionQuery == other.MinInstancesPerOcclusionQuery;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object o) => o is CullingTreeSettings converted && Equals(converted);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = BranchingFactor.GetHashCode();
                hashCode = (hashCode * 397) ^ MinVerticesPerCluster.GetHashCode();
                hashCode = (hashCode * 397) ^ MinOcclusionQueries.GetHashCode();
                hashCode = (hashCode * 397) ^ MaxOcclusionQueries.GetHashCode();
                hashCode = (hashCode * 397) ^ MinInstancesPerOcclusionQuery.GetHashCode();
                return hashCode;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(CullingTreeSettings lhs, CullingTreeSettings rhs) => lhs.Equals(rhs);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(CullingTreeSettings lhs, CullingTreeSettings rhs) => !lhs.Equals(rhs);
    }

    [DebuggerDisplay("{IsLeaf?\"Leaf\":\"Cluster\"} [{FirstInstance}, {LastInstance}]")]
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    struct CullingNode
    {
        public static readonly CullingNode Empty = new CullingNode
        {
            Bounds = AxisAlignedBox.Empty,
            FirstChild = -1,
            LastChild = -1,
            FirstInstance = -1,
            LastInstance = -1,
        };

        public AxisAlignedBox Bounds;
        public int FirstChild;
        public int LastChild;
        public int FirstInstance;
        public int LastInstance;

        public int ChildCount { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (LastChild - FirstChild + 1); }
        public bool IsLeaf { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => FirstChild < 0; }
        public int InstanceCount { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (LastInstance - FirstInstance + 1); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CullingNode TransformBy(in float4x4 transform)
        {
            CullingNode transformed = this;
            transformed.Bounds = Bounds.TransformBy(transform);
            return transformed;
        }
    }

    struct CullingTreeBuildResult : IDisposable
    {
        public float Density;
        public NativeList<CullingNode> Nodes;
        public NativeList<int> SortedIndices;
        public NativeList<int> RemappedInstances;
        public NativeReference<int> OcclusionLayerCount;
        public NativeReference<float> AverageInstanceScale;
        public bool IsCreated => Nodes.IsCreated;

        public CullingTreeBuildResult(int instanceCount, float density, AllocatorManager.AllocatorHandle allocator)
        {
            Nodes = new NativeList<CullingNode>(64, allocator);
            SortedIndices = new NativeList<int>(instanceCount, allocator);
            RemappedInstances = new NativeList<int>(instanceCount, allocator);
            OcclusionLayerCount = new NativeReference<int>(allocator);
            AverageInstanceScale = new NativeReference<float>(allocator);
            Density = density;
        }

        public void Dispose()
        {
            if (Nodes.IsCreated)
            {
                Nodes.Dispose();
                SortedIndices.Dispose();
                RemappedInstances.Dispose();
                OcclusionLayerCount.Dispose();
                AverageInstanceScale.Dispose();
            }
        }
    }

    [BurstCompile]
    unsafe struct CullingTreeBuildJob : IJob
    {
        [ReadOnly] public NativeArray<LocalTransform> InstanceTransforms;
        public AxisAlignedBox InstanceBounds;

        public uint DensityRandomSeed;

        public int SplitFactor;
        public int MaxInstancesPerLeaf;

        public int MinOcclusionQueries;
        public int MaxOcclusionQueries;
        public int MinInstancesPerOcclusionQuery;

        public CullingTreeBuildResult Result;

        struct BuildNode
        {
            public static readonly BuildNode Empty = new BuildNode
            {
                Bounds = AxisAlignedBox.Empty,
                FirstChild = -1,
                LastChild = -1,
                FirstInstance = -1,
                LastInstance = -1,
                MinInstanceScale = float.MaxValue,
                MaxInstanceScale = float.MinValue,
            };

            public AxisAlignedBox Bounds;
            public int FirstChild;
            public int LastChild;
            public int FirstInstance;
            public int LastInstance;
            public float3 MinInstanceScale;
            public float3 MaxInstanceScale;

            public int ChildCount { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (LastChild - FirstChild + 1); }
            public bool IsLeaf { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => FirstChild < 0; }
            public int InstanceCount { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (LastInstance - FirstInstance + 1); }
            public float3 AverageInstanceScale { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => MinInstanceScale + (MaxInstanceScale - MinInstanceScale) * 0.5f; }

            public static implicit operator CullingNode(BuildNode node) => new CullingNode
            {
                Bounds = node.Bounds,
                FirstChild = node.FirstChild,
                LastChild = node.LastChild,
                FirstInstance = node.FirstInstance,
                LastInstance = node.LastInstance
            };
        }

        readonly struct IndexRange : IComparable<IndexRange>
        {
            public readonly int Start;
            public readonly int Count;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IndexRange(int start, int count)
            {
                Start = start;
                Count = count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int CompareTo(IndexRange other) => Start.CompareTo(other.Start);
        }

        struct KeyAndIndex : IComparable<KeyAndIndex>
        {
            public float Value;
            public int Index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int CompareTo(KeyAndIndex other) => Value.CompareTo(other.Value);
        }

        struct BuildContext
        {
            public int RootCount;
            public int InstanceCount;
            public int BranchingFactor;
            public int InternalBranchingFactor;
            public int MaxInstancesPerLeaf;
            public int OcclusionLayerCount;

            public UnsafeList<IndexRange> Groups;
            public UnsafeList<float3> Points;
            public UnsafeList<int> Indices;
            public UnsafeList<KeyAndIndex> ScratchKeys;

            public UnsafeList<int> SortedIndices;
            public UnsafeList<int> NodesPerLevel;
            public UnsafeList<BuildNode> CurrentNodes;
            public UnsafeList<BuildNode> PreviousNodes;

            public UnsafeList<int> RemappedSortIndex;
            public UnsafeList<int> PreviousSortedIndices;
            public UnsafeList<int> InverseInstanceIndex;
            public UnsafeList<int> InverseChildIndex;
            public UnsafeList<int> LevelStarts;
        }

        BuildContext m_Context;

        public void Execute()
        {
            InitializeContext();

            if (m_Context.InstanceCount == 0)
            {
                // Happens if all instances are excluded due to density scale
                Result.RemappedInstances.Fill(-1);
                return;
            }

            // Start by constructing the leaf nodes

            bool isOcclusionLayer = false;
            m_Context.BranchingFactor = MaxInstancesPerLeaf;
            if (m_Context is { BranchingFactor: > 2, OcclusionLayerCount: > 0 } &&
                m_Context.InstanceCount / m_Context.BranchingFactor <= m_Context.OcclusionLayerCount)
            {
                m_Context.BranchingFactor = math.max(2, (m_Context.InstanceCount + m_Context.OcclusionLayerCount - 1) / m_Context.OcclusionLayerCount);
                m_Context.OcclusionLayerCount = 0;
                isOcclusionLayer = true;
            }

            SplitNodes(m_Context.InstanceCount);
            m_Context.RootCount = m_Context.Groups.Length;

            if (isOcclusionLayer)
            {
                Result.OcclusionLayerCount.Value = m_Context.Groups.Length;
                isOcclusionLayer = false;
            }

            // Create a list to hold sorted indices
            m_Context.SortedIndices = new UnsafeList<int>(m_Context.Indices.Length, AllocatorManager.Temp);
            m_Context.SortedIndices.AddRange(m_Context.Indices);

            // Build the initial set of nodes
            m_Context.NodesPerLevel = new UnsafeList<int>(m_Context.RootCount, AllocatorManager.Temp);
            m_Context.NodesPerLevel.Add(m_Context.RootCount);

            m_Context.PreviousNodes = new UnsafeList<BuildNode>(m_Context.RootCount, AllocatorManager.Temp);
            m_Context.CurrentNodes = new UnsafeList<BuildNode>(m_Context.RootCount, AllocatorManager.Temp);
            m_Context.CurrentNodes.Initialize(BuildNode.Empty, m_Context.RootCount);

            // Calculate bounding boxes for each root group of instances
            for (int nodeIndex = 0; nodeIndex < m_Context.RootCount; nodeIndex++)
            {
                ref BuildNode node = ref m_Context.CurrentNodes.Ptr[nodeIndex];
                node.FirstInstance = m_Context.Groups[nodeIndex].Start;
                node.LastInstance = m_Context.Groups[nodeIndex].Start + m_Context.Groups[nodeIndex].Count - 1;
                node.Bounds = AxisAlignedBox.Empty;

                for (int instanceIndex = node.FirstInstance; instanceIndex <= node.LastInstance; instanceIndex++)
                {
                    LocalTransform instanceTransform = InstanceTransforms[m_Context.SortedIndices[instanceIndex]];
                    AxisAlignedBox instanceBounds = InstanceBounds.TransformBy(instanceTransform);
                    node.Bounds += instanceBounds;
                    node.MinInstanceScale = math.min(node.MinInstanceScale, instanceTransform.Scale);
                    node.MaxInstanceScale = math.max(node.MaxInstanceScale, instanceTransform.Scale);
                }
            }

            m_Context.RemappedSortIndex = new UnsafeList<int>(m_Context.SortedIndices.Length, AllocatorManager.Temp);
            m_Context.PreviousSortedIndices = new UnsafeList<int>(m_Context.SortedIndices.Length, AllocatorManager.Temp);
            m_Context.InverseInstanceIndex = new UnsafeList<int>(m_Context.SortedIndices.Length, AllocatorManager.Temp);
            m_Context.InverseChildIndex = new UnsafeList<int>(m_Context.SortedIndices.Length, AllocatorManager.Temp);
            m_Context.LevelStarts = new UnsafeList<int>(m_Context.RootCount, AllocatorManager.Temp);

            // Construct the BVH tree until reaching the root node from the bottom up
            while (m_Context.RootCount > 1)
            {
                // Prepare for the new split
                m_Context.Indices.Resize(m_Context.RootCount);
                m_Context.Points.Resize(m_Context.RootCount);

                // Store the center of each node for partitioning purposes
                for (int index = 0; index < m_Context.RootCount; index++)
                {
                    ref readonly BuildNode node = ref m_Context.CurrentNodes.Ptr[index];
                    m_Context.Indices[index] = index;
                    m_Context.Points[index] = node.Bounds.Center;
                }

                // Adjust branching for internal nodes if necessary
                m_Context.BranchingFactor = m_Context.InternalBranchingFactor;
                if (m_Context is { BranchingFactor: > 2, OcclusionLayerCount: > 0 } &&
                    m_Context.RootCount / m_Context.BranchingFactor <= m_Context.OcclusionLayerCount)
                {
                    m_Context.BranchingFactor = math.max(2, (m_Context.RootCount + m_Context.OcclusionLayerCount - 1) / m_Context.OcclusionLayerCount);
                    m_Context.OcclusionLayerCount = 0;
                    isOcclusionLayer = true;
                }

                // Partition the nodes for the current level
                SplitNodes(m_Context.RootCount);

                if (isOcclusionLayer)
                {
                    Result.OcclusionLayerCount.Value = m_Context.Groups.Length;
                    isOcclusionLayer = false;
                }

                // Reorder the instances to match the new partitions
                RemapInstanceIndices();

                // Handle reordering of nodes based on the new partitions
                UpdateTreeStructure();

                // Swap the node lists
                SwapPrevious();

                // Update the bounds of the nodes
                UpdateNodeBounds();

                // Update the level node count
                m_Context.RootCount = m_Context.Groups.Length;
                m_Context.NodesPerLevel.Insert(0, m_Context.RootCount);
            }

            Result.SortedIndices.CopyFrom(m_Context.SortedIndices.AsReadOnlySpan());

            Result.Nodes.Resize(m_Context.CurrentNodes.Length, NativeArrayOptions.UninitializedMemory);
            for (int index = 0; index < m_Context.CurrentNodes.Length; index++)
                Result.Nodes[index] = m_Context.CurrentNodes[index];

            if (m_Context.CurrentNodes.Length > 0)
                Result.AverageInstanceScale.Value = math.cmax(math.abs(m_Context.CurrentNodes[0].AverageInstanceScale));
            else
                Result.AverageInstanceScale.Value = 1.0f;

            // NOTE: this is always initialized to the original instance count
            // Instances outside the density modified range will be set to -1
            Result.RemappedInstances.ResizeUninitialized(InstanceTransforms.Length);
            Result.RemappedInstances.Fill(-1);

            // Save inverse map
            for (int index = 0; index < m_Context.InstanceCount; index++)
            {
                Result.RemappedInstances[m_Context.SortedIndices[index]] = index;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void RemapInstanceIndices()
        {
            m_Context.RemappedSortIndex.Resize(m_Context.InstanceCount);
            int linearIndex = 0;
            for (int index = 0; index < m_Context.RootCount; index++)
            {
                ref readonly BuildNode node = ref m_Context.CurrentNodes.Ptr[m_Context.Indices[index]];
                for (int instanceIndex = node.FirstInstance; instanceIndex <= node.LastInstance; instanceIndex++)
                    m_Context.RemappedSortIndex[linearIndex++] = instanceIndex;
            }

            m_Context.InverseInstanceIndex.Resize(m_Context.InstanceCount);
            for (int index = 0; index < m_Context.InstanceCount; index++)
                m_Context.InverseInstanceIndex[m_Context.RemappedSortIndex[index]] = index;

            // Update the current nodes to reflect the new ordering of instances
            for (int index = 0; index < m_Context.CurrentNodes.Length; index++)
            {
                ref BuildNode node = ref m_Context.CurrentNodes.Ptr[index];
                node.FirstInstance = m_Context.InverseInstanceIndex[node.FirstInstance];
                node.LastInstance = m_Context.InverseInstanceIndex[node.LastInstance];
            }

            // Swap old and new instance indices
            Swap(ref m_Context.PreviousSortedIndices, ref m_Context.SortedIndices);
            m_Context.SortedIndices.Resize(m_Context.InstanceCount);
            for (int index = 0; index < m_Context.InstanceCount; index++)
                m_Context.SortedIndices[index] = m_Context.PreviousSortedIndices[m_Context.RemappedSortIndex[index]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateTreeStructure()
        {
            int newCount = m_Context.CurrentNodes.Length + m_Context.Groups.Length;
            m_Context.RemappedSortIndex.Resize(newCount);

            // Initialize levelStarts list and add the number of instances as the first element
            m_Context.LevelStarts.Clear();
            m_Context.LevelStarts.Add(m_Context.Groups.Length);
            for (int index = 0; index < m_Context.NodesPerLevel.Length - 1; index++)
                m_Context.LevelStarts.Add(m_Context.LevelStarts[index] + m_Context.NodesPerLevel[index]);

            for (int index = 0; index < m_Context.RootCount; index++)
            {
                ref readonly BuildNode node = ref m_Context.CurrentNodes.Ptr[m_Context.Indices[index]];
                m_Context.RemappedSortIndex[m_Context.LevelStarts[0]++] = m_Context.Indices[index];

                int left = node.FirstChild;
                int right = node.LastChild;
                int level = 1;

                while (right >= 0)
                {
                    int nextLeft = int.MaxValue;
                    int nextRight = -1;

                    for (int child = left; child <= right; child++)
                    {
                        m_Context.RemappedSortIndex[m_Context.LevelStarts[level]++] = child;

                        int childLeft = m_Context.CurrentNodes[child].FirstChild;
                        if (childLeft >= 0 && childLeft < nextLeft)
                            nextLeft = childLeft;

                        int childRight = m_Context.CurrentNodes[child].LastChild;
                        if (childRight >= 0 && childRight > nextRight)
                            nextRight = childRight;
                    }

                    left = nextLeft;
                    right = nextRight;
                    level++;
                }
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_Context.LevelStarts[^1] != newCount)
                throw new InvalidOperationException("Level start count mismatch.");
#endif

            m_Context.InverseChildIndex.Resize(newCount);
            for (int index = m_Context.Groups.Length; index < newCount; index++)
                m_Context.InverseChildIndex[m_Context.RemappedSortIndex[index]] = index;

            for (int index = 0; index < m_Context.CurrentNodes.Length; index++)
            {
                ref BuildNode node = ref m_Context.CurrentNodes.Ptr[index];
                if (node.FirstChild >= 0)
                {
                    node.FirstChild = m_Context.InverseChildIndex[node.FirstChild];
                    node.LastChild = m_Context.InverseChildIndex[node.LastChild];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SwapPrevious()
        {
            Swap(ref m_Context.PreviousNodes, ref m_Context.CurrentNodes);

            m_Context.CurrentNodes.Clear();
            for (int index = 0; index < m_Context.Groups.Length; index++)
                m_Context.CurrentNodes.Add(BuildNode.Empty);

            m_Context.CurrentNodes.AddUninitialized(m_Context.PreviousNodes.Length);
            for (int index = 0; index < m_Context.PreviousNodes.Length; index++)
                m_Context.CurrentNodes[m_Context.InverseChildIndex[index]] = m_Context.PreviousNodes[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UpdateNodeBounds()
        {
            int oldIndex = m_Context.Groups.Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            int debugTracker = 0;
#endif

            for (int index = 0; index < m_Context.Groups.Length; index++)
            {
                ref BuildNode node = ref m_Context.CurrentNodes.Ptr[index];
                node.FirstChild = oldIndex;

                oldIndex += m_Context.Groups[index].Count;

                node.LastChild = oldIndex - 1;
                node.FirstInstance = m_Context.CurrentNodes[node.FirstChild].FirstInstance;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (node.FirstInstance != debugTracker)
                    throw new InvalidOperationException("Instance tracker mismatch.");
#endif

                node.LastInstance = m_Context.CurrentNodes[node.LastChild].LastInstance;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                debugTracker = node.LastInstance + 1;
                if (node.LastInstance + 1 > m_Context.InstanceCount)
                    throw new InvalidOperationException("Instance count mismatch.");
#endif

                node.Bounds = AxisAlignedBox.Empty;

                for (int childIndex = node.FirstChild; childIndex <= node.LastChild; childIndex++)
                {
                    ref readonly BuildNode childNode = ref m_Context.CurrentNodes.Ptr[childIndex];
                    node.Bounds += childNode.Bounds;
                    node.MinInstanceScale = math.min(node.MinInstanceScale, childNode.MinInstanceScale);
                    node.MaxInstanceScale = math.max(node.MaxInstanceScale, childNode.MaxInstanceScale);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InitializeContext()
        {
            int transformCount = InstanceTransforms.Length;

            m_Context.Groups = new UnsafeList<IndexRange>(16, AllocatorManager.Temp);
            m_Context.ScratchKeys = new UnsafeList<KeyAndIndex>(transformCount, AllocatorManager.Temp);

            m_Context.Points = new UnsafeList<float3>(transformCount, AllocatorManager.Temp);
            m_Context.Points.Resize(transformCount);

            m_Context.Indices = new UnsafeList<int>((int)(transformCount * Result.Density), AllocatorManager.Temp);

            // Loop over all instances and add them to the sorting context if they pass the density test
            Random random = Result.Density < 1.0f ? new Random(DensityRandomSeed) : new Random(1);
            for (int index = 0; index < transformCount; index++)
            {
                m_Context.Points[index] = InstanceTransforms[index].Position;

                if (Result.Density < 1.0f && random.NextFloat() > Result.Density)
                {
                    // Skip instances that generate a random number greater than the density scaling
                    continue;
                }

                m_Context.Indices.Add(index);
            }

            // Instance count after density test
            m_Context.InstanceCount = m_Context.Indices.Length;
            m_Context.OcclusionLayerCount = MaxOcclusionQueries;

            if (m_Context.InstanceCount / MinInstancesPerOcclusionQuery < m_Context.OcclusionLayerCount)
            {
                m_Context.OcclusionLayerCount = m_Context.InstanceCount / MinInstancesPerOcclusionQuery;
                if (m_Context.OcclusionLayerCount < MinOcclusionQueries)
                {
                    m_Context.OcclusionLayerCount = 0;
                }
            }

            // Determine the maximum leaf size
            m_Context.MaxInstancesPerLeaf = MaxInstancesPerLeaf;
            m_Context.InternalBranchingFactor = SplitFactor;
            if (m_Context.InstanceCount / m_Context.MaxInstancesPerLeaf < m_Context.InternalBranchingFactor)
            {
                m_Context.MaxInstancesPerLeaf = math.clamp(m_Context.InstanceCount / SplitFactor, 1, 1024);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Swap<T>(ref T lhs, ref T rhs) => (lhs, rhs) = (rhs, lhs);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SplitNodes(int instanceCount)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (instanceCount <= 0)
                throw new ArgumentException("Instance count must be greater than zero.");
#endif

            // Split the instances recursively
            m_Context.Groups.Clear();
            SplitNodesRecursive(0, instanceCount - 1);

            // Sort the node groups
            m_Context.Groups.Sort();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_Context.Groups.Length <= 0)
                throw new InvalidOperationException("No groups were created during the split operation.");
#endif
            ValidateNodeGroups(m_Context, instanceCount);
        }

        void SplitNodesRecursive(int groupStart, int groupEnd)
        {
            // Calculate group bounding box
            AxisAlignedBox groupBounds = AxisAlignedBox.Empty;
            for (int i = groupStart; i <= groupEnd; i++)
                groupBounds += m_Context.Points[m_Context.Indices[i]];

            // If we're within range of the branching factor, create a group and stop splitting
            int rangeLength = 1 + groupEnd - groupStart;
            if (rangeLength <= m_Context.BranchingFactor)
            {
                m_Context.Groups.Add(new IndexRange(groupStart, rangeLength));
                return;
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (rangeLength < 2)
                throw new InvalidOperationException("Invalid range length.");
#endif

            m_Context.ScratchKeys.Clear();

            ComputeAxisAndPivot(groupBounds, out int axis, out float _);
            SortGroupByAxis(axis, groupStart, groupEnd);

            int midPoint = rangeLength / 2;
            int leftEnd = groupStart + midPoint - 1;
            int rightStart = groupEnd - midPoint + 1;

            if (IsOdd(rangeLength))
            {
                // Choose the closest element to the middle as the pivot
                if (m_Context.ScratchKeys[midPoint].Value - m_Context.ScratchKeys[midPoint - 1].Value < m_Context.ScratchKeys[midPoint + 1].Value - m_Context.ScratchKeys[midPoint].Value)
                {
                    leftEnd++;
                }
                else
                {
                    rightStart--;
                }
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (leftEnd < groupStart || rightStart > groupEnd)
                throw new InvalidOperationException("Invalid split range.");
            if (leftEnd + 1 != rightStart)
                throw new InvalidOperationException("Split range is not contiguous.");
#endif

            // Split left and right ranges
            SplitNodesRecursive(groupStart, leftEnd);
            SplitNodesRecursive(rightStart, groupEnd);
        }

        void SortGroupByAxis(int axis, int groupStart, int groupEnd)
        {
            // Prepare sorting pairs based on the specified axis
            for (int i = groupStart; i <= groupEnd; i++)
            {
                KeyAndIndex pair;
                pair.Index = m_Context.Indices[i];
                pair.Value = m_Context.Points[pair.Index][axis];
                m_Context.ScratchKeys.Add(pair);
            }

            // Sort and update context indices
            NativeSortExtension.Sort(m_Context.ScratchKeys.Ptr, m_Context.ScratchKeys.Length);
            for (int i = groupStart; i <= groupEnd; i++)
                m_Context.Indices[i] = m_Context.ScratchKeys[i - groupStart].Index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ComputeAxisAndPivot(in AxisAlignedBox domain, out int axis, out float pivot)
        {
            axis = VectorUtility.IndexOfMaxComponent(domain.Extents);
            pivot = domain.Center[axis];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsOdd(int value) => (value & 1) != 0;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateNodeGroups(in BuildContext context, int expectedTotalInstances)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            int accumulatedInstanceCount = 0;
            foreach (IndexRange group in context.Groups)
            {
                if (accumulatedInstanceCount != group.Start)
                    throw new InvalidOperationException("Instance groups are not contiguous.");

                accumulatedInstanceCount += group.Count;
            }

            if (accumulatedInstanceCount != expectedTotalInstances)
                throw new InvalidOperationException("Not all instances were accounted for in the groups.");
#endif
        }
    }
}
