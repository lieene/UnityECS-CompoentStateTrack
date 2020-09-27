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
using UnityEngine;
using Unity.Entities;
using Unity.Assertions;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using SRTK;
using SRTK.Utility;
using SRTK.Conscious;
using System.Text;

namespace SRTK
{
    using static ComponentDisable;
    using static ComponentDisableInfoSystem;

    public struct ComponentDisableHandle { internal int DisableID; }

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
            var quadState = GetOctState(handle, out var bitShift);
            return (K_KeepLowestMask & (((byte)quadState) >> bitShift)) == K_Enabled;
        }

        unsafe public void SetEnabled(ComponentDisableHandle handle, bool value)
        {
            ref var quadState = ref GetOctState(handle.DisableID, out var bitShift);
            var enable = 1 << bitShift;
            var trueCase = (byte)(quadState & ~enable);
            var falseCase = (byte)(quadState | enable);
            quadState = value ? trueCase : falseCase;
        }
        const int K_OctTrackStateCount = K_MaxTrackedComponentCount >> K_DisableID2QuadIDShift;
        internal const int K_QuadOffsetMask = 0x0008;
        internal const int K_KeepLowestMask = 0b0001;
        internal const int K_DisableID2QuadIDShift = 3;

        unsafe internal ref byte GetOctState(int disableID, out int bitShift)
        {
            Assert.IsTrue(disableID >= 0 && disableID < K_MaxTrackedComponentCount, "Invalid Track ID");
            bitShift = (disableID & K_QuadOffsetMask);
            var quadID = disableID >> K_DisableID2QuadIDShift;
            fixed (byte* pQuads = OctTracks) return ref *(pQuads + quadID);
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
                    sb.Append((OctTracks[i]).ToString());
                    sb.Append('.');
                }
            }
            return sb.ToString();
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public class ComponentDisableInfoSystem : SystemBase
    {
        internal const int CanDisable = 1;
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
        /// Register a new type that can be tracked, must be called in main thread
        /// </summary>
        public void RegisterTypeForDisable<T>()
        {
            Assert.IsTrue(mInitialized, "RegisterTypeForTracking must be call in OnCreate and before ComponentDisableInfoSystem's first update!");
            Initialize();
            var typeOffset = TypeManagerExt.GetTypeOffset<T>();
            TypeOffset2DisableID[typeOffset] = CanDisable;
        }

        public ComponentDisableHandle GetDisableHand<T>()
        {
            Assert.IsTrue(mInitialized, "Call GetTrackHand in OnUpdate of System, after ComponentDisableInfoSystem is initialized");
            var offset = TypeManagerExt.GetTypeOffset<T>();
            Assert.IsTrue(offset >= 0 && offset < TypeOffset2DisableID.Length, "Invalid Type");
            var disableID = TypeOffset2DisableID[offset];
            Assert.IsTrue(disableID > CanNotDisable, "Type is not tracked");
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