// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;

namespace MA.Flora.Editor.ShaderGraph
{
    class ProceduralInstancedPropertyBlock : AbstractShaderProperty
    {
        struct PropertyDescriptor
        {
            public string name;
            public string type;
        }

        List<AbstractShaderProperty> m_HybridProperties;

        public ProceduralInstancedPropertyBlock(List<AbstractShaderProperty> hybridProperties)
        {
            m_HybridProperties = hybridProperties;
            overrideReferenceName = "_FloraInstancingData";
        }

        internal override bool isExposable => false;
        internal override bool isRenamable => false;

        internal override ShaderInput Copy() => new ProceduralInstancedPropertyBlock(m_HybridProperties);

        public override PropertyType propertyType => PropertyType.Float;

        internal override void ForeachHLSLProperty(Action<HLSLProperty> action)
        {
            action(new HLSLProperty(HLSLType._CUSTOM, "FloraMetadata", HLSLDeclaration.Global)
            {
                customDeclaration = OnBuildHLSLString
            });
        }

        void OnBuildHLSLString(ShaderStringBuilder builder)
        {
            List<PropertyDescriptor> properties = BuildFloraProperties();
            builder.AppendNewLine();

            builder.AppendLine("// --- Flora Properties Begin");
            if (properties.Count > 0)
            {
                builder.AppendNewLine();

                builder.AppendLine("#if defined(FLORA_PROCEDURAL_INSTANCING_ENABLED)");
                builder.AppendLine("// Flora instanced property definitions");
                builder.AppendLine("FLORA_INSTANCING_START(MaterialPropertyMetadata)");
                foreach (PropertyDescriptor property in properties)
                {
                    builder.AppendLine($"    FLORA_INSTANCED_PROP({property.type}, {property.name})");
                }
                builder.AppendLine("FLORA_INSTANCING_END(MaterialPropertyMetadata)");
                builder.AppendNewLine();

                builder.AppendLine("// Flora instanced property cache");
                foreach (PropertyDescriptor property in properties)
                {
                    builder.AppendLine($"static {property.type} {GetSampledName(property)};");
                }
                builder.AppendNewLine();

                builder.AppendLine("void SetupFloraShaderGraphMaterialPropertyCaches()");
                builder.AppendLine("{");
                foreach (PropertyDescriptor property in properties)
                {
                    builder.AppendLine($"    {GetSampledName(property)} = FLORA_ACCESS_INSTANCED_PROP_WITH_DEFAULT({property.type}, {property.name});");
                }
                builder.AppendLine("}");

                builder.AppendNewLine();
                builder.AppendLine("#undef FLORA_SETUP_MATERIAL_PROPERTY_CACHES");
                builder.AppendLine("#define FLORA_SETUP_MATERIAL_PROPERTY_CACHES() SetupFloraShaderGraphMaterialPropertyCaches()");
                builder.AppendNewLine();
                builder.AppendLine("// Flora instanced property macros");
                foreach (PropertyDescriptor property in properties)
                {
                    builder.AppendLine($"#define {property.name} {GetSampledName(property)}");
                }
                builder.AppendLine("#endif // FLORA_PROCEDURAL_INSTANCING_ENABLED");
            }
            else
            {
                builder.AppendLine("// MaterialPropertyMetadata: None");
            }
            builder.AppendLine("// --- Flora Properties End");
        }

        List<PropertyDescriptor> BuildFloraProperties()
        {
            List<PropertyDescriptor> properties = new List<PropertyDescriptor>();
            foreach (AbstractShaderProperty property in m_HybridProperties)
            {
                if (property.overrideHLSLDeclaration &&
                    property.hlslDeclarationOverride == HLSLDeclaration.HybridPerInstance)
                {
                    string valueType = "";
                    switch (property)
                    {
                        case Vector1ShaderProperty v1:
                            valueType = v1.floatType == FloatType.Integer ? "int" : "float";
                            break;
                        case Vector2ShaderProperty:
                            valueType = "float2";
                            break;
                        case Vector3ShaderProperty:
                            valueType = "float3";
                            break;
                        case Vector4ShaderProperty:
                        case ColorShaderProperty:
                            valueType = "float4";
                            break;
                        case Matrix2ShaderProperty:
                            valueType = "float2x2";
                            break;
                        case Matrix3ShaderProperty:
                            valueType = "float3x3";
                            break;
                        case Matrix4ShaderProperty:
                            break;
                    }

                    if (!string.IsNullOrEmpty(valueType))
                        properties.Add(new PropertyDescriptor
                        {
                            name = property.referenceName,
                            type = valueType
                        });
                }
            }

            return properties;
        }

        static string GetSampledName(PropertyDescriptor propertyDescriptor) => $"flora_Sampled{propertyDescriptor.name}";

        internal override string GetPropertyAsArgumentString(string precisionString) => "";

        internal override AbstractMaterialNode ToConcreteNode() => new Vector1Node();

        internal override PreviewProperty GetPreviewMaterialProperty() => new PreviewProperty(propertyType) { floatValue = 0 };
    }
}
