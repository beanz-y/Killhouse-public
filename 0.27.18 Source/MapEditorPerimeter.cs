using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;
internal sealed partial class MapEditor
{
    private OuterWallViewport outerFrame;
    private Rect mapCanvas;
    private GUIStyle? portalStyle;
    private void SetPerimeter(EditorViewport viewport) {
        outerFrame=OuterWallDrawing.Frame(viewport,Draft.Document.Columns,Draft.Document.Rows);
        var c=outerFrame.Canvas;mapCanvas=new(c.X,c.Y,c.Width,c.Height);
    }
    private bool MapPointer(Vector2 point)=>board.Contains(point) || mapCanvas.Contains(point) && (tool is Tool.Props or Tool.Erase || tool==Tool.Select && relocatingTarget==null);
    private Vector2 WorldPoint(float x,float z){var p=OuterWallDrawing.Point(outerFrame,x,z);return new(p.X,p.Y);}
    private void CanvasLine(Vector2 a,Vector2 b,float width,Color color) {
        if(EditorDrawing.ClipLine(outerFrame.Canvas,a.x,a.y,b.x,b.y) is {} l)Line(new(l.AX,l.AY),new(l.BX,l.BY),width,color);
    }
    private void DrawExtraGrid() {
        if(!Plugin.EditorGrid.Value)return;
        var doc=Draft.Document;var f=outerFrame.Floor;float unit=outerFrame.Original.Cell;var c=new Color(.23f,.26f,.28f);
        // Original grid lines are still drawn by DrawMap at their exact old
        // positions. Only continue those lines across the added floor strips.
        for(int n=0;n<=doc.Columns;n++) {
            float x=board.x+n*unit;
            if(f.Y<board.y)CanvasLine(new(x,f.Y),new(x,board.y),1,c);
            if(f.Y+f.Height>board.yMax)CanvasLine(new(x,board.yMax),new(x,f.Y+f.Height),1,c);
        }
        for(int n=0;n<=doc.Rows;n++) {
            float y=board.y+n*unit;
            if(f.X<board.x)CanvasLine(new(f.X,y),new(board.x,y),1,c);
            if(f.X+f.Width>board.xMax)CanvasLine(new(board.xMax,y),new(f.X+f.Width,y),1,c);
        }
    }
    private void DrawWallCore(Edge edge,Color color) {
        var doc=Draft.Document;var s=OuterWallDrawing.Extend(doc,new(edge,Barrier.Wall,ConstructionPlan.Left+(edge.Column+(edge.Horizontal?.5f:0))*2,ConstructionPlan.North-(edge.Row+(edge.Horizontal?0:.5f))*2));
        var a=WorldPoint(s.X-(edge.Horizontal?s.Span/2:0),s.Z+(edge.Horizontal?0:s.Span/2));
        var b=WorldPoint(s.X+(edge.Horizontal?s.Span/2:0),s.Z-(edge.Horizontal?0:s.Span/2));
        CanvasLine(a,b,4,color);
    }
    private void DrawOuterBorder() {
        foreach(var l in OuterWallDrawing.Border(outerFrame))CanvasLine(new(l.AX,l.AY),new(l.BX,l.BY),2,WallColor);
    }
    private void DrawBoundaryWalls() {
        foreach(var edge in Draft.Document.BoundaryWalls)DrawWallCore(edge,selectedEdge==edge?Selected:WallColor);
    }
    private void DrawPortalMarkers() {
        portalStyle??=new GUIStyle(small!){fontSize=10,wordWrap=false};
        var(lo,hi)=OuterWallFloor.Portal;var f=outerFrame.Floor;float y=WorldPoint(0,(lo+hi)/2).y;
        if(y<mapCanvas.y+8 || y>mapCanvas.yMax-8)return;
        var saved=GUI.color;try {
            GUI.color=new(.60f,.69f,.72f);
            if(f.X>=mapCanvas.x)GUI.Label(new(f.X+4,y-8,24,18),"IN ›",portalStyle);
            if(f.X+f.Width<=mapCanvas.xMax)GUI.Label(new(f.X+f.Width-30,y-8,28,18),"OUT ›",portalStyle);
        }finally{GUI.color=saved;}
    }
}
