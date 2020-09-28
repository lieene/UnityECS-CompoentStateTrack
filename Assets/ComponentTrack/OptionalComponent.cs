/************************************************************************************
| File: OptionalComponent.cs                                                        |
| Project: lieene.OptionalComponent                                                 |
| Created Date: Mon Sep 28 2020                                                     |
| Author: Lieene Guo                                                                |
| -----                                                                             |
| Last Modified: Mon Sep 28 2020                                                    |
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
| Date      	By	Comments                                                          |
| ----------	---	----------------------------------------------------------        |
************************************************************************************/

using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Assertions;
using Unity.Burst;
using Unity.Jobs;
using System.Diagnostics;
using System;

namespace SRTK
{
    public static class OptionalComponent
    {
        unsafe public static NativeArray<T> ToOptionalDataArray<T>(this ref EntityQuery query, Allocator allocator, T defaultValue = default)
            where T : unmanaged, IComponentData
        {
            int totalEntityCount = query.CalculateEntityCount();
            var dataOut = new NativeArray<T>(totalEntityCount, allocator, NativeArrayOptions.UninitializedMemory);
            var pDst = (T*)dataOut.GetUnsafePtr();
            //var dataSize = UnsafeUtility.SizeOf<T>();
            var dataSize = sizeof(T);

            var entityCounter = 0;
            var imp = query._GetImpl();
            var filter = imp->_Filter;
            var matches = imp->_QueryData->MatchingArchetypes;
            var matchCount = matches.Length;
            var typeIndex = TypeManager.GetTypeIndex<T>();
            for (int ia = 0; ia < matchCount; ia++)
            {
                var archetype = (*matches.Ptr) + ia;
                var types = (int*)archetype->Archetype->Types;
                var typesCount = archetype->Archetype->TypesCount;
                //var indexInArchetype = typeIndex.BinarySearchIn(types, typesCount);
                var indexInArchetype = NativeArrayExtensions.IndexOf<int, int>(types, typesCount, typeIndex);

                if (indexInArchetype < 0)//Type not found in Archetype
                {
                    var matchChunkCount = archetype->Archetype->Chunks.Count;
                    for (int ic = 0; ic < matchChunkCount; ic++)
                    {
                        var chunk = archetype->Archetype->Chunks[ic];
                        var entityCount = chunk->Count;
                        UnsafeUtility.MemCpyReplicate(pDst, &defaultValue, dataSize, entityCount);
                        entityCounter += entityCount;
                    }
                }
                else//Type found in Archetype
                {
                    var matchChunkCount = archetype->Archetype->Chunks.Count;
                    for (int ic = 0; ic < matchChunkCount; ic++)
                    {
                        if (!archetype->ChunkMatchesFilter(ic, ref filter)) continue;
                        var chunk = archetype->Archetype->Chunks[ic];
                        var entityCount = chunk->Count;
                        var pSrc = ((T*)ChunkDataUtility.GetComponentDataRO(chunk, 0, indexInArchetype)) + entityCounter;
                        UnsafeUtility.MemCpy(pDst, pSrc, dataSize * entityCount);
                        entityCounter += entityCount;
                    }
                }
            }
            return dataOut;
        }

        public static NativeArray<T> GetOptionalNativeArray<T>(this ref ArchetypeChunk chunk, ComponentTypeHandle<T> typeHandle, int firstEntityIndex)
            where T : unmanaged, IComponentData
        {
            if (chunk.Has(typeHandle)) return chunk.GetNativeArray<T>(typeHandle);
            else return new NativeArray<T>(chunk.Count, Allocator.Temp, NativeArrayOptions.ClearMemory);
        }

