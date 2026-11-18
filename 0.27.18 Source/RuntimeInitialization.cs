using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using IntList=Il2CppSystem.Collections.Generic.List<int>;

namespace OperatorKillhouse;
internal static class NativeStockRepair {
    internal static bool Applies(RangeComputer c)=>c && c.name.StartsWith("Range Computer ShootHouse",StringComparison.OrdinalIgnoreCase);
    internal static void Repair(RangeComputer c) {
        if(!Applies(c) || c.shootHouseLayouts==null)return;
        for(int n=0;n<Math.Min(3,c.shootHouseLayouts.Length);n++) {
            var entry=c.shootHouseLayouts[n];if(entry==null || !entry.layout)continue;
            var old=entry.doors;var hierarchy=entry.layout.GetComponentsInChildren<DoorV2>(true).ToArray();
            var repaired=ReferenceRepair.Reconcile(old,hierarchy,d=>d is not null && d,d=>d.GetInstanceID());
            if(old!=null && old.Length==repaired.Length && old.Select((d,k)=>d && d.Pointer==repaired[k].Pointer).All(b=>b))continue;
            var array=new Il2CppReferenceArray<DoorV2>(repaired.Length);for(int k=0;k<array.Length;k++)array[k]=repaired[k];entry.doors=array;
            CoopLog.Event("stock-initialization-repair",new{range=c.name,index=n,doors=array.Length});
        }
    }
}
internal static partial class Runtime {
    private sealed record StockInitialization(RangeComputer Computer,int Index,IntList? Threats,IntList? Civilians,IntList? Hostages,float Started);
    private static readonly Dictionary<int,StockInitialization> stockInitialization=new();
    private static NativeCallGate? initializationGate;
    private static int initializationInstance;
    private static float nextInitialization;
    private static void BeforeAwake(RangeComputer __instance){if(!Plugin.Enabled.Value || !NativeStockRepair.Applies(__instance))return;rangeScenes.Add(__instance.gameObject.scene.handle);try{NativeStockRepair.Repair(__instance);}catch(Exception error){CoopLog.Event("stock-initialization-waiting",new{reason=error.GetBaseException().Message});}}
    private static bool ReadyForAttachment(RangeComputer c) {
        if(stockInitialization.ContainsKey(c.GetInstanceID()))return false;
        try{NativeStockRepair.Repair(c);NativeRangeContract.Validate(c,Math.Clamp(c.activeLayoutIndex,0,Math.Min(2,c.shootHouseLayouts.Length-1)));return true;}
        catch{return false;}
    }
    private static bool BeforeStockLayout(RangeComputer c,int index,IntList? threats,IntList? civilians,IntList? hostages) {
        if(!Plugin.Enabled.Value || !NativeStockRepair.Applies(c))return true;
        if(index<0 || index>=Math.Min(3,c.shootHouseLayouts?.Length??0))return false;
        try{NativeStockRepair.Repair(c);NativeRangeContract.Validate(c,index,threats,civilians,hostages,true);stockInitialization.Remove(c.GetInstanceID());return true;}
        catch(Exception error){
            IntList? Copy(IntList? source)=>source==null?null:List(NativeRangeContract.Mask(source)!);
            stockInitialization[c.GetInstanceID()]=new(c,index,Copy(threats),Copy(civilians),Copy(hostages),Time.unscaledTime);
            CoopLog.Event("stock-initialization-deferred",new{range=c.name,index,reason=error.GetBaseException().Message});return false;
        }
    }
    private static void TickStockInitialization() {
        if(Time.unscaledTime<nextInitialization)return;nextInitialization=Time.unscaledTime+.25f;
        foreach(var(key,job) in stockInitialization.ToArray()) {
            if(!Plugin.Enabled.Value || !job.Computer || Time.unscaledTime-job.Started>30){stockInitialization.Remove(key);if(job.Computer && Plugin.Enabled.Value)CoopLog.Event("stock-initialization-timeout",new{range=job.Computer.name,index=job.Index});continue;}
            try{NativeStockRepair.Repair(job.Computer);NativeRangeContract.Validate(job.Computer,job.Index,job.Threats,job.Civilians,job.Hostages,true);}catch{continue;}
            stockInitialization.Remove(key);initializationInstance=key;initializationGate=new();
            try{initializationGate.Invoke("native-stock-initialization",()=>job.Computer.UpdateLayout(job.Index,job.Threats!,job.Civilians!,job.Hostages!));CoopLog.Event("stock-initialization-complete",new{range=job.Computer.name,index=job.Index});}
            catch(Exception error){Status="Stock initialization: "+error.GetBaseException().Message;CoopLog.Event("stock-initialization-failed",new{range=job.Computer.name,error=error.ToString()});}
            finally{initializationGate=null;initializationInstance=0;}
        }
    }
}
