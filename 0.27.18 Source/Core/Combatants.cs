using System.Globalization;
using System.Text;

namespace OperatorKillhouse.Core;

public enum EncounterMode { Mixed, PaperOnly, AIOnly }
public enum CombatOrder { Defensive, Offensive, Cautious }
public enum CivilianReaction { None, Freeze, Flee }
public enum PatrolPacing { Natural, Continuous, Fixed }
public readonly record struct RoutePoint(float X,float South);
public sealed record Combatant(int Id,string Prefab,float X,float South,float Yaw=0,CombatOrder Order=CombatOrder.Defensive,int RouteId=0,CivilianReaction Reaction=CivilianReaction.None,float FleeDelay=Combatants.DefaultFleeDelay);
public sealed record CombatRoute(int Id,string Name,bool Loop=true,float Wait=2,PatrolPacing Pacing=PatrolPacing.Natural) {
    public List<RoutePoint> Points {get;}=new();
}

public static class Combatants
{
    public static bool IsCivilian(string prefab)=>prefab[(prefab.IndexOf(':')+1)..].StartsWith("BOT V2 Civilian",StringComparison.OrdinalIgnoreCase);
    public const int Limit=32,RouteLimit=32,PointLimit=64;
    public const float DefaultFleeDelay=10,MaxFleeDelay=30;
    public static bool Inside(Layout doc,float x,float south)=>float.IsFinite(x) && float.IsFinite(south) && x>=.4f && south>=.4f && x<=doc.Columns*2-.4f && south<=doc.Rows*2-.4f;
    public static bool ValidName(string value)=>!string.IsNullOrWhiteSpace(value) && value.Length<=160 && !value.Any(char.IsControl);
    public static IEnumerable<Issue> Check(Layout doc,bool geometry=true) {
        if(!Enum.IsDefined(doc.Encounter))yield return new("Unknown encounter mode.",true);
        if(doc.Combatants.Count>Limit || doc.Routes.Count>RouteLimit)yield return new("Use at most 32 AI and 32 routes per house.",true);
        if(doc.Combatants.Any(b=>b.Id is <1 or >1000000) || doc.Combatants.Select(b=>b.Id).Distinct().Count()!=doc.Combatants.Count)yield return new("AI IDs must be unique positive numbers.",true);
        if(doc.Routes.Any(r=>r.Id is <1 or >1000000) || doc.Routes.Select(r=>r.Id).Distinct().Count()!=doc.Routes.Count)yield return new("Route IDs must be unique positive numbers.",true);
        foreach(var route in doc.Routes) {
            if(!Enum.IsDefined(route.Pacing))yield return new($"Route {route.Id} has an unknown patrol pacing.",true);
            if(!ValidName(route.Name) || !float.IsFinite(route.Wait) || route.Wait<0 || route.Wait>60 || route.Points.Count is <1 or >PointLimit)yield return new($"Route {route.Id} needs a name, 1–64 points and a 0–60 second wait.",true);
            foreach(var p in route.Points)if(!Inside(doc,p.X,p.South))yield return new($"Route {route.Id} has a point outside the walkable floor bounds.",true);
            if(!doc.Combatants.Any(b=>b.RouteId==route.Id))yield return new($"Route {route.Id} ({route.Name}) is not assigned to any AI.",false);
        }
        foreach(var bot in doc.Combatants) {
            if(!Enum.IsDefined(bot.Reaction) || bot.Reaction!=CivilianReaction.None && !IsCivilian(bot.Prefab))yield return new($"AI {bot.Id}: freeze/flee reactions require a civilian AI type.",true);
            if(!float.IsFinite(bot.FleeDelay) || bot.FleeDelay<0 || bot.FleeDelay>MaxFleeDelay)yield return new($"AI {bot.Id}: flee delay must be 0–30 seconds.",true);
            if(bot.Reaction==CivilianReaction.Flee && bot.RouteId==0)yield return new($"AI {bot.Id}: assign an escape route before starting a Flee civilian.",false);
            if(!ValidName(bot.Prefab) || !Inside(doc,bot.X,bot.South) || !float.IsFinite(bot.Yaw) || bot.Yaw<0 || bot.Yaw>=360 || !Enum.IsDefined(bot.Order))yield return new($"AI {bot.Id} has an invalid type, position, angle or order.",true);
            if(bot.RouteId<0 || bot.RouteId!=0 && !doc.Routes.Any(r=>r.Id==bot.RouteId))yield return new($"AI {bot.Id} refers to a missing route.",true);
            if(doc.Combatants.Any(other=>other.Id!=bot.Id && Distance(bot.X,bot.South,other.X,other.South)<.8f))yield return new($"AI {bot.Id} overlaps another spawn.",true);
        }
        if(!geometry)yield break;
        foreach(var bot in doc.Combatants)if(Inside(doc,bot.X,bot.South) && Clearance(doc,bot.X,bot.South) is {} reason)yield return new($"AI {bot.Id}: {reason}",true);
        foreach(var route in doc.Routes)foreach(var p in route.Points)if(Inside(doc,p.X,p.South) && Clearance(doc,p.X,p.South) is {} reason)yield return new($"Route {route.Id}: {reason}",true);
        // Connectivity here is an early authoring check. Native graph paths are
        // checked after baking, including furniture and the closing loop leg.
        foreach(var bot in doc.Combatants.Where(b=>b.RouteId!=0 && !(b.Order==CombatOrder.Cautious && b.Reaction==CivilianReaction.None) && Inside(doc,b.X,b.South))) {
            var route=doc.Routes.FirstOrDefault(r=>r.Id==bot.RouteId);if(route==null)continue;
            var reach=RouteCells(doc,new((int)(bot.South/2),(int)(bot.X/2)));
            if(route.Points.Any(p=>Inside(doc,p.X,p.South) && !reach.Contains(new((int)(p.South/2),(int)(p.X/2)))))yield return new($"AI {bot.Id}'s route crosses disconnected rooms.",true);
        }
    }
    private static HashSet<Cell> RouteCells(Layout doc,Cell start) {
        var seen=new HashSet<Cell>{start};var queue=new Queue<Cell>();queue.Enqueue(start);
        while(queue.Count>0){var at=queue.Dequeue();foreach(var bearing in Enum.GetValues<Bearing>()){
            // Locks are temporary runtime obstacles. Players can unlock the
            // door later; walls/windows still separate authored routes.
            if(doc.At(at,bearing) is Barrier.Wall or Barrier.Window)continue;
            var next=Layout.Adjacent(at,bearing);if(doc.Contains(next) && seen.Add(next))queue.Enqueue(next);
        }}return seen;
    }
    private static float Distance(float x,float z,float bx,float bz)=>MathF.Sqrt((x-bx)*(x-bx)+(z-bz)*(z-bz));
    public static string? Clearance(Layout doc,float x,float south) {
        foreach(var (edge,kind) in doc.Interior.Where(e=>e.Value!=Barrier.Open)) {
            float ax=edge.Column*2,az=edge.Row*2,bx=ax+(edge.Horizontal?2:0),bz=az+(edge.Horizontal?0:2);
            float dx=bx-ax,dz=bz-az,t=Math.Clamp(((x-ax)*dx+(south-az)*dz)/4,0,1);
            if(Distance(x,south,ax+dx*t,az+dz*t)<.35f+EncounterClearance.AgentRadius)return "move the point clear of the wall or doorway.";
        }
        if(doc.Encounter!=EncounterMode.AIOnly)foreach(var target in doc.AllTargets){var g=TargetPlacement.Grid(target);if(Distance(x,south,g.X*2,g.Y*2)<.8f)return "move the point clear of the paper target.";}
        return null;
    }
    public static int NextId(IEnumerable<int> used){var ids=used.ToHashSet();return Enumerable.Range(1,ids.Count+1).First(n=>!ids.Contains(n));}
    public static int CreateRoute(Layout doc,int? combatantId=null) {
        if(doc.Routes.Count>=RouteLimit)throw new InvalidOperationException("Maximum 32 routes.");
        int index=combatantId is {} id?doc.Combatants.FindIndex(b=>b.Id==id):-1;
        if(combatantId.HasValue && index<0)throw new InvalidOperationException("Select an existing AI first.");
        var bot=index>=0?doc.Combatants[index]:null;
        int routeId=NextId(doc.Routes.Select(r=>r.Id));var route=new CombatRoute(routeId,"Route "+routeId);
        route.Points.Add(new(bot?.X??1,bot?.South??1));doc.Routes.Add(route);
        if(bot!=null)doc.Combatants[index]=bot with{RouteId=routeId};
        return routeId;
    }
    public static int? Hit(Layout doc,float x,float south)=>doc.Combatants.Where(b=>Distance(x,south,b.X,b.South)<.65f).OrderBy(b=>Distance(x,south,b.X,b.South)).Select(b=>(int?)b.Id).FirstOrDefault();
    public static IEnumerable<(RoutePoint From,RoutePoint To)> Legs(Combatant bot,CombatRoute route) {
        var from=new RoutePoint(bot.X,bot.South);
        foreach(var to in route.Points){yield return (from,to);from=to;}
        if(route.Loop && route.Points.Count>1)yield return (route.Points[^1],route.Points[0]);
    }
    public static void RemoveRoute(Layout doc,int id){doc.Routes.RemoveAll(r=>r.Id==id);for(int n=0;n<doc.Combatants.Count;n++)if(doc.Combatants[n].RouteId==id)doc.Combatants[n]=doc.Combatants[n] with{RouteId=0};}
}

