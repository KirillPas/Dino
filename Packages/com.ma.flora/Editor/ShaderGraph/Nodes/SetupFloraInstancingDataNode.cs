// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor.Graphing;
using UnityEditor.Rendering;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;

namespace MA.Flora.Editor.ShaderGraph
{
#if FLORA_DISABLE_SHADER_GRAPH_INJECTION
    [Title(Category, Name)]
#endif
    sealed class SetupFloraInstancingDataNode : AbstractMaterialNode, IGeneratesBodyCode, IMayRequirePosition
    {
        public const string Category = "Flora";
        public const string Name     = "Setup Flora Instancing Data";

        public const string InputSlotName  = "Vertex Position";
        public const string OutputSlotName = "Out";

        public const int InputSlotId  = 0;
        public const int OutputSlotId = 1;

        public SetupFloraInstancingDataNode()
        {
            name = Name;
            precision = Precision.Single;
            synonyms = new [] { "flora", "instancing", "instanced", "procedural" };
            UpdateNodeAfterDeserialization();
        }

        public override bool hasPreview => false;

        public override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new PositionMaterialSlot(InputSlotId, InputSlotName, InputSlotName, CoordinateSpace.Object, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(OutputSlotId, OutputSlotName, OutputSlotName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));

            RemoveSlotsNameNotMatching(new[] { InputSlotId, OutputSlotId });
        }

        public override void CollectShaderProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            base.CollectShaderProperties(properties, generationMode);

#if FLORA_DISABLE_SHADER_GRAPH_INJECTION
            if (generationMode == GenerationMode.ForReals)
            {
                List<AbstractShaderProperty> hybridPerInstanceProperties = new List<AbstractShaderProperty>();

                foreach (AbstractShaderProperty property in properties.properties)
                {
                    if (property.overrideHLSLDeclaration &&
                        property.hlslDeclarationOverride == HLSLDeclaration.HybridPerInstance)
                    {
                        hybridPerInstanceProperties.Add(property);
                    }
                }

                ProceduralInstancedPropertyBlock proceduralInstancingBlock = new ProceduralInstancedPropertyBlock(hybridPerInstanceProperties);
                properties.AddShaderProperty(proceduralInstancingBlock);
            }
#endif
        }

        public override void ValidateNode()
        {
            base.ValidateNode();

#if !FLORA_DISABLE_SHADER_GRAPH_INJECTION
            owner.AddValidationError(objectId, "Automatic ShaderGraph injection is enabled, this node will be ignored.", ShaderCompilerMessageSeverity.Warning);
#endif
        }

        public NeededCoordinateSpace RequiresPosition(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            return stageCapability is ShaderStageCapability.All or ShaderStageCapability.Vertex
                ? NeededCoordinateSpace.Object
                : NeededCoordinateSpace.None;
        }

        public void GenerateNodeCode(ShaderStringBuilder sb, GenerationMode generationMode)
        {
            var outputName = GetVariableNameForSlot(OutputSlotId);
            var inputValue = GetSlotValue(InputSlotId, generationMode);
            sb.AppendLine("float3 {0} = {1};", outputName, inputValue);
        }
    }
}
