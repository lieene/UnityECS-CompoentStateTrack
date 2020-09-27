/************************************************************************************
| File: ComponentExist.cs                                                           |
| Project: lieene.ComponentTrack                                                    |
| Created Date: Sat Sep 26 2020                                                     |
| Author: Lieene Guo                                                                |
| -----                                                                             |
| Last Modified: Sun Sep 27 2020                                                    |
| Modified By: Lieene Guo                                                           |
| -----                                                                             |
| MIT License                                                                       |
|                                                                                   |
| Copyright (c) 2020 Lieene@ShadeRealm                                              |
|                                                                                   |
| Permission is hereby granted, free of charge, to any person obtaining a copy of   |
| this software and associated documentation files (the "Software"), to deal in     |
| the Software without restriction, including without limitation the rights to      |
| use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies     |
| of the Software, and to permit persons to whom the Software is furnished to do    |
| so, subject to the following conditions:                                          |
|                                                                                   |
| The above copyright notice and this permission notice shall be included in all    |
| copies or substantial portions of the Software.                                   |
|                                                                                   |
| THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR        |
| IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,          |
| FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE       |
| AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER            |
| LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,     |
| OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE     |
| SOFTWARE.                                                                         |
|                                                                                   |
| -----                                                                             |
| HISTORY:                                                                          |
| Date      	By	Comments                                                        |
| ----------	---	----------------------------------------------------------      |
************************************************************************************/

using System;
using Unity.Entities;
using Unity.Assertions;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using System.Text;
using System.Diagnostics;

namespace SRTK
{
    using Debug = UnityEngine.Debug;
    using static ComponentExist;
    using static ComponentExistInfoSystem;
    public enum ExistState : byte
    {
        None                /**/= 0b00,
        AddedLastSync       /**/= 0b11,
        ExistedLastSync     /**/= 0b10,
        RemovedLastSync     /**/= 0b01,
    }

    public struct ComponentExistHandle { internal int trackID; }

    [GenerateAuthoringComponent]
    public struct ComponentExist : IComponentData
    {
        /// <summary>
        /// Maximum Number of Tracked component count
        /// Change this value to use less Chunk memory or track more components
        /// There are already 698 Component types in ECS when this script is made, it may grow to several thouands
        /// Call TypeManagerExt.TypeCheck() to see how many are there now
        /// Must be dividable by 4
        /// </summary>
        public const int K_MaxTrackedComponentCount = 128;
        unsafe internal fixed byte QuadTracks[K_DualTrackStateCount];//DualTrackState

        unsafe public ExistState GetTrackState(ComponentExistHandle handle)
        {
            var quadState = GetQuadState(handle, out var bitShift);
            return (ExistState)(K_KeepLowestMask & (((byte)quadState) >> bitShift));
        }

        public bool WasAdded(ComponentExistHandle handle) => GetTrackState(handle) == ExistState.AddedLastSync;
        public bool WasRemoved(ComponentExistHandle handle) => GetTrackState(handle) == ExistState.RemovedLastSync;
        public bool WasExist(ComponentExistHandle handle) => GetTrackState(handle) == ExistState.ExistedLastSync;


        const int K_DualTrackStateCount = K_MaxTrackedComponentCount >> K_TrackID2QuadIDShift;
        internal const int K_QuadOffsetMask = 0x0004;
        internal const int K_KeepLowestMask = 0b0011;
        internal const int K_TrackID2QuadIDShift = 2;

        unsafe internal ref QuadState GetQuadState(int trackID, out int bitShift)
        {
            Assert.IsTrue(trackID >= 0 && trackID < K_MaxTrackedComponentCount, "Invalid Track ID");
            bitShift = (trackID & K_QuadOffsetMask);
            var quadID = trackID >> K_TrackID2QuadIDShift;
            fixed (byte* pQuads = QuadTracks) return ref *(QuadState*)(pQuads + quadID);
        }

        internal QuadState GetQuadState(ComponentExistHandle handle, out int bitShift) => GetQuadState(handle.trackID, out bitShift);