        public static NativeArray<T> GetOptionalNativeArray<T>(this ref ArchetypeChunk chunk, ComponentTypeHandle<T> typeHandle, int firstEntityIndex, T defaultValue)
            where T : unmanaged, IComponentData
        {
            if (chunk.Has(typeHandle)) return chunk.GetNativeArray<T>(typeHandle);
            else
            {
                var count = chunk.Count;
                var array = new NativeArray<T>(count, Allocator.Temp, NativeArrayOptions.ClearMemory);
                unsafe
                {
                    var pDst = ((T*)array.GetUnsafePtr()) + firstEntityIndex;
                    UnsafeUtility.MemCpyReplicate(pDst, &defaultValue, sizeof(T), count);
                }
                return array;
            }
        }

        public static NativeArray<T> ToOptionalDataArrayAsync<T>(this ref EntityQuery query, SystemBase system, Allocator allocator, ref JobHandle dependency, T defaultValue = default)
            where T : unmanaged, IComponentData
        {
            int totalEntityCount = query.CalculateEntityCount();
            var dataOut = new NativeArray<T>(totalEntityCount, allocator, NativeArrayOptions.UninitializedMemory);
            dependency = new GetOptionalDataJob<T>()
            {
                DefaultValue = defaultValue,
                OptionalDataType = system.GetComponentTypeHandle<T>(true),
                DataOut = dataOut,
            }.ScheduleParallel(query, dependency);
            return dataOut;
        }

        public static (NativeArray<T1>, NativeArray<T2>) ToOptionalDataArrayAsync<T1, T2>(
            this ref EntityQuery query, SystemBase system, Allocator allocator, ref JobHandle dependency,
            T1 defaultValue1 = default, T2 defaultValue2 = default)
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
        {
            int totalEntityCount = query.CalculateEntityCount();
            var dataOut1 = new NativeArray<T1>(totalEntityCount, allocator, NativeArrayOptions.UninitializedMemory);
            var dataOut2 = new NativeArray<T2>(totalEntityCount, allocator, NativeArrayOptions.UninitializedMemory);
            dependency = new GetOptionalDataJob<T1, T2>()
            {
                DefaultValue1 = defaultValue1,
                DefaultValue2 = defaultValue2,
                OptionalDataType1 = system.GetComponentTypeHandle<T1>(true),
                OptionalDataType2 = system.GetComponentTypeHandle<T2>(true),
                DataOut1 = dataOut1,
                DataOut2 = dataOut2,
            }.ScheduleParallel(query, dependency);
            return (dataOut1, dataOut2);
        }


