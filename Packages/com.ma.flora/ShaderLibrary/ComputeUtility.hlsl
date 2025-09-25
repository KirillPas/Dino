// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_COMPUTE_UTILITY_INCLUDED
#define FLORA_COMPUTE_UTILITY_INCLUDED

//-----------------------------------------------------------------------------
// Group Wrapping
//-----------------------------------------------------------------------------

#ifndef WRAPPED_GROUP_STRIDE
#define WRAPPED_GROUP_STRIDE (128)
#endif

struct WrappedGroup
{
    uint  GroupIndex : SV_GroupIndex;
    uint3 GroupID    : SV_GroupID;
};

// Function: UnwrapLinearGroupID
// Description:
//   Converts a wrapped group ID to a linear group ID, considering possible wrapping in the X dimension.
// Parameters:
//   groupID (Input): A wrapped group ID in three dimensions (SV_GroupID).
// Returns:
//   The linear group ID.
uint UnwrapLinearGroupID(uint3 groupID)
{
    return groupID.x + (groupID.z * WRAPPED_GROUP_STRIDE + groupID.y) * WRAPPED_GROUP_STRIDE;
}

// Function: UnwrapLinearDispatchThreadID
// Description:
//   Calculates the linear dispatch thread ID from a wrapped group ID and a group thread index.
//   Useful for mapping threads to unique thread IDs in a compute shader with wrapped group IDs.
// Parameters:
//   groupID (Input): A wrapped group ID in three dimensions (SV_GroupID).
//   groupIndex (Input): The index of the thread within the group (SV_GroupIndex).
//   threadGroupSize (Input): The size of a thread group.
// Returns:
//   The linear dispatch thread ID.
uint UnwrapLinearDispatchThreadID(uint3 groupID, uint groupIndex, uint threadGroupSize)
{
    return UnwrapLinearGroupID(groupID) * threadGroupSize + groupIndex;
}

uint UnwrapLinearDispatchThreadID(WrappedGroup group, uint threadGroupSize)
{
    return UnwrapLinearGroupID(group.GroupID) * threadGroupSize + group.GroupIndex;
}

// Function: WrapGroupCount
// Description:
//   Calculates the wrapped group count based on the target group count and dimension limits.
//   If the target group count exceeds the dimension limit in the X or Y dimension,
//   it wraps the groups into the Y and Z dimensions to ensure that the resulting group count
//   fits within the specified limits.
// Parameters:
//   targetGroupCount (Input): The desired target group count in the X dimension.
//   dimensionLimit (Input): The dimension limits for the X and Y dimensions.
// Returns:
//   The wrapped group count in three dimensions (X, Y, Z).
uint3 WrapGroupCount(uint targetGroupCount, uint2 dimensionLimit)
{
    uint3 groupCount = uint3(targetGroupCount, 1, 1);

    if (groupCount.x > dimensionLimit.x)
    {
        groupCount.y = (groupCount.x + WRAPPED_GROUP_STRIDE - 1) / WRAPPED_GROUP_STRIDE;
        groupCount.x = WRAPPED_GROUP_STRIDE;
    }

    if (groupCount.y > dimensionLimit.y)
    {
        groupCount.z = (groupCount.y + WRAPPED_GROUP_STRIDE - 1) / WRAPPED_GROUP_STRIDE;
        groupCount.y = WRAPPED_GROUP_STRIDE;
    }

    return groupCount;
}

#endif // FLORA_COMPUTE_SHADER_UTILITY_INCLUDED