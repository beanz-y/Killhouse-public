namespace OperatorKillhouse.Core;

public readonly record struct OuterWallViewport(EditorViewport Original,EditorRect Floor,EditorRect Canvas);
public static class OuterWallDrawing
{
    public static OuterWallViewport Frame(EditorViewport original,int columns,int rows) {
        var b=original.Board;var f=OuterWallFloor.Bounds(columns,rows);float scale=original.Cell/2;
        var floor=new EditorRect(b.X+(f.Left-ConstructionPlan.Left)*scale,b.Y+(ConstructionPlan.North-f.North)*scale,(f.Right-f.Left)*scale,(f.North-f.South)*scale);
        float left=Math.Max(8,Math.Min(b.X,floor.X)),top=Math.Max(96,Math.Min(b.Y,floor.Y));
        float right=Math.Min(original.Inspector.X-8,Math.Max(b.X+b.Width,floor.X+floor.Width)),bottom=Math.Min(original.Footer.Y-6,Math.Max(b.Y+b.Height,floor.Y+floor.Height));
        return new(original,floor,new(left,top,right-left,bottom-top));
    }
    public static (float X,float Y) Point(OuterWallViewport frame,float x,float z)=>(frame.Original.Board.X+(x-ConstructionPlan.Left)/2*frame.Original.Cell,frame.Original.Board.Y+(ConstructionPlan.North-z)/2*frame.Original.Cell);
    public static IEnumerable<EditorLine> Border(OuterWallViewport frame) {
        var f=frame.Floor;var(lo,hi)=OuterWallFloor.Portal;
        float top=Math.Clamp(Point(frame,0,hi).Y,f.Y,f.Y+f.Height),bottom=Math.Clamp(Point(frame,0,lo).Y,f.Y,f.Y+f.Height);
        yield return new(f.X,f.Y,f.X+f.Width,f.Y);
        yield return new(f.X,f.Y+f.Height,f.X+f.Width,f.Y+f.Height);
        foreach(float x in new[]{f.X,f.X+f.Width}){yield return new(x,f.Y,x,top);yield return new(x,bottom,x,f.Y+f.Height);}
    }
    public static BarrierSegment Extend(Layout doc,BarrierSegment s) {
        if(!doc.InteriorEdge(s.Edge) || s.Kind!=Barrier.Wall || s.WindowPart || s.JointFill)return s;
        var f=OuterWallFloor.Bounds(doc);bool h=s.Edge.Horizontal;
        float low=(h?s.X:s.Z)-s.Span/2,high=(h?s.X:s.Z)+s.Span/2;
        if(h && s.Edge.Column==0 && !BoundaryWalls.BlocksEnd(doc,s,false))low=f.Left;
        if(h && s.Edge.Column==doc.Columns-1 && !BoundaryWalls.BlocksEnd(doc,s,true))high=f.Right;
        if(!h && s.Edge.Row==0 && !BoundaryWalls.BlocksEnd(doc,s,true))high=f.North;
        if(!h && s.Edge.Row==doc.Rows-1 && !BoundaryWalls.BlocksEnd(doc,s,false))low=f.South;
        return s with{X=h?(low+high)/2:s.X,Z=h?s.Z:(low+high)/2,Span=high-low};
    }
}
