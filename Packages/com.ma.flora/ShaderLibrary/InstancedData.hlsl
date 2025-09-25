// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_INSTANCED_DATA_INCLUDED
#define FLORA_INSTANCED_DATA_INCLUDED

#ifndef UINT_MAX
#define UINT_MAX 0xffffffffu
#endif

#ifndef CBUFFER_START
#define CBUFFER_START(name) cbuffer name {
#define CBUFFER_END }
#endif

#include "Packages/com.ma.flora/Config/ShaderConfig.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

//-----------------------------------------------------------------------------
// Utils
//-----------------------------------------------------------------------------

float UnpackFloraInstanceCrossFadeSnorm8(int crossfadeSNorm8)
{
    float crossfade = clamp((float)crossfadeSNorm8, -127, 127) + 0.5;
    crossfade *= 1.0 / 127;
    return crossfade;
}

float4x4 InverseFloraInstanceMatrix(float4x4 trs)
{
    float3x3 invRot;
    invRot[0] = trs[1].yzx * trs[2].zxy - trs[1].zxy * trs[2].yzx;
    invRot[1] = trs[0].zxy * trs[2].yzx - trs[0].yzx * trs[2].zxy;
    invRot[2] = trs[0].yzx * trs[1].zxy - trs[0].zxy * trs[1].yzx;

    float invDet = dot(trs[0].xyz, invRot[0]);
    invRot = transpose(invRot);
    invRot *= rcp(invDet);

    float3 invPos = mul(invRot, -trs._14_24_34);
    float4x4 invTRS;
    invTRS._11_21_31_41 = float4(invRot._11_21_31, 0.0);
    invTRS._12_22_32_42 = float4(invRot._12_22_32, 0.0);
    invTRS._13_23_33_43 = float4(invRot._13_23_33, 0.0);
    invTRS._14_24_34_44 = float4(invPos, 1.0);
    return invTRS;
}

//-----------------------------------------------------------------------------
// Instance Data
//-----------------------------------------------------------------------------

static const uint kFloraPerInstanceDataBit = 0x80000000;
static const uint kFloraAddressMask        = 0x7fffffff;

CBUFFER_START(flora_BuiltinPropertyMetadata)
    uint flora_RendererDataStrideSOA;
    uint flora_InstanceTransformMetadata;
    uint flora_InstanceLightProbeMetadata;
    uint flora_InstanceSelectionMetadata;
CBUFFER_END

StructuredBuffer<float4> flora_RendererData;
#ifdef FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_RAW
ByteAddressBuffer        flora_InstanceData;
#else
StructuredBuffer<float4> flora_InstanceData;
#endif

static uint flora_SampledInstanceIndex;
static int flora_SampledLODCrossfade;

uint GetFloraInstanceIndex()
{
    return flora_SampledInstanceIndex;
}

bool IsFloraInstancedProperty(uint metadata)
{
    return metadata > 0;
}

// Stride is typically expected to be a compile-time literal here, so this should
// be optimized into shifts and other cheap ALU ops by the compiler.
uint ComputeFloraInstanceOffset(uint instanceIndex, uint stride)
{
    return instanceIndex * stride;
}

uint ComputeFloraInstanceDataAddress(uint metadata, uint stride)
{
    uint isOverridden = metadata ? kFloraPerInstanceDataBit : 0;
    // Sign extend per-instance data bit so it can just be ANDed with the offset
    uint offsetMask   = (uint)((int)isOverridden >> 31);
    uint baseAddress  = metadata;
    uint offset       = ComputeFloraInstanceOffset(GetFloraInstanceIndex(), stride);
    offset           &= offsetMask;
    return baseAddress + offset;
}

// This version assumes that the high bit of the metadata is set (= per instance data).
// Useful if the call site has already branched over this.
uint ComputeFloraInstanceDataAddressOverridden(uint metadata, uint stride)
{
    uint baseAddress = metadata;
    uint offset      = ComputeFloraInstanceOffset(GetFloraInstanceIndex(), stride);
    return baseAddress + offset;
}

