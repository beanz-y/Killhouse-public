namespace OperatorKillhouse.Core;

// Measured native opening versus the two-metre frame's span midpoint.
// Segment X/Z denotes the opening centre, never a shifted grid coordinate.
public static class DoorwayAssembly
{
    public static float Offset=>(DoorwayProfile.Left+DoorwayProfile.Right-ConstructionPlan.Pitch)/2;
    public static bool Frame(BarrierSegment segment)=>segment.Kind is Barrier.Door or Barrier.Frame;
    public static (float X,float Y,float Z) Foot(AssetCalibration calibration,float apertureX,float apertureZ) {
        var(x,y,z)=calibration.Foot;float measured=calibration.AlongX?apertureX-x:apertureZ-z;
        if(!float.IsFinite(measured) || Math.Abs(measured-Offset)>.001f)throw new InvalidOperationException("Native doorway span offset changed; centering needs a new measurement.");
        return calibration.AlongX?(apertureX,y,z):(x,y,apertureZ);
    }
    public static BarrierSegment Body(BarrierSegment segment)=>!Frame(segment)?segment:segment with {
        X=segment.X-(segment.Edge.Horizontal?Offset:0),Z=segment.Z+(segment.Edge.Horizontal?0:Offset)
    };
    public static (float Low,float High) Ends(BarrierSegment segment) {
        var body=Body(segment);float center=body.Edge.Horizontal?body.X:body.Z;return(center-body.Span/2,center+body.Span/2);
    }
    public static Footprint[] Jambs(BarrierSegment opening,float apertureHalf) {
        var(low,high)=Ends(opening);float center=opening.Edge.Horizontal?opening.X:opening.Z;
        float inner=Math.Min(apertureHalf,opening.Span/2-.02f),left=center-inner,right=center+inner;
        return opening.Edge.Horizontal?
            new[]{new Footprint((low+left)/2,opening.Z,(left-low)/2,opening.Thickness/2),new Footprint((right+high)/2,opening.Z,(high-right)/2,opening.Thickness/2)}:
            new[]{new Footprint(opening.X,(low+left)/2,opening.Thickness/2,(left-low)/2),new Footprint(opening.X,(right+high)/2,opening.Thickness/2,(high-right)/2)};
    }
    public static float Join(BarrierSegment segment,bool high,float fitted,IReadOnlyList<BarrierSegment> incident,float seam) {
        var inline=incident.FirstOrDefault(s=>s.Edge!=segment.Edge && s.Edge.Horizontal==segment.Edge.Horizontal);
        // Consecutive frames share shifted end posts. A solid neighbour is
        // fitted to that same post; free frame ends also need no extra cap.
        if(Frame(segment) && (inline!=null || !incident.Any(s=>s.Edge.Horizontal!=segment.Edge.Horizontal))){var ends=Ends(segment);return high?ends.High:ends.Low;}
        if(inline==null)return fitted;
        if(!Frame(inline))return fitted;
        var adjacent=Ends(inline);float contact=high?adjacent.Low+seam:adjacent.High-seam;
        if(!incident.Any(s=>s.Edge.Horizontal!=segment.Edge.Horizontal))return contact;
        // Keep the existing L/T/cross ownership and also reach the shifted post.
        return high?Math.Max(fitted,contact):Math.Min(fitted,contact);
    }
}
