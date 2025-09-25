// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MA.Collections.Unsafe;
using MA.Flora.Rendering.Builtin;
using MA.Flora.Rendering.Occlusion;
using MA.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Plane = MA.Mathematics.Plane;

#if !UNITY_2022_3_OR_NEWER
using MA.Core;
#endif

namespace MA.Flora.Rendering
{
    [Flags]
    enum InstancedCameraFlags
    {
        None                = 0,
        PersistentCamera    = 1 << 0,
        SceneViewCamera     = 1 << 1,
        CPUOcclusionCulling = 1 << 3,
        GPUOcclusionCulling = 1 << 4,
        DisableRendering    = 1 << 5,
    }

    static class InstancedCameraFlagsHelpers
    {
        public static bool IsPersistentCamera(this InstancedCameraFlags flags) => (flags & InstancedCameraFlags.PersistentCamera) != 0;
        public static bool IsSceneViewCamera(this InstancedCameraFlags flags) => (flags & InstancedCameraFlags.SceneViewCamera) != 0;
        public static bool HasCPUOcclusionCulling(this InstancedCameraFlags flags) => (flags & InstancedCameraFlags.CPUOcclusionCulling) != 0;
        public static bool HasGPUOcclusionCulling(this InstancedCameraFlags flags) => (flags & InstancedCameraFlags.GPUOcclusionCulling) != 0;
        public static bool IsRenderingDisabled(this InstancedCameraFlags flags) => (flags & InstancedCameraFlags.DisableRendering) != 0;
        public static bool IsPersistentAndDisabled(this InstancedCameraFlags flags) => flags.IsPersistentCamera() && flags.IsRenderingDisabled();
    }

    [DebuggerTypeProxy(typeof(InstancedCameraIDDebugView))]
    struct InstancedCameraID : IEquatable<InstancedCameraID>
    {
        public static readonly InstancedCameraID Null = new InstancedCameraID { Value = 0 };