#ifdef FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_FLOAT4
// In UBO mode we precompute our select masks based on our instance index.
// All base addresses are aligned by 16, so we already know which offsets
// the instance index will load (modulo 16).
// All float1 loads will share the select4 masks, and all float2 loads
// will share the select2 mask.
// These variables are single assignment only, and should hopefully be well
// optimizable and dead code eliminatable for the compiler.
static uint flora_InstanceData_Select4_Mask0;
static uint flora_InstanceData_Select4_Mask1;
static uint flora_InstanceData_Select2_Mask;

// The compiler should dead code eliminate the parts of this that are not used by the shader.
void SetupFloraInstanceSelectMasks()
{
    uint instanceIndex = GetFloraInstanceIndex();
    uint offsetSingleChannel = instanceIndex << 2; // float: stride 4 bytes

    // x = 0 = 00
    // y = 1 = 01
    // z = 2 = 10
    // w = 3 = 11
    // Lowest 2 bits are zero, all accesses are aligned, and base addresses are aligned by 16.
    // Bits 29 and 28 give the channel index.
    // NOTE: Mask generation was rewritten to this form specifically to avoid codegen
    // correctness issues on GLES.
    flora_InstanceData_Select4_Mask0 = (offsetSingleChannel & 0x4) ? 0xffffffff : 0;
    flora_InstanceData_Select4_Mask1 = (offsetSingleChannel & 0x8) ? 0xffffffff : 0;
    // Select2 mask is the same as the low bit mask of select4, since
    // (x << 3) << 28 == (x << 2) << 29
    flora_InstanceData_Select2_Mask = flora_InstanceData_Select4_Mask0;
}

uint FloraInstanceData_Select(uint addressOrOffset, uint4 v)
{
    uint mask0 = flora_InstanceData_Select4_Mask0;
    uint mask1 = flora_InstanceData_Select4_Mask1;
    return
        (((v.w & mask0) | (v.z & ~mask0)) & mask1) |
        (((v.y & mask0) | (v.x & ~mask0)) & ~mask1);
}

uint2 FloraInstanceData_Select2(uint addressOrOffset, uint4 v)
{
    uint mask0 = flora_InstanceData_Select2_Mask;
    return (v.zw & mask0) | (v.xy & ~mask0);
}
#else
void SetupFloraInstanceSelectMasks() {} // No-op
#endif

uint FloraInstanceData_Load(uint address)
{
#ifdef FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_FLOAT4
    uint float4Index = address >> 4;
    uint4 raw = asuint(flora_InstanceData[float4Index]);
    return FloraInstanceData_Select(address, raw);
#else
    return flora_InstanceData.Load(address);
#endif
}
uint2 FloraInstanceData_Load2(uint address)
{
#ifdef FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_FLOAT4
    uint float4Index = address >> 4;
    uint4 raw = asuint(flora_InstanceData[float4Index]);
    return FloraInstanceData_Select2(address, raw);
#else
    return flora_InstanceData.Load2(address);
#endif
}

uint4 FloraInstanceData_Load4(uint address)
{
#ifdef FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_FLOAT4
    uint float4Index = address >> 4;
    return asuint(flora_InstanceData[float4Index]);
#else
    return flora_InstanceData.Load4(address);
#endif
}

uint3 FloraInstanceData_Load3(uint address)
{
#ifdef FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_FLOAT4
    // This is likely to be slow, tightly packed float3s are tricky
    switch (address & 0xf)
    {
    default:
    case 0:
        return FloraInstanceData_Load4(address).xyz;
    case 4:
        return FloraInstanceData_Load4(address).yzw;
    case 8:
        {
            uint float4Index = address >> 4;
            uint4 raw0 = asuint(flora_InstanceData[float4Index]);
            uint4 raw1 = asuint(flora_InstanceData[float4Index + 1]);
            uint3 v;
            v.xy = raw0.zw;
            v.z  = raw1.x;
            return v;
        }
    case 12:
        {
            uint float4Index = address >> 4;
            uint4 raw0 = asuint(flora_InstanceData[float4Index]);
            uint4 raw1 = asuint(flora_InstanceData[float4Index + 1]);
            uint3 v;
            v.x  = raw0.w;
            v.yz = raw1.xy;
            return v;
        }
    }
#else
    return flora_InstanceData.Load3(address);
#endif
}

