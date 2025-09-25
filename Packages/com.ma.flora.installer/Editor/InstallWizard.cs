// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MA.Flora.Installer.Editor
{
    [InitializeOnLoad]
    class InstallWizard : EditorWindow
    {
        const string k_UxmlPath = "Packages/com.ma.flora.installer/Editor/InstallWizard.uxml";
        const string k_ChangelogPath = "Packages/com.ma.flora.installer/CHANGELOG.md";

        static readonly System.Version k_NullVersion = new System.Version("0.0.0");

        static System.Version s_WizardVersion;
        static System.Version s_InstalledVersion;

        static System.Version s_UnityCollectionsVersion;
#if UNITY_2022_2_OR_NEWER
        static readonly string k_RecommendedUnityCollectionsVersionString = "2.4.2";
#else
        static readonly string k_RecommendedUnityCollectionsVersionString = "1.4.0";
#endif
        static readonly System.Version k_RecommendedUnityCollectionsVersion = new System.Version(k_RecommendedUnityCollectionsVersionString);

        static InstallWizard s_Instance;

        static InstallWizard()
        {
            WaitAndInitFloraVersions();
        }

        static async void WaitAndInitFloraVersions()
        {
            await Task.Yield();

            InitVersions();
            if (s_InstalledVersion < s_WizardVersion)
                InitWindow();
        }

        [MenuItem("Flora/Install Wizard", priority = 10000)]
        static void InitWindow()
        {
            InstallWizard window = GetWindow<InstallWizard>();
            window.titleContent = new GUIContent("Flora Install Wizard");
            window.minSize = new Vector2(400, 300);
            window.maxSize = new Vector2(600, 800);
            window.Show();
        }

        void OnEnable()
        {
            s_Instance = this;
        }

        void OnDisable()
        {
            s_Instance = null;
        }

        // --- GUI ---

        void CreateGUI()
        {
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_UxmlPath);
            visualTree.CloneTree(rootVisualElement);

            Button installButton = rootVisualElement.Q<Button>("install-button");
            installButton.clicked += InstallFlora;

            Button installDemoButton = rootVisualElement.Q<Button>("install-demo-button");
            installDemoButton.clicked += InstallDemo;

            TextElement changeLogText = rootVisualElement.Q<TextElement>("changelog-text");
            changeLogText.text = ReadChangeLog();

            UpdateUI();
        }

        void UpdateUI()
        {
            InitVersions();

            Label versionLabel = rootVisualElement.Q<Label>("version-label");
            versionLabel.text = $"Version {s_WizardVersion}";

            Label installedVersionLabel = rootVisualElement.Q<Label>("installed-version-label");

            string installButtonText = "Install";
            if (s_InstalledVersion.Major > 0)
            {
                installedVersionLabel.text = $"Installed {s_InstalledVersion}";

                if (s_WizardVersion > s_InstalledVersion)
                    installButtonText = "Update";
                else if (s_WizardVersion == s_InstalledVersion)
                    installButtonText = "Reinstall";
            }
            else
            {
                installedVersionLabel.text = "Not Installed";
            }

            Button installButton = rootVisualElement.Q<Button>("install-button");
            installButton.text = L10n.Tr(installButtonText);

            Button installDemoButton = rootVisualElement.Q<Button>("install-demo-button");
            if (s_InstalledVersion != s_WizardVersion)
            {
                installDemoButton.SetEnabled(false);
            }
            else
            {
                installDemoButton.SetEnabled(true);

                Sample demoSample = UnityEditor.PackageManager.UI.Sample.FindByPackage("com.ma.flora", s_WizardVersion.ToString()).FirstOrDefault();
                List<string> previousImports = GetPreviousImports(demoSample);
                installDemoButton.text = L10n.Tr("Import Sample");
                if (demoSample.isImported)
                    installDemoButton.text = L10n.Tr("Reimport Sample");
                else if (previousImports.Count > 0)
                    installDemoButton.text = L10n.Tr("Update Sample");
            }

            if (s_UnityCollectionsVersion == k_NullVersion)
            {
                UpgradePackages();
            }

            Button upgradeUnityCollectionsButton = rootVisualElement.Q<Button>("upgrade-unity-collections-button");
            upgradeUnityCollectionsButton.style.display = DisplayStyle.None;

#if UNITY_2022_2_OR_NEWER
            if (s_UnityCollectionsVersion < k_RecommendedUnityCollectionsVersion)
            {
                upgradeUnityCollectionsButton.style.display = DisplayStyle.Flex;
                upgradeUnityCollectionsButton.clicked += UpgradePackages;
            }
#endif
        }

        async void InstallFlora()
        {
            try
            {
                List<string> previousVersionPaths = GetValidPackagePaths();
                if (previousVersionPaths.Count > 0)
                {
                    AssetDatabase.StartAssetEditing();
                    foreach (string path in previousVersionPaths)
                    {
                        EditorUtility.DisplayProgressBar(L10n.Tr("Installing Flora"), L10n.Tr("Cleaning previous versions..."), 0);
                        AssetDatabase.DeleteAsset(path);
                    }

                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.Refresh();
                    EditorUtility.ClearProgressBar();
                }

                await Task.Yield();
                ImportPackage();
                AssetDatabase.Refresh();

                await Task.Yield();
                UpdateUI();
            }
            catch (IOException e)
            {
                Debug.LogError($"[Flora Install Wizard] Error installing Flora packages: {e.Message}");
            }
        }

        // --- Initialization ---

        static void InitVersions()
        {
            const string installerPath = "Packages/com.ma.flora.installer/package.json";
            PackageInfo installerPackageInfo = PackageInfo.FindForAssetPath(installerPath);
            s_WizardVersion = new System.Version(installerPackageInfo?.version ?? "0.0.0");

            const string floraPath = "Packages/com.ma.flora/package.json";
            PackageInfo floraPackageInfo = PackageInfo.FindForAssetPath(floraPath);
            s_InstalledVersion = new System.Version(floraPackageInfo?.version ?? "0.0.0");

            const string unityCollectionsPath = "Packages/com.unity.collections/package.json";
            PackageInfo unityCollectionsPackageInfo = PackageInfo.FindForAssetPath(unityCollectionsPath);
            s_UnityCollectionsVersion = new System.Version(unityCollectionsPackageInfo?.version ?? "0.0.0");
        }

        static string ReadChangeLog()
        {
            string changelog = File.ReadAllText(k_ChangelogPath);
            // Remove the introduction
            var changelogContent = Regex.Replace(changelog, @"^.*?(?=## \[\d+\.\d+\.\d+\] - \d{4}-\d{2}-\d{2})", string.Empty, RegexOptions.Singleline);
            // Convert ### headers to bold and larger size
            changelogContent = Regex.Replace(changelogContent, @"### (.+)", "<b><size=12>$1</size></b>");
            // Convert ## headers to bold and larger size
            changelogContent = Regex.Replace(changelogContent, @"## (.+)", "<b><size=14>$1</size></b>");
            return changelogContent;
        }

        static void InstallDemo()
        {
            Sample demoSample = UnityEditor.PackageManager.UI.Sample.FindByPackage("com.ma.flora", s_WizardVersion.ToString()).FirstOrDefault();

            List<string> previousImports = GetPreviousImports(demoSample);
            string previousImportPaths = previousImports.Aggregate(string.Empty,
                (current, next) => current + next.Replace(@"\", "/").Replace(Application.dataPath, "Assets") + "\n");

            string warningMessage = string.Empty;
            if (previousImports.Count > 1)
            {
                warningMessage = L10n.Tr("Different versions of the sample are already imported at") +
                                 "\n\n" +
                                 previousImportPaths +
                                 "\n" +
                                 L10n.Tr("They will be deleted when you update.");
            }
            else if (previousImports.Count == 1)
            {
                if (demoSample.isImported)
                {
                    warningMessage = L10n.Tr("The sample is already imported at") +
                                     "\n\n" + previousImportPaths +
                                     "\n" +
                                     L10n.Tr("Importing again will override all changes you have made to it.");
                }
                else
                {
                    warningMessage = L10n.Tr("A different version of the sample is already imported at") +
                                     "\n\n" +
                                     previousImportPaths +
                                     "\n" +
                                     L10n.Tr("It will be deleted when you update.");
                }
            }

            if (!string.IsNullOrEmpty(warningMessage) &&
                !EditorUtility.DisplayDialog(
                    L10n.Tr("Importing Flora Sample"),
                    warningMessage + L10n.Tr(" Are you sure you want to continue?"),
                    L10n.Tr("Yes"), L10n.Tr("No")))
            {
                return;
            }

            if (demoSample.Import(Sample.ImportOptions.OverridePreviousImports))
            {
                s_Instance?.UpdateUI();

                if (demoSample.isImported)
                {
                    string assetPath = ConvertPathToAssetPath(demoSample.importPath);
                    Object obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }
            }
        }

        // --- Package import helpers ---

        static void ImportPackage()
        {
            const string packagePath = "Packages/com.ma.flora.installer/Editor/Packages/Flora.unitypackage";
            if (!File.Exists(packagePath))
            {
                Debug.LogError("[Flora Install Wizard] Flora package not found at: " + packagePath);
                return;
            }

            try
            {
                AssetDatabase.ImportPackage(packagePath, false);
            }
            catch (System.IO.IOException e)
            {
                Debug.Log($"[Flora Install Wizard] Cannot import flora package: {e.Message}");
            }
        }

        static List<string> GetValidPackagePaths()
        {
            List<string> packageFolders = new List<string>();

            const string corePath = "Packages/com.ma.core";
            if (AssetDatabase.IsValidFolder(corePath))
                packageFolders.Add(corePath);

            const string floraPath = "Packages/com.ma.flora";
            if (AssetDatabase.IsValidFolder(floraPath))
                packageFolders.Add(floraPath);

            const string uiPath = "Packages/com.ma.ui";
            if (AssetDatabase.IsValidFolder(uiPath))
                packageFolders.Add(uiPath);

            return packageFolders;
        }

        static string ConvertPathToAssetPath(string path)
        {
            return path.Replace(@"\", "/").Replace(Application.dataPath, "Assets");
        }

        // --- Sample import helpers ---

        static PropertyInfo s_Sample_PreviousImports;

        static List<string> GetPreviousImports(Sample sample)
        {
            s_Sample_PreviousImports ??= typeof(Sample).GetProperty("PreviosImports", BindingFlags.NonPublic | BindingFlags.Instance);
            if (s_Sample_PreviousImports == null)
                return new List<string>();

            return (List<string>)s_Sample_PreviousImports.GetValue(sample);
        }

        // --- Update manifest.json to include Unity.Collections package ---

        static Type s_UnityEditorJsonType;

        delegate object DeserializeDelegate(string json);
        static MethodInfo s_Json_DeserializeMethodInfo;
        static DeserializeDelegate s_Json_Deserialize;

        delegate string SerializeDelegate(object obj, bool pretty = false, string indentText = "  ");
        static MethodInfo s_Json_SerializeMethodInfo;
        static SerializeDelegate s_Json_Serialize;

        static void UpgradePackages()
        {
            if (s_Json_DeserializeMethodInfo == null)
            {
                s_UnityEditorJsonType = typeof(InternalEditorUtility).Assembly.GetType("UnityEditor.Json");
                s_Json_DeserializeMethodInfo = s_UnityEditorJsonType.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static);
                s_Json_Deserialize = s_Json_DeserializeMethodInfo!.CreateDelegate(typeof(DeserializeDelegate)) as DeserializeDelegate;

                s_Json_SerializeMethodInfo = s_UnityEditorJsonType.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static);
                s_Json_Serialize = s_Json_SerializeMethodInfo!.CreateDelegate(typeof(SerializeDelegate)) as SerializeDelegate;
            }

            string manifestJson = File.ReadAllText("Packages/manifest.json");
            if (string.IsNullOrEmpty(manifestJson))
                return;

            if (s_Json_Deserialize!(manifestJson) is not Dictionary<string, object> info)
                return;

            bool hasUnityCollections = false;
            if (info.ContainsKey("dependencies"))
            {
                if (info["dependencies"] is not IDictionary)
                {
                    var dependenciesDictionary = new Dictionary<string, object>();
                    info["dependencies"] = dependenciesDictionary;
                }
                else
                {
                    var dependenciesDictionary = (IDictionary)info["dependencies"];
                    foreach (var packageName in dependenciesDictionary.Keys)
                    {
                        if (packageName.ToString().Contains("com.unity.collections"))
                        {
                            hasUnityCollections = true;
                            break;
                        }
                    }
                }
            }

            if (!hasUnityCollections)
            {
                if (!info.ContainsKey("dependencies"))
                    info["dependencies"] = new Dictionary<string, object>();

                var dependenciesDictionary = (IDictionary)info["dependencies"];
                dependenciesDictionary["com.unity.collections"] = k_RecommendedUnityCollectionsVersionString;

                string newManifestJson = s_Json_Serialize!(info, true);
                if (!string.IsNullOrEmpty(newManifestJson))
                {
                    File.WriteAllText("Packages/manifest.json", newManifestJson);
                    AssetDatabase.Refresh();
                }
            }
        }
    }
}
