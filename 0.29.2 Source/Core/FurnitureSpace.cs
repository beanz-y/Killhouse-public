namespace OperatorKillhouse.Core;

public readonly record struct Footprint(float X,float Z,float HalfX,float HalfZ,float Yaw=0)
{
    private float C=>MathF.Cos(Yaw*MathF.PI/180);
    private float S=>MathF.Sin(Yaw*MathF.PI/180);
    public bool Valid=>new[]{X,Z,HalfX,HalfZ,Yaw}.All(float.IsFinite) && HalfX>0 && HalfZ>0;
    [System.Text.Json.Serialization.JsonIgnore]
    public (float X,float Z)[] Corners {
        get {float x=X,z=Z,c=C,s=S;return new[]{(-HalfX,-HalfZ),(HalfX,-HalfZ),(HalfX,HalfZ),(-HalfX,HalfZ)}.Select(p=>(x+c*p.Item1+s*p.Item2,z-s*p.Item1+c*p.Item2)).ToArray();}
    }
    public bool Overlaps(Footprint other,float gap=.06f) {
        foreach(var(a,b) in new[]{(C,-S),(S,C),(other.C,-other.S),(other.S,other.C)}) {
            float radius=HalfX*Math.Abs(a*C-b*S)+HalfZ*Math.Abs(a*S+b*C);
            float theirs=other.HalfX*Math.Abs(a*other.C-b*other.S)+other.HalfZ*Math.Abs(a*other.S+b*other.C);
            if(Math.Abs((other.X-X)*a+(other.Z-Z)*b)>=radius+theirs+gap)return false;
        }
        return true;
    }
    public bool Blocks(float x,float z,float radius=.30f) {
        float dx=x-X,dz=z-Z;
        float a=Math.Max(0,Math.Abs(C*dx-S*dz)-HalfX),b=Math.Max(0,Math.Abs(S*dx+C*dz)-HalfZ);
        return a*a+b*b<radius*radius;
    }
    public bool Crosses(float x1,float z1,float x2,float z2,float radius=.30f) {
        float dx=x1-X,dz=z1-Z,px=C*dx-S*dz,pz=S*dx+C*dz;
        float vx=C*(x2-x1)-S*(z2-z1),vz=S*(x2-x1)+C*(z2-z1),enter=0,leave=1;
        foreach(var(p,v,extent) in new[]{(px,vx,HalfX+radius),(pz,vz,HalfZ+radius)}) {
            if(Math.Abs(v)<.00001f) {if(Math.Abs(p)>=extent)return false;continue;}
            float a=(-extent-p)/v,b=(extent-p)/v;if(a>b)(a,b)=(b,a);
            enter=Math.Max(enter,a);leave=Math.Min(leave,b);if(enter>leave)return false;
        }
        return leave>=0 && enter<=1;
    }
    public static Footprint From(FloorBox box)=>new((box.MinX+box.MaxX)/2,(box.MinZ+box.MaxZ)/2,(box.MaxX-box.MinX)/2,(box.MaxZ-box.MinZ)/2);
}
public sealed record PropShape(float MinX,float MaxX,float MinY,float MaxY,float MinZ,float MaxZ)
{
    public float Area=>(MaxX-MinX)*(MaxZ-MinZ);
    public bool Valid=>new[]{MinX,MaxX,MinY,MaxY,MinZ,MaxZ}.All(float.IsFinite) && MaxX>MinX && MaxZ>MinZ && MaxY>MinY && Area<100;
    public Footprint At(float x,float z,float yaw) {
        float c=MathF.Cos(yaw*MathF.PI/180),s=MathF.Sin(yaw*MathF.PI/180),cx=(MinX+MaxX)/2,cz=(MinZ+MaxZ)/2;
        return new(x+c*cx+s*cz,z-s*cx+c*cz,(MaxX-MinX)/2,(MaxZ-MinZ)/2,yaw);
    }
    public (float X,float Z) Origin(float centerX,float centerZ,float yaw) {var offset=At(0,0,yaw);return(centerX-offset.X,centerZ-offset.Z);}
}
public static class PropNames
{
    public static string Key(string name)=>System.Text.RegularExpressions.Regex.Replace(name.Replace("(Clone)","",StringComparison.Ordinal),@"(?:\s*\(\d+\)|_\d+)$","").Trim();
    public static string[] Primary(Theme theme)=>theme switch {
        Theme.Bedroom=>new[]{"Bed","Dresser_C","Dresser_B"},Theme.Office=>new[]{"Workdesk","Desk_metal"},Theme.Lounge=>new[]{"Couch"},Theme.Storage=>new[]{"CrCrateA1B4","Milk_crate","Storage_box_B"},_=>new[]{"PrpSandBagsWallA1"}
    };
    public static string[] Accent(Theme theme)=>theme switch {
        Theme.Bedroom=>new[]{"Dresser_C","Dresser_B"},Theme.Office=>new[]{"Chair_arm","Basement_table"},Theme.Lounge=>new[]{"Basement_table","TV_stand"},Theme.Storage=>new[]{"Milk_crate","CrCrateA1B4"},_=>new[]{"CrCrateA1B4","Milk_crate"}
    };
}
public sealed record FurniturePose(float X,float Z,float Yaw,Footprint Footprint,bool Adjusted);

