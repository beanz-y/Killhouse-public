namespace OperatorKillhouse.Core;

public static class TargetPlacement {
    public static float Angle(float value){value%=360;if(value<0)value+=360;return value==0?0:value;}
    public static float Yaw(Layout doc,Target t)=>t.Yaw??90*(int)(t.Facing??LayoutValidation.FaceEntry(doc,t.Cell));
    public static (float X,float Z) Position(Target t){var(x,z)=ConstructionPlan.Center(t.Cell);return(x+t.OffsetX,z-t.OffsetZ);}
    public static (float X,float Y) Grid(Target t)=>(t.Cell.Column+.5f+t.OffsetX/2,t.Cell.Row+.5f+t.OffsetZ/2);
    public static Target At(float xMetres,float southMetres,TargetRole role,float? yaw,bool always,int id,TargetStyle style=TargetStyle.Upright,float height=0,float pitch=0,float roll=0) {
        if(!float.IsFinite(xMetres) || !float.IsFinite(southMetres) || yaw.HasValue && !float.IsFinite(yaw.Value))throw new ArgumentException("Target position and angle must be finite.");
        var cell=new Cell((int)MathF.Floor(southMetres/2),(int)MathF.Floor(xMetres/2));
        return new(cell,role,null,always,xMetres-(cell.Column+.5f)*2,southMetres-(cell.Row+.5f)*2,yaw.HasValue?Angle(yaw.Value):null,id,style,height,pitch,roll);
    }
    public static float Snap(float metres,float step)=>step>0?MathF.Round(metres/step)*step:metres;
    public static Footprint Bounds(Layout doc,Target t) {
        return TargetGeometry.Footprint(doc,t);
    }
    public static TargetRef? Hit(Layout doc,float gridX,float gridY,float radiusMetres=.4f,int cycle=0) {
        var hits=doc.AllTargets.Select(t=>{var(x,y)=Grid(t);return(t,d:(x-gridX)*(x-gridX)+(y-gridY)*(y-gridY));}).Where(p=>p.d<=radiusMetres*radiusMetres/4 || p.t.Style==TargetStyle.Standless && PropPlacement.Hit(Bounds(doc,p.t),ConstructionPlan.Left+gridX*2,ConstructionPlan.North-gridY*2)).OrderBy(p=>p.d).ThenBy(p=>p.t.Id).ToArray();
        var found=hits.Length==0?default:hits[Math.Abs(cycle%hits.Length)];
        return found.t==null?null:new TargetRef(found.t.Cell,found.t.Id);
    }
    public static IReadOnlyList<Issue> Check(Layout doc,IReadOnlyList<Footprint>? boxes=null,IReadOnlyList<BarrierSegment>? architecture=null,TargetRef? only=null) {
        if(only==null && doc.FineTargets.Count==0 && !doc.Targets.Values.Any(t=>t.Style!=TargetStyle.Upright || t.Yaw.HasValue || t.OffsetX!=0 || t.OffsetZ!=0))return Array.Empty<Issue>();
        var targets=doc.AllTargets.OrderBy(t=>t.Cell.Row).ThenBy(t=>t.Cell.Column).ThenBy(t=>t.Id).ToArray();
        // Conservative bounds independently measured from the native target
        // assemblies. Runtime staging also checks its captured exact meshes.
        boxes??=targets.Select(t=>Bounds(doc,t)).ToArray();
        if(boxes.Count!=targets.Length)throw new ArgumentException("Target footprint order mismatch.");
        bool measuredArchitecture=architecture!=null;
        architecture??=WallJunctions.Make(doc,.60f,.60f).Build.Segments;
        var walls=architecture.Where(s=>s.Kind==Barrier.Wall).Concat(doc.Edges.Where(p=>p.Value==Barrier.Wall && !doc.InteriorEdge(p.Key) && !(p.Key.Horizontal==false && p.Key.Row==1 && (p.Key.Column==0 || p.Key.Column==doc.Columns))).Select(p=>new BarrierSegment(p.Key,Barrier.Wall,ConstructionPlan.Left+(p.Key.Column+(p.Key.Horizontal?.5f:0))*2,ConstructionPlan.North-(p.Key.Row+(p.Key.Horizontal?0:.5f))*2))).ToArray();
        var openings=architecture.Where(s=>s.Kind is Barrier.Door or Barrier.Frame).Concat(new[]{new BarrierSegment(new(1,0,false),Barrier.Door,ConstructionPlan.Left,ConstructionPlan.North-3),new BarrierSegment(new(1,doc.Columns,false),Barrier.Door,ConstructionPlan.Left+doc.Columns*2,ConstructionPlan.North-3)}).ToArray();
        var issues=new List<Issue>();
        for(int n=0;n<targets.Length;n++) {
            var t=targets[n];var box=boxes[n];bool fine=t.Id>0 || t.Style!=TargetStyle.Upright || t.Yaw.HasValue || t.OffsetX!=0 || t.OffsetZ!=0;
            if(only is {} key ? new TargetRef(t.Cell,t.Id)!=key : !fine)continue;
            if(!box.Valid || box.Corners.Any(p=>p.X<ConstructionPlan.Left+PlacementClearance.Margin || p.X>ConstructionPlan.Left+doc.Columns*2-PlacementClearance.Margin || p.Z>ConstructionPlan.North-PlacementClearance.Margin || p.Z<ConstructionPlan.North-doc.Rows*2+PlacementClearance.Margin))issues.Add(new($"Target {t.Id}: footprint crosses the house boundary.",true));
            if(t.Style==TargetStyle.Standless && TargetGeometry.Volume(doc,t).MaxY>TargetGeometry.Ceiling)issues.Add(new($"Target {t.Id}: tilted silhouette exceeds the 3 m house height.",true));
            for(int m=0;m<targets.Length;m++)if(m!=n && (only!=null || m<n || targets[m].Id==0) && (t.Style==TargetStyle.Standless || targets[m].Style==TargetStyle.Standless?TargetGeometry.Volume(doc,t).Overlaps(TargetGeometry.Volume(doc,targets[m]),.025f):box.Overlaps(boxes[m],.025f)))issues.Add(new($"Target {t.Id}: overlaps another target.",true));
            foreach(var wall in walls) {
                var obstacle=wall.Edge.Horizontal?new Footprint(wall.X,wall.Z,wall.Span/2,wall.Thickness/2):new Footprint(wall.X,wall.Z,wall.Thickness/2,wall.Span/2);
                // Native panels measure .5943 m; fallback walls are .600 m.
                // A bounded 4 mm tolerance admits contact against either face
                // without allowing the silhouette through the solid wall.
                float gap=t.Style==TargetStyle.Standless?(measuredArchitecture?0:-TargetContact.WallTolerance):PlacementClearance.Margin;
                if(wall.WindowPart?TargetGeometry.Volume(doc,t).Overlaps(WindowGeometry.Volume(wall),gap):box.Overlaps(obstacle,gap)){issues.Add(new($"Target {t.Id}: overlaps a wall or window frame/sill.",true));break;}
            }
            foreach(var opening in openings) {
                bool blocked=PlacementClearance.OpeningBlocked(box,opening,doc.DoorOptions.GetValueOrDefault(opening.Edge));
                if(blocked){issues.Add(new($"Target {t.Id}: blocks a doorway, frame post or swing area.",true));break;}
            }
        }
        return issues;
    }
}
