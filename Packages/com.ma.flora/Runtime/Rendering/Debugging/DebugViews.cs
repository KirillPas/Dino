// Copyright © Magnetic Arcade. All Rights Reserved.

using MA.Collections;
using UnityEngine;

namespace MA.Flora.Rendering
{
    class InstancedCameraIDDebugView
    {
        InstancedCameraID m_ID;

        public InstancedCameraIDDebugView(InstancedCameraID id) => m_ID = id;

        public int Value => m_ID.Value;

        public Camera Camera
        {
            get
            {
                if (m_ID.IsCreated && InstancingSystem.IsActive())
                    return InstancingSystem.Instance.Context.CameraManager.GetCamera(m_ID);

                return null;
            }
        }

        public string Name => Camera != null ? Camera.name : "(null)";

        public override string ToString() => m_ID.IsCreated ? $"({Name}:{Value})" : $"{m_ID.GetType().Name}.Null";
    }

    class InstancedMaterialIDDebugView
    {
        InstancedMaterialID m_ID;

        public InstancedMaterialIDDebugView(InstancedMaterialID id) => m_ID = id;

        public Handle Value => m_ID.Handle;

        public Material Material
        {
            get
            {
                if (m_ID.IsCreated && InstancingSystem.IsActive())
                    return InstancingSystem.Instance.Context.MaterialManager.GetMaterial(m_ID);

                return null;
            }
        }

        public string Name => Material != null ? Material.name : "(null)";

        public override string ToString() => m_ID.IsCreated ? $"({Name}:{Value})" : $"{m_ID.GetType().Name}.Null";
    }

    class InstancedMeshIDDebugView
    {
        InstancedMeshID m_ID;

        public InstancedMeshIDDebugView(InstancedMeshID id) => m_ID = id;

        public Handle Value => m_ID.Handle;

        public Mesh Mesh
        {
            get
            {
                if (m_ID.IsCreated && InstancingSystem.IsActive())
                    return InstancingSystem.Instance.Context.MeshManager.GetMesh(m_ID);

                return null;
            }
        }

        public string Name => Mesh != null ? Mesh.name : "(null)";

        public override string ToString() => m_ID.IsCreated ? $"({Name}:{Value})" : $"{m_ID.GetType().Name}.Null";
    }

    class InstancedPrototypeIDDebugView
    {
        InstancedPrototypeID m_ID;

        public InstancedPrototypeIDDebugView(InstancedPrototypeID id) => m_ID = id;

        public InstancedPrototypeID Value => m_ID;

        public InstancedPrototype Prototype
        {
            get
            {
                if (m_ID.IsCreated && InstancingSystem.IsActive())
                    return InstancingSystem.Instance.Context.PrototypeManager.GetPrototype(m_ID);

                return null;
            }
        }

        public string Name => Prototype != null ? Prototype.name : "(null)";

        public override string ToString() => m_ID.IsCreated ? $"({Name}:{Value})" : $"{m_ID.GetType().Name}.Null";
    }

    class InstancedRendererIDDebugView
    {
        InstancedRendererID m_ID;

        public InstancedRendererIDDebugView(InstancedRendererID id) => m_ID = id;

        public int Value => m_ID.Value;

        public GameObject Parent
        {
            get
            {
                if (InstancingSystem.IsActive() && m_ID.IsCreated && InstancingSystem.Instance.Context.RendererManager.Exists(m_ID))
                    return InstancingSystem.Instance.Context.RendererManager.GameObjects[m_ID.Value];

                return null;
            }
        }

        public IInstancedRenderer Renderer
        {
            get
            {
                if (InstancingSystem.IsActive() && m_ID.IsCreated && InstancingSystem.Instance.Context.RendererManager.Exists(m_ID))
                    return InstancingSystem.Instance.Context.RendererManager.Renderers[m_ID.Value];

                return null;
            }
        }

        public string Name
        {
            get
            {
                if (InstancingSystem.IsActive() && m_ID.IsCreated && InstancingSystem.Instance.Context.RendererManager.Exists(m_ID))
                    return InstancingSystem.Instance.Context.RendererManager.GameObjects[m_ID.Value].name;

                return $"{Value}";
            }
        }

        public override string ToString() => m_ID.IsCreated ? $"({Name}:{Value})" : $"{m_ID.GetType().Name}.Null";
    }

    class InstancedBatchIDDebugView
    {
        InstancedBatchID m_ID;

        public InstancedBatchIDDebugView(InstancedBatchID id) => m_ID = id;

        public int Value => m_ID.Value;

        public InstancedBatchDescriptor Descriptor
        {
            get
            {
                if (m_ID.IsValid && InstancingSystem.IsActive())
                    return InstancingSystem.Instance.Context.BatchManager.GetBatchDescription(m_ID);

                return default;
            }
        }

        public override string ToString() => m_ID.IsValid ? $"({Descriptor}:{Value})" : $"{m_ID.GetType().Name}.Null";
    }
}
