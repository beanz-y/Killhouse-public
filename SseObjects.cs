using HarmonyLib;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

// Native interaction UI owns the prompt and key handling. Only our registered
// components are intercepted; every other game's interactable passes through.
internal static class SseObjects
{
    internal sealed class Item
    {
        internal readonly int Id;
        internal readonly GameObject Node,Root;
        internal readonly BoxCollider Hit;
        internal readonly InteractableComponent Native;
        internal Transform? Support;
        private Vector3 supportPosition,supportScale;
        private Quaternion supportRotation;
        private bool reparented;
        internal void CaptureSupport() {Support=Node.transform.parent;supportPosition=Node.transform.localPosition;supportRotation=Node.transform.localRotation;supportScale=Node.transform.localScale;}
        internal void Follow(Transform? parent) {
            if(!parent || !Node)return;
            bool original=Support && parent!.Pointer==Support!.Pointer;
            if(original && reparented){Node.transform.SetParent(parent,false);Node.transform.localPosition=supportPosition;Node.transform.localRotation=supportRotation;Node.transform.localScale=supportScale;reparented=false;}
            else if(Node.transform.parent?.Pointer!=parent!.Pointer){Node.transform.SetParent(parent,true);reparented=!original;}
        }
        internal Item(GameObject node,GameObject root,SseObjective definition) {
            Id=definition.PropId;Node=node;Root=root;
            CaptureSupport();
            var shape=NativeProp.Measure(node,false);
            var hitNode=new GameObject("SSE native interaction"){layer=9};hitNode.transform.SetParent(node.transform,false);
            Hit=hitNode.AddComponent<BoxCollider>();
            // Local measurements use a yaw-free frame at the placed scale.
            Hit.center=new((shape.MinX+shape.MaxX)*.5f,(shape.MinY+shape.MaxY)*.5f,(shape.MinZ+shape.MaxZ)*.5f);
            // Keep the native prompt surface outside the solid model bounds so
            // a coincident scenery collider cannot win the interaction ray.
            Hit.size=new(Math.Max(.16f,shape.MaxX-shape.MinX+.025f),Math.Max(.12f,shape.MaxY-shape.MinY+.025f),Math.Max(.16f,shape.MaxZ-shape.MinZ+.025f));
            Native=hitNode.AddComponent<InteractableComponent>();Native.InteractableName=definition.Label;Native.InteractableContext="Collect SSE";
        }
    }
    private static readonly Dictionary<IntPtr,Item> owners=new();
    private static readonly Dictionary<int,Dictionary<int,Item>> roots=new();
    internal static void Install(Harmony harmony)=>harmony.Patch(AccessTools.Method(typeof(InteractableComponent),nameof(InteractableComponent.AccessInteractable)),prefix:new HarmonyMethod(typeof(SseObjects),nameof(Access)));
    private static bool Access(InteractableComponent __instance,PlayerNetworking __0) {
        if(!owners.TryGetValue(__instance.Pointer,out var item))return true;
        if(__0 && __0.isOwned)Runtime.UseNativeSse(item,__0);
        return false;
    }
    internal static void Add(GameObject node,GameObject root,Layout doc,int id) {
        var definition=doc.Sse.FirstOrDefault(s=>s.PropId==id);if(definition==null)return;
        var item=new Item(node,root,definition);int key=root.GetInstanceID();
        if(!roots.TryGetValue(key,out var group))roots[key]=group=new();group.Add(id,item);owners.Add(item.Native.Pointer,item);
    }
    internal static Item? Find(GameObject root,int id)=>roots.TryGetValue(root.GetInstanceID(),out var items)?items.GetValueOrDefault(id):null;
    internal static void AttachSupports(Layout doc,IReadOnlyDictionary<int,GameObject> nodes) {
        foreach(var prop in doc.Props.Where(p=>SseObjectives.IsAsset(p.Name) && p.SupportId>0)) {
            if(!nodes.TryGetValue(prop.Id,out var item) || !nodes.TryGetValue(prop.SupportId,out var support))throw new InvalidOperationException("SSE support did not enter the prepared map.");
            item.transform.SetParent(support.transform,true);
            foreach(var owned in owners.Values)if(owned.Node.Pointer==item.Pointer){owned.CaptureSupport();break;}
        }
    }
    internal static void Apply(GameObject root,IReadOnlySet<int> collected) {
        if(!roots.TryGetValue(root.GetInstanceID(),out var group))return;
        // Hiding a lower collectible must not hide uncollected SSE above it.
        // Follow the nearest remaining support, retaining world pose, then
        // restore the authored local pose when that support returns on reset.
        var transforms=group.Values.Where(i=>i.Node).ToDictionary(i=>i.Node.transform.Pointer);
        foreach(var item in group.Values)if(item.Node) {
            var parent=item.Support;
            for(int n=0;n<group.Count && parent && transforms.TryGetValue(parent!.Pointer,out var lower) && collected.Contains(lower.Id);n++)parent=lower.Support;
            item.Follow(parent);
        }
        foreach(var p in group)if(p.Value.Node)p.Value.Node.SetActive(!collected.Contains(p.Key));
    }
    internal static void Pending(GameObject root){if(roots.TryGetValue(root.GetInstanceID(),out var group))foreach(var item in group.Values)if(item.Node)item.Node.SetActive(false);}
    internal static void Release(GameObject root) {
        if(roots.Remove(root.GetInstanceID(),out var group)){foreach(var item in group.Values)owners.Remove(item.Native.Pointer);return;}
        // Cache eviction/cancel releases Geometry while retaining its layout
        // slot. Drop those interaction owners before a rebuilt map reuses IDs.
        foreach(var pair in roots.ToArray()) {
            foreach(var item in pair.Value.Values.Where(i=>!i.Node || i.Node.transform.IsChildOf(root.transform)).ToArray()){pair.Value.Remove(item.Id);owners.Remove(item.Native.Pointer);}
            if(pair.Value.Count==0)roots.Remove(pair.Key);
        }
    }
    internal static void Clear(){owners.Clear();roots.Clear();}
}
