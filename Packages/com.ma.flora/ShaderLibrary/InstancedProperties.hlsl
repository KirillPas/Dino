// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_INSTANCED_PROPERTIES_INCLUDED
#define FLORA_INSTANCED_PROPERTIES_INCLUDED

#if defined(FLORA_PROCEDURAL_INSTANCING_ENABLED)

#if UNITY_OLD_PREPROCESSOR
#error Flora Instancing requires the new shader preprocessor. Please enable Caching Preprocessor in the Editor settings!
#endif

// Config defines
// ==========================================================================================
// #define FLORA_CONFIG_DISABLE_INSTANCED_PROPERTIES

/*
Here's a bit of python code to generate these repetitive typespecs without
a lot of C macro magic

def print_dots_instancing_typespecs(elem_type, id_char, elem_size):
    print(f"#define FLORA_INSTANCING_TYPESPEC_{elem_type} {id_char}{elem_size}")
    for y in range(1, 5):
        for x in range(1, 5):
            rows = "" if y == 1 else f"x{y}"
            size = elem_size * x * y
            print(f"#define FLORA_INSTANCING_TYPESPEC_{elem_type}{x}{rows} {id_char}{size}")

for t, c, sz in (
        ('float', 'F', 4),
        ('int',   'I', 4),
        ('uint',  'U', 4),
        ('half',  'H', 2)
        ):
    print_dots_instancing_typespecs(t, c, sz)
*/

#define FLORA_INSTANCING_TYPESPEC_float F4
#define FLORA_INSTANCING_TYPESPEC_float1 F4
#define FLORA_INSTANCING_TYPESPEC_float2 F8
#define FLORA_INSTANCING_TYPESPEC_float3 F12
#define FLORA_INSTANCING_TYPESPEC_float4 F16
#define FLORA_INSTANCING_TYPESPEC_float1x2 F8
#define FLORA_INSTANCING_TYPESPEC_float2x2 F16
#define FLORA_INSTANCING_TYPESPEC_float3x2 F24
#define FLORA_INSTANCING_TYPESPEC_float4x2 F32
#define FLORA_INSTANCING_TYPESPEC_float1x3 F12
#define FLORA_INSTANCING_TYPESPEC_float2x3 F24
#define FLORA_INSTANCING_TYPESPEC_float3x3 F36
#define FLORA_INSTANCING_TYPESPEC_float4x3 F48
#define FLORA_INSTANCING_TYPESPEC_float1x4 F16
#define FLORA_INSTANCING_TYPESPEC_float2x4 F32
#define FLORA_INSTANCING_TYPESPEC_float3x4 F48
#define FLORA_INSTANCING_TYPESPEC_float4x4 F64
#define FLORA_INSTANCING_TYPESPEC_int I4
#define FLORA_INSTANCING_TYPESPEC_int1 I4
#define FLORA_INSTANCING_TYPESPEC_int2 I8
#define FLORA_INSTANCING_TYPESPEC_int3 I12
#define FLORA_INSTANCING_TYPESPEC_int4 I16
#define FLORA_INSTANCING_TYPESPEC_int1x2 I8
#define FLORA_INSTANCING_TYPESPEC_int2x2 I16
#define FLORA_INSTANCING_TYPESPEC_int3x2 I24
#define FLORA_INSTANCING_TYPESPEC_int4x2 I32
#define FLORA_INSTANCING_TYPESPEC_int1x3 I12
#define FLORA_INSTANCING_TYPESPEC_int2x3 I24
#define FLORA_INSTANCING_TYPESPEC_int3x3 I36
#define FLORA_INSTANCING_TYPESPEC_int4x3 I48
#define FLORA_INSTANCING_TYPESPEC_int1x4 I16
#define FLORA_INSTANCING_TYPESPEC_int2x4 I32
#define FLORA_INSTANCING_TYPESPEC_int3x4 I48
#define FLORA_INSTANCING_TYPESPEC_int4x4 I64
#define FLORA_INSTANCING_TYPESPEC_uint U4
#define FLORA_INSTANCING_TYPESPEC_uint1 U4
#define FLORA_INSTANCING_TYPESPEC_uint2 U8
#define FLORA_INSTANCING_TYPESPEC_uint3 U12
#define FLORA_INSTANCING_TYPESPEC_uint4 U16
#define FLORA_INSTANCING_TYPESPEC_uint1x2 U8
#define FLORA_INSTANCING_TYPESPEC_uint2x2 U16
#define FLORA_INSTANCING_TYPESPEC_uint3x2 U24
#define FLORA_INSTANCING_TYPESPEC_uint4x2 U32
#define FLORA_INSTANCING_TYPESPEC_uint1x3 U12
#define FLORA_INSTANCING_TYPESPEC_uint2x3 U24
#define FLORA_INSTANCING_TYPESPEC_uint3x3 U36
#define FLORA_INSTANCING_TYPESPEC_uint4x3 U48
#define FLORA_INSTANCING_TYPESPEC_uint1x4 U16
#define FLORA_INSTANCING_TYPESPEC_uint2x4 U32
#define FLORA_INSTANCING_TYPESPEC_uint3x4 U48
#define FLORA_INSTANCING_TYPESPEC_uint4x4 U64
#define FLORA_INSTANCING_TYPESPEC_half H2
#define FLORA_INSTANCING_TYPESPEC_half1 H2
#define FLORA_INSTANCING_TYPESPEC_half2 H4
#define FLORA_INSTANCING_TYPESPEC_half3 H6
#define FLORA_INSTANCING_TYPESPEC_half4 H8
#define FLORA_INSTANCING_TYPESPEC_half1x2 H4
#define FLORA_INSTANCING_TYPESPEC_half2x2 H8
#define FLORA_INSTANCING_TYPESPEC_half3x2 H12
#define FLORA_INSTANCING_TYPESPEC_half4x2 H16
#define FLORA_INSTANCING_TYPESPEC_half1x3 H6
#define FLORA_INSTANCING_TYPESPEC_half2x3 H12
#define FLORA_INSTANCING_TYPESPEC_half3x3 H18
#define FLORA_INSTANCING_TYPESPEC_half4x3 H24
#define FLORA_INSTANCING_TYPESPEC_half1x4 H8
#define FLORA_INSTANCING_TYPESPEC_half2x4 H16
#define FLORA_INSTANCING_TYPESPEC_half3x4 H24
#define FLORA_INSTANCING_TYPESPEC_half4x4 H32
#define FLORA_INSTANCING_TYPESPEC_min16float H2
#define FLORA_INSTANCING_TYPESPEC_min16float4 H8
#define FLORA_INSTANCING_TYPESPEC_SH F128

