using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

internal sealed partial class RangeSession
{
    private TriggerRun? triggerRun;
    private Layout? triggerDocument;
    private int triggerRoot;
    private float triggerAt;
    private readonly HashSet<int> waitingAI=new(),releasedAI=new(),requestedSpawns=new();
    private readonly Queue<int> triggerSpawns=new();
    private readonly System.Random triggerRandom=new();
    internal bool TriggersArmed=>triggerRun!=null;
    internal string TriggerStatus {get;private set;}="Triggers disarmed.";
    internal void ArmTriggers() {
        if(!NetworkServer.active || disposed || CoopNetwork.Switching)throw new InvalidOperationException("Only the host can arm a ready map.");
        var doc=ActiveDocument;var root=ActivePropRoot;
        if(doc==null || !root || !root!.activeInHierarchy || doc.Triggers.Count==0)throw new InvalidOperationException("Load a custom map containing triggers first.");
        var error=ScenarioTriggers.Check(doc).FirstOrDefault(i=>i.Fatal || i.Message.Contains("no actions") || i.Message.Contains("assign a route"));if(error!=null)throw new InvalidOperationException(error.Message);
        if(ScenarioTriggers.NeedsAI(doc) && (!EncounterActive || encounterPending))throw new InvalidOperationException("Start AI first to prepare this map's AI triggers. Start AI arms them when ready.");
        if(powerRoot!=root.GetInstanceID())ResetPower();
        triggerDocument=LayoutDraft.Copy(doc);triggerRoot=root.GetInstanceID();triggerRun=new();triggerAt=0;
        TriggerStatus=$"{doc.Triggers.Count} triggers armed; once-per-run state cleared.";
        CoopLog.Event("triggers-armed",new{House,count=doc.Triggers.Count,ai=EncounterActive});
    }
    internal void DisarmTriggers() {
        triggerRun=null;triggerDocument=null;triggerRoot=0;requestedSpawns.Clear();triggerSpawns.Clear();TriggerStatus="Triggers disarmed.";
    }
    internal void BreakerSwitched(int id,bool on) {
        if(!NetworkServer.active || triggerRun==null || triggerDocument==null || disposed || CoopNetwork.Switching)return;
        var root=ActivePropRoot;if(!root || !root!.activeInHierarchy || root.GetInstanceID()!=triggerRoot){DisarmTriggers();return;}
        var source=triggerDocument.Breakers.FirstOrDefault(b=>b.PropId==id);if(source==null || ((power.Mask&(1<<source.Circuit))!=0)!=on)return;
        try {
            foreach(var trigger in triggerRun.BreakerChanged(triggerDocument.Triggers,id,on,Time.unscaledTime)) {
                foreach(var action in trigger.Actions)RunTriggerAction(action);
                TriggerStatus=$"Fired: {trigger.Name} · {(trigger.Once?"once this run":"repeat enabled")}";
                CoopLog.Event("trigger-fired",new{House,trigger.Id,trigger.Name,breaker=id,on,actions=trigger.Actions.Count});
            }
        }catch(Exception error){if(EncounterActive)ClearEncounter();DisarmTriggers();TriggerStatus="Triggers disarmed after an error: "+error.GetBaseException().Message;Runtime.Status=TriggerStatus;Plugin.Logger.LogError(error);}
    }
    internal void TickTriggers() {
        if(!NetworkServer.active || triggerRun==null || triggerDocument==null)return;
        var root=ActivePropRoot;
        if(disposed || !root || !root!.activeInHierarchy || root.GetInstanceID()!=triggerRoot || CoopNetwork.Switching){DisarmTriggers();return;}
        if(Time.unscaledTime<triggerAt)return;triggerAt=Time.unscaledTime+.05f;
        try {
            var actors=new List<TriggerActor>();var seen=new HashSet<long>();
            foreach(var pair in NetworkServer.connections) {
                var connection=pair.Value;if(connection==null || !connection.isReady || !connection.isAuthenticated || !connection.identity)continue;
                var master=connection.identity.GetComponent<PlayerMaster>();var player=master?master!.PlayerSpawnedObject:null;
                if(!player || !player!.health || player.health.isDead || !seen.Add(player.netId))continue;
                actors.Add(TriggerSample(player.netId,false,0,player.health.transform.position));
            }
            var local=PlayerMaster.MyPlayerMaster?.PlayerSpawnedObject;
            if(local && local!.health && !local.health.isDead && seen.Add(local.netId))actors.Add(TriggerSample(local.netId,false,0,local.health.transform.position));
            foreach(var brain in encounterBrains.Values)if(brain && brain.health && encounterDefinitions.TryGetValue(brain.Pointer,out var definition)) {
                var moving=brain.agent && brain.agent.Agent?brain.agent.Agent.transform.position:brain.health.transform.position;
                actors.Add(TriggerSample(-brain.Pointer.ToInt64(),true,definition.Id,moving,brain.health.isDead));
            }
            foreach(var activation in triggerRun.TickActivations(triggerDocument.Triggers,actors,Time.unscaledTime)) {
                var trigger=activation.Trigger;
                foreach(var action in trigger.Actions)if(activation.FireSharedActions || action.Kind==TriggerActionKind.DespawnEnteringAI)RunTriggerAction(action,activation.Actors);
                TriggerStatus=$"Fired: {trigger.Name} · "+(ScenarioTriggers.HasEnteringDespawn(trigger)?"individual AI entry":trigger.Once?"once this run":"repeat enabled");
                CoopLog.Event("trigger-fired",new{House,trigger.Id,trigger.Name,actions=trigger.Actions.Count,sharedActions=activation.FireSharedActions,enteringAI=activation.Actors.Where(a=>a.AI).Select(a=>a.Placement).ToArray()});
            }
            if(EncounterActive && !encounterPending && triggerSpawns.Count>0) {
                int id=triggerSpawns.Dequeue();if(requestedSpawns.Remove(id) && encounterDocument?.Combatants.FirstOrDefault(b=>b.Id==id) is {} definition) {
                    RemoveTriggeredAI(id);SpawnCombatant(definition);
                    CoopLog.Event("trigger-ai-spawned",new{House,id});
                }
            }
        }catch(Exception error){if(EncounterActive)ClearEncounter();DisarmTriggers();TriggerStatus="Triggers disarmed after an error: "+error.GetBaseException().Message;Runtime.Status=TriggerStatus;Plugin.Logger.LogError(error);}
    }
    private TriggerActor TriggerSample(long key,bool ai,int placement,Vector3 world,bool dead=false) {
        var p=anchor.InverseTransformPoint(world);return new(key,ai,placement,p.x-ConstructionPlan.Left,ConstructionPlan.North-p.z,p.y-floor,dead);
    }
    private void RunTriggerAction(TriggerAction action,IReadOnlyList<TriggerActor>? activators=null) {
        if(action.Kind<=TriggerActionKind.ToggleLights) {
            foreach(int circuit in action.Targets) {
                bool on=action.Kind==TriggerActionKind.LightsOn || action.Kind==TriggerActionKind.ToggleLights && (power.Mask&(1<<circuit))==0;
                if(((power.Mask&(1<<circuit))!=0)==on)continue;
                foreach(var fixture in CircuitFixtures.For(ActivePropRoot!).Where(f=>f.Breaker && f.Circuit==circuit && f.Interacting))fixture.CancelInteraction();
                power.Set(circuit,on);powerSendAt=0;
            }
            CircuitFixtures.Apply(ActivePropRoot!,power.Mask,power.BrokenLights);return;
        }
        if(!EncounterActive || encounterPending)return;
        if(action.Kind==TriggerActionKind.DespawnEnteringAI) {
            if(!NetworkServer.active || activators==null)return;
            foreach(var actor in activators)if(actor.AI && !actor.Dead && encounterBrains.TryGetValue(new IntPtr(-actor.Key),out var brain) && brain && brain.health && !brain.health.isDead && encounterDefinitions.TryGetValue(brain.Pointer,out var definition) && definition.Id==actor.Placement) {
                requestedSpawns.Remove(definition.Id);RemoveTriggeredAI(definition.Id);
            }
            return;
        }
        if(action.Kind==TriggerActionKind.SpawnRandom) {
            var eligible=action.Targets.Where(CanTriggerSpawn).ToArray();if(eligible.Length>0)QueueTriggerSpawn(eligible[triggerRandom.Next(eligible.Length)]);return;
        }
        foreach(int id in action.Targets) {
            if(action.Kind==TriggerActionKind.Spawn)QueueTriggerSpawn(id);
            else if(action.Kind==TriggerActionKind.Despawn){requestedSpawns.Remove(id);RemoveTriggeredAI(id);}
            else if(action.Kind==TriggerActionKind.StartRoute)ReleaseTriggeredRoute(id);
        }
    }
    private bool CanTriggerSpawn(int id)=>encounterDocument?.AIStarts.GetValueOrDefault(id)==AIStart.Deferred && !requestedSpawns.Contains(id) && !encounterBrains.Values.Any(b=>b && b.health && !b.health.isDead && encounterDefinitions.GetValueOrDefault(b.Pointer)?.Id==id);
    private void QueueTriggerSpawn(int id){if(CanTriggerSpawn(id)){requestedSpawns.Add(id);triggerSpawns.Enqueue(id);}}
    private void ReleaseTriggeredRoute(int id) {
        waitingAI.Remove(id);releasedAI.Add(id);
        foreach(var brain in encounterBrains.Values.Where(b=>b && encounterDefinitions.GetValueOrDefault(b.Pointer)?.Id==id).ToArray()) {
            var definition=encounterDefinitions[brain.Pointer];if(definition.RouteId==0 || !brain.health || brain.health.isDead)continue;
            definition=definition with{Order=definition.Order==CombatOrder.Cautious?CombatOrder.Defensive:definition.Order};encounterDefinitions[brain.Pointer]=definition;
            encounterCaution.Remove(brain);encounterOrders.Remove(brain);encounterProgress.Remove(brain.Pointer);ConfigureCombatant(brain,definition);
        }
    }
    private bool HoldTriggeredRoute(BrainAI brain) {
        if(!encounterDefinitions.TryGetValue(brain.Pointer,out var definition) || !waitingAI.Contains(definition.Id))return false;
        if(brain.health && !brain.health.isDead && brain.agent && brain.agent.Agent) {
            encounterOrders.Bind(brain,true,brain.DefensiveInitialPosition,definition.Id);brain.idleStates=BrainAI.IdleStates.Idle;
            if(!brain.agent.Agent.isTraversingOffMeshLink)brain.agent.Agent.isStopped=true;
        }
        return true;
    }
    private void RemoveTriggeredAI(int id) {
        foreach(var brain in encounterBrains.Values.Where(b=>b && encounterDefinitions.GetValueOrDefault(b.Pointer)?.Id==id).ToArray()) {
            var go=brain.gameObject;var pointer=brain.Pointer;
            encounterOrders.Remove(brain);encounterCaution.Remove(brain);encounterCivilians.Remove(brain);
            encounterDefinitions.Remove(pointer);encounterBrains.Remove(pointer);encounterProgress.Remove(pointer);encounterSearchAt.Remove(pointer);encounterRouteState.Remove(pointer);encounterBots.RemoveAll(b=>!b || b.Pointer==go.Pointer);
            if(brain.agent && brain.agent.Agent)brain.agent.Agent.enabled=false;
            var identity=go.GetComponent<NetworkIdentity>();if(identity && identity.netId!=0)NetworkServer.Destroy(go);else Object.Destroy(go);
        }
    }
}