#define DEFINE_FLORA_LOAD_INSTANCE_SCALAR(type, conv, sizeof_type) \
type LoadFloraInstancedData_##type(uint metadata) \
{ \
    uint address = ComputeFloraInstanceDataAddress(metadata, sizeof_type); \
    return conv(FloraInstanceData_Load(address)); \
} \
type LoadFloraInstancedDataOverridden_##type(uint metadata) \
{ \
    uint address = ComputeFloraInstanceDataAddressOverridden(metadata, sizeof_type); \
    return conv(FloraInstanceData_Load(address)); \
} \
type LoadFloraInstancedData_##type(type default_value, uint metadata) \
{ \
    uint address = ComputeFloraInstanceDataAddressOverridden(metadata, sizeof_type); \
    return IsFloraInstancedProperty(metadata) ? \
        conv(FloraInstanceData_Load(address)) : default_value; \
}

#define DEFINE_FLORA_LOAD_INSTANCE_VECTOR(type, width, conv, sizeof_type) \
type##width LoadFloraInstancedData_##type##width(uint metadata) \
{ \
    uint address = ComputeFloraInstanceDataAddress(metadata, sizeof_type * width); \
    return conv(FloraInstanceData_Load##width(address)); \
} \
type##width LoadFloraInstancedDataOverridden_##type##width(uint metadata) \
{ \
    uint address = ComputeFloraInstanceDataAddressOverridden(metadata, sizeof_type * width); \
    return conv(FloraInstanceData_Load##width(address)); \
} \
type##width LoadFloraInstancedData_##type##width(type##width default_value, uint metadata) \
{ \
    uint address = ComputeFloraInstanceDataAddressOverridden(metadata, sizeof_type * width); \
    return IsFloraInstancedProperty(metadata) ? \
        conv(FloraInstanceData_Load##width(address)) : default_value; \
}

DEFINE_FLORA_LOAD_INSTANCE_SCALAR(float, asfloat, 4)
DEFINE_FLORA_LOAD_INSTANCE_SCALAR(int,   int,     4)
DEFINE_FLORA_LOAD_INSTANCE_SCALAR(uint,  uint,    4)
//DEFINE_FLORA_LOAD_INSTANCE_SCALAR(half,  half,    2)

DEFINE_FLORA_LOAD_INSTANCE_VECTOR(float, 2, asfloat, 4)
DEFINE_FLORA_LOAD_INSTANCE_VECTOR(float, 3, asfloat, 4)
DEFINE_FLORA_LOAD_INSTANCE_VECTOR(float, 4, asfloat, 4)
DEFINE_FLORA_LOAD_INSTANCE_VECTOR(int,   2, int2,    4)
DEFINE_FLORA_LOAD_INSTANCE_VECTOR(int,   3, int3,    4)
DEFINE_FLORA_LOAD_INSTANCE_VECTOR(int,   4, int4,    4)
DEFINE_FLORA_LOAD_INSTANCE_VECTOR(uint,  2, uint2,   4)
DEFINE_FLORA_LOAD_INSTANCE_VECTOR(uint,  3, uint3,   4)
DEFINE_FLORA_LOAD_INSTANCE_VECTOR(uint,  4, uint4,   4)
//DEFINE_FLORA_LOAD_INSTANCE_VECTOR(half,  2, half2,   2)
//DEFINE_FLORA_LOAD_INSTANCE_VECTOR(half,  3, half3,   2)
//DEFINE_FLORA_LOAD_INSTANCE_VECTOR(half,  4, half4,   2)

half LoadFloraInstancedData_half(uint metadata)
{
    float f = LoadFloraInstancedData_float(metadata);
    min16float f16 = min16float(f);
    return f16;
}
half LoadFloraInstancedDataOverridden_half(uint metadata)
{
    float f = LoadFloraInstancedDataOverridden_float(metadata);
    min16float f16 = min16float(f);
    return f16;
}

half4 LoadFloraInstancedData_half4(uint metadata)
{
    float4 f = LoadFloraInstancedData_float4(metadata);
    min16float4 f16x4 = min16float4(f.x, f.y, f.z, f.w);
    return f16x4;
}
half4 LoadFloraInstancedDataOverridden_half4(uint metadata)
{
    float4 f = LoadFloraInstancedDataOverridden_float4(metadata);
    min16float4 f16x4 = min16float4(f.x, f.y, f.z, f.w);
    return f16x4;
}

min16float LoadFloraInstancedData_min16float(uint metadata)
{
    return min16float(LoadFloraInstancedData_half(metadata));
}
min16float LoadFloraInstancedDataOverridden_min16float(uint metadata)
{
    return min16float(LoadFloraInstancedDataOverridden_half(metadata));
}

min16float4 LoadFloraInstancedData_min16float4(uint metadata)
{
    return min16float4(LoadFloraInstancedData_half4(metadata));
}
min16float4 LoadFloraInstancedDataOverridden_min16float4(uint metadata)
{
    return min16float4(LoadFloraInstancedDataOverridden_half4(metadata));
}

min16float LoadFloraInstancedData_min16float(min16float default_value, uint metadata)
{
    return IsFloraInstancedProperty(metadata) ?
        LoadFloraInstancedData_min16float(metadata) : default_value;
}

min16float4 LoadFloraInstancedData_min16float4(min16float4 default_value, uint metadata)
{
    return IsFloraInstancedProperty(metadata) ?
        LoadFloraInstancedData_min16float4(metadata) : default_value;
}

// TODO: Other matrix sizes
float4x4 LoadFloraInstancedData_float4x4(uint metadata)
{
    uint address = ComputeFloraInstanceDataAddress(metadata, 4 * 16);
    float4 p1 = asfloat(asfloat(FloraInstanceData_Load4(address + 0 * 16)));
    float4 p2 = asfloat(asfloat(FloraInstanceData_Load4(address + 1 * 16)));
    float4 p3 = asfloat(asfloat(FloraInstanceData_Load4(address + 2 * 16)));
    float4 p4 = asfloat(asfloat(FloraInstanceData_Load4(address + 3 * 16)));
    return float4x4(
        p1.x, p2.x, p3.x, p4.x,
        p1.y, p2.y, p3.y, p4.y,
        p1.z, p2.z, p3.z, p4.z,
        p1.w, p2.w, p3.w, p4.w);
}
float4x4 LoadFloraInstancedDataOverridden_float4x4(uint metadata)
{
    uint address = ComputeFloraInstanceDataAddressOverridden(metadata, 4 * 16);
    float4 p1 = asfloat(asfloat(FloraInstanceData_Load4(address + 0 * 16)));
    float4 p2 = asfloat(asfloat(FloraInstanceData_Load4(address + 1 * 16)));
    float4 p3 = asfloat(asfloat(FloraInstanceData_Load4(address + 2 * 16)));
    float4 p4 = asfloat(asfloat(FloraInstanceData_Load4(address + 3 * 16)));
    return float4x4(
        p1.x, p2.x, p3.x, p4.x,
        p1.y, p2.y, p3.y, p4.y,
        p1.z, p2.z, p3.z, p4.z,
        p1.w, p2.w, p3.w, p4.w);
}

float4x4 LoadFloraInstancedData_float4x4_from_float3x4(uint metadata)
{
    uint address = ComputeFloraInstanceDataAddress(metadata, 3 * 16);
    float4 p1 = asfloat(asfloat(FloraInstanceData_Load4(address + 0 * 16)));
    float4 p2 = asfloat(asfloat(FloraInstanceData_Load4(address + 1 * 16)));
    float4 p3 = asfloat(asfloat(FloraInstanceData_Load4(address + 2 * 16)));

    return float4x4(
        p1.x, p1.w, p2.z, p3.y,
        p1.y, p2.x, p2.w, p3.z,
        p1.z, p2.y, p3.x, p3.w,
        0.0,  0.0,  0.0,  1.0
    );
}
float4x4 LoadFloraInstancedDataOverridden_float4x4_from_float3x4(uint metadata)
{
    uint address = ComputeFloraInstanceDataAddressOverridden(metadata, 3 * 16);
    float4 p1 = asfloat(asfloat(FloraInstanceData_Load4(address + 0 * 16)));
    float4 p2 = asfloat(asfloat(FloraInstanceData_Load4(address + 1 * 16)));
    float4 p3 = asfloat(asfloat(FloraInstanceData_Load4(address + 2 * 16)));

    return float4x4(
        p1.x, p1.w, p2.z, p3.y,
        p1.y, p2.x, p2.w, p3.z,
        p1.z, p2.y, p3.x, p3.w,
        0.0,  0.0,  0.0,  1.0
    );
}

float2x4 LoadFloraInstancedData_float2x4(uint metadata)
{
    uint address = ComputeFloraInstanceDataAddress(metadata, 4 * 8);
    return float2x4(
        asfloat(FloraInstanceData_Load4(address + 0 * 8)),
        asfloat(FloraInstanceData_Load4(address + 1 * 8)));
}
float2x4 LoadFloraInstancedDataOverridden_float2x4(uint metadata)
{
    uint address = ComputeFloraInstanceDataAddressOverridden(metadata, 4 * 8);
    return float2x4(
        asfloat(FloraInstanceData_Load4(address + 0 * 8)),
        asfloat(FloraInstanceData_Load4(address + 1 * 8)));
}

float4x4 LoadFloraInstancedData_float4x4(float4x4 default_value, uint metadata)
{
    return IsFloraInstancedProperty(metadata) ?
        LoadFloraInstancedData_float4x4(metadata) : default_value;
}

float4x4 LoadFloraInstancedData_float4x4_from_float3x4(float4x4 default_value, uint metadata)
{
    return IsFloraInstancedProperty(metadata) ?
        LoadFloraInstancedData_float4x4_from_float3x4(metadata) : default_value;
}

float2x4 LoadFloraInstancedData_float2x4(float4 default_value[2], uint metadata)
{
    return IsFloraInstancedProperty(metadata) ?
        LoadFloraInstancedData_float2x4(metadata) : float2x4(default_value[0], default_value[1]);
}

float2x4 LoadFloraInstancedData_float2x4(float2x4 default_value, uint metadata)
{
    return IsFloraInstancedProperty(metadata) ?
        LoadFloraInstancedData_float2x4(metadata) : default_value;
}

//-----------------------------------------------------------------------------
// Instanced Properties
//-----------------------------------------------------------------------------

#include "Packages/com.ma.flora/ShaderLibrary/InstancedProperties.hlsl"

//-----------------------------------------------------------------------------
// Instance Renderer
//-----------------------------------------------------------------------------

static const uint kFloraInstanceRendererFlagHasDynamicDensity = (1 << 0); // Dynamic density is enabled
static const uint kFloraInstanceRendererFlagHasCrossFade      = (1 << 1); // Cross-fade is enabled
static const uint kFloraInstanceRendererFlagHasCrossFadeAnim  = (1 << 2); // Cross-fade animation is enabled
static const uint kFloraInstanceRendererFlagHasSpeedTree      = (1 << 3); // Has a SpeedTree model
static const uint kFloraInstanceRendererFlagAllowsOcclusion   = (1 << 5); // Allows occlusion culling

struct SampledFloraInstanceRenderer
{
    uint ID;                     // The ID of the renderer
    uint Flags;                  // The flags for the renderer
    uint LODMask;                // The LOD mask for the renderer
    uint InstanceCount;          // The number of instances in the renderer
    uint InstanceStart;          // The offset of the instance for the renderer
    float4 ModelBoundingSphere;  // The bounding sphere for the renderer's model
    float4 DynamicDensityParams; // The density parameters for the renderer
    float4 DynamicFadeParams;    // The fade parameters for the renderer
};

static SampledFloraInstanceRenderer flora_SampledRenderer;

SampledFloraInstanceRenderer GetSampledFloraInstanceRenderer()
{
    return flora_SampledRenderer;
}

float4 LoadFloraInstanceRendererElement(uint rendererID, uint index)
{
    return flora_RendererData[index * flora_RendererDataStrideSOA + rendererID];
}

SampledFloraInstanceRenderer SampleFloraInstanceRenderer(uint rendererID)
{
    flora_SampledRenderer.ID                   = rendererID;
    flora_SampledRenderer.Flags                = asuint(LoadFloraInstanceRendererElement(rendererID, 0).x) >> 8;
    flora_SampledRenderer.InstanceCount        = asuint(LoadFloraInstanceRendererElement(rendererID, 0).y);
    flora_SampledRenderer.InstanceStart        = asuint(LoadFloraInstanceRendererElement(rendererID, 0).z);
    flora_SampledRenderer.ModelBoundingSphere  = LoadFloraInstanceRendererElement(rendererID, 1);
    flora_SampledRenderer.DynamicDensityParams = LoadFloraInstanceRendererElement(rendererID, 2);
    flora_SampledRenderer.DynamicFadeParams    = LoadFloraInstanceRendererElement(rendererID, 3);
    return flora_SampledRenderer;
}

//-----------------------------------------------------------------------------
// Instance
//-----------------------------------------------------------------------------

struct SampledFloraInstance
{
    bool IsValid;               // Instance is valid
    uint RendererID;            // ID of the renderer
    uint RenderIndex;           // Index of the instance within the renderer
    float4 LODFade;             // LOD fade value
    float RandomID;             // Per-instance random ID
    float DistanceFadeMin;      // Minimum distance fade value
    float DistanceFadeInvRange; // Inverse range of the distance fade value
    float4x4 ObjectToWorld;     // Local to world matrix (unity_ObjectToWorld)
    float4x4 WorldToObject;     // World to local matrix (unity_WorldToObject)
    float3 Origin;              // Origin of the instance
    float3 Center;              // Center of the instance
    float  Radius;              // Radius of the instance
};

static SampledFloraInstance flora_SampledInstance;

SampledFloraInstance GetSampledFloraInstance()
{
    return flora_SampledInstance;
}

void SampleFloraInstance(uint instanceIndex, int lodCrossFade = 0)
{
    flora_SampledInstanceIndex = instanceIndex;
    flora_SampledLODCrossfade  = lodCrossFade;
    SetupFloraInstanceSelectMasks();

    // --- LOD Fade ---

    float fadeValue                 = UnpackFloraInstanceCrossFadeSnorm8(lodCrossFade);
    flora_SampledInstance.LODFade.x = fadeValue;
    flora_SampledInstance.LODFade.y = 1.0 - clamp(round(fadeValue * 16.0) / 16.0, 0.0625, 1.0);

    // --- Transform ---

#ifdef FLORA_CONFIG_INSTANCE_DATA_TRANSFORM_PACKING_DISABLED
    uint transformAddress = ComputeFloraInstanceDataAddressOverridden(flora_InstanceTransformMetadata, 3 * 16);
    float4 p1 = asfloat(FloraInstanceData_Load4(transformAddress + 0 * 16));
    float4 p2 = asfloat(FloraInstanceData_Load4(transformAddress + 1 * 16));
    float4 p3 = asfloat(FloraInstanceData_Load4(transformAddress + 2 * 16));

    float4x4 objectToWorld = float4x4(
        p1.x, p1.w, p2.z, p3.y,
        p1.y, p2.x, p2.w, p3.z,
        p1.z, p2.y, p3.x, p3.w,
        0.0,  0.0,  0.0,  1.0
    );
#else
    // Packed Instance 32 bytes -> float3 position, half3 axisX, half3 axisY, half3 axisZ, ushort rendererIndex
    uint transformAddress = ComputeFloraInstanceDataAddressOverridden(flora_InstanceTransformMetadata, 2 * 16);
    uint4 e0 = FloraInstanceData_Load4(transformAddress + 0 * 16);
    uint4 e1 = FloraInstanceData_Load4(transformAddress + 1 * 16);

    float3 position = asfloat(e0.xyz);
    float3 axisX = float3(f16tof32(e0.w & 0xffff), f16tof32(e0.w >> 16   ), f16tof32(e1.x & 0xffff));
    float3 axisY = float3(f16tof32(e1.x >> 16   ), f16tof32(e1.y & 0xffff), f16tof32(e1.y >> 16   ));
    float3 axisZ = float3(f16tof32(e1.z & 0xffff), f16tof32(e1.z >> 16   ), f16tof32(e1.w & 0xffff));

    float4x4 objectToWorld = float4x4(
        axisX.x, axisY.x, axisZ.x, position.x,
        axisX.y, axisY.y, axisZ.y, position.y,
        axisX.z, axisY.z, axisZ.z, position.z,
        0.0,     0.0,     0.0,     1.0);
#endif

    flora_SampledInstance.Origin        = position;
    flora_SampledInstance.ObjectToWorld = objectToWorld;
    flora_SampledInstance.WorldToObject = InverseFloraInstanceMatrix(objectToWorld);

    // --- Renderer ---

    SampledFloraInstanceRenderer renderer      = SampleFloraInstanceRenderer(e1.w >> 16);
    flora_SampledInstance.IsValid              = renderer.ID > 0;
    flora_SampledInstance.RendererID           = renderer.ID;
    flora_SampledInstance.RenderIndex          = instanceIndex - renderer.InstanceStart;
    flora_SampledInstance.RandomID             = GenerateHashedRandomFloat(uint2(renderer.ID, instanceIndex));
    flora_SampledInstance.DistanceFadeMin      = renderer.DynamicFadeParams.x;
    flora_SampledInstance.DistanceFadeInvRange = renderer.DynamicFadeParams.z;

    // --- Bounding Sphere ---

    float3 boundingSphereCenter = mul(objectToWorld, float4(renderer.ModelBoundingSphere.xyz, 1.0)).xyz;
    float  boundingSphereRadius = sqrt(max(dot(axisX, axisX), max(dot(axisY, axisY), dot(axisZ, axisZ)))) * renderer.ModelBoundingSphere.w;
    flora_SampledInstance.Center = boundingSphereCenter;
    flora_SampledInstance.Radius = boundingSphereRadius;
}

void SampleFloraInstanceWithPackedLODFade(uint crossFadeInstanceIndex)
{
    uint instanceIndex = crossFadeInstanceIndex & 0x00ffffff;
    int lodCrossFade = int(crossFadeInstanceIndex) >> 24;
    SampleFloraInstance(instanceIndex, lodCrossFade);
}

//-----------------------------------------------------------------------------
// Selection
//-----------------------------------------------------------------------------

bool LoadFloraInstancedData_IsSelected()
{
    if (flora_InstanceSelectionMetadata)
    {
        uint selectionAddress   = ComputeFloraInstanceDataAddressOverridden(flora_InstanceSelectionMetadata, 4);
        uint selected_PickingID = FloraInstanceData_Load(selectionAddress);
        return selected_PickingID & 0x80000000;
    }

    return false;
}

float4 LoadFloraInstancedData_SelectionValue()
{
    if (flora_InstanceSelectionMetadata)
    {
        uint selectionAddress = ComputeFloraInstanceDataAddressOverridden(flora_InstanceSelectionMetadata, 4);
        uint selectionID = FloraInstanceData_Load(selectionAddress);
        return float4(uint4(selectionID >> 0, selectionID >> 8, selectionID >> 16, selectionID >> 24) & 0xff) / 255.0f;
    }

    return 0;
}

#endif // FLORA_INSTANCE_DATA_INCLUDED
