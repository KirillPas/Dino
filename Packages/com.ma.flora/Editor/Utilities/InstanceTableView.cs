// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    class InstanceTableView
    {
        LightingExplorerTab m_TableTab;
        
        public InstanceTableView(
            string title,
            Func<UnityEngine.Object[]> objects,
            Func<LightingExplorerTableColumn[]> columns,
            bool showFilterGUI)
        {
            m_TableTab = new LightingExplorerTab(title, objects, columns, showFilterGUI);
        }
        
        public GUIContent Title 
            => s_TabGetTitle(m_TableTab);
        
        public void OnDisable()
            => s_TabOnDisable(m_TableTab);

        public void OnInspectorUpdate()
            => s_TabOnInspectorUpdate(m_TableTab);

        public void OnSelectionChange()
            => s_TabOnSelectionChange(m_TableTab);

        public void OnSelectionChange(int[] instanceIDs)
            => s_TabOnSelectionIndicesChanged(m_TableTab, instanceIDs);

        public void OnHierarchyChange() 
            => s_TabOnHierarchyChange(m_TableTab);

        public void OnGUI()
            => s_TabOnGUI(m_TableTab);
        
        // --- LightingExplorerTab Reflection ---
        
        static Action<LightingExplorerTab> s_TabOnDisable;
        static Action<LightingExplorerTab> s_TabOnInspectorUpdate;
        static Action<LightingExplorerTab> s_TabOnSelectionChange;
        static Action<LightingExplorerTab, int[]> s_TabOnSelectionIndicesChanged;
        static Action<LightingExplorerTab> s_TabOnHierarchyChange;
        static Action<LightingExplorerTab> s_TabOnGUI;
        static Func<LightingExplorerTab, GUIContent> s_TabGetTitle;

        static InstanceTableView()
        {
            s_TabOnDisable = (Action<LightingExplorerTab>)typeof(LightingExplorerTab)
                .GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(Action<LightingExplorerTab>));
            
            s_TabOnInspectorUpdate = (Action<LightingExplorerTab>)typeof(LightingExplorerTab)
                .GetMethod("OnInspectorUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(Action<LightingExplorerTab>));

            s_TabOnSelectionChange = (Action<LightingExplorerTab>)typeof(LightingExplorerTab)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .First(m => m.Name == "OnSelectionChange" && m.GetParameters().Length == 0)
                .CreateDelegate(typeof(Action<LightingExplorerTab>));
            
            s_TabOnSelectionIndicesChanged = (Action<LightingExplorerTab, int[]>)typeof(LightingExplorerTab)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .First(m => m.Name == "OnSelectionChange" && m.GetParameters().Length == 1)
                .CreateDelegate(typeof(Action<LightingExplorerTab, int[]>));
            
            s_TabOnHierarchyChange = (Action<LightingExplorerTab>)typeof(LightingExplorerTab)
                .GetMethod("OnHierarchyChange", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(Action<LightingExplorerTab>));
            
            s_TabOnGUI = (Action<LightingExplorerTab>)typeof(LightingExplorerTab)
                .GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(Action<LightingExplorerTab>));
            
            s_TabGetTitle = (Func<LightingExplorerTab, GUIContent>)typeof(LightingExplorerTab)
                .GetProperty("title", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod
                .CreateDelegate(typeof(Func<LightingExplorerTab, GUIContent>));
        }
    }
}