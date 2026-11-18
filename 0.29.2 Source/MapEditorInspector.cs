using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;
internal sealed partial class MapEditor
{
    private bool helpOpen,coordinatesOpen;
    private string ToolHint()=>tool switch {
        Tool.Select=>"Click an item to edit. Drag targets or props to move. Shift+click cycles overlapping items.",
        Tool.Wall=>"Click or drag along grid edges, including the original outer grid, to draw walls.",
        Tool.Door=>"Click an interior edge to place a door, then choose its lock and swing.",
        Tool.Opening=>"Click or drag along interior edges to add open door frames.",
        Tool.Window=>"Click an interior edge to place a window. Select it to resize.",
        Tool.Target=>"Green = allowed; red = blocked. Click to place, drag to aim. Wheel: free rotate. R / Shift+R: snap.",
        Tool.Props=>"Click to place; drag to aim. Select to move or duplicate. Wheel: free rotate. R / Shift+R: snap.",
        Tool.Triggers=>"Draw zones/lines or watch a specific AI death, add actions, then arm at the laptop.",
        Tool.Lighting=>"Select a placed light or breaker to wire it. Set circuit brightness and reset states here; place fixtures through Props.",
        Tool.MapSettings=>"Choose scene lighting and custom tags for this map.",
        Tool.AI=>"Choose a native AI type and order. Click to place, drag to aim. Start the encounter from the laptop after loading.",
        Tool.Route=>"Create a route for the selected AI, then click to add waypoints. Use Assign AI to share an existing route.",
        _=>"Click a target, prop or edge to erase it."
    };
    private void DrawInspector() {
        if(tool==Tool.MapSettings){MapOptionsInspector();return;}
        if(tool==Tool.Triggers){TriggerInspector();return;}
        if(tool==Tool.Lighting){LightingInspector();return;}
        if(Runtime.EditingAsGuest)KillhouseUi.Note(CoopNetwork.EditorNotice);
        if(Runtime.EditingWithPeers && CoopNetwork.Busy) {
            KillhouseUi.BeginSection("Multiplayer update");
            try {KillhouseUi.Note(CoopNetwork.Status);}finally{KillhouseUi.EndSection();}
        }
        CombatantInspector();TargetInspector();PropsInspector();WindowInspector();
        if(selectedEdge is {} edge)EdgeInspector(edge);
        else if(tool is Tool.Wall or Tool.Door or Tool.Opening or Tool.Erase || tool==Tool.Select && selectedTarget==null && selectedProp==null && !selectedAutomatic) {
            KillhouseUi.BeginSection(tool==Tool.Select?"Selection":tool==Tool.Erase?"Erase":"Draw "+tool.ToString().ToLowerInvariant());
            try {KillhouseUi.Note(ToolHint());}finally{KillhouseUi.EndSection();}
        }
        helpOpen=KillhouseUi.Foldout(helpOpen,"Help & map legend");
        if(helpOpen) {
            KillhouseUi.BeginSection("Using this tool");
            try {
                if(board.width<86) {
                    bool grid=GUILayout.Toggle(Plugin.EditorGrid.Value,"Show grid");
                    if(grid!=Plugin.EditorGrid.Value){Plugin.EditorGrid.Value=grid;Plugin.EditorGrid.ConfigFile.Save();}
                }
                KillhouseUi.Note(ToolHint());
                KillhouseUi.Note("T threat · N no-shoot · H hostage-taker\nS standless threat · SN standless no-shoot · SH standless hostage\nR random · A always · W window · L locked side");
                KillhouseUi.Note("The line beside S marks the head side. Door arcs show the swing. Save updates the same map name; rename to keep a separate map.");
                KillhouseUi.Note("Ctrl+Z undo · Ctrl+Y redo · Delete remove selection. The outer shell and entry/exit stay fixed.");
            }finally{KillhouseUi.EndSection();}
        }
    }
    private void RotationStepPicker() {
        GUILayout.Label("R / Shift+R snap step");
        rotationStep=TargetEditSteps.RotationStep(Choices(rotationStep==30?1:0,new[]{"45°","30°"})==1?30:45);
    }
    private void TargetInspector() {
        var target=selectedTarget is {} selected?Draft.Document.Find(selected):null;
        if(tool!=Tool.Target && target==null)return;
        TargetRef? at=target==null?null:selectedTarget;
        if(target!=null){role=target.Role;always=target.Always;}
        TargetPosePicker(at);
        KillhouseUi.BeginSection("Facing");
        try {
            KillhouseUi.Note("Wheel over map: free rotate · R / Shift+R: snap");
            if((target?.Style??targetStyle)==TargetStyle.Standless && targetAgainstSide && targetMatchSurface) {
                KillhouseUi.Note("Facing follows the selected face.");
                if(GUILayout.Button("Choose facing manually"))targetMatchSurface=false;
            }else {
            FacingPicker(at);
            GUILayout.BeginHorizontal();
            try {
                if(GUILayout.Button($"Left {rotationStep}°  [Shift+R]"))TurnTarget(at,true);
                if(GUILayout.Button($"Right {rotationStep}°  [R]"))TurnTarget(at,false);
            }finally{GUILayout.EndHorizontal();}
            }
        }finally{KillhouseUi.EndSection();}
        KillhouseUi.BeginSection("Activation");
        try {PresencePicker(at);}finally{KillhouseUi.EndSection();}
        if(at is {} key && Draft.Document.Find(key) is {} current) {
            coordinatesOpen=KillhouseUi.Foldout(coordinatesOpen,"Position coordinates");
            if(coordinatesOpen) {
                KillhouseUi.BeginSection("Position · metres from NW");
                try {PositionPicker(key,current);}finally{KillhouseUi.EndSection();}
            }
            KillhouseUi.BeginSection("Actions");
            try {if(GUILayout.Button("Delete target")){Draft.RemoveTarget(key);ClearSelection();message="Target deleted.";}}
            finally{KillhouseUi.EndSection();}
        }
    }
    private void TurnTarget(TargetRef? at,bool reverse) {
        if(at is {} key){Rotate(reverse);return;}
        float angle=yaw??0;SetYaw(null,TargetEditSteps.Turn(angle,rotationStep,reverse));
    }
    private void EdgeInspector(Edge edge) {
        var doc=Draft.Document;var kind=doc.Edges.GetValueOrDefault(edge);
        if(kind==Barrier.Door) {
            KillhouseUi.BeginSection("Lock");
            try {
                int state=doc.DoorLocks.TryGetValue(edge,out bool locked)?(locked?2:1):0;
                int next=Choices(state,new[]{"Original setting","Unlocked","Locked"});
                if(next!=state)Draft.Change(d=>{if(next==0)d.DoorLocks.Remove(edge);else d.DoorLocks[edge]=next==2;});
                if(doorAccess.TryGetValue(edge,out var access))KillhouseUi.Note($"Lock side: {access.LockedFrom}\nOpens freely from: {access.OpensFrom}");
            }finally{KillhouseUi.EndSection();}
            KillhouseUi.BeginSection("Hinge & swing");
            try {
                if(!doc.DoorOptions.TryGetValue(edge,out var door)) {
                    KillhouseUi.Note("Original door orientation.");
                    if(GUILayout.Button("Customize hinge & swing"))Draft.Change(d=>d.DoorOptions[edge]=new());
                }else {
                    GUILayout.Label("Hinge");
                    int hinge=Choices(door.HingeHigh?1:0,edge.Horizontal?new[]{"West","East"}:new[]{"North","South"});
                    GUILayout.Label("Opens toward");
                    int opens=Choices(door.OpensPositive?1:0,edge.Horizontal?new[]{"North","South"}:new[]{"West","East"});
                    if((hinge==1)!=door.HingeHigh || (opens==1)!=door.OpensPositive)Draft.Change(d=>d.DoorOptions[edge]=new(hinge==1,opens==1));
                    if(GUILayout.Button("Restore original orientation"))Draft.Change(d=>d.DoorOptions.Remove(edge));
                }
            }finally{KillhouseUi.EndSection();}
        }
        KillhouseUi.BeginSection("Actions");
        try {if(GUILayout.Button("Delete "+(kind==Barrier.Frame?"opening":kind.ToString().ToLowerInvariant()))){Draft.Barriers(new[]{edge},Barrier.Open);selectedEdge=null;message=kind+" removed.";}}
        finally{KillhouseUi.EndSection();}
    }
}
