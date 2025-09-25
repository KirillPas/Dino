// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Flora.Rendering;
using UnityEditor;
using UnityEditor.SettingsManagement;

namespace MA.Flora.Editor
{
    // --- User Settings ---

    // static class InstancingUserSettingsManager
    // {
    //     static Settings s_SettingsInstance;
    //
    //     public static Settings Instance => s_SettingsInstance ??= new Settings(new[] { new UserSettingsRepository() });
    //
    //     [SettingsProvider]
    //     static SettingsProvider CreateSettingsProvider() => new UserSettingsProvider("Preferences/Flora", Instance, new[] { typeof(InstancingUserSettingsManager).Assembly });
    // }
    //
    // class InstancingUserSetting<T> : UserSetting<T>
    // {
    //     public InstancingUserSetting(string key, T value) : base(InstancingUserSettingsManager.Instance, key, value, SettingsScope.User) { }
    // }

    // --- Project Settings ---

    static class InstancingProjectSettingsManager
    {
        static Settings s_SettingsInstance;

        public static Settings Instance => s_SettingsInstance ??= new Settings(new[] { new PackageSettingsRepository("com.ma.flora", "Settings") });

        [SettingsProvider]
        static SettingsProvider CreateSettingsProvider()
        {
            var provider = new UserSettingsProvider("Project/Graphics/Flora Global Settings",
                Instance,
                new[] { typeof(InstancingProjectSettingsManager).Assembly },
                SettingsScope.Project)
            {
                keywords = new[] { "Flora", "Instancing", "Global", "Settings" }
            };

            Instance.afterSettingsSaved -= InstancingProjectSettings.OnSettingsSaved;
            Instance.afterSettingsSaved += InstancingProjectSettings.OnSettingsSaved;

            return provider;
        }
    }

    class InstancingProjectSetting<T> : UserSetting<T>
    {
        public InstancingProjectSetting(string key, T value) : base(InstancingProjectSettingsManager.Instance, key, value) { }
    }

    static class InstancingProjectSettings
    {
        static string BuildTargetKey => EditorUserBuildSettings.selectedStandaloneTarget.ToString();

        // [UserSetting("Shader Config",
        //     "Buffer Type",
        //     "The instance buffer type used for instancing. " +
        //     "Some platforms may perform better with a Float4 aligned structured buffer. " +
        //     "Default is a Float4 structured buffer.")]
        // static readonly InstancingProjectSetting<InstanceBufferType> s_InstanceBufferMode
        //     = new InstancingProjectSetting<InstanceBufferType>($"ShaderConfig.InstanceBufferType.{BuildTargetKey}", InstanceBufferType.Raw);

        // [UserSetting("Shader Config",
        //     "Transform Packing Mode",
        //     "The instance buffer transform packing mode used for instancing. " +
        //     "Some platforms may perform better with packing disabled. Default is Float4x2.")]
        // static readonly InstancingProjectSetting<InstanceTransformPackingMode> s_InstanceBufferTransformPackingMode
        //     = new InstancingProjectSetting<InstanceTransformPackingMode>($"ShaderConfig.InstanceTransformPackingMode.{BuildTargetKey}", InstanceTransformPackingMode.Float4x2);

        [UserSetting("Shader Config",
            "Disable Legacy Light Probe Support",
            "Disables a runtime check for instanced light probe SH data.")]
        static readonly InstancingProjectSetting<bool> s_DisableLegacyLightProbeSupport
            = new InstancingProjectSetting<bool>($"ShaderConfig.DisableLegacyLightProbeSupport.{BuildTargetKey}", false);

        [UserSetting("Shader Config",
            "Disable Instanced Property Support",
            "Disables instanced property support for instances. Mainly used for debugging.")]
        static readonly InstancingProjectSetting<bool> s_DisableInstancedPropertySupport
            = new InstancingProjectSetting<bool>($"ShaderConfig.DisableInstancedPropertySupport.{BuildTargetKey}", false);

#if UNITY_2023_3_OR_NEWER
        [UserSetting("Render Config",
            "Use Experimental GPUDriven Occlusion Integration",
            "Pulls occlusion data from the new GPUDriven API for Flora instancing, if enabled.")]
        static readonly InstancingProjectSetting<bool> s_UseExperimentalGPUDrivenOcclusionIntegration
            = new InstancingProjectSetting<bool>($"RenderConfig.UseExperimentalGPUDrivenOcclusionIntegration", false);
#endif

        [UserSetting("Shader Graph",
            "Disable ShaderGraph Injection",
            "Disables automatic patching of ShaderGraph shaders for Flora instancing. " +
            "Disabling this will require you to use the SetupFloraInstancingDataNode in ShaderGroup. Place it before the vertex position slot.")]
        static readonly InstancingProjectSetting<bool> s_AutoInjection
            = new InstancingProjectSetting<bool>("ShaderGraph.DisableShaderGraphInjection", false);

        internal static InstanceBufferType InstanceBufferType => InstanceBufferType.Raw;
        internal static InstanceTransformPackingMode InstanceTransformPackingMode => InstanceTransformPackingMode.Float4x2;
        internal static bool DisableLegacyLightProbeSupport => s_DisableLegacyLightProbeSupport.value;
        internal static bool DisableInstancedPropertySupport => s_DisableInstancedPropertySupport.value;
        internal static bool UseExperimentalGPUDrivenOcclusionIntegration
        {
            get
            {
#if UNITY_2023_3_OR_NEWER
                return s_UseExperimentalGPUDrivenOcclusionIntegration.value;
#else
                return false;
#endif
            }
        }
        internal static bool DisableShaderGraphInjection => s_AutoInjection.value;

        internal static async void OnSettingsSaved()
        {
            InstancingDefines.UpdateProjectDefines();
            await InstancingShaderConfig.WriteInstancingConfig();
            AssetDatabase.Refresh();
        }
    }
}
