// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

#if HAS_PACKAGE_SHADERGRAPH
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MA.Flora.CodeGen.Forwarding;
using UnityEditor;
using UnityEditor.Rendering.BuiltIn.ShaderGraph;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;

namespace MA.Flora.Editor.ShaderGraph
{
    static class Generator
    {
        internal static string GenerateSource(string assetPath)
            => ShaderGraphImporter.GetShaderText(assetPath, out _, null, out _);

        const BindingFlags k_AllMembers = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

        static FieldInfo s_Generator_Targets_Field;
        static FieldInfo s_PragmaCollection_Items_Field;
        static FieldInfo s_PropertyCollector_Properties_Field;

        [InitializeOnLoadMethod]
        static void InitializeIncludeHelpers()
        {
            s_Generator_Targets_Field = typeof(UnityEditor.ShaderGraph.Generator).GetField("m_Targets", k_AllMembers);
            s_PragmaCollection_Items_Field = typeof(PragmaCollection).GetField("m_Items", k_AllMembers);
            s_PropertyCollector_Properties_Field = typeof(PropertyCollector).GetField("m_Properties", k_AllMembers);
            ShaderGraphEvents.PreGenerateShaderPass = OnPreGenerateShaderPass;
        }

        static readonly KeywordDescriptor DebugDisplayKeyword = new KeywordDescriptor
        {
            displayName = "Debug Display",
            referenceName = "DEBUG_DISPLAY",
            type = KeywordType.Boolean,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            stages = KeywordShaderStage.Fragment,
        };

        static readonly HashSet<string> k_ValidSubTargets = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "BuiltInLitSubTarget",
            "BuiltInUnlitSubTarget",
            "UniversalLitSubTarget",
            "UniversalUnlitSubTarget",
            "FabricSubTarget",
            "HDLitSubTarget",
            "HDUnlitSubTarget",
            "HairSubTarget",
            "EyeSubTarget",
            "StackLitSubTarget",
        };

