namespace OperatorKillhouse.Core;

public sealed record BarrierSegment(Edge Edge,Barrier Kind,float X,float Z,float Span=ConstructionPlan.Pitch,float Thickness=.60f,bool JointFill=false,float Bottom=0,float Height=3,bool WindowPart=false);
public sealed record Opening(Edge Edge,float X,float Z,float Yaw);
public sealed record BuildPlan(IReadOnlyList<BarrierSegment> Segments,IReadOnlyList<Opening> Doors);
public static class ConstructionPlan
{
    // Local house coordinates are part of the legacy layout interface.
    public const float Pitch=2, Left=2.5f, North=22.95f, PropLeft=2, PropSouth=1.95f;
    public static (float X,float Z) Center(Cell cell)=>(Left+(cell.Column+.5f)*Pitch,North-(cell.Row+.5f)*Pitch);
    public static BuildPlan Make(Layout layout) {
        var segments=new List<BarrierSegment>(); var doors=new List<Opening>();
        foreach(var (edge,kind) in BoundaryWalls.Construction(layout).OrderBy(e=>e.Key.Horizontal).ThenBy(e=>e.Key.Row).ThenBy(e=>e.Key.Column)) {
            if(kind==Barrier.Open) continue;
            float x=Left+(edge.Column+(edge.Horizontal?.5f:0))*Pitch;
            float z=North-(edge.Row+(edge.Horizontal?0:.5f))*Pitch;
            // A doorway is one authored asset, including its aperture, rather than
            // three guessed cuboids. Keep the file's two-metre edge lattice.
            segments.Add(new(edge,kind,x,z));
            if(kind==Barrier.Door) doors.Add(new(edge,x,z,edge.Horizontal?0:90));
        }
        return new(segments,doors);
    }
}
