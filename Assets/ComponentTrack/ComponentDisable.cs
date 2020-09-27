/************************************************************************************
| File: ComponentDisable.cs                                                         |
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
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Text;
using System.Diagnostics;
using Unity.Burst;

namespace SRTK
{
    using Debug = UnityEngine.Debug;
    using static ComponentDisable;
    using static ComponentDisableInfoSystem;

    public struct ComponentDisableHandle
    {
        internal int DisableID;
        public override string ToString() => $"DisableID={DisableID.ToString()}";
    }

    [GenerateAuthoringComponent]
    public struct ComponentDisable : IComponentData
    {
        /// <summary>
        /// Maximum Number of Tracked component count
        /// Change this value to use less Chunk memory or track more components
        /// There are already 698 Component types in ECS when this script is made, it may grow to several thouands
        /// Call TypeManagerExt.TypeCheck() to see how many are there now
        /// Must be dividable by 8
        /// </summary>
        public const int K_MaxTrackedComponentCount = 128;
        public const int K_Enabled = 0;
        public const int K_Disabled = 1;
        unsafe internal fixed byte OctTracks[K_OctTrackStateCount];//DualTrackState

        unsafe public bool GetEnabled(ComponentDisableHandle handle)
        {
            var octState = GetOctState(handle, out var bitShift);
            return (K_KeepLowestMask & (((byte)octState) >> bitShift)) == K_Enabled;
        }

        unsafe public void SetEnabled(ComponentDisableHandle handle, bool value)
        {
            ref var octState = ref GetOctState(handle.DisableID, out var bitShift);
            var enable = 1 << bitShift;
            var trueCase = (byte)(octState & ~enable);
            var falseCase = (byte)(octState | enable);
            octState = value ? trueCase : falseCase;
        }
        const int K_OctTrackStateCount = K_MaxTrackedComponentCount >> K_DisableID2OctIDShift;
        internal const int K_QctOffsetMask = 0b0111;
        internal const int K_KeepLowestMask = 0b0001;
        internal const int K_DisableID2OctIDShift = 3;

        unsafe internal ref byte GetOctState(int disableID, out int bitShift)
        {
            Assert.IsTrue(disableID >= 0 && disableID < K_MaxTrackedComponentCount, "Invalid Track ID");
            bitShift = (disableID & K_QctOffsetMask);
            var octID = disableID >> K_DisableID2OctIDShift;
            // Debug.Log($"[GetOctState] DisableID={disableID}, Shift={bitShift}, octID={octID}");
            fixed (byte* pOct = OctTracks) return ref *(pOct + octID);
        }

        internal byte GetOctState(ComponentDisableHandle handle, out int bitShift) => GetOctState(handle.DisableID, out bitShift);

        public override string ToString()
        {
            var sb = new StringBuilder();
            unsafe
            {
                for (int i = 0; i < K_OctTrackStateCount; i++)
                {
                    sb.Append($"[{i}]=");
                    sb.Append(OctTracks[i].ToString("X2"));
                    //sb.Append(Convert.ToInt32(Convert.ToString(OctTracks[i], 2)).ToString("D8"));
                    sb.Append('.');
                }
            }
            return sb.ToString();
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public class ComponentDisableInfoSystem : SystemBase
    {
        internal const int CanNotDisable = -1;
        internal NativeArray<int> TypeOffset2DisableID;
        private bool mInitialized = false;
        private int TrackedTypeCount = 0;

        public bool IsReady => mInitialized & TypeOffset2DisableID.IsCreated;
        public bool HasDisableInfo => IsReady & TrackedTypeCount > 0;

        unsafe internal void Initialize()
        {
            if (mInitialized) return;
            mInitialized = true;
            TypeManager.Initialize();
            TypeOffset2DisableID = new NativeArray<int>(TypeManager.GetTypeCount(), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            //Set all value to -1, which means we are tracking no components yet
            UnsafeUtility.MemSet(TypeOffset2DisableID.GetUnsafePtr(), 0xff, sizeof(int) * TypeOffset2DisableID.Length);
        }

        public struct DisableTypeInfo
        {
            public NativeArray<int>.ReadOnly TypeOffset2TrackIndex;
            public int RegisteredCount;
        }

        public DisableTypeInfo TrackInfo
        {
            get
            {
                Assert.IsTrue(IsReady, "TrackState not Initialize");
                return new DisableTypeInfo()
                {
                    TypeOffset2TrackIndex = TypeOffset2DisableID.AsReadOnly(),
                    RegisteredCount = TrackedTypeCount
                };
            }
        }

        /// <summary>
        /// Register a new type that can be disabled, must be called in main thread
        /// </summary>
        public void RegisterTypeForDisable<T>()
        {
            if (TrackedTypeCount > K_MaxTrackedComponentCount)
            {
                Debug.LogError($"Can not Track more than {K_MaxTrackedComponentCount} Disable types, consider increase ComponentDisable.K_MaxTrackedComponentCount");
                return;
            }
            Initialize();
            var typeOffset = TypeManagerExt.GetTypeOffset<T>();
            if (TypeOffset2DisableID[typeOffset] > CanNotDisable) return;
            TypeOffset2DisableID[typeOffset] = TrackedTypeCount++;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void DisableID_Check(int disableID, int typeIndex)
        {
            if (disableID <= CanNotDisable)
            {
                var type = TypeManager.GetType(typeIndex);
                throw new Exception($"Type: {type.Name} [TypeIndex={typeIndex}, DisableID={disableID}] is not Tracked by ComponentDisableInfoSystem");
            }
        }

        public ComponentDisableHandle GetDisableHandle<T>()
        {
            Assert.IsTrue(mInitialized, "Call GetTrackHand in OnUpdate of System, after ComponentDisableInfoSystem is initialized");
            var offset = TypeManagerExt.GetTypeOffset<T>();
            Assert.IsTrue(offset >= 0 && offset < TypeOffset2DisableID.Length, "Invalid Type");
            var disableID = TypeOffset2DisableID[offset];
            DisableID_Check(disableID, TypeManager.GetTypeIndex<T>());
            return new ComponentDisableHandle() { DisableID = disableID };
        }

        protected override void OnCreate() { Initialize(); }

        protected override void OnUpdate() { }

        protected override void OnDestroy()
        {
            if (TypeOffset2DisableID.IsCreated) TypeOffset2DisableID.Dispose();
            mInitialized = false;
        }
    }
}