// Collision uses measured oriented model bounds. A pedestrian graph separately
// protects routes to the exit, door portals and target approaches.
public sealed class FurnitureSpace
{
    private readonly Layout layout;
    private readonly List<Footprint> obstacles=new(),navigationObstacles=new(),placed=new(),navigationPlaced=new();
    private readonly List<BarrierSegment> openings=new();
    private readonly List<BarrierSegment> windowSolids=new();
    private readonly List<int[]> goals=new();
    private readonly List<(float X,float Z)> nodes=new();
    private readonly int[][] neighbours;
    private readonly bool[] staticFree;
    private readonly HashSet<int> occupiedNodes=new();
    private readonly int start;
    private readonly int width,height;
    private const float Step=.5f,Gap=PlacementClearance.Margin;
    public IReadOnlyList<Footprint> Placed=>placed;
    public FurnitureSpace(Layout doc,IEnumerable<Footprint>? measuredTargets=null,IReadOnlyList<BarrierSegment>? architecture=null)
    {
        layout=doc;width=doc.Columns*4;height=doc.Rows*4;
        architecture??=WallJunctions.Make(doc,.60f,.60f).Build.Segments;
        var fittedEdges=architecture.Select(s=>s.Edge).ToHashSet();
        foreach(var(edge,kind) in doc.Edges) {
            if(kind is Barrier.Open or Barrier.Window)continue;
            float x=ConstructionPlan.Left+(edge.Column+(edge.Horizontal?.5f:0))*2,z=ConstructionPlan.North-(edge.Row+(edge.Horizontal?0:.5f))*2;
            if(kind==Barrier.Wall) {
                if(fittedEdges.Contains(edge))continue;
                var wall=edge.Horizontal?new Footprint(x,z,1,.30f):new Footprint(x,z,.30f,1);
                obstacles.Add(wall);navigationObstacles.Add(wall);
            } else {
                openings.Add(architecture.FirstOrDefault(s=>s.Edge==edge && s.Kind==kind)??new(edge,kind,x,z));
                foreach(float sign in new[]{-1f,1f}) {
                    var post=edge.Horizontal?new Footprint(x+sign*.86f,z,.14f,.30f):new Footprint(x,z+sign*.86f,.30f,.14f);
                    navigationObstacles.Add(post);
                }
            }
        }
        foreach(var segment in architecture.Where(s=>s.Kind==Barrier.Wall)) {
            var wall=segment.Edge.Horizontal?new Footprint(segment.X,segment.Z,segment.Span/2,segment.Thickness/2):new Footprint(segment.X,segment.Z,segment.Thickness/2,segment.Span/2);
            if(segment.WindowPart)windowSolids.Add(segment);else obstacles.Add(wall);
            navigationObstacles.Add(wall);
        }
        var targetBoxes=measuredTargets?.ToArray()??doc.AllTargets.Select(t=> {
            var(x,z)=TargetPlacement.Position(t);return new Footprint(x,z,.45f,.8f,TargetPlacement.Yaw(doc,t));
        }).ToArray();
        obstacles.AddRange(targetBoxes);navigationObstacles.AddRange(targetBoxes);
        var(entryX,entryZ)=ConstructionPlan.Center(new(1,0));var(exitX,exitZ)=ConstructionPlan.Center(new(1,doc.Columns-1));
        obstacles.Add(new(ConstructionPlan.Left+.45f,entryZ,.45f,.6f));
        obstacles.Add(new(ConstructionPlan.Left+doc.Columns*2-.45f,exitZ,.45f,.6f));
        for(int row=0;row<height;row++)for(int column=0;column<width;column++)nodes.Add((ConstructionPlan.Left+(column+.5f)*Step,ConstructionPlan.North-(row+.5f)*Step));
        staticFree=nodes.Select(p=>!navigationObstacles.Any(o=>o.Blocks(p.X,p.Z))).ToArray();
        neighbours=new int[nodes.Count][];
        for(int n=0;n<nodes.Count;n++) {
            int row=n/width,col=n%width;
            var from=nodes[n];
            neighbours[n]=new[]{col>0?n-1:-1,col+1<width?n+1:-1,row>0?n-width:-1,row+1<height?n+width:-1}
                .Where(i=>i>=0 && staticFree[i] && !navigationObstacles.Any(o=>o.Crosses(from.X,from.Z,nodes[i].X,nodes[i].Z))).ToArray();
        }
        start=Nearest(entryX,entryZ);
        var baseline=Reachable(new HashSet<int>());
        AddGoal(exitX,exitZ,.8f,baseline);
        foreach(var(edge,kind) in doc.Interior.Where(p=>p.Value is Barrier.Door or Barrier.Frame)) {
            float x=ConstructionPlan.Left+(edge.Column+(edge.Horizontal?.5f:0))*2,z=ConstructionPlan.North-(edge.Row+(edge.Horizontal?0:.5f))*2;
            AddGoal(x,z,.5f,baseline);
        }
        foreach(var target in targetBoxes) {
            var accessible=nodes.Select((p,n)=>(p,n)).Where(v=>baseline[v.n] && !target.Blocks(v.p.X,v.p.Z,.28f) && target.Blocks(v.p.X,v.p.Z,.95f)).Select(v=>v.n).ToArray();
            if(accessible.Length>0)goals.Add(accessible);
        }
    }
    private int Nearest(float x,float z)=>Enumerable.Range(0,nodes.Count).Where(n=>staticFree[n]).OrderBy(n=>Distance(nodes[n].X-x,nodes[n].Z-z)).DefaultIfEmpty(-1).First();
    private void AddGoal(float x,float z,float radius,bool[] baseline) {
        var group=nodes.Select((p,n)=>(p,n)).Where(v=>baseline[v.n] && Distance(v.p.X-x,v.p.Z-z)<=radius*radius).Select(v=>v.n).ToArray();
        if(group.Length>0)goals.Add(group);
    }
    private bool[] Reachable(HashSet<int> blocked,Footprint? candidate=null) {
        var seen=new bool[nodes.Count];if(start<0 || blocked.Contains(start))return seen;
        var queue=new Queue<int>();queue.Enqueue(start);seen[start]=true;
        while(queue.Count>0) {
            int current=queue.Dequeue();var from=nodes[current];
            foreach(int next in neighbours[current])if(!seen[next] && !blocked.Contains(next)) {
                var to=nodes[next];
                if(navigationPlaced.Any(p=>p.Crosses(from.X,from.Z,to.X,to.Z)) || (candidate.HasValue && candidate.Value.Crosses(from.X,from.Z,to.X,to.Z)))continue;
                seen[next]=true;queue.Enqueue(next);
            }
        }
        return seen;
    }
    private static float Distance(float x,float z)=>x*x+z*z;
    public Cell CellAt(float x,float z)=>new(Math.Clamp((int)MathF.Floor((ConstructionPlan.North-z)/2),0,layout.Rows-1),Math.Clamp((int)MathF.Floor((x-ConstructionPlan.Left)/2),0,layout.Columns-1));
    public bool ClearStatic(Footprint footprint)=>ClearVolume(footprint,0,3);
    public bool ClearVolume(Footprint footprint,float bottom,float top)=>footprint.Valid && footprint.Corners.All(p=>p.X>=ConstructionPlan.Left && p.X<=ConstructionPlan.Left+layout.Columns*2 && p.Z<=ConstructionPlan.North && p.Z>=ConstructionPlan.North-layout.Rows*2) && !obstacles.Any(o=>o.Overlaps(footprint,Gap)) && !windowSolids.Any(s=>WindowGeometry.AtHeight(s,bottom,top) && footprint.Overlaps(new(s.X,s.Z,s.Edge.Horizontal?s.Span/2:s.Thickness/2,s.Edge.Horizontal?s.Thickness/2:s.Span/2),Gap)) && !openings.Any(o=>PlacementClearance.OpeningBlocked(footprint,o,layout.DoorOptions.GetValueOrDefault(o.Edge)));
    public bool Clear(Footprint footprint)=>ClearStatic(footprint) && !placed.Any(o=>o.Overlaps(footprint,Gap));
    public bool RoutesClear(Footprint footprint) {
        var blocked=new HashSet<int>(occupiedNodes);
        for(int n=0;n<nodes.Count;n++)if(staticFree[n] && footprint.Blocks(nodes[n].X,nodes[n].Z))blocked.Add(n);
        var reachable=Reachable(blocked,footprint);
        return start>=0 && reachable[start] && goals.All(group=>group.Any(n=>reachable[n]));
    }
    public FurniturePose? Find(PropShape shape,float desiredX,float desiredZ,float yaw,Cell roomSeed,bool exactFirst,bool rotate=true)
    {
        if(!shape.Valid)return null;
        var preferred=shape.At(desiredX,desiredZ,yaw);
        if(exactFirst && Clear(preferred) && RoutesClear(preferred))return new(desiredX,desiredZ,yaw,preferred,false);
        var room=LayoutValidation.Distances(layout,roomSeed,false).Keys.ToHashSet();
        var candidates=new List<(float X,float Z,float Yaw,float Cost)>();
        foreach(float turn in rotate?new[]{0f,90,180,270}:new[]{0f}) {
            float angle=(yaw+turn)%360;
            foreach(var cell in room)for(int iz=0;iz<8;iz++)for(int ix=0;ix<8;ix++) {
                float x=ConstructionPlan.Left+cell.Column*2+(ix+.5f)*.25f,z=ConstructionPlan.North-cell.Row*2-(iz+.5f)*.25f;
                candidates.Add((x,z,angle,Distance(x-preferred.X,z-preferred.Z)+(turn==0?0:.1f)));
            }
        }
        foreach(var p in candidates.OrderBy(c=>c.Cost).ThenBy(c=>c.Yaw).ThenBy(c=>c.X).ThenBy(c=>c.Z)) {
            var(x,z)=shape.Origin(p.X,p.Z,p.Yaw);var box=shape.At(x,z,p.Yaw);
            if(Clear(box) && RoutesClear(box))return new(x,z,p.Yaw,box,true);
        }
        return null;
    }
    public void Commit(FurniturePose pose,bool navigation=true) {
        placed.Add(pose.Footprint);
        if(!navigation)return;
        navigationPlaced.Add(pose.Footprint);
        for(int n=0;n<nodes.Count;n++)if(staticFree[n] && pose.Footprint.Blocks(nodes[n].X,nodes[n].Z))occupiedNodes.Add(n);
    }
}
