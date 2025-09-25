// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using MA.Flora.Rendering;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    static class InstancingShaderConfig
    {
        public const int    Version = 2;
        public const string Path    = "Packages/com.ma.flora/Config/ShaderConfig.hlsl";
        
        public const string InstanceDataBufferType_Raw            = "FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_RAW";
        public const string InstanceDataBufferType_Float4         = "FLORA_CONFIG_INSTANCE_DATA_BUFFER_TYPE_FLOAT4";
        
        public const string InstanceDataTransformPacking_Disabled = "FLORA_CONFIG_INSTANCE_DATA_TRANSFORM_PACKING_DISABLED";
        public const string InstanceDataTransformPacking_Float4x2 = "FLORA_CONFIG_INSTANCE_DATA_TRANSFORM_PACKING_FLOAT4X2";
        
        public const string DisableLegacyLightProbes              = "FLORA_CONFIG_DISABLE_LEGACY_LIGHT_PROBES";
        public const string DisableInstancedProperties            = "FLORA_CONFIG_DISABLE_INSTANCED_PROPERTIES";
        
        [InitializeOnLoadMethod]
        static void InitializeConfig()
        {
            EditorApplication.delayCall += EnsureConfig;
        }
        
        const string k_VersionDefine = "#define FLORA_INSTANCING_CONFIG_VERSION";
        const string k_HashDefine    = "#define FLORA_INSTANCING_CONFIG_HASH";
        
        static async void EnsureConfig()
        {
            string targetFilename = System.IO.Path.GetFullPath(Path);
            
            bool needsUpdate = false;
            if (File.Exists(targetFilename))
            {
                try
                {
                    using var reader = File.OpenText(targetFilename);
                    string line = await reader.ReadLineAsync();
                    while (line != null)
                    {
                        if (line.Contains(k_HashDefine))
                        {
                            int hash = int.Parse(line.Split(' ')[2].Trim('(', ')'));
                            needsUpdate = hash != GenerateConfigHash();
                            break;
                        }
                        else if (line.Contains(k_VersionDefine))
                        {
                            int version = int.Parse(line.Split(' ')[2].Trim('(', ')'));
                            needsUpdate = version != Version;
                            break;
                        }
                        line = await reader.ReadLineAsync();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            else
            {
                needsUpdate = true;
            }
            
            if (needsUpdate)
            {
                await WriteInstancingConfig();
                AssetDatabase.Refresh();
            }
        }
        
        internal static async Task WriteInstancingConfig()
        {
            string targetFilename = System.IO.Path.GetFullPath(Path);
            
            // Check access to the file
            if (File.Exists(targetFilename))
            {
                FileInfo info;
                try
                {
                    info = new FileInfo(targetFilename);
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.Log("Access to " + targetFilename + " is denied. Skipping it.");
                    return;
                }
                catch (SecurityException)
                {
                    Debug.Log("You do not have permission to access " + targetFilename + ". Skipping it.");
                    return;
                }

                if (info?.IsReadOnly ?? false)
                {
                    Debug.Log(targetFilename + " is ReadOnly. Skipping it.");
                    return;
                }
            }
            
            // Generate content
            await using var writer = File.CreateText(targetFilename);
            writer.NewLine = Environment.NewLine;
            
            await writer.WriteLineAsync("// Copyright \u00a9 Magnetic Arcade. All Rights Reserved.");
            await writer.WriteLineAsync("// This file was automatically generated. Please don't edit by hand.");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("#ifndef FLORA_INSTANCING_CONFIG_INCLUDED");
            await writer.WriteLineAsync("#define FLORA_INSTANCING_CONFIG_INCLUDED");
            await writer.WriteLineAsync();
            
            await writer.WriteLineAsync("// Instancing configuration version");
            await writer.WriteLineAsync($"{k_VersionDefine} ({Version})");
            await writer.WriteLineAsync($"{k_HashDefine} ({GenerateConfigHash()})");
            await writer.WriteLineAsync();
            
            await writer.WriteLineAsync("// Instancing configuration");
            switch (InstancingProjectSettings.InstanceBufferType)
            {
                case InstanceBufferType.Raw:
                    await writer.WriteLineAsync($"#define {InstanceDataBufferType_Raw}");
                    break;
                case InstanceBufferType.Float4:
                    await writer.WriteLineAsync($"#define {InstanceDataBufferType_Float4}");
                    break;
            }
            
            switch (InstancingProjectSettings.InstanceTransformPackingMode)
            {
                case InstanceTransformPackingMode.Disabled:
                    await writer.WriteLineAsync($"#define {InstanceDataTransformPacking_Disabled}");
                    break;
                case InstanceTransformPackingMode.Float4x2:
                    await writer.WriteLineAsync($"#define {InstanceDataTransformPacking_Float4x2}");
                    break;
            }
            
            if (InstancingProjectSettings.DisableInstancedPropertySupport)
                await writer.WriteLineAsync($"#define {DisableLegacyLightProbes}");
            
            if (InstancingProjectSettings.DisableInstancedPropertySupport)
                await writer.WriteLineAsync($"#define {DisableInstancedProperties}");
            
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("#endif // FLORA_INSTANCING_CONFIG_INCLUDED");
        }
        
        static int GenerateConfigHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + InstancingProjectSettings.InstanceBufferType.GetHashCode();
                hash = hash * 31 + InstancingProjectSettings.InstanceTransformPackingMode.GetHashCode();
                hash = hash * 31 + InstancingProjectSettings.DisableLegacyLightProbeSupport.GetHashCode();
                hash = hash * 31 + InstancingProjectSettings.DisableInstancedPropertySupport.GetHashCode();
                return hash;
            }
        }
    }
}