// Copyright © Magnetic Arcade. All Rights Reserved.

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    class InstanceToolContextSettingsWindow : EditorWindow
    {
        const float k_BorderWidth = 1;

        Toggle m_DisableDynamicColliders;
        Toggle m_DisableDynamicDensity;
        Toggle m_DisableCullDistance;

        public static void Show(Rect buttonRect)
        {
            InstanceToolContextSettingsWindow window = CreateInstance<InstanceToolContextSettingsWindow>();
            window.hideFlags = HideFlags.DontSave;
#if UNITY_2022_1_OR_NEWER
            int popupWidth = 190;
#else
            var popupWidth = 220;
#endif 
            window.ShowAsDropDown(GUIUtility.GUIToScreenRect(buttonRect), new Vector2(popupWidth, 80));
        }

        void OnEnable()
        {
            Color borderColor = EditorGUIUtility.isProSkin ? new Color(0.44f, 0.44f, 0.44f, 1f) : new Color(0.51f, 0.51f, 0.51f);

            rootVisualElement.style.borderLeftWidth = k_BorderWidth;
            rootVisualElement.style.borderTopWidth = k_BorderWidth;
            rootVisualElement.style.borderRightWidth = k_BorderWidth;
            rootVisualElement.style.borderBottomWidth = k_BorderWidth;
            rootVisualElement.style.borderLeftColor = borderColor;
            rootVisualElement.style.borderTopColor = borderColor;
            rootVisualElement.style.borderRightColor = borderColor;
            rootVisualElement.style.borderBottomColor = borderColor;
            
            m_DisableDynamicColliders = new Toggle(L10n.Tr("Disable Dynamic Colliders"));
            m_DisableDynamicColliders.style.flexDirection = FlexDirection.RowReverse;
            rootVisualElement.Add(m_DisableDynamicColliders);

            m_DisableDynamicDensity = new Toggle(L10n.Tr("Disable Density Culling"));
            m_DisableDynamicDensity.style.flexDirection = FlexDirection.RowReverse;
            rootVisualElement.Add(m_DisableDynamicDensity);
            
            m_DisableCullDistance = new Toggle(L10n.Tr("Disable Render Distance"));
            m_DisableCullDistance.style.flexDirection = FlexDirection.RowReverse;
            rootVisualElement.Add(m_DisableCullDistance);

            m_DisableDynamicColliders.RegisterValueChangedCallback((evt) =>
            {
                if (InstanceToolContext.IsActive)
                {
                    InstanceToolContext.Active.DisableDynamicColliders = evt.newValue;
                    SceneView.RepaintAll();
                }
            });

            m_DisableDynamicDensity.RegisterValueChangedCallback((evt) =>
            {
                if (InstanceToolContext.IsActive)
                {
                    InstanceToolContext.Active.DisableDensityCulling = evt.newValue;
                    SceneView.RepaintAll();
                }
            });
            
            m_DisableCullDistance.RegisterValueChangedCallback((evt) =>
            {
                if (InstanceToolContext.IsActive)
                {
                    InstanceToolContext.Active.DisableRenderDistance = evt.newValue;
                    SceneView.RepaintAll();
                }
            });

            UpdateValues();
        }


        void UpdateValues()
        {
            if (InstanceToolContext.IsActive)
            {
                m_DisableDynamicColliders.SetValueWithoutNotify(InstanceToolContext.Active.DisableDynamicColliders);
                m_DisableDynamicDensity.SetValueWithoutNotify(InstanceToolContext.Active.DisableDensityCulling);
                m_DisableCullDistance.SetValueWithoutNotify(InstanceToolContext.Active.DisableRenderDistance);
                SceneView.RepaintAll();
            }
        }
    }
}