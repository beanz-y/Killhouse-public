using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal static class DeferredRelease
{
    private static readonly Queue<GameObject> queue=new();
    internal static void Tree(GameObject? root) {
        if(root is null || !root)return;
        using var cleanup=new CleanupStack();
        // Always queue the parent, even if deactivation or a component release
        // fails. Destroying it also owns any children we could not enumerate.
        cleanup.Add(()=>queue.Enqueue(root));
        cleanup.Add(()=>Children(root));
        cleanup.Add(()=>SseObjects.Release(root));cleanup.Add(()=>CircuitFixtures.Release(root));
        cleanup.Add(()=>root.SetActive(false));
    }
    private static void Children(GameObject root) {
        using var cleanup=new CleanupStack();
        for(int n=0;n<root.transform.childCount;n++) {
            var child=root.transform.GetChild(n).gameObject;
            // Split containers into whole native assemblies. Traversing every
            // mesh/collider descendant here would reintroduce a cleanup spike.
            cleanup.Add(()=>{if(child.name is "Geometry" or "Targets" || child.name.StartsWith("Retired ",StringComparison.Ordinal))Tree(child);else queue.Enqueue(child);});
        }
    }
    internal static void Tick(FrameBudget budget) {
        List<Exception>? errors=null;
        while(queue.Count>0 && budget.Take())ReleaseNext(ref errors);
        if(errors!=null)throw new AggregateException(errors);
    }
    internal static void Drain() {
        List<Exception>? errors=null;
        while(queue.Count>0)ReleaseNext(ref errors);
        if(errors!=null)throw new AggregateException(errors);
    }
    private static void ReleaseNext(ref List<Exception>? errors) {
        var next=queue.Dequeue();
        try{if(next)UnityEngine.Object.Destroy(next);}catch(Exception error){(errors??=new()).Add(error);}
    }
}
