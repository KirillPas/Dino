// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    [FilePath("Library/com.ma.flora/Tools/InstanceToolContextShared", FilePathAttribute.Location.ProjectFolder)]
    sealed class InstanceToolContextShared : ScriptableSingleton<InstanceToolContextShared>, ISerializationCallbackReceiver
    {
        [SerializeField] List<InstancedPrototype> m_SerializedPrototypes = new List<InstancedPrototype>();
        [SerializeField] List<InstancedPrototype> m_SerializedActive = new List<InstancedPrototype>();
        
        [SerializeField] List<InstancedPropertyDescriptor> m_SerializedProperties = new List<InstancedPropertyDescriptor>();
        [SerializeField] List<InstancedPropertyDescriptor> m_SerializedActiveProperties = new List<InstancedPropertyDescriptor>();
            
        HashSet<InstancedPrototype> m_Prototypes = new HashSet<InstancedPrototype>();
        HashSet<InstancedPrototype> m_Active = new HashSet<InstancedPrototype>();
        
        HashSet<InstancedPropertyDescriptor> m_Properties = new HashSet<InstancedPropertyDescriptor>();
        HashSet<InstancedPropertyDescriptor> m_ActiveProperties = new HashSet<InstancedPropertyDescriptor>();

        void OnEnable()
        {
            InstancedPrototype.AnyInstancedPropertyChanged -= UpdateAvailableProperties;
            InstancedPrototype.AnyInstancedPropertyChanged += UpdateAvailableProperties;
        }

        void OnDisable()
        {
            InstancedPrototype.AnyInstancedPropertyChanged -= UpdateAvailableProperties;
        }

        void UpdateAvailableProperties()
        {
            HashSet<InstancedPropertyDescriptor> availableProperties = new HashSet<InstancedPropertyDescriptor>();
            foreach (InstancedPrototype prototype in m_Active)
                foreach (InstancedPropertyDescriptor descriptor in prototype.InstancedProperties)
                    availableProperties.Add(descriptor);
            
            m_Properties = availableProperties;
            
            HashSet<InstancedPropertyDescriptor> activeProperties = new HashSet<InstancedPropertyDescriptor>();
            foreach (InstancedPropertyDescriptor descriptor in m_ActiveProperties)
                if (availableProperties.Contains(descriptor))
                    activeProperties.Add(descriptor);
            
            m_ActiveProperties = activeProperties;
            
            PropertiesChanged?.Invoke();
        }

        public static event Action PrototypesChanged;
        public static event Action ActivePrototypesChanged;
        
        public static event Action PropertiesChanged;
        
        public static List<InstancedPrototype> Prototypes
        {
            get => instance.m_Prototypes.ToList();
            set
            {
                Undo.RecordObject(instance, "Set Context Prototypes");
                
                instance.m_Prototypes.Clear();
                
                foreach (InstancedPrototype prototype in value)
                    instance.m_Prototypes.Add(prototype);
                
                foreach (InstancedPrototype prototype in instance.m_SerializedActive)
                    if (!instance.m_Prototypes.Contains(prototype))
                        instance.m_Active.Remove(prototype);
                
                instance.UpdateAvailableProperties();
                
                PrototypesChanged?.Invoke();
            }
        }
        
        public static List<InstancedPrototype> ActivePrototypes
        {
            get => instance.m_Active.ToList();
            set
            {
                Undo.RecordObject(instance, "Set Context Active Prototypes");
                
                instance.m_Active.Clear();
                
                foreach (InstancedPrototype prototype in value)
                    if (instance.m_Prototypes.Contains(prototype))
                        instance.m_Active.Add(prototype);
                
                instance.UpdateAvailableProperties();
                
                ActivePrototypesChanged?.Invoke();
            }
        }
        
        public static List<InstancedPropertyDescriptor> Properties
        {
            get => instance.m_Properties.ToList();
            set
            {
                Undo.RecordObject(instance, "Set Context Property Properties");
                
                instance.m_Properties.Clear();
                
                foreach (InstancedPropertyDescriptor descriptor in value)
                    instance.m_Properties.Add(descriptor);
                
                PropertiesChanged?.Invoke();
            }
        }
        
        public static List<InstancedPropertyDescriptor> ActiveProperties
        {
            get => instance.m_ActiveProperties.ToList();
            set
            {
                Undo.RecordObject(instance, "Set Context Active Property Properties");
                
                instance.m_ActiveProperties.Clear();
                
                foreach (InstancedPropertyDescriptor descriptor in value)
                    instance.m_ActiveProperties.Add(descriptor);
                
                PropertiesChanged?.Invoke();
            }
        }
        
        public static int CalculateActiveHash()
        {
            int hash = 0;
            foreach (InstancedPrototype prototype in instance.m_Active)
                hash ^= prototype.GetInstanceID() * 397;
            return hash;
        }
        
        public static bool IsActive(InstancedPrototype prototype) => instance.m_Active.Contains(prototype);

        public static void Save() => instance.Save(true);

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            m_SerializedPrototypes.Clear();
            foreach (InstancedPrototype prototype in m_Prototypes)
                if (prototype) m_SerializedPrototypes.Add(prototype);
            
            m_SerializedActive.Clear();
            foreach (InstancedPrototype prototype in m_Active)
                if (prototype) m_SerializedActive.Add(prototype);
            
            m_SerializedProperties.Clear();
            foreach (InstancedPropertyDescriptor descriptor in m_Properties)
                if (!string.IsNullOrEmpty(descriptor.Name))
                    m_SerializedProperties.Add(descriptor);
            
            m_SerializedActiveProperties.Clear();
            foreach (InstancedPropertyDescriptor descriptor in m_ActiveProperties)
                if (!string.IsNullOrEmpty(descriptor.Name))
                    m_SerializedActiveProperties.Add(descriptor);
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            m_Prototypes.Clear();
            for (int i = 0; i < m_SerializedPrototypes.Count; ++i)
                if (m_SerializedPrototypes[i])
                    m_Prototypes.Add(m_SerializedPrototypes[i]);
            
            m_Active.Clear();
            for (int i = 0; i < m_SerializedActive.Count; ++i)
                if (m_SerializedActive[i] && m_Prototypes.Contains(m_SerializedActive[i]))
                    m_Active.Add(m_SerializedActive[i]);
            
            m_Properties.Clear();
            for (int i = 0; i < m_SerializedProperties.Count; ++i)
                if (!string.IsNullOrEmpty(m_SerializedProperties[i].Name))
                    m_Properties.Add(m_SerializedProperties[i]);
            
            m_ActiveProperties.Clear();
            for (int i = 0; i < m_SerializedActiveProperties.Count; ++i)
                if (m_Properties.Contains(m_SerializedActiveProperties[i]))
                    m_ActiveProperties.Add(m_SerializedActiveProperties[i]);
            
            UpdateAvailableProperties();
        }
    }
}
