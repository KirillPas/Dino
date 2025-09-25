// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using UnityEditor;

namespace MA.Flora.Editor.Obsolete
{
    [Obsolete]
    [CustomEditor(typeof(FloraCell))]
    class FloraCellEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() { }
    }
}