        static readonly HashSet<string> k_InvalidSubTargets = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "UniversalSpriteCustomLitSubTarget",
            "UniversalSpriteLitSubTarget",
            "UniversalSpriteUnlitSubTarget",
            "UniversalDecalSubTarget",
            "UniversalFullscreenSubTarget",
            "DecalSubTarget",
            "FogVolumeSubTarget",
            "WaterSubTarget",
            "VFXSubTarget",
        };

        static readonly HashSet<string> k_InvalidPassNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "Meta",
        };

        static readonly HashSet<string> k_InvalidShaderPassNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "SHADERPASS_META",
        };

        static readonly HashSet<string> k_SelectionPassNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "SceneSelectionPass",
            "ScenePickingPass"
        };

        static class Pragmas
        {
            public static PragmaDescriptor MultiCompileInstancing => new PragmaDescriptor { value = "multi_compile_instancing" };
            public static PragmaDescriptor ProceduralInstancing => new PragmaDescriptor { value = "multi_compile _ PROCEDURAL_INSTANCING_ON" };
            public static PragmaDescriptor ProceduralOptions => new PragmaDescriptor { value = "instancing_options procedural:SetupFloraInstancingData forwardadd" };
        }

        static object OnPreGenerateShaderPass(object generator, int passIndex, object boxedPass, object activeFields, object blockFieldDescriptors, object propertyCollector)
        {
            if (generator is UnityEditor.ShaderGraph.Generator gen)
            {
                var pass = (PassDescriptor)boxedPass;
                GenerateShaderPass(gen, passIndex, ref pass, (ActiveFields)activeFields, (List<BlockFieldDescriptor>)(blockFieldDescriptors), (PropertyCollector)propertyCollector);
                return pass;
            }

            return null;
        }

        static void GenerateShaderPass(
            UnityEditor.ShaderGraph.Generator generator,
            int targetIndex,
            ref PassDescriptor pass,
            ActiveFields activeFields,
            List<BlockFieldDescriptor> currentBlockDescriptors,
            PropertyCollector subShaderProperties)
        {
#if !FLORA_DISABLE_SHADER_GRAPH_INJECTION
            if (s_Generator_Targets_Field.GetValue(generator) is not IList targets)
                return;

            var target = targets[targetIndex];
            if (target == null)
                return;

            var targetType = target.GetType();
            var activeSubTargetProperty = targetType.GetProperty("activeSubTarget", k_AllMembers);
            if (activeSubTargetProperty != null)
            {
                var activeSubTarget = activeSubTargetProperty.GetValue(target);
                if (activeSubTarget != null)
                {
                    var subTargetName = activeSubTarget.GetType().Name;
                    if (k_InvalidSubTargets.Contains(subTargetName))
                        return;
                }
            }

            if (k_InvalidPassNames.Contains(pass.displayName) ||
                k_InvalidShaderPassNames.Contains(pass.referenceName))
            {
                return;
            }

            if (target is BuiltInTarget)
            {
                pass.keywords = new KeywordCollection { pass.keywords ?? new KeywordCollection(), DebugDisplayKeyword };
            }

            // Include collection de-duplicates includes, so it's safe to add the same include multiple times
            pass.includes = new IncludeCollection { pass.includes ?? new IncludeCollection() };
#if UNITY_2022_2_OR_NEWER
            pass.includes.Add("Packages/com.ma.flora/ShaderLibrary/Instancing.hlsl", IncludeLocation.Pregraph, true);
#else
            pass.includes.Add("Packages/com.ma.flora/ShaderLibrary/Instancing.hlsl", IncludeLocation.Pregraph);
#endif

            pass.pragmas = new PragmaCollection { pass.pragmas ?? new PragmaCollection() };
            var pragmaItems = (List<PragmaCollection.Item>)s_PragmaCollection_Items_Field.GetValue(pass.pragmas);

            var indexOfInstancingOptions = pragmaItems.FindIndex(item => item.value.Contains("SetupFloraInstancingData"));
            if (indexOfInstancingOptions == -1)
            {
                pass.pragmas.Add(Pragmas.ProceduralOptions);
            }

            bool hasInstancingPragma = pragmaItems.FindIndex(item => item.value.Contains("multi_compile_instancing")) != -1 ||
                                       pragmaItems.FindIndex(item => item.value.Contains("multi_compile _ PROCEDURAL_INSTANCING_ON")) != -1;
            if (!hasInstancingPragma)
            {
#if UNITY_2022_2_OR_NEWER
                // Note: The instancing header includes the multi_compile with include_with_pragmas for selection passes, so don't add it again here
                // Use multi_compile _ PROCEDURAL_INSTANCING_ON instead of multi_compile_instancing to avoid an additional instancing variant
                if (!k_SelectionPassNames.Contains(pass.displayName))
                    pass.pragmas.Add(Pragmas.ProceduralInstancing);
#else
                pass.pragmas.Add(Pragmas.ProceduralInstancing);
#endif

                pass.requiredFields = new FieldCollection { pass.requiredFields ?? new FieldCollection() };
                pass.requiredFields.Add(StructFields.Attributes.instanceID);
                pass.requiredFields.Add(StructFields.Varyings.instanceID);
            }

            var properties = (List<AbstractShaderProperty>)s_PropertyCollector_Properties_Field.GetValue(subShaderProperties);

            var indexOfInstancedPropertyBlock = properties.FindIndex(property => property is ProceduralInstancedPropertyBlock);
            if (indexOfInstancedPropertyBlock == -1)
            {
                var hybridPerInstanceProperties = new List<AbstractShaderProperty>();

                foreach (var property in properties)
                {
                    if (property.overrideHLSLDeclaration && property.hlslDeclarationOverride == HLSLDeclaration.HybridPerInstance)
                    {
                        hybridPerInstanceProperties.Add(property);
                    }
                }

                if (hybridPerInstanceProperties.Count > 0)
                {
                    var instancedPropertyBlock = new ProceduralInstancedPropertyBlock(hybridPerInstanceProperties);
                    properties.Add(instancedPropertyBlock);

                }
            }
#endif
        }
    }
}
#endif
