using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OperatorKillhouse.Core;

public enum Barrier { Open, Wall, Door, Frame, Window }
public enum Bearing { North, East, South, West }
public enum TargetRole { Threat, Civilian, Hostage }
public enum TargetStyle { Upright, Standless }
public enum Theme { Bedroom, Office, Lounge, Storage, Cover }
public readonly record struct Cell(int Row, int Column);
public readonly record struct Edge(int Row, int Column, bool Horizontal);
public sealed record Target(Cell Cell, TargetRole Role, Bearing? Facing = null, bool Always = false,float OffsetX=0,float OffsetZ=0,float? Yaw=null,int Id=0,TargetStyle Style=TargetStyle.Upright,float Height=0,float Pitch=0,float Roll=0);
public readonly record struct TargetRef(Cell Cell,int Id=0);
// Positive IDs identify individually edited, fixed furniture. Zero retains the
// original import's automatic fitting behavior and canonical text.
public sealed record PlacedProp(string Name, float X, float Z, float Yaw = 0, float Height = 0,int Id=0,int SupportId=0,SupportPeers SupportPeers=default);
// HingeHigh follows drawing coordinates: east on H edges, south on V edges.
// OpensPositive means south on H edges, east on V edges.
public sealed record DoorSettings(bool HingeHigh = false, bool OpensPositive = true) {
    public bool NativeInvert(bool horizontal) => horizontal ? HingeHigh != OpensPositive : HingeHigh == OpensPositive;
}
public sealed class Layout
{
    public string Name { get; set; } = "Untitled";
    public string Author { get; set; } = "";
    public int Houses { get; set; } = 3;
    public LightingPreset Lighting {get;set;}
    public float Darkness {get;set;}
    public bool Night {get=>Lighting==LightingPreset.Night;set{Lighting=value?LightingPreset.Night:LightingPreset.Normal;if(!value)Darkness=0;}}
    public List<string> Tags {get;}=new();
    public int Columns { get; }
    public int Rows { get; }
    public Dictionary<Edge, Barrier> Edges { get; } = new();
    public HashSet<Edge> BoundaryWalls {get;}=new();
    public Dictionary<Cell, Target> Targets { get; } = new();
    public List<Target> FineTargets {get;}=new();
    public IEnumerable<Target> AllTargets=>Targets.Values.Concat(FineTargets);
    public int TargetCount=>Targets.Count+FineTargets.Count;
    public Target? Find(TargetRef key)=>key.Id==0?Targets.GetValueOrDefault(key.Cell):FineTargets.FirstOrDefault(t=>t.Id==key.Id);
    public Dictionary<Cell, Theme> Themes { get; } = new();
    public List<PlacedProp> Props { get; } = new();
    public List<SseObjective> Sse {get;}=new();
    public List<PowerCircuit> Circuits {get;}=new();
    public List<PlacedLight> Lights {get;}=new();
    public List<PlacedBreaker> Breakers {get;}=new();
    public List<MapTrigger> Triggers {get;}=new();
    public Dictionary<int,AIStart> AIStarts {get;}=new();
    public EncounterMode Encounter {get;set;}
    public List<Combatant> Combatants {get;}=new();
    public List<CombatRoute> Routes {get;}=new();
    public Dictionary<Edge, DoorSettings> DoorOptions { get; } = new();
    public Dictionary<Edge, bool> DoorLocks { get; } = new();
    public Dictionary<Edge, WindowSettings> WindowOptions { get; } = new();
    public Layout(int columns, int rows) { Columns=columns; Rows=rows; }
    public bool Contains(Cell cell) => cell.Row>=0 && cell.Row<Rows && cell.Column>=0 && cell.Column<Columns;
    public bool InteriorEdge(Edge edge) => edge.Horizontal
        ? edge.Row>0 && edge.Row<Rows && edge.Column>=0 && edge.Column<Columns
        : edge.Column>0 && edge.Column<Columns && edge.Row>=0 && edge.Row<Rows;
    public static Edge Boundary(Cell cell, Bearing bearing) => bearing switch {
        Bearing.North => new(cell.Row,cell.Column,true), Bearing.East => new(cell.Row,cell.Column+1,false),
        Bearing.South => new(cell.Row+1,cell.Column,true), _ => new(cell.Row,cell.Column,false) };
    public static Cell Adjacent(Cell cell, Bearing bearing) => bearing switch {
        Bearing.North => cell with {Row=cell.Row-1}, Bearing.East => cell with {Column=cell.Column+1},
        Bearing.South => cell with {Row=cell.Row+1}, _ => cell with {Column=cell.Column-1} };
    public Barrier At(Cell cell, Bearing bearing) => Edges.GetValueOrDefault(Boundary(cell,bearing));
    public void Enclose() {
        for(int x=0;x<Columns;x++) { Edges[new(0,x,true)]=Barrier.Wall; Edges[new(Rows,x,true)]=Barrier.Wall; }
        for(int y=0;y<Rows;y++) { Edges[new(y,0,false)]=Barrier.Wall; Edges[new(y,Columns,false)]=Barrier.Wall; }
    }
    public IEnumerable<KeyValuePair<Edge,Barrier>> Interior => Edges.Where(p => p.Key.Horizontal ? p.Key.Row>0 && p.Key.Row<Rows : p.Key.Column>0 && p.Key.Column<Columns);
    public string Identity => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LayoutFormat.Write(this)))).ToLowerInvariant();
}

