using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class MapEditor
{
    private int? selectedTrigger;
    private TriggerEvent newTriggerEvent;
    private bool placingTrigger,draggingTrigger,creatingTrigger,addingTriggerAction;
    private Vector2 triggerListScroll,triggerTargetScroll,triggerSourceScroll;
    private Vector2 triggerFrom,triggerTo;
    private TriggerActionKind triggerActionKind;
    private readonly HashSet<int> triggerTargets=new();
    private MapTrigger? SelectedTrigger=>Draft.Document.Triggers.FirstOrDefault(t=>t.Id==selectedTrigger);
    private void ClearTriggerSelection(){selectedTrigger=null;placingTrigger=false;draggingTrigger=false;creatingTrigger=false;addingTriggerAction=false;triggerTargets.Clear();}
    private bool TriggerKeyboard(Event e) {
        if(tool!=Tool.Triggers)return false;
        if(e.keyCode is KeyCode.Delete or KeyCode.Backspace){if(selectedTrigger is {} id)Draft.Change(d=>d.Triggers.RemoveAll(t=>t.Id==id));ClearTriggerSelection();return true;}
        return false;
    }
    private static readonly string[] TriggerActions={"Lights on","Lights off","Toggle lights","Start route","Spawn group","Spawn random AI","Despawn AI","Despawn the AI that entered"};
    private void EditTrigger(MapTrigger next)=>Draft.Change(d=>{int n=d.Triggers.FindIndex(t=>t.Id==next.Id);if(n>=0)d.Triggers[n]=next;});
    private void TriggerInspector() {
        var doc=Draft.Document;
        KillhouseUi.BeginSection("Triggers");try {
            bool create=Choices(creatingTrigger?1:0,new[]{"Your triggers","Create new"})==1;
            if(create!=creatingTrigger){creatingTrigger=create;selectedTrigger=null;placingTrigger=false;triggerTargets.Clear();}
            KillhouseUi.Note("Arm at the laptop, or use Start AI. Loading a map leaves triggers disarmed.");
        }finally{KillhouseUi.EndSection();}
        if(creatingTrigger){CreateTriggerInspector();return;}
        TriggerLibraryInspector();
        var current=SelectedTrigger;if(current==null)return;
        KillhouseUi.BeginSection("Edit · "+current.Name);try {
            GUILayout.Label("When: "+TriggerTypeLabel(current.Event));
            GUI.SetNextControlName("kh-trigger-name");string name=GUILayout.TextField(current.Name,64);if(!string.IsNullOrWhiteSpace(name) && name!=current.Name){EditTrigger(current with{Name=name});current=SelectedTrigger!;}
            if(ScenarioTriggers.Spatial(current.Event)) {
                var filter=(TriggerActors)Choices((int)current.Actors,new[]{"Players","Any AI","Anyone","Specific AI"},1);
                if(filter!=current.Actors) {
                    if(filter==TriggerActors.Players && ScenarioTriggers.HasEnteringDespawn(current))message="Despawn entering AI needs Any AI, Anyone or Specific AI. Remove that action before choosing Players only.";
                    else if(filter==TriggerActors.SpecificAI && doc.Combatants.Count==0)message="Place the source AI first.";
                    else {EditTrigger(current with{Actors=filter,ActorId=filter==TriggerActors.SpecificAI?doc.Combatants[0].Id:0,ActorIds=null});current=SelectedTrigger!;}
                }
                KillhouseUi.Note($"{current.X:0.##}, {current.South:0.##} → {current.EndX:0.##}, {current.EndSouth:0.##} m. Lines detect crossing in either direction; zones detect entry, including fast movement.");
                if(GUILayout.Button("Redraw selected "+(current.Event==TriggerEvent.CrossLine?"line":"zone"))){placingTrigger=true;message="Drag the new bounds on the map.";}
                GUILayout.Label($"Vertical range: {current.Bottom:0.0} to {current.Top:0.0} m above floor");
                float bottom=MathF.Round(GUILayout.HorizontalSlider(current.Bottom,-.5f,2.5f)*10)/10;
                float top=MathF.Round(GUILayout.HorizontalSlider(current.Top,Math.Max(bottom+.1f,.1f),3)*10)/10;
                if(bottom!=current.Bottom || top!=current.Top){EditTrigger(current with{Bottom=bottom,Top=top});current=SelectedTrigger!;}
            }
            if(current.Event==TriggerEvent.SseCollected) {
                GUILayout.Label("Source SSE objective");
                foreach(var item in doc.Sse.ToArray())if(GUILayout.Button((current.SseId==item.PropId?"● ":"")+"#"+item.PropId+" · "+item.Label)){EditTrigger(current with{SseId=item.PropId});current=SelectedTrigger!;}
                KillhouseUi.Note("Fires once when the host accepts this item collection while armed. Reset the range and rearm to collect again. Existing collected items do not fire when armed later.");
            }
            if(ScenarioTriggers.UsesAISelection(current))current=TriggerAISources(current);
            if(current.Event==TriggerEvent.BreakerOff) {
                var when=(BreakerSignal)Choices((int)current.BreakerWhen,new[]{"Off","On","Either"});
                if(when!=current.BreakerWhen){EditTrigger(current with{BreakerWhen=when});current=SelectedTrigger!;}
                GUILayout.Label("Source breaker box");
                foreach(var box in doc.Breakers.ToArray())if(GUILayout.Button((current.BreakerId==box.PropId?"● ":"")+"Breaker #"+box.PropId+" · "+PowerCircuits.Label(doc,box.Circuit))){EditTrigger(current with{BreakerId=box.PropId});current=SelectedTrigger!;}
                KillhouseUi.Note("Fires when a player uses this box and its switch cuts power. Restoring power, range resets and scripted lighting changes do not fire this event.");
            }
            bool enteringDespawn=ScenarioTriggers.HasEnteringDespawn(current);
            if(enteringDespawn)KillhouseUi.Note("Despawn the AI that entered runs on every qualifying AI entry, including simultaneous entries. Once/repeat and cooldown only affect other actions.");
            if(current.Event!=TriggerEvent.SseCollected && (!enteringDespawn || current.Actions.Any(a=>a.Kind!=TriggerActionKind.DespawnEnteringAI))) {
                bool once=Choices(current.Once?0:1,new[]{"Once per arming","Repeat"})==0;
                if(once!=current.Once){EditTrigger(current with{Once=once});current=SelectedTrigger!;}
                if(!once){GUILayout.Label($"Cooldown: {current.Cooldown:0.0} s");float seconds=MathF.Round(GUILayout.HorizontalSlider(current.Cooldown,.1f,60)*10)/10;if(seconds!=current.Cooldown){EditTrigger(current with{Cooldown=seconds});current=SelectedTrigger!;}}
            }
            KillhouseUi.Note(current.Event==TriggerEvent.SseCollected?"Arm before collecting. The item completes once per run; range reset restores it and disarms triggers.":current.Event==TriggerEvent.BreakerOff?"Arm before using the box. Off, On or Either selects the switch transition. Once per arming fires once; Repeat observes the cooldown. Reset and scripted light actions do not trigger it.":"Actors already inside when armed must leave and re-enter. Despawning an AI does not count as its death. Rearming clears fired state; range reset restores saved circuit states.");
            if(GUILayout.Button("Delete trigger")){int id=current.Id;Draft.Change(d=>d.Triggers.RemoveAll(t=>t.Id==id));selectedTrigger=null;placingTrigger=false;return;}
        }finally{KillhouseUi.EndSection();}
        TriggerActionInspector(current);
    }
    private MapTrigger TriggerAISources(MapTrigger current) {
        var doc=Draft.Document;
        GUILayout.Label(current.Event==TriggerEvent.AIDeath?"Any selected AI's death fires this trigger":"Any selected AI can activate the zone/line");
        GUILayout.Label($"Selected AI · {ScenarioTriggers.AIIds(current).Length} / {doc.Combatants.Count}");
        if(doc.Combatants.Count>1 && GUILayout.Button("Select all AI")){EditTrigger(ScenarioTriggers.WithAI(current,doc.Combatants.Select(b=>b.Id)));current=SelectedTrigger!;}
        triggerSourceScroll=GUILayout.BeginScrollView(triggerSourceScroll,GUILayout.Height(128));
        try {foreach(var bot in doc.Combatants.ToArray()) {
            bool selected=ScenarioTriggers.IncludesAI(current,bot.Id);
            bool next=GUILayout.Toggle(selected,"AI "+bot.Id+" · "+bot.Prefab.Split(':').Last());
            if(next==selected)continue;
            var ids=ScenarioTriggers.AIIds(current).ToHashSet();
            if(!next && ids.Count==1){message="Keep at least one AI selected. Select another AI before removing the last one.";continue;}
            if(next)ids.Add(bot.Id);else ids.Remove(bot.Id);
            EditTrigger(ScenarioTriggers.WithAI(current,ids));current=SelectedTrigger!;
        }}finally{GUILayout.EndScrollView();}
        KillhouseUi.Note(ScenarioTriggers.HasEnteringDespawn(current)?"Select every AI allowed to use this exit. Each is removed individually when it enters; other selected AI stay in the map.":"Select one or more AI. Once per arming and repeat cooldown apply to this whole trigger, shared by all selected AI.");
        return current;
    }
    private static string TriggerTypeLabel(TriggerEvent kind)=>kind switch{TriggerEvent.EnterZone=>"Enters a zone",TriggerEvent.CrossLine=>"Crosses a line",TriggerEvent.BreakerOff=>"Breaker switched",TriggerEvent.SseCollected=>"SSE collected",_=>"Selected AI dies"};
    private void SelectTrigger(int id) {
        selectedTrigger=id;creatingTrigger=false;placingTrigger=false;triggerTargets.Clear();
        addingTriggerAction=SelectedTrigger?.Actions.Count==0;triggerTargetScroll=Vector2.zero;
    }
    private void TriggerLibraryInspector() {
        var doc=Draft.Document;
        KillhouseUi.BeginSection($"Your triggers · {doc.Triggers.Count} / {ScenarioTriggers.Limit}");try {
            if(doc.Triggers.Count==0)KillhouseUi.Note("No triggers yet. Choose Create new to add a zone, line, AI death, breaker-off or SSE-collected event.");
            else {
                triggerListScroll=GUILayout.BeginScrollView(triggerListScroll,GUILayout.Height(148));
                try {foreach(var t in doc.Triggers.OrderBy(t=>t.Id).ToArray())if(GUILayout.Button((selectedTrigger==t.Id?"● ":"")+t.Name+"\n"+TriggerTypeLabel(t.Event)+$" · {t.Actions.Count} actions",GUILayout.Height(44)))SelectTrigger(t.Id);}
                finally{GUILayout.EndScrollView();}
            }
        }finally{KillhouseUi.EndSection();}
    }
    private void CreateTriggerInspector() {
        var doc=Draft.Document;
        KillhouseUi.BeginSection("Create trigger · choose an event");try {
            newTriggerEvent=(TriggerEvent)Choices((int)newTriggerEvent,new[]{"Enter zone","Cross line","AI death","Breaker switched","SSE collected"},1);
            KillhouseUi.Note(newTriggerEvent switch{TriggerEvent.EnterZone=>"Draw a rectangle. Moving into or through it activates the trigger.",TriggerEvent.CrossLine=>"Draw a line across a doorway or entrance. Crossing either way activates it.",TriggerEvent.SseCollected=>"Choose a placed SSE item. Collecting it activates the trigger for the team.",TriggerEvent.BreakerOff=>"Choose a placed breaker box. Choose Off, On or Either to activate at the matching switch flip.",_=>"Choose a placed AI to watch. Its death activates the trigger."});
            GUI.enabled=doc.Triggers.Count<ScenarioTriggers.Limit && !placingTrigger;
            if(GUILayout.Button(newTriggerEvent==TriggerEvent.SseCollected?"Create SSE-collected trigger":newTriggerEvent==TriggerEvent.BreakerOff?"Create breaker trigger":newTriggerEvent==TriggerEvent.AIDeath?"Create AI-death trigger":"Draw "+(newTriggerEvent==TriggerEvent.EnterZone?"zone on map":"line on map"),GUILayout.Height(30))) {
                if(newTriggerEvent==TriggerEvent.SseCollected) {
                    if(doc.Sse.Count==0)message="Place an SSE item through Props > SSE first.";
                    else {int id=Combatants.NextId(doc.Triggers.Select(t=>t.Id));Draft.Change(d=>d.Triggers.Add(new(id,"Trigger "+id,newTriggerEvent,SseId:doc.Sse[0].PropId)));SelectTrigger(id);}
                }else if(newTriggerEvent==TriggerEvent.BreakerOff) {
                    if(doc.Breakers.Count==0)message="Place a breaker box through Props or Lighting first.";
                    else {int id=Combatants.NextId(doc.Triggers.Select(t=>t.Id));Draft.Change(d=>d.Triggers.Add(new(id,"Trigger "+id,newTriggerEvent,BreakerId:doc.Breakers[0].PropId)));SelectTrigger(id);}
                }else if(newTriggerEvent==TriggerEvent.AIDeath) {
                    if(doc.Combatants.Count==0)message="Place the source AI first.";
                    else {int id=Combatants.NextId(doc.Triggers.Select(t=>t.Id));Draft.Change(d=>d.Triggers.Add(new(id,"Trigger "+id,newTriggerEvent,TriggerActors.SpecificAI,doc.Combatants[0].Id)));SelectTrigger(id);}
                }else {selectedTrigger=null;placingTrigger=true;message="Drag on the map to draw the trigger. Then add its actions.";}
            }
            GUI.enabled=true;
            if(placingTrigger){KillhouseUi.Note("Drag on the map to finish placement.");if(GUILayout.Button("Cancel drawing")){placingTrigger=false;draggingTrigger=false;}}
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
    }
    private void TriggerActionInspector(MapTrigger trigger) {
        var doc=Draft.Document;
        KillhouseUi.BeginSection("Saved actions · "+trigger.Actions.Count+" / 8");try {
            if(trigger.Actions.Count==0)KillhouseUi.Note("This trigger needs at least one action before it can be armed.");
            for(int n=0;n<trigger.Actions.Count;n++) {
                int at=n;var a=trigger.Actions[n];GUILayout.Label((n+1)+". "+TriggerActions[(int)a.Kind]+" → "+(a.Kind==TriggerActionKind.DespawnEnteringAI?"Each qualifying AI individually":string.Join(", ",a.Targets.Select(id=>a.Kind<=TriggerActionKind.ToggleLights?PowerCircuits.Label(doc,id):"AI "+id))));
                if(GUILayout.Button("Remove action "+(n+1))){Draft.Change(d=>d.Triggers.Single(t=>t.Id==trigger.Id).Actions.RemoveAt(at));return;}
            }
            GUI.enabled=trigger.Actions.Count<ScenarioTriggers.ActionLimit;
            if(GUILayout.Button("Add an action",GUILayout.Height(30))){addingTriggerAction=true;triggerTargets.Clear();}
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
        if(!addingTriggerAction)return;
        KillhouseUi.BeginSection("New action · choose what happens");try {
            var kind=(TriggerActionKind)Choices((int)triggerActionKind,TriggerActions,1);if(kind!=triggerActionKind){triggerActionKind=kind;triggerTargets.Clear();}
            bool entering=kind==TriggerActionKind.DespawnEnteringAI;
            if(!entering) {
                var choices=kind<=TriggerActionKind.ToggleLights?doc.Circuits.Select(c=>c.Id).Append(0).Distinct().OrderBy(id=>id):doc.Combatants.Select(b=>b.Id).OrderBy(id=>id);
                triggerTargetScroll=GUILayout.BeginScrollView(triggerTargetScroll,GUILayout.Height(128));
                try {foreach(int id in choices) {
                    string label=kind<=TriggerActionKind.ToggleLights?PowerCircuits.Label(doc,id):"AI "+id+" · "+doc.Combatants.First(b=>b.Id==id).Prefab.Split(':').Last();
                    bool chosen=GUILayout.Toggle(triggerTargets.Contains(id),label);if(chosen)triggerTargets.Add(id);else triggerTargets.Remove(id);
                }
                }finally{GUILayout.EndScrollView();}
            }
            KillhouseUi.Note(kind switch{TriggerActionKind.Spawn=>"All selected AI spawn as a group, one per update. Adding this action sets them to Deferred.",TriggerActionKind.SpawnRandom=>"Choose one available AI from the selected pool each time. Adding this action sets candidates to Deferred; give each a route for a random patrol.",TriggerActionKind.StartRoute=>"Start each selected AI's assigned route. Use Wait for trigger in its AI start setting. This can also release a civilian onto its escape route.",TriggerActionKind.Despawn=>"Remove every AI selected below when this trigger fires.",TriggerActionKind.DespawnEnteringAI=>ScenarioTriggers.CanDespawnEntering(trigger)?"Uses this zone/line's AI filter and Specific AI selections. Removes only AI that actually enter, with no separate action targets. Runs for every entry, independent of shared once/cooldown.":"Choose a zone or line with Any AI, Anyone or Specific AI first. Players-only, AI-death and breaker events cannot use this action.",_=>"Choose the lighting circuits to change. Whole house is the main feed; branch switches and shot-light damage are preserved."});
            GUI.enabled=(entering?ScenarioTriggers.CanDespawnEntering(trigger):triggerTargets.Count>0) && trigger.Actions.Count<ScenarioTriggers.ActionLimit;
            if(GUILayout.Button("Save action")) {
                var targets=entering?Array.Empty<int>():triggerTargets.OrderBy(id=>id).ToArray();
                if(kind==TriggerActionKind.StartRoute && targets.Any(id=>doc.Combatants.First(b=>b.Id==id).RouteId==0))message="Assign a route to every selected AI first.";
                else {Draft.Change(d=>{d.Triggers.Single(t=>t.Id==trigger.Id).Actions.Add(new(kind,targets));if(kind is TriggerActionKind.Spawn or TriggerActionKind.SpawnRandom)foreach(int id in targets)d.AIStarts[id]=AIStart.Deferred;});triggerTargets.Clear();addingTriggerAction=false;message="Trigger action added.";}
            }
            GUI.enabled=true;if(GUILayout.Button("Cancel new action")){addingTriggerAction=false;triggerTargets.Clear();}
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
    }
    private void AIStartInspector(Combatant bot) {
        var doc=Draft.Document;int old=doc.AIStarts.TryGetValue(bot.Id,out var mode)?(int)mode+1:0;
        GUILayout.Label("At encounter start");int next=Choices(old,new[]{"Active","Wait for trigger","Deferred spawn"},1);
        if(next!=old)Draft.Change(d=>{if(next==0)d.AIStarts.Remove(bot.Id);else d.AIStarts[bot.Id]=(AIStart)(next-1);});
        KillhouseUi.Note(next==1?"Spawns at its position and holds there until a Start route action.":next==2?"Does not spawn at encounter start. Select it in a Spawn group or Spawn random AI action.":"Spawns and follows its usual order when Start AI is chosen.");
    }
    private bool HandleTriggerMap() {
        if(tool!=Tool.Triggers)return false;
        var e=Event.current;
        if(e.type==EventType.MouseDown && e.button==0 && board.Contains(e.mousePosition)) {
            ClearFocus();var g=Grid(e.mousePosition)*2;
            if(placingTrigger){draggingTrigger=true;triggerFrom=triggerTo=g;}
            else {var picked=Draft.Document.Triggers.Where(t=>ScenarioTriggers.Spatial(t.Event)).OrderBy(t=>Math.Abs((t.X+t.EndX)/2-g.x)+Math.Abs((t.South+t.EndSouth)/2-g.y)).FirstOrDefault();if(picked!=null)SelectTrigger(picked.Id);}
            e.Use();
        }else if(e.type==EventType.MouseDrag && draggingTrigger){triggerTo=Grid(e.mousePosition)*2;e.Use();}
        else if(e.type==EventType.MouseUp && e.button==0 && draggingTrigger) {
            draggingTrigger=false;var doc=Draft.Document;var to=Grid(e.mousePosition)*2;
            float X(float v)=>Math.Clamp(TargetPlacement.Snap(v,.1f),0,doc.Columns*2);float Z(float v)=>Math.Clamp(TargetPlacement.Snap(v,.1f),0,doc.Rows*2);
            int id=SelectedTrigger?.Id??Combatants.NextId(doc.Triggers.Select(t=>t.Id));var next=(SelectedTrigger??new MapTrigger(id,"Trigger "+id,newTriggerEvent)) with{X=X(triggerFrom.x),South=Z(triggerFrom.y),EndX=X(to.x),EndSouth=Z(to.y)};
            var candidate=LayoutDraft.Copy(doc);candidate.Triggers.RemoveAll(t=>t.Id==id);candidate.Triggers.Add(next);
            var error=ScenarioTriggers.Check(candidate).FirstOrDefault(i=>i.Fatal);
            if(error!=null)message=error.Message;
            else {Draft.Change(d=>{d.Triggers.RemoveAll(t=>t.Id==id);d.Triggers.Add(next);});SelectTrigger(id);message="Trigger drawn. Add actions, save and apply the map, then arm at the laptop.";}
            e.Use();
        }
        return true;
    }
    private void DrawTriggers(float unit) {
        Vector2 P(float x,float south)=>new(board.x+x*unit/2,board.y+south*unit/2);
        void Shape(float x,float z,float bx,float bz,bool line,Color color){var a=P(x,z);var b=P(bx,bz);if(line)Line(a,b,3,color);else {var r=new Rect(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Abs(a.x-b.x),Math.Abs(a.y-b.y));Fill(r,new Color(color.r,color.g,color.b,.1f));Line(new(r.x,r.y),new(r.xMax,r.y),2,color);Line(new(r.xMax,r.y),new(r.xMax,r.yMax),2,color);Line(new(r.xMax,r.yMax),new(r.x,r.yMax),2,color);Line(new(r.x,r.yMax),new(r.x,r.y),2,color);}}
        foreach(var t in Draft.Document.Triggers.Where(t=>ScenarioTriggers.Spatial(t.Event))) {
            var color=t.Id==selectedTrigger?Selected:new Color(.35f,.9f,1,.65f);Shape(t.X,t.South,t.EndX,t.EndSouth,t.Event==TriggerEvent.CrossLine,color);
            var p=P((t.X+t.EndX)/2,(t.South+t.EndSouth)/2);GUI.Label(new(Math.Clamp(p.x,board.x,board.xMax-55),Math.Clamp(p.y,board.y,board.yMax-22),55,22),"TR "+t.Id,small!);
        }
        if(draggingTrigger)Shape(triggerFrom.x,triggerFrom.y,Math.Clamp(triggerTo.x,0,Draft.Document.Columns*2),Math.Clamp(triggerTo.y,0,Draft.Document.Rows*2),(SelectedTrigger?.Event??newTriggerEvent)==TriggerEvent.CrossLine,Selected);
    }
}
