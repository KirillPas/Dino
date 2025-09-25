// Copyright © Magnetic Arcade. All Rights Reserved.
// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using MA.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace MA.Flora
{
    /// <summary>
    /// Supported types of properties that can be instanced.
    /// </summary>
    [Serializable]
    public enum InstancedPropertyType
    {
        /// <summary>A color value.</summary>
        Color    = 0,
        /// <summary>A single floating-point value.</summary>
        Float    = 1,
        /// <summary>A 2D floating-point vector.</summary>
        Float2   = 2,
        /// <summary>A 3D floating-point vector.</summary>
        Float3   = 3,
        /// <summary>A 4D floating-point vector.</summary>
        Float4   = 4,   
        /// <summary>A 2x2 floating-point matrix.</summary>
        Float2x2 = 5,
        /// <summary>A 3x3 floating-point matrix.</summary>
        Float3x3 = 6,
        /// <summary>A 4x4 floating-point matrix.</summary>
        Float4x4 = 7,
        /// <summary>A single integer value.</summary>
        Int      = 20,
        /// <summary>A 2D integer vector.</summary>
        Int2     = 21,
        /// <summary>A 3D integer vector.</summary>
        Int3     = 22,
        /// <summary>A 4D integer vector.</summary>
        Int4     = 23,
        /// <summary>A single unsigned integer value.</summary>
        UInt     = 24,
        /// <summary>A 2D unsigned integer vector.</summary>
        UInt2    = 25,
        /// <summary>A 3D unsigned integer vector.</summary>
        UInt3    = 26,
        /// <summary>A 4D unsigned integer vector.</summary>
        UInt4    = 27,
    }

    /// <summary>
    /// Descriptor of a property that is stored per instance.
    /// </summary>
    [Serializable]
    [DebuggerDisplay("{Name} ({Type})")]
    public unsafe struct InstancedPropertyDescriptor : IEquatable<InstancedPropertyDescriptor>, IComparable<InstancedPropertyDescriptor>
    {
        [SerializeField] InstancedPropertyType m_Type;
        [SerializeField] string m_Name;
        [SerializeField] string m_DOTSMetadataName; // The name of the DOTS metadata property, calculated from the name and type
        [SerializeField] string m_FloraMetadataName; // The name of the flora metadata property, calculated from the name and type
        [SerializeField] float4x4 m_DefaultValue;
        
        const string k_DOTSMetadataNameFormat = "unity_DOTSInstancing{0}_Metadata{1}";
        const string k_FloraMetadataNameFormat = "flora_ProceduralInstancing{0}_Metadata{1}";
        
        /// <summary>Creates a new property descriptor with the specified name and type.</summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="type">The type of the property.</param>
        /// <param name="defaultValue">The default value of the property.</param>
        public InstancedPropertyDescriptor(string name, InstancedPropertyType type, float4x4 defaultValue = default)
        {
            m_Name = name;
            m_Type = type;
            m_DefaultValue = defaultValue;
            m_DOTSMetadataName = string.Format(k_DOTSMetadataNameFormat, name, GetPropertyMetadataTypeName(name, type));
            m_FloraMetadataName = string.Format(k_FloraMetadataNameFormat, name, GetPropertyMetadataTypeName(name, type));
        }

        internal string FloraMetadataName => m_FloraMetadataName;
        internal int FloraMetadataNameID => Shader.PropertyToID(m_FloraMetadataName);
        
        /// <summary>Indicates whether the property descriptor is valid.</summary>
        public bool IsValid => !string.IsNullOrEmpty(m_Name);

        /// <summary>The type of the property.</summary>
        public InstancedPropertyType Type => m_Type;
        
        /// <summary>The name of the property.</summary>
        public string Name => m_Name;

        /// <summary>The shader property ID of the property.</summary>
        public int NameID => Shader.PropertyToID(m_Name);

        /// <summary>The default value of the property.</summary>
        /// <remarks>A 4x4 matrix is used to store the default value of the property, as it is the largest possible type.</remarks>
        public float4x4 DefaultValue => m_DefaultValue;
        
        /// <summary>Number of sizeof(uint) data that the property occupies.</summary>
        public int StrideUInt
        {
            get
            {
                return m_Type switch
                {
                    InstancedPropertyType.Float or InstancedPropertyType.Int or InstancedPropertyType.UInt => 1,
                    InstancedPropertyType.Float2 or InstancedPropertyType.Int2 or InstancedPropertyType.UInt2 => 2,
                    InstancedPropertyType.Float3 or InstancedPropertyType.Int3 or InstancedPropertyType.UInt3 => 3,
                    InstancedPropertyType.Color or InstancedPropertyType.Float4 or InstancedPropertyType.Int4 or InstancedPropertyType.UInt4 => 4,
                    InstancedPropertyType.Float2x2 => 8,
                    InstancedPropertyType.Float3x3 => 12,
                    InstancedPropertyType.Float4x4 => 16,
                    _ => throw new ArgumentOutOfRangeException(nameof(m_Type), m_Type, "Invalid property type.")
                };
            }
        }

        /// <summary>Size of the property data in bytes.</summary>
        public int SizeInBytes => StrideUInt * sizeof(uint);

        /// <summary>Returns the default value of the property, cast to the specified type.</summary>
        /// <typeparam name="T">The type to cast the default value to.</typeparam>
        /// <returns>The default value of the property.</returns>
        /// <exception cref="ArgumentException">Thrown when the size of <typeparamref name="T"/> does not match the size of the property.</exception>
        public T GetDefaultValue<T>() where T : unmanaged
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (sizeof(T) != SizeInBytes)
                throw new ArgumentException($"Size of {typeof(T).Name} does not match the size of the property.");
#endif
            
            fixed (float4x4* defaultValue = &m_DefaultValue)
                return *(T*)defaultValue;
        }
        
        /// <summary>Sets the default value of the property.</summary>
        /// <param name="value">The value to set as the default value.</param>
        /// <typeparam name="T">The type of the value to set.</typeparam>
        /// <exception cref="ArgumentException">Thrown when the size of <typeparamref name="T"/> does not match the size of the property.</exception>
        public void SetDefaultValue<T>(T value) where T : unmanaged
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (sizeof(T) != SizeInBytes)
                throw new ArgumentException($"Size of {typeof(T).Name} does not match the size of the property.");
#endif
            
            fixed (float4x4* defaultValue = &m_DefaultValue)
                *(T*)defaultValue = value;
        }

        /// <summary>Creates a string representation of the property descriptor.</summary>
        public override string ToString() => $"{nameof(InstancedPropertyDescriptor)}: {m_Name} ({m_Type})";

        /// <summary>Compares this property descriptor to another for equality.</summary>
        /// <param name="other">The other property descriptor to compare to.</param>
        /// <returns>True if the property descriptors are equal, false otherwise.</returns>
        public bool Equals(InstancedPropertyDescriptor other) => m_Name.Equals(other.m_Name, StringComparison.InvariantCultureIgnoreCase);

        /// <summary>Compares this property descriptor to an object for equality.</summary>
        /// <param name="obj">The object to compare to.</param>
        /// <returns>True if the object is an <see cref="InstancedPropertyDescriptor"/> and is equal to this property descriptor, false otherwise.</returns>
        public override bool Equals(object obj) => obj is InstancedPropertyDescriptor other && Equals(other);

        /// <summary>Comparison of this property descriptor to another.</summary>
        /// <param name="other">The other property descriptor to compare to.</param>
        /// <returns>A value indicating the relative order of the property descriptors.</returns>
        public int CompareTo(InstancedPropertyDescriptor other) => string.Compare(m_Name, other.m_Name, StringComparison.InvariantCultureIgnoreCase);

        /// <summary>Gets the hash code of the property descriptor.</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((m_Name != null ? m_Name.GetHashCode() : 0) * 397) ^ (int)m_Type;
            }
        }
        
        internal static string GetPropertyMetadataTypeName(string name, InstancedPropertyType type)
        {
            return type switch
            {
                InstancedPropertyType.Float  => "F4",
                InstancedPropertyType.Float2 => "F8",
                InstancedPropertyType.Float3 => "F12",
                InstancedPropertyType.Color or InstancedPropertyType.Float4 => "F16",
                InstancedPropertyType.Float2x2 => "F16",
                InstancedPropertyType.Float3x3 => "F36",
                InstancedPropertyType.Float4x4 => "F64",
                InstancedPropertyType.Int  => "I4",
                InstancedPropertyType.Int2 => "I8",
                InstancedPropertyType.Int3 => "I12",
                InstancedPropertyType.Int4 => "I16",
                InstancedPropertyType.UInt  => "U4",
                InstancedPropertyType.UInt2 => "U8",
                InstancedPropertyType.UInt3 => "U12",
                InstancedPropertyType.UInt4 => "U16",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }
    }
    
    /// <summary>
    /// Runtime representation of an instanced property.
    /// </summary>
    readonly struct RuntimeInstancedProperty : IEquatable<RuntimeInstancedProperty>
    {
        /// <summary>The type of the property.</summary>
        public readonly InstancedPropertyType Type;
        /// <summary>The name ID of the property.</summary>
        public readonly int NameID;
        // /// <summary>The name ID of the DOTS metadata property.</summary>
        // public readonly int DOTSMetadataNameID;
        /// <summary>The name ID of the Flora metadata property.</summary>
        public readonly int FloraMetadataNameID;
        /// <summary>The number of sizeof(uint) data that the property occupies.</summary>
        public readonly int StrideUInt;
        /// <summary>Size of the property data in bytes.</summary>
        public readonly int SizeInBytes => StrideUInt * 4;
        
        /// <summary>Creates a new runtime representation of an instanced property.</summary>
        public RuntimeInstancedProperty(InstancedPropertyDescriptor descriptor)
        {
            Type = descriptor.Type;
            NameID = descriptor.NameID;
            // DOTSMetadataNameID = descriptor.DOTSMetadataNameID;
            FloraMetadataNameID = descriptor.FloraMetadataNameID;
            StrideUInt = descriptor.StrideUInt;
        }

        /// <summary>Compares this runtime property to another for equality.</summary>
        public bool Equals(RuntimeInstancedProperty other) => NameID == other.NameID;

        /// <summary>Compares this runtime property to an object for equality.</summary>
        public override bool Equals(object obj) => obj is RuntimeInstancedProperty other && Equals(other);

        /// <summary>Gets the hash code of the runtime property.</summary>
        public override int GetHashCode() => NameID;

        /// <summary>Creates a string representation of the runtime property.</summary>
        public static bool operator ==(RuntimeInstancedProperty left, RuntimeInstancedProperty right) => left.Equals(right);

        /// <summary>Creates a string representation of the runtime property.</summary>
        public static bool operator !=(RuntimeInstancedProperty left, RuntimeInstancedProperty right) => !left.Equals(right);
    }

    /// <summary>
    /// Maintains a collection of properties that are available for instancing.
    /// </summary>
    [Serializable]
    unsafe class InstancedPropertyArrays : IDisposable, ISerializationCallbackReceiver
    {
        [SerializeField] int m_InstanceCount;
        [SerializeField] int m_PropertyArrayVersion;
        [SerializeField] InstancedPropertyDescriptor[] m_Descriptors = Array.Empty<InstancedPropertyDescriptor>();
        [SerializeField] int[] m_PropertyVersions = Array.Empty<int>();
        
        RuntimeInstancedProperty[] m_RuntimeProperties = Array.Empty<RuntimeInstancedProperty>();
        UnsafeUntypedList[] m_PropertyDataArrays = Array.Empty<UnsafeUntypedList>();
        Dictionary<int, int> m_PropertyLookupByName = new Dictionary<int, int>();
        
        [SerializeField] uint[] m_SerializedData = Array.Empty<uint>();
        [SerializeField] int[] m_SerializedOffsets = Array.Empty<int>();
        
        const int k_Alignment = 64;

        /// <summary>The number of instances that the properties are instanced on.</summary>
        public int InstanceCount => m_InstanceCount;

        /// <summary>The number of properties that are available for instancing.</summary>
        public int PropertyCount => m_Descriptors.Length;

        /// <summary>The version of the property arrays. Changes when properties are added or removed.</summary>
        public int PropertyArrayVersion => m_PropertyArrayVersion;

        /// <summary>True if the property arrays are empty, false otherwise.</summary>
        public bool IsEmpty => PropertyCount == 0 || m_InstanceCount == 0;

        /// <summary>The list of property descriptors.</summary>
        public ReadOnlySpan<InstancedPropertyDescriptor> Descriptors => m_Descriptors;

        /// <summary>The list of property descriptors.</summary>
        public ReadOnlySpan<RuntimeInstancedProperty> RuntimeProperties
        {
            get
            {
                if (m_RuntimeProperties.Length != m_Descriptors.Length)
                {
                    Array.Resize(ref m_RuntimeProperties, m_Descriptors.Length);
                    
                    for (int i = 0; i < m_Descriptors.Length; i++)
                        m_RuntimeProperties[i] = new RuntimeInstancedProperty(m_Descriptors[i]);
                }
                
                return m_RuntimeProperties;
            }
        }

        /// <summary>The list of property data arrays.</summary>
        public ReadOnlySpan<UnsafeUntypedList> DataArrays => m_PropertyDataArrays;

        /// <summary>The list of property versions.</summary>
        public ReadOnlySpan<int> Versions => m_PropertyVersions;

        /// <summary>Disposes of the property data arrays.</summary>
        public void Dispose()
        {
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
                m_PropertyDataArrays[i].Dispose();
        }

        /// <summary>Gets the number of active property arrays.</summary>
        public int GetActiveArrayCount()
        {
            int count = 0;
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
            {
                if (m_PropertyDataArrays[i].IsCreated)
                    count++;
            }
            return count;
        }
        
        // --- Instance Management ---
        
        /// <summary>>Clears the instances on the properties.</summary>
        public void ClearInstances()
        {
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
            {
                if (!m_PropertyDataArrays[i].IsCreated)
                    continue;
                
                m_PropertyDataArrays[i].Dispose();
                m_PropertyVersions[i]++;
            }
            
            m_InstanceCount = 0;
        }
        
        /// <summary>Reserves space for instances on the properties.</summary>
        /// <param name="instanceCount">The number of instances to reserve space for.</param>
        public void ReserveInstances(int instanceCount)
        {
            if (m_InstanceCount >= instanceCount)
                return;
            
            m_InstanceCount = instanceCount;
            
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
            {
                if (!m_PropertyDataArrays[i].IsCreated)
                    continue;
                
                m_PropertyDataArrays[i].Capacity = instanceCount;
            }
        }

        /// <summary>Resizes the number of instances that the properties are instanced on.</summary>
        /// <param name="newInstanceCount">The new number of instances.</param>
        public void ResizeInstances(int newInstanceCount)
        {
            if (m_InstanceCount == newInstanceCount)
                return;
            
            m_InstanceCount = newInstanceCount;
            
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
            {
                if (!m_PropertyDataArrays[i].IsCreated)
                    continue;
                    
                m_PropertyDataArrays[i].Resize(m_InstanceCount);
                m_PropertyVersions[i]++;
            }
        }

        /// <summary>Adds instances to the properties.</summary>
        /// <param name="additionalInstanceCount">The number of instances to add.</param>
        public void AddInstances(int additionalInstanceCount)
        {
            int oldInstanceCount = m_InstanceCount;
            int newInstanceCount = m_InstanceCount + additionalInstanceCount;
            
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
            {
                if (!m_PropertyDataArrays[i].IsCreated)
                    continue;
                
                m_PropertyDataArrays[i].Resize(newInstanceCount);
                m_PropertyVersions[i]++;
                
                float4x4 defaultValue = m_Descriptors[i].GetDefaultValue<float4x4>();
                int strideInBytes = m_Descriptors[i].SizeInBytes;
                byte* src = (byte*)&defaultValue;
                byte* dst = (byte*)m_PropertyDataArrays[i].Ptr + oldInstanceCount * strideInBytes;
                UnsafeUtility.MemCpyReplicate(dst, src, strideInBytes, additionalInstanceCount);
            }
            
            m_InstanceCount = newInstanceCount;
        }

        /// <summary>Removes an instance from the properties.</summary>
        /// <param name="instanceIndex">The index of the instance to remove.</param>
        public void RemoveInstanceSwapBack(int instanceIndex)
        {
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
            {
                if (!m_PropertyDataArrays[i].IsCreated)
                    continue;
                
                m_PropertyDataArrays[i].RemoveAtSwapBack(instanceIndex);
                m_PropertyVersions[i]++;
            }
        }

        /// <summary>Removes a range of instances from the properties.</summary>
        /// <param name="startInstanceIndex">The index of the first instance to remove.</param>
        /// <param name="instanceCount">The number of instances to remove.</param>
        public void RemoveRangeSwapBack(int startInstanceIndex, int instanceCount)
        {
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
            {
                if (!m_PropertyDataArrays[i].IsCreated)
                    continue;
                
                m_PropertyDataArrays[i].RemoveRangeSwapBack(startInstanceIndex, instanceCount);
                m_PropertyVersions[i]++;
            }
        }
        
        // --- Property Value Management ---

        /// <summary>Gets the value of a property for a specific instance.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <returns>The value of the property for the specified instance.</returns>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <exception cref="ArgumentException">Thrown when the property does not exist, or the property value is not of type <typeparamref name="T"/>.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetPropertyValue<T>(int nameID, int instanceIndex) where T : unmanaged
        {
            if (!m_PropertyLookupByName.TryGetValue(nameID, out int propertyIndex))
                throw new ArgumentException($"Property with name ID {nameID} does not exist.");
            
            ref UnsafeUntypedList propertyArray = ref m_PropertyDataArrays[propertyIndex];
            return !propertyArray.IsCreated ? m_Descriptors[propertyIndex].GetDefaultValue<T>() : propertyArray.GetElement<T>(instanceIndex);
        }

        /// <summary>Set the value of a property for a specific instance.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <param name="instanceIndex">The index of the instance.</param>
        /// <param name="value">The value to set the property to.</param>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <exception cref="ArgumentException">Thrown when the property does not exist, or the property value is not of type <typeparamref name="T"/>.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPropertyValue<T>(int nameID, int instanceIndex, T value) where T : unmanaged
        {
            if (!m_PropertyLookupByName.TryGetValue(nameID, out int index))
                throw new ArgumentException($"Property with name ID {nameID} does not exist.");
            
            CreatePropertyIfEmpty(index);
            ref UnsafeUntypedList propertyArray = ref m_PropertyDataArrays[index];
            propertyArray.SetElement(instanceIndex, value);
            m_PropertyVersions[index]++;
        }

        /// <summary>Set the value of a property for a range of instances.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <param name="startInstanceIndex">The index of the first instance to set the property value for.</param>
        /// <param name="values">The values to set the property to.</param>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <exception cref="ArgumentException">Thrown when the property does not exist, or the property value is not of type <typeparamref name="T"/>.</exception>
        public void SetPropertyValueRange<T>(int nameID, int startInstanceIndex, ReadOnlySpan<T> values) where T : unmanaged
        {
            if (!m_PropertyLookupByName.TryGetValue(nameID, out int index))
                throw new ArgumentException($"Property with name ID {nameID} does not exist.");
            
            CreatePropertyIfEmpty(index);
            ref UnsafeUntypedList propertyArray = ref m_PropertyDataArrays[index];
            for (int i = 0; i < values.Length; i++)
                propertyArray.SetElement(startInstanceIndex + i, values[i]);
            
            m_PropertyVersions[index]++;
        }
        
        // --- Property Management ---

        /// <summary>Clears the all properties data.</summary>
        public void ClearProperties()
        {
            for (int i = 0; i < m_PropertyDataArrays.Length; i++)
                m_PropertyDataArrays[i].Dispose();
            
            m_PropertyLookupByName.Clear();
            m_Descriptors = Array.Empty<InstancedPropertyDescriptor>();
            m_PropertyVersions = Array.Empty<int>();
            m_SerializedData = Array.Empty<uint>();
            m_SerializedOffsets = Array.Empty<int>();
            m_PropertyDataArrays = Array.Empty<UnsafeUntypedList>();
            m_RuntimeProperties = Array.Empty<RuntimeInstancedProperty>();
            m_InstanceCount = 0;
            m_PropertyArrayVersion++;
        }

        /// <summary>Sets the properties of the array.</summary>
        /// <param name="newDescriptors">The new properties to set.</param>
        /// <remarks>If a property already exists in the set, its data will be preserved.</remarks>
        public void SetProperties(ReadOnlySpan<InstancedPropertyDescriptor> newDescriptors)
        {
            bool allEqual = m_Descriptors.Length == newDescriptors.Length;
            Span<int> sharedIndices = stackalloc int[newDescriptors.Length];
            for (int i = 0; i < newDescriptors.Length; i++)
            {
                int oldIndex = Array.IndexOf(m_Descriptors, newDescriptors[i]);
                if (i != oldIndex)
                    allEqual = false;
                
                bool typesMatch = oldIndex != -1 && m_Descriptors[oldIndex].Type == newDescriptors[i].Type;
                if (typesMatch)
                    sharedIndices[i] = oldIndex;  // Store the old index, or -1 if not found
            }
            
            if (allEqual)
                return;

            m_PropertyLookupByName.Clear();  // Clearing the lookup to rebuild it

            // Dispose unused property data arrays and increment versions where needed
            for (int i = 0; i < m_Descriptors.Length; i++)
            {
                int newIndex = sharedIndices.IndexOf(i);
                if (newIndex == -1)  // Check if old descriptor is not referenced anymore
                {
                    m_PropertyDataArrays[i].Dispose();
                }
            }

            m_Descriptors = newDescriptors.ToArray();  // Replace newProperties with new ones
            Array.Resize(ref m_PropertyVersions, m_Descriptors.Length);
            Array.Resize(ref m_PropertyDataArrays, m_Descriptors.Length);
            Array.Resize(ref m_RuntimeProperties, m_Descriptors.Length);

            // Rebuild lookup and assign data arrays and versions
            for (int i = 0; i < m_Descriptors.Length; i++)
            {
                m_PropertyLookupByName[m_Descriptors[i].NameID] = i;
                m_RuntimeProperties[i] = new RuntimeInstancedProperty(m_Descriptors[i]);
        
                if (sharedIndices[i] != -1)  // Check if there was a previous descriptor
                {
                    m_PropertyDataArrays[i] = m_PropertyDataArrays[sharedIndices[i]];
                    m_PropertyVersions[i] = m_PropertyVersions[sharedIndices[i]];
                    m_PropertyVersions[i]++;  // Increment the version correctly
                }
                else
                {
                    m_PropertyDataArrays[i] = default;
                    m_PropertyVersions[i] = 0;
                }
            }
        }
        
        /// <summary>Returns true if a property with the specified name ID exists.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <returns>A value indicating whether the property exists.</returns>
        public bool HasProperty(int nameID) => m_PropertyLookupByName.ContainsKey(nameID);
        
        /// <summary>Returns true if a property with the specified name ID exists.</summary>
        /// <param name="name">The name of the property.</param>
        /// <returns>A value indicating whether the property exists.</returns>
        public bool HasProperty(string name) => HasProperty(Shader.PropertyToID(name));

        /// <summary>Adds a property to the list of property arrays.</summary>
        /// <param name="propertyDescriptor">A unique property for the property.</param>
        /// <exception cref="ArgumentException">Thrown when a property with the same name ID already exists.</exception>
        public void AddProperty(InstancedPropertyDescriptor propertyDescriptor)
        {
            if (!propertyDescriptor.IsValid)
                throw new ArgumentException("Descriptor is not valid.", nameof(propertyDescriptor));
            
            if (m_PropertyLookupByName.ContainsKey(propertyDescriptor.NameID))
                throw new ArgumentException($"Property with name ID {propertyDescriptor.NameID} already exists.");
            
            int index = m_Descriptors.Length;
            Array.Resize(ref m_PropertyVersions, index + 1);
            Array.Resize(ref m_Descriptors, index + 1);
            Array.Resize(ref m_PropertyDataArrays, index + 1);
            Array.Resize(ref m_RuntimeProperties, index + 1);
            
            m_PropertyLookupByName[propertyDescriptor.NameID] = index;
            m_PropertyVersions[index] = 0;
            m_Descriptors[index] = propertyDescriptor;
            m_RuntimeProperties[index] = new RuntimeInstancedProperty(propertyDescriptor);
            m_SerializedOffsets[index] = 0;
            m_PropertyArrayVersion++;
        }
        
        /// <summary>Removes a property from the list of property arrays.</summary>
        /// <param name="nameID">The name ID of the property to remove.</param>
        /// <exception cref="ArgumentException">Thrown when the property does not exist.</exception>
        public void RemoveProperty(int nameID)
        {
            if (!m_PropertyLookupByName.Remove(nameID, out int index))
                throw new ArgumentException($"Property with name ID {nameID} does not exist.");
            
            m_PropertyDataArrays[index].Dispose();
            
            int lastIndex = m_Descriptors.Length - 1;
            if (index < lastIndex)
            {
                m_PropertyVersions[index] = m_PropertyVersions[lastIndex];
                m_Descriptors[index] = m_Descriptors[lastIndex];
                m_PropertyDataArrays[index] = m_PropertyDataArrays[lastIndex];
                m_RuntimeProperties[index] = m_RuntimeProperties[lastIndex];
                m_PropertyLookupByName[m_Descriptors[index].NameID] = index;
            }
            
            Array.Resize(ref m_PropertyVersions, lastIndex);
            Array.Resize(ref m_Descriptors, lastIndex);
            Array.Resize(ref m_PropertyDataArrays, lastIndex);
            Array.Resize(ref m_RuntimeProperties, lastIndex);
            m_PropertyArrayVersion++;
        }
        
        /// <summary>Updates the default defaultValue of a property.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <param name="defaultValue">The new default value of the property.</param>
        /// <typeparam name="T">The type of the default value.</typeparam>
        /// <exception cref="ArgumentException">Thrown when the property does not exist.</exception>
        public void UpdateDefaultPropertyValue<T>(int nameID, T defaultValue) where T : unmanaged
        {
            if (!m_PropertyLookupByName.TryGetValue(nameID, out int index))
                throw new ArgumentException($"Property with name ID {nameID} does not exist.");
            
            m_Descriptors[index].SetDefaultValue(defaultValue);
        }
        
        /// <summary>Updates the descriptor of a property.</summary>
        /// <param name="oldPropertyDescriptor">The old descriptor of the property.</param>
        /// <param name="newPropertyDescriptor">The new descriptor of the property.</param>
        /// <exception cref="ArgumentException">Thrown when the old descriptor does not exist, or the new descriptor already exists.</exception>
        public void UpdatePropertyDescriptor(in InstancedPropertyDescriptor oldPropertyDescriptor, in InstancedPropertyDescriptor newPropertyDescriptor)
        {
            if (!oldPropertyDescriptor.IsValid || !newPropertyDescriptor.IsValid)
                throw new ArgumentException("Properties are not valid.");
            
            if (!m_PropertyLookupByName.TryGetValue(oldPropertyDescriptor.NameID, out int index))
                throw new ArgumentException($"Property with name ID {oldPropertyDescriptor.NameID} does not exist.");
            
            bool nameIDChanged = oldPropertyDescriptor.NameID != newPropertyDescriptor.NameID;
            if (nameIDChanged)
            {
                if (m_PropertyLookupByName.ContainsKey(newPropertyDescriptor.NameID))
                    throw new ArgumentException($"Property with name ID {newPropertyDescriptor.NameID} already exists.");
                
                m_PropertyLookupByName.Remove(oldPropertyDescriptor.NameID);
                m_PropertyLookupByName[newPropertyDescriptor.NameID] = index;
            }
            
            bool typeChanged = oldPropertyDescriptor.Type != newPropertyDescriptor.Type;
            if (typeChanged && m_PropertyDataArrays[index].IsCreated)
            {
                m_PropertyDataArrays[index].Dispose();
                CreatePropertyIfEmpty(index);
            }
            
            m_Descriptors[index] = newPropertyDescriptor;
            m_RuntimeProperties[index] = new RuntimeInstancedProperty(newPropertyDescriptor);
            m_PropertyArrayVersion++;
        }
        
        // --- Property Array Management ---

        /// <summary>Ensures that the property array for a property with the specified name ID is created.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <exception cref="ArgumentException">Thrown when the property does not exist.</exception>
        public void EnsureArrayIsCreated(int nameID)
        {
            if (!m_PropertyLookupByName.TryGetValue(nameID, out int index))
                throw new ArgumentException($"Property with name ID {nameID} does not exist.");
            
            CreatePropertyIfEmpty(index);
        }
        
        /// <summary>Tries to get the untyped property array for a property with the specified name ID.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <param name="propertyArray">The property as an untyped list.</param>
        /// <returns>True if the property array was found, false otherwise.</returns>
        public bool TryGetPropertyDataArray(int nameID, out UnsafeUntypedList.ReadOnly propertyArray)
        {
            if (m_InstanceCount > 0 && !m_PropertyLookupByName.TryGetValue(nameID, out int propertyIndex) && m_PropertyDataArrays[propertyIndex].IsCreated)
            {
                propertyArray = m_PropertyDataArrays[propertyIndex].AsReadOnly();
                return true;
            }
            
            propertyArray = default;
            return false;
        }
        
        /// <summary>Tries to get the typed property array for a property with the specified name ID.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <param name="array">The property as a native array.</param>
        /// <returns>True if the property array was found, false otherwise.</returns>
        /// <typeparam name="T">The type of the property array.</typeparam>
        public bool TryGetPropertyDataArray<T>(int nameID, out NativeArray<T>.ReadOnly array) where T : unmanaged
        {
            if (!TryGetPropertyDataArray(nameID, out UnsafeUntypedList.ReadOnly propertyArray))
            {
                array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(propertyArray.Ptr, propertyArray.Length, Allocator.None).AsReadOnly();
                return true;
            }
            
            array = default;
            return false;
        }
        
        /// <summary>Tries to get the typed property array for a property with the specified name ID.</summary>
        /// <param name="nameID">The name ID of the property.</param>
        /// <param name="array">The property as an unsafe array.</param>
        /// <returns>True if the property array was found, false otherwise.</returns>
        /// <typeparam name="T">The type of the property array.</typeparam>
        public bool TryGetPropertyDataArray<T>(int nameID, out UnsafeArray<T>.ReadOnly array) where T : unmanaged
        {
            if (!TryGetPropertyDataArray(nameID, out UnsafeUntypedList.ReadOnly propertyArray))
            {
                array = new UnsafeArray<T>((T*)propertyArray.Ptr, propertyArray.Length).AsReadOnly();
                return true;
            }
            
            array = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CreatePropertyIfEmpty(int propertyIndex)
        {
            if (!m_PropertyDataArrays[propertyIndex].IsCreated)
            {
                m_PropertyDataArrays[propertyIndex] = new UnsafeUntypedList(m_InstanceCount, m_Descriptors[propertyIndex].SizeInBytes, k_Alignment, AllocatorManager.Persistent);
                m_PropertyDataArrays[propertyIndex].Resize(m_InstanceCount);
                float4x4 defaultValue = m_Descriptors[propertyIndex].DefaultValue;
                int strideInBytes = m_Descriptors[propertyIndex].SizeInBytes;
                UnsafeUtility.MemCpyReplicate(m_PropertyDataArrays[propertyIndex].Ptr, &defaultValue, strideInBytes, m_InstanceCount);
                m_PropertyVersions[propertyIndex]++;
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (PropertyCount == 0 || m_InstanceCount == 0)
            {
                m_SerializedData = Array.Empty<uint>();
                m_SerializedOffsets = Array.Empty<int>();
                return;
            }
            
            Span<int> invalidIndices = stackalloc int[m_Descriptors.Length];
            int invalidCount = 0;
            for (int i = 0; i < m_Descriptors.Length; i++)
            {
                if (string.IsNullOrEmpty(m_Descriptors[i].Name) || m_Descriptors[i].SizeInBytes == 0 || m_Descriptors[i].NameID == 0)
                    invalidIndices[invalidCount++] = i;
            }
            
            if (invalidCount > 0)
            {
                for (int i = 0; i < invalidCount; i++)
                {
                    m_PropertyDataArrays[invalidIndices[i]].Dispose();
                    m_PropertyLookupByName.Remove(m_Descriptors[invalidIndices[i]].NameID);
                }
                
                Array.Resize(ref m_Descriptors, m_Descriptors.Length - invalidCount);
                Array.Resize(ref m_PropertyVersions, m_PropertyVersions.Length - invalidCount);
                Array.Resize(ref m_PropertyDataArrays, m_PropertyDataArrays.Length - invalidCount);
            }
            
            int count = m_PropertyDataArrays.Length;
            m_SerializedOffsets = new int[count];
            
            int totalSizeInUInt = 0;
            for (int i = 0; i < count; i++)
            {
                if (!m_PropertyDataArrays[i].IsCreated)
                {
                    m_SerializedOffsets[i] = -1;
                    continue;
                }
                
                int countInUInt = MathUtility.DivideAndRoundUp(m_InstanceCount * m_PropertyDataArrays[i].ElementSize, sizeof(uint));
                m_SerializedOffsets[i] = totalSizeInUInt;
                totalSizeInUInt += countInUInt;
            }
            
            m_SerializedData = new uint[totalSizeInUInt];
            
            fixed (uint* data = m_SerializedData)
            {
                for (int i = 0; i < count; i++)
                {
                    int offsetInUInt = m_SerializedOffsets[i];
                    if (offsetInUInt == -1)
                        continue;
                    
                    int offsetInBytes = offsetInUInt * sizeof(uint);
                    int lengthInBytes = m_PropertyDataArrays[i].ElementSize * m_InstanceCount;
                    
                    byte* src = (byte*)m_PropertyDataArrays[i].Ptr;
                    byte* dst = (byte*)data + offsetInBytes;
                    UnsafeUtility.MemCpy(dst, src, lengthInBytes);
                }
            }
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (PropertyCount == 0 || m_InstanceCount == 0)
            {
                m_SerializedData = Array.Empty<uint>();
                m_SerializedOffsets = Array.Empty<int>();
                m_PropertyLookupByName.Clear();
                return;
            }
            
            int serializedCount = m_SerializedOffsets.Length;
            if (serializedCount == 0)
                return;

            if (m_PropertyDataArrays.Length != serializedCount)
            {
                if (m_PropertyDataArrays.Length > serializedCount)
                {
                    for (int i = m_PropertyDataArrays.Length; i < serializedCount; i++)
                        m_PropertyDataArrays[i].Dispose();
                }
                    
                Array.Resize(ref m_PropertyDataArrays, serializedCount);
            }
            
            if (m_PropertyVersions.Length != serializedCount)
                Array.Resize(ref m_PropertyVersions, serializedCount);
            
            fixed (uint* data = m_SerializedData)
            {
                for (int i = 0; i < serializedCount; i++)
                {
                    int offsetInUInt = m_SerializedOffsets[i];
                    if (offsetInUInt == -1)
                        continue;
                    
                    if (!m_PropertyDataArrays[i].IsCreated)
                        m_PropertyDataArrays[i] = new UnsafeUntypedList(m_InstanceCount, m_Descriptors[i].SizeInBytes, k_Alignment, AllocatorManager.Persistent);
                    
                    m_PropertyDataArrays[i].Resize(m_InstanceCount);
                    
                    int offsetInBytes = offsetInUInt * sizeof(uint);
                    int lengthInBytes = m_PropertyDataArrays[i].ElementSize * m_InstanceCount;
                    
                    byte* src = (byte*)data + offsetInBytes;
                    byte* dst = (byte*)m_PropertyDataArrays[i].Ptr;
                    UnsafeUtility.MemCpy(dst, src, lengthInBytes);
                }
            }
            
            m_PropertyLookupByName.Clear();
            for (int i = 0; i < m_Descriptors.Length; i++)
                m_PropertyLookupByName[m_Descriptors[i].NameID] = i;
            
            if (m_RuntimeProperties.Length != m_Descriptors.Length)
                Array.Resize(ref m_RuntimeProperties, m_Descriptors.Length);
                
            for (int i = 0; i < m_Descriptors.Length; i++)
                m_RuntimeProperties[i] = new RuntimeInstancedProperty(m_Descriptors[i]);
            
            m_SerializedData = Array.Empty<uint>();
            m_SerializedOffsets = Array.Empty<int>();
        }
    }
}