namespace OperatorKillhouse.Core;

public enum GeneratedEncounter { Paper, AI, Mixed }
public sealed record CombatGenerationOptions(int Seed, int Rooms=7, int Targets=10,
    GeneratedEncounter Encounter=GeneratedEncounter.Paper, string AIPrefab="");
public sealed record CombatRoom(int X,int Y,int Width,int Height,IReadOnlyList<Cell>? Footprint=null,bool Passage=false) {
    public bool Contains(Cell c)=>Footprint?.Contains(c)??(c.Column>=X && c.Column<X+Width && c.Row>=Y && c.Row<Y+Height);
    public IEnumerable<Cell> Cells=>Footprint??Enumerable.Range(Y,Height).SelectMany(y=>Enumerable.Range(X,Width).Select(x=>new Cell(y,x))).ToArray();
}
public sealed record GeneratedPosition(Cell Cell,float X,float South,float Yaw,float IngressX,float IngressSouth,int Room,bool Corner=false);
public sealed record GeneratedCombat(Layout Layout,IReadOnlyList<CombatRoom> Rooms,IReadOnlyList<GeneratedPosition> Positions,string Topology="");

// One complete interior in the existing shell. The version is part of the recipe:
// changing the algorithm must not silently change a player's saved seed.
public static partial class CombatGenerator {
    public const int Version=3;
    private sealed class Dice {
        private uint state;
        public Dice(int seed){state=unchecked((uint)seed)^0xA341316Cu;if(state==0)state=1;}
        public int Next(int count){if(count<1)throw new ArgumentOutOfRangeException(nameof(count));state^=state<<13;state^=state>>17;state^=state<<5;return (int)(state%(uint)count);}
        public void Shuffle<T>(IList<T> values){for(int n=values.Count-1;n>0;n--){int other=Next(n+1);(values[n],values[other])=(values[other],values[n]);}}
    }
    private sealed record Connection(int A,int B,List<Edge> Edges);
    public static GeneratedCombat Generate(CombatGenerationOptions options) {
        if(options.Seed<0 || options.Rooms is <4 or >9 || options.Targets is <1 or >20 || !Enum.IsDefined(options.Encounter))throw new ArgumentOutOfRangeException(nameof(options));
        if(options.Encounter!=GeneratedEncounter.Paper && (!Combatants.ValidName(options.AIPrefab) || Combatants.IsCivilian(options.AIPrefab)))throw new ArgumentException("Choose an available combat AI type.");
        var dice=new Dice(options.Seed);
        var (rooms,topology)=MakeRooms(dice,options.Rooms);
        var doc=new Layout(10,10){Name=$"Combat v{Version} {options.Seed} {options.Encounter}",Author="Killhouse generator",Encounter=EncounterMode.PaperOnly};
        doc.Enclose();doc.Tags.AddRange(new[]{"generated","combat","generator-v"+Version,"seed-"+options.Seed,"room-limit-"+options.Rooms,"target-limit-"+options.Targets});
        doc.Tags.Add(topology);
        var owners=rooms.SelectMany((r,n)=>r.Cells.Select(c=>(c,n))).ToDictionary(p=>p.c,p=>p.n);
        var pairs=new Dictionary<(int,int),List<Edge>>();
        foreach(var (cell,owner) in owners)foreach(var direction in new[]{Bearing.East,Bearing.South}) {
            var next=Layout.Adjacent(cell,direction);
            if(!owners.TryGetValue(next,out int other) || other==owner)continue;
            var edge=Layout.Boundary(cell,direction);doc.Edges[edge]=Barrier.Wall;
            var key=(Math.Min(owner,other),Math.Max(owner,other));
            if(!pairs.TryGetValue(key,out var edges))pairs[key]=edges=new();edges.Add(edge);
        }
        var connections=pairs.Select(p=>new Connection(p.Key.Item1,p.Key.Item2,p.Value)).ToList();dice.Shuffle(connections);
        var components=Enumerable.Range(0,rooms.Count).ToArray();
        int Root(int n){while(components[n]!=n)n=components[n];return n;}
        var selected=new List<(Connection Link,Edge Edge)>();
        var links=new List<Connection>();
        foreach(var link in connections) {
            int a=Root(link.A),b=Root(link.B);
            if(a==b)continue;
            components[b]=a;links.Add(link);
        }
        links.AddRange(connections.Except(links).Take(1+dice.Next(3)));
        foreach(var link in links) {
            var ordered=link.Edges.OrderBy(e=>e.Row).ThenBy(e=>e.Column).ToArray();
            // Shared boundaries may bend. Prefer straight wall spans and move
            // openings off the fixed entrance/exit row when possible.
            var straight=ordered.Where(e=>ordered.Contains(e.Horizontal?e with{Column=e.Column-1}:e with{Row=e.Row-1}) && ordered.Contains(e.Horizontal?e with{Column=e.Column+1}:e with{Row=e.Row+1})).ToArray();
            var choices=straight.Length>0?straight:ordered;
            var offset=choices.Where(e=>e.Horizontal || e.Row!=1).ToArray();if(offset.Length>0)choices=offset;
            var edge=choices[dice.Next(choices.Length)];
            doc.Edges[edge]=dice.Next(3)==0 || rooms[link.A].Passage || rooms[link.B].Passage?Barrier.Frame:Barrier.Door;
            if(doc.Edges[edge]==Barrier.Door)doc.DoorOptions[edge]=new(dice.Next(2)==0,dice.Next(2)==0);
            selected.Add((link,edge));
        }
        var distances=LayoutValidation.Distances(doc,new(1,0));
        if(distances.Count!=100)throw new InvalidOperationException("Generated rooms are disconnected.");
        var architecture=WallJunctions.Make(doc,.60f,.60f).Build.Segments;
        var navigation=new FurnitureSpace(doc,Array.Empty<Footprint>(),architecture);
        var positions=new List<GeneratedPosition>();
        var perRoom=new List<GeneratedPosition>[rooms.Count];
        for(int n=0;n<rooms.Count;n++) {
            var room=rooms[n];var entries=new List<(int Distance,float X,float South,Cell Inside)>();
            if(room.Contains(new(1,0)))entries.Add((-1,0,3,new(1,0)));
            foreach(var (link,edge) in selected.Where(p=>p.Link.A==n || p.Link.B==n)) {
                Cell first=edge.Horizontal?new(edge.Row-1,edge.Column):new(edge.Row,edge.Column-1);
                Cell second=edge.Horizontal?new(edge.Row,edge.Column):new(edge.Row,edge.Column);
                var outside=room.Contains(first)?second:first;
                entries.Add((distances[outside],(edge.Column+(edge.Horizontal?.5f:0))*2,(edge.Row+(edge.Horizontal?0:.5f))*2,room.Contains(first)?first:second));
            }
            var cells=room.Cells.ToList();dice.Shuffle(cells);
            perRoom[n]=new();
            if(room.Passage)continue;
            foreach(var cell in cells) {
                float x=cell.Column*2+1+(dice.Next(3)-1)*.25f,south=cell.Row*2+1+(dice.Next(3)-1)*.25f;
                // Keep the shell's entry and exit approaches free in every mode.
                if(Squared(x-1,south-3)<9 || Squared(x-19,south-3)<9)continue;
                // A stand can fit beside an opening yet leave too little width
                // for a character to pass behind it. Protect every portal,
                // including secondary approaches, rather than only its sightline.
                if(selected.Any(p=>Squared(x-(p.Edge.Column+(p.Edge.Horizontal?.5f:0))*2,south-(p.Edge.Row+(p.Edge.Horizontal?0:.5f))*2)<4.41f))continue;
                var ingress=entries.OrderBy(p=>p.Distance).ThenBy(p=>Squared(p.X-x,p.South-south)).First();
                var aim=Approach(doc,room,cell,x,south,ingress.Inside,ingress.X,ingress.South,architecture);
                if(Squared(x-aim.X,south-aim.South)<5)continue;
                float yaw=Face(x,south,aim.X,aim.South);
                perRoom[n].Add(new(cell,x,south,yaw,aim.X,aim.South,n,aim.Corner));
            }
            // Depth is a preference rather than a farthest-corner rule.
            perRoom[n]=perRoom[n].Select(p=>(p,score:dice.Next(60)+Math.Min(36,Squared(p.X-p.IngressX,p.South-p.IngressSouth)))).OrderByDescending(p=>p.score).Select(p=>p.p).ToList();
        }
        var order=Enumerable.Range(0,rooms.Count).ToList();dice.Shuffle(order);
        bool advanced=true;
        while(positions.Count<options.Targets && advanced) {
            advanced=false;
            foreach(int n in order) {
                while(perRoom[n].Count>0 && positions.Count<options.Targets) {
                    var p=perRoom[n][0];perRoom[n].RemoveAt(0);
                    var target=Paper(p);
                    if(positions.Any(other=>Squared(p.X-other.X,p.South-other.South)<6.76f))continue;
                    doc.Targets[p.Cell]=target;
                    var bounds=TargetPlacement.Bounds(doc,target);
                    bool blocked=TargetPlacement.Check(doc,architecture:architecture).Any(i=>i.Fatal) || !navigation.RoutesClear(bounds);
                    if(!blocked) {
                        // Preserve a clear view of the chosen doorway, including
                        // avoiding one paper stand screening another target.
                        foreach(var point in positions.Append(p))foreach(var other in doc.AllTargets.Where(t=>t.Cell!=point.Cell))
                            if(BlocksView(TargetPlacement.Bounds(doc,other),point)){blocked=true;break;}
                    }
                    if(blocked){doc.Targets.Remove(p.Cell);continue;}
                    navigation.Commit(new(bounds.X,bounds.Z,bounds.Yaw,bounds,false));
                    positions.Add(p);advanced=true;break;
                }
            }
        }
        if(positions.Count==0)throw new InvalidOperationException("No clear target positions were found.");
        for(int n=0;n<positions.Count;n++) {
            var p=positions[n];
            bool ai=options.Encounter==GeneratedEncounter.AI || options.Encounter==GeneratedEncounter.Mixed && n%2==0;
            if(!ai)continue;
            doc.Targets.Remove(p.Cell);
            doc.Combatants.Add(new(n+1,options.AIPrefab,p.X,p.South,p.Yaw,CombatOrder.Cautious));
        }
        RandomizePaperRoles(doc,new Dice(options.Seed^0x05EED73A));
        doc.Encounter=options.Encounter switch{GeneratedEncounter.AI=>EncounterMode.AIOnly,GeneratedEncounter.Mixed=>EncounterMode.Mixed,_=>EncounterMode.PaperOnly};
        var errors=LayoutValidation.Check(doc,true).Where(i=>i.Fatal).ToArray();
        if(errors.Length>0)throw new InvalidOperationException(string.Join(" ",errors.Select(i=>i.Message)));
        return new(doc,rooms,positions,topology);
    }
    // Reserve the widest native upright variant before choosing roles, so
    // switching a slot to hostage cannot invalidate navigation or sightlines.
    private static Target Paper(GeneratedPosition p)=>new(p.Cell,TargetRole.Hostage,Yaw:p.Yaw,Always:true,OffsetX:p.X-p.Cell.Column*2-1,OffsetZ:p.South-p.Cell.Row*2-1);
    private static void RandomizePaperRoles(Layout doc,Dice dice) {
        var paper=doc.AllTargets.OrderBy(t=>t.Cell.Row).ThenBy(t=>t.Cell.Column).ToArray();
        if(paper.Length==0)return;
        var roles=paper.Select(_=>dice.Next(10) switch{<6=>TargetRole.Threat,<8=>TargetRole.Civilian,_=>TargetRole.Hostage}).ToArray();
        // Apply coverage after choosing AI slots, so Mixed mode cannot consume
        // the only no-shoot or hostage. Counts still vary with the seed.
        if(roles.Length>=3)foreach(var role in Enum.GetValues<TargetRole>()) {
            if(roles.Contains(role))continue;
            var replace=Enumerable.Range(0,roles.Length).Where(i=>roles.Count(r=>r==roles[i])>1).ToArray();
            roles[replace[dice.Next(replace.Length)]]=role;
        }
        // Tiny layouts must still have a shootable target to finish the run.
        if(roles.All(r=>r==TargetRole.Civilian))roles[dice.Next(roles.Length)]=TargetRole.Threat;
        for(int n=0;n<paper.Length;n++)doc.Targets[paper[n].Cell]=paper[n] with{Role=roles[n]};
    }
    public static float Face(float x,float south,float ingressX,float ingressSouth)=>TargetPlacement.Angle(MathF.Atan2(ingressX-x,south-ingressSouth)*180/MathF.PI);
    private static float Squared(float x,float y)=>x*x+y*y;
    public static bool BlocksView(Footprint box,GeneratedPosition p)=>box.Crosses(ConstructionPlan.Left+p.X,ConstructionPlan.North-p.South,ConstructionPlan.Left+p.IngressX,ConstructionPlan.North-p.IngressSouth,.10f);

