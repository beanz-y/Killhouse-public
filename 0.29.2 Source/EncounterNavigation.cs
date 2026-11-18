using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

// Outdoors has no scene AstarPath. Both houses share one native runtime;
// each encounter still owns and removes only its own bounded house graph.
internal static class EncounterNavigation
{
    private static GameObject? ownedRoot;
    internal static AstarPath Ensure(Scene scene) {
        var active=AstarPath.active;
        if(active is not null && active) {
            if(!active.isActiveAndEnabled || active.data==null)throw new InvalidOperationException("The existing navigation runtime is not initialized.");
            return active;
        }
        if(!scene.IsValid() || !scene.isLoaded)throw new InvalidOperationException("The range scene is no longer loaded.");
        // Discard a disabled/failed owned runtime before retrying. Never take
        // ownership of, enable or destroy an unrelated scene component.
        Dispose();
        var root=new GameObject("Operator Killhouse navigation");root.SetActive(false);
        try {
            SceneManager.MoveGameObjectToScene(root,scene);
            var path=root.AddComponent<AstarPath>();
            // The native constructor creates AstarData and all processors.
            // OnEnable binds the singleton, loads graph types and starts the
            // processors. Suppress its automatic scan before activation: the
            // house graph is added and scanned explicitly on the next frame.
            path.scanOnStartup=false;
            if(path.data==null)throw new InvalidOperationException("Native navigation construction did not create its graph data.");
            ownedRoot=root;root.SetActive(true);
            var initialized=AstarPath.active;
            if(initialized is null || !initialized || initialized.Pointer!=path.Pointer || !path.isActiveAndEnabled || path.data==null)throw new InvalidOperationException("Native navigation did not initialize when activated. See the engine log.");
            CoopLog.Event("ai-navigation-runtime",new{created=true,scene=scene.name});
            return path;
        } catch {
            // The library's normal OnDisable/OnDestroy disposes its processors.
            // Do not manually assign AstarPath.active or bypass that lifecycle.
            if(ownedRoot && ownedRoot!.Pointer==root.Pointer)Dispose();else Object.Destroy(root);
            throw;
        }
    }
    internal static void Dispose() {
        var root=ownedRoot;ownedRoot=null;
        if(root){root!.SetActive(false);Object.Destroy(root);}
    }
}
