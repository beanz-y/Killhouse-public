namespace OperatorKillhouse.Core;

public readonly record struct EditorRect(float X,float Y,float Width,float Height);
public readonly record struct EditorViewport(EditorRect Board,EditorRect Inspector,EditorRect Footer,float Cell);
public readonly record struct EditorLine(float AX,float AY,float BX,float BY);
public static class EditorDrawing
{
    public static IReadOnlyList<EditorLine> TargetMarkers(Layout doc,Target target,float x,float y,float radius) {
        if(target.Style!=TargetStyle.Standless)return new[]{new EditorLine(x-radius,y-radius,x+radius,y-radius),new EditorLine(x-radius,y+radius,x+radius,y+radius)};
        var(dx,dy)=TargetGeometry.HeadDirection(doc,target);float cx=x+dx*radius,cy=y+dy*radius;
        return new[]{new EditorLine(cx+dy*radius,cy-dx*radius,cx-dy*radius,cy+dx*radius)};
    }
    public static EditorViewport Viewport(float width,float height,int columns,int rows) {
        var footer=new EditorRect(18,height-110,width-36,52);
        // The entry/exit legend ends 28 px below the board. Leave another 8 px
        // before the separate status region, and keep the inspector above it.
        float size=Math.Min(width-318,footer.Y-36-132),cell=Math.Min(size/columns,size/rows);
        var board=new EditorRect(36,132,cell*columns,cell*rows);
        return new(board,new(board.X+board.Width+24,112,width-board.X-board.Width-42,footer.Y-124),footer,cell);
    }
    // IMGUI clips a rotated horizontal texture strip against its unrotated
    // bounds. Draw axis-aligned grid/wall lines as actual vertical/horizontal
    // rectangles so the rightmost columns reach the bottom of the board.
    public static EditorRect? AxisLine(float ax,float ay,float bx,float by,float width) {
        if(Math.Abs(bx-ax)<.001f)return new(ax-width*.5f,Math.Min(ay,by),width,Math.Abs(by-ay));
        if(Math.Abs(by-ay)<.001f)return new(Math.Min(ax,bx),ay-width*.5f,Math.Abs(bx-ax),width);
        return null;
    }
    public static EditorLine? ClipLine(EditorRect area,float ax,float ay,float bx,float by,float padding=0) {
        float left=area.X+padding,right=area.X+area.Width-padding,top=area.Y+padding,bottom=area.Y+area.Height-padding;
        if(right<left || bottom<top)return null;
        float dx=bx-ax,dy=by-ay,low=0,high=1;
        bool Edge(float p,float q){if(p==0)return q>=0;float t=q/p;if(p<0){if(t>high)return false;low=Math.Max(low,t);}else{if(t<low)return false;high=Math.Min(high,t);}return true;}
        return Edge(-dx,ax-left) && Edge(dx,right-ax) && Edge(-dy,ay-top) && Edge(dy,bottom-ay)?new(ax+low*dx,ay+low*dy,ax+high*dx,ay+high*dy):null;
    }
}
