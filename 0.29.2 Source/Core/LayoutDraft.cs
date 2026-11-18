namespace OperatorKillhouse.Core;

// All editing is managed data. No live catalog or native geometry is mutated.
public sealed class LayoutDraft
{
    private readonly List<string> undo=new(),redo=new();
    public Layout Document {get;private set;}
    public int Revision {get;private set;}
    public bool CanUndo=>undo.Count>0;
    public bool CanRedo=>redo.Count>0;
    public bool StrictValidation {get;set;}
    public string? LastRejection {get;private set;}
    private readonly Func<Layout,TargetRef,IReadOnlyList<Issue>>? targetValidation;
    private readonly Func<Layout,IReadOnlyList<Issue>>? propValidation;
    private readonly Func<Layout,PlacedProp,int?,IReadOnlyList<Issue>>? propEditValidation;
    private readonly Func<Layout,Target,Target>? targetAlignment;
    private readonly Func<Layout,IReadOnlyList<Issue>>? windowValidation;
    public LayoutDraft(Layout layout,Func<Layout,TargetRef,IReadOnlyList<Issue>>? targetValidation=null,Func<Layout,IReadOnlyList<Issue>>? propValidation=null,Func<Layout,PlacedProp,int?,IReadOnlyList<Issue>>? propEditValidation=null,Func<Layout,Target,Target>? targetAlignment=null,Func<Layout,IReadOnlyList<Issue>>? windowValidation=null){Document=Copy(layout);this.targetValidation=targetValidation;this.propValidation=propValidation;this.propEditValidation=propEditValidation;this.targetAlignment=targetAlignment;this.windowValidation=windowValidation;}
    public static Layout Copy(Layout layout)=>LayoutFormat.Read(LayoutFormat.Write(layout)).Layout??throw new ArgumentException("Invalid draft.");
    public static Layout Blank() {var layout=new Layout(10,10){Name="New layout"};layout.Enclose();return layout;}
    public bool Change(Action<Layout> edit) {
        string before=LayoutFormat.Write(Document);
        try {edit(Document);} catch {Document=LayoutFormat.Read(before).Layout!;throw;}
        if(LayoutFormat.Write(Document)==before)return false;
        undo.Add(before);if(undo.Count>100)undo.RemoveAt(0);redo.Clear();Revision++;return true;
    }
    public void Replace(Layout layout)=>Change(_=>Document=Copy(layout));
    public bool Undo() {
        if(!CanUndo)return false;redo.Add(LayoutFormat.Write(Document));Restore(undo);return true;
    }
    public bool Redo() {
        if(!CanRedo)return false;undo.Add(LayoutFormat.Write(Document));Restore(redo);return true;
    }
    private void Restore(List<string> history) {Document=LayoutFormat.Read(history[^1]).Layout!;history.RemoveAt(history.Count-1);Revision++;}
    public void Barriers(IEnumerable<Edge> edges,Barrier kind)=>Change(doc=> {
        foreach(var edge in edges.Where(e=>BoundaryWalls.CanEdit(doc,e,kind))) {
            if(!doc.InteriorEdge(edge)){if(kind==Barrier.Wall)doc.BoundaryWalls.Add(edge);else doc.BoundaryWalls.Remove(edge);continue;}
            doc.Edges[edge]=kind;
            if(kind==Barrier.Door)doc.DoorOptions.TryAdd(edge,new());else {doc.DoorOptions.Remove(edge);doc.DoorLocks.Remove(edge);}
            if(kind!=Barrier.Window)doc.WindowOptions.Remove(edge);
        }
    });
    private static void ApplyWindow(Layout doc,Edge edge,WindowSettings settings) {
        doc.Edges[edge]=Barrier.Window;doc.WindowOptions[edge]=settings;doc.DoorOptions.Remove(edge);doc.DoorLocks.Remove(edge);
    }
    public string? PreviewWindow(Edge edge,WindowSettings settings) {
        if(!Document.InteriorEdge(edge))return "Place windows on an interior wall edge. The outer shell stays fixed.";
        if(!settings.Valid)return "Use 0.6–1.2 m width, 0.5–1.5 m sill/height, with a top at or below 2.7 m.";
        var candidate=Copy(Document);ApplyWindow(candidate,edge,settings);
        try {
            _=WallJunctions.Make(candidate,.60f,.60f);
            var before=LayoutValidation.Check(Document,StrictValidation).Where(i=>i.Fatal).ToList();
            foreach(var issue in LayoutValidation.Check(candidate,StrictValidation).Where(i=>i.Fatal)){int at=before.IndexOf(issue);if(at<0)return issue.Message;before.RemoveAt(at);}
            return windowValidation?.Invoke(candidate).FirstOrDefault(i=>i.Fatal)?.Message;
        }catch(Exception error){return "Window clearance could not be checked: "+error.GetBaseException().Message;}
    }
    public bool Window(Edge edge,WindowSettings settings) {
        LastRejection=PreviewWindow(edge,settings);
        return LastRejection==null && Change(doc=>ApplyWindow(doc,edge,settings));
    }
    public void Target(Cell cell,TargetRole role,Bearing? facing=null,bool? always=null){var old=Document.Targets.GetValueOrDefault(cell);CommitTarget(old==null?null:new TargetRef(cell),new(cell,role,facing,always??old?.Always??false));}
    public void MoveTarget(Cell from,Cell to){LastRejection=null;if(Document.Targets.TryGetValue(from,out var old)){if(to!=from && Document.Targets.ContainsKey(to)){LastRejection="That position is occupied by another target.";return;}CommitTarget(new(from),old with{Cell=to});}}
    public void Theme(Cell cell,Theme theme)=>Change(doc=>{if(doc.Contains(cell))doc.Themes[cell]=theme;});
    public void Erase(Cell cell)=>Change(doc=>{doc.Targets.Remove(cell);doc.Themes.Remove(cell);});
    private Target Align(Target target)=>targetAlignment?.Invoke(Document,target)??target;
    public Target PrepareFine(float gridX,float gridY,TargetRole role,float? yaw,bool always,float step=.25f,TargetStyle style=TargetStyle.Upright,float height=0,float pitch=0,float roll=0)=>Align(TargetPlacement.At(TargetPlacement.Snap(gridX*2,step),TargetPlacement.Snap(gridY*2,step),role,yaw,always,NextId(Document),style,height,pitch,roll));
    public TargetRef? AddFine(float gridX,float gridY,TargetRole role,float? yaw,bool always,float step=.25f,TargetStyle style=TargetStyle.Upright,float height=0,float pitch=0,float roll=0) {
        var t=PrepareFine(gridX,gridY,role,yaw,always,step,style,height,pitch,roll);
        return CommitTarget(null,t)?new(t.Cell,t.Id):null;
    }
    public void EditTarget(TargetRef key,Func<Target,Target> edit){LastRejection=null;var old=Document.Find(key);if(old!=null)CommitTarget(key,edit(old) with{Cell=old.Cell,Id=old.Id});}
    public Target? PrepareMove(TargetRef key,float gridX,float gridY,float step=.25f,float? yawOverride=null) {
        var old=Document.Find(key);if(old==null)return null;
        float? yaw=yawOverride??old.Yaw??(old.Facing is {} facing?90*(int)facing:null);
        return Align(TargetPlacement.At(TargetPlacement.Snap(gridX*2,step),TargetPlacement.Snap(gridY*2,step),old.Role,yaw,old.Always,key.Id==0?NextId(Document):key.Id,old.Style,old.Height,old.Pitch,old.Roll));
    }
    public TargetRef MoveFine(TargetRef key,float gridX,float gridY,float step=.25f,float? yawOverride=null) {
        LastRejection=null;var next=PrepareMove(key,gridX,gridY,step,yawOverride);if(next==null)return key;
        return CommitTarget(key,next)?new(next.Cell,next.Id):key;
    }
    public TargetRef RepositionTarget(TargetRef key,Target next) {
        LastRejection=null;if(Document.Find(key)==null)return key;
        next=next with{Id=key.Id>0?key.Id:NextId(Document)};
        return CommitTarget(key,next)?new(next.Cell,next.Id):key;
    }
    private static void ApplyTarget(Layout doc,TargetRef? previous,Target next) {
        if(previous is {} key){if(key.Id==0)doc.Targets.Remove(key.Cell);else{int at=doc.FineTargets.FindIndex(t=>t.Id==key.Id);if(at>=0){if(next.Id==key.Id){doc.FineTargets[at]=next;return;}doc.FineTargets.RemoveAt(at);}}}
        if(next.Id==0)doc.Targets[next.Cell]=next;else doc.FineTargets.Add(next);
    }
    private bool CommitTarget(TargetRef? previous,Target next) {
        var preview=PreviewTarget(previous,next);LastRejection=preview.Rejection;
        if(!preview.CanPlace)return false;
        Change(doc=>ApplyTarget(doc,previous,next));return true;
    }
    public TargetPreview PreviewTarget(TargetRef? previous,Target next) {
        TargetPreview Reject(string reason)=>new(next,previous,reason);
        if(next.Style==TargetStyle.Standless && next.Height==-3)return Reject("Outside the selected face. Use Move to another face to reattach.");
        if(next.Style==TargetStyle.Standless && next.Height==-2)return Reject("Move closer to a wall or prop side at the target's height.");
        if(next.Style==TargetStyle.Standless && next.Height<0)return Reject("Choose a fitting individual furniture surface, or turn off surface snapping.");
        if(!Document.Contains(next.Cell))return Reject("Keep the entire target inside the house.");
        if(next.Cell==new Cell(1,0) || next.Cell==new Cell(1,Document.Columns-1))return Reject("Keep the fixed entry and exit clear.");
        if(previous==null && Document.TargetCount>=60)return Reject("More than 60 targets.");
        var candidate=Copy(Document);ApplyTarget(candidate,previous,next);
        var old=previous is {} prior?Document.Find(prior):null;
        bool geometry=old==null || old with{Always=next.Always}!=next;
        IReadOnlyList<Issue> measured=Array.Empty<Issue>();
        try{if(geometry && targetValidation!=null)measured=targetValidation(candidate,new(next.Cell,next.Id));}
        catch(Exception error){return Reject("Target clearance could not be checked: "+error.GetBaseException().Message);}
        var issues=geometry?TargetPlacement.Check(candidate,only:new(next.Cell,next.Id)).Concat(measured):Enumerable.Empty<Issue>();
        // Existing invalid drafts must remain repairable one target at a time.
        // Reject every new fatal condition, including strict reachability/caps.
        var existing=LayoutValidation.Check(Document,StrictValidation).Where(i=>i.Fatal).ToList();
        var introduced=new List<Issue>();
        foreach(var issue in LayoutValidation.Check(candidate,StrictValidation).Where(i=>i.Fatal)){int at=existing.IndexOf(issue);if(at>=0)existing.RemoveAt(at);else introduced.Add(issue);}
        var blocked=issues.Concat(introduced).FirstOrDefault(i=>i.Fatal);
        if(blocked!=null){string reason=blocked.Message;int colon=reason.IndexOf(':');return Reject(reason.StartsWith("Target ",StringComparison.Ordinal) && colon>=0?reason[(colon+1)..].Trim():reason);}
        return new(next,previous,null);
    }
    public void RemoveTarget(TargetRef key)=>Change(doc=>{if(key.Id==0)doc.Targets.Remove(key.Cell);else doc.FineTargets.RemoveAll(t=>t.Id==key.Id);});
    public int NextPropId(){var used=Document.Props.Select(p=>p.Id).ToHashSet();return Enumerable.Range(1,used.Count+1).First(id=>!used.Contains(id));}
    public static HashSet<int> PropTree(Layout doc,int root) {
        var ids=new HashSet<int>();if(root<=0)return ids;ids.Add(root);
        bool changed;do{changed=false;foreach(var p in doc.Props)if(p.Id>0 && PropSupport.Parents(p).Any(ids.Contains))changed|=ids.Add(p.Id);}while(changed);
        return ids;
    }
    private static void ApplyProp(Layout doc,int? previous,PlacedProp prop) {
        if(previous is not {} at){doc.Props.Add(prop);PowerCircuits.EnsureProp(doc,prop);SseObjectives.EnsureProp(doc,prop);return;}
        var old=doc.Props[at];var branch=PropTree(doc,old.Id);
        float radians=(prop.Yaw-old.Yaw)*MathF.PI/180,c=MathF.Cos(radians),s=MathF.Sin(radians);
        for(int n=0;n<doc.Props.Count;n++) {
            var child=doc.Props[n];if(n==at || !branch.Contains(child.Id))continue;
            float dx=child.X-old.X,dz=child.Z-old.Z;
            doc.Props[n]=child with{X=prop.X+c*dx+s*dz,Z=prop.Z-s*dx+c*dz,Height=child.Height+prop.Height-old.Height,Yaw=TargetPlacement.Angle(child.Yaw+prop.Yaw-old.Yaw)};
        }
        doc.Props[at]=prop;PowerCircuits.EnsureProp(doc,prop);SseObjectives.EnsureProp(doc,prop);
    }
    public Layout WithProp(int? previous,PlacedProp prop){var copy=Copy(Document);ApplyProp(copy,previous,prop);return copy;}
    public PropPreview PreviewProp(int? previous,PlacedProp prop) {
        PropPreview Reject(string reason)=>new(prop,previous,reason);
        if(prop.Id<1 || prop.Id>1000000)return Reject("Individual furniture needs a unique positive ID.");
        if(previous is {} at && (at<0 || at>=Document.Props.Count))return Reject("Select an existing furniture item.");
        if(previous==null && Document.Props.Count(p=>p.Id>0)>=PropPlacement.Limit)return Reject("More than 128 individual furniture items.");
        var candidate=WithProp(previous,prop);
        try {
            var overlap=propEditValidation?.Invoke(Document,prop,previous).FirstOrDefault(i=>i.Fatal);if(overlap!=null)return Reject(overlap.Message);
            var measured=propValidation?.Invoke(candidate)??new[]{new Issue("Native furniture measurements are not ready.",true)};
            var existing=LayoutValidation.Check(Document,StrictValidation).Where(i=>i.Fatal).ToList();
            var introduced=new List<Issue>();
            foreach(var issue in LayoutValidation.Check(candidate,StrictValidation).Where(i=>i.Fatal)){int index=existing.IndexOf(issue);if(index>=0)existing.RemoveAt(index);else introduced.Add(issue);}
            var blocked=measured.Concat(introduced).FirstOrDefault(i=>i.Fatal);
            return blocked==null?new(prop,previous,null):Reject(blocked.Message);
        }catch(Exception error){return Reject("Furniture clearance could not be checked: "+error.GetBaseException().Message);}
    }
    public bool CommitProp(int? previous,PlacedProp prop) {
        var preview=PreviewProp(previous,prop);LastRejection=preview.Rejection;
        return preview.CanPlace && Change(doc=>ApplyProp(doc,previous,prop));
    }
    public void RemoveProp(int index){if(index>=0 && index<Document.Props.Count)Change(doc=>{var branch=PropTree(doc,doc.Props[index].Id);if(branch.Count==0)doc.Props.RemoveAt(index);else doc.Props.RemoveAll(p=>branch.Contains(p.Id));PowerCircuits.RemoveMissing(doc);SseObjectives.RemoveMissing(doc);});}
    private static int NextId(Layout doc){var used=doc.FineTargets.Select(t=>t.Id).ToHashSet();int next=used.DefaultIfEmpty(0).Max()+1;return next<=1000000?next:Enumerable.Range(1,used.Count+1).First(id=>!used.Contains(id));}
    // Grid-space hit testing is shared by the UI and headless interaction checks.
    public static Edge NearestEdge(float x,float y) {
        float dx=Math.Abs(x-MathF.Round(x)),dy=Math.Abs(y-MathF.Round(y));
        return dy<=dx?new((int)MathF.Round(y),(int)MathF.Floor(x),true):new((int)MathF.Floor(y),(int)MathF.Round(x),false);
    }
    public static IReadOnlyList<Edge> Stroke(float startX,float startY,float endX,float endY) {
        if(Math.Abs(endX-startX)+Math.Abs(endY-startY)<.25f)return new[]{NearestEdge(startX,startY)};
        int x0=(int)MathF.Round(startX),y0=(int)MathF.Round(startY),x1=(int)MathF.Round(endX),y1=(int)MathF.Round(endY);
        if(Math.Abs(x1-x0)>=Math.Abs(y1-y0))return Enumerable.Range(Math.Min(x0,x1),Math.Abs(x1-x0)).Select(x=>new Edge(y0,x,true)).ToArray();
        return Enumerable.Range(Math.Min(y0,y1),Math.Abs(y1-y0)).Select(y=>new Edge(y,x0,false)).ToArray();
    }
}

public static class LayoutFiles
{
    // Save copies, including after a reload; imported originals are never overwritten.
    public static string SaveCopy(Layout doc,string folder,bool strict) {
        doc=LayoutDraft.Copy(doc);
        var issues=LayoutValidation.Check(doc,strict);if(issues.Any(i=>i.Fatal))throw new InvalidOperationException(string.Join(" ",issues.Where(i=>i.Fatal).Select(i=>i.Message)));
        Directory.CreateDirectory(folder);
        if(Directory.EnumerateFiles(folder,"*.txt",SearchOption.TopDirectoryOnly).Take(256).Count()>=256)throw new InvalidOperationException("This library folder has reached its 256-file limit.");
        string slug=new(doc.Name.Where(c=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_').Take(48).ToArray());
        if(slug.Length==0)slug="layout";
        string file=Path.Combine(folder,"editor-"+slug+"-"+Guid.NewGuid().ToString("N")[..8]+".txt");
        using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        using var writer=new StreamWriter(stream);writer.Write(LayoutFormat.Write(doc));return file;
    }
    public static void SaveDraft(Layout doc,string file) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        string temp=file+".tmp";
        File.WriteAllText(temp,LayoutFormat.Write(doc));File.Move(temp,file,true);
    }
}
