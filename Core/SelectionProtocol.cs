using System.Diagnostics;

namespace OperatorKillhouse.Core;

public sealed record SelectionIdentity(long Attempt,int House,string Range,int Instance,string Scene,int Index,string Layout,string LayoutId,string Source);
public sealed record SelectionEvent(DateTimeOffset Time,string Event,string Stage,SelectionIdentity Selection,string Detail,long ElapsedMs);
public sealed record SelectionResult(bool Success,string Stage,Exception? Error);
public interface ISelectionSteps
{
    void Prepare();void Validate();void StopTimer();void Activate();void Update();void Reset();void Verify();
    string Summary { get; }
}
public static class SelectionProtocol
{
    public static SelectionResult Execute(SelectionIdentity identity,ISelectionSteps steps,Action<SelectionEvent> emit)
    {
        var watch=Stopwatch.StartNew();string stage="begin";
        void Log(string outcome,string detail)=>emit(new(DateTimeOffset.UtcNow,outcome,stage,identity,detail,watch.ElapsedMilliseconds));
        Log("begin","");
        try {
            foreach(var(name,action) in new (string,Action)[]{("prepare",steps.Prepare),("preflight",steps.Validate),("stop-timer",steps.StopTimer),("activate",steps.Activate),("update",steps.Update),("reset",steps.Reset),("verify",steps.Verify)}) {
                stage=name;Log("stage","");action();
            }
            stage="complete";Log("complete",steps.Summary);return new(true,stage,null);
        } catch(Exception error) {Log("failed",error.ToString());return new(false,stage,error);}
    }
}

// IL2CPP's patched native->managed bridge can swallow an exception after logging
// it. The finalizer records it here so callers cannot mistake return for success.
public sealed class NativeCallGate
{
    private Exception? fault;
    public bool Active { get; private set; }
    public bool Record(Exception error) {if(!Active)return false;fault??=error;return true;}
    public void Invoke(string name,Action call) {
        if(Active)throw new InvalidOperationException("Nested native selection call.");
        fault=null;Active=true;
        try {call();if(fault!=null)throw new InvalidOperationException("Native "+name+" failed.",fault);}
        finally {Active=false;}
    }
}
public static class ReferenceRepair
{
    public static T[] Reconcile<T>(IEnumerable<T?>? configured,IEnumerable<T> hierarchy,Func<T,bool> alive,Func<T,int> key) where T:class
    {
        var found=hierarchy.Where(alive).GroupBy(key).ToDictionary(g=>g.Key,g=>g.First());
        var result=new List<T>();var seen=new HashSet<int>();
        foreach(var candidate in configured??Array.Empty<T>())
            if(candidate!=null && alive(candidate) && found.ContainsKey(key(candidate)) && seen.Add(key(candidate)))result.Add(candidate);
        foreach(var candidate in found.Values)if(seen.Add(key(candidate)))result.Add(candidate);
        return result.ToArray();
    }
}
public sealed record LayoutContract(bool Root,bool[]? Threats,bool[]? Civilians,bool[]? Hostages,bool[]? Doors);
public static class RangePreflight
{
    public static void Validate(IReadOnlyList<bool> roots,int index,LayoutContract layout,bool ui,bool hitLists,int[]?[]? masks=null)
    {
        if(index<0 || index>=roots.Count)throw new InvalidOperationException("Layout index is outside the installed array.");
        for(int n=0;n<roots.Count;n++)if(!roots[n])throw new InvalidOperationException($"Layout root at index {n} is missing/destroyed.");
        if(!layout.Root)throw new InvalidOperationException("Selected layout root is missing/destroyed.");
        var arrays=new[]{layout.Threats,layout.Civilians,layout.Hostages,layout.Doors};
        string[] names={"targets","civilians","hostageTakers","doors"};
        for(int kind=0;kind<arrays.Length;kind++) {
            var array=arrays[kind]??throw new InvalidOperationException(names[kind]+" array is null.");
            for(int n=0;n<array.Length;n++)if(!array[n])throw new InvalidOperationException($"{names[kind]}[{n}] is missing/destroyed.");
            if(kind<3 && masks!=null && (masks.Length!=3 || masks[kind]==null || masks[kind]!.Length!=array.Length || masks[kind]!.Any(b=>b is not (0 or 1))))
                throw new InvalidOperationException(names[kind]+" activation mask does not match the live targets.");
        }
        if(!ui)throw new InvalidOperationException("Native timer/score UI is not ready.");
        if(!hitLists)throw new InvalidOperationException("Native target/civilian hit lists are not ready.");
    }
}
