# UnityECS-ComponentStateTrack

1. Pre Component Remove/Add (2bit flags) Tracking with one Tracking System
2. Pre Component Enable/Disable (1bit flag) capability.

## How It Works

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
    var disableHandle = DisableInfo.GetDisableHandle<DataA>();
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

## Added OptionalComponent API

This it to solve the case when optional data create to many branch in Job or Duplicate Entities.ForEach
Data can be collected ahead, and with default value backup. So ForEach/IJobChunk can run the same routain that allway has this optional Data T.

In OnUpdate of ComponentSystem, the following function will collect ComponentData of Type T, and store them in the returned Array. If T is not found in some of the Entity, defaultValue will be filled;

``` csharp
public static NativeArray<T> ToOptionalDataArrayAsync<T>(this ref EntityQuery query, SystemBase system, Allocator allocator, ref JobHandle dependency, T defaultValue = default)
```

In IJobChunk, the following function will return a NativeArray of size: Chunk.Count. if the chunk dose not have T then the array will be filled with defaultValue;

``` csharp
public static NativeArray<T> GetOptionalNativeArray<T>(this ref ArchetypeChunk chunk, ComponentTypeHandle<T> typeHandle, int firstEntityIndex, T defaultValue);
```

## Cheers! Bug report is expected.