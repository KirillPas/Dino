// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable AssignNullToNotNullAttribute

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace MA.Flora.CodeGen
{
    class FloraILPostProcessManager : ILPostProcessor
    {
        static readonly FloraILPostProcessor[] PostProcessors =
        {
            new ShaderGraphILPostProcessor()
        };

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return PostProcessors.Any(p => p.WillProcessAssembly(compiledAssembly));
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            List<DiagnosticMessage> diagnostics = new List<DiagnosticMessage>();
            AssemblyDefinition assemblyDefinition = CodeGenUtility.AssemblyDefinitionFor(compiledAssembly);
            bool madeAnyChange = false;
            
            foreach (FloraILPostProcessor postProcessor in PostProcessors)
            {
                if (postProcessor.WillProcessAssembly(compiledAssembly))
                {
                    diagnostics.AddRange(postProcessor.PostProcess(assemblyDefinition, out bool madeChange));
                    madeAnyChange |= madeChange;
                }
            }
            
            // Hack to remove circular references
            string selfName = assemblyDefinition.Name.FullName;
            foreach (AssemblyNameReference referenceName in assemblyDefinition.MainModule.AssemblyReferences)
            {
                if (referenceName.FullName == selfName)
                {
                    assemblyDefinition.MainModule.AssemblyReferences.Remove(referenceName);
                    break;
                }
            }
            
            if (!madeAnyChange || diagnostics.Any(d => d.DiagnosticType == DiagnosticType.Error))
                return new ILPostProcessResult(null, diagnostics);
            
            MemoryStream pe = new MemoryStream();
            MemoryStream pdb = new MemoryStream();
            WriterParameters writerParameters = new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true
            };
            assemblyDefinition.Write(pe, writerParameters);
            
            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
        }
    }

    abstract class FloraILPostProcessor
    {
        protected const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        
        public IEnumerable<DiagnosticMessage> PostProcess(AssemblyDefinition assemblyDefinition, out bool madeAChange)
        {
            try
            {
                madeAChange = PostProcessAssemblyDefinition(assemblyDefinition);
            }
            catch (FoundErrorInUserCodeException e)
            {
                madeAChange = false;
                return e.DiagnosticMessages;
            }

            return DiagnosticMessages;
        }

        protected List<DiagnosticMessage> DiagnosticMessages = new List<DiagnosticMessage>();

        public abstract bool WillProcessAssembly(ICompiledAssembly compiledAssembly);
        protected abstract bool PostProcessAssemblyDefinition(AssemblyDefinition shaderGraphAssemblyDefinition);
    }
}