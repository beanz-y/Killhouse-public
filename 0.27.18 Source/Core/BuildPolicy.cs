using System.Diagnostics;
using System.Globalization;

namespace OperatorKillhouse.Core;

// The active and one recently used layout remain. A staged third layout is
// temporary, and the old cache entry is retired after the switch commits.
public sealed class LayoutCachePolicy
{
    private readonly Dictionary<int,long> used=new();private long clock;
    public int Count=>used.Count;
    public void Touch(int index)=>used[index]=++clock;
    public int[] Trim(int active,int staged=-1,int keep=2) {
        var victims=used.OrderBy(p=>p.Value).Where(p=>p.Key!=active && p.Key!=staged).Take(Math.Max(0,used.Count-keep)).Select(p=>p.Key).ToArray();
        foreach(int victim in victims)used.Remove(victim);return victims;
    }
    public void Remove(int index)=>used.Remove(index);
}
public static class BuildPolicy
{
    public const double FrameMilliseconds=2;
    public const int MaxStepsPerFrame=4;
    public static string Number(float value) {float rounded=MathF.Round(value,3);return rounded==0?"0.000":rounded.ToString("0.000",CultureInfo.InvariantCulture);}
    public static string GeometryDigest(Layout doc,BuildPlan architecture,FurnitureResult? furniture,string contract) {
        var rows=new List<string>{doc.Identity,contract};
        foreach(var s in architecture.Segments)rows.Add($"w:{s.Edge.Row}:{s.Edge.Column}:{s.Edge.Horizontal}:{s.Kind}:{Number(s.X)}:{Number(s.Z)}:{Number(s.Span)}:{Number(s.Thickness)}:{s.JointFill}");
        foreach(var s in architecture.Segments.Where(s=>s.WindowPart))rows.Add($"window:{Number(s.X)}:{Number(s.Z)}:{Number(s.Bottom)}:{Number(s.Height)}");
        foreach(var p in furniture?.Items??Array.Empty<FurnitureItem>())rows.Add($"p:{p.Asset}:{Number(p.Pose.X)}:{Number(p.Pose.Z)}:{Number(p.Pose.Yaw)}:{Number(p.Height)}");
        return CoopProtocol.Digest(string.Join("\n",rows));
    }
    public static PreparedLayout Plan(Layout doc,BuildPlan architecture,IReadOnlyDictionary<string,PropShape> shapes,IReadOnlyList<Footprint> targets,string contract,bool furnishings) {
        var watch=Stopwatch.StartNew();
        if(doc.Props.Any(p=>p.Id>0)) {
            if(!furnishings)throw new InvalidOperationException("Enable furniture in Mod Controls before loading individual furniture.");
            var issues=PropPlacement.Check(doc,shapes,targets,architecture.Segments);
            if(issues.Any(i=>i.Fatal))throw new InvalidOperationException(string.Join(" ",issues.Select(i=>i.Message)));
        }
        var furniture=furnishings?FurniturePlanner.Make(doc,shapes,targets,architecture.Segments):null;
        if(doc.Props.Any(p=>p.Id>0) && furniture?.Unresolved.Count>0)throw new InvalidOperationException(string.Join(" ",furniture.Unresolved));
        return new(furniture,GeometryDigest(doc,architecture,furniture,contract),watch.Elapsed.TotalMilliseconds);
    }
}
public sealed record PreparedLayout(FurnitureResult? Furniture,string Digest,double PlanningMs);
public sealed class FrameBudget
{
    private readonly Func<double> now;
    private readonly double start,milliseconds;
    private readonly int limit;
    public int Steps {get;private set;}
    public FrameBudget(double milliseconds=BuildPolicy.FrameMilliseconds,int limit=BuildPolicy.MaxStepsPerFrame,Func<double>? clock=null) {
        if(!double.IsFinite(milliseconds) || milliseconds<=0 || limit<1)throw new ArgumentOutOfRangeException();
        now=clock??(()=>Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency);start=now();this.milliseconds=milliseconds;this.limit=limit;
    }
    public bool Take() {if(Steps>=limit || Steps>0 && now()-start>=milliseconds)return false;Steps++;return true;}
    public double Elapsed=>now()-start;
}
