using UnityEngine.SceneManagement;

namespace OperatorKillhouse;

internal static partial class Runtime
{
    private static void RestoreAll() {
        var failures=new List<Exception>();
        void Release(Action action){try{action();}catch(Exception error){failures.Add(error);}}
        Release(MapNightMode.Release);
        Release(PropPhysics.Clear);
        foreach(var session in Sessions.Values.ToArray())Release(()=>Detach(session));
        if(failures.Count>0)throw new AggregateException("Range restoration failed.",failures);
    }
    private static void ReleaseOnStop(string group,Action release) {
        try{release();}
        catch(Exception error){Plugin.Logger.LogWarning("Killhouse cleanup ("+group+"): "+error);}
    }
    private static void ReleaseTraining() {
        var failures=new List<Exception>();
        void Release(string group,Action action) {
            try{action();}catch(Exception error){failures.Add(error);Plugin.Logger.LogWarning("Killhouse cleanup ("+group+"): "+error);}
        }
        Release("menu notice",menuNotice.Reset);noticeStyle=null;noticeContent=null;RangeSetupFailure="";
        stopping=true;
        // A failed native release must not strand the other asset groups or
        // leave our global patches/scene callback installed after shutdown.
        Release("menus",Close);
        stockInitialization.Clear();pendingSelections.Clear();pendingResets.Clear();
        Release("co-op",CoopNetwork.Dispose);
        Release("ranges",RestoreAll);
        Release("encounter residue",EncounterResidue.Reset);
        Release("navigation",EncounterNavigation.Dispose);
        Release("furniture catalog",NativeFurnitureCatalog.Clear);
        Release("deferred objects",DeferredRelease.Drain);
        Release("gear locker",OutdoorGearLocker.Clear);
        Release("gear assets",NativeGearAssets.Stop);
        Release("SSE objects",SseObjects.Clear);
        Release("SSE assets",NativeSseAssets.Stop);
        Release("fixture assets",NativeFixtureAssets.Stop);
        Release("circuits",CircuitFixtures.Clear);
        Release("breaker assets",NativeBreakerAssets.Stop);
        Release("damage resources",NativeDamageResources.Dispose);
        Release("performance journal",PerformanceJournal.Reset);
        NativeLodOwnership.Clear();
        rangeScenes.Clear();quarantined.Clear();pendingFaults.Clear();activeSelections.Clear();
        Sessions.Clear();focused=null;editor=null;proposalPreview=null;previewHouse=0;
        initializationGate=null;initializationInstance=0;nextInitialization=0;nextScan=0;transition=false;
        // Keep failed patch ownership available for a later cleanup attempt.
        // Resource failures above must never skip the native detour release.
        if(patches!=null)Release("patches",()=>{TrainingPatches.Remove(patches);patches=null;});
        if(failures.Count>0)throw new AggregateException("Training cleanup failed.",failures);
        Status="Enter an Outdoors training range to use Killhouse.";
        Plugin.Logger.LogInfo("Training inactive: Killhouse hooks and resources released.");
    }
}
