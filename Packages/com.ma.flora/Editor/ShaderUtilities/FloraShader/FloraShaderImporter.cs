// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.IO;
using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MA.Flora.Editor
{
    [ExcludeFromPreset]
    [ScriptedImporter(Version, new[] { "florashader", "floraautoshader" })]
    class FloraShaderImporter : ScriptedImporter
    {
        public const int Version = 4;
        public const string Extension = "florashader";

        public string SourceAssetGUID;
        public Shader PatchedShader;

        const string k_ErrorShader = @"
Shader ""Hidden/FloraAutoShaderError""
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include ""UnityCG.cginc""

            struct appdata_t {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(1,0,1,1);
            }
            ENDCG
        }
    }
    Fallback Off
}";

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text = File.ReadAllText(ctx.assetPath);
            if (string.IsNullOrEmpty(text))
                return;

            FloraShaderData data = JsonUtility.FromJson<FloraShaderData>(text);
            if (data == null)
                return;

            if (!TryPatchSource(data.SourceGUID, data.PatchFlags, out string sourceCode))
            {
                Debug.LogError($"FloraShaderImporter: Failed to modify source code for {data.GetSourceAssetPath()}");
                return;
            }

            PatchedShader = ShaderUtil.CreateShaderAsset(ctx, sourceCode, true);

            if (ShaderUtil.ShaderHasError(PatchedShader))
            {
                ShaderMessage[] errors = ShaderUtil.GetShaderMessages(PatchedShader);
                foreach (ShaderMessage error in errors)
                {
                    if (error.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    {
                        Debug.LogError(error.message);
                    }
                    else
                    {
                        Debug.LogWarning(error.message);
                    }
                }
            }
            else
            {
                ShaderUtil.ClearShaderMessages(PatchedShader);
                ShaderUtil.RegisterShader(PatchedShader);
            }

            Texture2D icon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/Shader Icon.png");
            ctx.AddObjectToAsset("MainAsset", PatchedShader, icon);
            ctx.SetMainObject(PatchedShader);

            // only the main shader gets a material created
            Material material = new Material(PatchedShader) { name = PatchedShader.name };
            ctx.AddObjectToAsset("Material", material);

            FloraShaderCache.instance.Register(data.SourceGUID, this);
            FloraShaderCache.instance.SaveToLibrary();

            string instancingHeaderPath = AssetDatabase.GUIDToAssetPath(ShaderPatcher.InstancingHeaderGUID);
            if (string.IsNullOrEmpty(instancingHeaderPath))
                Debug.LogError("FloraShaderImporter: Instancing header not found!");
            else
                ctx.DependsOnSourceAsset(instancingHeaderPath);

            if (!string.IsNullOrEmpty(data.SourceGUID))
            {
                string sourceAssetPath = data.GetSourceAssetPath();
                ctx.DependsOnSourceAsset(sourceAssetPath);

                string[] dependencies = AssetDatabase.GetDependencies(sourceAssetPath);
                foreach (var dependency in dependencies)
                    ctx.DependsOnSourceAsset(dependency);
            }
        }

        public static bool TryPatchSource(string assetGUID, ShaderPatcher.PatchFlags patchFlags, out string patchedSource) =>
            TryPatchSource(assetGUID, patchFlags, out patchedSource, out _);

        public static bool TryPatchSource(string assetGUID, ShaderPatcher.PatchFlags patchFlags, out string patchedSource, out string[] dependencies)
        {
            patchedSource = k_ErrorShader;
            dependencies = Array.Empty<string>();

            string assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            string assetDirectory = Path.GetDirectoryName(assetPath);
            string originalSource = string.Empty;

            if (!string.IsNullOrEmpty(assetPath))
            {
                FloraSourceShaderType sourceType = GetShaderSourceType(assetPath);

                if (sourceType == FloraSourceShaderType.Shader)
                {
                    originalSource = File.ReadAllText(assetPath);
                }
                else if (sourceType == FloraSourceShaderType.ShaderGraph)
                {
#if HAS_PACKAGE_SHADERGRAPH
#endif
                }
                else if (sourceType == FloraSourceShaderType.BetterShader)
                {
#if HAS_PACKAGE_BETTER_SHADERS
                    Shader shader = AssetDatabase.LoadMainAssetAtPath(assetPath) as Shader;
                    if (shader)
                    {
                        JBooth.BetterShaders.ShaderBuilder.RenderPipeline pipeline = JBooth.BetterShaders.ShaderBuilder.RenderPipeline.Standard;

#if HAS_PACKAGE_UNITY_URP_12_0_0
                        if (GraphicsSettings.renderPipelineAsset is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)
                        {
#if UNITY_2023_3_OR_NEWER
                            pipeline = JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2023;
#elif UNITY_2022_3_OR_NEWER
                            pipeline = JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2022;
#elif UNITY_2021_3_OR_NEWER
                            pipeline = JBooth.BetterShaders.ShaderBuilder.RenderPipeline.URP2021;
#endif
                        }
#elif HAS_PACKAGE_UNITY_HDRP_12_0_0
                        else if (GraphicsSettings.renderPipelineAsset is UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset)
                        {
#if UNITY_2023_3_OR_NEWER
                            pipeline = JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2023;
#elif UNITY_2022_3_OR_NEWER
                            pipeline = JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2022;
#elif UNITY_2021_3_OR_NEWER
                            pipeline = JBooth.BetterShaders.ShaderBuilder.RenderPipeline.HDRP2021;
#endif
                        }
#endif // HAS_PACKAGE_BETTER_SHADERS

                        originalSource = JBooth.BetterShaders.StackedShaderImporterEditor.BuildExportShader(pipeline, null, assetPath);
                    }

#endif
                }
                else if (sourceType == FloraSourceShaderType.MicroVersePack)
                {
#if HAS_PACKAGE_MICRO_VERSE
                    var package = ScriptableObject.CreateInstance<JBooth.MicroVerseCore.ShaderPackager.ShaderPackage>();
                    EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(assetPath), package);
                    if (package)
                    {
                        originalSource = package.GetShaderSrc();
                    }
#endif
                }
            }

            if (!string.IsNullOrEmpty(originalSource))
            {
                patchedSource = ShaderPatcher.PatchShaderCode(originalSource, patchFlags);
                if (!string.IsNullOrEmpty(patchedSource))
                    patchedSource = ShaderUtility.FixRelativePaths(patchedSource, assetDirectory);

                return !string.IsNullOrEmpty(patchedSource);
            }

            return false;
        }

        public static FloraSourceShaderType GetShaderSourceType(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return FloraSourceShaderType.Invalid;
            }
            else if (assetPath.EndsWith(".shader", StringComparison.InvariantCultureIgnoreCase))
            {
                return FloraSourceShaderType.Shader;
            }
            else if (assetPath.EndsWith(".microversepack", StringComparison.InvariantCultureIgnoreCase))
            {
                return FloraSourceShaderType.MicroVersePack;
            }
            else if (assetPath.EndsWith(".surfshader", StringComparison.InvariantCultureIgnoreCase) ||
                     assetPath.EndsWith(".stackedshader", StringComparison.InvariantCultureIgnoreCase))
            {
                return FloraSourceShaderType.BetterShader;
            }
            else
            {
                return FloraSourceShaderType.Unrecognized;
            }
        }
    }
}
