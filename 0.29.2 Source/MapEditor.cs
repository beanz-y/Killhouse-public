using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;

// IMGUI uses the range panel's existing gameplay-input/cursor ownership.
internal sealed partial class MapEditor
{
    private enum Tool { Select,Wall,Door,Opening,Window,Target,Props,Erase,AI,Route,Lighting,Triggers,MapSettings }
    internal readonly LayoutDraft Draft;
    internal bool Visible;
    private bool readOnly;
    private Tool tool;
    private TargetRole role;
    private float? yaw;
    private float snap=TargetEditSteps.MinimumPositionStep;
    private bool targetFree=true;
    private float TargetPositionStep()=>TargetEditSteps.PositionStep(targetFree?0:snap);
    private int rotationStep=45;
    private string xText="0",zText="0";
    private TargetRef? coordinateTarget;
    private int coordinatesRevision=-1;
    private bool always;
    private bool alignWalls=true;
    private int architectureRevision=-1,architectureContext=-1;
    private IReadOnlyList<BarrierSegment> architecture=Array.Empty<BarrierSegment>();
    private IReadOnlyList<BarrierSegment> EditorArchitecture() {
        if(architectureRevision!=Draft.Revision || architectureContext!=Runtime.EditorPreviewContext){architecture=PlacementClearance.WithShell(Draft.Document,Runtime.PreviewArchitecture(Draft.Document));architectureRevision=Draft.Revision;architectureContext=Runtime.EditorPreviewContext;}
        return architecture;
    }
    private TargetRef? selectedTarget;
    private Edge? selectedEdge;
    private Rect window,board;
    private Vector2 start,drag;
    private Vector2 inspectorScroll;
    private Vector2 footerScroll;
    private bool drawing;
    private string message="Draw on the grid. Select an item to change its options.";
    private int persisted=-1;
    private float nextPersist;
    private readonly Func<Layout,bool,string> save;
    private readonly string draftFile;
    private GUIStyle? small;
    private readonly Dictionary<Cell,Bearing> autoFaces=new();
    private int facesRevision=-1;
    private int doorAccessRevision=-1,doorAccessHouse=-1;
    private IReadOnlyDictionary<Edge,DoorAccess> doorAccess=new Dictionary<Edge,DoorAccess>();
    private int validationRevision=-1,validationHouse=-1;
    private bool validationStrict;
    private Issue[] validationIssues=Array.Empty<Issue>();
    private readonly TargetPreviewCache targetPreviewCache=new();
    private TargetPreview? cursorPreview;
    internal MapEditor(string file,Func<Layout,bool,string> save) {
        this.save=save;draftFile=file;Layout doc=LayoutDraft.Blank();
        if(File.Exists(file)) {var parsed=LayoutFormat.Read(File.ReadAllText(file));if(parsed.Success && parsed.Layout!.Columns is >=2 and <=10 && parsed.Layout.Rows is >=2 and <=10)doc=parsed.Layout;}
        Draft=new(doc,Runtime.PreviewTargetEditIssues,Runtime.PreviewPropIssues,PreviewPropObstacles,PrepareEditorTarget,WindowIssues);
    }
    internal void Open(Layout? copy=null,bool preview=false) {if(copy!=null)Draft.Replace(copy);var snapped=Runtime.SnapEditorFixtures(Draft.Document);if(!ReferenceEquals(snapped,Draft.Document))Draft.Replace(snapped);readOnly=preview;Visible=true;ClearSelection();doorAccessRevision=-1;cursorPreview=null;targetPreviewCache.Clear();propPreviewCache.Clear();furnitureRevision=-1;}
    internal void Persist(bool force=false) {
        if(readOnly)return;
        if(persisted==Draft.Revision || !force && Time.unscaledTime<nextPersist)return;
        nextPersist=Time.unscaledTime+1;
        try {LayoutFiles.SaveDraft(Draft.Document,draftFile);persisted=Draft.Revision;}
        catch(Exception error){message="Draft could not be saved: "+error.GetBaseException().Message;Plugin.Logger.LogWarning(message);}
    }
    internal void Hide() {drawing=false;dragYaw=null;Visible=false;cursorPreview=null;Persist(true);}
    internal void Draw() {
        if(!Visible)return;
        float height=Math.Min(820,Screen.height-30),width=Math.Min(1060,Screen.width-30);
        window=new((Screen.width-width)/2,(Screen.height-height)/2,width,height);
        GUI.Window(0x4B4802,window,(GUI.WindowFunction)DrawWindow,readOnly?"Operator Killhouse — load request preview":"Operator Killhouse — map editor");
    }
    private void DrawWindow(int id) {
        // Exceptions must be caught inside this delegate. IL2CPP swallows a
        // callback exception before the outer Runtime.Draw catch can see it.
        try{DrawContents(id);}catch(Exception error){Runtime.EditorFailed(error);}finally{GUI.enabled=true;}
    }
    private void DrawContents(int id) {
        small??=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true};
        Draft.StrictValidation=Plugin.Strict.Value;
        EnsureFurniture();var doc=Draft.Document;float w=window.width,h=window.height;
        var viewport=EditorDrawing.Viewport(w,h,doc.Columns,doc.Rows);
        SetPerimeter(viewport);
        Rect R(EditorRect rect)=>new(rect.X,rect.Y,rect.Width,rect.Height);
        if(doorAccessRevision!=Draft.Revision || doorAccessHouse!=Runtime.EditorHouse) {
            doorAccess=Runtime.PreviewDoorAccess(doc);doorAccessRevision=Draft.Revision;doorAccessHouse=Runtime.EditorHouse;
        }
        if(readOnly) {
            GUI.Label(new(18,32,w-36,54),doc.Name+"\nLoad request · preview only",small);
            board=R(viewport.Board);DrawMap(viewport.Cell);
            GUILayout.BeginArea(R(viewport.Inspector));
            try {
                KillhouseUi.BeginSection("Map summary");
                try {KillhouseUi.Note($"House {Runtime.EditorHouse}\n{doc.TargetCount} paper targets\n{doc.Combatants.Count} AI · {doc.Routes.Count} routes · {doc.Encounter}\n{doc.Interior.Count(p=>p.Value==Barrier.Door)} doors\n{doc.Interior.Count(p=>p.Value==Barrier.Window)} windows\n{doc.Props.Count} props");}
                finally{KillhouseUi.EndSection();}
            }finally{GUILayout.EndArea();}
            if(GUI.Button(new(w-140,h-46,122,28),"Back to request"))Hide();return;
        }
        GUI.Label(new(18,30,48,24),"Name");GUI.SetNextControlName("kh-map-name");
        string name=GUI.TextField(new(66,30,w-390,24),doc.Name,80);if(name!=doc.Name)Draft.Change(d=>d.Name=name);
        GUI.Label(new(w-305,30,50,24),"House");
        int house=Buttons(new(w-255,30,235,24),doc.Houses-1,new[]{"1","2","Both"})+1;if(house!=doc.Houses)Draft.Change(d=>d.Houses=house);
        string[] tools={"Select","Wall","Door","Opening","Window","Target","Props","Erase","AI","Route","Lighting","Triggers"};
        int chosen=Buttons(new(18,66,w-154,30),(int)tool,tools);
        if(GUI.Button(new(w-130,66,112,30),(tool==Tool.MapSettings?"● ":"")+"Map settings")){chosen=(int)Tool.MapSettings;ClearFocus();}
        if(chosen!=(int)tool)ChangeTool((Tool)chosen);
        board=R(viewport.Board);
        if(tool is not (Tool.Lighting or Tool.Triggers or Tool.MapSettings))HandleWheel();DrawMap(viewport.Cell);
        if(tool!=Tool.MapSettings){DrawCircuitWires(viewport.Cell);HandleCombatantMap();if(!HandleLightingMap() && !HandleTriggerMap())HandleMap();}
        DrawTargetPreview(viewport.Cell);DrawPropPreview(viewport.Cell);DrawWindowPreview(viewport.Cell);
        GUILayout.BeginArea(R(viewport.Inspector));
        try {
        inspectorScroll=GUILayout.BeginScrollView(inspectorScroll);
        try {
        DrawInspector();
        }finally{GUILayout.EndScrollView();}
        }finally{GUILayout.EndArea();}
        if(validationRevision!=Draft.Revision || validationHouse!=Runtime.EditorHouse || validationStrict!=Plugin.Strict.Value){validationIssues=LayoutValidation.Check(doc,Plugin.Strict.Value).Concat(Runtime.PreviewTargetIssues(doc)).Concat(Runtime.PreviewPropIssues(doc)).Distinct().ToArray();validationRevision=Draft.Revision;validationHouse=Runtime.EditorHouse;validationStrict=Plugin.Strict.Value;}
        var issues=validationIssues;string validation=issues.Length==0?"Entry → exit route is open.":string.Join("  ",issues.OrderByDescending(i=>i.Fatal).Take(2).Select(i=>i.Message));
        DrawFooter(R(viewport.Footer),validation);
        GUI.enabled=Draft.CanUndo;if(GUI.Button(new(18,h-46,70,28),"Undo")){Draft.Undo();ClearSelection();}
        GUI.enabled=Draft.CanRedo;if(GUI.Button(new(94,h-46,70,28),"Redo")){Draft.Redo();ClearSelection();}
        GUI.enabled=true;if(GUI.Button(new(174,h-46,90,28),"New blank")){Draft.Replace(LayoutDraft.Blank());ClearSelection();message="Blank draft. Undo restores the previous map.";}
        GUI.enabled=Runtime.CanSaveEdited && !issues.Any(i=>i.Fatal);
        if(GUI.Button(new(w-370,h-46,108,28),"Save map"))Save(false);
        GUI.enabled=Runtime.CanPushEdited && !issues.Any(i=>i.Fatal);
        if(GUI.Button(new(w-256,h-46,150,28),Runtime.EditingAsGuest?"Save & request load":Runtime.EditingWithPeers?"Save & push to all":"Save & walk through"))Save(true);
        GUI.enabled=true;if(GUI.Button(new(w-100,h-46,82,28),"Back"))Hide();
        Keyboard();
    }
    private void DrawFooter(Rect area,string validation) {
        string status=message;
        if(cursorPreview is {} preview) {
            if(!preview.CanPlace)status="Blocked target: "+preview.Rejection;
            else if(preview.Previous!=null)status="Release to move target.";
            else status=drawing?"Release to place target. Wheel: free rotate; R / Shift+R: snap.":"Green preview: click to place, drag to aim. Wheel: free rotate; R / Shift+R: snap.";
        }
        if(cursorPropPreview is {} propPreview)status=!propPreview.CanPlace?"Blocked furniture: "+propPreview.Rejection:propPreview.Previous!=null?"Release to move furniture.":"Green preview: click to place; drag to aim. Wheel: free rotate; R / Shift+R: snap.";
        if(cursorPreview is {CanPlace:true,Target.Style:TargetStyle.Standless} standless)status+=" "+PoseLabel(standless.Target);
        if(cursorWindow)status=windowRejection==null?"Green preview: click an interior edge to place an open window.":"Blocked window: "+windowRejection;
        Fill(area,Floor);
        GUILayout.BeginArea(new Rect(area.x+4,area.y+4,area.width-8,area.height-8));
        try {
            footerScroll=GUILayout.BeginScrollView(footerScroll);
            try {GUILayout.Label(status,small!);GUILayout.Label(validation,small!);}
            finally{GUILayout.EndScrollView();}
        }finally{GUILayout.EndArea();}
    }
    private static void ClearFocus()=>GUIUtility.keyboardControl=0;
    private static int Buttons(Rect area,int selected,string[] labels) {
        float width=area.width/labels.Length;
        for(int n=0;n<labels.Length;n++)if(GUI.Button(new(area.x+n*width,area.y,width-4,area.height),(n==selected?"● ":"")+labels[n])){selected=n;ClearFocus();}
        return selected;
    }
    private static int Choices(int selected,string[] labels,int columns=2)=>KillhouseUi.Choices(selected,labels,columns);
    private void Save(bool walk) {try{bool peers=Runtime.EditingWithPeers;message=save(Draft.Document,walk);Persist(true);if(walk && !peers && !Runtime.EditingAsGuest)Hide();}catch(Exception error){message=error.GetBaseException().Message;}}
    private void RolePicker(TargetRef? at=null) {
        int value=Choices((int)role,new[]{"Threat","No-shoot","Hostage-taker"});
        if(value==(int)role)return;role=(TargetRole)value;if(at is {} key){EditSelectedPose(key,t=>t with{Role=role});TargetFeedback("Target role updated.");role=Draft.Document.Find(key)!.Role;}
    }
    private void PresencePicker(TargetRef? at) {
        
        bool next=Choices(always?1:0,new[]{"Random","Always"})==1;
        if(next!=always){always=next;if(at is {} key){Draft.EditTarget(key,t=>t with{Always=next});TargetFeedback("Target presence updated.");}}
        GUILayout.Label(always?"Always appears, even at 0% target density.":$"Random: {Plugin.Density.Value*100:0}% chance using target density.",small);
        
    }
    private void FacingPicker(TargetRef? at) {
        var target=at is {} key?Draft.Document.Find(key):null;
        float? current=target==null?yaw:target.Yaw??(target.Facing is {} f?90*(int)f:null);
        bool mounted=(target?.Style??targetStyle)==TargetStyle.Standless && targetAgainstSide;
        if(!mounted) {
            bool automatic=GUILayout.Toggle(current==null,"Face toward entry automatically");
            if(automatic!=(current==null)){SetYaw(at,automatic?null:target==null?0:TargetPlacement.Yaw(Draft.Document,target));return;}
        }
        GUILayout.Label(current.HasValue?$"Facing: {current.Value:0.###}°":"Facing: toward entry");
        GUILayout.BeginHorizontal();try {
            GUILayout.FlexibleSpace();if(GUILayout.Button((current==0?"● ":"")+"North ↑",GUILayout.Width(100),GUILayout.Height(28)))SetYaw(at,0);GUILayout.FlexibleSpace();
        }finally{GUILayout.EndHorizontal();}
        GUILayout.BeginHorizontal();try {
            if(GUILayout.Button((current==270?"● ":"")+"West ←",GUILayout.Height(28)))SetYaw(at,270);
            if(GUILayout.Button((current==90?"● ":"")+"East →",GUILayout.Height(28)))SetYaw(at,90);
        }finally{GUILayout.EndHorizontal();}
        GUILayout.BeginHorizontal();try {
            GUILayout.FlexibleSpace();if(GUILayout.Button((current==180?"● ":"")+"South ↓",GUILayout.Width(100),GUILayout.Height(28)))SetYaw(at,180);GUILayout.FlexibleSpace();
        }finally{GUILayout.EndHorizontal();}
    }
    private void SetYaw(TargetRef? at,float? value,bool snapAngle=true){if(value.HasValue)value=snapAngle?TargetEditSteps.SnapAngle(value.Value,rotationStep):TargetPlacement.Angle(value.Value);if(at is {} key){EditSelectedPose(key,t=>t with{Yaw=value,Facing=null});TargetFeedback("Target angle updated.");if(Draft.LastRejection!=null)return;}yaw=value;targetMatchSurface=false;}
    private void TargetFeedback(string success,bool placing=false)=>message=Draft.LastRejection==null?success:(placing?"Target not placed: ":"Target unchanged: ")+Draft.LastRejection;
    private static bool Number(string text,out float value)=>float.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value) && float.IsFinite(value);
    private void PositionPicker(TargetRef key,Target target) {
        if(coordinateTarget!=key || coordinatesRevision!=Draft.Revision){var g=TargetPlacement.Grid(target);xText=(g.X*2).ToString("R",System.Globalization.CultureInfo.InvariantCulture);zText=(g.Y*2).ToString("R",System.Globalization.CultureInfo.InvariantCulture);coordinateTarget=key;coordinatesRevision=Draft.Revision;}
        GUILayout.BeginHorizontal();try {GUILayout.Label("East");GUI.SetNextControlName("kh-target-x");xText=GUILayout.TextField(xText,12);}finally{GUILayout.EndHorizontal();}
        GUILayout.BeginHorizontal();try {GUILayout.Label("South");GUI.SetNextControlName("kh-target-z");zText=GUILayout.TextField(zText,12);}finally{GUILayout.EndHorizontal();}
        if(GUILayout.Button("Apply position") && Number(xText,out float x) && Number(zText,out float z)){BeginTargetDrag();try{selectedTarget=Draft.MoveFine(key,x/2,z/2,TargetPositionStep());TargetFeedback("Target moved.");}finally{EndTargetDrag();}}
    }
    private void ChangeTool(Tool next) {
        var bot=next==Tool.Route?SelectedAI:null;
        tool=next;ClearSelection();
        if(bot!=null){selectedAI=bot.Id;routeId=bot.RouteId;}
        message=ToolHint();ClearFocus();
    }
    private void ClearSelection(){ClearTriggerSelection();selectedAI=null;selectedWaypoint=null;aiDragging=false;selectedProp=null;propSurfaceSelection=-1;selectedAutomatic=false;selectedTarget=null;coordinateTarget=null;selectedEdge=null;drawing=false;dragYaw=null;inspectorScroll=Vector2.zero;coordinatesOpen=false;propCoordinatesOpen=false;relocatingTarget=null;EndTargetDrag();}
    private void Rotate(bool reverse=false) {if(selectedTarget is {} key && Draft.Document.Find(key) is {} t)SetYaw(key,TargetEditSteps.Turn(TargetPlacement.Yaw(Draft.Document,t),rotationStep,reverse));}
    private void Keyboard() {
        var e=Event.current;if(e.type!=EventType.KeyDown || GUI.GetNameOfFocusedControl().StartsWith("kh-",StringComparison.Ordinal))return;
        if(TriggerKeyboard(e)){e.Use();return;}
        if(CombatantKeyboard(e)){e.Use();return;}
        if((e.control || e.command) && e.keyCode==KeyCode.Z){if(e.shift)Draft.Redo();else Draft.Undo();ClearSelection();e.Use();}
        else if((e.control || e.command) && e.keyCode==KeyCode.Y){Draft.Redo();ClearSelection();e.Use();}
        else if(e.keyCode==KeyCode.R){if(drawing && MapPointer(e.mousePosition))RotateAtPointer(null,e.shift);else if(tool==Tool.Props || selectedProp.HasValue)RotateProp(e.shift);else if(tool==Tool.Target && board.Contains(e.mousePosition)){var g=Grid(e.mousePosition);var t=Draft.PrepareFine(g.x,g.y,role,yaw,always,TargetPositionStep(),targetStyle,targetHeight,targetPitch,targetRoll);SetYaw(null,TargetEditSteps.Turn(TargetPlacement.Yaw(Draft.Document,t),rotationStep,e.shift));}else Rotate(e.shift);e.Use();}
        else if(e.keyCode==KeyCode.F && selectedEdge is {} edge && Draft.Document.DoorOptions.TryGetValue(edge,out var door)){Draft.Change(d=>d.DoorOptions[edge]=door with{OpensPositive=!door.OpensPositive});e.Use();}
        else if(e.keyCode is KeyCode.Delete or KeyCode.Backspace){if(selectedTarget is {} key)Draft.RemoveTarget(key);if(selectedProp is {} prop){Draft.RemoveProp(prop);message="Furniture and attached pieces deleted. Undo restores them.";}if(selectedEdge is {} wall)Draft.Barriers(new[]{wall},Barrier.Open);ClearSelection();e.Use();}
    }
    private Vector2 Grid(Vector2 point)=>new((point.x-board.x)/board.width*Draft.Document.Columns,(point.y-board.y)/board.height*Draft.Document.Rows);
    private void HandleMap() {
        var e=Event.current;var doc=Draft.Document;
        if(e.type==EventType.MouseDown && e.button==0 && MapPointer(e.mousePosition)) {
            ClearFocus();dragYaw=null;start=drag=Grid(e.mousePosition);drawing=true;
            var cell=new Cell((int)start.y,(int)start.x);var edge=LayoutDraft.NearestEdge(start.x,start.y);
            var priorSelection=(selectedTarget,selectedProp,selectedEdge,selectedAutomatic);var priorTarget=selectedTarget;selectedTarget=null;coordinateTarget=null;selectedEdge=null;selectedProp=null;selectedAutomatic=false;
            if(relocatingTarget is {} moving)selectedTarget=moving;
            else if(tool==Tool.Select) {selectedTarget=PickTarget(start,e.shift);if(selectedTarget!=priorTarget)SelectTargetSurface();if(selectedTarget==null){var hit=HitFurniture(start,e.shift);if(hit!=null){if(hit.PropIndex>=0)selectedProp=hit.PropIndex;else selectedAutomatic=true;}}if(selectedTarget==null && selectedProp==null && !selectedAutomatic && BoundaryWalls.Visible(doc,edge) && doc.Edges.GetValueOrDefault(edge)!=Barrier.Open)selectedEdge=edge;}
            BeginTargetDrag();
            if(priorSelection!=(selectedTarget,selectedProp,selectedEdge,selectedAutomatic)){inspectorScroll=Vector2.zero;coordinatesOpen=false;}
            e.Use();
        } else if(e.type==EventType.MouseDrag && drawing) {drag=Grid(e.mousePosition);e.Use();}
        else if(e.type==EventType.MouseUp && drawing && e.button==0) {
            drawing=false;drag=Grid(e.mousePosition);var cell=new Cell((int)start.y,(int)start.x);
            if(relocatingTarget is {} moving) {
                selectedTarget=Draft.MoveFine(moving,start.x,start.y,TargetPositionStep(),dragYaw);TargetFeedback("Target moved to the new face.");
                if(Draft.LastRejection==null)relocatingTarget=null;
            }
            else if(tool is Tool.Wall or Tool.Opening)Draft.Barriers(LayoutDraft.Stroke(start.x,start.y,drag.x,drag.y),tool==Tool.Wall?Barrier.Wall:Barrier.Frame);
            else if(tool==Tool.Door){var edge=LayoutDraft.NearestEdge(start.x,start.y);Draft.Barriers(new[]{edge},Barrier.Door);if(doc.InteriorEdge(edge))selectedEdge=edge;}
            else if(tool==Tool.Window)PlaceWindow(LayoutDraft.NearestEdge(start.x,start.y));
            else if(tool==Tool.Target) {
                float? aimed=dragYaw??TargetEditSteps.Aim(start.x,start.y,drag.x,drag.y,rotationStep,yaw);
                Draft.AddFine(start.x,start.y,role,aimed,always,TargetPositionStep(),targetStyle,targetHeight,targetPitch,targetRoll);TargetFeedback("Target placed.",true);
            } else if(tool==Tool.Props)PlaceProp(start,drag);
            else if(tool==Tool.Erase) {var hit=TargetPlacement.Hit(doc,start.x,start.y);if(hit is {} key)Draft.RemoveTarget(key);else if(HitFurniture(start) is {PropIndex:>=0} prop)Draft.RemoveProp(prop.PropIndex);else{var edge=LayoutDraft.NearestEdge(start.x,start.y);float distance=edge.Horizontal?Math.Abs(start.y-edge.Row):Math.Abs(start.x-edge.Column);if(distance<.22f)Draft.Barriers(new[]{edge},Barrier.Open);else Draft.Erase(cell);}}
            else if(tool==Tool.Select && selectedTarget is {} from && ((drag-start).magnitude>.0001f || dragYaw.HasValue)) {var target=doc.Find(from)!;var g=TargetPlacement.Grid(target);selectedTarget=Draft.MoveFine(from,g.X+drag.x-start.x,g.Y+drag.y-start.y,TargetPositionStep(),dragYaw);TargetFeedback("Target moved.");}
            else if(tool==Tool.Select && selectedProp is {} propIndex && ((drag-start).magnitude>.0001f || dragYaw.HasValue))MoveProp(propIndex,drag);
            EndTargetDrag();dragYaw=null;
            e.Use();
        }
    }
    private static readonly Color WallColor=new(.77f,.80f,.82f),DoorColor=new(.95f,.73f,.28f),Selected=new(.25f,.85f,1),Floor=new(.10f,.12f,.14f);
    private void DrawTargetPreview(float unit) {
        cursorPreview=null;var pointer=Event.current.mousePosition;
        if(readOnly || !board.Contains(pointer))return;
        var g=Grid(pointer);Target? target=null;TargetRef? previous=null;
        if(relocatingTarget is {} relocating) {
            previous=relocating;var origin=drawing?start:g;target=Draft.PrepareMove(relocating,origin.x,origin.y,TargetPositionStep(),dragYaw);
        } else if(tool==Tool.Target) {
            var origin=drawing?start:g;
            float? aimed=dragYaw??TargetEditSteps.Aim(origin.x,origin.y,g.x,g.y,rotationStep,yaw);
            target=Draft.PrepareFine(origin.x,origin.y,role,aimed,always,TargetPositionStep(),targetStyle,targetHeight,targetPitch,targetRoll);
        } else if(tool==Tool.Select && drawing && selectedTarget is {} key && Draft.Document.Find(key) is {} selected) {
            var at=TargetPlacement.Grid(selected);previous=key;
            target=Draft.PrepareMove(key,at.X+g.x-start.x,at.Y+g.y-start.y,TargetPositionStep(),dragYaw);
        }
        if(target==null)return;
        cursorPreview=targetPreviewCache.Get(Draft,target,previous,Runtime.EditorPreviewContext);
        var color=cursorPreview.CanPlace?new Color(.20f,.95f,.40f):new Color(1,.25f,.20f);
        Vector2 P(float x,float y)=>new(board.x+x*unit,board.y+y*unit);
        var corners=TargetPlacement.Bounds(Draft.Document,target).Corners.Select(v=>P((v.X-ConstructionPlan.Left)/2,(ConstructionPlan.North-v.Z)/2)).ToArray();
        for(int n=0;n<4;n++)GhostLine(corners[n],corners[(n+1)%4],2,color);
        var grid=TargetPlacement.Grid(target);var center=P(grid.X,grid.Y);float radius=Math.Min(11,unit*.18f);
        var tint=color;tint.a=.30f;GhostFill(new(center.x-radius,center.y-radius,radius*2,radius*2),tint);
        foreach(var marker in EditorDrawing.TargetMarkers(Draft.Document,target,center.x,center.y,radius))GhostLine(new(marker.AX,marker.AY),new(marker.BX,marker.BY),2,color);
        float angle=TargetPlacement.Yaw(Draft.Document,target)*Mathf.Deg2Rad;var dir=new Vector2(MathF.Sin(angle),-MathF.Cos(angle));var side=new Vector2(-dir.y,dir.x);var end=center+dir*unit*.4f;
        GhostLine(center,end,3,color);GhostLine(end,end-dir*7+side*4,3,color);GhostLine(end,end-dir*7-side*4,3,color);
        GhostLabel(new(center.x-6,center.y-9,20,20),TargetMarker(target),color);
        GhostLabel(new(center.x+radius+(target.Style==TargetStyle.Standless?4:0),center.y-radius-9,16,18),target.Always?"A":"R",color);
    }
    private void GhostLine(Vector2 a,Vector2 b,float width,Color color) {
        if(EditorDrawing.ClipLine(new(mapCanvas.x,mapCanvas.y,mapCanvas.width,mapCanvas.height),a.x,a.y,b.x,b.y,width*.5f) is {} line)Line(new(line.AX,line.AY),new(line.BX,line.BY),width,color);
    }
    private void GhostFill(Rect rect,Color color) {
        float x=Math.Max(rect.x,mapCanvas.x),y=Math.Max(rect.y,mapCanvas.y),right=Math.Min(rect.xMax,mapCanvas.xMax),bottom=Math.Min(rect.yMax,mapCanvas.yMax);
        if(right>x && bottom>y)Fill(new(x,y,right-x,bottom-y),color);
    }
    private void GhostLabel(Rect rect,string text,Color color) {
        if(rect.width>mapCanvas.width || rect.height>mapCanvas.height)return;
        rect.x=Math.Clamp(rect.x,mapCanvas.x,mapCanvas.xMax-rect.width);rect.y=Math.Clamp(rect.y,mapCanvas.y,mapCanvas.yMax-rect.height);
        var saved=GUI.color;try{GUI.color=color;GUI.Label(rect,text,small!);}finally{GUI.color=saved;}
    }
    private void DrawMap(float unit) {
        var doc=Draft.Document;Fill(mapCanvas,Floor);Fill(board,Floor);
        if(facesRevision!=Draft.Revision){autoFaces.Clear();foreach(var target in doc.AllTargets.Where(t=>!t.Facing.HasValue && !t.Yaw.HasValue))autoFaces[target.Cell]=LayoutValidation.FaceEntry(doc,target.Cell);facesRevision=Draft.Revision;}
        Vector2 P(float x,float y)=>new(board.x+x*unit,board.y+y*unit);
        if(Plugin.EditorGrid.Value){for(int x=0;x<=doc.Columns;x++)Line(P(x,0),P(x,doc.Rows),1,new(.23f,.26f,.28f));for(int y=0;y<=doc.Rows;y++)Line(P(0,y),P(doc.Columns,y),1,new(.23f,.26f,.28f));}
        DrawExtraGrid();
        // Native reinforced walls are about 60 cm thick. Show their measured
        // footprint so a valid near-wall preview corresponds to the real face.
        foreach(var segment in EditorArchitecture().Where(s=>BoundaryWalls.Visible(doc,s.Edge)).Select(s=>OuterWallDrawing.Extend(doc,s))) {
            if(segment.WindowPart && segment.Height<2.99f)continue;
            var solids=segment.Kind==Barrier.Wall?new[]{new Footprint(segment.X,segment.Z,segment.Edge.Horizontal?segment.Span/2:segment.Thickness/2,segment.Edge.Horizontal?segment.Thickness/2:segment.Span/2)}:PlacementClearance.Jambs(segment);
            foreach(var solid in solids){var a=P((solid.X-solid.HalfX-ConstructionPlan.Left)/2,(ConstructionPlan.North-solid.Z-solid.HalfZ)/2);var b=P((solid.X+solid.HalfX-ConstructionPlan.Left)/2,(ConstructionPlan.North-solid.Z+solid.HalfZ)/2);float left=Math.Max(mapCanvas.x,a.x),top=Math.Max(mapCanvas.y,a.y),right=Math.Min(mapCanvas.xMax,b.x),bottom=Math.Min(mapCanvas.yMax,b.y);if(right>left && bottom>top)Fill(new(left,top,right-left,bottom-top),new(.30f,.34f,.36f));}
        }
        foreach(var (edge,kind) in doc.Edges) {
            if(!doc.InteriorEdge(edge))continue;
            if(kind==Barrier.Open)continue;
            if(!edge.Horizontal && edge.Row==1 && (edge.Column==0 || edge.Column==doc.Columns))continue;
            if(kind==Barrier.Window){DrawWindowEdge(edge,WindowGeometry.Settings(doc,edge),unit,selectedEdge==edge?Selected:new(.40f,.82f,.95f));continue;}
            Vector2 a=P(edge.Column,edge.Row),b=P(edge.Column+(edge.Horizontal?1:0),edge.Row+(edge.Horizontal?0:1));Color color=selectedEdge==edge?Selected:kind==Barrier.Wall?WallColor:DoorColor;
            if(kind==Barrier.Wall)DrawWallCore(edge,color);
            else {Line(a,Vector2.Lerp(a,b,.28f),4,color);Line(Vector2.Lerp(a,b,.72f),b,4,color);
                if(kind==Barrier.Door && doc.DoorOptions.TryGetValue(edge,out var door)) {
                    Vector2 hinge=Vector2.Lerp(a,b,door.HingeHigh?.72f:.28f),closed=Vector2.Lerp(a,b,door.HingeHigh?.28f:.72f),along=closed-hinge;
                    Vector2 normal=edge.Horizontal?new(0,door.OpensPositive?1:-1):new(door.OpensPositive?1:-1,0),previous=closed;
                    for(int n=1;n<=20;n++){float angle=n/20f*PlacementClearance.DoorDegrees*Mathf.Deg2Rad;var next=hinge+along*MathF.Cos(angle)+normal*along.magnitude*MathF.Sin(angle);Line(previous,next,1,color);previous=next;}
                    Line(hinge,previous,3,color);Fill(new(hinge.x-3,hinge.y-3,6,6),color);
                } else if(kind==Barrier.Door) {Line(Vector2.Lerp(a,b,.28f),Vector2.Lerp(a,b,.72f),2,color);GUI.Label(new((a.x+b.x)/2-6,(a.y+b.y)/2-9,18,20),"D",small!);}
                if(kind==Barrier.Door && doc.DoorLocks.GetValueOrDefault(edge) && doorAccess.TryGetValue(edge,out var access)) {
                    var(dx,dy)=access.MarkerDirection;var normal=new Vector2(dx,dy);var center=(a+b)*.5f;float offset=Math.Max(14,unit*.27f);
                    var marker=center+normal*offset;Line(center+normal*5,center+normal*(offset-8),1,color);
                    Fill(new(marker.x-8,marker.y-9,16,18),Floor);GUI.Label(new(marker.x-5,marker.y-9,18,20),"L",small!);
                }
            }
        }
        DrawBoundaryWalls();DrawOuterBorder();DrawFurniture(unit);
        foreach(var target in doc.AllTargets) {
            var g=TargetPlacement.Grid(target);var c=P(g.X,g.Y);float radius=Math.Min(10,unit*.17f);Color color=selectedTarget==new TargetRef(target.Cell,target.Id)?Selected:target.Role==TargetRole.Threat?new(.96f,.45f,.39f):new(.60f,.84f,.68f);
            if(selectedTarget==new TargetRef(target.Cell,target.Id)) {
                var corners=TargetPlacement.Bounds(doc,target).Corners.Select(v=>P((v.X-ConstructionPlan.Left)/2,(ConstructionPlan.North-v.Z)/2)).ToArray();
                for(int n=0;n<4;n++)Line(corners[n],corners[(n+1)%4],1,Selected);
            }
            float angle=(target.Yaw??90*(int)(target.Facing??autoFaces.GetValueOrDefault(target.Cell)))*Mathf.Deg2Rad;Vector2 dir=new(MathF.Sin(angle),-MathF.Cos(angle));
            Vector2 end=c+dir*unit*.30f,side=new(-dir.y,dir.x);Line(c,end,2,color);Line(end,end-dir*6+side*4,2,color);Line(end,end-dir*6-side*4,2,color);
            Fill(new(c.x-radius,c.y-radius,2*radius,2*radius),Floor);
            foreach(var marker in EditorDrawing.TargetMarkers(doc,target,c.x,c.y,radius))Line(new(marker.AX,marker.AY),new(marker.BX,marker.BY),2,color);
            GUI.Label(new(c.x-6,c.y-9,20,20),TargetMarker(target),small!);
            float badgeX=c.x+radius+(target.Style==TargetStyle.Standless?4:0);Fill(new(badgeX-2,c.y-radius-8,15,16),Floor);GUI.Label(new(badgeX,c.y-radius-9,16,18),target.Always?"A":"R",small!);
        }
        DrawCombatants(unit);DrawTriggers(unit);
        GUI.Label(new(board.x,board.y-24,24,22),"N ↑",small!);
        if(board.width>=86) {
            bool grid=GUI.Toggle(new(board.xMax-58,board.y-26,58,24),Plugin.EditorGrid.Value,"Grid");
            if(grid!=Plugin.EditorGrid.Value){Plugin.EditorGrid.Value=grid;Plugin.EditorGrid.ConfigFile.Save();}
        }
        DrawPortalMarkers();
        if(drawing && tool is Tool.Wall or Tool.Opening)foreach(var edge in LayoutDraft.Stroke(start.x,start.y,drag.x,drag.y).Where(e=>BoundaryWalls.CanEdit(doc,e,tool==Tool.Wall?Barrier.Wall:Barrier.Frame)))Line(P(edge.Column,edge.Row),P(edge.Column+(edge.Horizontal?1:0),edge.Row+(edge.Horizontal?0:1)),5,Selected);
    }
    private static void Fill(Rect rect,Color color){var saved=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=saved;}
    private static void Line(Vector2 a,Vector2 b,float width,Color color) {
        if(EditorDrawing.AxisLine(a.x,a.y,b.x,b.y,width) is {} rect){Fill(new(rect.X,rect.Y,rect.Width,rect.Height),color);return;}
        var saved=GUI.matrix;
        try{var delta=b-a;GUIUtility.RotateAroundPivot(MathF.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,a);Fill(new(a.x,a.y-width/2,delta.magnitude,width),color);}
        finally{GUI.matrix=saved;}
    }
}