        public int Value;

        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InstancedCameraID(int value) => Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Value.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is InstancedCameraID other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstancedCameraID other) => (int)Value == (int)other.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstancedCameraID other) => Value.CompareTo(other.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(InstancedCameraID id) => id.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstancedCameraID a, InstancedCameraID b) => a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstancedCameraID a, InstancedCameraID b) => !a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            if (InstancingSystem.IsActive() && IsCreated && InstancingSystem.Instance.Context.CameraManager.Exists(this))
                return InstancingSystem.Instance.Context.CameraManager.Cameras[Value].name;

            return Value == 0 ? $"{nameof(InstancedCameraID)}.Null" : Value.ToString();
        }
    }

    struct InstancedCameraCullingData
    {
        public InstancedCameraFlags Flags;
        public uint CullingLayerMask;
        public ulong SceneCullingMask;
        public float FarClipPlane;
        public UnsafeArray<Plane> CullingPlanes;
        public UnsafeArray<FrustumSIMDPacket> CullingPlanePackets;
    }

    struct InstancedCameraPositionData
    {
        public float3 Origin;
        public float3 PrevOrigin;
        public float DistanceMoved;
        public ushort FixedDistanceMoved;
    }

    struct InstancedCameraLODData
    {
        public float3 Origin;
        public bool IsOrthographic;
        public float MinimumScreenSize;
        public float LODGlobalBias;
        public float ScreenRelativeMetric;
        public float ScreenRelativeMetricSq => ScreenRelativeMetric * ScreenRelativeMetric;
    }

    struct InstancedCameraShadowData
    {
        [MarshalAs(UnmanagedType.U1)] public bool ShadowEnabled;
        public UnsafeArray<Plane> CullingPlanes;
        public UnsafeArray<FrustumSIMDPacket> CullingPlanePackets;
        public UnsafeArray<Plane> LightFacingPlanes;
        public UnsafeArray<Plane> SilhouettePlanes;
        public float3x3 WorldToLightSpaceRotation;
        public float4 ReceiverSphereInLightSpace;
    }

    struct InstancedCameraAnimatedCrossFadeData
    {
        public float3 ViewPosition0;
        public float3 ViewPosition1;
        public float Time0;
        public float Time1;
        public float Duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetTransitionAlpha(float time)
        {
            return Duration == 0.0f ? 0.0f : math.saturate((time - Duration - Time0) / (Time1 - Time0));
        }

        public void Update(float nextTime, float duration, float3 viewPosition)
        {
            bool reset = duration <= 0;
            if (!reset)
            {
                if (Time1 < (nextTime - duration))
                {
                    if (Time0 < Time1)
                    {
                        ViewPosition0 = ViewPosition1;
                        Time0 = Time1;
                    }

                    ViewPosition1 = viewPosition;
                    Time1 = nextTime;
                    if (Time1 <= Time0)
                        reset = true;
                }
            }

            if (reset)
            {
                ViewPosition0 = ViewPosition1 = viewPosition;
                Time0 = Time1 = nextTime;
            }

            Duration = duration;
        }
    }

    struct InstancedCameraArrays : IDisposable
    {
        UnsafeArray<int> m_CountCapacity;

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_CountCapacity[0];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => m_CountCapacity[0] = value;
        }

        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_CountCapacity[1];
        }

        public UnsafeArray<int> LastFrameUpdated;
        public UnsafeArray<InstancedCameraPositionData> Position;
        public UnsafeArray<InstancedCameraCullingData> Culling;
        public UnsafeArray<InstancedCameraLODData> LOD;
        public UnsafeArray<InstancedCameraShadowData> Shadow;
        public UnsafeArray<InstancedCameraAnimatedCrossFadeData> AnimatedCrossFade;

        public UnsafeBitList RenderedLastFrame;
        public UnsafeBitList Rendering;

        public InstancedCameraArrays(int capacity)
        {
            m_CountCapacity = new UnsafeArray<int>(2, AllocatorManager.Persistent);
            m_CountCapacity[0] = 0;
            m_CountCapacity[1] = capacity;

            LastFrameUpdated = new UnsafeArray<int>(capacity, AllocatorManager.Persistent);
            Position = new UnsafeArray<InstancedCameraPositionData>(capacity, AllocatorManager.Persistent);
            Culling = new UnsafeArray<InstancedCameraCullingData>(capacity, AllocatorManager.Persistent);
            LOD = new UnsafeArray<InstancedCameraLODData>(capacity, AllocatorManager.Persistent);
            Shadow = new UnsafeArray<InstancedCameraShadowData>(capacity, AllocatorManager.Persistent);
            AnimatedCrossFade = new UnsafeArray<InstancedCameraAnimatedCrossFadeData>(capacity, AllocatorManager.Persistent);

            Rendering = new UnsafeBitList(capacity, AllocatorManager.Persistent);
            RenderedLastFrame = new UnsafeBitList(capacity, AllocatorManager.Persistent);
        }

        public void Dispose()
        {
            m_CountCapacity.Dispose();
            LastFrameUpdated.Dispose();
            Position.Dispose();
            Culling.Dispose();
            LOD.Dispose();
            Shadow.Dispose();
            AnimatedCrossFade.Dispose();

            Rendering.Dispose();
            RenderedLastFrame.Dispose();
        }

        public void EnsureCapacity(int newCapacity)
        {
            if (newCapacity <= Capacity)
                return;

            LastFrameUpdated.Resize(newCapacity, AllocatorManager.Persistent);
            Position.Resize(newCapacity, AllocatorManager.Persistent);
            Culling.Resize(newCapacity, AllocatorManager.Persistent);
            LOD.Resize(newCapacity, AllocatorManager.Persistent);
            Shadow.Resize(newCapacity, AllocatorManager.Persistent);
            AnimatedCrossFade.Resize(newCapacity, AllocatorManager.Persistent);

            Rendering.Resize(newCapacity);
            RenderedLastFrame.Resize(newCapacity);

            m_CountCapacity[1] = newCapacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnly AsReadOnly() => new(this);

        public struct ReadOnly
        {
            public UnsafeArray<InstancedCameraPositionData>.ReadOnly Position;
            public UnsafeArray<InstancedCameraCullingData>.ReadOnly Culling;
            public UnsafeArray<InstancedCameraLODData>.ReadOnly LOD;
            public UnsafeArray<InstancedCameraShadowData>.ReadOnly Shadow;
            public UnsafeArray<InstancedCameraAnimatedCrossFadeData>.ReadOnly AnimatedCrossFade;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ReadOnly(InstancedCameraArrays arrays)
            {
                Position = arrays.Position.AsReadOnly();
                Culling = arrays.Culling.AsReadOnly();
                LOD = arrays.LOD.AsReadOnly();
                Shadow = arrays.Shadow.AsReadOnly();
                AnimatedCrossFade = arrays.AnimatedCrossFade.AsReadOnly();
            }
        }
    }

    unsafe struct InstancedCameraUpdatePacket
    {
        [ReadOnly, NoAlias] public ScriptableCullingParameters* CameraParameters;

        public InstancedCameraFlags Flags;
        public int FrameCount;
        public float Time;
        public float4x4 LocalToWorldMatrix;
        public float4x4 ProjectionMatrix;
        public ulong SceneCullingMask;

        public float FarClipPlane;
        public float LODGlobalBias;
        public float MinimumScreenSize;
        public float CrossFadeDuration;

        public float ShadowNear;
        public float ShadowDistance;
        public float3x3 LightToWorldRotation;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InstancedCameraUpdatePacket* Create(InstancingContext context, Camera camera, ref InstancingCameraSettings cameraSettings, AllocatorManager.AllocatorHandle allocator)
        {
            bool isInvalid = camera == null;
            if (isInvalid)
                return null;

            CameraType cameraType = camera.cameraType;
            if (cameraType == CameraType.Preview)
                return null;

            ScriptableCullingParameters* cameraCullingParameters = AllocatorManager.Allocate<ScriptableCullingParameters>(allocator);
            if (cameraCullingParameters != null && !camera.TryGetCullingParameters(out *cameraCullingParameters))
                return null;

            if (!cameraSettings)
            {
                if (cameraType != CameraType.SceneView)
                {
                    if (!camera.TryGetComponent(out cameraSettings))
                        cameraSettings = camera.gameObject.AddComponent<InstancingCameraSettings>();
                }
                else
                {
                    cameraSettings = ComponentSingleton<InstancingCameraSettings>.instance;
                }
            }

            if (cameraSettings.DisableInstanceRendering)
                return null;

            InstancedCameraFlags cameraFlags = InstancedCameraFlags.None;
            switch (cameraType)
            {
                case CameraType.Game:
                    cameraFlags |= InstancedCameraFlags.PersistentCamera;
                    break;
#if UNITY_EDITOR
                case CameraType.SceneView:
                    cameraFlags |= InstancedCameraFlags.SceneViewCamera;
                    cameraFlags |= InstancedCameraFlags.PersistentCamera;
                    break;
#endif
            }

            if (cameraFlags.IsPersistentCamera())
            {
                InstancingOcclusionMode occlusionMode = cameraFlags.IsSceneViewCamera()
                    ? InstancingSceneViewSettings.SceneViewOcclusionMode
                    : cameraSettings.OcclusionMode;

                switch (occlusionMode)
                {
                    case InstancingOcclusionMode.Umbra:
                        cameraFlags |= InstancedCameraFlags.CPUOcclusionCulling;
                        break;
                    case InstancingOcclusionMode.HierarchicalDepth:
                        cameraFlags |= InstancedCameraFlags.GPUOcclusionCulling;
                        break;
                }
            }

            InstancedCameraUpdatePacket* packet = AllocatorManager.Allocate<InstancedCameraUpdatePacket>(allocator);
            if (packet == null)
                return null;

            packet->CameraParameters = cameraCullingParameters;
            packet->Flags = cameraFlags;
            packet->FrameCount = context.FrameCount;
            packet->Time = context.Time;
            packet->LocalToWorldMatrix = camera.transform.localToWorldMatrix;
            packet->ProjectionMatrix = camera.projectionMatrix;
#if UNITY_EDITOR
            if (cameraType is CameraType.SceneView)
                packet->SceneCullingMask = RenderUtility.GetSceneCullingMaskFromCamera(camera);
#endif

            packet->FarClipPlane = camera.farClipPlane;
            packet->LODGlobalBias = QualitySettings.lodBias * cameraSettings.LODBiasScale;
            packet->MinimumScreenSize = cameraSettings.MinimumScreenSize;
            packet->CrossFadeDuration = cameraSettings.CrossFadeAnimatedDurationMode == CrossFadeAnimatedDurationMode.Global
                ? LODGroup.crossFadeAnimationDuration
                : cameraSettings.CrossFadeAnimatedDuration;

            bool shadowEnabled = context.MainLightInstanceID != 0;
            if (shadowEnabled)
            {
                packet->ShadowNear = context.MainLight.shadowNearPlane;
                packet->ShadowDistance = RenderUtility.GetMaximumShadowDistance(camera);
                packet->LightToWorldRotation = context.MainLightRotation;
            }

            return packet;
        }
    }

    [BurstCompile]
    sealed class InstancedCameraManager : IDisposable
    {
        public InstancedCameraArrays Data;
        public SlotMap<int> CameraIDHash;
        public Camera[] Cameras;
        public int[] InstanceIDs;
        public InstancingCameraSettings[] AdditionalCameraData;
        public BuiltinInstancingCameraRenderer[] BuiltinRenderer;
        public InstanceCuller[] CullingContexts;

        public UnsafeIndirectList<InstancedCameraID> ActiveCameraIDs;
        public UnsafeIndirectList<InstancedCameraID> PrevRenderedCameraIDs;
        public UnsafeParallelHashSet<InstancedCameraID> RenderedCameraIDs;

        InstancingContext m_Context;
        UnsafeIndirectList<InstancedCameraID> m_InvalidIDs;

        public InstancedCameraManager(InstancingContext context, int capacity)
        {
            Data = new InstancedCameraArrays(capacity);
            CameraIDHash = new SlotMap<int>(capacity, AllocatorManager.Persistent);
            Cameras = new Camera[capacity];
            InstanceIDs = new int[capacity];
            AdditionalCameraData = new InstancingCameraSettings[capacity];
            BuiltinRenderer = new BuiltinInstancingCameraRenderer[capacity];
            CullingContexts = new InstanceCuller[capacity];

            ActiveCameraIDs = new UnsafeIndirectList<InstancedCameraID>(capacity, AllocatorManager.Persistent);
            PrevRenderedCameraIDs = new UnsafeIndirectList<InstancedCameraID>(capacity, AllocatorManager.Persistent);
            RenderedCameraIDs = new UnsafeParallelHashSet<InstancedCameraID>(capacity, AllocatorManager.Persistent);

            m_Context = context;
            m_InvalidIDs = new UnsafeIndirectList<InstancedCameraID>(capacity, AllocatorManager.Persistent);
        }

        public void Dispose()
        {
            foreach (KeyValue<int, int> kvp in CameraIDHash)
                m_InvalidIDs.Add(new InstancedCameraID(kvp.Value));

            foreach (InstancedCameraID cameraID in m_InvalidIDs)
                UnregisterCamera(cameraID);

            Data.Dispose();
            CameraIDHash.Dispose();

            ActiveCameraIDs.Dispose();
            PrevRenderedCameraIDs.Dispose();
            RenderedCameraIDs.Dispose();

            m_InvalidIDs.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TryGetCameraID(int viewInstanceID, out InstancedCameraID id)
        {
            if (CameraIDHash.TryGetSlot(viewInstanceID, out int cameraIndex))
            {
                id = new InstancedCameraID(cameraIndex);
                return true;
            }

            id = InstancedCameraID.Null;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Exists(InstancedCameraID id) =>  id.IsCreated && CameraIDHash.Exists(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Camera GetCamera(InstancedCameraID id) =>  Exists(id) ? Cameras[id] : null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRenderingCameraID(Camera camera, out InstancedCameraID id)
        {
            return TryGetCameraID(camera.GetHashCode(), out id) && Data.Rendering[id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetLastRenderCameraID(Camera camera, out InstancedCameraID id)
        {
            return TryGetCameraID(camera.GetHashCode(), out id) && Data.RenderedLastFrame[id];
        }

        public InstancedCameraID RegisterCamera(Camera camera, int viewInstanceID)
        {
            if (CameraIDHash.TryGetSlot(viewInstanceID, out int cameraIndex))
                return new InstancedCameraID(cameraIndex);

            InstancedCameraID id = new InstancedCameraID(CameraIDHash.Allocate(viewInstanceID));
            if (id >= Data.Capacity)
            {
                int newCapacity = id + 8;
                Array.Resize(ref Cameras, newCapacity);
                Array.Resize(ref InstanceIDs, newCapacity);
                Array.Resize(ref AdditionalCameraData, newCapacity);
                Array.Resize(ref BuiltinRenderer, newCapacity);
                Array.Resize(ref CullingContexts, newCapacity);
                Data.EnsureCapacity(newCapacity);
            }

            Data.LastFrameUpdated[id] = -1;
            Cameras[id] = camera;
            InstanceIDs[id] = viewInstanceID;
            AdditionalCameraData[id] = null;
            BuiltinRenderer[id] = new BuiltinInstancingCameraRenderer(m_Context, camera, id);
            CullingContexts[id] = new InstanceCuller(m_Context, camera, id);
            ActiveCameraIDs.Add(id);

            return id;
        }

        public void UnregisterCamera(InstancedCameraID id)
        {
            if (!CameraIDHash.Exists(id))
                return;

            CameraIDHash.Free(id);
            ActiveCameraIDs.Remove(id);

            BuiltinRenderer[id]?.Dispose();
            BuiltinRenderer[id] = null;
            CullingContexts[id]?.Dispose();
            CullingContexts[id] = null;

            if (m_Context.HasStaticOcclusionManager())
                m_Context.GetStaticOcclusionManager().UnregisterCamera(id);

            Data.LastFrameUpdated[id] = -1;
            Cameras[id] = null;
            InstanceIDs[id] = 0;
            AdditionalCameraData[id] = null;
            CullingContexts[id] = null;
        }

        public void NextFrame()
        {
            PrevRenderedCameraIDs.Clear();

            foreach (InstancedCameraID id in ActiveCameraIDs)
            {
                Data.Rendering[id] = false;
                Data.RenderedLastFrame[id] = false;
            }

            foreach (InstancedCameraID id in RenderedCameraIDs)
            {
                PrevRenderedCameraIDs.Add(id);
                Data.RenderedLastFrame[id] = true;
            }

            RenderedCameraIDs.Clear();
        }

        public void NextRender()
        {
            foreach (KeyValue<int, int> kvp in CameraIDHash)
            {
                InstancedCameraID id = new InstancedCameraID(kvp.Value);
                Camera camera = Cameras[id];

                if (camera != null && camera.cameraType == CameraType.SceneView)
                    continue;

                bool isPersistentDisabled = Data.Culling[id].Flags.IsPersistentAndDisabled();
                bool renderedLastFrame = Data.RenderedLastFrame[id];

                if (camera == null || (!camera.isActiveAndEnabled && camera.cameraType != CameraType.Preview) || (!renderedLastFrame && !isPersistentDisabled))
                    m_InvalidIDs.Add(id);
            }

            for (int i = 0; i < m_InvalidIDs.Length; i++)
                UnregisterCamera(m_InvalidIDs[i]);

            m_InvalidIDs.Clear();
        }

        public void EndCameraRender()
        {
            foreach (InstancedCameraID id in RenderedCameraIDs)
            {
                Data.Rendering[id] = false;
            }
        }

        public void EndCameraRender(InstancedCameraID id)
        {
            Data.Rendering[id] = false;
            RenderedCameraIDs.Add(id);
        }

        public bool TryGetInstancedCamera(Camera view, out InstancedCameraID id)
        {
            id = InstancedCameraID.Null;
            if (view == null)
                return false;

            int viewInstanceID = view.GetHashCode();
            if (!id.IsCreated)
            {
                if (CameraIDHash.TryGetSlot(viewInstanceID, out int slot))
                {
                    id = new InstancedCameraID(slot);
                }
                else
                {
                    id = RegisterCamera(view, viewInstanceID);
                }
            }

            return TryUpdateInstancedCamera(id);
        }

        public bool TryUpdateInstancedCamera(InstancedCameraID id)
        {
            if (!id.IsCreated || !CameraIDHash.Exists(id))
                return false;

            if (!Data.Rendering[id])
                return TryUpdateID(id);
            else
                return true;
        }

        unsafe bool TryUpdateID(InstancedCameraID id)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DebugDisplayData.IsActive() && DebugDisplayData.Instance.FreezeCulling)
            {
                Data.LastFrameUpdated[id] = m_Context.FrameCount;
                return true;
            }
#endif

            AllocatorManager.AllocatorHandle frameAllocator = m_Context.FrameAllocator.GeneralAllocator->Handle;
            Camera camera = Cameras[id];
            InstancedCameraUpdatePacket* updatePacket = InstancedCameraUpdatePacket.Create(m_Context, camera, ref AdditionalCameraData[id], frameAllocator);
            if (updatePacket == null)
                return false;

            Data.LastFrameUpdated[id] = m_Context.FrameCount;
            Data.Rendering[id] = true;
            RenderedCameraIDs.Add(id);

            InstancedCameraFlags oldFlags = Data.Culling[id].Flags;
            InstancedCameraFlags newFlags = updatePacket->Flags;

            bool wasCPUOcclusionEnabled = oldFlags.HasCPUOcclusionCulling();
            bool newCPUOcclusionEnabled = newFlags.HasCPUOcclusionCulling();
            if (newCPUOcclusionEnabled != wasCPUOcclusionEnabled)
            {
                if (wasCPUOcclusionEnabled && m_Context.HasStaticOcclusionManager())
                    m_Context.GetStaticOcclusionManager().UnregisterCamera(id);

                if (newCPUOcclusionEnabled)
                    m_Context.EnsureStaticOcclusionManager().RegisterCamera(id, camera);
            }

            UpdateCameraArraysJob updateCameraArraysJob = new UpdateCameraArraysJob
            {
                UpdatePacket = updatePacket,
                Arrays = Data,
                CameraID = id,
                Allocator = frameAllocator,
            };
            updateCameraArraysJob.RunByRef();

            if (newFlags.HasCPUOcclusionCulling())
            {
                OcclusionManager occlusionManager = m_Context.EnsureOcclusionManager();
                occlusionManager.UpdateSilhouettePlanes(CameraIDHash.GetKey(id), Data.Shadow[id].SilhouettePlanes.Reinterpret<UnityEngine.Plane>());
            }

            return true;
        }

        [BurstCompile]
        unsafe struct UpdateCameraArraysJob : IJob
        {
            [NativeDisableUnsafePtrRestriction, NoAlias] public InstancedCameraUpdatePacket* UpdatePacket;

            public InstancedCameraArrays Arrays;
            public InstancedCameraID CameraID;
            public AllocatorManager.AllocatorHandle Allocator;

            public void Execute()
            {
                int planeCount = UpdatePacket->CameraParameters->cullingPlaneCount;
                UnsafeArray<Plane> cullingPlanes = new UnsafeArray<Plane>(planeCount, Allocator);
                for (int i = 0; i < planeCount; i++)
                    cullingPlanes[i] = UpdatePacket->CameraParameters->cameraProperties.GetCameraCullingPlane(i);

                int packetCount = FrustumUtility.ComputeSIMDPacketCount(planeCount);
                UnsafeArray<FrustumSIMDPacket> cullingPlanePackets = new UnsafeArray<FrustumSIMDPacket>(packetCount, Allocator);
                FrustumUtility.InitializeSIMDPackets(cullingPlanes, cullingPlanePackets);

                Arrays.Culling[CameraID] = new InstancedCameraCullingData
                {
                    Flags = UpdatePacket->Flags,
                    CullingLayerMask = UpdatePacket->CameraParameters->cullingMask,
                    SceneCullingMask = UpdatePacket->SceneCullingMask,
                    FarClipPlane = UpdatePacket->FarClipPlane,
                    CullingPlanes = cullingPlanes,
                    CullingPlanePackets = cullingPlanePackets,
                };

                LODParameters lodParameters = UpdatePacket->CameraParameters->lodParameters;
                float3 prevOrigin = UpdatePacket->FrameCount > 1 ? Arrays.Position[CameraID].Origin : lodParameters.cameraPosition;
                float3 currOrigin = lodParameters.cameraPosition;
                float distanceMoved = math.length(currOrigin - prevOrigin);

                Arrays.Position[CameraID] = new InstancedCameraPositionData
                {
                    PrevOrigin = prevOrigin,
                    Origin = currOrigin,
                    DistanceMoved = distanceMoved,
                    FixedDistanceMoved = FixedMathUtility.FixedFromFloatCeilU16(distanceMoved),
                };

                float globalLODBias = UpdatePacket->LODGlobalBias;
                float fovHalfAngle = LODGroupUtility.CalculateFOVHalfAngle(lodParameters.fieldOfView);
                float screenRelativeMetric = LODGroupUtility.CalculateScreenRelativeMetric(lodParameters, fovHalfAngle, globalLODBias);

                Arrays.LOD[CameraID] = new InstancedCameraLODData
                {
                    Origin = lodParameters.cameraPosition,
                    IsOrthographic = lodParameters.isOrthographic,
                    MinimumScreenSize = UpdatePacket->MinimumScreenSize,
                    LODGlobalBias = globalLODBias,
                    ScreenRelativeMetric = screenRelativeMetric,
                };

                InstancedCameraAnimatedCrossFadeData animatedCrossFade = Arrays.AnimatedCrossFade[CameraID];
                animatedCrossFade.Update(UpdatePacket->Time, UpdatePacket->CrossFadeDuration, currOrigin);
                Arrays.AnimatedCrossFade[CameraID] = animatedCrossFade;

                BuildShadowData(currOrigin, cullingPlanes, out Arrays.Shadow[CameraID]);
            }

            void BuildShadowData(float3 viewOrigin, UnsafeArray<Plane> cameraPlanes, out InstancedCameraShadowData outShadowData)
            {
                if (UpdatePacket->ShadowDistance <= 0 || cameraPlanes.Length < 6)
                {
                    outShadowData = default;
                    return;
                }

                float4x4 localToWorldMatrix = UpdatePacket->LocalToWorldMatrix;
                float4x4 projectionMatrix = UpdatePacket->ProjectionMatrix;
                bool isOrthographic = Arrays.LOD[CameraID].IsOrthographic;

                float3 viewDir = localToWorldMatrix.Forward();
                float3 lightDir = -UpdatePacket->LightToWorldRotation.c2;
                float n = UpdatePacket->ShadowNear;
                float f = UpdatePacket->ShadowDistance;

                // Calculate the clipped view frustum for the shadow receiver
                Span<Plane> receiverPlanes = stackalloc Plane[6];
                for (int i = 0; i < 6; i++)
                    receiverPlanes[i] = cameraPlanes[i];

                Plane clippedNearPlane = new Plane(viewDir, viewOrigin + viewDir * n);
                receiverPlanes[4] = clippedNearPlane;

                Plane clippedFarPlane = new Plane(-viewDir, viewOrigin + viewDir * f);
                receiverPlanes[5] = clippedFarPlane;

                Span<float3> frustumCornersWS = stackalloc float3[8];
                FrustumUtility.ComputeCorners(receiverPlanes, frustumCornersWS);

                // Reset the far plane to the shadow distance
                float aspect = projectionMatrix[1][1] / projectionMatrix[0][0];
                float halfHFov = !isOrthographic ? math.atan(1.0f / projectionMatrix[0][0]) : math.PI / 4.0f;
                float halfVFov = !isOrthographic ? math.atan(1.0f / projectionMatrix[1][1]) : math.atan((math.tan(math.PI / 4.0f) / aspect));

                // Fit a bounding sphere around the world space camera cascade frustum
                float tanHalfFoVx = math.tan(halfHFov);
                float tanHalfFoVy = math.tan(halfVFov);
                float frustumLength = f - n;

                float farX = tanHalfFoVx * f;
                float farY = tanHalfFoVy * f;
                float diagonalASq = farX * farX + farY * farY;

                float nearX = tanHalfFoVx * n;
                float nearY = tanHalfFoVy * n;
                float diagonalBSq = nearX * nearX + nearY * nearY;

                float offset = (diagonalBSq - diagonalASq) / (2.0f * frustumLength) + frustumLength * 0.5f;
                float centreZ = f - offset;
                centreZ = math.clamp(centreZ, n, f);

                // Calculate the receiver sphere
                Sphere receiverSphere = new Sphere(viewOrigin + viewDir * centreZ, 0);
                for (int index = 0; index < frustumCornersWS.Length; index++)
                    receiverSphere.Radius = math.max(receiverSphere.Radius, math.distancesq(frustumCornersWS[index], receiverSphere.Center));
                receiverSphere.Radius = math.max(math.sqrt(receiverSphere.Radius), 1.0f);

                // Light space receiver sphere
                float3x3 worldToLightSpaceRotation = math.transpose(UpdatePacket->LightToWorldRotation);
                float4 receiverSphereInLightSpace = new float4(math.mul(worldToLightSpaceRotation, receiverSphere.Center), receiverSphere.Radius);

                // Calculate the shadow planes
                UnsafeList<Plane> shadowPlaneList = new UnsafeList<Plane>(12, Allocator);

                // Receiver planes facing the light
                int lightFacingPlanesCount = 0;
                int planeSignBits = 0;
                for (int i = 0; i < receiverPlanes.Length; ++i)
                {
                    Plane plane = receiverPlanes[i];
                    float facingTerm = math.dot(plane.Normal, lightDir);
                    if (IsSignBitSet(facingTerm))
                    {
                        planeSignBits |= (1 << i);
                    }
                    else
                    {
                        shadowPlaneList.Add(plane);
                        lightFacingPlanesCount++;
                    }
                }

                // Silhouette edges, assume ordering +/-x, +/-y, +/-z for frustum planes, test pairs for silhouette edges
                int silhouetteEdgeCount = 0;
                if (receiverPlanes.Length == 6)
                {
                    for (int i = 0; i < 6; ++i)
                    {
                        for (int j = i + 1; j < 6; ++j)
                        {
                            // Skip pairs that are from the same frustum axis (i.e. both xs, both ys or both zs)
                            if ((i / 2) == (j / 2))
                                continue;

                            // Silhouette edges occur when the planes have opposing signs
                            int signCheck = ((planeSignBits >> i) ^ (planeSignBits >> j)) & 1;
                            if (signCheck == 0)
                                continue;

                            // Process in consistent order for consistent plane normal in the result
                            (int indexA, int indexB) = (((planeSignBits >> i) & 1) == 0) ? (i, j) : (j, i);
                            Plane planeA = receiverPlanes[indexA];
                            Plane planeB = receiverPlanes[indexB];

                            // Construct a plane that contains the light origin and this silhouette edge
                            Line silhouetteEdge = Line.LineOfPlaneIntersectingPlane(planeA, planeB);
                            float4 silhouettePlaneEq = Line.PlaneContainingLineWithNormalPerpendicularToVector(silhouetteEdge, lightDir);

                            // Try to normalize
                            silhouettePlaneEq /= math.length(silhouettePlaneEq.xyz);
                            if (!math.any(math.isnan(silhouettePlaneEq)))
                            {
                                shadowPlaneList.Add(new Plane(silhouettePlaneEq.xyz, silhouettePlaneEq.w));
                                silhouetteEdgeCount++;
                            }
                        }
                    }
                }

                UnsafeArray<Plane> lightFacingPlanes = shadowPlaneList.AsUnsafeArray(0, lightFacingPlanesCount);
                UnsafeArray<Plane> silhouettePlanes = shadowPlaneList.AsUnsafeArray(lightFacingPlanesCount, silhouetteEdgeCount);
                UnsafeArray<Plane> shadowPlanes = shadowPlaneList.AsUnsafeArray();

                int packetCount = FrustumUtility.ComputeSIMDPacketCount(shadowPlanes.Length);
                UnsafeArray<FrustumSIMDPacket> shadowPlanePackets = new UnsafeArray<FrustumSIMDPacket>(packetCount, Allocator);
                FrustumUtility.InitializeSIMDPackets(shadowPlanes, shadowPlanePackets);

                outShadowData = new InstancedCameraShadowData
                {
                    ShadowEnabled = true,
                    CullingPlanes = shadowPlanes,
                    CullingPlanePackets = shadowPlanePackets,
                    LightFacingPlanes = lightFacingPlanes,
                    SilhouettePlanes = silhouettePlanes,
                    WorldToLightSpaceRotation = worldToLightSpaceRotation,
                    ReceiverSphereInLightSpace = receiverSphereInLightSpace,
                };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool IsSignBitSet(float x)
            {
                uint i = math.asuint(x);
                return (i >> 31) != 0;
            }

            // 6-component representation of a (infinite length) line in 3D space
            struct Line
            {
                // for the line to be valid, dot(m, t) == 0
                public float3 M;
                public float3 T;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static Line LineOfPlaneIntersectingPlane(float4 a, float4 b)
                {
                    // planes do not need to have a unit length normal
                    return new Line
                    {
                        M = a.w * b.xyz - b.w * a.xyz,
                        T = math.cross(a.xyz, b.xyz),
                    };
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static float4 PlaneContainingLineAndPoint(Line a, float3 b)
                {
                    // the resulting plane will not have a unit length normal (and the normal will be approximately zero when no plane exists)
                    return new float4(a.M + math.cross(a.T, b), -math.dot(a.M, b));
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static float4 PlaneContainingLineWithNormalPerpendicularToVector(Line a, float3 b)
                {
                    // the resulting plane will not have a unit length normal (and the normal will be approximately zero when no plane exists)
                    return new float4(math.cross(a.T, b), -math.dot(a.M, b));
                }
            }
        }
    }
}
