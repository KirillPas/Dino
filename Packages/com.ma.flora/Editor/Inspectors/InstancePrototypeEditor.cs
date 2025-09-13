// Copyright © Magnetic Arcade. All Rights Reserved.

using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace MA.Flora.Editor
{
    using CED = CoreEditorDrawer<SerializedInstancePrototype>;

    [CanEditMultipleObjects]
    [CustomEditor(typeof(InstancedPrototype))]
    class InstancePrototypeEditor : UnityEditor.Editor
    {
        internal SerializedInstancePrototype SerializedInstancePrototype;

        void OnEnable()
        {
            InstancePrototypeUI.RegisterEditor(this);
        }

        void OnDisable()
        {
            InstancePrototypeUI.UnregisterEditor(this);
        }

        public override void OnInspectorGUI()
        {
            InstancedPrototype prototype = (InstancedPrototype)target;
            PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(prototype.gameObject);
            if (prefabStage == null && prototype.gameObject.scene.IsValid())
            {
                EditorGUILayout.Space(2);

                if (PrefabUtility.IsPartOfAnyPrefab(prototype))
                {
                    EditorGUILayout.HelpBox("Changes to this prototype must be made in the prefab.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Prototype must be attached to a prefab.", MessageType.Error);
                }
            }
            else
            {
                SerializedInstancePrototype ??= new SerializedInstancePrototype(serializedObject);
                SerializedInstancePrototype.Update();

                if (!prototype.Validate(out string error, out MessageType messageType))
                {
                    EditorGUILayout.HelpBox(error, messageType);
                    EditorGUILayout.Space(4);
                }

                EditorGUI.BeginChangeCheck();
                InstancePrototypeUI.Inspector.Draw(SerializedInstancePrototype, this);

                if (EditorGUI.EndChangeCheck())
                    prototype.ClearCache();

                SerializedInstancePrototype.Apply();
            }
        }
    }

    class SerializedInstanceDynamicDensitySettings
    {
        public SerializedProperty Density;
        public SerializedProperty Falloff;
        public SerializedProperty Range;

        public SerializedInstanceDynamicDensitySettings(SerializedProperty baseProperty)
        {
            Density = baseProperty.FindPropertyRelative("Density");
            Falloff = baseProperty.FindPropertyRelative("Falloff");
            Range = baseProperty.FindPropertyRelative("Range");
        }
    }

    class SerializedInstanceCullingTreeBuildSettings
    {
        public SerializedProperty BranchingFactor;
        public SerializedProperty MinVerticesPerCluster;

        public SerializedProperty MinOcclusionQueries;
        public SerializedProperty MaxOcclusionQueries;
        public SerializedProperty MinInstancesPerOcclusionQuery;

        public SerializedInstanceCullingTreeBuildSettings(SerializedProperty baseProperty)
        {
            BranchingFactor = baseProperty.FindPropertyRelative("BranchingFactor");
            MinVerticesPerCluster = baseProperty.FindPropertyRelative("MinVerticesPerCluster");
            MinOcclusionQueries = baseProperty.FindPropertyRelative("MinOcclusionQueries");
            MaxOcclusionQueries = baseProperty.FindPropertyRelative("MaxOcclusionQueries");
            MinInstancesPerOcclusionQuery = baseProperty.FindPropertyRelative("MinInstancesPerOcclusionQuery");
        }
    }

    class SerializedPlacementSettings
    {
        public SerializedProperty Density;
        public SerializedProperty Radius;
        public SerializedProperty UseSinglePlacementRadius;
        public SerializedProperty SinglePlacementRadius;

        public SerializedProperty ScalingMode;
        public SerializedProperty ScaleX;
        public SerializedProperty ScaleY;
        public SerializedProperty ScaleZ;

        public SerializedProperty VerticalOffset;
        public SerializedProperty RandomizeYaw;
        public SerializedProperty RandomPitchAngle;
        public SerializedProperty AlignToSurface;
        public SerializedProperty AlignToSurfaceMaxAngle;
        public SerializedProperty AverageNormal;
        public SerializedProperty AverageNormalSingleComponent;
        public SerializedProperty AverageNormalSampleCount;

        public SerializedProperty SlopeMask;
        public SerializedProperty HeightMask;

        public SerializedProperty CheckWorldCollisions;
        public SerializedProperty CheckColliderOverhang;
        public SerializedProperty CollisionLayerMask;
        public SerializedProperty CollisionBoundsScale;

        public SerializedPlacementSettings(SerializedProperty baseProperty)
        {
            Density = baseProperty.FindPropertyRelative("Density");
            Radius = baseProperty.FindPropertyRelative("Radius");
            UseSinglePlacementRadius = baseProperty.FindPropertyRelative("OverrideSinglePlacementRadius");
            SinglePlacementRadius = baseProperty.FindPropertyRelative("SinglePlacementRadius");

            ScalingMode = baseProperty.FindPropertyRelative("ScalingMode");
            ScaleX = baseProperty.FindPropertyRelative("ScaleX");
            ScaleY = baseProperty.FindPropertyRelative("ScaleY");
            ScaleZ = baseProperty.FindPropertyRelative("ScaleZ");

            VerticalOffset = baseProperty.FindPropertyRelative("VerticalOffset");
            RandomizeYaw = baseProperty.FindPropertyRelative("RandomizeYaw");
            RandomPitchAngle = baseProperty.FindPropertyRelative("RandomPitchAngle");
            AlignToSurface = baseProperty.FindPropertyRelative("AlignToSurface");
            AlignToSurfaceMaxAngle = baseProperty.FindPropertyRelative("AlignToSurfaceMaxAngle");
            AverageNormal = baseProperty.FindPropertyRelative("AverageNormal");
            AverageNormalSingleComponent = baseProperty.FindPropertyRelative("AverageNormalSingleComponent");
            AverageNormalSampleCount = baseProperty.FindPropertyRelative("AverageNormalSampleCount");

            SlopeMask = baseProperty.FindPropertyRelative("SlopeMask");
            HeightMask = baseProperty.FindPropertyRelative("HeightMask");

            CheckWorldCollisions = baseProperty.FindPropertyRelative("CheckWorldCollisions");
            CheckColliderOverhang = baseProperty.FindPropertyRelative("CheckColliderOverhang");
            CollisionLayerMask = baseProperty.FindPropertyRelative("CollisionLayerMask");
            CollisionBoundsScale = baseProperty.FindPropertyRelative("CollisionBoundsScale");
        }
    }

    class SerializedPropertyList
    {
        public InstancedPrototype Prototype;
        public ReorderableList PropertyList;

        public SerializedPropertyList(InstancedPrototype prototype, SerializedProperty property)
        {
            Prototype = prototype;
            PropertyList = new ReorderableList(property.serializedObject, property, true, true, true, true)
            {
                drawHeaderCallback = DrawHeader,
                drawElementCallback = DrawElement,
                onReorderCallbackWithDetails = ReorderElement,
                onAddCallback = AddElement,
                onRemoveCallback = RemoveElement,
            };
        }

        public void DoLayoutList()
        {
            PropertyList.DoLayoutList();
        }

        void DrawHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, InstancePrototypeUI.Styles.InstancedPropertiesHeading);
        }

        void AddElement(ReorderableList list)
        {
            string newName = "_NewProperty";
            while (Prototype.HasInstancedProperty(newName))
                newName = $"_New{newName}";

            Prototype.AddInstancedProperty(new InstancedPropertyDescriptor(newName, InstancedPropertyType.Color, 1));
            EditorUtility.SetDirty(Prototype);
        }

        void RemoveElement(ReorderableList list)
        {
            InstancedPropertyDescriptor descriptor = Prototype.InstancedProperties[list.index];
            Prototype.RemoveInstancedProperty(descriptor.Name);
            EditorUtility.SetDirty(Prototype);
        }

        void ReorderElement(ReorderableList list, int oldIndex, int newIndex)
        {
            Prototype.SwapInstancedProperties(oldIndex, newIndex);
            EditorUtility.SetDirty(Prototype);
        }

        void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            InstancedPropertyDescriptor descriptor = Prototype.InstancedProperties[index];

            // Calculate rects
            Rect nameRect = new Rect(rect.x, rect.y, rect.width / 3, EditorGUIUtility.singleLineHeight);
            Rect typeRect = new Rect(rect.x + rect.width / 3, rect.y, rect.width / 3, EditorGUIUtility.singleLineHeight);
            Rect valueRect = new Rect(rect.x + 2 * rect.width / 3, rect.y, rect.width / 3, EditorGUIUtility.singleLineHeight);

            // Draw fields
            EditorGUI.BeginChangeCheck();

            string oldName = descriptor.Name;
            string newName = EditorGUI.TextField(nameRect, GUIContent.none, oldName);

            InstancedPropertyType oldType = descriptor.Type;
            InstancedPropertyType newType = (InstancedPropertyType)EditorGUI.EnumPopup(typeRect, GUIContent.none, oldType);

            float4x4 defaultValue = descriptor.DefaultValue;

            switch (newType)
            {
                case InstancedPropertyType.Float4:
                {
                    defaultValue.c0 = EditorGUI.Vector4Field(valueRect, GUIContent.none, defaultValue.c0);
                    break;
                }
                case InstancedPropertyType.Color:
                {
                    defaultValue.c0 = (Vector4)EditorGUI.ColorField(valueRect, GUIContent.none, (Vector4)defaultValue.c0, true, true, true);
                    break;
                }
            }

            if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(newName))
            {
                if (oldType != newType)
                    defaultValue = default;

                InstancedPropertyDescriptor newPropertyDescriptor = new InstancedPropertyDescriptor(newName, newType, defaultValue);
                Prototype.UpdateInstancedProperty(index, newPropertyDescriptor);
                EditorUtility.SetDirty(Prototype);
            }
        }
    }

    class SerializedInstancePrototype
    {
        public SerializedObject SerializedObject;
        public InstancedPrototype InstancedPrototype;

        public SerializedProperty CullingMode;
        public SerializedProperty CullingDistance;
        public SerializedProperty StreamingMode;
        public SerializedProperty StreamingDistance;
        public SerializedProperty LayerMaskMode;

        public SerializedProperty AffectedByGlobalInstanceDensity;
        public SerializedInstanceDynamicDensitySettings DensitySettings;

        public SerializedProperty ShadowDistance;
        public SerializedProperty ShadowLODRange;
        public SerializedProperty ShadowOverrideMode;
        public SerializedProperty ShadowCustomMaterial;

        public SerializedProperty SampleLightProbes;
        public SerializedProperty SampleLightProbesOffset;

        public SerializedProperty CreateLinkedObject;
        public SerializedProperty LinkedObjectContributesToGI;

        public SerializedPropertyList InstancedProperties;
        public SerializedInstanceCullingTreeBuildSettings CullingTreeBuildSettings;
        public SerializedPlacementSettings PlacementSettings;

        public SerializedInstancePrototype(SerializedObject serializedObject)
        {
            SerializedObject = serializedObject;
            InstancedPrototype = (InstancedPrototype)serializedObject.targetObject;
            CullingMode = serializedObject.FindProperty("m_CullingMode");
            CullingDistance = serializedObject.FindProperty("m_CullingDistance");
            StreamingMode = serializedObject.FindProperty("m_StreamingMode");
            StreamingDistance = serializedObject.FindProperty("m_StreamingDistance");
            LayerMaskMode = serializedObject.FindProperty("m_LayerMask");
            AffectedByGlobalInstanceDensity = serializedObject.FindProperty("m_AffectedByGlobalInstanceDensity");
            DensitySettings = new SerializedInstanceDynamicDensitySettings(serializedObject.FindProperty("m_DynamicDensitySettings"));
            ShadowDistance = serializedObject.FindProperty("m_ShadowDistance");
            ShadowLODRange = serializedObject.FindProperty("m_ShadowLODRange");
            ShadowOverrideMode = serializedObject.FindProperty("m_ShadowOverrideMode");
            ShadowCustomMaterial = serializedObject.FindProperty("m_ShadowCustomMaterial");
            SampleLightProbes = serializedObject.FindProperty("m_SampleLightProbes");
            SampleLightProbesOffset = serializedObject.FindProperty("m_SampleLightProbesOffset");
            CreateLinkedObject = serializedObject.FindProperty("m_CreateLinkedObject");
            LinkedObjectContributesToGI = serializedObject.FindProperty("m_LinkedObjectContributesToGI");
            InstancedProperties = new SerializedPropertyList(InstancedPrototype, serializedObject.FindProperty("m_InstancedProperties"));
            CullingTreeBuildSettings = new SerializedInstanceCullingTreeBuildSettings(serializedObject.FindProperty("m_CullingTreeSettings"));
            PlacementSettings = new SerializedPlacementSettings(serializedObject.FindProperty("m_PlacementSettings"));
        }

        public void Update()
        {
            SerializedObject.Update();
        }

        public void Apply()
        {
            SerializedObject.ApplyModifiedProperties();
        }
    }

    static class InstancePrototypeUI
    {
        enum Expandable
        {
            Instancing  = 1 << 0,
            Placement   = 1 << 1,
            Default     = Instancing | Placement
        }

        enum AdditionalProperties
        {
            Instancing = 1 << 0
        }

        internal static FoldoutOption CurrentFoldoutOption = FoldoutOption.None;
        static readonly ExpandedState<Expandable, InstancePrototypeEditor> k_ExpandedState = new(Expandable.Default, "MA.Flora");
        static readonly AdditionalPropertiesState<AdditionalProperties, InstancePrototypeEditor> k_AdditionalPropertiesState = new(0, "MA.Flora");

        public static readonly CED.IDrawer SectionInstancingSettings =
            CED.AdditionalPropertiesFoldoutGroup(Styles.InstancingSection,
                Expandable.Instancing, k_ExpandedState,
                AdditionalProperties.Instancing, k_AdditionalPropertiesState,
                CED.Group(DrawInstancingContent), DrawInstancingAdditionalContent,
                CurrentFoldoutOption);

        public static readonly CED.IDrawer SectionPlacementSettings =
            CED.FoldoutGroup(Styles.PlacementSection, Expandable.Placement, k_ExpandedState, CurrentFoldoutOption,
                CED.Group(DrawPlacementContent));

        public static readonly CED.IDrawer SectionInstancingOnly = CED.Group(DrawInstancingContent);

        public static readonly CED.IDrawer[] Inspector =
        {
            SectionInstancingSettings,
            SectionPlacementSettings,
        };

        internal static void RegisterEditor(InstancePrototypeEditor editor)
        {
            k_AdditionalPropertiesState.RegisterEditor(editor);
        }

        internal static void UnregisterEditor(InstancePrototypeEditor editor)
        {
            k_AdditionalPropertiesState.UnregisterEditor(editor);
        }

        [SetAdditionalPropertiesVisibility]
        internal static void SetAdditionalPropertiesVisibility(bool value)
        {
            if (value)
                k_AdditionalPropertiesState.ShowAll();
            else
                k_AdditionalPropertiesState.HideAll();
        }

        static void DrawInstancingContent(SerializedInstancePrototype data, UnityEditor.Editor owner)
        {
            bool hasLODGroup = data.InstancedPrototype.GetComponent<LODGroup>();

            EditorGUILayout.LabelField(Styles.RenderingHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                if (hasLODGroup)
                {
                    EditorGUILayout.PropertyField(data.CullingMode, Styles.CullingMode);
                    if (data.CullingMode.enumValueIndex == (int)InstancedCullingMode.Override)
                    {
                        using (new EditorGUI.IndentLevelScope())
                            EditorGUILayout.PropertyField(data.CullingDistance, Styles.CullingDistance);
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.EnumPopup(Styles.CullingMode, InstancedCullingMode.Override);

                    using (new EditorGUI.IndentLevelScope())
                        EditorGUILayout.PropertyField(data.CullingDistance, Styles.CullingDistance);
                }

                EditorGUILayout.PropertyField(data.StreamingMode, Styles.StreamingMode);
                if (data.StreamingMode.enumValueIndex == (int)InstancedStreamingMode.Override)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.PropertyField(data.StreamingDistance, Styles.StreamingDistance);
                        data.StreamingDistance.floatValue = math.max(data.StreamingDistance.floatValue, data.CullingDistance.floatValue);
                    }
                }

                EditorGUILayout.PropertyField(data.LayerMaskMode, Styles.LayerMask);

                EditorGUILayout.LabelField(Styles.GlobalDensity, EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(data.AffectedByGlobalInstanceDensity, Styles.AffectedByGlobalInstanceDensity);
                }

                EditorGUILayout.LabelField(Styles.DynamicDensity, EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(data.DensitySettings.Density, Styles.DynamicDensityDensity);
                    EditorGUILayout.PropertyField(data.DensitySettings.Falloff, Styles.DynamicDensityFalloff);
                    EditorGUILayout.PropertyField(data.DensitySettings.Range, Styles.DynamicDensityRange);
                }
            }

            EditorGUILayout.LabelField(Styles.ShadowsHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.ShadowDistance, Styles.ShadowDistance);
                using (new EditorGUI.DisabledScope(!hasLODGroup))
                    EditorGUILayout.PropertyField(data.ShadowLODRange, Styles.ShadowLODRange);
                EditorGUILayout.PropertyField(data.ShadowOverrideMode, Styles.ShadowMaterialMode);
                if (data.ShadowOverrideMode.enumValueIndex == (int)InstancedShadowOverrideMode.SharedCustom)
                    EditorGUILayout.PropertyField(data.ShadowCustomMaterial, Styles.ShadowCustomMaterial);
            }

            EditorGUILayout.LabelField(Styles.LightProbesHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.SampleLightProbes, Styles.SampleLightProbes);
                using (new EditorGUI.DisabledScope(!data.SampleLightProbes.boolValue))
                    EditorGUILayout.PropertyField(data.SampleLightProbesOffset, Styles.SampleLightProbesOffset);
            }

            EditorGUILayout.LabelField(Styles.LinkedObjectHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.CreateLinkedObject, Styles.CreateLinkedObject);
                using (new EditorGUI.DisabledScope(!data.CreateLinkedObject.boolValue))
                    EditorGUILayout.PropertyField(data.LinkedObjectContributesToGI, Styles.LinkedObjectContributesToGI);
            }

            EditorGUILayout.Space(8);

            data.SerializedObject.ApplyModifiedProperties();
            data.InstancedProperties.DoLayoutList();
            data.SerializedObject.Update();
        }

        static void DrawInstancingAdditionalContent(SerializedInstancePrototype data, UnityEditor.Editor owner)
        {
            EditorGUILayout.LabelField(Styles.TreeSection, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(data.CullingTreeBuildSettings.BranchingFactor, Styles.TreeBranchingFactor);
            EditorGUILayout.PropertyField(data.CullingTreeBuildSettings.MinVerticesPerCluster, Styles.TreeMinVerticesPerCluster);

            EditorGUILayout.LabelField(Styles.TreeOcclusionHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.CullingTreeBuildSettings.MinOcclusionQueries, Styles.TreeMinOcclusionQueries);
                EditorGUILayout.PropertyField(data.CullingTreeBuildSettings.MaxOcclusionQueries, Styles.TreeMaxOcclusionQueries);
                EditorGUILayout.PropertyField(data.CullingTreeBuildSettings.MinInstancesPerOcclusionQuery, Styles.TreeMinInstancesPerOcclusionQuery);
            }
        }

        static void DrawPlacementContent(SerializedInstancePrototype data, UnityEditor.Editor owner)
        {
            EditorGUILayout.LabelField(Styles.DensityHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.PlacementSettings.Density, Styles.PlacementDensity);
                EditorGUILayout.PropertyField(data.PlacementSettings.Radius, Styles.PlacementRadius);
                EditorGUILayout.PropertyField(data.PlacementSettings.UseSinglePlacementRadius, Styles.UseSinglePlacementRadius);
                using (new EditorGUI.DisabledScope(!data.PlacementSettings.UseSinglePlacementRadius.boolValue))
                    EditorGUILayout.PropertyField(data.PlacementSettings.SinglePlacementRadius, Styles.SinglePlacementRadius);
            }

            EditorGUILayout.LabelField(Styles.ScaleHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.PlacementSettings.ScalingMode, Styles.ScalingMode);
                switch ((InstanceScalingMode)data.PlacementSettings.ScalingMode.intValue)
                {
                    case InstanceScalingMode.Uniform:
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleX, Styles.Scale);
                        break;
                    case InstanceScalingMode.LockXZ:
                    {
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleX, Styles.ScaleXZ);
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleY, Styles.ScaleY);
                        break;
                    }
                    case InstanceScalingMode.LockXY:
                    {
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleX, Styles.ScaleXY);
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleZ, Styles.ScaleZ);
                        break;
                    }
                    case InstanceScalingMode.LockYZ:
                    {
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleY, Styles.ScaleYZ);
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleX, Styles.ScaleX);
                        break;
                    }
                    case InstanceScalingMode.Free:
                    {
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleX, Styles.ScaleX);
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleY, Styles.ScaleY);
                        EditorGUILayout.PropertyField(data.PlacementSettings.ScaleZ, Styles.ScaleZ);
                        break;
                    }
                }
            }

            EditorGUILayout.LabelField(Styles.AlignmentHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.PlacementSettings.VerticalOffset, Styles.VerticalOffset);
                EditorGUILayout.PropertyField(data.PlacementSettings.RandomizeYaw, Styles.RandomizeYaw);
                EditorGUILayout.PropertyField(data.PlacementSettings.RandomPitchAngle, Styles.RandomPitchAngle);

                EditorGUILayout.PropertyField(data.PlacementSettings.AlignToSurface, Styles.AlignToSurface);
                using (new EditorGUI.DisabledScope(!data.PlacementSettings.AlignToSurface.boolValue))
                {
                    using var _0 = new EditorGUI.IndentLevelScope();
                    EditorGUILayout.PropertyField(data.PlacementSettings.AlignToSurfaceMaxAngle, Styles.AlignToSurfaceMaxAngle);
                    EditorGUILayout.PropertyField(data.PlacementSettings.AverageNormal, Styles.AverageNormal);
                    using (new EditorGUI.DisabledScope(!data.PlacementSettings.AverageNormal.boolValue))
                    {
                        using var _1 = new EditorGUI.IndentLevelScope();
                        EditorGUILayout.PropertyField(data.PlacementSettings.AverageNormalSingleComponent, Styles.AverageNormalSingleCollider);
                        EditorGUILayout.PropertyField(data.PlacementSettings.AverageNormalSampleCount, Styles.AverageNormalSampleCount);
                    }
                }
            }

            EditorGUILayout.LabelField(Styles.MaskingHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.PlacementSettings.SlopeMask, Styles.SlopeMask);
                EditorGUILayout.PropertyField(data.PlacementSettings.HeightMask, Styles.HeightMask);
            }

            EditorGUILayout.LabelField(Styles.CollisionHeading, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(data.PlacementSettings.CollisionLayerMask, Styles.CollisionLayerMask);
                EditorGUILayout.PropertyField(data.PlacementSettings.CheckColliderOverhang, Styles.CheckColliderOverhang);
                EditorGUILayout.PropertyField(data.PlacementSettings.CheckWorldCollisions, Styles.CheckWorldCollisions);
                using (new EditorGUI.DisabledScope(!data.PlacementSettings.CheckWorldCollisions.boolValue))
                    EditorGUILayout.PropertyField(data.PlacementSettings.CollisionBoundsScale, Styles.CollisionBoundsScale);
            }
        }

        public static class Styles
        {
            public static readonly GUIContent InstancingSection = EditorGUIUtility.TrTextContent("Instancing", "Settings that control how instances of this prototype are rendered.");

            public static readonly GUIContent RenderingHeading = EditorGUIUtility.TrTextContent("Rendering", "Settings that control the rendering of instances of this prototype.");
            public static readonly GUIContent CullingMode = EditorGUIUtility.TrTextContent("Culling Mode", "The method used to determine how instances of this prototype are culled.");
            public static readonly GUIContent CullingDistance = EditorGUIUtility.TrTextContent("Distance", "The distance from the camera at which instances of this prototype will be culled.");
            public static readonly GUIContent StreamingMode = EditorGUIUtility.TrTextContent("Streaming Mode", "The method used to determine how instances of this prototype are streamed.");
            public static readonly GUIContent StreamingDistance = EditorGUIUtility.TrTextContent("Distance", "The distance from the camera at which instances of this prototype will be streamed.");
            public static readonly GUIContent LayerMask = EditorGUIUtility.TrTextContent("Layer Mask", "The layer mask used to determine the visibility of instances of this prototype.");

            public static readonly GUIContent GlobalDensity = EditorGUIUtility.TrTextContent("Global Density", "Settings related to the overall density of instances.");
            public static readonly GUIContent AffectedByGlobalInstanceDensity = EditorGUIUtility.TrTextContent("Affected By", "If true, instances of this prototype are affected by the global instance density of the scene.");

            public static readonly GUIContent DynamicDensity = EditorGUIUtility.TrTextContent("Dynamic Density", "Settings that control how instances are culled based on distance.");
            public static readonly GUIContent DynamicDensityDensity = EditorGUIUtility.TrTextContent("Density", "The density of instances at their maximum render distance.");
            public static readonly GUIContent DynamicDensityFalloff = EditorGUIUtility.TrTextContent("Falloff", "Controls how quickly the density decreases from maximum to minimum.");
            public static readonly GUIContent DynamicDensityRange = EditorGUIUtility.TrTextContent("Range", "The range within which the density transition occurs.");

            public static readonly GUIContent ShadowsHeading = EditorGUIUtility.TrTextContent("Shadows", "Settings that control the shadows cast by instances of this prototype.");
            public static readonly GUIContent ShadowDistance = EditorGUIUtility.TrTextContent("Distance", "The maximum distance from the camera at which shadows are rendered.");
            public static readonly GUIContent ShadowLODRange = EditorGUIUtility.TrTextContent("LOD Range", "Specifies the minimum and maximum LODs used for rendering shadows.");
            public static readonly GUIContent ShadowMaterialMode = EditorGUIUtility.TrTextContent("Material Mode", "The mode used to determine the material applied to shadows.");
            public static readonly GUIContent ShadowCustomMaterial = EditorGUIUtility.TrTextContent("Custom Material", "The custom material used for rendering shadows.");

            public static readonly GUIContent LightProbesHeading = EditorGUIUtility.TrTextContent("Light Probes", "Settings that control the use of light probes for instances.");
            public static readonly GUIContent SampleLightProbes = EditorGUIUtility.TrTextContent("Sample", "Enables or disables sampling of light probes for instances.");
            public static readonly GUIContent SampleLightProbesOffset = EditorGUIUtility.TrTextContent("Sample Offset", "The offset applied when sampling light probes for instances.");

            public static readonly GUIContent TreeSection = EditorGUIUtility.TrTextContent("Culling Tree", "Settings that control the construction of the culling tree for instances.");
            public static readonly GUIContent TreeBranchingFactor = EditorGUIUtility.TrTextContent("Branching Factor", "The branching factor of the culling tree.");
            public static readonly GUIContent TreeMinVerticesPerCluster = EditorGUIUtility.TrTextContent("Min Vertices Per Cluster", "The minimum number of vertices per cluster in the culling tree.");
            public static readonly GUIContent TreeOcclusionHeading = EditorGUIUtility.TrTextContent("Static Occlusion", "Settings that control how static occlusion is queried.");
            public static readonly GUIContent TreeMinOcclusionQueries = EditorGUIUtility.TrTextContent("Min Queries", "The minimum number of occlusion queries per cluster.");
            public static readonly GUIContent TreeMaxOcclusionQueries = EditorGUIUtility.TrTextContent("Max Queries", "The maximum number of occlusion queries per cluster.");
            public static readonly GUIContent TreeMinInstancesPerOcclusionQuery = EditorGUIUtility.TrTextContent("Min Instances Per Query", "The minimum number of instances per occlusion query.");

            public static readonly GUIContent LinkedObjectHeading = EditorGUIUtility.TrTextContent("Linked Game Object", "Settings for the linked GameObject representing instances.");
            public static readonly GUIContent CreateLinkedObject = EditorGUIUtility.TrTextContent("Create", "If true, creates a linked GameObject to represent instances.");
            public static readonly GUIContent LinkedObjectContributesToGI = EditorGUIUtility.TrTextContent("Contribute to GI", "If true, the linked object contributes to global illumination.");

            public static readonly GUIContent InstancedPropertiesHeading = EditorGUIUtility.TrTextContent("Instanced Properties", "Settings that control the instanced properties of instances.");

            public static readonly GUIContent PlacementSection = EditorGUIUtility.TrTextContent("Placement", "Settings that control how instances are placed in the scene.");

            public static readonly GUIContent DensityHeading = EditorGUIUtility.TrTextContent("Density", "Settings that control the density of instances.");
            public static readonly GUIContent PlacementDensity = EditorGUIUtility.TrTextContent("Density (10sqm)", "The number of instances per 10 square meters.");
            public static readonly GUIContent PlacementRadius = EditorGUIUtility.TrTextContent("Radius", "The minimum distance between instances, in meters.");
            public static readonly GUIContent UseSinglePlacementRadius = EditorGUIUtility.TrTextContent("Use Single Placement Radius", "Overrides the radius during single placement mode.");
            public static readonly GUIContent SinglePlacementRadius = EditorGUIUtility.TrTextContent("Single Placement Radius", "The minimum distance between instances during single placement mode.");

            public static readonly GUIContent ScaleHeading = EditorGUIUtility.TrTextContent("Scale", "Settings that control the scale of instances.");
            public static readonly GUIContent ScalingMode = EditorGUIUtility.TrTextContent("Scaling Mode", "Determines how instances are scaled.");
            public static readonly GUIContent Scale = EditorGUIUtility.TrTextContent("Scale", "The overall scale range of instances.");
            public static readonly GUIContent ScaleX = EditorGUIUtility.TrTextContent("Scale X", "The scale range along the X axis.");
            public static readonly GUIContent ScaleY = EditorGUIUtility.TrTextContent("Scale Y", "The scale range along the Y axis.");
            public static readonly GUIContent ScaleZ = EditorGUIUtility.TrTextContent("Scale Z", "The scale range along the Z axis.");
            public static readonly GUIContent ScaleXZ = EditorGUIUtility.TrTextContent("Scale XZ", "The scale range along the X and Z axes.");
            public static readonly GUIContent ScaleXY = EditorGUIUtility.TrTextContent("Scale XY", "The scale range along the X and Y axes.");
            public static readonly GUIContent ScaleYZ = EditorGUIUtility.TrTextContent("Scale YZ", "The scale range along the Y and Z axes.");

            public static readonly GUIContent AlignmentHeading = EditorGUIUtility.TrTextContent("Alignment", "Settings that control the alignment of instances.");
            public static readonly GUIContent VerticalOffset = EditorGUIUtility.TrTextContent("Vertical Offset", "The vertical offset range along the local Y axis.");
            public static readonly GUIContent RandomizeYaw = EditorGUIUtility.TrTextContent("Randomize Yaw", "If enabled, instances are randomly rotated around the Y axis.");
            public static readonly GUIContent RandomPitchAngle = EditorGUIUtility.TrTextContent("Random Pitch Angle", "If enabled, instances are randomly rotated around the X axis.");
            public static readonly GUIContent AlignToSurface = EditorGUIUtility.TrTextContent("Align To Surface", "If enabled, instances align to the surface normal.");
            public static readonly GUIContent AlignToSurfaceMaxAngle = EditorGUIUtility.TrTextContent("Max Angle", "The maximum angle in degrees from the vertical axis to align instances.");
            public static readonly GUIContent AverageNormal = EditorGUIUtility.TrTextContent("Average Normal", "If enabled, instances align to the averaged normal of sampled points.");
            public static readonly GUIContent AverageNormalSampleCount = EditorGUIUtility.TrTextContent("Sample Count", "The number of points sampled to calculate the averaged normal.");
            public static readonly GUIContent AverageNormalSingleCollider = EditorGUIUtility.TrTextContent("Single Collider", "If enabled, the averaged normal is calculated from the first collider hit.");

            public static readonly GUIContent MaskingHeading = EditorGUIUtility.TrTextContent("Masking", "Settings that control the masking of instances.");
            public static readonly GUIContent SlopeMask = EditorGUIUtility.TrTextContent("Slope Mask", "The minimum and maximum slope angles where instances can be placed.");
            public static readonly GUIContent HeightMask = EditorGUIUtility.TrTextContent("Height Mask", "The minimum and maximum height levels where instances can be placed.");

            public static readonly GUIContent CollisionHeading = EditorGUIUtility.TrTextContent("Collision", "Settings that control collision checks for instances.");
            public static readonly GUIContent CheckWorldCollisions = EditorGUIUtility.TrTextContent("Check World Collisions", "If enabled, instances check for world collisions before being placed.");
            public static readonly GUIContent CheckColliderOverhang = EditorGUIUtility.TrTextContent("Check Collider Overhang", "If enabled, instances check for overhangs during collision checks.");
            public static readonly GUIContent CollisionLayerMask = EditorGUIUtility.TrTextContent("Collision Layer Mask", "The layer mask used for collision checks.");
            public static readonly GUIContent CollisionBoundsScale = EditorGUIUtility.TrTextContent("Collision Bounds Scale", "The scale of bounds used for collision checks.");
        }
    }
}
