namespace OperatorKillhouse.Core;

// Measurements of the installed Wall2Meter mesh, not its enclosing bounds.
// The side stiles/top rail stand 47.92 mm proud of the plywood. NativePiece
// scales the whole two-metre mesh along its span (and window parts vertically).
public static class ReinforcedPanels
{
    public const float Recess=.04792f,Stile=.188172f;
    public static IEnumerable<BarrierSegment> Solids(BarrierSegment wall) {
        if(wall.Kind!=Barrier.Wall)yield break;
        float end=wall.Span*Stile/2,rail=wall.Height*Stile/3;
        yield return wall with{Thickness=Math.Max(.02f,wall.Thickness-2*Recess),Span=wall.Span-2*end,Height=wall.Height-rail};
        foreach(int sign in new[]{-1,1})yield return wall with{X=wall.X+(wall.Edge.Horizontal?sign*(wall.Span-end)/2:0),Z=wall.Z+(wall.Edge.Horizontal?0:sign*(wall.Span-end)/2),Span=end};
        yield return wall with{Bottom=wall.Bottom+wall.Height-rail,Height=rail};
    }
}