// The installed BrainAI.Patrol wraps its index even when AIPatrol.Looping is
// false. Keep authored once/loop semantics here, and yield movement to combat.
public sealed class RouteProgress
{
    public int Index {get;private set;}
    public bool Finished {get;private set;}
    public long Visits {get;private set;}
    public bool Waiting {get;private set;}
    private double? waitingSince;
    public void Pause(){waitingSince=null;Waiting=false;}
    public void Tick(int count,bool loop,float wait,double now,float physicalDistance,bool pending,float remaining) {
        if(Finished || count<1 || !double.IsFinite(now))return;
        if(pending || !float.IsFinite(physicalDistance) || physicalDistance>.55f || !float.IsFinite(remaining) || remaining>.65f){Pause();return;}
        waitingSince??=now;
        Waiting=wait>0;
        if(now<waitingSince || now-waitingSince<wait)return;
        Pause();Visits++;
        if(Index+1<count)Index++;else if(loop)Index=0;else Finished=true;
    }
}

public static class PatrolTiming
{
    // One watch stop in each group of four arrivals. Moving its slot by one
    // gives 3–5 waypoints between stops. Stable IDs keep host behaviour repeatable
    // without synchronized squads or random destinations outside the route.
    public static float Wait(CombatRoute route,RouteProgress progress,int placement,bool escaping=false) {
        if(escaping || route.Pacing==PatrolPacing.Continuous || progress.Finished)return 0;
        if(route.Pacing==PatrolPacing.Fixed)return route.Wait;
        if(progress.Index==route.Points.Count-1 && !route.Loop)return 0;
        uint seed=Mix(unchecked((uint)placement*747796405u+(uint)route.Id*2891336453u+(uint)(progress.Visits/4)*277803737u));
        if(progress.Visits%4!=2+seed%2)return 0;
        return route.Wait*(.5f+(seed>>8)%501/1000f);
    }
    private static uint Mix(uint value){value^=value>>16;value*=2246822519u;value^=value>>13;return value;}
}

