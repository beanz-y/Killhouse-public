namespace OperatorKillhouse.Core;

public enum JoinVerdict { Pending,Matching,Rejected }
public static class JoinSynchronization
{
    public static bool ApplyHost(CoopPacket selection,Func<bool> apply)=>selection.Catchup || apply();
}
public sealed class JoinAdmission
{
    public const double CheckSeconds=30;
    private double loaded=double.NaN;
    private bool matched;
    public JoinVerdict Observe(bool playerLoaded,bool explicitMismatch,bool matching,double now) {
        if(!double.IsFinite(now))return JoinVerdict.Rejected;
        if(!playerLoaded)return matched?JoinVerdict.Rejected:JoinVerdict.Pending;
        if(double.IsNaN(loaded))loaded=now;
        if(explicitMismatch)return JoinVerdict.Rejected;
        if(matching){matched=true;return JoinVerdict.Matching;}
        return !matched && WithinWindow(now)?JoinVerdict.Pending:JoinVerdict.Rejected;
    }
    public bool WithinWindow(double now)=>double.IsFinite(now) && !double.IsNaN(loaded) && now>=loaded && now-loaded<CheckSeconds;
}

// Borrow just the serialized layout index during native initial spawning.
// No layout activation, timer, score, door or AI action belongs in this scope.
public sealed class JoinSpawnLease<T>:IDisposable
{
    private readonly List<(T Item,int Index)> originals=new();
    private readonly Action<T,int> write;
    public JoinSpawnLease(IEnumerable<T> items,Func<T,int> read,Func<T,int> stock,Action<T,int> write) {
        this.write=write;
        try{foreach(var item in items){int index=read(item);originals.Add((item,index));write(item,stock(item));}}
        catch{Dispose();throw;}
    }
    public void Dispose(){Exception? failure=null;foreach(var entry in originals)try{write(entry.Item,entry.Index);}catch(Exception error){failure??=error;}originals.Clear();if(failure!=null)throw new InvalidOperationException("A range index could not be restored after initial spawning.",failure);}
}
