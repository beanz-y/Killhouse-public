using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using OperatorKillhouse.Core;

namespace OperatorKillhouse;

// Aggregate frame gaps; do not scan the scene or log every hitch. Unity's frame
// delta includes engine/GPU/other mods and is not a Killhouse CPU measurement.
internal static class PerformanceJournal
{
    private static float nextSample,maximum,windowStarted;
    private static int frames,over100,over500;
    private static bool collecting;
    private static readonly WorkMetrics work=new("update","gui","draft-save","native-initialization","encounter-residue","damage-resources","performance-sample","co-op","reset-verification","editor-permission","range-build","deferred-release","map-selection","encounter","triggers","lighting","fixture-build","breaker-bind","power-reset","prop-physics","native-fault-recovery","range-discovery","lighting-discovery","menu-key");
    private static bool Collecting() {
        bool enabled=DiagnosticLog.Enabled;
        if(enabled!=collecting){Clear();collecting=enabled;}
        return enabled;
    }
    internal static long Begin()=>Collecting()?Stopwatch.GetTimestamp():0;
    internal static long Allocated()=>Collecting()?GC.GetAllocatedBytesForCurrentThread():0;
    internal static void Stage(string name,ref long began,ref long allocated){
        if(!Collecting() || began==0)return;
        long end=Stopwatch.GetTimestamp(),bytes=GC.GetAllocatedBytesForCurrentThread();
        work.Record(name,(end-began)*1000d/Stopwatch.Frequency,bytes-allocated);began=end;allocated=bytes;
    }
    internal static void Complete(string name,long began,long allocatedBefore) {
        if(!Collecting() || began==0)return;
        work.Record(name,(Stopwatch.GetTimestamp()-began)*1000d/Stopwatch.Frequency,GC.GetAllocatedBytesForCurrentThread()-allocatedBefore);
    }
    internal static void Tick() {
        if(!Collecting())return;
        if(Runtime.Sessions.Count==0){if(nextSample!=0 || frames!=0)Clear();return;}
        FrameTimingJournal.Tick();
        float ms=Time.unscaledDeltaTime*1000;
        frames++;maximum=Math.Max(maximum,ms);if(ms>=100)over100++;if(ms>=500)over500++;
        if(nextSample==0){windowStarted=Time.unscaledTime;nextSample=Time.unscaledTime+30;return;}
        if(Time.unscaledTime<nextSample)return;nextSample=Time.unscaledTime+30;
        try {
        using var process=Process.GetCurrentProcess();
        CoopLog.Event("performance-sample",new{
            frameWindow=frames,maxFrameMs=maximum,framesOver100Ms=over100,framesOver500Ms=over500,
            sampleSeconds=Time.unscaledTime-windowStarted,meanFps=frames/Math.Max(.001f,Time.unscaledTime-windowStarted),frameTiming=FrameTimingJournal.Take(),placedLightShadowQuality=Plugin.PlacedLightShadows.Value,
            activePlacedLightEmitters=CircuitFixtures.ActiveEmitterCount,cachePlacedLightShadows=Plugin.CachePlacedLightShadows.Value,
            cachedPlacedLightEmitters=CircuitFixtures.ActiveCachedEmitterCount,cacheFallbackPlacedLightEmitters=CircuitFixtures.ActiveCacheFallbackEmitterCount,
            mapLighting=MapNightMode.Current,spawnLights=SpawnLighting.Count,
            processPrivateBytes=process.PrivateMemorySize64,processWorkingSetBytes=process.WorkingSet64,
            unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),unityReservedBytes=Profiler.GetTotalReservedMemoryLong(),
            unityMonoUsedBytes=Profiler.GetMonoUsedSizeLong(),unityMonoHeapBytes=Profiler.GetMonoHeapSizeLong(),
            graphicsDriverBytes=Profiler.GetAllocatedMemoryForGraphicsDriver(),
            managedBytes=GC.GetTotalMemory(false),managedTotalAllocatedBytes=GC.GetTotalAllocatedBytes(false),managedGen0=GC.CollectionCount(0),managedGen1=GC.CollectionCount(1),managedGen2=GC.CollectionCount(2),ownedTrees=NativeDamageResources.OwnerCount,
            menuOpen=Runtime.Visible,editorOpen=Runtime.EditorVisible,sessionMapSlots=Runtime.Sessions.Values.Sum(s=>s.Items.Count),laptopScreens=LaptopLighting.Count,work=work.Take(),
            ownedRenderTextures=NativeDamageResources.TextureCount,ownedMaterials=NativeDamageResources.MaterialCount,
            movableProps=PropPhysics.Count,ownedPhysicsMaterials=PropPhysics.ContactMaterialCount,createdPhysicsMaterials=PropPhysics.CreatedContactMaterials,releasedPhysicsMaterials=PropPhysics.ReleasedContactMaterials,
            propPoseSamples=PropPlayback.Received,rejectedPropPoseSamples=PropPlayback.Rejected,propHardCorrections=PropPlayback.HardCorrections,propBufferRestarts=PropPlayback.BufferRestarts,maxPropArrivalGapMs=PropPlayback.MaxArrivalGap*1000,
            protectedRangeDoors=DoorIntegrity.Count,
            createdRenderTextures=NativeDamageResources.CreatedTextures,releasedRenderTextures=NativeDamageResources.ReleasedTextures,
            createdMaterials=NativeDamageResources.CreatedMaterials,releasedMaterials=NativeDamageResources.ReleasedMaterials});
        } catch(Exception error){Plugin.Logger.LogWarning("Performance sample unavailable: "+error.GetBaseException().Message);}
        frames=over100=over500=0;maximum=0;windowStarted=Time.unscaledTime;
    }
    private static void Clear(){nextSample=maximum=windowStarted=0;frames=over100=over500=0;work.Reset();FrameTimingJournal.Reset();}
    internal static void Reset(){Clear();collecting=false;}
}
