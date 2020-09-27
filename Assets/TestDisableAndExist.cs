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

namespace SRTK
{
    public struct DataA : IComponentData { public int Value; }
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
            DisableInfo.RegisterTypeForDisable<DataC>();

            ExistInfo = World.GetOrCreateSystem<ComponentExistInfoSystem>();
            ExistInfo.RegisterTypeForTracking<DataA>();
            ExistInfo.RegisterTypeForTracking<DataB>();

            target = EntityManager.CreateEntity();
            EntityManager.AddComponent<ComponentExist>(target);
            EntityManager.AddComponent<ComponentDisable>(target);
            ECBS = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            var disableHandleA = DisableInfo.GetDisableHandle<DataA>();
            var disableHandleC = DisableInfo.GetDisableHandle<DataC>();
            var existHandleA = ExistInfo.GetExistHandle<DataA>();
            var existHandleB = ExistInfo.GetExistHandle<DataB>();

            Entities.WithoutBurst()
            .WithChangeFilter<ComponentDisable>()
            .WithChangeFilter<ComponentExist>()
            .ForEach((Entity e, in ComponentDisable disable, in ComponentExist exist) =>
            {
                Debug.Log($"Entity[{e}] Has DataA={HasComponent<DataA>(e)} Enabled={disable.GetEnabled(disableHandleA)}, Exist={exist.GetTrackState(existHandleA)}");
            }).Schedule();

            var keyboard = InputSystem.GetDevice<Keyboard>();
            var ECB = ECBS.CreateCommandBuffer();
            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                ECB.AddComponent<DataA>(target);
                Debug.LogWarning($"Add DataA to {target}");
            }
            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                ECB.RemoveComponent<DataA>(target);
                Debug.LogWarning($"Remove DataA From {target}");

            }
            if (keyboard.digit3Key.wasPressedThisFrame)
            {
                Entities.ForEach((ref ComponentDisable disable, in ComponentExist exist, in DataA a) =>
                { disable.SetEnabled(disableHandleA, false); }).Schedule();
                Debug.LogWarning($"Setting DataA Disable");
            }
            if (keyboard.digit4Key.wasPressedThisFrame)
            {
                Entities.ForEach((ref ComponentDisable disable, in ComponentExist exist, in DataA a) =>
                { disable.SetEnabled(disableHandleA, true); }).Schedule();
                Debug.LogWarning($"Setting DataA Enable");
            }


            ECBS.AddJobHandleForProducer(Dependency);
        }

        protected override void OnDestroy() { }
    }
}