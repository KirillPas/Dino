// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Core.Editor.Bridge;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace MA.Flora.Editor
{
    class PlaceModeDropdown : EditorToolbarDropdown
    {
        public const string ID = "Instance Tool Context/Place Mode";

        public static readonly string TooltipTitle = L10n.Tr("Placement Mode");
        public static readonly string TooltipSingle = L10n.Tr("Places a single instance of each active prototype.");
        public static readonly string TooltipSingleCycle = L10n.Tr("Places a single instance, cycling through each active prototype.");

        static readonly Texture2D s_SingleIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/PlaceMode Icon.png");
        static readonly Texture2D s_SingleCycleIcon = EditorGUIUtilityBridge.LoadIconRequired("Packages/com.ma.flora/Editor/EditorResources/Icon/PlaceMode Cycle Icon.png");

        readonly GUIContent m_Single;
        readonly GUIContent m_SingleCycle;

        public PlaceModeDropdown()
        {
            name = "Placement Mode";

            m_Single = EditorGUIUtility.TrTextContent(
                L10n.Tr("Single"),
                L10n.Tr($"{TooltipTitle}\n\n{TooltipSingle}"),
                s_SingleIcon);

            m_SingleCycle = EditorGUIUtility.TrTextContent(
                L10n.Tr("Single Cycle"),
                L10n.Tr($"{TooltipTitle}\n\n{TooltipSingleCycle}"),
                s_SingleCycleIcon);

            ToolManager.activeToolChanged += UpdateValues;
            ToolManager.activeContextChanged += UpdateValues;

            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ToolManager.activeToolChanged -= UpdateValues;
                ToolManager.activeContextChanged -= UpdateValues;
            });

            clicked += OpenContextMenu;

            UpdateValues();
            UpdateIcon();
        }

        void UpdateValues()
        {
            if (InstanceTool.Active is InstanceBrushTool brushTool)
            {
                // m_BrushMode = brushTool.Brush.Mode;
                SetEnabled(true);
            }
            else
            {
                SetEnabled(false);
            }
        }

        void UpdateBrushMode(BrushToolMode mode)
        {
            // m_BrushMode = mode;
            UpdateIcon();

            if (InstanceTool.Active is InstanceBrushTool brushTool)
            {
                // brushTool.Brush.Mode = m_BrushMode;
                SetEnabled(true);
            }
            else
            {
                SetEnabled(false);
            }
        }

        void OpenContextMenu()
        {
            GenericMenu menu = new GenericMenu();
            // menu.AddItem(m_Single, m_BrushMode == InstanceBrushMode.Single, () => UpdateBrushMode(InstanceBrushMode.Single));
            // menu.AddItem(m_SingleCycle, m_BrushMode == InstanceBrushMode.SingleCycle, () => UpdateBrushMode(InstanceBrushMode.SingleCycle));
            menu.DropDown(worldBound);
        }

        void UpdateIcon()
        {
            // GUIContent content = m_BrushMode switch
            // {
            //     InstanceBrushMode.Single      => m_Single,
            //     InstanceBrushMode.SingleCycle => m_SingleCycle,
            //     _                          => m_Sphere
            // };

            // text = content.text;
            // tooltip = content.tooltip;
            // icon = content.image as Texture2D;
        }
    }
}