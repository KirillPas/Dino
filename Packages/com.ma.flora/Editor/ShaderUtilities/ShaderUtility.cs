// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MA.Flora.Editor
{
    static class ShaderUtility
    {
        static readonly Regex s_IncludeRegex = new Regex(@"#include\s+""(.*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

        public static string FixRelativePaths(string sourceCode, string directory)
        {
            // Normalize directory once before using it
            directory = directory.Replace("\\", "/").Trim('/');
            // Replace all relative paths with absolute paths
            return s_IncludeRegex.Replace(sourceCode, match => MakePathAbsolute(match, directory));
        }

        static bool IsRelative(string path)
        {
            if (path.StartsWith("/", StringComparison.InvariantCultureIgnoreCase) || 
                path.StartsWith(".", StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }

            return !s_UnityPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase))
                && !s_BuiltInIncludes.Contains(path);
        }

        static string MakePathAbsolute(Match match, string directory)
        {
            string matchedPath = match.Groups[1].Value;
            return IsRelative(matchedPath) 
                ? $"#include \"{directory}/{matchedPath}\"" 
                : match.Value;
        }
        
        static readonly string[] s_UnityPathPrefixes = { "packages/", "assets/", "resources/" };
        static readonly HashSet<string> s_BuiltInIncludes = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "AutoLight.cginc",
            "EditorUIE.cginc",
            "GLSLSupport.glslinc",
            "GraniteShaderLib3.cginc",
            "HLSLSupport.cginc",
            "Lighting.cginc",
            "SpeedTree8Common.cginc",
            "SpeedTreeBillboardCommon.cginc",
            "SpeedTreeCommon.cginc",
            "SpeedTreeVertex.cginc",
            "SpeedTreeWind.cginc",
            "TerrainEngine.cginc",
            "TerrainPreview.cginc",
            "TerrainSplatmapCommon.cginc",
            "TerrainTool.cginc",
            "tessellation.cginc",
            "TextCore_Properties.cginc",
            "TextCore_SDF_SSD.cginc",
            "TextcoreProperties.cginc",
            "UnityBuiltin2xTreeLibrary.cginc",
            "UnityBuiltin3xTreeLibrary.cginc",
            "UnityCG.cginc",
            "UnityCG.glslinc",
            "UnityCustomRenderTexture.cginc",
            "UnityDeferredLibrary.cginc",
            "UnityDeprecated.cginc",
            "UnityGBuffer.cginc",
            "UnityGlobalIllumination.cginc",
            "UnityImageBasedLighting.cginc",
            "UnityIndirect.cginc",
            "UnityInstancing.cginc",
            "UnityLegacyTextureStack.cginc",
            "UnityLightingCommon.cginc",
            "UnityMetaOass.cginc",
            "UnityPBSLighting.cginc",
            "UnityRayTracingMeshUtils.cginc",
            "UnityShaderUtilities.cginc",
            "UnityShaderVariables.cginc",
            "UnityShadowLibrary.cginc",
            "UnitySprites.cginc",
            "UnityStandardBRDF.cginc",
            "UnityStandardConfig.cginc",
            "UnityStandardCore.cginc",
            "UnityStandardCoreForward.cginc",
            "UnityStandardCoreForwardSimple.cginc",
            "UnityStandardInput.cginc",
            "UnityStandardMeta.cginc",
            "UnityStandardParticleEditor.cginc",
            "UnityStandardParticleInstancing.cginc",
            "UnityStandardParticles.cginc",
            "UnityStandardParticleShadow.cginc",
            "UnityStandardShadow.cginc",
            "UnityStandardUtils.cginc",
            "UnityStereoExtensions.glslinc",
            "UnityStereoSupport.glslinc",
            "UnityUI.cginc",
            "UnityUIE.cginc"
        };
    }
}