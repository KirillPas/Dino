// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef INSTANCE_DRAW_BATCHER_INCLUDED
#define INSTANCE_DRAW_BATCHER_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.ma.flora/ShaderLibrary/ComputeUtility.hlsl"

//-----------------------------------------------------------------------------
// Defines
//-----------------------------------------------------------------------------

#ifndef THREADS_PER_GROUP
#error Must define THREADS_PER_GROUP before including InstanceDrawBatcher.hlsl
#define THREADS_PER_GROUP (64)
#endif

#if THREADS_PER_GROUP == 128
#define PACKED_PREFIX_BITS  (7u)   // 7 bits can store 127 values
#define PACKED_PREFIX_MASK  (127u) // The mask for the prefix bits
#define PACKED_PREFIX_MAX   (4u)   // The number of prefix sums stored in the batch
#else
#define PACKED_PREFIX_BITS  (6u)   // 6 bits can store 63 values
#define PACKED_PREFIX_MASK  (63u)  // The mask for the prefix bits
#define PACKED_PREFIX_MAX   (5u)   // The number of prefix sums stored in the batch
#endif

#if THREADS_PER_GROUP == 128
#define PACKED_INSTANCE_COUNT_BITS (8u)   // 8 bits can store 255 values (need 128)
#define PACKED_INSTANCE_COUNT_MASK (255u) // The mask for the instance count bits
#else
#define PACKED_INSTANCE_COUNT_BITS (7u)   // 7 bits can store 127 values (need 64)
#define PACKED_INSTANCE_COUNT_MASK (127u) // The mask for the instance count bits
#endif

//-----------------------------------------------------------------------------
// Prefix Sum
//-----------------------------------------------------------------------------

groupshared uint gs_ScratchItemIndex[THREADS_PER_GROUP]; // Group-shared memory for prefix maximum computation

// Performs a prefix max operation on a group-shared array using parallel reduction.
// TODO: Create SM 6.0 version equivalent 
uint PrefixMax(uint groupIndex)
{
    GroupMemoryBarrierWithGroupSync();
    uint value = gs_ScratchItemIndex[groupIndex];
    GroupMemoryBarrierWithGroupSync();
    
    [unroll]
    for (uint s = THREADS_PER_GROUP; s > 0u; s >>= 1u)
    {
        if (groupIndex >= s)
            value = max(value, gs_ScratchItemIndex[groupIndex - s]);
        
        GroupMemoryBarrierWithGroupSync();
        if (groupIndex >= s)
            gs_ScratchItemIndex[groupIndex] = value;
        GroupMemoryBarrierWithGroupSync();
    }
    
    if (groupIndex > 0u)
        value = max(value, gs_ScratchItemIndex[groupIndex - 1u]);
    
    return value;
}

//-----------------------------------------------------------------------------
// Uniforms
//-----------------------------------------------------------------------------

struct PackedInstanceBatch
{
    uint Index_Count;            // packed 32-Index:24-Count:8
    uint PackedPrefixSumOffsets; // packed 32-5xPrefixSum:30-Unused:2
};

struct PackedInstanceBatchItem
{
    uint InstanceDataOffset_NumInstances; // packed 32-InstanceDataOffset:24-NumInstances:8
    uint Payload_BatchPrefixOffset;       // packed 32-Payload:26-BatchPrefixOffset:6
};

StructuredBuffer<PackedInstanceBatch>     _InstanceBatches;
StructuredBuffer<PackedInstanceBatchItem> _InstanceBatchItems;

struct InstanceBatch
{
    uint Index;            // Index of the first item in the batch
    uint Count;            // Number of items in the batch
    uint PackedPrefixSums; // First 5 prefix sums for the items in the batch
};

struct InstanceBatchItem
{
    uint InstanceDataOffset; // Offset of the first instance in the batch
    uint InstanceCount;      // Number of instances in the batch
    uint Payload;            // Payload data for the batch
    uint BatchPrefixOffset;  // Offset of the batch prefix sum in the batch
};

