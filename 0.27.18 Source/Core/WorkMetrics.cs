namespace OperatorKillhouse.Core;

public sealed record WorkSnapshot(string Name,long Calls,double MeanMs,double MaxMs,long AllocatedBytes);
// Fixed labels and counters: recording a sample never retains individual frames.
public sealed class WorkMetrics
{
    private sealed class Sample {internal long Calls,Bytes;internal double Total,Max;internal void Reset(){Calls=Bytes=0;Total=Max=0;}}
    private readonly Dictionary<string,Sample> samples;
    public WorkMetrics(params string[] labels)=>samples=labels.Distinct().ToDictionary(n=>n,_=>new Sample(),StringComparer.Ordinal);
    public void Record(string label,double milliseconds,long allocatedBytes=0){
        if(!samples.TryGetValue(label,out var sample) || !double.IsFinite(milliseconds) || milliseconds<0)return;
        sample.Calls++;sample.Total+=milliseconds;sample.Max=Math.Max(sample.Max,milliseconds);sample.Bytes+=Math.Max(0,allocatedBytes);
    }
    public WorkSnapshot[] Take(){var rows=samples.Where(p=>p.Value.Calls>0).Select(p=>new WorkSnapshot(p.Key,p.Value.Calls,p.Value.Total/p.Value.Calls,p.Value.Max,p.Value.Bytes)).ToArray();Reset();return rows;}
    public void Reset(){foreach(var sample in samples.Values)sample.Reset();}
}
