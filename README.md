# UnityECS-ComponentStateTrack

1. Pre Component Remove/Add (2bit flags) Tracking with one Tracking System
2. Pre Component Enable/Disable (1bit flag) capability.

They works like this:

``` csharp
public struct DataA : IComponentData{}// Buffer/Component/SCD/Object are all supported
protected override void OnCreate()
 {
    DisableInfo = World.GetOrCreateSystem<ComponentDisableInfoSystem>();
    DisableInfo.RegisterTypeForDisable<DataA>();

    ExistInfo = World.GetOrCreateSystem<ComponentExistInfoSystem>();
    ExistInfo.RegisterTypeForTracking<DataA>();
}
protected override void OnUpdate()
{
    var disableHandle = DisableInfo.GetDisableHand<DataA>();
    var existHandle = ExistInfo.GetExistHandle<DataA>();

    Entities.WithoutBurst() // WithoutBurst Only for Debug.Log
    .WithChangeFilter<ComponentDisable>()
    .WithChangeFilter<ComponentExist>()
    .ForEach((Entity e, in ComponentDisable disable, in ComponentExist exist) =>
    {
        Debug.Log($"Entity[{e}] Has DataA={HasComponent<DataA>(e)} Enabled={disable.GetEnabled(disableHandle)}, Exist={exist.GetTrackState(existHandle)}");
    }).Schedule();
}
```

Bug report is expected
