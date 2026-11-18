using System.Diagnostics;

namespace OperatorKillhouse.Core;

// Unity's frame time can still describe the frame before a blocking scene
// load when a native request is submitted. Measure from the request itself,
// so the next frame cannot charge that earlier scene load to this request.
public sealed class NativeLoadDeadline
{
    private readonly Func<double> now;
    private double started;
    private bool running;
    public NativeLoadDeadline(Func<double>? clock=null) => now=clock??(()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);
    public void Restart(){started=now();running=true;}
    public void Clear()=>running=false;
    public bool Expired=>running && now()-started>30;
}
