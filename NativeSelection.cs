using BepInEx;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using OperatorKillhouse.Core;
using UnityEngine;
using Mirror;
using System.Diagnostics;
using IntList=Il2CppSystem.Collections.Generic.List<int>;

namespace OperatorKillhouse;

internal static class SelectionLog
{
    internal static readonly string Run=Guid.NewGuid().ToString("N");
    private static long sequence;
    private static bool journalWarning;
    internal static SelectionIdentity Identity(RangeComputer computer,int index,string source,RangeSession? session=null)
    {
        string range=session?.RangeName??(computer?computer.name:"destroyed range");
        int house=session?.House??(range.TrimEnd().EndsWith('2')?2:1),instance=session?.InstanceKey??(computer?computer.GetInstanceID():0);
        string scene=session?.SceneName??(computer?computer.gameObject.scene.name:"unloaded");
        string label="invalid index",id="";
        if(computer && computer.shootHouseLayouts!=null && index>=0 && index<computer.shootHouseLayouts.Length && computer.shootHouseLayouts[index]!=null) {
            label=computer.shootHouseLayouts[index].LayoutName;
            if(session!=null && index>=session.StockCount && index-session.StockCount<session.Items.Count)id=session.Items[index-session.StockCount].Id;
            else id="stock:"+index;
        }
        return new(System.Threading.Interlocked.Increment(ref sequence),house,range,instance,scene,index,label,id,source);
    }
    internal static void Emit(SelectionEvent entry)
    {
        if(!DiagnosticLog.Enabled && entry.Event!="failed")return;
        string quoted=System.Text.Json.JsonSerializer.Serialize(entry.Selection.Layout);
        string detail=System.Text.Json.JsonSerializer.Serialize(entry.Detail);
        string line=$"selection {entry.Event}: run={Run}; attempt={entry.Selection.Attempt}; house={entry.Selection.House}; range={System.Text.Json.JsonSerializer.Serialize(entry.Selection.Range)}; instance={entry.Selection.Instance}; scene={System.Text.Json.JsonSerializer.Serialize(entry.Selection.Scene)}; index={entry.Selection.Index}; layout={quoted}; id={entry.Selection.LayoutId}; source={entry.Selection.Source}; stage={entry.Stage}; elapsedMs={entry.ElapsedMs}; detail={detail}";
        if(entry.Event=="failed")Plugin.Logger.LogError(line);else Plugin.Logger.LogInfo(line);
        if(!DiagnosticLog.Enabled)return;
        try {
            string folder=Path.Combine(Paths.BepInExRootPath,"../UserData/OperatorKillhouse");Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"selections.jsonl"),System.Text.Json.JsonSerializer.Serialize(new{version=Plugin.Version,run=Run,entry.Time,entry.Event,entry.Stage,entry.Selection,entry.Detail,entry.ElapsedMs})+Environment.NewLine);
        } catch(Exception error) {if(!journalWarning){journalWarning=true;Plugin.Logger.LogWarning("Selection journal unavailable: "+error.GetBaseException().Message);}}
    }
    internal static void Note(SelectionIdentity identity,string outcome,string stage,string detail)=>Emit(new(DateTimeOffset.UtcNow,outcome,stage,identity,detail,0));
}

internal static class NativeRangeContract
{
    private static bool Live(UnityEngine.Object? item)=>item is not null && item;
    private static bool[]? Objects(Il2CppReferenceArray<GameObject>? array)=>array?.Select(Live).ToArray();
    internal static int[]? Mask(IntList? list) {if(list==null)return null;var result=new int[list.Count];for(int n=0;n<result.Length;n++)result[n]=list[n];return result;}
    internal static void Validate(RangeComputer computer,int index,IntList? threats=null,IntList? civilians=null,IntList? hostages=null,bool masks=false)
    {
        if(!computer || !computer.gameObject.scene.isLoaded)throw new InvalidOperationException("Range was destroyed or its scene is unloading.");
        if(!GameManager.instance)throw new InvalidOperationException("Native GameManager is unavailable for the range timer.");
        var layouts=computer.shootHouseLayouts??throw new InvalidOperationException("Native layout array is null.");
        if(index<0 || index>=layouts.Length || layouts[index]==null)throw new InvalidOperationException("Selected native layout entry is missing.");
        var selected=layouts[index];
        var snapshot=new LayoutContract(Live(selected.layout),Objects(selected.targets),Objects(selected.civilians),Objects(selected.hostageTakers),selected.doors?.Select(d=>Live(d)).ToArray());
        RangePreflight.Validate(layouts.Select(l=>l!=null && Live(l.layout)).ToArray(),index,snapshot,
            Live(computer.Timer) && Live(computer.TargetHit) && Live(computer.CivilianHit),computer.goTargetsHit!=null && computer.goCiviliansHit!=null,
            masks?new[]{Mask(threats),Mask(civilians),Mask(hostages)}:null);
    }
    internal static ResetSnapshot ResetState(RangeComputer c)=>!c?default:new(true,c.goTargetsHit!=null && c.goCiviliansHit!=null,c.CountTime,c.Seconds,c.Minutes,c.targetsHit,c.civiliansHit,c.goTargetsHit?.Count??-1,c.goCiviliansHit?.Count??-1);
    internal static void VerifyReset(RangeComputer computer) {
        var state=ResetState(computer);if(!state.Clear)throw new InvalidOperationException("Native reset did not clear the timer and scoring state: "+state);
    }
}

