// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MA.Flora.Editor
{
    [CustomPreview(typeof(InstancedTerrainFoliage))]
    class InstancedTerrainFoliagePreview : DefaultGameObjectPreview
    {
        public override bool HasPreviewGUI() => TerrainPrototypePreviewUI.Selected.Count > 0;

        public override void Initialize(Object[] targets)
        {
            TerrainPrototypePreviewUI.SelectedChanged += InitSelected;
            InitSelected();
        }

        public override void Cleanup()
        {
            TerrainPrototypePreviewUI.SelectedChanged -= InitSelected;
            base.Cleanup();
        }

        void InitSelected()
        {
            ResetTarget();
            GameObject[] gameObjects = TerrainPrototypePreviewUI.Selected
                .Where(p => p != null)
                .Select(p => p.gameObject)
                .ToArray();

            base.Initialize(gameObjects);
        }
    }
}