internal static class CombatantFormat
{
    private static readonly CultureInfo Numbers=CultureInfo.InvariantCulture;
    internal static void Read(Layout doc,string key,string value,List<(int Id,RoutePoint Point)> points) {
        var w=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        int Id(int n,bool zero=false){if(n>=w.Length || !int.TryParse(w[n],NumberStyles.None,Numbers,out int id) || id<(zero?0:1) || id>1000000)throw new FormatException("Invalid AI/route ID.");return id;}
        float Number(int n){if(n>=w.Length || !float.TryParse(w[n],NumberStyles.Float,Numbers,out float v) || !float.IsFinite(v))throw new FormatException("AI/route numbers must be finite.");return v;}
        if(key=="encounter") {if(w.Length!=1 || !Enum.TryParse<EncounterMode>(w[0],false,out var mode) || !Enum.IsDefined(mode))throw new FormatException("encounter needs Mixed, PaperOnly or AIOnly.");doc.Encounter=mode;}
        else if(key=="combatant") {
            if(w.Length is not (7 or 8 or 9) || !Enum.TryParse<CombatOrder>(w[5],false,out var order) || !Enum.IsDefined(order))throw new FormatException("combatant needs id encoded-prefab x south yaw Defensive/Offensive/Cautious route-id, optional civilian reaction and optional flee delay (0–30 seconds).");
            var reaction=CivilianReaction.None;
            if(w.Length>=8 && (!Enum.TryParse(w[7],false,out reaction) || !Enum.IsDefined(reaction)))throw new FormatException("Civilian reaction must be None, Freeze or Flee.");
            doc.Combatants.Add(new(Id(0),Uri.UnescapeDataString(w[1]),Number(2),Number(3),TargetPlacement.Angle(Number(4)),order,Id(6,true),reaction,w.Length==9?Number(8):Combatants.DefaultFleeDelay));
        } else if(key=="route") {
            if(w.Length is not (4 or 5) || w[2] is not ("LOOP" or "ONCE"))throw new FormatException("route needs id encoded-name LOOP/ONCE wait-seconds and optional patrol pacing.");
            var pacing=PatrolPacing.Natural;
            if(w.Length==5 && (!Enum.TryParse(w[4],false,out pacing) || !Enum.IsDefined(pacing)))throw new FormatException("Patrol pacing must be Natural, Continuous or Fixed.");
            doc.Routes.Add(new(Id(0),Uri.UnescapeDataString(w[1]),w[2]=="LOOP",Number(3),pacing));
        } else {if(w.Length!=3 || points.Count>=Combatants.RouteLimit*Combatants.PointLimit)throw new FormatException("waypoint needs route-id x south (maximum 64 per route).");points.Add((Id(0),new(Number(1),Number(2))));}
    }
    internal static void Finish(Layout doc,List<(int Id,RoutePoint Point)> points) {
        foreach(var (id,point) in points){var route=doc.Routes.FirstOrDefault(r=>r.Id==id)??throw new FormatException("Waypoint refers to a missing route.");route.Points.Add(point);}
        var error=Combatants.Check(doc,false).FirstOrDefault(i=>i.Fatal);if(error!=null)throw new FormatException(error.Message);
    }
    internal static void Write(Layout doc,StringBuilder output) {
        string F(float v)=>v.ToString("R",Numbers);
        if(doc.Encounter!=EncounterMode.Mixed)output.Append($"encounter: {doc.Encounter}\n");
        foreach(var b in doc.Combatants.OrderBy(b=>b.Id))output.Append($"combatant: {b.Id} {Uri.EscapeDataString(b.Prefab)} {F(b.X)} {F(b.South)} {F(b.Yaw)} {b.Order} {b.RouteId}{(b.Reaction==CivilianReaction.None && b.FleeDelay==Combatants.DefaultFleeDelay?"":" "+b.Reaction)}{(b.FleeDelay==Combatants.DefaultFleeDelay?"":" "+F(b.FleeDelay))}\n");
        foreach(var r in doc.Routes.OrderBy(r=>r.Id)){output.Append($"route: {r.Id} {Uri.EscapeDataString(r.Name)} {(r.Loop?"LOOP":"ONCE")} {F(r.Wait)}{(r.Pacing==PatrolPacing.Natural?"":" "+r.Pacing)}\n");foreach(var p in r.Points)output.Append($"waypoint: {r.Id} {F(p.X)} {F(p.South)}\n");}
    }
}
