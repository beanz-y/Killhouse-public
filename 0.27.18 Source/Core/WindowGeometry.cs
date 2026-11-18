using System.Numerics;

namespace OperatorKillhouse.Core;

// Open training window. No glass, moving parts or new network identities.
public sealed record WindowSettings(float Width=1.2f,float Sill=1,float Height=1.2f)
{
    public bool Valid=>new[]{Width,Sill,Height}.All(float.IsFinite) && Width>=.6f && Width<=1.2f && Sill>=.5f && Sill<=1.5f && Height>=.5f && Height<=1.5f && Sill+Height<=2.7f;
}
public static class WindowGeometry
{
    public static WindowSettings Settings(Layout doc,Edge edge)=>doc.WindowOptions.GetValueOrDefault(edge)??new();
    public static IEnumerable<BarrierSegment> Parts(BarrierSegment fitted,WindowSettings settings,float apertureCenter)
    {
        if(!settings.Valid)throw new InvalidOperationException("Window opening dimensions are outside the supported range.");
        bool h=fitted.Edge.Horizontal;float center=h?fitted.X:fitted.Z;
        float low=center-fitted.Span/2,high=center+fitted.Span/2,left=apertureCenter-settings.Width/2,right=apertureCenter+settings.Width/2;
        if(left-low<.08f || high-right<.08f)throw new InvalidOperationException("Window opening leaves too little wall at a junction.");
        BarrierSegment Piece(float a,float b,float bottom,float height)=>fitted with{Kind=Barrier.Wall,X=h?(a+b)/2:fitted.X,Z=h?fitted.Z:(a+b)/2,Span=b-a,Bottom=bottom,Height=height,WindowPart=true};
        // Sill/header span only the aperture. Side posts run floor to ceiling,
        // so collision volumes meet without overlapping or hiding the opening.
        yield return Piece(low,left,0,3);
        yield return Piece(right,high,0,3);
        yield return Piece(left,right,0,settings.Sill);
        yield return Piece(left,right,settings.Sill+settings.Height,3-settings.Sill-settings.Height);
    }
    public static SolidBox Volume(BarrierSegment s)=>new(new(s.X,s.Bottom+s.Height/2,s.Z),new(s.Edge.Horizontal?s.Span/2:s.Thickness/2,s.Height/2,s.Edge.Horizontal?s.Thickness/2:s.Span/2),Quaternion.Identity);
    public static bool AtHeight(BarrierSegment s,float bottom,float top)=>!s.WindowPart || Math.Min(top,s.Bottom+s.Height)>Math.Max(bottom,s.Bottom);
    public static string Key(Layout doc)=>string.Join(";",doc.Edges.Where(p=>p.Value!=Barrier.Open).OrderBy(p=>p.Key.Row).ThenBy(p=>p.Key.Column).ThenBy(p=>p.Key.Horizontal).Select(p=>$"{p.Key.Row},{p.Key.Column},{p.Key.Horizontal},{(int)p.Value}")) + "|" + string.Join(";",doc.Interior.Where(p=>p.Value==Barrier.Window).OrderBy(p=>p.Key.Row).ThenBy(p=>p.Key.Column).ThenBy(p=>p.Key.Horizontal).Select(p=>$"{p.Key}:{Settings(doc,p.Key)}"));
}
