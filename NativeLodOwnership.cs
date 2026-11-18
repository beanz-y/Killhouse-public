using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace OperatorKillhouse;

// Called only for stock training architecture, before cloning or reactivation.
// The reinforced assembly repeats its cinder-block renderers in the parent LOD
// even though each block has its own complete high/low-detail LOD chain.
internal static class NativeLodOwnership
{
    private sealed record ChildLods(LODGroup Group,Il2CppReferenceArray<LOD> Levels,HashSet<IntPtr> Renderers);
    private sealed class PendingRepair
    {
        internal readonly GameObject Root;
        internal readonly LODGroup Parent;
        internal readonly Il2CppReferenceArray<LOD> Levels;
        internal readonly List<ChildLods> Children;
        internal readonly int Removed;
        internal bool ParentApplied;
        internal PendingRepair(GameObject root,LODGroup parent,Il2CppReferenceArray<LOD> levels,List<ChildLods> children,int removed) {
            Root=root;Parent=parent;Levels=levels;Children=children;Removed=removed;
        }
    }
    private static readonly Dictionary<IntPtr,PendingRepair> pending=new();
    internal static void Clear()=>pending.Clear();

    internal static int Repair(GameObject root) {
        if(!root || !(root.name.StartsWith("Reinforced Wall",StringComparison.Ordinal) || root.name.StartsWith("Reinforced Door Way",StringComparison.Ordinal)))return 0;
        if(pending.TryGetValue(root.Pointer,out var retry)) {
            if(retry.Root)return Apply(retry);
            pending.Remove(root.Pointer);
        }
        var parent=root.GetComponent<LODGroup>();
        if(!parent)return 0;
        var childRenderers=new HashSet<IntPtr>();var children=new List<ChildLods>();
        foreach(var child in root.GetComponentsInChildren<LODGroup>(true)) {
            if(!child || child.Pointer==parent.Pointer || child.transform==root.transform || !child.transform.IsChildOf(root.transform))continue;
            var levels=child.GetLODs();var owned=new HashSet<IntPtr>();
            foreach(var lod in levels) {
                var renderers=lod.renderers;
                if(renderers==null)continue;
                foreach(var renderer in renderers)
                    if(renderer && renderer.transform.IsChildOf(root.transform))owned.Add(renderer.Pointer);
            }
            childRenderers.UnionWith(owned);children.Add(new(child,levels,owned));
        }
        if(childRenderers.Count==0)return 0;
        var original=parent.GetLODs();
        var retained=new Renderer[]?[original.Length];
        int removed=0;var removedRenderers=new HashSet<IntPtr>();
        for(int level=0;level<original.Length;level++) {
            var renderers=original[level].renderers;
            if(renderers==null)continue;
            var keep=new List<Renderer>(renderers.Length);
            foreach(var renderer in renderers) {
                if(renderer && childRenderers.Contains(renderer.Pointer)){removed++;removedRenderers.Add(renderer.Pointer);}
                else keep.Add(renderer);
            }
            retained[level]=keep.ToArray();
        }
        if(removed==0)return 0;
        // IL2CPP LOD values are reference wrappers. Build fresh values and arrays
        // instead of editing GetLODs results in place; apply only after all reads
        // succeed. The wood faces legitimately occur in both parent LOD levels.
        var repaired=new Il2CppReferenceArray<LOD>(original.Length);
        for(int level=0;level<original.Length;level++) {
            Il2CppReferenceArray<Renderer>? renderers=null;
            if(retained[level] is {} keep) {
                renderers=new Il2CppReferenceArray<Renderer>(keep.Length);
                for(int index=0;index<keep.Length;index++)renderers[index]=keep[index];
            }
            repaired[level]=new LOD {
                screenRelativeTransitionHeight=original[level].screenRelativeTransitionHeight,
                fadeTransitionWidth=original[level].fadeTransitionWidth,
                renderers=renderers!
            };
        }
        // Preserve all group flags, local bounds and child LODs. Recalculating
        // bounds here would change the authored screen-size switching distances.
        var repair=new PendingRepair(root,parent,repaired,children.Where(c=>c.Renderers.Overlaps(removedRenderers)).ToList(),removed);
        // Retain the plan before native mutation: either a rejected parent update
        // or a partially completed child refresh can be retried on attachment.
        pending[root.Pointer]=repair;
        return Apply(repair);
    }
    private static int Apply(PendingRepair repair) {
        if(!repair.ParentApplied){repair.Parent.SetLODs(repair.Levels);repair.ParentApplied=true;}
        var failures=new List<Exception>();
        foreach(var child in repair.Children.ToArray()) {
            try {
                // Updating the parent can unregister its old renderer links.
                // Reassert the unchanged child chains afterwards so their native
                // ownership wins immediately, including already-active sources.
                if(child.Group && child.Group.transform.IsChildOf(repair.Root.transform))child.Group.SetLODs(child.Levels);
                repair.Children.Remove(child);
            }catch(Exception error){failures.Add(error);}
        }
        if(failures.Count>0)throw new AggregateException("Training LOD child registration refresh failed.",failures);
        pending.Remove(repair.Root.Pointer);
        return repair.Removed;
    }
}