internal sealed class NativeSelectionSteps : ISelectionSteps
{
    internal readonly NativeCallGate Gate=new();
    private readonly RangeComputer computer;
    private readonly int index;
    private readonly Action prepare;
    private readonly Func<int,IntList> activation;
    private readonly bool supplied;
    private IntList? threats,civilians,hostages;
    private ShootHouseLayout? layout;
    private double updateMs,resetMs;
    private double damageMs,doorMs;
    private int damageCalls,doorCalls;
    private readonly HashSet<IntPtr> layoutRoots=new();
    private readonly double[] activationMs=new double[4];
    private readonly int[] activationCalls=new int[4];
    internal int ActivationKind(GameObject item,bool enabled)=>(layoutRoots.Contains(item.Pointer)?2:4)+(enabled?0:1);
    internal void RecordCost(int kind,double milliseconds){if(kind==0){damageMs+=milliseconds;damageCalls++;}else if(kind==1){doorMs+=milliseconds;doorCalls++;}else{activationMs[kind-2]+=milliseconds;activationCalls[kind-2]++;}}
    internal NativeSelectionSteps(RangeComputer range,int selected,Action build,Func<int,IntList> bits,IntList? targets=null,IntList? cives=null,IntList? hostage=null,bool suppliedMasks=false)
    {computer=range;index=selected;prepare=build;activation=bits;threats=targets;civilians=cives;hostages=hostage;supplied=suppliedMasks;foreach(var entry in range.shootHouseLayouts)if(entry!=null && entry.layout is {} root && root)layoutRoots.Add(root.Pointer);}
    public void Prepare() {
        prepare();
        var layouts=computer.shootHouseLayouts;
        if(layouts==null || index<0 || index>=layouts.Length || layouts[index]==null)throw new InvalidOperationException("Layout disappeared during preparation.");
        layout=layouts[index];
        if(!supplied) {
            threats=activation(layout.targets?.Length??0);civilians=activation(layout.civilians?.Length??0);hostages=activation(layout.hostageTakers?.Length??0);
        }
    }
    public void Validate()=>NativeRangeContract.Validate(computer,index,threats,civilians,hostages,true);
    public void StopTimer()=>Gate.Invoke("StopTimer",()=>computer.UserCode_RPC_StopTimer());
    public void Activate() {
        computer._targetindexdata=threats!;computer._civeindexdata=civilians!;computer._hostagetakerindexdata=hostages!;
        if(NetworkServer.active)computer.NetworkactiveLayoutIndex=index;else computer.activeLayoutIndex=index;
    }
    public void Update(){var watch=DiagnosticLog.Enabled?Stopwatch.StartNew():null;try{Gate.Invoke("UpdateLayout",()=>computer.UpdateLayout(index,threats!,civilians!,hostages!));}finally{if(watch!=null)updateMs=watch.Elapsed.TotalMilliseconds;}}
    public void Reset(){var watch=DiagnosticLog.Enabled?Stopwatch.StartNew():null;try{Gate.Invoke("ResetRange",()=>computer.UserCode_RPC_ResetRange());}finally{if(watch!=null)resetMs=watch.Elapsed.TotalMilliseconds;}}
    public void Verify() {
        NativeRangeContract.Validate(computer,index,threats,civilians,hostages,true);
        if(computer.activeLayoutIndex!=index || layout is null || !layout.layout.activeInHierarchy)throw new InvalidOperationException("Native selection did not activate the requested layout.");
        for(int n=0;n<computer.shootHouseLayouts.Length;n++)if(n!=index && computer.shootHouseLayouts[n].layout.activeSelf)throw new InvalidOperationException($"Native layout {n} remained active.");
        NativeRangeContract.VerifyReset(computer);
    }
    public string Summary=>$"targets={layout!.targets.Length}; civilians={layout.civilians.Length}; hostages={layout.hostageTakers.Length}; doors={layout.doors.Length}; activeTargets={NativeRangeContract.Mask(threats)!.Count(b=>b!=0)}; activeCivilians={NativeRangeContract.Mask(civilians)!.Count(b=>b!=0)}; activeHostages={NativeRangeContract.Mask(hostages)!.Count(b=>b!=0)}; nativeUpdateMs={updateMs:F2}; nativeResetMs={resetMs:F2}; damageCleanupMs={damageMs:F2}; damageCleanupCalls={damageCalls}; doorResetMs={doorMs:F2}; doorResetCalls={doorCalls}; rootEnableMs={activationMs[0]:F2}; rootDisableMs={activationMs[1]:F2}; objectEnableMs={activationMs[2]:F2}; objectDisableMs={activationMs[3]:F2}; activationCalls={string.Join(",",activationCalls)}; update+reset verified";
}
