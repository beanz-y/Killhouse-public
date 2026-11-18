using OperatorKillhouse.Core;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace OperatorKillhouse;

internal sealed class EncounterSpectators
{
    private readonly Transform anchor;
    private readonly float floor;
    private readonly int house;
    private readonly HashSet<IntPtr> shell;
    private readonly Dictionary<IntPtr,(float Time,Vector3 Feet,bool Excluded)> cache=new();
    private readonly Il2CppStructArray<RaycastHit> supportHits=new(32);
    internal EncounterSpectators(Transform anchor,float floor,int house) {
        this.anchor=anchor;this.floor=floor;this.house=house;
        var root=anchor.parent?anchor.parent.Find("Killhouse Mesh"):null;
        shell=root?root!.GetComponentsInChildren<Collider>(true).Where(c=>c && !c.isTrigger).Select(c=>c.Pointer).ToHashSet():new();
        if(shell.Count==0)throw new InvalidOperationException("Permanent house shell is unavailable for spectator filtering.");
    }
    internal bool Excluded(Transform? target) {
        if(!target || !anchor)return false;
        var health=target!.root.GetComponent<Health>();
        if(!health || health.isBot)return false;
        // Remote replicas can disable their movement capsule. Native sight
        // targets use the moving health root as the feet origin in that case.
        var feet=health.transform.position;var controller=health.masterController;
        if(controller && controller.characterCollider && controller.characterCollider.enabled) {
            var box=controller.characterCollider.bounds;
            if(box.size.sqrMagnitude>.01f)feet=new Vector3(box.center.x,box.min.y,box.center.z);
        }
        var localFeet=anchor.InverseTransformPoint(feet);
        if(localFeet.y-floor<SpectatorSupport.MinimumHeight-.15f)return false;
        if(cache.TryGetValue(health.Pointer,out var prior) && Time.time>=prior.Time && Time.time-prior.Time<.1f && (feet-prior.Feet).sqrMagnitude<.0025f)return prior.Excluded;
        bool hidden=false;var actor=target.root;var up=anchor.up;
        // Closest physical support wins: a prop above/beside a walkway keeps the
        // player targetable, even when it reaches the walkway's elevation.
        int count=Physics.RaycastNonAlloc(feet+up*.2f,-up,supportHits,1.2f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        // A saturated buffer may omit closer prop support. Keep the target
        // visible until an unambiguous query instead of hiding a real player.
        if(count<supportHits.Length) {
            float nearest=float.PositiveInfinity;int selected=-1;
            for(int n=0;n<count;n++) {
                var hit=supportHits[n];var collider=hit.collider;
                if(hit.distance>=nearest || !collider || collider.transform.IsChildOf(actor))continue;
                nearest=hit.distance;selected=n;
            }
            if(selected>=0) {
                var hit=supportHits[selected];var point=anchor.InverseTransformPoint(hit.point);
                hidden=SpectatorSupport.Excluded(shell.Contains(hit.collider.Pointer),point.y-floor,Vector3.Dot(hit.normal,up),localFeet.y-point.y);
            }
        }
        cache[health.Pointer]=(Time.time,feet,hidden);
        if(hidden && !prior.Excluded)CoopLog.Event("ai-spectator-hidden",new{house,player=health.netId,feet=feet.ToString()});
        return hidden;
    }
    internal void Forget(BrainAI brain) {
        bool seen=Excluded(brain.CurrentSeenTarget),previous=Excluded(brain.PreviousTarget);
        if(seen){brain.CurrentSeenTarget=null;brain.cachedLastTargetHealth=null;}
        if(previous){brain.PreviousTarget=null;brain.PreviousTargetActual=null;brain._hasVisuallySeenPreviousTarget=false;}
        if((seen || previous) && !brain.CurrentSeenTarget && !brain.PreviousTarget) {
            if(brain.weapons)brain.weapons.fireWeapon=false;brain.CurrentState=BrainAI.States.Idle;
        }
        ForgetCandidates(brain.availableTargets);ForgetCandidates(brain.possibleTargets);
    }
    private void ForgetCandidates(Il2CppSystem.Collections.Generic.List<Transform>? targets) {
        if(targets!=null)for(int n=targets.Count-1;n>=0;n--)if(Excluded(targets[n]))targets.RemoveAt(n);
    }
}
