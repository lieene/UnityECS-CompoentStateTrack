/************************************************************************************
| File: TestDisableAndExist.cs                                                      |
| Project: lieene.RunTests                                                          |
| Created Date: Sun Sep 27 2020                                                     |
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

using UnityEngine;
using Unity.Entities;
using UnityEngine.InputSystem;
using Unity.Collections;

namespace SRTK
{
    public struct DataA : IComponentData { public int Value; }
    public struct D0 : IComponentData { }
    public struct D1 : IComponentData { }
    public struct D2 : IComponentData { }
    public struct D3 : IComponentData { }
    public struct D4 : IComponentData { }
    public struct D5 : IComponentData { }
    public struct D6 : IComponentData { }
    public struct D7 : IComponentData { }
    public struct D8 : IComponentData { }
    public struct DataB : IBufferElementData { public int Value; }
    public struct DataC : IComponentData { }

    public class TestDisableAndExist : SystemBase
    {
        ComponentDisableInfoSystem DisableInfo;
        ComponentExistInfoSystem ExistInfo;
        Entity target;
        EntityCommandBufferSystem ECBS;
        protected override void OnCreate()
        {
            DisableInfo = World.GetOrCreateSystem<ComponentDisableInfoSystem>();
            DisableInfo.RegisterTypeForDisable<DataA>();
            DisableInfo.RegisterTypeForDisable<D0>();
            DisableInfo.RegisterTypeForDisable<D1>();
            DisableInfo.RegisterTypeForDisable<D2>();
            DisableInfo.RegisterTypeForDisable<D3>();
            DisableInfo.RegisterTypeForDisable<D4>();
            DisableInfo.RegisterTypeForDisable<D5>();
            DisableInfo.RegisterTypeForDisable<D6>();
            DisableInfo.RegisterTypeForDisable<D7>();
            DisableInfo.RegisterTypeForDisable<DataC>();

            ExistInfo = World.GetOrCreateSystem<ComponentExistInfoSystem>();
            ExistInfo.RegisterTypeForTracking<DataA>();
            ExistInfo.RegisterTypeForTracking<D0>();
            ExistInfo.RegisterTypeForTracking<D1>();
            ExistInfo.RegisterTypeForTracking<D2>();
            ExistInfo.RegisterTypeForTracking<D3>();
            ExistInfo.RegisterTypeForTracking<D4>();
            ExistInfo.RegisterTypeForTracking<D5>();
            ExistInfo.RegisterTypeForTracking<D6>();
            ExistInfo.RegisterTypeForTracking<D7>();
            ExistInfo.RegisterTypeForTracking<D8>();
            ExistInfo.RegisterTypeForTracking<DataB>();

            target = EntityManager.CreateEntity();
            EntityManager.AddComponent<ComponentExist>(target);
            EntityManager.AddComponent<ComponentDisable>(target);
            ECBS = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();

            DisableACRecord = new NativeArray<bool>(2, Allocator.Persistent); ;
        }
        NativeArray<bool> DisableACRecord;

        protected override void OnUpdate()
        {
            var disableHandleA = DisableInfo.GetDisableHandle<DataA>();
            var disableHandleC = DisableInfo.GetDisableHandle<DataC>();
            var existHandleA = ExistInfo.GetExistHandle<DataA>();
            var existHandleB = ExistInfo.GetExistHandle<DataB>();
            var keyboard = InputSystem.GetDevice<Keyboard>();
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                Debug.Log("Disable" + EntityManager.GetComponentData<ComponentDisable>(target).ToString() + "\nExist" + EntityManager.GetComponentData<ComponentExist>(target).ToString());
            }
            if (keyboard.pKey.wasPressedThisFrame)
            {
                Debug.Log($"A: {disableHandleA}|{existHandleA}, C: {disableHandleC}, B: {existHandleB}");
            }

            var recordCache = DisableACRecord;
            var BAccess = GetBufferFromEntity<DataB>();
            Entities.WithoutBurst()
            .ForEach((Entity e, in ComponentDisable disable, in ComponentExist exist) =>
            {
                bool enabledA = disable.GetEnabled(disableHandleA);
                bool enabledC = disable.GetEnabled(disableHandleC);
                var stateA = exist.GetTrackState(existHandleA);
                var stateB = exist.GetTrackState(existHandleB);
                if (recordCache[0] != enabledA || recordCache[1] != enabledC ||
                    stateA == ExistState.AddedLastSync || stateA == ExistState.RemovedLastSync ||
                    stateB == ExistState.AddedLastSync || stateB == ExistState.RemovedLastSync)
                {
                    Debug.Log(
                        $"Entity[{e}] HasA={HasComponent<DataA>(e)} HasB={BAccess.HasComponent(e)} HasC={HasComponent<DataC>(e)}\n" +
                        $"  EnabledA={enabledA}, EnabledC={enabledC}\n" +
                        $"  ExistA={stateA} ExistB={stateB}");
                }
                recordCache[0] = enabledA;
                recordCache[1] = enabledC;
            }).Schedule();

            var ECB = ECBS.CreateCommandBuffer();
            if (keyboard.qKey.wasPressedThisFrame)
            {
                ECB.AddComponent<DataA>(target);
                Debug.LogWarning($"Add DataA to {target}");
            }
            if (keyboard.aKey.wasPressedThisFrame)
            {
                ECB.RemoveComponent<DataA>(target);
                Debug.LogWarning($"Remove DataA From {target}");
            }

            if (keyboard.wKey.wasPressedThisFrame)
            {
                ECB.AddBuffer<DataB>(target);
                Debug.LogWarning($"Add DataB to {target}");
            }
            if (keyboard.sKey.wasPressedThisFrame)
            {
                ECB.RemoveComponent<DataB>(target);
                Debug.LogWarning($"Remove DataB From {target}");
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                ECB.AddComponent<DataC>(target);
                Debug.LogWarning($"Add DataC to {target}");
            }
            if (keyboard.dKey.wasPressedThisFrame)
            {
                ECB.RemoveComponent<DataC>(target);
                Debug.LogWarning($"Remove DataC From {target}");
            }


            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                Entities.ForEach((ref ComponentDisable disable) => { disable.SetEnabled(disableHandleA, !disable.GetEnabled(disableHandleA)); }).Schedule();
                Debug.LogWarning($"Toggle DataA Disable");
            }
            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                Entities.ForEach((ref ComponentDisable disable) => { disable.SetEnabled(disableHandleC, !disable.GetEnabled(disableHandleC)); }).Schedule();
                Debug.LogWarning($"Toggle DataC Disable");
            }



            ECBS.AddJobHandleForProducer(Dependency);
        }

        protected override void OnDestroy()
        {
            DisableACRecord.Dispose();
        }
    }
}