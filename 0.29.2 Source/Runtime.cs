using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BepInEx;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using IntList=Il2CppSystem.Collections.Generic.List<int>;

namespace OperatorKillhouse;

internal static partial class Runtime
{
    internal static readonly Dictionary<int,RangeSession> Sessions=new();
    internal static CatalogSnapshot CatalogState=new(Array.Empty<CatalogItem>(),Array.Empty<string>(),"");
    private static Harmony? patches;
    private static readonly System.Random random=new();
    private static UnityEngine.Events.UnityAction<Scene>? sceneCallback;
    private static float nextScan;
    private static bool stopping,transition;
    private static readonly Dictionary<int,NativeSelectionSteps> activeSelections=new();
    private static readonly Dictionary<int,RangeComputer> quarantined=new();
    private static readonly Dictionary<int,Exception> pendingFaults=new();
    private sealed record PendingSelection(RangeSession Session,int Index,SelectionIdentity Identity,int[][]? Masks,float Started);
    private static readonly Dictionary<int,PendingSelection> pendingSelections=new();
    private static readonly HashSet<int> rangeScenes=new();
    internal static bool HasCustomActive=>Sessions.Values.Any(s=>s.Computer && s.Computer.activeLayoutIndex>=s.StockCount);
    private sealed class ResetTicket {internal readonly SelectionIdentity Identity;internal ResetTicket(SelectionIdentity identity){Identity=identity;}}
    private sealed record PendingReset(RangeComputer Computer,ResetTicket Ticket,ResetVerification Verification,int Frame);
    private static readonly Dictionary<int,PendingReset> pendingResets=new();
    internal static string Status="Starting";
    internal static string RangeSetupFailure="";
    internal static bool Visible;
    private static Rect window=new(40,80,500,540);
    private static Vector2 scroll;
    private static RangeComputer? focused;
    private static MapEditor? editor;
    private static MapEditor? proposalPreview;
    private static int previewHouse;
    private static MapEditor? ActiveEditor=>proposalPreview?.Visible==true?proposalPreview:editor?.Visible==true?editor:null;
    internal static bool EditorVisible=>ActiveEditor!=null;
    internal static bool CanEdit=>EditorSessionPolicy.CanEdit(Plugin.Enabled.Value,NetworkServer.active,stopping,CoopNetwork.GuestCanEdit);
    internal static bool CanSaveEdited=>EditingAsGuest?CanEdit:EditorSessionPolicy.CanSave(CanEdit,pendingSelections.Count>0,CoopNetwork.Busy);
    internal static bool CanPushEdited=>EditingAsGuest?CanSaveEdited && CoopNetwork.GuestCanRequest:EditorSessionPolicy.CanPush(CanSaveEdited,RemoteCount(),CoopNetwork.Ready);
    internal static bool EditingWithPeers=>NetworkServer.active && RemoteCount()>0;
    internal static bool EditingAsGuest=>!NetworkServer.active;
    internal static bool CanAcceptGuestMap=>NetworkServer.active && CanSaveEdited && CoopNetwork.Ready;
    internal static bool CanLoadMaps=>NetworkServer.active && Eligible() && !transition && pendingSelections.Count==0 && pendingResets.Count==0 && !CoopNetwork.Busy;
    private static CursorLockMode savedLock;
    private static bool savedCursor;
    private static int suppressInputThrough=-1;
    private static string ownFolder=>Path.Combine(Paths.PluginPath,"OperatorKillhouse","layouts");
    internal static readonly string[] RequiredMethods={"Awake","UpdateLayout","RandomiseRange","UserCode_CMD_RandomiseRange","InvokeUserCode_CMD_RandomiseRange","CMD_SyncLayoutWithNewPlayer","UserCode_CMD_SyncLayoutWithNewPlayer__NetworkConnectionToClient","InvokeUserCode_CMD_SyncLayoutWithNewPlayer__NetworkConnectionToClient"};
    internal static void Start() {
        NativeAssetDiagnostics.Start();
        string game=Path.GetDirectoryName(Environment.ProcessPath)!;
        using(var binary=File.OpenRead(Path.Combine(game,"GameAssembly.dll")))
        using(var sha=SHA256.Create()) {
            string hash=Convert.ToHexString(sha.ComputeHash(binary));NativeAssetDiagnostics.GameAssembly(hash);
            if(hash!="D4347448524D79A7E367F2B22D66BB3A21E1F3733D32ABA37FC7E3E270A620DE") throw new NotSupportedException("Game build needs compatibility inspection.");
        }
        foreach(string name in RequiredMethods) if(AccessTools.Method(typeof(RangeComputer),name)==null) throw new MissingMethodException("RangeComputer."+name);
        TrainingPatches.Validate();
        WatchTraining();
    }
    private static void EnterTraining() {
        stopping=false;menuFailure="";
        NativeBreakerAssets.Start();NativeSseAssets.Start();NativeFixtureAssets.Start();NativeGearAssets.Start();Reload(enteringTraining:true); patches=new Harmony(Plugin.Id);
        InstallTrainingDepartureHooks();
        CoopNetwork.Install();
        NativeBreakerInteraction.Install(patches);SseObjects.Install(patches);OutdoorGearLocker.Install(patches);
        Patch("Awake",nameof(BeforeAwake),nameof(AfterAwake));
        patches.Patch(AccessTools.Method(typeof(RangeComputer),"UpdateLayout"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeLayout)),finalizer:new HarmonyMethod(typeof(Runtime),nameof(LayoutFinalizer)));
        patches.Patch(AccessTools.Method(typeof(RangeComputer),"UserCode_RPC_ResetRange"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeReset)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterReset)),finalizer:new HarmonyMethod(typeof(Runtime),nameof(ResetFinalizer)));
        patches.Patch(AccessTools.Method(typeof(RangeComputer),"UserCode_RPC_StopTimer"),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterStopTimer)));
        foreach(string name in new[]{"RandomiseRange","UserCode_CMD_RandomiseRange"}) Patch(name,nameof(BeforeRandom));
        Patch("InvokeUserCode_CMD_RandomiseRange",nameof(BeforeRandomDispatch));
        foreach(string name in RequiredMethods.Where(n=>n.Contains("SyncLayoutWithNewPlayer"))) Patch(name,name.StartsWith("Invoke")?nameof(BeforeSyncDispatch):nameof(BeforeSync));
        patches.Patch(AccessTools.Method(typeof(NetworkServer),"SetClientReady",new[]{typeof(NetworkConnectionToClient)}),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeClientReady)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterClientReady)),finalizer:new HarmonyMethod(typeof(Runtime),nameof(ClientReadyFinalizer)));
        foreach(string name in new[]{"StartTimer","AddTargetHit","AddCivilianHit","ResetRange","UserCode_CMD_ResetRange"})Patch(name,nameof(BeforeTrainingAction));
        patches.Patch(AccessTools.Method(typeof(BulletDamageable),"CleanDamage"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeDamageCost)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterNativeCost)));
        patches.Patch(AccessTools.Method(typeof(BulletDamageable),"InitAndSetResources"),prefix:new HarmonyMethod(typeof(NativeDamageResources),nameof(NativeDamageResources.BeforeBullet)),finalizer:new HarmonyMethod(typeof(NativeDamageResources),nameof(NativeDamageResources.AfterBullet)));
        patches.Patch(AccessTools.Method(typeof(Knife.RealBlood.Decals.CharacterDamagePainter),"InitAndSetResources"),prefix:new HarmonyMethod(typeof(NativeDamageResources),nameof(NativeDamageResources.BeforePainter)),finalizer:new HarmonyMethod(typeof(NativeDamageResources),nameof(NativeDamageResources.AfterPainter)));
        patches.Patch(AccessTools.Method(typeof(DoorV2),"ResetDoor"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeDoorCost)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterNativeCost)));
        patches.Patch(AccessTools.Method(typeof(DoorV2),"ResetDoor"),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterDoorReset)));
        patches.Patch(AccessTools.Method(typeof(DoorV2),"UserCode_RPC_RepairDoor"),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterDoorRepair)));
        patches.Patch(AccessTools.Method(typeof(DoorV2),"Awake"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeDoorAwake)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterDoorAwake)));
        patches.Patch(AccessTools.Method(typeof(BrainAI),"Start"),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterCombatantStart)));
        patches.Patch(AccessTools.Method(typeof(BrainAI),"Patrol"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeCombatantPatrol)));
        patches.Patch(AccessTools.Method(typeof(BrainAI),"RunBrainTick"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeEncounterBrainTick)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterEncounterBrainTick)));
        patches.Patch(AccessTools.Method(typeof(EyesAI),"Vision"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeEncounterVision)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterEncounterVision)),finalizer:new HarmonyMethod(typeof(Runtime),nameof(EncounterVisionFinalizer)));
        patches.Patch(AccessTools.Method(typeof(EyesAI),"TestIfCanSeeAtHeight"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeEncounterSight)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterEncounterSight)));
        patches.Patch(AccessTools.Method(typeof(EyesAI),"ScoreTarget"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeEncounterTargetScore)));
        patches.Patch(AccessTools.Method(typeof(EyesAI),"FindTarget"),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterEncounterFindTarget)));
        patches.Patch(AccessTools.Method(typeof(BrainAI),"UpdateStateMachine"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeEncounterState)));
        patches.Patch(AccessTools.Method(typeof(WeaponsAI),"FixedUpdate"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeCivilianWeapons)));
        patches.Patch(AccessTools.Method(typeof(OnBulletHitAddForce),"OnHit"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforePropShot)));
        patches.Patch(AccessTools.Method(typeof(Melee),"ProcessKick"),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterPropKick)));
        patches.Patch(AccessTools.Method(typeof(BulletInstance),"ProcessBullet"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforePropBullet)),finalizer:new HarmonyMethod(typeof(Runtime),nameof(AfterPropBullet)));
        patches.Patch(AccessTools.PropertySetter(typeof(Pathfinding.FollowerEntity),"destination"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeEncounterDestination)));
        // Other door mods may restore the lock-only policy first. Resolve our
        // encounter-owned doors last, immediately before the native blockers.
        foreach(string method in new[]{"Update","HandleAIBlockers"})patches.Patch(AccessTools.Method(typeof(DoorV2),method),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeEncounterDoor)){priority=Priority.Last});
        patches.Patch(AccessTools.Method(typeof(Health),"TakeDamage",new[]{typeof(float),typeof(HealthBodyPart),typeof(HealthBodyPart),typeof(int),typeof(Vector3)}),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeEncounterDamage)),finalizer:new HarmonyMethod(typeof(Runtime),nameof(AfterEncounterDamage)));
        try {patches.Patch(AccessTools.Method(typeof(GameObject),"SetActive",new[]{typeof(bool)}),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeActivationCost)),postfix:new HarmonyMethod(typeof(Runtime),nameof(AfterNativeCost)));}
        catch(Exception error){Plugin.Logger.LogWarning("Optional activation timing unavailable: "+error.GetBaseException().Message);}
        patches.Patch(AccessTools.Method(typeof(InputLayer),"GetKey",new[]{typeof(InputKey),typeof(bool)}),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeKey)));
        foreach(string name in new[]{"MouseMovement","MouseMovementUnscaled"}) patches.Patch(AccessTools.PropertyGetter(typeof(InputLayer),name),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeMouse)));
        foreach(string name in new[]{"GetAxis","GetAxisRaw"})patches.Patch(AccessTools.Method(typeof(Input),name,new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeScrollAxis)));
        patches.Patch(AccessTools.PropertyGetter(typeof(Input),"mouseScrollDelta"),prefix:new HarmonyMethod(typeof(Runtime),nameof(BeforeMouse)));
        Plugin.Logger.LogInfo("Training active: Killhouse hooks and resources enabled.");
    }
    private static void Patch(string name,string? prefix,string? postfix=null)=>patches!.Patch(AccessTools.Method(typeof(RangeComputer),name),prefix==null?null:new HarmonyMethod(typeof(Runtime),prefix),postfix==null?null:new HarmonyMethod(typeof(Runtime),postfix));
    internal static int RemoteCount() {
        if(!NetworkServer.active) return 0;
        var local=NetworkServer.localConnection;
        int count=0; foreach(var entry in NetworkServer.connections) if(entry.Value!=null && (local==null || entry.Value.Pointer!=local.Pointer)) count++;
        return count;
    }
    private static bool Eligible()=>!stopping && SelectionPolicy.CanCustomize(Plugin.Enabled.Value,NetworkServer.active,RemoteCount(),false,CoopNetwork.Ready);
    private static bool CanAttach()=>!stopping && Plugin.Enabled.Value && (NetworkServer.active?(RemoteCount()==0 || CoopNetwork.CanAttach):NetworkClient.active && CoopNetwork.CanAttach);
    private static void AfterAwake(RangeComputer __instance) {if(CanAttach())Attach(__instance);}
    private static void Attach(RangeComputer computer) {
        if(!computer || Sessions.ContainsKey(computer.GetInstanceID()) || quarantined.ContainsKey(computer.GetInstanceID()) || !CanAttach()) return;
        if(!NativeBreakerAssets.Ready || !NativeSseAssets.Ready || !NativeFixtureAssets.Ready || !ReadyForAttachment(computer)){Status="Waiting for native range initialization.";return;}
        rangeScenes.Add(computer.gameObject.scene.handle);
        try {
            var session=new RangeSession(computer,CatalogState);
            try{session.ProtectDoorIntegrity();Sessions.Add(computer.GetInstanceID(),session);}
            catch(Exception setupError){try{session.Dispose();}catch(Exception cleanupError){throw new AggregateException(setupError,cleanupError);}throw;}
            Status=$"{session.Items.Count} custom layouts ready.";
        }
        catch(Exception error) {Status="Range setup failed: "+error.GetBaseException().Message;RangeSetupFailure=MenuNotice.Plain(Status,300);quarantined[computer.GetInstanceID()]=computer;SelectionLog.Note(SelectionLog.Identity(computer,computer.activeLayoutIndex,"attach"),"failed","attach",error.ToString());}
    }
    private static bool BeforeLayout(RangeComputer __instance,int __0,IntList? __1,IntList? __2,IntList? __3) {
        if(!__instance)return false;
        if(initializationGate?.Active==true && initializationInstance==__instance.GetInstanceID())return true;
        int key=__instance.GetInstanceID();if(activeSelections.TryGetValue(key,out var active))return active.Gate.Active;
        if(!Sessions.TryGetValue(key,out var session) && !quarantined.ContainsKey(key))return BeforeStockLayout(__instance,__0,__1,__2,__3);
        if(!NetworkServer.active && session!=null) {
            // Prepared custom selections arrive through our commit barrier. A
            // native late-join RPC may never activate an unprepared custom root.
            if(__0>=session.StockCount)return false;
            session.Prepare(__0);__instance.activeLayoutIndex=__0;
            __instance._targetindexdata=__1!;__instance._civeindexdata=__2!;__instance._hostagetakerindexdata=__3!;return true;
        }
        if(!Eligible()) {
            if(session!=null) {Detach(session,"ineligible");return false;}
            return true;
        }
        Select(__instance,__0,"native-update",__1,__2,__3,true);return false;
    }
    private static bool BeforeRandom(RangeComputer __instance) {
        if(transition || !NetworkServer.active) return true;
        if(CoopNetwork.Busy || pendingSelections.Count>0)return false;
        if(!Eligible()) {RestoreAll();return true;}
        Attach(__instance);
        if(!Sessions.ContainsKey(__instance.GetInstanceID()) && !quarantined.ContainsKey(__instance.GetInstanceID())) return true;
        var session=Sessions.GetValueOrDefault(__instance.GetInstanceID());
        var choices=Enumerable.Range(0,__instance.shootHouseLayouts.Length).Where(n=>session==null || session.Listed(n)).ToArray();
        if(choices.Length>0)Select(__instance,choices[SelectionPolicy.Next(choices.Length,Array.IndexOf(choices,__instance.activeLayoutIndex),random.Next(),Plugin.AvoidRepeat.Value)],"random");return false;
    }
    private static bool BeforeRandomDispatch(NetworkBehaviour __0) {
        var computer=__0.TryCast<RangeComputer>();return computer is null || !computer || BeforeRandom(computer);
    }
    private static bool BeforeTrainingAction()=>activeSelections.Count>0 || !CoopNetwork.Switching && pendingResets.Count==0;
    private sealed record NativeCost(NativeSelectionSteps Steps,int Kind,long Began);
    private static void BeforeDamageCost(ref NativeCost? __state){__state=DiagnosticLog.Enabled && activeSelections.Count>0 && activeSelections.Values.FirstOrDefault() is {} steps?new(steps,0,System.Diagnostics.Stopwatch.GetTimestamp()):null;}
    private static void BeforeDoorCost(ref NativeCost? __state){__state=DiagnosticLog.Enabled && activeSelections.Count>0 && activeSelections.Values.FirstOrDefault() is {} steps?new(steps,1,System.Diagnostics.Stopwatch.GetTimestamp()):null;}
    private static void BeforeDoorAwake(DoorV2 __instance){foreach(var session in Sessions.Values)session.DoorAwake(__instance,false);}
    private static void AfterDoorAwake(DoorV2 __instance){foreach(var session in Sessions.Values)session.DoorAwake(__instance,true);}
    private static void AfterDoorReset(DoorV2 __instance){foreach(var session in Sessions.Values)session.DoorReset(__instance);}
    private static void AfterDoorRepair(DoorV2 __instance){foreach(var session in Sessions.Values)session.DoorRepaired(__instance);}
    private static void BeforeActivationCost(GameObject __instance,bool __0,ref NativeCost? __state){__state=DiagnosticLog.Enabled && activeSelections.Count>0 && activeSelections.Values.FirstOrDefault() is {} steps?new(steps,steps.ActivationKind(__instance,__0),System.Diagnostics.Stopwatch.GetTimestamp()):null;}
    private static void AfterNativeCost(NativeCost? __state){if(__state!=null)__state.Steps.RecordCost(__state.Kind,(System.Diagnostics.Stopwatch.GetTimestamp()-__state.Began)*1000d/System.Diagnostics.Stopwatch.Frequency);}
    internal static void Select(RangeComputer computer,int index,string source="F8",IntList? targets=null,IntList? civilians=null,IntList? hostages=null,bool suppliedMasks=false) {
        if(!computer)return;
        Sessions.TryGetValue(computer.GetInstanceID(),out var session);
        var identity=SelectionLog.Identity(computer,index,source,session);
        if(!Eligible() || transition || CoopNetwork.Busy || pendingSelections.Count>0 || computer.shootHouseLayouts==null || index<0 || index>=computer.shootHouseLayouts.Length || (session==null && !quarantined.ContainsKey(computer.GetInstanceID()))) {
            SelectionLog.Note(identity,"rejected","guard","Range is not ready for this selection.");return;
        }
        stockInitialization.Remove(computer.GetInstanceID());
        if(session!=null && !MapNightMode.Ready(session.LightingMap(index),out string lightingReason)){Status=lightingReason;SelectionLog.Note(identity,"rejected","map-lighting",lightingReason);return;}
        int[][]? masks=suppliedMasks?new[]{NativeRangeContract.Mask(targets)!,NativeRangeContract.Mask(civilians)!,NativeRangeContract.Mask(hostages)!}:null;
        if(session==null) {Execute(identity,new NativeSelectionSteps(computer,index,()=>{},Bits,targets,civilians,hostages,suppliedMasks));return;}
        if(RemoteCount()>0) {
            if(!CoopNetwork.Request(session,index,identity,masks))SelectionLog.Note(identity,"rejected","co-op-prepare",CoopNetwork.Status);
            else Status=$"Preparing {identity.Layout} for all players…";
            return;
        }
        if(!session.BeginStage(index)){SelectionLog.Note(identity,"failed","planning",session.BuildError(index)??"A build is already pending.");return;}
        pendingSelections[identity.Instance]=new(session,index,identity,masks,Time.unscaledTime);
        SelectionLog.Note(identity,"queued","build","Preparing layout geometry.");Status=$"Preparing {identity.Layout}…";
    }
    internal static bool ValidateMasks(RangeSession session,int index,int[][] masks,bool liveState=false) {
        if(masks==null || masks.Length!=3 || masks.Any(m=>m==null || m.Length>60 || m.Any(v=>v is not (0 or 1))))return false;
        var layout=session.Computer.shootHouseLayouts[index];return masks[0].Length==layout.targets.Length && masks[1].Length==layout.civilians.Length && masks[2].Length==layout.hostageTakers.Length && (index<session.StockCount || (liveState?TargetActivation.LiveState(session.Items[index-session.StockCount].Layout,masks):TargetActivation.Matches(session.Items[index-session.StockCount].Layout,masks)));
    }
    internal static int[][] NewTargetMasks(RangeSession session,int index,int[][]? supplied=null) {
        if(index>=session.StockCount) {
            var doc=session.Items[index-session.StockCount].Layout;
            if(supplied==null)return TargetActivation.Make(doc,Plugin.Density.Value,random);
            try{return TargetActivation.Enforce(doc,supplied);}catch(ArgumentException){return supplied;} // Normal validation rejects malformed supplied masks.
        }
        var layout=session.Computer.shootHouseLayouts[index];
        return supplied??new[]{NativeRangeContract.Mask(Bits(layout.targets.Length))!,NativeRangeContract.Mask(Bits(layout.civilians.Length))!,NativeRangeContract.Mask(Bits(layout.hostageTakers.Length))!};
    }
    private static IntList List(int[] values){var result=new IntList();foreach(int value in values)result.Add(value);return result;}
    internal static bool ApplyPrepared(RangeSession session,int index,SelectionIdentity identity,int[][] masks,bool liveState=false) {
        if(!ValidateMasks(session,index,masks,liveState)){SelectionLog.Note(identity,"failed","mask-check","Target masks do not match the prepared layout.");return false;}
        if(NetworkServer.active && !MapNightMode.Ready(session.LightingMap(index),out string reason)){Status=reason;SelectionLog.Note(identity,"rejected","map-lighting",reason);return false;}
        var result=Execute(identity,new NativeSelectionSteps(session.Computer,index,()=>session.Prepare(index),Bits,List(masks[0]),List(masks[1]),List(masks[2]),true));
        if(result.Success){try{if(NetworkServer.active)MapNightMode.Apply(session.LightingMap(index));}catch(Exception error){SelectionLog.Note(identity,"failed","map-lighting",error.ToString());Status=error.GetBaseException().Message;return false;}Status=$"House {identity.House}: loaded {identity.Layout}.";return true;}
        quarantined[session.InstanceKey]=session.Computer;Status=$"House {identity.House}: {identity.Layout} failed at {result.Stage}.";return false;
    }
    private static void TickSelections() {
        foreach(var pending in pendingSelections.Values.ToArray()) {
            var session=pending.Session;
            if(!Eligible() || !session.Computer || Time.unscaledTime-pending.Started>30 || session.BuildError(pending.Index)!=null) {
                CoopNetwork.FinishQueuedLoad(session.House,session.LayoutId(pending.Index),false);
                SelectionLog.Note(pending.Identity,"failed","build",session.BuildError(pending.Index)??"Preparation interrupted or timed out.");pendingSelections.Remove(pending.Identity.Instance);session.CancelStage();Detach(session,"build-failed");continue;
            }
            if(!session.IsPrepared(pending.Index))continue;
            int[][] masks=NewTargetMasks(session,pending.Index,pending.Masks);
            pendingSelections.Remove(pending.Identity.Instance);
            bool success=ApplyPrepared(session,pending.Index,pending.Identity,masks);
            CoopNetwork.FinishQueuedLoad(session.House,session.LayoutId(pending.Index),success);
            if(!success)Detach(session,"selection-failed:"+pending.Identity.Attempt);else session.TrimCache(pending.Index);
            break;
        }
    }
    internal static void SuspendCustom(string reason) {
        pendingResets.Clear();
        CoopNetwork.Abort(reason);
        foreach(var pending in pendingSelections.Values)SelectionLog.Note(pending.Identity,"rejected","cancelled",reason);
        pendingSelections.Clear();foreach(var session in Sessions.Values)session.CancelStage();RestoreAll();Close();Status=reason;
        CoopLog.Event("stock-fallback",new{reason});
    }
    private static SelectionResult Execute(SelectionIdentity identity,NativeSelectionSteps steps) {
        bool previous=transition;transition=true;activeSelections.Add(identity.Instance,steps);
        try {return SelectionProtocol.Execute(identity,steps,SelectionLog.Emit);}
        finally {activeSelections.Remove(identity.Instance);transition=previous;}
    }
    private static Exception? LayoutFinalizer(RangeComputer __instance,Exception? __exception) {
        CircuitFixtures.GeometryChanged();
        if(__exception!=null && __instance && initializationGate!=null && initializationInstance==__instance.GetInstanceID() && initializationGate.Record(__exception))return null;
        if(__exception!=null && __instance && activeSelections.TryGetValue(__instance.GetInstanceID(),out var active) && active.Gate.Record(__exception))return null;
        return __exception;
    }
    private static bool BeforeReset(RangeComputer __instance,ref ResetTicket? __state) {
        __state=null;if(!__instance)return false;
        int key=__instance.GetInstanceID();if(activeSelections.TryGetValue(key,out var active))return active.Gate.Active;
        Sessions.TryGetValue(key,out var session);
        if(session==null && !quarantined.ContainsKey(key))return true;
        var identity=SelectionLog.Identity(__instance,__instance.activeLayoutIndex,"native-reset",session);
        try {
            if(stopping || !(Eligible() || CoopNetwork.ClientReady) || CoopNetwork.Switching) {SelectionLog.Note(identity,"rejected","reset","Range is stopping, switching, or customization is unavailable.");return false;}
            session?.RepairStockDoors("reset");NativeRangeContract.Validate(__instance,__instance.activeLayoutIndex);
            __state=new(identity);SelectionLog.Note(identity,"reset-begin","reset","");return true;
        } catch(Exception error) {SelectionLog.Note(identity,"failed","reset-preflight",error.ToString());pendingFaults[key]=error;return false;}
    }
    private static void AfterReset(RangeComputer __instance,ResetTicket? __state) {
        if(__instance && (__state!=null || activeSelections.ContainsKey(__instance.GetInstanceID())) && Sessions.TryGetValue(__instance.GetInstanceID(),out var propSession)){propSession.ResetProps();propSession.ResetPower();propSession.ResetSse();propSession.ResetDoorSwingProtection();}
        if(__state==null)return;
        if(Sessions.TryGetValue(__instance.GetInstanceID(),out var encounterSession))encounterSession.ClearEncounter();
        try {
            pendingResets[__instance.GetInstanceID()]=new(__instance,__state,new ResetVerification(Time.unscaledTime),Time.frameCount);
            VerifyPendingReset(__instance.GetInstanceID());
        }
        catch(Exception error) {SelectionLog.Note(__state.Identity,"failed","reset-verify",error.ToString());pendingFaults[__instance.GetInstanceID()]=error;}
    }
    private static void AfterStopTimer(RangeComputer __instance){if(__instance)VerifyPendingReset(__instance.GetInstanceID());}
    private static void VerifyPendingReset(int key) {
        if(!pendingResets.TryGetValue(key,out var pending))return;
        var state=NativeRangeContract.ResetState(pending.Computer);var outcome=pending.Verification.Check(state,Time.unscaledTime);
        if(outcome==ResetOutcome.Pending)return;
        pendingResets.Remove(key);
        if(outcome==ResetOutcome.Complete){SelectionLog.Note(pending.Ticket.Identity,"reset-complete","reset","Timer and scoring state verified after native reset/stop ordering.");return;}
        var error=new InvalidOperationException("Native reset/stop did not settle within two seconds: "+state);
        SelectionLog.Note(pending.Ticket.Identity,"failed","reset-verify",error.Message);pendingFaults[key]=error;
    }
    private static Exception? ResetFinalizer(RangeComputer __instance,ResetTicket? __state,Exception? __exception) {
        if(__exception==null)return null;
        if(__instance && activeSelections.TryGetValue(__instance.GetInstanceID(),out var active) && active.Gate.Record(__exception))return null;
        if(__state!=null && __instance) {SelectionLog.Note(__state.Identity,"failed","reset",__exception.ToString());pendingFaults[__instance.GetInstanceID()]=__exception;return null;}
        return __exception;
    }
    private static IntList Bits(int count) {var bits=new IntList();foreach(int bit in SelectionPolicy.Activation(count,Plugin.Density.Value,random))bits.Add(bit);return bits;}
    private static IntList All(int count) {var bits=new IntList();for(int n=0;n<count;n++)bits.Add(1);return bits;}
    private static void Detach(RangeSession session,string reason="restore") {
        if(transition)return;transition=true;
        try {
            var computer=session.Computer;
            // Restore the native array before rebuilding a valid stock selection.
            session.Dispose();Sessions.Remove(session.InstanceKey);
            // A disconnect/quit can leave managed wrappers alive while the
            // native range UI and targets are already being destroyed.
            if(!stopping && (NetworkServer.active || NetworkClient.active) && computer && computer.gameObject.scene.isLoaded && computer.shootHouseLayouts.Length>0) {
                int index=-1;string unavailable="No usable stock layout.";
                for(int n=0;n<computer.shootHouseLayouts.Length;n++) {
                    try {NativeRangeContract.Validate(computer,n);index=n;break;}
                    catch(Exception error) {unavailable=error.GetBaseException().Message;}
                }
                var identity=SelectionLog.Identity(computer,index,"stock-recovery:"+reason,session);
                if(index<0) {SelectionLog.Note(identity,"recovery-skipped","preflight",unavailable);return;}
                var result=Execute(identity,new NativeSelectionSteps(computer,index,()=>{},All));
                if(!result.Success) {quarantined[session.InstanceKey]=computer;return;}
                if(NetworkServer.active && RemoteCount()>0){computer.RPC_UpdateLayout(index,computer._targetindexdata,computer._civeindexdata,computer._hostagetakerindexdata);computer.RPC_StopTimer();computer.RPC_ResetRange();}
            }
            else SelectionLog.Note(SelectionLog.Identity(computer,-1,"restore:"+reason,session),"recovery-skipped","stopping","Original arrays and doors restored; native UI calls skipped during shutdown/unload.");
        } catch(Exception error) {SelectionLog.Note(SelectionLog.Identity(session.Computer,-1,"restore:"+reason,session),"failed","restore-stock",error.ToString());}
        finally{Sessions.Remove(session.InstanceKey);transition=false;CircuitFixtures.GeometryChanged();}
    }
    private static void Prune() {
        foreach(var session in Sessions.Values.Where(s=>!s.Computer).ToArray())Detach(session,"scene-unload");
        foreach(int key in quarantined.Where(p=>!p.Value).Select(p=>p.Key).ToArray())quarantined.Remove(key);
    }
    internal static void Reload(bool enteringTraining=false) {
        if(!enteringTraining && NetworkServer.active && RemoteCount()>0) {Status="Layout reload is available in solo sessions.";return;}
        RangeSetupFailure="";stockInitialization.Clear();pendingSelections.Clear();pendingResets.Clear();RestoreAll();quarantined.Clear();pendingFaults.Clear();NativeFurnitureCatalog.Clear();CoopNetwork.SceneReset();Directory.CreateDirectory(ownFolder);
        var folders=new List<string>{ownFolder};
        if(Plugin.Legacy.Value) {string legacy=Path.Combine(Paths.PluginPath,"ExtendedKillhouse","layouts");folders.Add(legacy);folders.Add(Path.Combine(legacy,"built-in (reference copies, not loaded)"));}
        CatalogState=FilterDeletedMaps(Catalog.Load(folders,Plugin.Strict.Value));
        foreach(string note in CatalogState.Notes)Plugin.Logger.LogWarning(note);
        WriteCatalogManifest();
        Status=$"{CatalogState.Items.Count} validated layouts; {CatalogState.Notes.Count} notes.";
        if(CanAttach())foreach(var computer in UnityEngine.Object.FindObjectsOfType<RangeComputer>())Attach(computer);
    }
    internal static void Update() {
        UpdateTrainingLifetime();
        if(stopping){UpdateMenuAccess();return;}
        string stage="draft-save";int house=0;long began=PerformanceJournal.Begin(),stageBegan=began,allocated=PerformanceJournal.Allocated(),stageAllocated=allocated;
        try {
            editor?.Persist();
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="native-initialization";
            NativeBreakerAssets.Tick();NativeSseAssets.Tick();NativeFixtureAssets.Tick();NativeGearAssets.Tick();TickStockInitialization();OutdoorGearLocker.Tick();
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="encounter-residue";
            EncounterResidue.Tick();
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="damage-resources";
            NativeDamageResources.Tick();
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="performance-sample";
            PerformanceJournal.Tick();
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="co-op";
            CoopNetwork.Tick();
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="reset-verification";
            foreach(var entry in pendingResets.Where(p=>Time.frameCount>p.Value.Frame).ToArray())VerifyPendingReset(entry.Key);
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="editor-permission";
            if(editor?.Visible==true && !CanEdit){editor.Hide();Status="Map editing permission is unavailable. Your draft is preserved.";}
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="range-build";
            var budget=new FrameBudget();foreach(var session in Sessions.Values){house=session.House;session.TickBuild(budget);}house=0;
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="deferred-release";DeferredRelease.Tick(budget);
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="map-selection";
            TickSelections();
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="encounter";
            foreach(var session in Sessions.Values){house=session.House;session.TickEncounter();}house=0;
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="triggers";
            foreach(var session in Sessions.Values){house=session.House;session.TickTriggers();}house=0;
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="lighting";
            TickPower();foreach(var sseSession in Sessions.Values)sseSession.TickSse();
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="prop-physics";
            PropPhysics.Prune();foreach(var session in Sessions.Values){house=session.House;session.TickProps();}house=0;
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="native-fault-recovery";
            foreach(var(key,error) in pendingFaults.ToArray()) {
                pendingFaults.Remove(key);
                if(Sessions.TryGetValue(key,out var failed)) {quarantined[key]=failed.Computer;if(RemoteCount()>0 || !NetworkServer.active)CoopNetwork.NativeFault(failed,error);else Detach(failed,"native-reset-failed");}
            }
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="range-discovery";
            if(Time.unscaledTime>=nextScan) {
                nextScan=Time.unscaledTime+.5f;Prune();
                if(!CanAttach() && !CoopNetwork.PreservingJoin) {if(Sessions.Count>0)SuspendCustom(CoopNetwork.Status);Close();Status=NetworkServer.active && RemoteCount()>0?CoopNetwork.Status:"Enter an Outdoors range to use custom layouts.";}
                else if(Sessions.Count<2){foreach(var computer in UnityEngine.Object.FindObjectsOfType<RangeComputer>())Attach(computer);}
                PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="lighting-discovery";
                if(MapNightMode.Active){var computers=Sessions.Values.Select(s=>s.Computer).Concat(quarantined.Values);LaptopLighting.Refresh(computers);SpawnLighting.Refresh(computers);}
                PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="range-discovery";
                focused=null;Camera camera=Camera.main;
                if(camera)focused=Sessions.Values.Select(s=>s.Computer).Concat(quarantined.Values).Where(c=>c && (c.transform.position-camera.transform.position).sqrMagnitude<36)
                    .OrderBy(c=>(c.transform.position-camera.transform.position).sqrMagnitude).FirstOrDefault();
                if(Visible && !focused)Close();
            }
            PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);stage="menu-key";UpdateMenuAccess();
        } catch(Exception error) {UpdateFailed(stage,error,house);}
        finally {PerformanceJournal.Stage(stage,ref stageBegan,ref stageAllocated);PerformanceJournal.Complete("update",began,allocated);}
    }
    private static bool GameplayInputBlocked=>EditorWheel.BlocksGameplay(Visible,Time.frameCount,suppressInputThrough);
    internal static bool CanOpenGear=>!stopping && Application.isFocused && !GameplayInputBlocked && !Cursor.visible && !InputLayer.InputDisabled && !CharacterCustomisationController.isCustomising;
    internal static void CloseForGear()=>Close();
    internal static void SuppressGearCloseClick()=>suppressInputThrough=Time.frameCount+2;
    private static bool BeforeKey(ref bool __result) {if(!GameplayInputBlocked)return true;__result=false;return false;}
    private static bool BeforeMouse(ref Vector2 __result) {if(!GameplayInputBlocked)return true;__result=Vector2.zero;return false;}
    private static bool BeforeScrollAxis(string __0,ref float __result) {if(!EditorWheel.BlocksAxis(GameplayInputBlocked,__0))return true;__result=0;return false;}
    internal static void GuardCursor() {if(Visible) {Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}}
    private static void Close() {pendingDeleteName=null;editor?.Hide();proposalPreview?.Hide();if(!Visible)return;Visible=false;suppressInputThrough=Time.frameCount+1;Cursor.lockState=savedLock;Cursor.visible=savedCursor;}
    internal static void Draw() {
        long began=PerformanceJournal.Begin(),allocated=PerformanceJournal.Allocated();
        try {
        try{DrawMenuNotice();}catch(Exception error){menuNotice.Reset();Plugin.Logger.LogWarning("Killhouse notice: "+error.GetBaseException().Message);}
        try {
            if(NetworkServer.active && !Visible && CoopNetwork.LoadRequests.Count>0)GUI.Box(new Rect(18,90,430,54),$"Killhouse: {CoopNetwork.LoadRequests.Count} map load request(s) awaiting approval.\nOpen {Plugin.PanelKey.Value} at a range laptop to review.");
            if(Visible && focused){if(ActiveEditor is {} activeEditor)activeEditor.Draw();else {
                window.width=Math.Min(600,Screen.width-30);window.height=Math.Min(720,Screen.height-30);
                window.x=Math.Clamp(window.x,15,Screen.width-window.width-15);window.y=Math.Clamp(window.y,15,Screen.height-window.height-15);
                window=GUI.Window(0x4B4801,window,(GUI.WindowFunction)Window,"Operator Killhouse");
            }}
        }
        catch(Exception error){RecordFailure("range-menu-draw",error,false);Status="Range panel: "+error.GetBaseException().Message;Close();}
        }finally{PerformanceJournal.Complete("gui",began,allocated);}
    }
    private static void Window(int id) {
        try{DrawPanelContents(id);}catch(Exception error){EditorFailed(error);}finally{GUI.enabled=true;}
    }
    internal static int EditorHouse=>proposalPreview?.Visible==true?previewHouse:focused is {} current && current && Sessions.TryGetValue(current.GetInstanceID(),out var session)?session.House:0;
    internal static IReadOnlyDictionary<Edge,DoorAccess> PreviewDoorAccess(Layout doc)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.PreviewDoorAccess(doc)??new Dictionary<Edge,DoorAccess>();
    internal static IReadOnlyList<Issue> PreviewTargetIssues(Layout doc)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.PreviewTargetIssues(doc)??TargetPlacement.Check(doc);
    internal static IReadOnlyList<Issue> PreviewTargetEditIssues(Layout doc,TargetRef key)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.PreviewTargetEditIssues(doc,key)??TargetPlacement.Check(doc,only:key);
    internal static int EditorPreviewContext=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.InstanceKey??0;
    internal static bool StandlessAvailable=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.StandlessAvailable??false;
    internal static bool StandlessAvailableFor(TargetRole role)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.StandlessAvailableFor(role)??false;
    internal static IReadOnlyList<BarrierSegment> PreviewArchitecture(Layout doc)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.PreviewArchitecture(doc)??WallJunctions.Make(doc,.60f,.60f).Build.Segments;
    internal static PlacedProp AlignEditorFixture(Layout doc,PlacedProp prop,PropShape shape)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.AlignFixture(doc,prop,shape)??prop;
    internal static Layout SnapEditorFixtures(Layout doc)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.SnapLoadedFixtures(doc)??doc;
    internal static Target AlignEditorTarget(Layout doc,Target target)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.AlignEditorTarget(doc,target)??target;
    internal static IReadOnlyDictionary<string,PropShape> EditorPropShapes=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.EditorPropShapes??new Dictionary<string,PropShape>();
    internal static FurnitureResult? PreviewFurniture(Layout doc)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.PreviewFurniture(doc);
    internal static IReadOnlyList<Issue> PreviewPropIssues(Layout doc)=>Sessions.Values.FirstOrDefault(s=>s.House==EditorHouse)?.PreviewPropIssues(doc)??(doc.Props.Any(p=>p.Id>0)?new[]{new Issue("Native furniture measurements are not ready. Return to the range laptop.",true)}:Array.Empty<Issue>());
    private static void OpenEditor(Layout? copy=null) {
        if(!CanEdit)return;
        editor??=new MapEditor(Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../UserData/OperatorKillhouse/editor-draft.txt")),SaveEdited);
        editor.Open(copy);
    }
    private static string SaveEdited(Layout doc,bool walk) {
        if(!CanSaveEdited)throw new InvalidOperationException("Wait for the current selection or map push to finish before saving another revision.");
        if(walk && !CanPushEdited)throw new InvalidOperationException("Wait for all players to have matching Killhouse and range assets before pushing this map.");
        doc=LayoutDraft.Copy(doc);
        var furnitureIssues=PreviewPropIssues(doc);if(furnitureIssues.Any(i=>i.Fatal))throw new InvalidOperationException(string.Join(" ",furnitureIssues.Select(i=>i.Message)));
        var computer=focused;if(!computer)throw new InvalidOperationException("Return to the range laptop.");
        int house=computer!.name.TrimEnd().EndsWith('2')?2:1;
        if(walk && (doc.Houses&house)==0)throw new InvalidOperationException("Enable this house in the draft before walking through it.");
        if(EditingAsGuest)return SaveGuestRevision(doc,walk,computer,house);
        return SaveHostRevision(doc,walk,computer,"editor-push");
    }
    private static string SaveHostRevision(Layout doc,bool walk,RangeComputer computer,string source,bool persist=true) {
        if(!NetworkServer.active || !CanSaveEdited || walk && !CanPushEdited)throw new InvalidOperationException("Only the ready host may register and load a shared revision.");
        if(doc.Props.Any(p=>p.Id>0)) {
            if(!Sessions.TryGetValue(computer.GetInstanceID(),out var nativeSession))throw new InvalidOperationException("Return to the range laptop to validate furniture.");
            var issues=nativeSession.PreviewPropIssues(doc);if(issues.Any(i=>i.Fatal))throw new InvalidOperationException(string.Join(" ",issues.Select(i=>i.Message)));
        }
        int house=computer.name.TrimEnd().EndsWith('2')?2:1;
        // Append immutable slots instead of reloading ranges or changing the
        // lobby nonce. The old active revision stays intact during preparation.
        var candidate=Catalog.Publish(CatalogState,doc,"editor revision",Plugin.Strict.Value);
        if(walk && EditingWithPeers)LayoutTransfer.Packets(doc,new(CoopKind.LayoutChunk,1,1,1,1,house,computer.netId,0));
        string file=persist?NamedMaps.Save(doc,ownFolder,Plugin.Strict.Value):"generated session layout",identity=doc.Identity;
        if(persist)MapDeletion.Unhide(doc.Name,LibraryData);
        var item=candidate.Items.Single(i=>i.Id==identity) with{Origin=file};
        foreach(var attached in Sessions.Values)attached.AppendAuthored(item);
        CatalogState=candidate with{Items=candidate.Items.Select(i=>i.Id==identity?item:i).ToArray()};
        WriteCatalogManifest();Status=persist?"Saved "+Path.GetFileName(file):"Preparing generated combat layout.";
        CoopLog.Event("editor-revision-saved",new{layout=doc.Name,id=identity,peers=RemoteCount(),push=walk});
        if(walk && Sessions.TryGetValue(computer.GetInstanceID(),out var session)) {
            int custom=session.Items.ToList().FindIndex(i=>i.Id==identity);
            if(custom<0)throw new InvalidOperationException("Saved file did not enter the catalog. Check import notes.");
            int index=session.StockCount+custom;
            if(EditingWithPeers){Select(computer,index,source);if(!CoopNetwork.Busy)throw new InvalidOperationException(session.BuildError(index)??"The revision was saved, but the group could not begin the push. Wait for co-op ready and retry.");}
            else {
                // A solo walkthrough previews all stands without changing density.
                var masks=new[]{doc.AllTargets.Count(t=>t.Role==TargetRole.Threat),doc.AllTargets.Count(t=>t.Role==TargetRole.Civilian),doc.AllTargets.Count(t=>t.Role==TargetRole.Hostage)};
                Select(computer,index,"editor-walkthrough",All(masks[0]),All(masks[1]),All(masks[2]),true);Close();
            }
        }
        return walk && EditingWithPeers?"Pushing updated map to everyone. Wait for co-op ready before starting a fresh run.":!walk?"Saved locally: "+doc.Name+". The current range is unchanged. Saving this name again updates the same map.":"Saved "+doc.Name+". Saving this name again updates the same map.";
    }
    private static void WriteCatalogManifest() {
        if(!DiagnosticLog.Enabled)return;
        try {
            string data=Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../UserData/OperatorKillhouse"));Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data,"catalog.json"),JsonSerializer.Serialize(new{schema=1,CatalogState.Manifest,layouts=NamedMaps.Visible(CatalogState.Items).Select(i=>new{i.Id,i.Origin,i.Layout.Name}),notes=CatalogState.Notes},new JsonSerializerOptions{WriteIndented=true}));
        }catch(Exception error){Plugin.Logger.LogWarning("Catalog report: "+error.GetBaseException().Message);}
    }
    internal static void EditorFailed(Exception error) {RecordFailure("menu-callback",error,false);Status="Map editor closed: "+error.GetBaseException().Message;Close();}
    private static void AfterCombatantStart(BrainAI __instance){foreach(var session in Sessions.Values)session.CombatantStarted(__instance);}
    private static bool DriveEncounterCivilian(BrainAI brain) {
        if(!NetworkServer.active)return false;
        foreach(var session in Sessions.Values)try{if(session.DriveCivilian(brain))return true;}catch(Exception error){session.ClearEncounter();Status="Civilian reaction failed: "+error.GetBaseException().Message;Plugin.Logger.LogError(error);return true;}
        return false;
    }
    private static void BeforeEncounterBrainTick(BrainAI __instance){if(NetworkServer.active)foreach(var session in Sessions.Values){session.FilterEncounterSight(__instance);session.MaintainCautious(__instance);}}
    private static void AfterEncounterBrainTick(BrainAI __instance){BeforeEncounterBrainTick(__instance);DriveEncounterCivilian(__instance);}
    private static void BeforeEncounterVision(EyesAI __instance,ref EncounterLighting.VisionScope? __state){
        if(NetworkServer.active && __instance.brain)BeforeEncounterBrainTick(__instance.brain);
        __state=EncounterLighting.Begin(__instance,MapNightMode.Current,__instance.brain && Sessions.Values.Any(s=>s.OwnsCombatant(__instance.brain)),NetworkServer.active);
    }
    private static void AfterEncounterVision(EyesAI __instance,EncounterLighting.VisionScope? __state){__state?.Dispose();if(NetworkServer.active && __instance.brain)BeforeEncounterBrainTick(__instance.brain);}
    private static Exception? EncounterVisionFinalizer(EncounterLighting.VisionScope? __state,Exception? __exception){
        try{__state?.Dispose();}catch(Exception error){Plugin.Logger.LogWarning("AI vision restoration: "+error);return __exception??error;}return __exception;
    }
    private static bool ExcludedSight(EyesAI eyes,Transform? target)=>NetworkServer.active && eyes.brain && Sessions.Values.Any(s=>s.ExcludesSight(eyes.brain,target) || s.OwnsCombatant(eyes.brain) && !EncounterLighting.Allows(eyes,target,MapNightMode.Current));
    private static bool BeforeEncounterSight(EyesAI __instance,Transform __1,ref bool __result) {
        if(!ExcludedSight(__instance,__1))return true;__result=false;return false;
    }
    private static void AfterEncounterSight(EyesAI __instance,Transform __1,bool __result){if(__result && NetworkServer.active && __instance.brain)foreach(var session in Sessions.Values)session.ObserveCautiousSight(__instance.brain,__1);}
    private static bool BeforeEncounterTargetScore(EyesAI __instance,Transform __0,ref float __result) {
        if(!ExcludedSight(__instance,__0))return true;__result=float.NegativeInfinity;return false;
    }
    private static void AfterEncounterFindTarget(EyesAI __instance,ref Transform? __result) {
        if(!ExcludedSight(__instance,__result))return;
        __result=__instance.FindClosestsTarget();if(ExcludedSight(__instance,__result))__result=null;
    }
    private static bool BeforeEncounterState(BrainAI __instance)=>!DriveEncounterCivilian(__instance);
    private static void BeforeCivilianWeapons(WeaponsAI __instance){if(NetworkServer.active && __instance.brain && Sessions.Values.Any(s=>s.IsReactionCivilian(__instance.brain)))EncounterCivilians.Disarm(__instance.brain);}
    private static void BeforeEncounterDestination(Pathfinding.FollowerEntity __instance,ref Vector3 __0){if(NetworkServer.active)foreach(var session in Sessions.Values)if(session.FilterEncounterDestination(__instance,ref __0))return;}
    private static void BeforeEncounterDoor(DoorV2 __instance){if(NetworkServer.active && __instance)foreach(var session in Sessions.Values)session.BeforeEncounterDoor(__instance);}
    private static void BeforeEncounterDamage(Health __instance,float __0,int __3,out EncounterDamage.SafetyScope? __state) {
        __state=null;
        if(!NetworkServer.active || !Plugin.CombatAI.Value || __3<=0 || !NetworkServer.spawned.TryGetValue((uint)__3,out var shooter) || !shooter)return;
        var brain=shooter.GetComponent<BrainAI>();var target=__instance?__instance.brainAI:null;
        bool ownedShooter=brain && Sessions.Values.Any(s=>s.OwnsCombatant(brain));
        bool ownedTarget=target && Sessions.Values.Any(s=>s.OwnsCombatant(target));
        bool playerShooter=shooter.GetComponent<PlayerMaster>() || shooter.GetComponent<Health>() is {} health && !health.isBot;
        __state=EncounterDamage.Begin(__instance,__0,ownedShooter,ownedTarget,playerShooter,true);
    }
    private static Exception? AfterEncounterDamage(Exception? __exception,EncounterDamage.SafetyScope? __state){__state?.Dispose();return __exception;}
    private static bool BeforeCombatantPatrol(BrainAI __instance){foreach(var session in Sessions.Values)try{if(session.DriveCombatantPatrol(__instance))return false;}catch(Exception error){session.ClearEncounter();Status="AI route failed: "+error.GetBaseException().Message;Plugin.Logger.LogError(error);return false;}return true;}
}

