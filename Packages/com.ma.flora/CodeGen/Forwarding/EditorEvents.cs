// Copyright © Magnetic Arcade. All Rights Reserved.

namespace MA.Flora.CodeGen.Forwarding
{
    /// Avoiding circular references between generated code and Unity.ShaderGraph.Editor.
    public static class ShaderGraphEvents
    {
        public delegate object PreGenerateShaderPassDelegate(object generator, int passIndex, object passDescriptor, object activeFields, object blockFieldDescriptors, object propertyCollector);

        public static PreGenerateShaderPassDelegate PreGenerateShaderPass;

        internal static object ForwardPreGenerateShaderPass(object generator, int passIndex, object passDescriptor, object activeFields, object blockFieldDescriptors, object propertyCollector)
        {
            return PreGenerateShaderPass?.Invoke(generator, passIndex, passDescriptor, activeFields, blockFieldDescriptors, propertyCollector);
        }
    }
}
