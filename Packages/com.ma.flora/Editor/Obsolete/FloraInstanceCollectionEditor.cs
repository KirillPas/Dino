// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using UnityEditor;

namespace MA.Flora.Editor.Obsolete
{
    [Obsolete]
    [CustomEditor(typeof(FloraInstanceCollection))]
    class FloraInstanceCollectionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() { }
    }
}