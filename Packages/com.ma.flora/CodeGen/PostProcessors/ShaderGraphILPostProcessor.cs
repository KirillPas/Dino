// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Linq;
using System.Reflection;
using MA.Flora.CodeGen.Forwarding;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace MA.Flora.CodeGen
{
    sealed class ShaderGraphILPostProcessor : FloraILPostProcessor
    {
        public override bool WillProcessAssembly(ICompiledAssembly compiledAssembly)
        {
            return compiledAssembly.Name == "Unity.ShaderGraph.Editor";
        }

        protected override bool PostProcessAssemblyDefinition(AssemblyDefinition shaderGraphAssemblyDefinition)
        {
            TypeDefinition generatorType = shaderGraphAssemblyDefinition.MainModule.Types.FirstOrDefault(t => t.FullName == "UnityEditor.ShaderGraph.Generator");
            if (generatorType == null)
            {
                DiagnosticMessages.AddError($"ShaderGraphILPostProcessor: Could not find the Generator type in the assembly: {shaderGraphAssemblyDefinition.FullName}.");
                return false;
            }

            MethodDefinition generatorGenerateShaderPassMethod = generatorType.Methods.FirstOrDefault(m => m.Name == "GenerateShaderPass");
            if (generatorGenerateShaderPassMethod == null)
            {
                DiagnosticMessages.AddError($"ShaderGraphILPostProcessor: Could not find the GenerateShaderPass method in the type: {generatorType.FullName}.");
                return false;
            }

            MethodInfo forwardMethod = typeof(ShaderGraphEvents).GetMethods(AllMembers).FirstOrDefault(m => m.Name == "ForwardPreGenerateShaderPass");
            if (forwardMethod == null)
            {
                DiagnosticMessages.AddError($"ShaderGraphILPostProcessor: Could not find the ForwardPreGenerateShaderPass method in the type: {typeof(ShaderGraphEvents).FullName}.");
                return false;
            }

            MethodReference forwardMethodRef = shaderGraphAssemblyDefinition.MainModule.ImportReference(forwardMethod);
            ILProcessor processor = generatorGenerateShaderPassMethod.Body.GetILProcessor();
            Collection<Instruction> instructions = generatorGenerateShaderPassMethod.Body.Instructions;

            // Ensure InitLocals is set to true
            generatorGenerateShaderPassMethod.Body.InitLocals = true;

            // Create local variables for the boxed passDescriptor, the returned object, and the unboxed passDescriptor
            VariableDefinition boxedPassVar = new VariableDefinition(shaderGraphAssemblyDefinition.MainModule.ImportReference(typeof(object)));
            VariableDefinition returnedPassVar = new VariableDefinition(shaderGraphAssemblyDefinition.MainModule.ImportReference(typeof(object)));
            VariableDefinition comparisonResultVar = new VariableDefinition(shaderGraphAssemblyDefinition.MainModule.ImportReference(typeof(bool)));
            generatorGenerateShaderPassMethod.Body.Variables.Add(boxedPassVar);
            generatorGenerateShaderPassMethod.Body.Variables.Add(returnedPassVar);
            generatorGenerateShaderPassMethod.Body.Variables.Add(comparisonResultVar);

            // Insert instructions to box the passDescriptor parameter and store it in the local variable
            Instruction firstInstruction = instructions[0];
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldarg_2)); // Load passDescriptor
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Box, generatorGenerateShaderPassMethod.Parameters[1].ParameterType)); // Box the struct
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Stloc, boxedPassVar)); // Store it in the local variable

            // Insert instructions to call EditorForwarders.ShaderGraphEvents.ForwardPreGenerateShaderPass
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldarg_0)); // Load 'this'
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldarg_1)); // Load passIndex
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldloc, boxedPassVar)); // Load the boxed passDescriptor
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldarg_3)); // Load activeFields
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldarg, 4)); // Load blockFieldDescriptors
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldarg, 5)); // Load propertyCollector
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Call, forwardMethodRef)); // Call the pre-hook method
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Stloc, returnedPassVar)); // Store the returned object

            // Insert instructions to check if the returned object is null
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldloc, returnedPassVar)); // Load the returned object
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldnull)); // Load null
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ceq)); // Compare for equality
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldc_I4_0)); // Load 0
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ceq)); // Compare for inequality (effectively !Ceq)
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Stloc, comparisonResultVar)); // Store the result of the comparison

            Instruction afterNullCheck = processor.Create(OpCodes.Nop);
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldloc, comparisonResultVar)); // Load the comparison result
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Brfalse_S, afterNullCheck)); // Branch if false (null)

            // Insert instructions to unbox the returned object and store it back into the passDescriptor parameter
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Ldloc, returnedPassVar)); // Load the returned object
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Unbox_Any, generatorGenerateShaderPassMethod.Parameters[1].ParameterType)); // Unbox to passDescriptor type
            processor.InsertBefore(firstInstruction, processor.Create(OpCodes.Starg_S, (byte)2)); // Store the value back into passDescriptor

            processor.InsertBefore(firstInstruction, afterNullCheck); // Mark the end of the null check

            return true;
        }
    }
}