    // Freeze optional native furnishings at validated poses. Reserve paper-sized
    // space around AI as well, and omit anything obstructing an ingress sightline.
    public static void Furnish(GeneratedCombat generated,IReadOnlyDictionary<string,PropShape> shapes) {
        var doc=generated.Layout;if(doc.Props.Count!=0)throw new InvalidOperationException("Already furnished.");
        var reserved=LayoutDraft.Copy(doc);reserved.Combatants.Clear();reserved.Encounter=EncounterMode.PaperOnly;reserved.Targets.Clear();
        foreach(var p in generated.Positions)reserved.Targets[p.Cell]=Paper(p);
        for(int n=0;n<generated.Rooms.Count;n++) {
            var room=generated.Rooms[n];if(room.Passage)continue;
            var cell=room.Cells.OrderBy(c=>Squared(c.Column-room.X-room.Width/2f,c.Row-room.Y-room.Height/2f)).First();
            reserved.Themes[cell]=(Theme)(n%5);
        }
        var furniture=FurniturePlanner.Make(reserved,shapes,reserved.AllTargets.Select(t=>TargetPlacement.Bounds(reserved,t)));
        var portals=doc.Interior.Where(e=>e.Value is Barrier.Door or Barrier.Frame).Select(e=>e.Key).Concat(new[]{new Edge(1,0,false),new Edge(1,doc.Columns,false)}).ToArray();
        foreach(var item in furniture.Items) {
            if(generated.Positions.Any(p=>BlocksView(item.Pose.Footprint,p)))continue;
            // The generic furnishing planner permits a bend right beside a
            // frame. Generated rooms preserve a straight, full-width approach
            // on both sides, including portals that are not a target's ingress.
            if(portals.Any(e=> {
                float x=ConstructionPlan.Left+(e.Column+(e.Horizontal?.5f:0))*2,z=ConstructionPlan.North-(e.Row+(e.Horizontal?0:.5f))*2;
                return item.Pose.Footprint.Crosses(x-(e.Horizontal?0:1.4f),z-(e.Horizontal?1.4f:0),x+(e.Horizontal?0:1.4f),z+(e.Horizontal?1.4f:0),.40f);
            }))continue;
            var prop=new PlacedProp(item.Asset,item.Pose.X-ConstructionPlan.PropLeft,item.Pose.Z-ConstructionPlan.PropSouth,item.Pose.Yaw,item.Height,doc.Props.Count+1);
            reserved.Props.Add(prop);
            if(PropPlacement.Check(reserved,shapes).Any(i=>i.Fatal)){reserved.Props.Remove(prop);continue;}
            doc.Props.Add(prop);
        }
    }
}
