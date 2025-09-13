// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace MA.Flora.CodeGen
{
    class PostProcessorReflectionImporterProvider : IReflectionImporterProvider
    {
        public IReflectionImporter GetReflectionImporter(ModuleDefinition module)
        {
            return new PostProcessorReflectionImporter(module);
        }
    }
    
    class PostProcessorReflectionImporter : DefaultReflectionImporter
    {
        const string SystemPrivateCoreLib = "System.Private.CoreLib";

        AssemblyNameReference m_CorrectCorlib;

        public PostProcessorReflectionImporter(ModuleDefinition module) : base(module)
        {
            m_CorrectCorlib = module.AssemblyReferences.FirstOrDefault(a => a.Name is "mscorlib" or "netstandard" or SystemPrivateCoreLib);
        }

        public override AssemblyNameReference ImportReference(AssemblyName reference)
        {
            if (m_CorrectCorlib != null && reference.Name == SystemPrivateCoreLib)
                return m_CorrectCorlib;

            return base.ImportReference(reference);
        }
    }
}