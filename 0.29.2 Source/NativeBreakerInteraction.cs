using HarmonyLib;
using UnityEngine;

namespace OperatorKillhouse;

// Same native interaction surface/AccessInteractable hook as ATAK's room TV.
internal static class NativeBreakerInteraction
{
    internal sealed class Control : IDisposable
    {
        internal readonly GameObject Node;
        internal readonly InteractableComponent Native;
        internal Control(CircuitFixtures.Fixture fixture,string circuit) {
            var model=fixture.Node.transform.Find("PowerBox_V2");var source=model.GetComponent<BoxCollider>();
            Node=new GameObject("Killhouse native breaker interaction"){layer=9};Node.transform.SetParent(model,false);
            try {
                var hit=Node.AddComponent<BoxCollider>();
                hit.center=source.center+Vector3.back*(source.size.z*.5f+.012f);hit.size=new(source.size.x,source.size.y,.02f);
                Native=Node.AddComponent<InteractableComponent>();Native.InteractableName="Breaker box · "+circuit;
                owners.Add(Native.Pointer,fixture);
            }catch{UnityEngine.Object.Destroy(Node);throw;}
        }
        internal void Update(bool on)=>Native.InteractableContext=on?"Cut power":"Restore power";
        public void Dispose(){if(Native!=null)owners.Remove(Native.Pointer);if(Node)UnityEngine.Object.Destroy(Node);}
    }
    private static readonly Dictionary<IntPtr,CircuitFixtures.Fixture> owners=new();
    internal static void Install(Harmony harmony)=>harmony.Patch(AccessTools.Method(typeof(InteractableComponent),nameof(InteractableComponent.AccessInteractable)),prefix:new HarmonyMethod(typeof(NativeBreakerInteraction),nameof(Access)));
    private static bool Access(InteractableComponent __instance,PlayerNetworking __0) {
        if(!owners.TryGetValue(__instance.Pointer,out var fixture))return true;
        if(__0 && __0.isOwned)Runtime.UseNativeBreaker(fixture,__0);
        return false;
    }
}