        public static (NativeArray<T1>, NativeArray<T2>, NativeArray<T3>) ToOptionalDataArrayAsync<T1, T2, T3>(
            this ref EntityQuery query, SystemBase system, Allocator allocator, ref JobHandle dependency,
            T1 defaultValue1 = default, T2 defaultValue2 = default, T3 defaultValue3 = default)
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
        {
            int totalEntityCount = query.CalculateEntityCount();
            var dataOut1 = new NativeArray<T1>(totalEntityCount, allocator, NativeArrayOptions.UninitializedMemory);
            var dataOut2 = new NativeArray<T2>(totalEntityCount, allocator, NativeArrayOptions.UninitializedMemory);
            var dataOut3 = new NativeArray<T3>(totalEntityCount, allocator, NativeArrayOptions.UninitializedMemory);
            dependency = new GetOptionalDataJob<T1, T2, T3>()
            {
                DefaultValue1 = defaultValue1,
                DefaultValue2 = defaultValue2,
                DefaultValue3 = defaultValue3,
                OptionalDataType1 = system.GetComponentTypeHandle<T1>(true),
                OptionalDataType2 = system.GetComponentTypeHandle<T2>(true),
                OptionalDataType3 = system.GetComponentTypeHandle<T3>(true),
                DataOut1 = dataOut1,
                DataOut2 = dataOut2,
                DataOut3 = dataOut3,
            }.ScheduleParallel(query, dependency);
            return (dataOut1, dataOut2, dataOut3);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void SizeCheck<T>(ref EntityQuery query, NativeArray<T> dataOut)
            where T : unmanaged, IComponentData
        {
            var count = query.CalculateChunkCount();
            if (count != dataOut.Length) throw new OverflowException($"Query Entity Count[{count}] and DataOut size[{dataOut.Length}] Missmatch");
        }

        public static JobHandle ToOptionalDataArrayAsync<T>(
            this ref EntityQuery query, SystemBase system, Allocator allocator,ref JobHandle dependency,
            NativeArray<T> dataOut, T defaultValue = default)
            where T : unmanaged, IComponentData
        {
            SizeCheck(ref query, dataOut);
            var handel = system.GetComponentTypeHandle<T>();
            return new GetOptionalDataJob<T>()
            {
                DefaultValue = defaultValue,
                OptionalDataType = system.GetComponentTypeHandle<T>(true),
                DataOut = dataOut,
            }.ScheduleParallel(query, dependency);
        }

        public unsafe struct GetOptionalDataJob<T> : IJobChunk
            where T : unmanaged, IComponentData
        {
            [ReadOnly] public ComponentTypeHandle<T> OptionalDataType;
            [WriteOnly] public NativeArray<T> DataOut;
            public T DefaultValue;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                int chunkCount = chunk.Count;
                if (chunk.Has(OptionalDataType))
                {
                    var data = chunk.GetNativeArray(OptionalDataType);
                    DataOut.Slice(firstEntityIndex, chunkCount).CopyFrom(data);
                }
                else
                {
                    var pDst = ((T*)DataOut.GetUnsafePtr()) + firstEntityIndex;
                    var defaultValue = DefaultValue;
                    UnsafeUtility.MemCpyReplicate(pDst, &defaultValue, sizeof(T), chunkCount);
                }
            }
        }

        public static JobHandle ToOptionalDataArrayAsync<T1, T2>(
            this ref EntityQuery query, SystemBase system, Allocator allocator,ref JobHandle dependency,
            NativeArray<T1> dataOut1, NativeArray<T2> dataOut2,
            T1 defaultValue1 = default, T2 defaultValue2 = default)
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
        {
            SizeCheck(ref query, dataOut1);
            SizeCheck(ref query, dataOut2);
            var handel = system.GetComponentTypeHandle<T1>();
            return new GetOptionalDataJob<T1, T2>()
            {
                DefaultValue1 = defaultValue1,
                DefaultValue2 = defaultValue2,
                OptionalDataType1 = system.GetComponentTypeHandle<T1>(true),
                OptionalDataType2 = system.GetComponentTypeHandle<T2>(true),
                DataOut1 = dataOut1,
                DataOut2 = dataOut2,
            }.ScheduleParallel(query, dependency);
        }

        public unsafe struct GetOptionalDataJob<T1, T2> : IJobChunk
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
        {
            [ReadOnly] public ComponentTypeHandle<T1> OptionalDataType1;
            [ReadOnly] public ComponentTypeHandle<T2> OptionalDataType2;
            [WriteOnly] public NativeArray<T1> DataOut1;
            [WriteOnly] public NativeArray<T2> DataOut2;
            public T1 DefaultValue1;
            public T2 DefaultValue2;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                int chunkCount = chunk.Count;
                if (chunk.Has(OptionalDataType1))
                {
                    var data = chunk.GetNativeArray(OptionalDataType1);
                    DataOut1.Slice(firstEntityIndex, chunkCount).CopyFrom(data);
                }
                else
                {
                    var pDst = ((T1*)DataOut1.GetUnsafePtr()) + firstEntityIndex;
                    var defaultValue = DefaultValue1;
                    UnsafeUtility.MemCpyReplicate(pDst, &defaultValue, sizeof(T1), chunkCount);
                }
                if (chunk.Has(OptionalDataType2))
                {
                    var data = chunk.GetNativeArray(OptionalDataType2);
                    DataOut2.Slice(firstEntityIndex, chunkCount).CopyFrom(data);
                }
                else
                {
                    var pDst = ((T2*)DataOut2.GetUnsafePtr()) + firstEntityIndex;
                    var defaultValue = DefaultValue2;
                    UnsafeUtility.MemCpyReplicate(pDst, &defaultValue, sizeof(T2), chunkCount);
                }
            }
        }


