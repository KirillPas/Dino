// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using MA.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace MA.Flora
{
    /// <summary>
    /// A serializable asset wrapper for a list of <see cref="FloraInstance"/>.
    /// </summary>
    [PreferBinarySerialization]
    [Obsolete]
    public sealed class FloraInstanceCollection : ScriptableObject, IEnumerable<FloraInstance>, ISerializationCallbackReceiver
    {
        enum Version
        {
            None,
            Initial,
        }
        
        [SerializeField] LeanList<FloraInstance> m_Instances = new LeanList<FloraInstance>();

        public int Count => m_Instances.Count;

        public event Action OnBeforeSerializeEvent; 
        public event Action OnAfterDeserializeEvent;
        
        public int ChangeVersion
        {
            get => m_ChangeVersion;
            set => m_ChangeVersion = value;
        }
        [SerializeField] int m_ChangeVersion;
        
        public void IncrementChangeVersion() => throw new Exception("FloraInstanceCollection is obsolete.");
        
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            OnBeforeSerializeEvent?.Invoke();
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            OnAfterDeserializeEvent?.Invoke();
        }

        public ref FloraInstance this[int index] => ref m_Instances[index];

        public void ReserveAdditional(int count) => throw new Exception("FloraInstanceCollection is obsolete.");
        public void Resize(int newSize) => throw new Exception("FloraInstanceCollection is obsolete.");
        public void Clear() => throw new Exception("FloraInstanceCollection is obsolete.");
        public void TrimExcess() => throw new Exception("FloraInstanceCollection is obsolete.");

        public void CopyFrom(FloraInstanceCollection collection) => throw new Exception("FloraInstanceCollection is obsolete.");
        public void CopyFrom(ReadOnlySpan<FloraInstance> instances) => throw new Exception("FloraInstanceCollection is obsolete.");
        
        public Span<FloraInstance> AsSpan() => m_Instances.AsSpan();
        public ReadOnlySpan<FloraInstance> AsReadOnlySpan() => m_Instances.AsReadOnlySpan();
        
        public LeanList<FloraInstance>.Enumerator GetEnumerator() => m_Instances.GetEnumerator();
        IEnumerator<FloraInstance> IEnumerable<FloraInstance>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public PinnedArrayView<FloraInstance> Pin() => new PinnedArrayView<FloraInstance>(m_Instances);

        #region Obsolete Fields
        [SerializeField, Obsolete, HideInInspector, FormerlySerializedAs("Instances"), UsedImplicitly] 
        FloraInstance[] m_ObsoleteInstances = Array.Empty<FloraInstance>();
        #endregion
    }
}