        internal enum QuadState : byte
        {
            None = 0b0000_0000,//No component No action in last sync
            LlAd = 0b0000_0011,//low Low:[0,1]bit Added component in last sync
            LlEx = 0b0000_0010,//Low Low:[0,1]bit Exist in last sync
            LlRm = 0b0000_0001,//Low Low:[0,1]bit Removed in last sync
            LhAd = 0b0000_1100,//low Heigh:[2,3]bit Added component in last sync
            LhEx = 0b0000_1000,//Low Heigh:[2,3]bit Exist in last sync
            LhRm = 0b0000_0100,//Low Heigh:[2,3]bit Removed in last sync
            HlAd = 0b0011_0000,//Hight Low:[4,5]bit Added component in last sync
            HlEx = 0b0010_0000,//Hight Low:[4,5]bit Exist in last sync
            HlRm = 0b0001_0000,//Hight Low:[4,5]bit Removed in last sync
            HhAd = 0b1100_0000,//Hight Heigh:[6,7]bit Added component in last sync
            HhEx = 0b1000_0000,//Hight Heigh:[6,7]bit Exist in last sync
            HhRm = 0b0100_0000,//Hight Heigh:[6,7]bit Removed in last sync
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            unsafe
            {
                for (int i = 0; i < K_DualTrackStateCount; i++)
                {
                    sb.Append($"[{i}]=");
                    sb.Append(((QuadState)QuadTracks[i]).ToString());
                    sb.Append('.');
                }
            }
            return sb.ToString();
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true), UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    public class ComponentExistTrackingSystem : SystemBase
    {
        ComponentExistInfoSystem init;
        EntityQuery HasTrackQuery;
        protected override void OnCreate()
        {
            init = World.GetOrCreateSystem<ComponentExistInfoSystem>();
            HasTrackQuery = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[] { ComponentType.ReadWrite<ComponentExist>() },
                Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
            });
        }

        protected override void OnUpdate()
        {
            if (init.HasTrackInfo)
            {
                Dependency = new TickComponentsJob()
                {
                    StateType = GetComponentTypeHandle<ComponentExist>(false),
                    Info = init.TrackInfo,
                }.ScheduleParallel(HasTrackQuery, Dependency);
            }
        }

        protected override void OnDestroy() { }

        [BurstCompile]
        unsafe struct TickComponentsJob : IJobChunk
        {
            public ComponentTypeHandle<ComponentExist> StateType;
            [ReadOnly] public TrackedTypeInfo Info;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var chunkStates = chunk.GetNativeArray(StateType);
                var chunkCount = chunk.Count;
                var chunkTypeIds = (int*)chunk.Archetype.Archetype->Types;
                var chunkTypeCount = chunk.Archetype.Archetype->TypesCount;
                int* chunkTrackIds = stackalloc int[chunkTypeCount + 4];//+3 give it enough space for align fill,even if all component type of chunk is tracked
                int chunkTrackedTypeCount = 0;
                var pChunkTracks = (ComponentExist*)NativeArrayUnsafeUtility.GetUnsafePtr(chunkStates);
                var map = Info.TypeOffset2TrackIndex;

                //find all tracked types
                for (int i = 0; i < chunkTypeCount; i++)
                {
                    var trackId = map[chunkTypeIds[i] & TypeManager.ClearFlagsMask];
                    chunkTrackIds[chunkTrackedTypeCount] = trackId;
                    chunkTrackedTypeCount += trackId <= UnTracked ? 0 : 1;
                }

                //align fill so int4 from any int* in chunkTrackedTypeCount range will have meaningful value
                for (int i = chunkTrackedTypeCount & TypeManager.ClearFlagsMask, j = chunkTrackedTypeCount; i < 4; i++, j++) { chunkTrackIds[j] = UnTracked; }