static const int kFloraInstancedPropOverrideDisabled  = 0;
static const int kFloraInstancedPropOverrideSupported = 1;
static const int kFloraInstancedPropOverrideRequired  = 2;

#define FLORA_INSTANCING_CONCAT2(a, b) a ## b
#define FLORA_INSTANCING_CONCAT4(a, b, c, d) a ## b ## c ## d
#define FLORA_INSTANCING_CONCAT_WITH_METADATA(metadata_prefix, typespec, name) FLORA_INSTANCING_CONCAT4(metadata_prefix, typespec, _Metadata, name)

// Metadata constants for properties have the following name format:
// flora_ProceduralInstancing<Type><Size>_Metadata<Name>
// where
// <Type> is a single character element type specifier (e.g. F for float4x4)
//          F = float, I = int, U = uint, H = half
// <Size> is the total size of the property in bytes (e.g. 64 for float4x4)
// <Name> is the name of the property
// NOTE: There is no underscore between 'Metadata' and <Name> to avoid a double
//       underscore in the common case where the property name starts with an underscore.
//       A prefix double underscore is illegal on some platforms like OpenGL.
#define FLORA_INSTANCED_METADATA_NAME(type, name) FLORA_INSTANCING_CONCAT_WITH_METADATA(flora_ProceduralInstancing, FLORA_INSTANCING_CONCAT2(FLORA_INSTANCING_TYPESPEC_, type), name)
#define FLORA_INSTANCED_PROP_OVERRIDE_MODE_NAME(name) FLORA_INSTANCING_CONCAT2(name, _FloraInstancingOverrideMode)

#define FLORA_INSTANCING_START(name) cbuffer UnityFloraInstancing_##name {
#define FLORA_INSTANCING_END(name)   }

