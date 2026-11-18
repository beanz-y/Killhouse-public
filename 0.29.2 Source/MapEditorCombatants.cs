using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal sealed partial class MapEditor
{
    private int? selectedAI,selectedWaypoint;
    private int routeId;
    private string aiType="",aiSearch="",routeName="";
    private bool replacePaper,routeAssignmentsOpen;
    private CombatOrder aiOrder;
    private CivilianReaction aiReaction;
    private float aiFleeDelay=Combatants.DefaultFleeDelay;
    private float aiYaw;
    private bool aiDragging;
    private Vector2 aiDragStart;
    private (int Revision,Combatant Bot,bool Replace)? aiPreviewKey;
    private string? aiPreviewError;
    private Combatant? SelectedAI=>selectedAI is {} id?Draft.Document.Combatants.FirstOrDefault(b=>b.Id==id):null;
    private CombatRoute? SelectedRoute=>Draft.Document.Routes.FirstOrDefault(r=>r.Id==routeId);
    private void CombatantInspector() {
        var doc=Draft.Document;var bot=SelectedAI;
        if(tool is not (Tool.AI or Tool.Route) && bot==null && selectedWaypoint==null)return;
        KillhouseUi.BeginSection("Encounter contents");
        try {
            var mode=(EncounterMode)Choices((int)doc.Encounter,new[]{"AI + paper","Paper only","AI only"},1);
            if(mode!=doc.Encounter)Draft.Change(d=>d.Encounter=mode);
            KillhouseUi.Note("AI start when the host chooses Start AI encounter at the laptop. Loading a map is safe for a walk-through.");
        }finally{KillhouseUi.EndSection();}
        if(tool!=Tool.Route && selectedWaypoint==null) {
            KillhouseUi.BeginSection(bot==null?"Place AI":$"AI {bot.Id}");
            try {
                if(bot!=null){aiType=bot.Prefab;aiOrder=bot.Order;aiYaw=bot.Yaw;aiReaction=bot.Reaction;aiFleeDelay=bot.FleeDelay;}
                var types=CombatantCatalog.Types;
                var type=types.FirstOrDefault(t=>t.Key==aiType);
                if(aiType.Length==0 && types.Count>0){aiType=types[0].Key;type=types[0];}
                GUILayout.Label("Type: "+(type?.Label??(aiType.Length==0?"none available":aiType)));
                GUILayout.Label("Find AI type");GUI.SetNextControlName("kh-ai-search");aiSearch=GUILayout.TextField(aiSearch,60);
                foreach(var option in types.Where(t=>aiSearch.Length==0 || t.Label.Contains(aiSearch,StringComparison.OrdinalIgnoreCase)))
                    if(GUILayout.Button((option.Key==aiType?"● ":"")+option.Label)){aiType=option.Key;if(!Combatants.IsCivilian(aiType))aiReaction=CivilianReaction.None;if(bot!=null)EditAI(bot with{Prefab=aiType,Reaction=aiReaction});}
                if(types.Count==0)KillhouseUi.Note(CombatantCatalog.Status);
                if(GUILayout.Button("Refresh AI types"))CombatantCatalog.Refresh();
                if(Combatants.IsCivilian(aiType)) {
                    GUILayout.Label("Civilian reaction to danger");
                    var reaction=(CivilianReaction)Choices((int)aiReaction,new[]{"Native","Freeze","Flee"});
                    if(reaction!=aiReaction){aiReaction=reaction;if(bot!=null)EditAI(bot with{Reaction=reaction});}
                    if(aiReaction==CivilianReaction.Flee)FleeDelayInspector(SelectedAI);
                    KillhouseUi.Note(aiReaction==CivilianReaction.Flee?"Waits for detected danger, then runs its escape route after the selected delay, with no waypoint waits, and holds at the end. Assign a route before starting.":aiReaction==CivilianReaction.Freeze?"On danger, stops at its current position. May patrol before alarm if assigned a route. Does not fight back.":"Uses the selected combat order. Freeze/Flee are noncombatant reactions and persist until encounter reset.");
                }
                if(aiReaction==CivilianReaction.None) {
                    GUILayout.Label("Combat order");
                    var orders=new[]{CombatOrder.Defensive,CombatOrder.Cautious,CombatOrder.Offensive};
                    var order=orders[Choices(Array.IndexOf(orders,aiOrder),new[]{"Defensive","Cautious","Offensive"})];
                    if(order!=aiOrder){aiOrder=order;if(bot!=null)EditAI(bot with{Order=order});}
                    KillhouseUi.Note(aiOrder==CombatOrder.Cautious?"Holds its placed position until it personally sees a player, then advances offensively for the rest of the encounter. Hearing and radio calls do not release it. Assigned patrols are ignored for this order.":aiOrder==CombatOrder.Defensive?"With no route, holds its placed position and returns fire without advancing. With a route, patrols until contact and fights defensively.":"Advances against detected threats. With a route, patrols until contact and pursues offensively.");
                }
                GUILayout.Label($"Facing: {aiYaw:0.#}°");
                int direction=Choices(-1,new[]{"North ↑","East →","South ↓","West ←"});
                if(direction>=0){aiYaw=direction*90;if(bot!=null)EditAI(bot with{Yaw=aiYaw});}
                GUILayout.Label("Assigned route");
                int assigned=bot?.RouteId??routeId;
                string noRoute=aiReaction==CivilianReaction.Flee?"No escape route (required)":aiReaction==CivilianReaction.Freeze?"Stay at spawn before alarm":aiOrder==CombatOrder.Defensive?"Hold position / no route":"Guard spawn / no route";
                if(GUILayout.Button((assigned==0?"● ":"")+noRoute)){if(bot!=null)EditAI(bot with{RouteId=0});else routeId=0;}
                foreach(var r in doc.Routes)if(GUILayout.Button((assigned==r.Id?"● ":"")+r.Name)){routeId=r.Id;if(bot!=null)EditAI(bot with{RouteId=r.Id});}
                if(bot!=null) {
                    AIStartInspector(bot);
                    if(GUILayout.Button(bot.RouteId==0?"Create route for this AI":"Edit this AI's route")){ChangeTool(Tool.Route);if(bot.RouteId==0)NewRoute();}
                    if(GUILayout.Button("Delete AI")){Draft.Change(d=>ScenarioTriggers.RemoveAI(d,bot.Id));selectedAI=null;message="AI deleted.";}
                }
                replacePaper=GUILayout.Toggle(replacePaper,"Replace paper target under new AI");
                KillhouseUi.Note(replacePaper?"Placing on a paper target removes it in the same undo step.":"Paper targets remain in mixed encounters. AI-only maps hide their paper targets.");
                KillhouseUi.Note("Click to place. Drag to aim. Select and drag to move. R rotates. Delete removes the selection.");
            }finally{KillhouseUi.EndSection();}
        }
        if(tool==Tool.Route || selectedWaypoint!=null)RouteInspector();
    }
    private void FleeDelayInspector(Combatant? bot) {
        GUILayout.Label(aiFleeDelay==0?"Freeze before fleeing: Instant (0 s)":$"Freeze before fleeing: {aiFleeDelay:0.#} s");
        float delay=MathF.Round(GUILayout.HorizontalSlider(aiFleeDelay,0,Combatants.MaxFleeDelay)*2)/2;
        if(delay!=aiFleeDelay){aiFleeDelay=delay;if(bot!=null)EditAI(bot with{FleeDelay=delay});}
        KillhouseUi.Note("0 = flee immediately when danger is detected. Maximum 30 seconds; default 10. Saved separately for each AI.");
    }
    private void NewRoute() {
        if(Draft.Document.Routes.Count>=Combatants.RouteLimit){message="Maximum 32 routes.";return;}
        int? bot=SelectedAI?.Id;
        Draft.Change(d=>routeId=Combatants.CreateRoute(d,bot));selectedWaypoint=0;
        message=bot is {} id?$"Route created and assigned to AI {id}. Click the map to append waypoints.":"Route created. Assign AI below, then click the map to append waypoints.";
    }
    private void RouteInspector() {
        KillhouseUi.BeginSection("Routes");
        try {
            var doc=Draft.Document;
            if(GUILayout.Button(SelectedAI is {} selectedBot?$"New route for AI {selectedBot.Id}":"New route"))NewRoute();
            foreach(var r in doc.Routes)if(GUILayout.Button((r.Id==routeId?"● ":"")+r.Name+" ("+r.Points.Count+")")){routeId=r.Id;selectedWaypoint=null;}
            var route=SelectedRoute;if(route==null){KillhouseUi.Note("Create or select a route, then click the map to add waypoints.");return;}
            var assigned=doc.Combatants.Where(b=>b.RouteId==route.Id).Select(b=>b.Id).ToArray();
            KillhouseUi.Note(assigned.Length==0?"No AI assigned. Choose AI below to make this route patrol.":"Assigned AI: "+string.Join(", ",assigned));
            if(SelectedAI is {} bot && bot.RouteId!=route.Id && GUILayout.Button("Assign route to AI "+bot.Id) && EditAI(bot with{RouteId=route.Id}))message="Route assigned to AI "+bot.Id+".";
            routeAssignmentsOpen=KillhouseUi.Foldout(routeAssignmentsOpen,"Assign AI to this route");
            if(routeAssignmentsOpen)foreach(var candidate in doc.Combatants.ToArray()) {
                bool usesRoute=candidate.RouteId==route.Id;
                if(GUILayout.Button((usesRoute?"Remove":"Assign")+$" AI {candidate.Id} ({candidate.Order})")) {
                    EditAI(candidate with{RouteId=usesRoute?0:route.Id});
                }
            }
            GUI.SetNextControlName("kh-route-name");routeName=GUILayout.TextField(route.Name,80);if(!string.IsNullOrWhiteSpace(routeName) && routeName!=route.Name)Draft.Change(d=>{int n=d.Routes.FindIndex(r=>r.Id==route.Id);var next=route with{Name=routeName};d.Routes[n]=next;});
            bool loop=Choices(route.Loop?0:1,new[]{"Loop","Once & hold"})==0;
            if(loop!=route.Loop)Draft.Change(d=>{int n=d.Routes.FindIndex(r=>r.Id==route.Id);d.Routes[n]=d.Routes[n] with{Loop=loop};});
            GUILayout.Label("Patrol pacing");
            var pacing=(PatrolPacing)Choices((int)route.Pacing,new[]{"Natural","Continuous","Fixed waits"});
            if(pacing!=route.Pacing)Draft.Change(d=>{int n=d.Routes.FindIndex(r=>r.Id==route.Id);d.Routes[n]=d.Routes[n] with{Pacing=pacing};});
            if(route.Pacing!=PatrolPacing.Continuous) {
                GUILayout.Label(route.Pacing==PatrolPacing.Natural?$"Watch pause up to: {route.Wait:0.#} s":$"Wait at each point: {route.Wait:0.#} s");
                float wait=MathF.Round(GUILayout.HorizontalSlider(route.Wait,0,30)*2)/2;if(wait!=route.Wait)Draft.Change(d=>{int n=d.Routes.FindIndex(r=>r.Id==route.Id);d.Routes[n]=d.Routes[n] with{Wait=wait};});
            }
            KillhouseUi.Note(route.Pacing==PatrolPacing.Natural?"Walks through most waypoints; pauses for half to all of the watch time every 3–5 points. AI and route passes vary the timing. Set watch time to 0 for continuous movement.":route.Pacing==PatrolPacing.Continuous?"Walks through waypoints without planned stops.":"Waits the same time at every waypoint.");
            KillhouseUi.Note("Contact still interrupts patrol. Civilian escape routes always run through without watch stops.");
            for(int n=0;n<route.Points.Count;n++){int index=n;var point=route.Points[n];if(GUILayout.Button((selectedWaypoint==n?"● ":"")+$"{n+1}: {point.X:0.##} E, {point.South:0.##} S"))selectedWaypoint=index;}
            if(selectedWaypoint is {} selected && selected<route.Points.Count) {
                GUILayout.BeginHorizontal();try {
                    GUI.enabled=selected>0;if(GUILayout.Button("Earlier")){Draft.Change(d=>{var points=d.Routes.Single(r=>r.Id==route.Id).Points;(points[selected-1],points[selected])=(points[selected],points[selected-1]);});selectedWaypoint--;}
                    GUI.enabled=selected<route.Points.Count-1;if(GUILayout.Button("Later")){Draft.Change(d=>{var points=d.Routes.Single(r=>r.Id==route.Id).Points;(points[selected+1],points[selected])=(points[selected],points[selected+1]);});selectedWaypoint++;}
                }finally{GUI.enabled=true;GUILayout.EndHorizontal();}
                GUI.enabled=route.Points.Count>1;if(GUILayout.Button("Remove waypoint")){Draft.Change(d=>d.Routes.Single(r=>r.Id==route.Id).Points.RemoveAt(selected));selectedWaypoint=null;}GUI.enabled=true;
            }
            if(GUILayout.Button("Delete route")){Draft.Change(d=>Combatants.RemoveRoute(d,route.Id));routeId=0;selectedWaypoint=null;message="Route deleted and unassigned from AI.";}
            KillhouseUi.Note("Click map to append. Use Select to drag a waypoint; Shift+click picks a point beneath AI. Pathfinding navigates between points; numbered lines show their order.");
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
    }
    private bool AIChange(Action<Layout> edit) {
        var candidate=LayoutDraft.Copy(Draft.Document);edit(candidate);
        var error=Combatants.Check(candidate).FirstOrDefault(i=>i.Fatal);
        if(error!=null){message=error.Message;return false;}
        Draft.Change(edit);message="AI encounter updated.";return true;
    }
    private bool EditAI(Combatant bot)=>AIChange(d=>{int n=d.Combatants.FindIndex(b=>b.Id==bot.Id);if(n>=0)d.Combatants[n]=bot;});
    private void InsertAI(Layout doc,Combatant bot) {
        if(replacePaper && TargetPlacement.Hit(doc,bot.X/2,bot.South/2) is {} key){if(key.Id==0)doc.Targets.Remove(key.Cell);else doc.FineTargets.RemoveAll(t=>t.Id==key.Id);}
        doc.Combatants.Add(bot);
    }
    private string? PreviewAI(Combatant bot) {
        var key=(Draft.Revision,bot,replacePaper);if(aiPreviewKey==key)return aiPreviewError;
        var candidate=LayoutDraft.Copy(Draft.Document);InsertAI(candidate,bot);
        aiPreviewKey=key;return aiPreviewError=Combatants.Check(candidate).FirstOrDefault(i=>i.Fatal)?.Message;
    }
    private (int Route,int Point)? HitWaypoint(Vector2 g) {
        foreach(var r in Draft.Document.Routes.OrderByDescending(r=>r.Id==routeId))for(int n=0;n<r.Points.Count;n++)if(Vector2.Distance(new(r.Points[n].X/2,r.Points[n].South/2),g)<.2f)return (r.Id,n);
        return null;
    }
    private void HandleCombatantMap() {
        if(readOnly)return;var e=Event.current;var doc=Draft.Document;bool inside=board.Contains(e.mousePosition);
        if(e.type==EventType.MouseDown && e.button==0 && inside) {
            var g=Grid(e.mousePosition);int? hit=Combatants.Hit(doc,g.x*2,g.y*2);var waypoint=HitWaypoint(g);
            if(tool is Tool.AI or Tool.Route || tool is Tool.Select or Tool.Erase && (hit.HasValue || waypoint.HasValue)) {
                ClearFocus();selectedTarget=null;selectedProp=null;selectedEdge=null;selectedAutomatic=false;
                aiDragging=true;aiDragStart=g;drawing=false;
                if(tool==Tool.Select){selectedAI=e.shift && waypoint.HasValue?null:hit;if(selectedAI==null && waypoint is {} p){routeId=p.Route;selectedWaypoint=p.Point;}else selectedWaypoint=null;}
                if(tool==Tool.Erase){if(hit is {} id)Draft.Change(d=>ScenarioTriggers.RemoveAI(d,id));else if(waypoint is {} p){var r=doc.Routes.Single(r=>r.Id==p.Route);if(r.Points.Count>1)Draft.Change(d=>d.Routes.Single(r=>r.Id==p.Route).Points.RemoveAt(p.Point));else Draft.Change(d=>Combatants.RemoveRoute(d,p.Route));}aiDragging=false;}
                e.Use();return;
            }
            selectedAI=null;selectedWaypoint=null;
        }
        if(e.type==EventType.MouseDrag && aiDragging){e.Use();return;}
        if(e.type==EventType.MouseUp && e.button==0 && aiDragging) {
            aiDragging=false;var g=Grid(e.mousePosition);
            float X(float value)=>TargetPlacement.Snap(value*2,TargetPositionStep());
            if(tool==Tool.AI && CombatantCatalog.Types.Any(t=>t.Key==aiType)) {
                int id=Combatants.NextId(doc.Combatants.Select(b=>b.Id));float angle=TargetEditSteps.Aim(aiDragStart.x,aiDragStart.y,g.x,g.y,rotationStep,aiYaw)??aiYaw;
                var bot=new Combatant(id,aiType,X(aiDragStart.x),X(aiDragStart.y),angle,aiOrder,SelectedRoute?.Id??0,Combatants.IsCivilian(aiType)?aiReaction:CivilianReaction.None,aiFleeDelay);
                if(AIChange(d=>InsertAI(d,bot)))selectedAI=id;
            }else if(tool==Tool.Route && SelectedRoute is {} route) {
                if(route.Points.Count>=Combatants.PointLimit)message="Maximum 64 points per route.";
                else if(AIChange(d=>d.Routes.Single(r=>r.Id==route.Id).Points.Add(new(X(g.x),X(g.y)))))selectedWaypoint=route.Points.Count-1;
            }else if(tool==Tool.Select) {
                if(SelectedAI is {} bot)EditAI(bot with{X=X(bot.X/2+g.x-aiDragStart.x),South=X(bot.South/2+g.y-aiDragStart.y)});
                else if(SelectedRoute is {} r && selectedWaypoint is {} n && n<r.Points.Count)AIChange(d=>d.Routes.Single(route=>route.Id==r.Id).Points[n]=new(X(g.x),X(g.y)));
            }
            e.Use();
        }
    }
    private bool CombatantKeyboard(Event e) {
        if(e.keyCode is KeyCode.Delete or KeyCode.Backspace) {
            if(SelectedRoute is {} r && selectedWaypoint is {} n && n<r.Points.Count){if(r.Points.Count>1)Draft.Change(d=>d.Routes.Single(route=>route.Id==r.Id).Points.RemoveAt(n));else Draft.Change(d=>Combatants.RemoveRoute(d,r.Id));selectedWaypoint=null;return true;}
            if(SelectedAI is {} bot){Draft.Change(d=>ScenarioTriggers.RemoveAI(d,bot.Id));selectedAI=null;return true;}
        }
        if(e.keyCode==KeyCode.R && (SelectedAI!=null || tool==Tool.AI)){aiYaw=TargetEditSteps.Turn(SelectedAI?.Yaw??aiYaw,rotationStep,e.shift);if(SelectedAI is {} b)EditAI(b with{Yaw=aiYaw});return true;}
        return false;
    }
    private void DrawCombatants(float unit) {
        Vector2 P(float x,float south)=>new(board.x+x*unit/2,board.y+south*unit/2);
        void Label(Vector2 p,string text){float width=Math.Min(40,board.width),height=Math.Min(22,board.height);GUI.Label(new(Math.Clamp(p.x,board.x,board.xMax-width),Math.Clamp(p.y,board.y,board.yMax-height),width,height),text,small!);}
        foreach(var r in Draft.Document.Routes) {
            var color=r.Id==routeId?Selected:new Color(.35f,.5f,.6f,.65f);
            for(int n=0;n<r.Points.Count;n++){var p=P(r.Points[n].X,r.Points[n].South);if(n>0)Line(P(r.Points[n-1].X,r.Points[n-1].South),p,2,color);if(n==r.Points.Count-1 && r.Loop && n>0)Line(p,P(r.Points[0].X,r.Points[0].South),2,color);Fill(new(p.x-4,p.y-4,8,8),color);Label(new(p.x+4,p.y-15),(n+1).ToString());}
        }
        foreach(var bot in Draft.Document.Combatants) {
            var p=P(bot.X,bot.South);var color=selectedAI==bot.Id?Selected:bot.Order==CombatOrder.Defensive?new Color(1,.65f,.2f):new Color(1,.3f,.25f);
            float a=bot.Yaw*Mathf.Deg2Rad;GhostLine(p,p+new Vector2(MathF.Sin(a),-MathF.Cos(a))*unit*.35f,3,color);Fill(new(p.x-7,p.y-7,14,14),color);Label(new(p.x-6,p.y-10),"A"+bot.Id);
            var route=Draft.Document.Routes.FirstOrDefault(r=>r.Id==bot.RouteId);if(route?.Points.Count>0)Line(p,P(route.Points[0].X,route.Points[0].South),1,color);
        }
        if(!readOnly && tool==Tool.AI && board.Contains(Event.current.mousePosition)) {
            var g=Grid(Event.current.mousePosition);var pos=aiDragging?aiDragStart:g;float x=TargetPlacement.Snap(pos.x*2,TargetPositionStep()),z=TargetPlacement.Snap(pos.y*2,TargetPositionStep());
            var candidate=new Combatant(Combatants.NextId(Draft.Document.Combatants.Select(b=>b.Id)),aiType,x,z,aiYaw,aiOrder,SelectedRoute?.Id??0,Combatants.IsCivilian(aiType)?aiReaction:CivilianReaction.None,aiFleeDelay);
            bool allowed=PreviewAI(candidate)==null;var p=P(x,z);GhostFill(new(p.x-8,p.y-8,16,16),allowed?new Color(.2f,1,.4f,.6f):new Color(1,.2f,.2f,.6f));
        }
    }
}
