using OperatorKillhouse.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperatorKillhouse;

// A one-shot engine event registration, admitted only after the old scenes
// have unloaded. It is never a game-method/LOD detour or a per-frame scene scan.
internal static class TrainingInitialLod
{
    private const int TrainingBuildIndex=18;
    private const string TrainingPath="Assets/Scenes/Outdoors area.unity";
    private const float MaximumWait=30;
    private static IDisposable? registration;
    private static int? expectedHandle;
    private static long generation;
    private static float armedAt,nextCancelRetry;
    private static bool cancelWarning,cleanupPending;
    internal static bool IsArmed=>expectedHandle.HasValue;

    internal static void SceneUnloaded(Scene outgoing) {
        if(!Cancel("scene unloaded") || !Plugin.Enabled.Value)return;
        try {
            // sceneCount includes loading and unloading scenes. loadedSceneCount
            // excludes both; an old training scene is also !isLoaded until its
            // unload callback returns, so always exclude the outgoing handle.
            if(SceneManager.loadedSceneCount!=0)return;
            int count=SceneManager.sceneCount;
            if(count<1 || count>256)return;
            Scene? candidate=null;
            for(int i=0;i<count;i++) {
                var scene=SceneManager.GetSceneAt(i);
                if(scene.handle==outgoing.handle || !Matches(scene) || scene.isLoaded)continue;
                if(candidate.HasValue)return;
                candidate=scene;
            }
            if(!candidate.HasValue)return;
            int handle=candidate.Value.handle;
            long token=++generation;
            expectedHandle=handle;armedAt=Time.unscaledTime;cancelWarning=false;
            // Subscribe verifies the exact installed engine before accessing
            // its event ABI. Runtime.Start already verifies the game assembly.
            cleanupPending=true;
            registration=NativeInitialLod.Subscribe((scene,queue,mode)=>BeforeAwake(token,handle,scene,queue,mode));
            Plugin.Logger.LogInfo($"Initial training LOD: armed for scene {handle}.");
        }catch(Exception error) {
            Cancel("registration failed");
            Warn("could not arm",error);
        }
    }

    private static void BeforeAwake(long token,int expected,int handle,IntPtr queue,int mode) {
        // NativeInitialLod removes its own registration before invoking us.
        // A delayed callback from a cancelled generation cannot touch a newer one.
        if(token!=generation || expectedHandle!=expected)return;
        if(!Cancel("before awake"))return;
        try {
            if(!Plugin.Enabled.Value || handle!=expected || mode!=0 || queue==IntPtr.Zero || SceneManager.loadedSceneCount!=0) {
                Plugin.Logger.LogInfo("Initial training LOD: skipped an unmatched or cancelled load.");return;
            }
            bool found=false;
            int count=SceneManager.sceneCount;
            if(count<1 || count>256)return;
            for(int i=0;i<count;i++) {
                var scene=SceneManager.GetSceneAt(i);
                if(scene.handle==handle){found=Matches(scene) && !scene.isLoaded;break;}
            }
            if(!found){Plugin.Logger.LogInfo("Initial training LOD: skipped an unavailable training scene.");return;}
            int removed=NativeInitialLod.Repair(queue);
            Plugin.Logger.LogInfo($"Initial training LOD: repaired {removed} redundant parent renderer references before awake.");
        }catch(Exception error){Warn("repair skipped",error);}
    }

    private static bool Matches(Scene scene)=>scene.IsValid() && scene.buildIndex==TrainingBuildIndex &&
        TrainingLifetime.IsTraining(scene.name) && string.Equals(scene.path,TrainingPath,StringComparison.Ordinal);

    internal static void Tick() {
        if(registration==null && !cleanupPending)return;
        if(!expectedHandle.HasValue) {
            if(Time.unscaledTime>=nextCancelRetry)Cancel("cleanup retry");
        }else if(!Plugin.Enabled.Value)Cancel("disabled");
        else if(Time.unscaledTime-armedAt>=MaximumWait)Cancel("load timed out");
    }

    internal static bool Cancel(string reason) {
        expectedHandle=null;++generation;
        if(registration==null && !cleanupPending)return true;
        try {
            registration?.Dispose();
            // Subscribe may have acquired native ownership before throwing.
            // Its static fallback root must also be released when no lease was returned.
            NativeInitialLod.Cancel();
            registration=null;cleanupPending=false;cancelWarning=false;
            Plugin.Logger.LogInfo($"Initial training LOD: released ({reason}).");return true;
        }catch(Exception error) {
            // Keep the lease alive for Stop or Tick to retry. A failed removal
            // must never lose its rooted native delegate or admit another lease.
            nextCancelRetry=Time.unscaledTime+1;
            if(!cancelWarning){cancelWarning=true;Warn("cleanup will retry",error);}
            return false;
        }
    }

    private static void Warn(string action,Exception error)=>Plugin.Logger.LogWarning("Initial training LOD "+action+": "+error.GetBaseException().Message);
}