                //TODO: May not need to sort
                //TypeOffset2TrackIndex is Order
                //If Types in ArchetypeChunk is Order, then we don't need to sort this
                NativeSortExtension.Sort(chunkTrackIds, chunkTrackedTypeCount);
                var registeredCount = Info.RegisteredCount;
                if (chunkTrackIds[0] == ComponentExistInfoSystem.UnTracked)
                {
                    //No tracked component on this chunk, update as none-exist
                    for (int i = 0; i < chunkCount; i++)
                    {
                        var track = pChunkTracks + i;
                        for (int trackID = 0; trackID < registeredCount; trackID += 4)
                        {
                            var quadID = trackID >> K_TrackID2QuadIDShift;
                            ref var quadState = ref *(track->QuadTracks + quadID);
                            var intTrack = (int)quadState;
                            //var quad = intTrack >> math.int4(0, 2, 4, 6); //not implemented
                            var int4State = math.int4(intTrack, intTrack >> 2, intTrack >> 4, intTrack >> 6);
                            int4State = int4State & K_KeepLowestMask;
                            var wasExist4 = int4State >= (int)ExistState.ExistedLastSync;
                            int4State = math.select(math.int4((int)ExistState.None), math.int4((int)ExistState.RemovedLastSync), wasExist4);
                            quadState = (byte)(int4State.x | int4State.y << 2 | int4State.z << 4 | int4State.w << 6);
                        }
                    }
                    return;
                }