public sealed record Issue(string Message, bool Fatal = false);
public sealed record ImportResult(Layout? Layout, IReadOnlyList<Issue> Issues) {
    public bool Success => Layout!=null && !Issues.Any(i=>i.Fatal);
}
public static class LayoutValidation
{
    public static Dictionary<Cell,int> Distances(Layout layout, Cell start, bool crossDoors=true) {
        var distances = new Dictionary<Cell,int>();
        if(!layout.Contains(start)) return distances;
        var frontier=new Queue<Cell>(); distances[start]=0; frontier.Enqueue(start);
        while(frontier.Count>0) {
            Cell cell=frontier.Dequeue();
            foreach(Bearing direction in Enum.GetValues(typeof(Bearing))) {
                Barrier boundary=layout.At(cell,direction);
                if(boundary is Barrier.Wall or Barrier.Window || (!crossDoors && boundary!=Barrier.Open)) continue;
                Cell next=Layout.Adjacent(cell,direction);
                if(layout.Contains(next) && !distances.ContainsKey(next)) { distances.Add(next,distances[cell]+1); frontier.Enqueue(next); }
            }
        }
        return distances;
    }
    public static IReadOnlyList<Issue> Check(Layout layout, bool strict=false) {
        var issues=new List<Issue>();
        issues.AddRange(Combatants.Check(layout));
        issues.AddRange(PowerCircuits.Check(layout));
        issues.AddRange(SseObjectives.Check(layout));
        issues.AddRange(ScenarioTriggers.Check(layout));
        issues.AddRange(BoundaryWalls.Check(layout));
        foreach(var(edge,kind) in layout.Edges) {
            if(!Enum.IsDefined(typeof(Barrier),kind))issues.Add(new("Unknown wall type.",true));
            if(kind==Barrier.Window && (!layout.InteriorEdge(edge) || !WindowGeometry.Settings(layout,edge).Valid))issues.Add(new("Windows need an interior edge, 0.6–1.2 m width, 0.5–1.5 m sill/height, and a top at or below 2.7 m.",true));
        }
        if(layout.WindowOptions.Keys.Any(e=>layout.Edges.GetValueOrDefault(e)!=Barrier.Window))issues.Add(new("A window setting must refer to a window edge.",true));
        if(layout.Columns<2 || layout.Columns>10 || layout.Rows<2 || layout.Rows>10) issues.Add(new("Range layouts must fit 2..10 columns and 2..10 rows.",true));
        if(layout.TargetCount>60) issues.Add(new("More than 60 targets.",true));
        foreach(var t in layout.AllTargets) {
            if(!Enum.IsDefined(typeof(TargetStyle),t.Style) || !new[]{t.Height,t.Pitch,t.Roll}.All(float.IsFinite) || t.Height<0 || t.Height>TargetGeometry.MaximumHeight || Math.Abs(t.Pitch)>90 || Math.Abs(t.Roll)>180)issues.Add(new("Target height/tilt is outside its supported range.",true));
            if(!Enum.IsDefined(typeof(TargetRole),t.Role))issues.Add(new("Unknown target role.",true));
            if(t.Style==TargetStyle.Upright && (t.Height!=0 || t.Pitch!=0 || t.Roll!=0))issues.Add(new("Height and tilt require a standless target.",true));
        }
        Cell entry=new(1,0), exit=new(1,layout.Columns-1);
        var distances=Distances(layout,entry);
        if(!distances.ContainsKey(exit)) issues.Add(new("Exit cannot be reached from the fixed entry.",true));
        foreach(var target in layout.AllTargets) {
            if(!layout.Contains(target.Cell) || target.Cell==entry) issues.Add(new($"Invalid target cell {target.Cell}.",true));
            else if(!distances.ContainsKey(target.Cell)) issues.Add(new($"Target at {target.Cell} is unreachable.",strict));
        }
        foreach(var target in layout.AllTargets)if(!float.IsFinite(target.OffsetX) || !float.IsFinite(target.OffsetZ) || target.OffsetX< -1 || target.OffsetX>=1 || target.OffsetZ< -1 || target.OffsetZ>=1 || target.Yaw.HasValue && (!float.IsFinite(target.Yaw.Value) || target.Yaw.Value<0 || target.Yaw.Value>=360))issues.Add(new("Target position/angle must be finite and inside its cell.",true));
        if(layout.FineTargets.Any(t=>t.Id<1 || t.Id>1000000) || layout.FineTargets.Select(t=>t.Id).Distinct().Count()!=layout.FineTargets.Count)issues.Add(new("Fine target IDs must be unique positive numbers.",true));
        if(layout.Interior.Count(p=>p.Value==Barrier.Door)>15) issues.Add(new("More than 15 doors requested; available stock doors limit placement.",strict));
        foreach(var edge in layout.DoorOptions.Keys.Concat(layout.DoorLocks.Keys))
            if(!layout.InteriorEdge(edge) || layout.Edges.GetValueOrDefault(edge)!=Barrier.Door)
                issues.Add(new("A door setting must refer to an interior door edge.",true));
        if((layout.DoorOptions.Count>0 || layout.DoorLocks.Count>0) && layout.Interior.Count(p=>p.Value==Barrier.Door)>15)
            issues.Add(new("Layouts with custom door swings or locks support at most 15 doors.",true));
        foreach(var p in layout.Props) if(string.IsNullOrWhiteSpace(p.Name) || !float.IsFinite(p.X) || !float.IsFinite(p.Z) || !float.IsFinite(p.Yaw) || !float.IsFinite(p.Height)) issues.Add(new("Prop coordinates, angle and height must be finite.",true));
        foreach(var p in layout.Props) if(Math.Abs(p.X)>100 || Math.Abs(p.Z)>100 || Math.Abs(p.Height)>10) issues.Add(new("Prop position exceeds the supported range bounds.",true));
        if(layout.Props.Any(p=>p.Id<0 || p.Id>1000000) || layout.Props.Where(p=>p.Id>0).Select(p=>p.Id).Distinct().Count()!=layout.Props.Count(p=>p.Id>0))issues.Add(new("Individual furniture IDs must be unique positive numbers.",true));
        if(layout.Props.Count(p=>p.Id>0)>128)issues.Add(new("More than 128 individual furniture items.",true));
        var propIds=layout.Props.Where(p=>p.Id>0).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First());
        issues.AddRange(PropSupport.Check(layout));
        foreach(var prop in layout.Props.Where(p=>p.SupportId!=0)) {
            var seen=new HashSet<int>{prop.Id};var next=prop;
            while(next.SupportId!=0){if(next.Id<=0 || next.SupportId<0 || !propIds.TryGetValue(next.SupportId,out var parent) || !seen.Add(next.SupportId)){issues.Add(new("Furniture supports must refer to existing individual pieces without cycles.",true));break;}next=parent;}
        }
        if(!issues.Any(i=>i.Fatal))issues.AddRange(TargetPlacement.Check(layout));
        return issues;
    }
    public static Bearing FaceEntry(Layout layout, Cell cell) {
        var distances=Distances(layout,new(1,0));
        return Enum.GetValues(typeof(Bearing)).Cast<Bearing>().Where(d=>layout.At(cell,d) is not (Barrier.Wall or Barrier.Window))
            .OrderBy(d=>distances.GetValueOrDefault(Layout.Adjacent(cell,d),int.MaxValue)).FirstOrDefault();
    }
}
