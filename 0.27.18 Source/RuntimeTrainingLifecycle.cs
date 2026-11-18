using HarmonyLib;
using OperatorKillhouse.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperatorKillhouse;

internal static partial class Runtime
{
    private static TrainingLifetime? trainingLifetime;
    private static bool watchingTraining;
    private static float nextLifetimeRetry;
    private static UnityEngine.Events.UnityAction<Scene,LoadSceneMode>? trainingLoaded;
    private static UnityEngine.Events.UnityAction<Scene,Scene>? trainingChanged;

    private static void WatchTraining() {
        if(watchingTraining)return;
        stopping=true;
        trainingLifetime=new(EnterTraining,ReleaseTraining);
        watchingTraining=true;
        trainingLoaded=(UnityEngine.Events.UnityAction<Scene,LoadSceneMode>)TrainingLoaded;
        trainingChanged=(UnityEngine.Events.UnityAction<Scene,Scene>)TrainingChanged;
        sceneCallback=(UnityEngine.Events.UnityAction<Scene>)SceneUnloaded;
        SceneManager.sceneLoaded+=trainingLoaded;
        SceneManager.activeSceneChanged+=trainingChanged;
        SceneManager.sceneUnloaded+=sceneCallback;
        UpdateTrainingLifetime();
    }
    private static void TrainingLoaded(Scene scene,LoadSceneMode mode) {
        TrainingInitialLod.Cancel("scene loaded");UpdateTrainingLifetime();
    }
    private static void TrainingChanged(Scene previous,Scene current) {
        TrainingInitialLod.Cancel("active scene changed");UpdateTrainingLifetime();
    }
    private static void UpdateTrainingLifetime() {
        TrainingInitialLod.Tick();
        if(!watchingTraining || Time.unscaledTime<nextLifetimeRetry)return;
        try {
            var scene=SceneManager.GetActiveScene();
            // The Enabled switch promises a live return to stock layouts.
            // Do that while the current range is still valid; departure/quit
            // deliberately skips native UI/network calls during destruction.
            if(trainingLifetime!.Active && !Plugin.Enabled.Value && scene.isLoaded && scene.handle==trainingLifetime.SceneHandle && TrainingLifetime.IsTraining(scene.name))
                try{RestoreAll();}finally{trainingLifetime.Observe(scene.handle,true,scene.name,false);}
            trainingLifetime!.Observe(scene.handle,scene.IsValid() && scene.isLoaded,scene.name,Plugin.Enabled.Value);
        }catch(Exception error){TrainingLifetimeFailed(error);}
    }
    private static void TrainingLifetimeFailed(Exception error) {
        nextLifetimeRetry=Time.unscaledTime+1;
        stopping=true;
        RecordFailure("training-lifecycle",error,true);
        MenuFailed(error.GetBaseException().Message);
        Status="Training initialization or cleanup failed. See UserData/OperatorKillhouse/last-error.json.";
    }
    // These callbacks themselves exist only during training. Both native
    // methods receive the DESTINATION before the game's delayed/async load.
    // Removing our own detour here leaves the current native call able to
    // finish and keeps all destination-scene Awake callbacks unmodified.
    private static void BeforeTrainingDeparture(string __0) {
        try{trainingLifetime?.Depart();}
        catch(Exception error){TrainingLifetimeFailed(error);}
    }
    private static void InstallTrainingDepartureHooks() {
        foreach(string method in new[]{"Server_BeforeLoadScene","BeforeUnloadScene"})
            patches!.Patch(AccessTools.Method(typeof(GameManager),method,new[]{typeof(string)}),
                prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeTrainingDeparture)){priority=Priority.First});
    }
    private static void SceneUnloaded(Scene scene) {
        try{trainingLifetime?.Unloaded(scene.handle);}
        catch(Exception error){TrainingLifetimeFailed(error);}
        if(watchingTraining && trainingLifetime?.NeedsCleanup!=true)TrainingInitialLod.SceneUnloaded(scene);
        // A scene change outside training has no range state to traverse.
        if(stopping)return;
        CircuitFixtures.GeometryChanged();OutdoorGearLocker.SceneUnloaded(scene.handle);
        bool range=rangeScenes.Remove(scene.handle);menuNotice.Reset();Close();Prune();focused=null;
        if(range){MapNightMode.Release();PropPhysics.Clear();RangeSetupFailure="";stockInitialization.Clear();pendingSelections.Clear();pendingResets.Clear();CoopNetwork.SceneReset();NativeFurnitureCatalog.Clear();}
    }
    internal static void Stop() {
        watchingTraining=false;
        TrainingInitialLod.Cancel("plugin stopped");
        try{trainingLifetime?.Stop();}
        catch(Exception error){Plugin.Logger.LogWarning("Killhouse training cleanup: "+error);}
        // Startup may have failed before the lifetime acquired its resources.
        if(patches!=null)ReleaseOnStop("remaining patches",()=>{TrainingPatches.Remove(patches);patches=null;});
        try{if(sceneCallback!=null)ReleaseOnStop("scene callback",()=>SceneManager.sceneUnloaded-=sceneCallback);}
        finally{sceneCallback=null;}
        try{if(trainingLoaded!=null)ReleaseOnStop("load callback",()=>SceneManager.sceneLoaded-=trainingLoaded);}
        finally{trainingLoaded=null;}
        try{if(trainingChanged!=null)ReleaseOnStop("active scene callback",()=>SceneManager.activeSceneChanged-=trainingChanged);}
        finally{trainingChanged=null;}
        // Preserve pending non-Harmony cleanup too (for example an owned
        // Mirror delegate). OnDestroy can retry after OnApplicationQuit, even
        // when native patch removal already succeeded on the first attempt.
        if(trainingLifetime?.NeedsCleanup!=true)trainingLifetime=null;
        stopping=true;
    }
}