                //Update existence 
                for (int i = 0; i < chunkCount; i++)
                {
                    var track = pChunkTracks + i;
                    var chunkTrackIDOffset = 0;
                    for (int trackID = 0; trackID < registeredCount; trackID += 4)
                    {
                        var quadID = trackID >> K_TrackID2QuadIDShift;
                        ref var quadState = ref *(track->QuadTracks + quadID);
                        var intTrack = (int)quadState;
                        //var quad = intTrack >> math.int4(0, 2, 4, 6); //not implemented
                        var int4State = math.int4(intTrack, intTrack >> 2, intTrack >> 4, intTrack >> 6);
                        int4State = int4State & K_KeepLowestMask;

                        var chunkTrackId4 = *(int4*)(chunkTrackIds + chunkTrackIDOffset);
                        var exist4 = (chunkTrackId4 >> K_TrackID2QuadIDShift) == quadID;
                        var wasExist4 = int4State >= (int)ExistState.ExistedLastSync;

                        //update state
                        int4State = math.select(
                            math.select(math.int4((int)ExistState.None), math.int4((int)ExistState.RemovedLastSync), wasExist4),
                            math.select(math.int4((int)ExistState.AddedLastSync), math.int4((int)ExistState.ExistedLastSync), wasExist4), exist4);

                        //step chunk TrackID Offset
                        var offset4 = math.select(math.int4(0), math.int4(1), exist4);
                        chunkTrackIDOffset += math.csum(offset4);

                        quadState = (byte)(int4State.x | int4State.y << 2 | int4State.z << 4 | int4State.w << 6);
                    }
                }
            }
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public class ComponentExistInfoSystem : SystemBase
    {
        internal const int ShouldTrack = 1;
        internal const int UnTracked = -1;
        internal NativeArray<int> TypeOffset2TrackIndex;
        private bool mInitialized = false;
        private bool mSorted = false;
        private int TrackedTypeCount = 0;

        public bool IsReady => mInitialized & mSorted & TypeOffset2TrackIndex.IsCreated;
        public bool HasTrackInfo => IsReady & TrackedTypeCount > 0;

        unsafe internal void Initialize()
        {
            if (mInitialized) return;
            mInitialized = true;
            TypeManager.Initialize();
            TypeOffset2TrackIndex = new NativeArray<int>(TypeManager.GetTypeCount(), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            //Set all value to -1, which means we are tracking no components yet
            UnsafeUtility.MemSet(TypeOffset2TrackIndex.GetUnsafePtr(), 0xff, sizeof(int) * TypeOffset2TrackIndex.Length);
        }

        unsafe internal void Sort()
        {
            Assert.IsTrue(mInitialized, "Sort should happen after Initialize");
            if (mSorted) return;
            mSorted = true;
            TrackedTypeCount = 0;
            for (int i = 0, end = TypeOffset2TrackIndex.Length; i < end; i++)
            {
                if (TrackedTypeCount > ComponentExist.K_MaxTrackedComponentCount)
                {
                    Debug.LogError($"Can not Track more than {ComponentExist.K_MaxTrackedComponentCount} Tracked types, consider increase ComponentExistTrack.K_MaxTrackedComponentCount");
                    break;
                }
                bool tracked = TypeOffset2TrackIndex[i] == ShouldTrack;
                TypeOffset2TrackIndex[i] = tracked ? TrackedTypeCount : UnTracked;
                TrackedTypeCount += tracked ? 1 : 0;
            }
        }
        public struct TrackedTypeInfo
        {
            public NativeArray<int>.ReadOnly TypeOffset2TrackIndex;
            public int RegisteredCount;
        }

        public TrackedTypeInfo TrackInfo
        {
            get
            {
                Assert.IsTrue(IsReady, "TrackState not Initialize");
                return new TrackedTypeInfo()
                {
                    TypeOffset2TrackIndex = TypeOffset2TrackIndex.AsReadOnly(),
                    RegisteredCount = TrackedTypeCount
                };
            }
        }

        /// <summary>
        /// Register a new type that can be tracked, must be called in main thread within SystemBase.OnCreate()
        /// </summary>
        public void RegisterTypeForTracking<T>()
        {
            Assert.IsTrue(!mSorted, "RegisterTypeForTracking must be call in OnCreate and before ComponentExistInfoSystem's first update!");
            Initialize();
            var typeOffset = TypeManagerExt.GetTypeOffset<T>();
            TypeOffset2TrackIndex[typeOffset] = ShouldTrack;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void TrackID_Check(int trackID, int typeIndex)
        {
            if (trackID <= UnTracked)
            {
                var type = TypeManager.GetType(typeIndex);
                throw new Exception($"Type: {type.Name} [TypeIndex={typeIndex}, TrackID={trackID}] is not Tracked by ComponentExistInfoSystem");
            }
        }
        public ComponentExistHandle GetExistHandle<T>()
        {
            Assert.IsTrue(mSorted, "Call GetTrackHand in OnUpdate of System, after ComponentExistInfoSystem is Sorted");
            var offset = TypeManagerExt.GetTypeOffset<T>();
            Assert.IsTrue(offset >= 0 && offset < TypeOffset2TrackIndex.Length, "Invalid Type");
            var trackID = TypeOffset2TrackIndex[offset];
            TrackID_Check(trackID, TypeManager.GetTypeIndex<T>());
            return new ComponentExistHandle() { trackID = trackID };
        }

        protected override void OnCreate() { Initialize(); }

        protected override void OnUpdate() { if (mInitialized) Sort(); }

        protected override void OnDestroy()
        {
            if (TypeOffset2TrackIndex.IsCreated) TypeOffset2TrackIndex.Dispose();
            mInitialized = false;
            mSorted = false;
            TrackedTypeCount = 0;
        }
    }

    public static class TypeManagerExt
    {
        public static int GetTypeOffset(int typeIndex) => typeIndex & TypeManager.ClearFlagsMask;
        public static int GetTypeOffset<T>() => GetTypeOffset(TypeManager.GetTypeIndex<T>());

        internal static void TypeCheck(bool listTypes = true)
        {
            var all = (NativeArray<TypeManager.TypeInfo>)TypeManager.AllTypes;
            var sb = new StringBuilder();
            //0=null 1=Entity pre subtract these two
            int unmanagedTypeCount = -2;
            int lastTypeOrder = 0;
            int last = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t.Category == TypeManager.TypeCategory.Class) continue;
                unmanagedTypeCount++;
                int typeOrder = t.TypeIndex & TypeManager.ClearFlagsMask;
                if (lastTypeOrder != 0 && lastTypeOrder != typeOrder - 1)
                {
                    var t_1 = all[last];
                    Debug.LogWarning("Jumped\n" +
                        $"Last[{last}]: {t_1.Type?.Name} Category=[{t_1.Category}] Index={t_1.TypeIndex.ToString("X8")} Order={lastTypeOrder}\n" +
                        $"This[{i}]: {t.Type?.Name} Category=[{t.Category}] Index={t.TypeIndex.ToString("X8")} Order{typeOrder}\n");
                }
                if (listTypes) sb.Append($"[{i}]: {t.Type?.Name} C=[{t.Category}] T={t.TypeIndex.ToString("X8")} O={typeOrder}\n");
                lastTypeOrder = typeOrder;
                last = i;
            }
            sb.Insert(0, $"None UnityEngine.Object TypeCount={unmanagedTypeCount}\n");
            sb.Insert(0, $"All Type Count={all.Length}\n");
            Debug.Log(sb.ToString());
        }
    }
}