        public static JobHandle ToOptionalDataArrayAsync<T1, T2, T3>(
            this ref EntityQuery query, SystemBase system, Allocator allocator,ref JobHandle dependency,
            NativeArray<T1> dataOut1, NativeArray<T2> dataOut2, NativeArray<T3> dataOut3,
            T1 defaultValue1 = default, T2 defaultValue2 = default, T3 defaultValue3 = default)
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
        {
            SizeCheck(ref query, dataOut1);
            SizeCheck(ref query, dataOut2);
            SizeCheck(ref query, dataOut3);
            var handel = system.GetComponentTypeHandle<T1>();
            return new GetOptionalDataJob<T1, T2, T3>()
            {
                DefaultValue1 = defaultValue1,
                DefaultValue2 = defaultValue2,
                DefaultValue3 = defaultValue3,
                OptionalDataType1 = system.GetComponentTypeHandle<T1>(true),
                OptionalDataType2 = system.GetComponentTypeHandle<T2>(true),
                OptionalDataType3 = system.GetComponentTypeHandle<T3>(true),
                DataOut1 = dataOut1,
                DataOut2 = dataOut2,
                DataOut3 = dataOut3,
            }.ScheduleParallel(query, dependency);
        }

        public unsafe struct GetOptionalDataJob<T1, T2, T3> : IJobChunk
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
        {
            [ReadOnly] public ComponentTypeHandle<T1> OptionalDataType1;
            [ReadOnly] public ComponentTypeHandle<T2> OptionalDataType2;
            [ReadOnly] public ComponentTypeHandle<T3> OptionalDataType3;
            [WriteOnly] public NativeArray<T1> DataOut1;
            [WriteOnly] public NativeArray<T2> DataOut2;
            [WriteOnly] public NativeArray<T3> DataOut3;
            public T1 DefaultValue1;
            public T2 DefaultValue2;
            public T3 DefaultValue3;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                int chunkCount = chunk.Count;
                if (chunk.Has(OptionalDataType1))
                {
                    var data = chunk.GetNativeArray(OptionalDataType1);
                    DataOut1.Slice(firstEntityIndex, chunkCount).CopyFrom(data);
                }
                else
                {
                    var pDst = ((T1*)DataOut1.GetUnsafePtr()) + firstEntityIndex;
                    var defaultValue = DefaultValue1;
                    UnsafeUtility.MemCpyReplicate(pDst, &defaultValue, sizeof(T1), chunkCount);
                }
                if (chunk.Has(OptionalDataType2))
                {
                    var data = chunk.GetNativeArray(OptionalDataType2);
                    DataOut2.Slice(firstEntityIndex, chunkCount).CopyFrom(data);
                }
                else
                {
                    var pDst = ((T2*)DataOut2.GetUnsafePtr()) + firstEntityIndex;
                    var defaultValue = DefaultValue2;
                    UnsafeUtility.MemCpyReplicate(pDst, &defaultValue, sizeof(T2), chunkCount);
                }
                if (chunk.Has(OptionalDataType3))
                {
                    var data = chunk.GetNativeArray(OptionalDataType3);
                    DataOut3.Slice(firstEntityIndex, chunkCount).CopyFrom(data);
                }
                else
                {
                    var pDst = ((T3*)DataOut3.GetUnsafePtr()) + firstEntityIndex;
                    var defaultValue = DefaultValue3;
                    UnsafeUtility.MemCpyReplicate(pDst, &defaultValue, sizeof(T3), chunkCount);
                }
            }
        }
    }
}