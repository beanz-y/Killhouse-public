using Mirror;
using OperatorKillhouse.Core;
using Pathfinding;
using UnityEngine;
using Object=UnityEngine.Object;
using TransformList=Il2CppSystem.Collections.Generic.List<UnityEngine.Transform>;

namespace OperatorKillhouse;

// Every live object belongs to one range session. Clients receive the native
// Mirror spawn; only the server creates brains, routes and navigation graphs.
internal sealed partial class RangeSession
{
    private readonly List<GameObject> encounterBots=new();
    private readonly List<EncounterDoorState> encounterDoorBaseline=new();
    private readonly Dictionary<IntPtr,Combatant> encounterDefinitions=new();
    private readonly Dictionary<IntPtr,RouteProgress> encounterProgress=new();
    private readonly Dictionary<IntPtr,BrainAI> encounterBrains=new();
    private readonly Dictionary<IntPtr,float> encounterSearchAt=new();
    private readonly Dictionary<IntPtr,RouteObservation> encounterRouteState=new();
    private readonly List<(NodeLink2 Link,GraphMask Mask)> encounterLinks=new();
    private readonly EncounterDoors encounterDoors=new();
    private readonly EncounterOrders encounterOrders=new();
    private readonly EncounterCaution encounterCaution=new();
    private readonly EncounterDoorAwareness encounterDoorAwareness=new();
    private readonly EncounterCivilians encounterCivilians=new();
    private EncounterSpectators? encounterSpectators;
    private readonly Dictionary<int,TransformList> encounterRoutes=new();
    private readonly Dictionary<int,CombatRoute> encounterRouteDefinitions=new();
    private GameObject? routeRoot,barrierRoot;
    private RecastGraph? encounterGraph;
    private AstarPath? encounterAstar;
    private Layout? encounterDocument;
    private int encounterIndex=-1,spawnIndex,spawnFrame;
    private bool encounterPending;
    private string encounterStage="idle";
    internal string EncounterStatus {get;private set;}="AI encounter stopped.";
    internal bool EncounterActive=>encounterIndex>=0;
    internal Layout? ActiveDocument=>Computer && Computer.activeLayoutIndex>=StockCount && Computer.activeLayoutIndex-StockCount<items.Count?items[Computer.activeLayoutIndex-StockCount].Layout:null;
    internal void StartEncounter() {
        if(!NetworkServer.active || disposed || !Computer)throw new InvalidOperationException("Only the host can start an AI encounter.");
        if(!Plugin.CombatAI.Value)throw new InvalidOperationException("Enable AI encounters in Settings > Mods > Killhouse.");
        var doc=ActiveDocument??throw new InvalidOperationException("Load a custom map first.");
        if(doc.Encounter==EncounterMode.PaperOnly || doc.Combatants.Count==0)throw new InvalidOperationException("This map has no enabled AI. Add combatants in the editor and choose AI + paper or AI only.");
        var error=Combatants.Check(doc).FirstOrDefault(i=>i.Fatal);if(error!=null)throw new InvalidOperationException(error.Message);
        var triggerError=ScenarioTriggers.Check(doc).FirstOrDefault(i=>i.Fatal || i.Message.Contains("no actions") || i.Message.Contains("assign a route"));if(triggerError!=null)throw new InvalidOperationException(triggerError.Message);
        foreach(var b in doc.Combatants) {
            if(b.Reaction==CivilianReaction.Flee && b.RouteId==0)throw new InvalidOperationException($"Assign an escape route to Flee civilian AI {b.Id} before starting.");
            var type=CombatantCatalog.Require(b.Prefab);
            if(!CombatantCatalog.Registered(type.Prefab,type.AssetId))throw new InvalidOperationException("The native network prefab is not registered: "+type.Label);
        }
        ClearEncounter(true);encounterDocument=LayoutDraft.Copy(doc);encounterIndex=Computer.activeLayoutIndex;encounterPending=true;spawnIndex=-2;spawnFrame=Time.frameCount;
        foreach(var p in encounterDocument.AIStarts)if(p.Value==AIStart.WaitForTrigger)waitingAI.Add(p.Key);
        encounterDoorBaseline.AddRange(Computer.shootHouseLayouts[encounterIndex].doors.Where(d=>d).Select(d=>new EncounterDoorState(d,d.CurrentlyLocked)));
        EncounterStatus="Preparing house navigation…";
    }
    internal void TickEncounter() {
        if(!EncounterActive)return;
        if(!NetworkServer.active || !Plugin.CombatAI.Value || !Computer || Computer.activeLayoutIndex!=encounterIndex || disposed){ClearEncounter();return;}
        if(!encounterPending) {
            try{encounterDoorAwareness.Tick(encounterBrains.Values,encounterCaution,encounterOrders,MapNightMode.Current);foreach(var brain in encounterBrains.Values)if(brain && brain.health && !brain.health.isDead){if(!DriveCivilian(brain))DriveCombatantPatrol(brain);}}
            catch(Exception error){ClearEncounter();Runtime.Status="AI movement failed: "+error.GetBaseException().Message;Plugin.Logger.LogError(error);}
            return;
        }
        if(Time.frameCount<=spawnFrame)return;
        try {
            if(spawnIndex==-2){encounterStage="navigation runtime";encounterAstar=EncounterNavigation.Ensure(Computer.gameObject.scene);spawnIndex=-1;spawnFrame=Time.frameCount;return;}
            if(spawnIndex<0){BakeEncounter();spawnIndex=0;spawnFrame=Time.frameCount;return;}
            if(spawnIndex<encounterDocument!.Combatants.Count){encounterStage="spawn AI "+encounterDocument.Combatants[spawnIndex].Id;var definition=encounterDocument.Combatants[spawnIndex++];if(encounterDocument.AIStarts.GetValueOrDefault(definition.Id)!=AIStart.Deferred)SpawnCombatant(definition);spawnFrame=Time.frameCount;EncounterStatus=$"Spawning AI {spawnIndex}/{encounterDocument.Combatants.Count}…";return;}
            int patrols=encounterDocument!.Combatants.Count(b=>b.RouteId!=0);
            encounterDoorAwareness.Bind(Computer.shootHouseLayouts[encounterIndex].doors);
            encounterPending=false;if(encounterDocument.Triggers.Count>0)ArmTriggers();EncounterStatus=$"AI encounter active: {encounterBots.Count} combatants, {patrols} assigned patrols. Stop or reset from the laptop.";Runtime.Status=EncounterStatus;
            CoopLog.Event("ai-started",new{house=House,index=encounterIndex,bots=encounterBots.Count,hostOwned=encounterBots.Count(go=>go && go.GetComponent<NetworkIdentity>() is {} identity && identity.isOwned),routes=encounterRoutes.Count,patrols,routeAssignments=encounterDocument.Combatants.Where(b=>b.RouteId!=0).Select(b=>new{placement=b.Id,route=b.RouteId}).ToArray()});
        }catch(Exception error){string failedStage=encounterStage;ClearEncounter();EncounterStatus=$"AI encounter failed ({failedStage}): "+error.GetBaseException().Message;Runtime.Status=EncounterStatus;Plugin.Logger.LogError(EncounterStatus+"\n"+error);CoopLog.Event("ai-failed",new{house=House,stage=failedStage,error=error.ToString()});}
    }
    internal Vector3 CombatPoint(float east,float south)=>anchor.TransformPoint(new Vector3(ConstructionPlan.Left+east,floor,ConstructionPlan.North-south));
    private void BakeEncounter() {
        encounterStage="house graph setup";
        var doc=encounterDocument!;var astar=encounterAstar;
        encounterSpectators=new(anchor,floor,House);
        if(!astar || !astar!.isActiveAndEnabled || astar.data==null)throw new InvalidOperationException("The encounter navigation runtime became unavailable.");
        encounterAstar=astar;encounterGraph=astar.data.AddGraph(Il2CppInterop.Runtime.Il2CppType.Of<RecastGraph>()).Cast<RecastGraph>();
        var graph=encounterGraph;graph.name=$"Killhouse {House} encounter";
        graph.forcedBoundsCenter=CombatPoint(doc.Columns,doc.Rows)+anchor.up*1.3f;
        graph.forcedBoundsSize=new(doc.Columns*2+1,4,doc.Rows*2+1);graph.rotation=anchor.rotation.eulerAngles;
        graph.characterRadius=EncounterClearance.AgentRadius;graph.walkableHeight=1.8f;graph.walkableClimb=.35f;graph.maxSlope=45;graph.cellSize=.1f;graph.minRegionSize=.1f;
        graph.rasterizeColliders=true;graph.rasterizeMeshes=false;graph.rasterizeTerrain=true;graph.rasterizeTrees=false;graph.mask=Physics.DefaultRaycastLayers;graph.useTiles=true;graph.editorTileSize=128;graph.enableNavmeshCutting=true;
        var graphMask=GraphMask.FromGraph(graph);
        barrierRoot=EncounterBarriers.Create(anchor,floor,doc,graphMask);
        var native=Computer.shootHouseLayouts[encounterIndex];
        foreach(var door in native.doors)encounterDoors.Bind(door,graphMask);
        // Rebind only this layout's borrowed door links to the new floor graph.
        foreach(var door in native.doors)foreach(var link in new[]{door.DoorOpenableNavLink,door.DoorWalkableNavLink})if(link && !encounterLinks.Any(p=>p.Link.Pointer==link.Pointer)) {
            encounterLinks.Add((link,link.graphMask));link.graphMask=graphMask;
        }
        encounterStage="floor scan";
        Physics.SyncTransforms();var watch=System.Diagnostics.Stopwatch.StartNew();astar.Scan(graph);
        encounterStage="door links";
        astar.navmeshUpdates.ForceUpdate();astar.FlushWorkItems();
        foreach(var pair in encounterLinks)if(pair.Link){EncounterDoors.RefreshLink(pair.Link);}
        astar.FlushGraphUpdates();astar.FlushWorkItems();
        if(graph.CountNodes()==0)throw new InvalidOperationException("No walkable floor was found in this house.");
        CoopLog.Event("ai-navigation-scanned",new{house=House,graph=graph.graphIndex,radius=graph.characterRadius,nodes=graph.CountNodes(),wallCuts=EncounterBoundary.Blocks(doc).Count()});
        routeRoot=new GameObject($"Killhouse {House} AI routes");routeRoot.transform.SetParent(anchor,false);
        encounterStage="waypoint clearance";
        foreach(var route in doc.Routes) {
            encounterRouteDefinitions.Add(route.Id,route);
            var list=new TransformList();encounterRoutes.Add(route.Id,list);
            foreach(var p in route.Points){var point=CheckedPoint(p.X,p.South,$"Route {route.Id} waypoint {list.Count+1}");var go=new GameObject($"Route {route.Id} point {list.Count+1}");go.transform.SetParent(routeRoot.transform,false);go.transform.position=point;list.Add(go.transform);}
        }
        encounterDoors.CheckRoutes(()=>{foreach(var b in doc.Combatants) {
            encounterStage="AI "+b.Id+" route preflight";
            CheckedPoint(b.X,b.South,$"AI {b.Id} spawn");
            if(b.RouteId==0 || b.Order==CombatOrder.Cautious && b.Reaction==CivilianReaction.None)continue;
            var route=doc.Routes.Single(r=>r.Id==b.RouteId);
            foreach(var (from,to) in Combatants.Legs(b,b.Reaction==CivilianReaction.Flee?route with{Loop=false}:route))CheckLeg(from,to,b.Id);
        }},()=>{astar.FlushGraphUpdates();astar.FlushWorkItems();});
        CoopLog.Event("ai-navigation",new{house=House,graph=graph.graphIndex,doorCuts=native.doors.Length,wallCuts=EncounterBoundary.Blocks(doc).Count(),radius=graph.characterRadius,nodes=graph.CountNodes(),bakeMs=watch.Elapsed.TotalMilliseconds,bots=doc.Combatants.Count});
    }
    private GraphMask EncounterMask()=>encounterAstar!.data.linkGraph is {} links?GraphMask.FromGraph(encounterGraph!)|GraphMask.FromGraph(links):GraphMask.FromGraph(encounterGraph!);
    private Vector3 CheckedPoint(float east,float south,string context="AI/route point") {
        var world=CombatPoint(east,south);var constraint=NearestNodeConstraint.Walkable;constraint.graphMask=GraphMask.FromGraph(encounterGraph!);
        var nearest=encounterAstar!.GetNearest(world,constraint);
        float? distance=nearest.node==null?null:Vector3.Distance(world,nearest.position);
        if(distance==null || distance>EncounterClearance.ProjectionLimit) {
            CoopLog.Event("ai-clearance-failed",new{house=House,context,east,south,radius=EncounterClearance.AgentRadius,projectionLimit=EncounterClearance.ProjectionLimit,distance,requested=world.ToString(),nearest=nearest.node==null?null:nearest.position.ToString()});
            throw new InvalidOperationException($"{context}: no walkable floor at east {east:0.##}, south {south:0.##}. Move the AI/route clear of props and walls.");
        }
        return nearest.position;
    }
    private void CheckLeg(RoutePoint from,RoutePoint to,int id) {
        var path=ABPath.Construct(CheckedPoint(from.X,from.South),CheckedPoint(to.X,to.South),null);
        path.nnConstraint.graphMask=EncounterMask();
        // Match Awake + SetDoorMode(CanOpenAndGoThrough), including the
        // prefab's other tag restrictions. Preflight and live movement must
        // use the same door connections.
        path.enabledTags=CombatantNavigation.OpenDoorTags(CombatantCatalog.Require(encounterDocument!.Combatants.Single(b=>b.Id==id).Prefab).Navigation.Agent);
        AstarPath.StartPath(path,false,false);path.BlockUntilCalculated();
        if(path.error || path.CompleteState!=PathCompleteState.Complete)throw new InvalidOperationException($"AI {id} has an unreachable route leg near {to.X:0.##}, {to.South:0.##}. Check walls and furniture.");
    }
    private void SpawnCombatant(Combatant definition) {
        var type=CombatantCatalog.Require(definition.Prefab);
        var go=NativeDamageResources.Clone(type.Prefab,CheckedPoint(definition.X,definition.South),anchor.rotation*Quaternion.Euler(0,definition.Yaw,0));
        encounterBots.Add(go);var brain=go.GetComponent<BrainAI>();
        if(!brain)throw new InvalidOperationException("Spawned prefab has no BrainAI.");
        encounterDefinitions.Add(brain.Pointer,definition);encounterBrains.Add(brain.Pointer,brain);ConfigureCombatant(brain,definition);EncounterSpawn.HostOwned(go);
    }
    internal void CombatantStarted(BrainAI brain) {
        if(!NetworkServer.active || !encounterDefinitions.TryGetValue(brain.Pointer,out var definition))return;
        try{ConfigureCombatant(brain,definition);}catch(Exception error){ClearEncounter();EncounterStatus="AI initialization failed: "+error.GetBaseException().Message;Plugin.Logger.LogError(error);}
    }
    internal bool OwnsCombatant(BrainAI? brain)=>brain && encounterDefinitions.ContainsKey(brain!.Pointer);
    internal bool ExcludesSight(BrainAI brain,Transform? target)=>OwnsCombatant(brain) && encounterSpectators?.Excluded(target)==true;
    internal void FilterEncounterSight(BrainAI brain){if(OwnsCombatant(brain))encounterSpectators?.Forget(brain);}
    internal void MaintainCautious(BrainAI brain){if(OwnsCombatant(brain))encounterCaution.Apply(brain,encounterOrders);}
    internal void ObserveCautiousSight(BrainAI brain,Transform target) {
        if(!OwnsCombatant(brain) || !target)return;
        var health=target.root.GetComponent<Health>();
        encounterCaution.ObserveVision(brain,encounterOrders,!encounterPending,health && !health.isBot && !health.isDead && !ExcludesSight(brain,target));
    }
    internal void CleanEncounterResidue(int index) {
        if(!Computer || index<0 || index>=Computer.shootHouseLayouts.Length || Computer.activeLayoutIndex!=index)return;
        int n=index-StockCount;
        if(n<0)EncounterResidue.Schedule(anchor,new(10,10,floor),House);
        else if(n<items.Count)EncounterResidue.Schedule(anchor,new(items[n].Layout.Columns,items[n].Layout.Rows,floor),House);
    }
    internal void BeforeEncounterDoor(DoorV2 door) {
        // Preserve each blocked waypoint. An unlock/breach schedules a fresh
        // path on the next normal patrol tick without interrupting traversal.
        if(encounterDoors.BeforeBlockers(door))encounterSearchAt.Clear();
    }
    internal bool FilterEncounterDestination(FollowerEntity follower,ref Vector3 destination)=>encounterOrders.FilterDestination(follower,ref destination);
    internal bool IsReactionCivilian(BrainAI brain)=>encounterDefinitions.TryGetValue(brain.Pointer,out var definition) && definition.Reaction!=CivilianReaction.None;
    internal bool DriveCivilian(BrainAI brain) {
        if(HoldTriggeredRoute(brain))return true;
        if(!NetworkServer.active || !encounterDefinitions.TryGetValue(brain.Pointer,out var definition) || definition.Reaction==CivilianReaction.None)return false;
        if(!brain.health || brain.health.isDead || !brain.agent || !brain.agent.entityExists)return true;
        EncounterCivilians.Disarm(brain);
        if(releasedAI.Contains(definition.Id)) {
            brain.CurrentState=BrainAI.States.Idle;brain.responding=false;
            brain.agent.maxSpeed=brain.isFlashed?0:definition.Reaction==CivilianReaction.Flee?Math.Max(brain.PatrolSpeed,brain.EvasionSpeed):brain.PatrolSpeed;
            DriveCombatantPatrol(brain,true);return true;
        }
        bool alarmed=encounterCivilians.Observe(brain,definition,!encounterPending);
        bool fleeing=definition.Reaction==CivilianReaction.Flee && encounterCivilians.CanFlee(brain);
        brain.CurrentState=BrainAI.States.Idle;brain.responding=false;
        if(definition.Reaction==CivilianReaction.Freeze && alarmed || definition.Reaction==CivilianReaction.Flee && !fleeing || definition.RouteId==0) {
            var point=alarmed?encounterCivilians.HoldPoint(brain):brain.DefensiveInitialPosition;
            encounterOrders.Bind(brain,true,point,definition.Id);brain.idleStates=BrainAI.IdleStates.Idle;
            if((brain.agent.Agent.destination-point).sqrMagnitude>.0025f)brain.agent.Agent.destination=point;
            if(!brain.agent.Agent.isTraversingOffMeshLink)brain.agent.Agent.isStopped=alarmed || definition.Reaction==CivilianReaction.Flee;
            brain.agent.maxSpeed=brain.isFlashed?0:brain.PatrolSpeed;
        } else {
            brain.agent.maxSpeed=brain.isFlashed?0:definition.Reaction==CivilianReaction.Flee?Math.Max(brain.PatrolSpeed,brain.EvasionSpeed):brain.PatrolSpeed;
            DriveCombatantPatrol(brain,true);
        }
        return true;
    }
    internal bool DriveCombatantPatrol(BrainAI brain,bool civilian=false) {
        if(HoldTriggeredRoute(brain))return true;
        if(encounterDefinitions.TryGetValue(brain.Pointer,out var cautious) && cautious.Order==CombatOrder.Cautious && cautious.Reaction==CivilianReaction.None)return true;
        if(!NetworkServer.active || !encounterDefinitions.TryGetValue(brain.Pointer,out var definition) || definition.RouteId==0)return false;
        if(!civilian && definition.Reaction!=CivilianReaction.None)return true;
        if(!brain.health || brain.health.isDead || !brain.agent || !brain.agent.entityExists)return true;
        var route=encounterRouteDefinitions[definition.RouteId];var points=encounterRoutes[route.Id];
        if(!encounterProgress.TryGetValue(brain.Pointer,out var progress))encounterProgress.Add(brain.Pointer,progress=new());
        if(brain.isFlashed || !civilian && (brain.responding || brain.CurrentState!=BrainAI.States.Idle)) {
            progress.Pause();RouteNote(brain,definition,progress,brain.isFlashed?RoutePhase.Flashed:brain.responding?RoutePhase.Response:RoutePhase.NativeState);return true;
        }
        if(brain.agent.Agent.isTraversingOffMeshLink)return true;
        var destination=points[progress.Index].position;
        float wait=PatrolTiming.Wait(route,progress,definition.Id,definition.Reaction==CivilianReaction.Flee);
        progress.Tick(points.Count,route.Loop && definition.Reaction!=CivilianReaction.Flee,wait,Time.time,EncounterMotion.ArrivalDistance(brain.agent.Agent,destination),brain.agent.pathPending,brain.agent.hasPath?brain.agent.remainingDistance:0);
        brain.aiPatrol.currentWayPointIndex=progress.Index;
        destination=points[progress.Index].position;
        // Set the destination while patrolling, including the final hold. Never
        // claim arrival from a missing/failed native path alone.
        if(civilian)encounterOrders.Bind(brain,true,destination,definition.Id);
        bool changed=EncounterMotion.SetGoal(brain.agent.Agent,destination);
        if(!progress.Waiting && !progress.Finished)EncounterMotion.FaceTravel(brain,civilian);
        if(!brain.agent.pathPending && (changed || !progress.Finished && !progress.Waiting && Time.time>=encounterSearchAt.GetValueOrDefault(brain.Pointer))) {
            brain.agent.Agent.SearchPath();encounterSearchAt[brain.Pointer]=Time.time+3;
        }
        RouteNote(brain,definition,progress,progress.Finished?RoutePhase.Finished:progress.Waiting?RoutePhase.Watching:RoutePhase.Patrolling,progress.Waiting?wait:0);
        if(progress.Finished){encounterOrders.Bind(brain,true,destination,definition.Id);brain.idleStates=BrainAI.IdleStates.Idle;brain.DefensiveInitialPosition=destination;}
        return true;
    }
    private void RouteNote(BrainAI brain,Combatant definition,RouteProgress progress,RoutePhase phase,float wait=0) {
        var key=new RouteObservation(progress.Index,phase,phase==RoutePhase.NativeState?(int)brain.CurrentState:0);
        if(encounterRouteState.TryGetValue(brain.Pointer,out var prior) && prior==key)return;
        encounterRouteState[brain.Pointer]=key;
        string state=phase==RoutePhase.NativeState?key.Label+brain.CurrentState:key.Label;
        CoopLog.Event("ai-route-progress",new{placement=definition.Id,route=definition.RouteId,waypoint=progress.Index+1,state,waitSeconds=wait,arrivals=progress.Visits,position=brain.agent.Agent.position.ToString()});
    }
    private void ConfigureCombatant(BrainAI brain,Combatant definition) {
        if(releasedAI.Contains(definition.Id) && definition.Order==CombatOrder.Cautious){definition=definition with{Order=CombatOrder.Defensive};encounterDefinitions[brain.Pointer]=definition;}
        bool waiting=waitingAI.Contains(definition.Id);
        brain.reactType=definition.Order==CombatOrder.Offensive?BrainAI.ReactType.Offensive:BrainAI.ReactType.Defensive;
        brain.DefensiveInitialPosition=CombatPoint(definition.X,definition.South);brain.DefensiveInitialRotation=anchor.rotation*Quaternion.Euler(0,definition.Yaw,0);
        brain.DoorInteraction=BrainAI.DoorInteractionMode.CanOpenAndGoThrough;
        var navigation=CombatantCatalog.Navigation(brain);
        if(!navigation || !navigation!.Agent)throw new InvalidOperationException("AI navigation controller is missing.");
        brain.agent=navigation;
        var origin=CheckedPoint(definition.X,definition.South);
        brain.DefensiveInitialPosition=origin;
        encounterOrders.Bind(brain,waiting || (definition.Order==CombatOrder.Defensive && definition.RouteId==0) || definition.Reaction==CivilianReaction.Flee,origin,definition.Id);
        if(waiting)encounterCaution.Remove(brain);else {encounterCaution.Bind(brain,definition,origin);encounterCaution.Apply(brain,encounterOrders);}
        // SetDoorMode recreates the follower entity and requests a path. Give
        // it the owned graph before that recreation, so it cannot attach to a
        // cached stock graph overlapping the custom house.
        CombatantNavigation.Configure(brain.agent,EncounterMask(),brain.DoorInteraction);
        if(definition.RouteId!=0 && definition.Order!=CombatOrder.Cautious && !waiting) {
            var route=encounterRouteDefinitions[definition.RouteId];
            brain.aiPatrol??=new AIPatrol();brain.aiPatrol.patrolPoints=encounterRoutes[route.Id];brain.aiPatrol.currentWayPointIndex=0;brain.aiPatrol.Looping=route.Loop;brain.aiPatrol.patrolWaitTime=route.Wait;brain.aiPatrol.onReachDestination=AIPatrol.OnReachDestination.Wait;
            brain.idleStates=BrainAI.IdleStates.Patrol;if(!brain.agent.Agent.isTraversingOffMeshLink)brain.agent.Agent.isStopped=false;
            brain.agent.Agent.destination=encounterRoutes[route.Id][0].position;
        } else {brain.idleStates=BrainAI.IdleStates.Idle;brain.agent.destination=CheckedPoint(definition.X,definition.South);}
        if(waiting && !brain.agent.Agent.isTraversingOffMeshLink)brain.agent.Agent.isStopped=true;
        brain.agent.Agent.SearchPath();
        encounterCivilians.Bind(brain,definition);
        if(definition.Reaction!=CivilianReaction.None){EncounterCivilians.Disarm(brain);DriveCivilian(brain);}
    }
    internal void ClearEncounter(bool resetDoors=false) {
        DisarmTriggers();waitingAI.Clear();releasedAI.Clear();
        bool had=EncounterActive;int stoppedIndex=encounterIndex;var stoppedDocument=encounterDocument;
        if(resetDoors && !had && Computer){stoppedIndex=Computer.activeLayoutIndex;stoppedDocument=ActiveDocument;}
        var stoppedDoors=encounterDoorBaseline.ToArray();encounterDoorBaseline.Clear();
        encounterPending=false;encounterIndex=-1;encounterDefinitions.Clear();encounterProgress.Clear();encounterOrders.Clear();encounterCaution.Clear();encounterDoorAwareness.Clear();encounterCivilians.Clear();encounterSpectators=null;encounterBrains.Clear();encounterSearchAt.Clear();encounterRouteState.Clear();
        int cleanupFailures=0;string clearStatus="AI cleared.";
        void Cleanup(Action action){try{action();}catch(Exception error){cleanupFailures++;Plugin.Logger.LogWarning("AI cleanup: "+error.GetBaseException().Message);}}
        foreach(var go in encounterBots)if(go){Cleanup(()=>{var brain=go.GetComponent<BrainAI>();if(brain && brain.agent && brain.agent.Agent)brain.agent.Agent.enabled=false;});Cleanup(()=>{var identity=go.GetComponent<NetworkIdentity>();if(NetworkServer.active && identity && identity.netId!=0)NetworkServer.Destroy(go);else Object.Destroy(go);});}
        if((had || resetDoors) && stoppedIndex>=0) {
            Cleanup(()=>EncounterResidue.Schedule(anchor,new(stoppedDocument?.Columns??10,stoppedDocument?.Rows??10,floor),House));
            Cleanup(()=>CoopNetwork.CleanEncounter(this,stoppedIndex));
        }
        encounterBots.Clear();encounterRoutes.Clear();encounterRouteDefinitions.Clear();if(routeRoot)Cleanup(()=>Object.Destroy(routeRoot));routeRoot=null;
        if(barrierRoot)Cleanup(()=>{barrierRoot!.SetActive(false);Object.Destroy(barrierRoot);});barrierRoot=null;
        foreach(var (link,_) in encounterLinks)if(link)Cleanup(()=>link.RemoveLink());
        if(encounterGraph!=null && encounterAstar && encounterAstar!.data!=null)Cleanup(()=>encounterAstar.data.RemoveGraph(encounterGraph));
        encounterDoors.Restore(Cleanup);
        foreach(var (link,mask) in encounterLinks)if(link)Cleanup(()=>{link.graphMask=mask;EncounterDoors.RefreshLink(link);});encounterLinks.Clear();
        if(resetDoors && !disposed && NetworkServer.active && Computer && Computer.gameObject.scene.isLoaded && stoppedIndex==Computer.activeLayoutIndex && stoppedIndex>=0 && stoppedIndex<Computer.shootHouseLayouts.Length) Cleanup(()=>{
            if(stoppedDoors.Length==0)stoppedDoors=Computer.shootHouseLayouts[stoppedIndex].doors.Where(d=>d).Select(d=>new EncounterDoorState(d,d.CurrentlyLocked)).ToArray();
            var result=EncounterDoorReset.Restore(stoppedDoors,d=>doors.Any(saved=>saved.Door.Pointer==d.Pointer && saved.RepairReady),error=>{cleanupFailures++;Plugin.Logger.LogWarning("AI door reset: "+error.GetBaseException().Message);});
            clearStatus=$"AI cleared; {result.Reset} doors repaired and closed."+(result.Skipped>0?$" {result.Skipped} uninitialized doors skipped.":"");
            Physics.SyncTransforms();CoopLog.Event("ai-doors-reset",new{house=House,index=stoppedIndex,result.Reset,result.Skipped,result.Failures});
        });
        encounterGraph=null;encounterAstar=null;encounterDocument=null;EncounterStatus=cleanupFailures==0?resetDoors?clearStatus:"AI encounter stopped.":"AI cleanup encountered an error. See the BepInEx log.";
        if(had)CoopLog.Event("ai-stopped",new{house=House});
    }
}
