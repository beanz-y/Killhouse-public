using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class MapEditor
{
    private int lightingSeenId=-1,lightingSeenRevision=-1;
    private float fixturePercent=100;
    private Vector2 lightingFixtureScroll;
    private string fixtureKelvin="6500",fixtureRange="16";
    private int circuitEdit,circuitSeen=-1,circuitRevision=-1;
    private string circuitName="Whole house";
    private bool circuitStartsOn=true,circuitIndependent;
    private float circuitPercent=100;

    private void PlaceElectrical(string asset) {
        ChangeTool(Tool.Props);propAsset=asset;propHeight=DefaultPropHeight(asset);propOnTop=false;powerProp=-1;
        message="Place "+PropLabel(asset)+" at a green position, then open Lighting to wire it.";
    }
    private void LightingInspector() {
        var doc=Draft.Document;
        KillhouseUi.BeginSection("Lighting");
        try {
            KillhouseUi.Note("Place lights and breaker boxes through Props. Select their map markers or the list below to wire them.");
            foreach(string asset in new[]{"Wall lamp","Work light","Bulkhead wall light","Shade wall sconce","Glass wall sconce","Desk lamp",PowerCircuits.BreakerAsset}) {
                bool enabled=GUI.enabled;GUI.enabled=enabled && propShapes.ContainsKey(asset);
                if(GUILayout.Button("Place "+PropLabel(asset)))PlaceElectrical(asset);
                GUI.enabled=enabled;
            }
            if(!propShapes.ContainsKey(PowerCircuits.BreakerAsset))KillhouseUi.Note(NativeBreakerAssets.Ready?"Breaker box unavailable: the native model could not load. Details are in the game log.":"Preparing breaker box model…");
            KillhouseUi.Note("Brightness and wiring save with the map. Save and apply a revision to preview them in the house.");
        }finally{KillhouseUi.EndSection();}
        var piece=selectedProp is {} index && index>=0 && index<doc.Props.Count?doc.Props[index]:null;
        if(piece!=null && PowerCircuits.IsElectrical(piece.Name))FixtureWiring(piece);

        KillhouseUi.BeginSection($"Placed fixtures · {doc.Lights.Count}/{PowerCircuits.MaxLights} lights, {doc.Breakers.Count} breakers");
        try {
            lightingFixtureScroll=GUILayout.BeginScrollView(lightingFixtureScroll,GUILayout.Height(130));
            try {
            foreach(var prop in doc.Props.Where(p=>PowerCircuits.IsElectrical(p.Name)).ToArray()) {
                int circuit=doc.Lights.FirstOrDefault(l=>l.PropId==prop.Id)?.Circuit??doc.Breakers.FirstOrDefault(b=>b.PropId==prop.Id)?.Circuit??0;
                bool selected=selectedProp is {} at && at>=0 && at<doc.Props.Count && doc.Props[at].Id==prop.Id;
                if(GUILayout.Button((selected?"● ":"")+PropLabel(prop.Name)+" #"+prop.Id+" → "+PowerCircuits.Label(doc,circuit))) {
                    ClearSelection();selectedProp=doc.Props.FindIndex(p=>p.Id==prop.Id);lightingSeenId=-1;
                }
            }
            }finally{GUILayout.EndScrollView();}
            if(doc.Lights.Count+doc.Breakers.Count==0)KillhouseUi.Note("No electrical props placed yet.");
            KillhouseUi.Note("Work lights cost more than single-bulb lamps. Dense maps automatically reduce shadow detail. Placed light shadows in Mod Controls offers Reduced or Off for extra performance.");
        }finally{KillhouseUi.EndSection();}
        CircuitControls();
    }
    private void FixtureWiring(PlacedProp prop) {
        var doc=Draft.Document;var light=doc.Lights.FirstOrDefault(l=>l.PropId==prop.Id);var breaker=doc.Breakers.FirstOrDefault(b=>b.PropId==prop.Id);
        if(light==null && breaker==null)return;
        if(lightingSeenId!=prop.Id || lightingSeenRevision!=Draft.Revision) {
            lightingSeenId=prop.Id;lightingSeenRevision=Draft.Revision;
            fixturePercent=light==null?100:FixtureLighting.Percent(light);
            fixtureKelvin=(light==null?6500:FixtureLighting.Temperature(light)).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            fixtureRange=(light==null?16:FixtureLighting.Reach(light)).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        }
        KillhouseUi.BeginSection(PropLabel(prop.Name)+" #"+prop.Id+" · wiring");
        try {
            int current=light?.Circuit??breaker!.Circuit;
            var ids=doc.Circuits.Where(c=>c.Id>0).Select(c=>c.Id).OrderBy(n=>n).Prepend(0).ToArray();
            int picked=Choices(Math.Max(0,Array.IndexOf(ids,current)),ids.Select(i=>PowerCircuits.Label(doc,i)).ToArray());
            if(ids[picked]!=current)Draft.Change(d=> {
                if(light!=null)d.Lights[d.Lights.FindIndex(l=>l.PropId==prop.Id)]=light with{Circuit=ids[picked]};
                else d.Breakers[d.Breakers.FindIndex(b=>b.PropId==prop.Id)]=breaker! with{Circuit=ids[picked]};
            });
            if(breaker!=null)KillhouseUi.Note("Whole house switches dependent fixtures while keeping branch positions. Independent circuits keep their own supply. Other assignments switch that branch. Aim at the box in play and use your Interact key.");
            else {
                GUILayout.Label($"Fixture brightness · {fixturePercent:0}%");
                fixturePercent=GUILayout.HorizontalSlider(fixturePercent,10,500);
                GUILayout.BeginHorizontal();try {
                    if(GUILayout.Button("Dimmer −25%"))fixturePercent=Math.Max(10,fixturePercent-25);
                    if(GUILayout.Button("Brighter +25%"))fixturePercent=Math.Min(500,fixturePercent+25);
                }finally{GUILayout.EndHorizontal();}
                GUILayout.Label("Colour temperature · 2000–10000 K");fixtureKelvin=GUILayout.TextField(fixtureKelvin,10);
                GUILayout.Label("Light coverage · 4–24 m");fixtureRange=GUILayout.TextField(fixtureRange,10);
                if(GUILayout.Button("Apply fixture brightness & colour") && Number(fixtureKelvin,out float kelvin) && Number(fixtureRange,out float range)) {
                    var next=light! with{Lumens=FixtureLighting.FromPercent(fixturePercent),Kelvin=kelvin,Range=range/2};
                    var copy=LayoutDraft.Copy(doc);copy.Lights[copy.Lights.FindIndex(l=>l.PropId==prop.Id)]=next;
                    var issue=PowerCircuits.Check(copy).FirstOrDefault(i=>i.Fatal);
                    if(issue!=null)message=issue.Message;
                    else {Draft.Change(d=>d.Lights[d.Lights.FindIndex(l=>l.PropId==prop.Id)]=next);message="Fixture brightness updated in the draft.";}
                }
                KillhouseUi.Note("The fixture and circuit brightness combine. Whole house brightness also applies to dependent branches.");
            }
            if(GUILayout.Button("Move / rotate in Props")){tool=Tool.Select;message="Drag the selected fixture to move it; wheel or R rotates it.";}
        }finally{KillhouseUi.EndSection();}
    }
    private void CircuitControls() {
        var doc=Draft.Document;var ids=doc.Circuits.Where(c=>c.Id>0).Select(c=>c.Id).OrderBy(n=>n).Prepend(0).ToArray();
        if(!ids.Contains(circuitEdit))circuitEdit=0;
        KillhouseUi.BeginSection("Circuits · dimmers & reset state");
        try {
            circuitEdit=ids[Choices(Array.IndexOf(ids,circuitEdit),ids.Select(i=>PowerCircuits.Label(doc,i)).ToArray())];
            var circuit=doc.Circuits.FirstOrDefault(c=>c.Id==circuitEdit)??new(0,"Whole house");
            if(circuitSeen!=circuitEdit || circuitRevision!=Draft.Revision) {
                circuitSeen=circuitEdit;circuitRevision=Draft.Revision;circuitName=circuit.Name;circuitStartsOn=circuit.StartsOn;circuitPercent=circuit.Brightness*100;circuitIndependent=circuit.Independent;
            }
            if(circuitEdit>0){GUILayout.Label("Circuit name");circuitName=GUILayout.TextField(circuitName,48);}
            circuitStartsOn=GUILayout.Toggle(circuitStartsOn,"Starts on after range reset");
            if(circuitEdit>0)circuitIndependent=GUILayout.Toggle(circuitIndependent,"Independent power · ignores main switch & dimmer");
            GUILayout.Label($"Circuit brightness · {circuitPercent:0}%");circuitPercent=GUILayout.HorizontalSlider(circuitPercent,0,200);
            if(GUILayout.Button("Apply circuit settings")) {
                var next=circuit with{Name=circuitEdit==0?"Whole house":circuitName.Trim(),StartsOn=circuitStartsOn,Brightness=circuitPercent/100,Independent=circuitEdit>0 && circuitIndependent};
                if(next.Name.Length==0 || next.Name.Any(char.IsControl))message="Enter a circuit name.";
                else if(next!=circuit) {Draft.Change(d=>{d.Circuits.RemoveAll(c=>c.Id==circuitEdit);d.Circuits.Add(next);});message="Circuit settings updated in the draft.";}
            }
            if(doc.Circuits.Count(c=>c.Id>0)<PowerCircuits.MaxCircuits && GUILayout.Button("Add circuit")) {
                int next=Enumerable.Range(1,PowerCircuits.MaxCircuits).First(i=>!doc.Circuits.Any(c=>c.Id==i));
                Draft.Change(d=>d.Circuits.Add(new(next,"Circuit "+next)));circuitEdit=next;circuitSeen=-1;
            }
            if(circuitEdit>0 && GUILayout.Button("Remove circuit · reconnect to Whole house"))Draft.Change(d=> {
                ScenarioTriggers.RemoveCircuit(d,circuitEdit);d.Circuits.RemoveAll(c=>c.Id==circuitEdit);
                for(int i=0;i<d.Lights.Count;i++)if(d.Lights[i].Circuit==circuitEdit)d.Lights[i]=d.Lights[i] with{Circuit=0};
                for(int i=0;i<d.Breakers.Count;i++)if(d.Breakers[i].Circuit==circuitEdit)d.Breakers[i]=d.Breakers[i] with{Circuit=0};
            });
            KillhouseUi.Note("0% dims a circuit fully without changing its switch position. Range reset restores the saved on/off state and brightness.");
        }finally{KillhouseUi.EndSection();}
    }
    private bool HandleLightingMap() {
        if(tool!=Tool.Lighting)return false;
        var e=Event.current;
        if(e.type==EventType.MouseDown && e.button==0 && mapCanvas.Contains(e.mousePosition)) {
            var grid=Grid(e.mousePosition);float x=ConstructionPlan.Left+grid.x*2,z=ConstructionPlan.North-grid.y*2;
            var hit=DisplayFurniture().Where(p=>p.PropIndex>=0 && PowerCircuits.IsElectrical(p.Asset) && PropPlacement.Hit(p.Pose.Footprint,x,z)).OrderByDescending(p=>p.Height).FirstOrDefault();
            ClearSelection();if(hit!=null)selectedProp=hit.PropIndex;ClearFocus();e.Use();
        }
        return true;
    }
    private void DrawCircuitWires(float unit) {
        if(tool!=Tool.Lighting)return;
        var doc=Draft.Document;
        Vector2 Position(PlacedProp prop) {
            var(x,z)=PropPlacement.Position(prop);
            return new(board.x+(x-ConstructionPlan.Left)/2*unit,board.y+(ConstructionPlan.North-z)/2*unit);
        }
        foreach(var breaker in doc.Breakers) {
            var source=doc.Props.FirstOrDefault(p=>p.Id==breaker.PropId);if(source==null)continue;
            foreach(var light in doc.Lights.Where(l=>breaker.Circuit==0?!PowerCircuits.Independent(doc,l.Circuit):l.Circuit==breaker.Circuit)) {
                var dest=doc.Props.FirstOrDefault(p=>p.Id==light.PropId);if(dest==null)continue;
                GhostLine(Position(source),Position(dest),1,breaker.Circuit==0?new Color(1,.75f,.3f,.45f):new Color(.3f,.8f,1,.7f));
            }
        }
    }
}
