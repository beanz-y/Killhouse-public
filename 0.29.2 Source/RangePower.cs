using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class RangeSession
{
    private int powerRoot;
    private readonly CircuitPowerState power=new();
    private float powerSendAt;
    private readonly Dictionary<int,float> breakerUsedAt=new();
    private readonly HashSet<int> pendingCircuits=new();
    private Layout? PowerLayout=>Computer && Computer.activeLayoutIndex>=StockCount && Computer.activeLayoutIndex-StockCount<items.Count?items[Computer.activeLayoutIndex-StockCount].Layout:null;
    internal bool OwnsFixture(CircuitFixtures.Fixture fixture)=>ActivePropRoot && ActivePropRoot!.Pointer==fixture.Root.Pointer;
    internal void ResetPower() {
        DisarmTriggers();
        var root=ActivePropRoot;var doc=PowerLayout;if(!root || doc==null)return;
        long began=PerformanceJournal.Begin(),allocated=PerformanceJournal.Allocated();
        try {
        power.Reset(doc,NetworkServer.active);if(NetworkServer.active)powerSendAt=0;
        powerRoot=root!.GetInstanceID();breakerUsedAt.Clear();CircuitFixtures.Reset(root,power.Mask,power.BrokenLights);pendingCircuits.Clear();
        }finally{PerformanceJournal.Complete("power-reset",began,allocated);}
    }
    internal void TickPower() {
        var root=ActivePropRoot;var doc=PowerLayout;
        if(!root || !root!.activeInHierarchy || doc==null){powerRoot=0;return;}
        if(powerRoot!=root.GetInstanceID())ResetPower();
        CircuitFixtures.TickBreakers(root);
        CircuitFixtures.Apply(root,power.Mask,power.BrokenLights);
        if(!NetworkServer.active || Time.unscaledTime<powerSendAt || !PowerCircuits.HasData(doc))return;
        powerSendAt=Time.unscaledTime+1;
        CoopNetwork.CircuitState(this,power.Mask,power.Epoch,power.BrokenLights.OrderBy(id=>id).ToArray());
        foreach(var fixture in CircuitFixtures.For(root))if(fixture.Pending is {} cue && BreakerTiming.Valid(cue))CoopNetwork.AnimateBreaker(this,cue,power.Epoch);
    }
    internal bool UseBreaker(int id,PlayerNetworking player,long epoch=0) {
        var root=ActivePropRoot;var doc=PowerLayout;
        if(disposed || !root || !root!.activeInHierarchy || doc==null || !player || !player.health || player.health.isDead || CoopNetwork.Switching)return false;
        var breaker=doc.Breakers.FirstOrDefault(b=>b.PropId==id);if(breaker==null)return false;
        var fixture=CircuitFixtures.For(root!).FirstOrDefault(f=>f.Id==id && f.Breaker);if(fixture==null || !CanReachBreaker(player,fixture))return false;
        if(!NetworkServer.active){if(power.Epoch<=0)return false;CoopNetwork.ToggleBreaker(this,id,power.Epoch);return true;}
        if(epoch!=0 && epoch!=power.Epoch || Time.unscaledTime<breakerUsedAt.GetValueOrDefault(id) || pendingCircuits.Contains(breaker.Circuit))return false;
        breakerUsedAt[id]=Time.unscaledTime+BreakerTiming.Cooldown;
        long acceptedEpoch=power.Epoch;uint actor=player.netId;bool restore=(power.Mask&(1<<breaker.Circuit))==0;
        pendingCircuits.Add(breaker.Circuit);
        if(fixture.Use(player,()=>{
                pendingCircuits.Remove(breaker.Circuit);
                // Cached roots, resets and selection changes cannot commit old gestures.
                if(disposed || !NetworkServer.active || !root || !root!.activeInHierarchy || !OwnsFixture(fixture) || CoopNetwork.Switching || power.Epoch!=acceptedEpoch) {
                    fixture.CancelInteraction();CoopNetwork.AnimateBreaker(this,new(id,actor,Cancelled:true,Restore:restore),acceptedEpoch);return;
                }
                CommitBreaker(id,breaker.Circuit,acceptedEpoch,actor);
            },()=>{
                pendingCircuits.Remove(breaker.Circuit);
                CoopNetwork.AnimateBreaker(this,new(id,actor,Cancelled:true,Restore:restore),acceptedEpoch);
        },restore:restore)) {
            CoopNetwork.AnimateBreaker(this,new(id,actor,Restore:restore),acceptedEpoch);return true;
        }
        pendingCircuits.Remove(breaker.Circuit);
        // Keep circuit access available if native animation could not initialize.
        bool changed=CommitBreaker(id,breaker.Circuit,acceptedEpoch,actor);
        if(changed)CircuitFixtures.Apply(root!,power.Mask,power.BrokenLights);return changed;
    }
    private bool CommitBreaker(int id,int circuit,long epoch,uint actor) {
        if(!NetworkServer.active || !power.Toggle(circuit,epoch))return false;powerSendAt=0;
        CoopLog.Event("breaker-used",new{House,id,Circuit=circuit,power.Mask,power.Epoch,player=actor});
        BreakerSwitched(id,(power.Mask&(1<<circuit))!=0);
        return true;
    }
    private static bool CanReachBreaker(PlayerNetworking player,CircuitFixtures.Fixture fixture) {
        var eye=player.transform.position+Vector3.up*1.55f;
        var collider=fixture.Node.GetComponentInChildren<Collider>();if(!collider)return false;
        var target=collider!.bounds.center;var delta=target-eye;
        if(delta.sqrMagnitude>2.75f*2.75f)return false;
        foreach(var hit in Physics.RaycastAll(eye,delta.normalized,delta.magnitude+.02f,-1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)) {
            if(!hit.collider || hit.collider.transform.IsChildOf(player.transform))continue;
            return hit.collider.transform.IsChildOf(fixture.Node.transform);
        }
        return false;
    }
    internal CircuitFixtures.Fixture? AimedBreaker(Ray ray) {
        var root=ActivePropRoot;if(!root || !root!.activeInHierarchy)return null;
        foreach(var hit in Physics.RaycastAll(ray,2.75f,-1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)) {
            if(!hit.collider)continue;
            var local=PlayerMaster.MyPlayerMaster?.PlayerSpawnedObject;
            if(local && hit.collider.transform.IsChildOf(local!.transform))continue;
            return CircuitFixtures.For(root).FirstOrDefault(f=>f.Breaker && hit.collider.transform.IsChildOf(f.Node.transform));
        }
        return null;
    }
    internal string BreakerLabel(CircuitFixtures.Fixture fixture)=>PowerCircuits.Label(PowerLayout!,fixture.Circuit)+" · "+((power.Mask&(1<<fixture.Circuit))!=0?"ON":"OFF");
    internal void ReceiveBreaker(CoopPacket packet,PlayerNetworking player) {
        if(NetworkServer.active && PropContext(packet) && packet.Revision==power.Epoch && packet.Breaker is {} id)UseBreaker(id,player,packet.Revision);
    }
    internal void ReceivePower(CoopPacket packet) {
        if(NetworkServer.active || !PropContext(packet) || packet.Power is not {} mask)return;
        if(packet.BrokenLights!=null && packet.BrokenLights.Any(key=>PowerLayout is not {} doc || !LightDamage.Known(doc,key)))return;
        bool reset=packet.Revision>power.Epoch;
        if(power.Receive(packet.Revision,packet.Sequence,mask,packet.BrokenLights)) {
            if(reset)CircuitFixtures.Reset(ActivePropRoot!,mask,power.BrokenLights);
            else CircuitFixtures.Apply(ActivePropRoot!,mask,power.BrokenLights);
        }
    }
    internal void ReceiveBreakerAnimation(CoopPacket packet) {
        if(NetworkServer.active || !PropContext(packet) || packet.Revision!=power.Epoch || !BreakerTiming.Valid(packet.BreakerAnimation))return;
        var cue=packet.BreakerAnimation!;var fixture=CircuitFixtures.For(ActivePropRoot!).FirstOrDefault(f=>f.Id==cue.Id && f.Breaker);
        if(fixture==null)return;
        if(cue.Cancelled){fixture.CancelInteraction();fixture.Tick();return;}
        if(((power.Mask&(1<<fixture.Circuit))!=0)==cue.Restore)return;
        PlayerNetworking? actor=null;
        if(NetworkClient.spawned.ContainsKey(cue.Player))actor=NetworkClient.spawned[cue.Player]?.GetComponent<PlayerNetworking>();
        fixture.Use(actor,elapsed:cue.Elapsed,restore:cue.Restore);
    }
    internal bool BreakLight(CircuitFixtures.Fixture fixture,Vector3 point,int source) {
        var root=ActivePropRoot;
        if(!root || !root!.activeInHierarchy || fixture.Breaker || root.Pointer!=fixture.Root.Pointer || source<0 || source>=fixture.SourceCount || CoopNetwork.Switching || power.Epoch<=0 || PowerLayout is not {} doc || !LightDamage.Known(doc,LightDamage.Key(fixture.Id,source)))return false;
        if(!NetworkServer.active){CoopNetwork.ShootLight(this,new(fixture.Id,Pack(anchor.InverseTransformPoint(point)),source),power.Epoch);return true;}
        if(!power.BreakLight(LightDamage.Key(fixture.Id,source)))return false;
        powerSendAt=0;CircuitFixtures.Apply(root,power.Mask,power.BrokenLights);
        CoopLog.Event("light-shot",new{House,id=fixture.Id,source,power.Epoch});return true;
    }
    internal void ReceiveLightShot(CoopPacket packet,PlayerNetworking player) {
        if(!NetworkServer.active || !PropContext(packet) || packet.Revision!=power.Epoch || !LightDamage.Valid(packet.LightShot) || !player || !player.health || player.health.isDead)return;
        var shot=packet.LightShot!;var fixture=CircuitFixtures.For(ActivePropRoot!).FirstOrDefault(f=>f.Id==shot.Id && !f.Breaker);if(fixture==null || shot.Source>=fixture.SourceCount)return;
        var point=anchor.TransformPoint(new(shot.Point[0],shot.Point[1],shot.Point[2]));
        // Bounds cover both convex and static mesh fixtures. The following
        // solid ray still has to hit this exact fixture before any obstacle.
        if(!fixture.Node.GetComponentsInChildren<Collider>(true).Any(c=>c && c.enabled && !c.isTrigger && fixture.SourceOf(c.transform)==shot.Source && c.bounds.SqrDistance(point)<=.0225f))return;
        var eye=player.transform.position+Vector3.up*1.55f;var delta=point-eye;if(delta.sqrMagnitude>250*250 || delta.sqrMagnitude<.0001f)return;
        foreach(var hit in Physics.RaycastAll(eye,delta.normalized,delta.magnitude+.2f,-1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)) {
            if(!hit.collider || hit.collider.transform.IsChildOf(player.transform))continue;
            if(hit.collider.transform.IsChildOf(fixture.Node.transform) && fixture.SourceOf(hit.collider.transform)==shot.Source)BreakLight(fixture,point,shot.Source);
            return;
        }
    }
}