#define FLORA_INSTANCED_PROP_OVERRIDE_DISABLED(type, name) static const uint FLORA_INSTANCED_METADATA_NAME(type, name) = 0; \
static const int FLORA_INSTANCED_PROP_OVERRIDE_MODE_NAME(name) = kFloraInstancedPropOverrideDisabled;

#define FLORA_INSTANCED_PROP_OVERRIDE_SUPPORTED(type, name) uint FLORA_INSTANCED_METADATA_NAME(type, name); \
static const int FLORA_INSTANCED_PROP_OVERRIDE_MODE_NAME(name) = kFloraInstancedPropOverrideSupported;

#define FLORA_INSTANCED_PROP_OVERRIDE_REQUIRED(type, name) uint FLORA_INSTANCED_METADATA_NAME(type, name); \
static const int FLORA_INSTANCED_PROP_OVERRIDE_MODE_NAME(name) = kFloraInstancedPropOverrideRequired;

#ifdef FLORA_CONFIG_DISABLE_INSTANCED_PROPERTIES
#define FLORA_INSTANCED_PROP(type, name) FLORA_INSTANCED_PROP_OVERRIDE_DISABLED(type, name)
#else
#define FLORA_INSTANCED_PROP(type, name) FLORA_INSTANCED_PROP_OVERRIDE_SUPPORTED(type, name)
#endif

#define FLORA_INSTANCED_PROP_IS_OVERRIDE_DISABLED(name) (FLORA_INSTANCED_PROP_OVERRIDE_MODE_NAME(name) == kFloraInstancedPropOverrideDisabled)
#define FLORA_INSTANCED_PROP_IS_OVERRIDE_ENABLED(name) (FLORA_INSTANCED_PROP_OVERRIDE_MODE_NAME(name) == kFloraInstancedPropOverrideSupported)
#define FLORA_INSTANCED_PROP_IS_OVERRIDE_REQUIRED(name) (FLORA_INSTANCED_PROP_OVERRIDE_MODE_NAME(name) == kFloraInstancedPropOverrideRequired)

#define FLORA_ACCESS_INSTANCED_PROP(type, var) ( /* Compile-time branches */ \
FLORA_INSTANCED_PROP_IS_OVERRIDE_ENABLED(var) ? LoadFloraInstancedData_##type(FLORA_INSTANCED_METADATA_NAME(type, var)) \
: FLORA_INSTANCED_PROP_IS_OVERRIDE_REQUIRED(var) ? LoadFloraInstancedDataOverridden_##type(FLORA_INSTANCED_METADATA_NAME(type, var)) \
: ((type)0) \
)

#define FLORA_ACCESS_INSTANCED_PROP_WITH_DEFAULT(type, var) ( /* Compile-time branches */ \
FLORA_INSTANCED_PROP_IS_OVERRIDE_ENABLED(var) ? LoadFloraInstancedData_##type(var, FLORA_INSTANCED_METADATA_NAME(type, var)) \
: FLORA_INSTANCED_PROP_IS_OVERRIDE_REQUIRED(var) ? LoadFloraInstancedDataOverridden_##type(FLORA_INSTANCED_METADATA_NAME(type, var)) \
: (var) \
)

#define FLORA_ACCESS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(type, var, default_value) ( /* Compile-time branches */ \
FLORA_INSTANCED_PROP_IS_OVERRIDE_ENABLED(var) ? LoadFloraInstancedData_##type(default_value, FLORA_INSTANCED_METADATA_NAME(type, var)) \
: FLORA_INSTANCED_PROP_IS_OVERRIDE_REQUIRED(var) ? LoadFloraInstancedDataOverridden_##type(FLORA_INSTANCED_METADATA_NAME(type, var)) \
: (default_value) \
)

#define FLORA_ACCESS_AND_TRADITIONAL_INSTANCED_PROP(type, arr, var) FLORA_ACCESS_INSTANCED_PROP(type, var)
#define FLORA_ACCESS_AND_TRADITIONAL_INSTANCED_PROP_WITH_DEFAULT(type, arr, var) FLORA_ACCESS_INSTANCED_PROP_WITH_DEFAULT(type, var)
#define FLORA_ACCESS_AND_TRADITIONAL_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(type, arr, var, default_value) FLORA_ACCESS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(type, var, default_value)

#endif // !defined(FLORA_PROCEDURAL_INSTANCING_ENABLED)

#endif