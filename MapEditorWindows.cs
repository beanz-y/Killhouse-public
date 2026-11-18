using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;
internal sealed partial class MapEditor
{
    private WindowSettings windowSettings=new();
    private int windowPreviewRevision=-1,windowPreviewContext=-1;
    private Edge? windowPreviewEdge;
    private WindowSettings? windowPreviewSettings;
    private string? windowRejection;
    private bool cursorWindow;
    private bool windowPreviewStrict;
    private static IReadOnlyList<Issue> WindowIssues(Layout doc)=>Runtime.PreviewTargetIssues(doc).Concat(Runtime.PreviewPropIssues(doc)).Distinct().ToArray();
    private float WindowDimension(string label,float value,float min,float max) {
        GUILayout.Label($"{label}: {value:0.0} m");
        GUILayout.BeginHorizontal();
        try {
            if(GUILayout.Button("− 0.1 m")){value=Math.Max(min,MathF.Round(value-.1f,1));ClearFocus();}
            if(GUILayout.Button("+ 0.1 m")){value=Math.Min(max,MathF.Round(value+.1f,1));ClearFocus();}
        }finally{GUILayout.EndHorizontal();}
        return value;
    }
    private void WindowInspector() {
        var doc=Draft.Document;
        Edge? selected=selectedEdge is {} e && doc.Edges.GetValueOrDefault(e)==Barrier.Window?e:null;
        if(selected==null && tool!=Tool.Window)return;
        var old=selected is {} edge?WindowGeometry.Settings(doc,edge):windowSettings;
        KillhouseUi.BeginSection(selected==null?"Window dimensions":"Selected window · dimensions");
        try {
        float width=WindowDimension("Clear width",old.Width,.6f,1.2f);
        float sill=WindowDimension("Sill above floor",old.Sill,.5f,Math.Min(1.5f,MathF.Round(2.7f-old.Height,1)));
        float height=WindowDimension("Clear height",old.Height,.5f,Math.Min(1.5f,MathF.Round(2.7f-sill,1)));
        var next=new WindowSettings(width,sill,height);
        if(next!=old) {
            if(selected is {} at) {Draft.Window(at,next);message=Draft.LastRejection==null?"Window dimensions updated.":"Window unchanged: "+Draft.LastRejection;if(Draft.LastRejection==null)windowSettings=next;}
            else windowSettings=next;
        }
        KillhouseUi.Note("Open window · no glass. Keep a doorway for room access.");
        }finally{KillhouseUi.EndSection();}
    }
    private string? PreviewWindow(Edge edge) {
        if(windowPreviewRevision!=Draft.Revision || windowPreviewContext!=Runtime.EditorPreviewContext || windowPreviewEdge!=edge || windowPreviewSettings!=windowSettings || windowPreviewStrict!=Draft.StrictValidation) {
            windowRejection=Draft.PreviewWindow(edge,windowSettings);windowPreviewRevision=Draft.Revision;windowPreviewContext=Runtime.EditorPreviewContext;windowPreviewEdge=edge;windowPreviewSettings=windowSettings;windowPreviewStrict=Draft.StrictValidation;
        }
        return windowRejection;
    }
    private void PlaceWindow(Edge edge) {
        if(Draft.Window(edge,windowSettings)){selectedEdge=edge;message="Window placed. Select to adjust its opening.";}
        else message=Draft.LastRejection==null?"Window already matches these dimensions.":"Window not placed: "+Draft.LastRejection;
    }
    private void DrawWindowEdge(Edge edge,WindowSettings settings,float unit,Color color,bool ghost=false) {
        Vector2 a=new(board.x+edge.Column*unit,board.y+edge.Row*unit),b=a+(edge.Horizontal?Vector2.right:Vector2.up)*unit;
        float half=settings.Width/4;
        var left=Vector2.Lerp(a,b,.5f-half);var right=Vector2.Lerp(a,b,.5f+half);var normal=edge.Horizontal?Vector2.up:Vector2.right;
        void Stroke(Vector2 from,Vector2 to,float width){if(ghost)GhostLine(from,to,width,color);else Line(from,to,width,color);}
        Stroke(a,left,4);Stroke(right,b,4);Stroke(left+normal*3,right+normal*3,1);Stroke(left-normal*3,right-normal*3,1);
        var center=(a+b)*.5f;var label=new Rect(center.x-7,center.y-9,18,20);
        if(ghost)GhostLabel(label,"W",color);else {Fill(new(center.x-8,center.y-9,16,18),Floor);var saved=GUI.color;try{GUI.color=color;GUI.Label(label,"W",small!);}finally{GUI.color=saved;}}
    }
    private void DrawWindowPreview(float unit) {
        cursorWindow=false;
        if(readOnly || tool!=Tool.Window || !board.Contains(Event.current.mousePosition))return;
        var grid=Grid(Event.current.mousePosition);var edge=LayoutDraft.NearestEdge(grid.x,grid.y);
        cursorWindow=true;string? blocked=PreviewWindow(edge);
        // Boundary highlights also stay within the board through GhostLine.
        DrawWindowEdge(edge,windowSettings,unit,blocked==null?new(.20f,.95f,.40f):new(1,.25f,.20f),true);
    }
}
