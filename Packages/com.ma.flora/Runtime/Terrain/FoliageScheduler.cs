// Copyright © Magnetic Arcade. All Rights Reserved.

using System;
using System.Runtime.CompilerServices;
using MA.Collections;
using MA.Collections.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace MA.Flora
{
    enum FoliageJobType
    {
        None             = 0,
        GatherDirtyCells = 1,
        BuildInstances   = 2,
        Count            = 3
    }

    struct FoliageJobKey : IEquatable<FoliageJobKey>
    {
        public static readonly FoliageJobKey Null = new FoliageJobKey(0);

        int m_Key;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FoliageJobKey(int slot) => m_Key = slot;

        public bool IsCreated { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_Key != 0; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FoliageJobKey other) => m_Key == other.m_Key;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is FoliageJobKey other && Equals(other);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => (int)m_Key;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FoliageJobKey a, FoliageJobKey b) => a.Equals(b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FoliageJobKey a, FoliageJobKey b) => !a.Equals(b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(FoliageJobKey key) => key.m_Key;
    }

    struct FoliageJob : IDisposable
    {
        public JobHandle Handle;
        public int Frames;
        public bool IsComplete;

        public void Dispose()
        {
            Handle.Complete();
        }
    }

    unsafe struct FoliageScheduler : IDisposable, IEquatable<FoliageScheduler>
    {
        struct UnsafeScheduler : IDisposable
        {
            public SlotAllocator SlotAllocator;
            public UnsafeArray<FoliageJob> Jobs;

            public UnsafeScheduler(AllocatorManager.AllocatorHandle allocator)
            {
                SlotAllocator = new SlotAllocator(16, allocator);
                Jobs = new UnsafeArray<FoliageJob>(16, allocator);
            }

            public void Dispose()
            {
                SlotAllocator.Dispose();
                Jobs.Dispose();
            }

            public void CancelAll()
            {
                foreach (var slot in SlotAllocator)
                {
                    ref FoliageJob job = ref Jobs[slot];
                    job.Dispose();
                    Jobs[slot] = default;
                }

                SlotAllocator.Clear();
            }

            public FoliageJobKey Schedule(FoliageJob job)
            {
                FoliageJobKey key = new FoliageJobKey(SlotAllocator.Allocate());
                if (SlotAllocator.MaxAllocatedSlot >= Jobs.Length)
                    Jobs.Resize(math.ceilpow2(SlotAllocator.MaxAllocatedSlot + 1), Allocator.Persistent);

                Jobs[key] = job;
                return key;
            }

            public void Cancel(FoliageJobKey key)
            {
                if (!key.IsCreated || !SlotAllocator.Exists(key))
                    return;

                Jobs[key].Dispose();
                Remove(key);
            }

            public bool IsComplete(FoliageJobKey key)
            {
                if (!key.IsCreated || !SlotAllocator.Exists(key))
                    return true;

                return Jobs[key].IsComplete;
            }

            public void ForceComplete(FoliageJobKey key)
            {
                if (!key.IsCreated || !SlotAllocator.Exists(key))
                    return;

                Jobs[key].Handle.Complete();
            }

            public void AddDependency(FoliageJobKey key, JobHandle dependency)
            {
                if (!key.IsCreated || !SlotAllocator.Exists(key))
                    return;

                var job = Jobs[key];
                job.Handle = JobHandle.CombineDependencies(job.Handle, dependency);
                Jobs[key] = job;
            }

            public void Remove(FoliageJobKey key)
            {
                if (!key.IsCreated || !SlotAllocator.Exists(key))
                    return;

                ref var job = ref Jobs[key];
                job.Handle.Complete();
                job.Dispose();
                Jobs[key] = default;
                SlotAllocator.Free(key);
            }

            public void Update()
            {
                foreach (var slot in SlotAllocator)
                {
                    ref FoliageJob job = ref Jobs[slot];
                    job.Frames--;

                    if (job.Frames <= 0 || job.Handle.IsCompleted)
                    {
                        job.IsComplete = true;
                        job.Handle.Complete();
                    }
                }
            }
        }

        AllocatorManager.AllocatorHandle m_Allocator;
        UnsafeScheduler* m_Data;

        public FoliageScheduler(AllocatorManager.AllocatorHandle allocator)
        {
            m_Allocator = allocator;
            m_Data = AllocatorManager.Allocate<UnsafeScheduler>(allocator);
            *m_Data = new UnsafeScheduler(allocator);
        }

        public void Dispose()
        {
            if (m_Data != null)
            {
                m_Data->Dispose();
                AllocatorManager.Free(m_Allocator, m_Data);
            }

            m_Data = null;
            m_Allocator = Allocator.Invalid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CancelAll() => m_Data->CancelAll();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FoliageJobKey Schedule(FoliageJob job) => m_Data->Schedule(job);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Cancel(FoliageJobKey key) => m_Data->Cancel(key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsComplete(FoliageJobKey key) => m_Data->IsComplete(key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForceComplete(FoliageJobKey key) => m_Data->ForceComplete(key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddDependency(FoliageJobKey key, JobHandle dependency) => m_Data->AddDependency(key, dependency);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(FoliageJobKey key) => m_Data->Remove(key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update() => m_Data->Update();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FoliageScheduler other) => m_Data == other.m_Data;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is FoliageScheduler other && Equals(other);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => unchecked((int)(long)m_Data);
    }

    [BurstCompile]
    static class FoliageJobManager
    {
        struct State
        {
            public byte Initialized;
            public UnsafeList<FoliageScheduler> Schedulers;
        }

        struct StateKey { }

        static readonly SharedStatic<State> s_State = SharedStatic<State>.GetOrCreate<StateKey>();

        public static bool IsInitialized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_State.Data.Initialized != 0;
        }

        public static bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_State.Data.Schedulers.Length == 0;
        }

        internal static void Initialize()
        {
            if (IsInitialized)
                return;

            s_State.Data.Initialized = 1;
            s_State.Data.Schedulers = new UnsafeList<FoliageScheduler>(16, Allocator.Persistent);
        }

        internal static void Shutdown()
        {
            if (!IsInitialized)
                return;

            s_State.Data.Schedulers.Dispose();
            s_State.Data.Initialized = 0;
        }

        public static void Register(FoliageScheduler scheduler)
        {
            if (!IsInitialized)
                return;

            int index = s_State.Data.Schedulers.IndexOf(scheduler);
            if (index < 0)
            {
                s_State.Data.Schedulers.Add(scheduler);
            }
        }

        public static void Unregister(FoliageScheduler scheduler)
        {
            if (!IsInitialized)
                return;

            int index = s_State.Data.Schedulers.IndexOf(scheduler);
            if (index >= 0)
            {
                s_State.Data.Schedulers[index].CancelAll();
                s_State.Data.Schedulers.RemoveAt(index);
            }
        }

        static readonly ProfilerMarker s_UpdateMarker = new ProfilerMarker("FoliageJobManager.Update");

        [BurstCompile]
        public static void Update()
        {
            if (!IsInitialized)
                return;

            using (s_UpdateMarker.Auto())
            {
                for (int i = 0; i < s_State.Data.Schedulers.Length; i++)
                    s_State.Data.Schedulers[i].Update();
            }
        }

        [BurstCompile]
        public static void CancelAll()
        {
            if (!IsInitialized)
                return;

            for (int i = 0; i < s_State.Data.Schedulers.Length; i++)
                s_State.Data.Schedulers[i].CancelAll();
        }
    }
}
