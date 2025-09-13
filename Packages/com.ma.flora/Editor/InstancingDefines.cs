// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Flora.Rendering;
using UnityEditor;

#if UNITY_2023_3_OR_NEWER
using UnityEditor.Build;
#endif

namespace MA.Flora.Editor
{
    static class InstancingDefines
    {
        public const string DisableShaderGraphInjection = "FLORA_DISABLE_SHADER_GRAPH_INJECTION";
        public const string EnableExperimentalGPUDrivenOcclusionIntegration = "FLORA_ENABLE_EXPERIMENTAL_GPU_DRIVEN_OCCLUSION_INTEGRATION";

        public static void UpdateProjectDefines()
        {
            if (InstancingProjectSettings.DisableShaderGraphInjection)
                AddDefine(DisableShaderGraphInjection);
            else
                RemoveDefine(DisableShaderGraphInjection);

            switch (InstancingProjectSettings.InstanceBufferType)
            {
                case InstanceBufferType.Float4:
                    AddDefine(InstancingShaderConfig.InstanceDataBufferType_Float4);
                    break;
                default:
                    AddDefine(InstancingShaderConfig.InstanceDataBufferType_Float4);
                    break;
            }

            switch (InstancingProjectSettings.InstanceTransformPackingMode)
            {
                case InstanceTransformPackingMode.Disabled:
                    AddDefine(InstancingShaderConfig.InstanceDataTransformPacking_Disabled);
                    break;
                default:
                    RemoveDefine(InstancingShaderConfig.InstanceDataTransformPacking_Disabled);
                    break;
            }

#if UNITY_2023_3_OR_NEWER
            if (InstancingProjectSettings.UseExperimentalGPUDrivenOcclusionIntegration)
                AddDefine(EnableExperimentalGPUDrivenOcclusionIntegration);
            else
                RemoveDefine(EnableExperimentalGPUDrivenOcclusionIntegration);
#endif
        }

        static bool AddDefine(string newDefine)
        {
            if (string.IsNullOrEmpty(newDefine))
                return false;

            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = GetTargetDefines();

            if (!defines.Contains(newDefine))
            {
                if (!string.IsNullOrEmpty(defines))
                    defines += ";";

                defines += newDefine;
                SetTargetDefines(buildTargetGroup, defines);
                return true;
            }

            return false;
        }

        static bool RemoveDefine(string defineToRemove)
        {
            if (string.IsNullOrEmpty(defineToRemove))
                return false;

            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = GetTargetDefines();

            if (defines.Contains(defineToRemove))
            {
                defines = defines.Replace(defineToRemove, "");
                SetTargetDefines(buildTargetGroup, defines);
                return true;
            }

            return false;
        }

        static string GetTargetDefines()
        {
            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
#if UNITY_2023_3_OR_NEWER
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup));
#else
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
#endif
            return defines;
        }

        static void SetTargetDefines(BuildTargetGroup targetGroup, string defines)
        {
#if UNITY_2023_3_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup), defines);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
#endif
        }
    }
}
