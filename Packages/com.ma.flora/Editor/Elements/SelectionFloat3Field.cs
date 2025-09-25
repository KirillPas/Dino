// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine.UIElements;

#if !UNITY_2022_1_OR_NEWER
using UnityEditor.UIElements;
#endif

namespace MA.Flora.Editor
{
    class SelectionFloat3Field : Vector3Field
    {
        static readonly List<float3> s_Float3Buffer = new List<float3>();
        static readonly InstanceGUIUtility.EqualityComparer<float3> s_ComparerX = (a, b) => a.x.Equals(b.x);
        static readonly InstanceGUIUtility.EqualityComparer<float3> s_ComparerY = (a, b) => a.y.Equals(b.y);
        static readonly InstanceGUIUtility.EqualityComparer<float3> s_ComparerZ = (a, b) => a.z.Equals(b.z);
        static readonly string k_ApplyInstanceTransform = L10n.Tr("Modify Instance Transform");

        readonly FloatField m_X;
        readonly FloatField m_Y;
        readonly FloatField m_Z;

        readonly Func<InstanceSelectionGroup, int, float3> m_Get;
        readonly Action<InstanceSelectionGroup, int, float3> m_Set;

        List<InstanceSelectionGroup> m_SelectionGroups = new List<InstanceSelectionGroup>(0);

        public event Action Changed;

        public SelectionFloat3Field(string label, Func<InstanceSelectionGroup, int, float3> get, Action<InstanceSelectionGroup, int, float3> set) : base(label)
        {
            m_Get = get;
            m_Set = set;

            m_X = this.Q<FloatField>("unity-x-input");
            m_Y = this.Q<FloatField>("unity-y-input");
            m_Z = this.Q<FloatField>("unity-z-input");

            m_X.RegisterValueChangedCallback(ApplyX);
            m_Y.RegisterValueChangedCallback(ApplyY);
            m_Z.RegisterValueChangedCallback(ApplyZ);
        }

        public void Update(InstanceSelectionGroup[] groups)
        {
            m_SelectionGroups.Clear();
            for (int i = 0; i < groups.Length; ++i)
            {
                if (!groups[i].IsEmpty)
                    m_SelectionGroups.Add(groups[i]);
            }
            
            s_Float3Buffer.Clear();
            for (int i = 0; i < m_SelectionGroups.Count; ++i)
            {
                foreach (int instanceIndex in m_SelectionGroups[i].Indices)
                    s_Float3Buffer.Add(m_Get.Invoke(m_SelectionGroups[i], instanceIndex));
            }
            
            float3 value = s_Float3Buffer.Count > 0 ? s_Float3Buffer[0] : 0;
            m_X.showMixedValue = InstanceGUIUtility.HasMultipleValues(s_Float3Buffer, s_ComparerX);
            if (!m_X.showMixedValue)
                m_X.SetValueWithoutNotify(value[0]);

            m_Y.showMixedValue = InstanceGUIUtility.HasMultipleValues(s_Float3Buffer, s_ComparerY);
            if (!m_Y.showMixedValue)
                m_Y.SetValueWithoutNotify(value[1]);

            m_Z.showMixedValue = InstanceGUIUtility.HasMultipleValues(s_Float3Buffer, s_ComparerZ);
            if (!m_Z.showMixedValue)
                m_Z.SetValueWithoutNotify(value[2]);
        }

        void ApplyX(ChangeEvent<float> evt)
        {
            Undo.RecordObjects(InstanceSelectionGroup.GetSelectedRenderers(), k_ApplyInstanceTransform);

            InstanceInspectorView.IgnoreModificationCallbacks = true;
            for (int i = 0; i < m_SelectionGroups.Count; ++i)
            {
                foreach (int instanceIndex in m_SelectionGroups[i].Indices)
                {
                    float3 value = m_Get.Invoke(m_SelectionGroups[i], instanceIndex);
                    value.x = evt.newValue;
                    m_Set.Invoke(m_SelectionGroups[i], instanceIndex, value);
                }
            }

            m_X.showMixedValue = false;
            m_X.SetValueWithoutNotify(evt.newValue);
            Changed?.Invoke();
            InstanceInspectorView.IgnoreModificationCallbacks = false;
        }

        void ApplyY(ChangeEvent<float> evt)
        {
            Undo.RecordObjects(InstanceSelectionGroup.GetSelectedRenderers(), k_ApplyInstanceTransform);
            
            InstanceInspectorView.IgnoreModificationCallbacks = true;
            for (int i = 0; i < m_SelectionGroups.Count; ++i)
            {
                foreach (int instanceIndex in m_SelectionGroups[i].Indices)
                {
                    float3 value = m_Get.Invoke(m_SelectionGroups[i], instanceIndex);
                    value.y = evt.newValue;
                    m_Set.Invoke(m_SelectionGroups[i], instanceIndex, value);
                }
            }
            
            m_Y.showMixedValue = false;
            m_Y.SetValueWithoutNotify(evt.newValue);
            Changed?.Invoke();
            InstanceInspectorView.IgnoreModificationCallbacks = false;
        }

        void ApplyZ(ChangeEvent<float> evt)
        {
            Undo.RecordObjects(InstanceSelectionGroup.GetSelectedRenderers(), k_ApplyInstanceTransform);
            
            InstanceInspectorView.IgnoreModificationCallbacks = true;
            for (int i = 0; i < m_SelectionGroups.Count; ++i)
            {
                foreach (int instanceIndex in m_SelectionGroups[i].Indices)
                {
                    float3 value = m_Get.Invoke(m_SelectionGroups[i], instanceIndex);
                    value.z = evt.newValue;
                    m_Set.Invoke(m_SelectionGroups[i], instanceIndex, value);
                }
            }
            
            m_Z.showMixedValue = false;
            m_Z.SetValueWithoutNotify(evt.newValue);
            Changed?.Invoke();
            InstanceInspectorView.IgnoreModificationCallbacks = false;
        }
    }
}