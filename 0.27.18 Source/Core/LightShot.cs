namespace OperatorKillhouse.Core;

public sealed record LightShot(int Id,float[] Point,int Source=0);
public static class LightDamage
{
    public const int IdStride=1000000,MaxSources=4;
    // Run-state keys keep the stable placed-prop ID in the low part. Source
    // zero remains the original single-lamp key; layouts need no migration.
    public static int Key(int id,int source=0)=>id+source*IdStride;
    public static int Id(int key)=>(key-1)%IdStride+1;
    public static int Source(int key)=>(key-1)/IdStride;
    public static int Sources(Layout doc,int id)=>doc.Props.Any(p=>p.Id==id && p.Name=="Work light")?4:1;
    public static bool Known(Layout doc,int key)=>key is >=1 and <=IdStride*MaxSources && doc.Lights.Any(l=>l.PropId==Id(key)) && Source(key)<Sources(doc,Id(key));
    public static bool Valid(LightShot? shot)=>shot is {Id:>=1 and <=IdStride,Source:>=0 and <MaxSources,Point.Length:3} && shot.Point.All(n=>float.IsFinite(n) && Math.Abs(n)<=1000);
    public static bool ValidBroken(int[]? ids)=>ids==null || ids.Length<=PowerCircuits.MaxLights*MaxSources && ids.All(id=>id is >=1 and <=IdStride*MaxSources) && ids.Distinct().Count()==ids.Length;
}