InstanceBatch LoadBatch(uint index, uint itemOffset)
{
    PackedInstanceBatch packedBatch = _InstanceBatches[index];
    
    InstanceBatch result;
    result.Index = itemOffset + packedBatch.Index_Count >> PACKED_INSTANCE_COUNT_BITS;
    result.Count = packedBatch.Index_Count & PACKED_INSTANCE_COUNT_MASK;
    result.PackedPrefixSums = packedBatch.PackedPrefixSumOffsets;
    return result;
}

InstanceBatchItem LoadBatchItem(uint index)
{
    PackedInstanceBatchItem packedItem = _InstanceBatchItems[index];
    
    InstanceBatchItem result;
    result.InstanceDataOffset = packedItem.InstanceDataOffset_NumInstances >> PACKED_INSTANCE_COUNT_BITS;
    result.InstanceCount      = packedItem.InstanceDataOffset_NumInstances  & PACKED_INSTANCE_COUNT_MASK;
    result.Payload            = packedItem.Payload_BatchPrefixOffset >> PACKED_PREFIX_BITS;
    result.BatchPrefixOffset  = packedItem.Payload_BatchPrefixOffset  & PACKED_PREFIX_MASK;
    return result;
}

//-----------------------------------------------------------------------------
// Batch Processing
//-----------------------------------------------------------------------------

groupshared InstanceBatchItem gs_InstanceBatchItems[THREADS_PER_GROUP];

struct InstanceBatchTask
{
    bool IsValid;           // Whether the task is valid
    InstanceBatchItem Item; // The batch item for the task
    int Index;              // The index of the instance within the batch
    uint WorkItemIndex;     // The index of the work item
};

void InitializeInstanceBatchTask(uint groupThreadIndex, InstanceBatchItem item, uint workItemIndex, out InstanceBatchTask task)
{
    task.Item = item;
    task.WorkItemIndex = workItemIndex;
    task.Index = int(groupThreadIndex - task.Item.BatchPrefixOffset);
    task.IsValid = task.Index >= 0 && task.Index < int(task.Item.InstanceCount);
}

void SetupInstanceBatchTask(uint batchStart, uint batchCount, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex, out InstanceBatchTask task)
{
    task = (InstanceBatchTask)0;

    uint batchIndex = batchStart + UnwrapLinearGroupID(groupID);
    uint batchEnd = batchStart + batchCount;
    if (batchIndex >= batchEnd)
        return;

    InstanceBatch batch = LoadBatch(batchIndex, 0u);
    
    if (batch.Count == THREADS_PER_GROUP)
    {
        // If the number of instances matches the number of threads, map each instance to a thread directly
        task.Item = LoadBatchItem(batch.Index + groupIndex);
        task.IsValid = true;
    }
    else if (batch.Count <= PACKED_PREFIX_MAX)
    {
        // For small batches, unpack the prefix sums from the batch and find the work item index
        uint workItemIndex = 0u;
        for (int itemIndex = int(batch.Count) - 1; itemIndex >= 0; --itemIndex)
        {
            uint itemPrefixOffset = (batch.PackedPrefixSums >> (itemIndex * PACKED_PREFIX_BITS)) & PACKED_PREFIX_MASK;
            if (groupIndex >= itemPrefixOffset)
            {
                workItemIndex = uint(itemIndex);
                break;
            }
        }
    
        InstanceBatchItem item = LoadBatchItem(batch.Index + workItemIndex);
        InitializeInstanceBatchTask(groupIndex, item, workItemIndex, task);
    }
    else
    {
        // For large batches, use a parallel prefix sum to find the work item index
        gs_ScratchItemIndex[groupIndex] = 0u;
    
        GroupMemoryBarrierWithGroupSync();
        if (groupIndex < batch.Count)
        {
            // Unpack items into shared memory
            InstanceBatchItem item = LoadBatchItem(batch.Index + groupIndex);
            gs_ScratchItemIndex[item.BatchPrefixOffset] = groupIndex;
            gs_InstanceBatchItems[groupIndex] = item;
        }
        GroupMemoryBarrierWithGroupSync();

        uint workItemIndex = PrefixMax(groupIndex);
        InitializeInstanceBatchTask(groupIndex, gs_InstanceBatchItems[workItemIndex], workItemIndex, task);
    }
}

#endif // INSTANCE_DRAW_BATCHER_INCLUDED