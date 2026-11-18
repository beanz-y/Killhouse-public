using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class MapEditor
{
    private int? selectedProp;
    private bool selectedAutomatic;
    private string? propAsset;
    private string propSearch="";
    private bool sseOnly;
    private float propYaw,propHeight;
    private float propSnap=.1f;
    private bool propFree=true;
    private float PropPositionStep()=>PropRules.Step(propFree?0:propSnap);
    private bool propOnTop;
    private int propSurfaceSelection=-1,pickCycle;
    private Vector2 pickPosition=new(-100,-100);
    private Vector2 paletteScroll;
    private int furnitureRevision=-1,furnitureContext=-1;
    private IReadOnlyDictionary<string,PropShape> propShapes=new Dictionary<string,PropShape>();
    private FurnitureResult? furniturePreview;
    private string[] propPalette=Array.Empty<string>();
    private readonly PropPreviewCache propPreviewCache=new();
    private PropPreview? cursorPropPreview;

    private void EnsureFurniture() {
        if(furnitureRevision==Draft.Revision && furnitureContext==Runtime.EditorPreviewContext)return;
        propShapes=Runtime.EditorPropShapes;
        propPalette=propShapes.Where(p=>p.Value.Valid && !FixtureLighting.Retired(p.Key) && PropNames.Key(p.Key)==p.Key && !p.Key.Contains("Target",StringComparison.OrdinalIgnoreCase)).Select(p=>p.Key).OrderBy(PropLabel,StringComparer.OrdinalIgnoreCase).ToArray();
        if(propAsset==null || !propShapes.ContainsKey(propAsset))propAsset=propPalette.FirstOrDefault();
        furniturePreview=Runtime.PreviewFurniture(Draft.Document);
        furnitureRevision=Draft.Revision;furnitureContext=Runtime.EditorPreviewContext;
    }
    private static string PropLabel(string name)=>name switch {
        "CrCrateA1B4"=>"Crate", "PrpSandBagsWallA1"=>"Sandbag wall", "PrpHescoA1"=>"Hesco barrier", "PrpPalletB1"=>"Pallet", "PrpFireExtFastA01"=>"Fire extinguisher",
        "SM_FoldingChair_LOD16"=>"Folding chair", "SM_FoldingTable_01_LOD9"=>"Folding table", "SM_Laundry_Bag_01_LOD0"=>"Laundry bag", "SM_MERGED_PlasticStorages01"=>"Plastic storage 1", "SM_MERGED_PlasticStorages03"=>"Plastic storage 3",
        "Sandbag_Barrier_udmhefrqx_Mid"=>"Sandbag barrier", "Fabric09_HDRP"=>"Fabric cover", "T_toilet"=>"Toilet", "Power box"=>"Breaker box", _=>FixtureMount.Label(name).Replace('_',' ')
    };
    private IEnumerable<FurnitureItem> DisplayFurniture() {
        var planned=furniturePreview?.Items??Array.Empty<FurnitureItem>();
        foreach(var item in planned)yield return item;
        // Invalid imported/draft pieces still need a selectable footprint so
        // they can be moved or deleted instead of disappearing from the editor.
        for(int index=0;index<Draft.Document.Props.Count;index++) {
            if(planned.Any(p=>p.PropIndex==index))continue;
            var prop=Draft.Document.Props[index];var shape=PropPlacement.Shape(prop,propShapes);if(shape==null)continue;
            var(x,z)=PropPlacement.Position(prop);yield return new(prop.Name,new(x,z,prop.Yaw,shape.At(x,z,prop.Yaw),false),prop.Height,"unresolved prop",true,index,prop.Id,prop.SupportId);
        }
    }
    private FurnitureItem? SelectedFurniture()=>selectedProp is {} index?DisplayFurniture().FirstOrDefault(p=>p.PropIndex==index):null;
    private IReadOnlyList<Issue> PreviewPropObstacles(Layout before,PlacedProp prop,int? previous) {
        EnsureFurniture();
        if(prop.SupportId<0)return Array.Empty<Issue>(); // Native check explains missing/ineligible support.
        var branch=previous is {} at?LayoutDraft.PropTree(before,before.Props[at].Id):new HashSet<int>();
        var candidate=Draft.WithProp(previous,prop);branch.Add(prop.Id);
        // Fixed pieces are checked together in the transformed candidate by
        // the native validator, including ancestor/descendant support regions.
        var existing=DisplayFurniture().Where(p=>p.FixedId==0 && (previous==null || p.PropIndex!=previous)).ToArray();
        var issues=new List<Issue>();foreach(var changed in candidate.Props.Where(p=>branch.Contains(p.Id)))issues.AddRange(PropPlacement.CheckAgainst(changed,propShapes,existing));return issues;
    }
    private FurnitureItem? HitFurniture(Vector2 grid,bool cycle=false) {
        float x=ConstructionPlan.Left+grid.x*2,z=ConstructionPlan.North-grid.y*2;
        var hits=DisplayFurniture().Where(p=>PropPlacement.Hit(p.Pose.Footprint,x,z)).OrderByDescending(p=>p.Height).ThenBy(p=>p.Explicit?0:1).ThenBy(p=>p.Pose.Footprint.HalfX*p.Pose.Footprint.HalfZ).ToArray();
        if(!cycle || (grid-pickPosition).magnitude>.15f)pickCycle=0;else pickCycle++;
        pickPosition=grid;return hits.Length==0?null:hits[pickCycle%hits.Length];
    }
    private bool propRulesOpen;
    private void PropsInspector() {
        if(tool!=Tool.Props && selectedProp==null && !selectedAutomatic)return;
        var doc=Draft.Document;
        var selected=selectedProp is {} at && at>=0 && at<doc.Props.Count?doc.Props[at]:null;
        if(selected!=null && propSurfaceSelection!=selected.Id){propSurfaceSelection=selected.Id;propOnTop=selected.SupportId>0;}
        string? asset=selected?.Name??(tool==Tool.Props?propAsset:null);
        if(tool==Tool.Props) {
            KillhouseUi.BeginSection("Furniture");
            try {
                if(propAsset==null)KillhouseUi.Note("Furniture is not ready in this house.");
                bool filter=Choices(sseOnly?1:0,new[]{"All props","SSE"})==1;
                if(filter!=sseOnly){sseOnly=filter;paletteScroll=Vector2.zero;if(sseOnly && !SseObjectives.IsAsset(propAsset??""))propAsset=propPalette.FirstOrDefault(SseObjectives.IsAsset);}
                if(sseOnly)KillhouseUi.Note(NativeSseAssets.Status);
                GUILayout.Label("Search");GUI.SetNextControlName("kh-prop-search");propSearch=GUILayout.TextField(propSearch,80);
                paletteScroll=GUILayout.BeginScrollView(paletteScroll,GUILayout.Height(150));
                try {
                    foreach(string model in propPalette.Where(a=>(!sseOnly || SseObjectives.IsAsset(a)) && (PropLabel(a).Contains(propSearch,StringComparison.OrdinalIgnoreCase) || a.Contains(propSearch,StringComparison.OrdinalIgnoreCase))))
                        if(GUILayout.Button((model==propAsset?"● ":"")+PropLabel(model))){propAsset=model;propHeight=DefaultPropHeight(model);powerProp=-1;if(!PropRules.For(model).CanRest)propOnTop=false;ClearFocus();}
                }finally{GUILayout.EndScrollView();}
            }finally{KillhouseUi.EndSection();}
            asset=propAsset;
        }
        if(asset!=null) {
            if(selected!=null) {
                KillhouseUi.BeginSection("Selected furniture");
                try {
                    GUILayout.Label(PropLabel(asset));
                    if(SelectedFurniture() is {} piece){var f=piece.Pose.Footprint;KillhouseUi.Note($"{f.HalfX*2:0.##} × {f.HalfZ*2:0.##} m · height {selected.Height:0.##} m");}
                }finally{KillhouseUi.EndSection();}
            }
            KillhouseUi.BeginSection("Placement");
            try {
                GUILayout.Label("Position");propFree=Choices(propFree?0:1,new[]{"Free place","Snap to grid"})==0;
                if(!propFree){int value=propSnap<=.05f?0:propSnap<=.1f?1:propSnap<=.25f?2:3;propSnap=new[]{.05f,.1f,.25f,.5f}[Choices(value,new[]{"5 cm","10 cm","25 cm","50 cm"})];alignWalls=GUILayout.Toggle(alignWalls,"Align near wall faces");}
                RotationStepPicker();
                if(PowerCircuits.IsElectrical(asset) && !FixtureMount.Portable(asset))KillhouseUi.Note(FixtureMount.Wall(asset)?"Snaps to a solid wall. Set the mounting height below.":"Fixture height is adjustable below.");
                else if(PropRules.For(asset).CanRest){GUILayout.Label("Surface");propOnTop=Choices(propOnTop?1:0,new[]{"Floor","Prop surface"})==1;}
                else KillhouseUi.Note("Floor placement only.");
                if(propOnTop)KillhouseUi.Note("Rotate or offset flat stacks freely. Keep the centre and at least 65% supported; adjacent surfaces can share the load.");
            }finally{KillhouseUi.EndSection();}
            KillhouseUi.BeginSection("Facing");
            try {
                KillhouseUi.Note("Wheel over map: free rotate · R / Shift+R: snap");
                GUILayout.Label($"Angle: {selected?.Yaw??propYaw:0.###}°");
                GUILayout.BeginHorizontal();try {
                    if(GUILayout.Button($"Left {rotationStep}°  [Shift+R]"))RotateProp(true);
                    if(GUILayout.Button($"Right {rotationStep}°  [R]"))RotateProp();
                }finally{GUILayout.EndHorizontal();}
            }finally{KillhouseUi.EndSection();}
            propRulesOpen=KillhouseUi.Foldout(propRulesOpen,"Placement rules");
            if(propRulesOpen){KillhouseUi.BeginSection("Contact & stacking");try{KillhouseUi.Note(PropRules.For(asset).Description);}finally{KillhouseUi.EndSection();}}
        }
        PowerInspector(selected);SseInspector(selected);
        if(selected!=null && selectedProp is {} index) {
            PropPositionPicker(index,selected);
            int children=LayoutDraft.PropTree(doc,selected.Id).Count-1;
            KillhouseUi.BeginSection("Actions");
            try {
                if(children>0)KillhouseUi.Note($"{children} stacked piece(s) move, rotate and delete with this base.");
                if(GUILayout.Button("Duplicate · choose position")) {
                    propAsset=SelectedFurniture()?.Asset??selected.Name;propYaw=selected.Yaw;propHeight=selected.Height;propOnTop=selected.SupportId>0;
                    ClearSelection();tool=Tool.Props;ClearFocus();message="Choose a green position for the duplicate.";
                }
                if(GUILayout.Button(children>0?"Delete furniture & stack":"Delete furniture")){Draft.RemoveProp(index);ClearSelection();message="Furniture deleted. Undo restores it.";}
            }finally{KillhouseUi.EndSection();}
        }
        if(selectedAutomatic || tool==Tool.Props && (doc.Themes.Count>0 || doc.Props.Any(p=>p.Id==0))) {
            KillhouseUi.BeginSection("Imported furniture");
            try {
                KillhouseUi.Note("Convert existing furniture to individual props to move, duplicate or stack it.");
                bool enabled=GUI.enabled;GUI.enabled=furniturePreview is {Unresolved.Count:0} && doc.Props.Count+furniturePreview.Items.Count(p=>!p.Explicit)<=PropPlacement.Limit;
                if(GUILayout.Button("Make all furniture individual"))ConvertThemes();GUI.enabled=enabled;
            }finally{KillhouseUi.EndSection();}
        }
        var unavailable=doc.Props.Select((p,i)=>(p,i)).Where(v=>PropPlacement.Shape(v.p,propShapes)==null).ToArray();
        if(unavailable.Length>0) {
            KillhouseUi.BeginSection("Unavailable furniture");
            try {foreach(var pair in unavailable)if(GUILayout.Button("Delete "+PropLabel(pair.p.Name))){Draft.RemoveProp(pair.i);ClearSelection();break;}}
            finally{KillhouseUi.EndSection();}
        }
    }
    private void ConvertThemes() {
        var items=furniturePreview?.Items.ToArray()??Array.Empty<FurnitureItem>();
        var candidate=LayoutDraft.Copy(Draft.Document);var used=candidate.Props.Select(p=>p.Id).ToHashSet();
        foreach(var item in items) {
            if(item.PropIndex>=0 && candidate.Props[item.PropIndex].Id>0)continue;
            int id=Enumerable.Range(1,used.Count+1).First(n=>!used.Contains(n));used.Add(id);
            var fixedProp=new PlacedProp(item.Asset,item.Pose.X-ConstructionPlan.PropLeft,item.Pose.Z-ConstructionPlan.PropSouth,item.Pose.Yaw,item.Height,id);
            if(item.PropIndex>=0)candidate.Props[item.PropIndex]=fixedProp;else candidate.Props.Add(fixedProp);
        }
        candidate.Themes.Clear();
        var issues=Runtime.PreviewPropIssues(candidate).Concat(LayoutValidation.Check(candidate,Plugin.Strict.Value)).Where(i=>i.Fatal).ToArray();
        if(issues.Length>0){message="Themes unchanged: "+issues[0].Message;return;}
        Draft.Replace(candidate);ClearSelection();tool=Tool.Select;message="Furniture is now individual and can support stacks. Undo restores its previous layout.";
    }
    private PlacedProp? PropCandidate(Vector2 origin,Vector2 pointer,int? previous=null,bool aim=false) {
        if(previous is {} index && index>=0 && index<Draft.Document.Props.Count) {
            var old=Draft.Document.Props[index];var shape=PropPlacement.Shape(old,propShapes);if(shape==null)return null;
            var item=SelectedFurniture();var at=item==null?PropPlacement.Grid(old,shape):((item.Pose.Footprint.X-ConstructionPlan.Left)/2,(ConstructionPlan.North-item.Pose.Footprint.Z)/2);
            var moved=PropPlacement.At(item?.Asset??old.Name,shape,at.Item1+pointer.x-start.x,at.Item2+pointer.y-start.y,dragYaw??old.Yaw,old.Id>0?old.Id:Draft.NextPropId(),old.Height,PropPositionStep());
            var branch=LayoutDraft.PropTree(Draft.Document,old.Id);
            return SurfaceProp(AlignProp(moved,shape),shape,propShapes,DisplayFurniture().Where(p=>!branch.Contains(p.FixedId)),propOnTop);
        }
        if(propAsset==null || !propShapes.TryGetValue(propAsset,out var model))return null;
        float angle=dragYaw??(aim?TargetEditSteps.Aim(origin.x,origin.y,pointer.x,pointer.y,rotationStep,propYaw)??propYaw:propYaw);
        return SurfaceProp(AlignProp(PropPlacement.At(propAsset,model,origin.x,origin.y,angle,Draft.NextPropId(),propHeight,PropPositionStep()),model),model,propShapes,DisplayFurniture(),propOnTop);
    }
    private PlacedProp AlignProp(PlacedProp prop,PropShape shape) {
        if(FixtureMount.Mounted(prop.Name))return Runtime.AlignEditorFixture(Draft.Document,prop,shape);
        if(propFree || !alignWalls)return prop;
        var(x,z)=PropPlacement.Position(prop);var delta=PlacementClearance.Align(shape.At(x,z,prop.Yaw),Runtime.PreviewArchitecture(Draft.Document).Where(s=>WindowGeometry.AtHeight(s,prop.Height+.01f,prop.Height+.01f+shape.MaxY-shape.MinY)).ToArray(),Math.Min(.15f,propSnap*.6f+.03f));
        return OuterWallFloor.Align(Draft.Document,prop with{X=prop.X+delta.X,Z=prop.Z+delta.Z},shape);
    }
    private void PropFeedback(string success)=>message=Draft.LastRejection==null?success:"Furniture unchanged: "+Draft.LastRejection;
    private void PlaceProp(Vector2 origin,Vector2 pointer) {
        if(PropCandidate(origin,pointer,aim:true) is {} candidate){Draft.CommitProp(null,candidate);PropFeedback("Furniture placed.");}
    }
    private void MoveProp(int index,Vector2 pointer) {
        if(PropCandidate(start,pointer,index) is {} candidate){Draft.CommitProp(index,candidate);PropFeedback("Furniture moved.");}
    }
    private void RotateProp(bool reverse=false,float? freeYaw=null) {
        if(tool==Tool.Props){propYaw=freeYaw??TargetEditSteps.Turn(propYaw,rotationStep,reverse);return;}
        if(selectedProp is not {} index || index<0 || index>=Draft.Document.Props.Count)return;
        var old=Draft.Document.Props[index];var shape=PropPlacement.Shape(old,propShapes);if(shape==null)return;
        var item=SelectedFurniture();var at=item==null?PropPlacement.Grid(old,shape):((item.Pose.Footprint.X-ConstructionPlan.Left)/2,(ConstructionPlan.North-item.Pose.Footprint.Z)/2);
        var candidate=PropPlacement.At(item?.Asset??old.Name,shape,at.Item1,at.Item2,freeYaw??TargetEditSteps.Turn(old.Yaw,rotationStep,reverse),old.Id>0?old.Id:Draft.NextPropId(),old.Height,freeYaw.HasValue?0:PropPositionStep());
        var branch=LayoutDraft.PropTree(Draft.Document,old.Id);
        candidate=SurfaceProp(AlignProp(candidate,shape),shape,propShapes,DisplayFurniture().Where(p=>!branch.Contains(p.FixedId)),old.SupportId>0);
        Draft.CommitProp(index,candidate);PropFeedback("Furniture rotated.");
    }
    private void DrawPropPreview(float unit) {
        cursorPropPreview=null;var pointer=Event.current.mousePosition;
        if(readOnly || !mapCanvas.Contains(pointer))return;
        var g=Grid(pointer);PlacedProp? candidate=null;int? previous=null;
        if(tool==Tool.Props)candidate=PropCandidate(drawing?start:g,g,aim:true);
        else if(tool==Tool.Select && drawing && selectedProp is {} index){previous=index;candidate=PropCandidate(start,g,index);}
        if(candidate==null || PropPlacement.Shape(candidate,propShapes) is not {} shape)return;
        cursorPropPreview=propPreviewCache.Get(Draft,candidate,previous,Runtime.EditorPreviewContext);
        var color=cursorPropPreview.CanPlace?new Color(.20f,.95f,.40f):new Color(1,.25f,.20f);
        var(x,z)=PropPlacement.Position(candidate);DrawPropFootprint(shape.At(x,z,candidate.Yaw),unit,color,2,PropLabel(candidate.Name)+$" · {candidate.Height:0.##} m");
        if(previous is {} at) {
            var moved=Draft.WithProp(at,candidate);var branch=LayoutDraft.PropTree(moved,candidate.Id);
            foreach(var child in moved.Props.Where(p=>p.Id!=candidate.Id && branch.Contains(p.Id)))if(PropPlacement.Shape(child,propShapes) is {} childShape) {
                var(cx,cz)=PropPlacement.Position(child);DrawPropFootprint(childShape.At(cx,cz,child.Yaw),unit,color,1,PropLabel(child.Name)+$" · {child.Height:0.##} m");
            }
        }
    }
    private void DrawFurniture(float unit) {
        foreach(var item in DisplayFurniture()) {
            var color=selectedProp==item.PropIndex && item.PropIndex>=0?Selected:item.Explicit?new Color(.70f,.68f,.53f):new Color(.40f,.47f,.43f);
            DrawPropFootprint(item.Pose.Footprint,unit,color,selectedProp==item.PropIndex?2:1,PropLabel(item.Asset)+(item.Height>.01f?$" · {item.Height:0.##} m":""));
        }
    }
    private void DrawPropFootprint(Footprint footprint,float unit,Color color,float width,string label) {
        Vector2 P(float x,float z)=>new(board.x+(x-ConstructionPlan.Left)/2*unit,board.y+(ConstructionPlan.North-z)/2*unit);
        var corners=footprint.Corners.Select(p=>P(p.X,p.Z)).ToArray();
        for(int n=0;n<4;n++)GhostLine(corners[n],corners[(n+1)%4],width,color);
        var center=P(footprint.X,footprint.Z);float angle=footprint.Yaw*Mathf.Deg2Rad;var dir=new Vector2(MathF.Sin(angle),-MathF.Cos(angle));var side=new Vector2(-dir.y,dir.x);var end=center+dir*Math.Min(18,unit*.25f);
        GhostLine(center,end,2,color);GhostLine(end,end-dir*5+side*3,2,color);GhostLine(end,end-dir*5-side*3,2,color);
        GhostLabel(new(center.x-30,center.y+3,100,30),label,color);
    }
}