internal static partial class Runtime
{
    private static float breakerKeyAt;
    private static void TickPower() {
        CircuitFixtures.TickInactive();
        foreach(var session in Sessions.Values)session.TickPower();
        if(!Input.GetKeyDown(Plugin.BreakerKey.Value) || Time.unscaledTime<breakerKeyAt || GameplayInputBlocked || !Plugin.Enabled.Value || CoopNetwork.Switching)return;
        var player=PlayerMaster.MyPlayerMaster?.PlayerSpawnedObject;var camera=Camera.main;
        if(!player || !player!.health || player.health.isDead || !camera)return;
        var ray=new Ray(camera!.transform.position,camera.transform.forward);
        foreach(var session in Sessions.Values)if(session.AimedBreaker(ray) is {} fixture) {
            UseNativeBreaker(fixture,player);
            break;
        }
    }
    internal static void UseNativeBreaker(CircuitFixtures.Fixture fixture,PlayerNetworking player) {
        if(GameplayInputBlocked || !Plugin.Enabled.Value || CoopNetwork.Switching || !player || !player.isOwned || Time.unscaledTime<breakerKeyAt)return;
        foreach(var session in Sessions.Values)if(session.OwnsFixture(fixture) && session.UseBreaker(fixture.Id,player)){breakerKeyAt=Time.unscaledTime+BreakerTiming.Cooldown;return;}
    }
}
