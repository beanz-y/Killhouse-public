using System.Globalization;
using System.Text;

namespace OperatorKillhouse.Core;

public enum TriggerEvent { EnterZone,CrossLine,AIDeath,BreakerOff,SseCollected }
public enum BreakerSignal { Off,On,Either }
public enum TriggerActors { Players,AI,Anyone,SpecificAI }
public enum TriggerActionKind { LightsOn,LightsOff,ToggleLights,StartRoute,Spawn,SpawnRandom,Despawn,DespawnEnteringAI }
public enum AIStart { WaitForTrigger,Deferred }
public sealed record TriggerAction(TriggerActionKind Kind,int[] Targets);
public sealed record MapTrigger(int Id,string Name,TriggerEvent Event=TriggerEvent.EnterZone,TriggerActors Actors=TriggerActors.Players,int ActorId=0,
    float X=0,float South=0,float EndX=2,float EndSouth=2,float Bottom=-.5f,float Top=2.5f,bool Once=true,float Cooldown=2,int BreakerId=0,int[]? ActorIds=null,int SseId=0,BreakerSignal BreakerWhen=BreakerSignal.Off) {
    public List<TriggerAction> Actions {get;}=new();
}
public readonly record struct TriggerActor(long Key,bool AI,int Placement,float X,float South,float Height,bool Dead=false);
public sealed record TriggerActivation(MapTrigger Trigger,TriggerActor[] Actors,bool FireSharedActions);

public static class ScenarioTriggers
{
    public const int Limit=64,ActionLimit=8;
    public static bool Spatial(TriggerEvent kind)=>kind is TriggerEvent.EnterZone or TriggerEvent.CrossLine;
    public static bool HasEnteringDespawn(MapTrigger trigger)=>trigger.Actions.Any(a=>a.Kind==TriggerActionKind.DespawnEnteringAI);
    public static bool CanDespawnEntering(MapTrigger trigger)=>Spatial(trigger.Event) && trigger.Actors!=TriggerActors.Players;
    public static bool UsesAISelection(MapTrigger trigger)=>trigger.Event==TriggerEvent.AIDeath || trigger.Actors==TriggerActors.SpecificAI;
    public static bool IncludesAI(MapTrigger trigger,int id)=>trigger.ActorIds?.Contains(id)??trigger.ActorId==id;
    public static int[] AIIds(MapTrigger trigger)=>trigger.ActorIds??(trigger.ActorId>0?new[]{trigger.ActorId}:Array.Empty<int>());
    public static MapTrigger WithAI(MapTrigger trigger,IEnumerable<int> ids) {
        var selected=ids.Distinct().OrderBy(id=>id).ToArray();
        return trigger with{ActorId=selected.Length==1?selected[0]:0,ActorIds=selected.Length>1?selected:null};
    }
    public static bool HasData(Layout doc)=>doc.Triggers.Count>0 || doc.AIStarts.Count>0;
    public static bool NeedsAI(Layout doc)=>doc.Triggers.Any(t=>t.Event==TriggerEvent.AIDeath || t.Actors is TriggerActors.AI or TriggerActors.SpecificAI || t.Actions.Any(a=>a.Kind>=TriggerActionKind.StartRoute));
    public static IEnumerable<Issue> Check(Layout doc) {
        var ids=doc.Combatants.Select(b=>b.Id).ToHashSet();var circuits=doc.Circuits.Select(c=>c.Id).Append(0).ToHashSet();
        if(doc.Triggers.Count>Limit || doc.Triggers.Any(t=>t.Id is <1 or >1000000) || doc.Triggers.Select(t=>t.Id).Distinct().Count()!=doc.Triggers.Count)yield return new("Use at most 64 triggers with unique positive IDs.",true);
        if(doc.AIStarts.Any(p=>!ids.Contains(p.Key) || !Enum.IsDefined(typeof(AIStart),p.Value)))yield return new("AI start settings must refer to existing AI.",true);
        foreach(var t in doc.Triggers) {
            if(!Enum.IsDefined(typeof(BreakerSignal),t.BreakerWhen) || t.Event!=TriggerEvent.BreakerOff && t.BreakerWhen!=BreakerSignal.Off)yield return new($"Trigger {t.Id}: Off/On/Either applies only to a breaker source.",true);
            if(!Combatants.ValidName(t.Name) || t.Name.Length>64 || !Enum.IsDefined(typeof(TriggerEvent),t.Event) || !Enum.IsDefined(typeof(TriggerActors),t.Actors) || !new[]{t.X,t.South,t.EndX,t.EndSouth,t.Bottom,t.Top,t.Cooldown}.All(float.IsFinite) ||
                t.X<0 || t.EndX<0 || t.South<0 || t.EndSouth<0 || t.X>doc.Columns*2 || t.EndX>doc.Columns*2 || t.South>doc.Rows*2 || t.EndSouth>doc.Rows*2 || t.Bottom<-.5f || t.Top>3 || t.Top<=t.Bottom || t.Cooldown is <.1f or >3600)
                yield return new($"Trigger {t.Id}: invalid name, bounds, height or cooldown (0.1–3600 s).",true);
            if(Spatial(t.Event) && (Math.Abs(t.X-t.EndX)+Math.Abs(t.South-t.EndSouth)<.1f || t.Event==TriggerEvent.EnterZone && (Math.Abs(t.X-t.EndX)<.1f || Math.Abs(t.South-t.EndSouth)<.1f)))yield return new($"Trigger {t.Id}: draw a nonempty zone or a line at least 0.1 m long.",true);
            if(t.Event==TriggerEvent.SseCollected) {
                if(t.ActorId!=0 || t.ActorIds!=null || t.Actors!=TriggerActors.Players || t.BreakerId!=0 || !doc.Sse.Any(s=>s.PropId==t.SseId))yield return new($"Trigger {t.Id}: choose an existing SSE objective as its source.",true);
            }else if(t.SseId!=0)yield return new($"Trigger {t.Id}: SSE source belongs only to an SSE-collected event.",true);
            else if(t.Event==TriggerEvent.BreakerOff) {
                if(t.ActorId!=0 || t.ActorIds!=null || t.Actors!=TriggerActors.Players || !doc.Breakers.Any(b=>b.PropId==t.BreakerId))yield return new($"Trigger {t.Id}: choose an existing breaker box as its source.",true);
            }else {
                bool invalid=t.ActorIds!=null?(t.ActorId!=0 || !UsesAISelection(t) || t.ActorIds.Length is <2 or >Combatants.Limit || t.ActorIds.Distinct().Count()!=t.ActorIds.Length || t.ActorIds.Any(id=>!ids.Contains(id))):(UsesAISelection(t) && !ids.Contains(t.ActorId) || t.ActorId!=0 && !ids.Contains(t.ActorId));
                if(t.BreakerId!=0 || invalid)yield return new($"Trigger {t.Id}: select one or more existing AI without duplicate sources (maximum 32).",true);
            }
            if(t.Actions.Count>ActionLimit)yield return new($"Trigger {t.Id}: use at most eight actions.",true);
            if(t.Actions.Count==0)yield return new($"Trigger {t.Id} has no actions; add an action before arming.");
            foreach(var a in t.Actions) {
                if(a.Kind==TriggerActionKind.DespawnEnteringAI) {
                    if(!CanDespawnEntering(t) || a.Targets==null || a.Targets.Length!=0)yield return new($"Trigger {t.Id}: Despawn entering AI needs a zone/line with Any AI, Anyone or Specific AI, and no fixed action targets.",true);
                    continue;
                }
                bool electrical=a.Kind<=TriggerActionKind.ToggleLights;
                if(!Enum.IsDefined(typeof(TriggerActionKind),a.Kind) || a.Targets==null || a.Targets.Length is <1 or >32 || a.Targets.Distinct().Count()!=a.Targets.Length || a.Targets.Any(id=>electrical?!circuits.Contains(id):!ids.Contains(id)))yield return new($"Trigger {t.Id}: actions need existing circuits or AI without duplicate targets.",true);
                else if(a.Kind==TriggerActionKind.StartRoute && a.Targets.Any(id=>doc.Combatants.First(b=>b.Id==id).RouteId==0))yield return new($"Trigger {t.Id}: assign a route to every AI in Start route.");
                else if(a.Kind is TriggerActionKind.Spawn or TriggerActionKind.SpawnRandom && a.Targets.Any(id=>!doc.AIStarts.TryGetValue(id,out var start) || start!=AIStart.Deferred))yield return new($"Trigger {t.Id}: spawn targets must use Deferred spawn at encounter start.",true);
            }
        }
    }
    public static void RemoveAI(Layout doc,int id) {
        doc.Combatants.RemoveAll(b=>b.Id==id);doc.AIStarts.Remove(id);
        for(int n=doc.Triggers.Count-1;n>=0;n--) {
            var t=doc.Triggers[n];if(!UsesAISelection(t) || !IncludesAI(t,id))continue;
            var remaining=AIIds(t).Where(v=>v!=id).ToArray();
            if(remaining.Length==0)doc.Triggers.RemoveAt(n);else doc.Triggers[n]=WithAI(t,remaining);
        }
        foreach(var t in doc.Triggers)for(int n=t.Actions.Count-1;n>=0;n--)if(t.Actions[n].Kind>=TriggerActionKind.StartRoute && t.Actions[n].Kind!=TriggerActionKind.DespawnEnteringAI) {
            var next=t.Actions[n].Targets.Where(v=>v!=id).ToArray();if(next.Length==0)t.Actions.RemoveAt(n);else t.Actions[n]=t.Actions[n] with{Targets=next};
        }
    }
    public static void RemoveCircuit(Layout doc,int id){foreach(var t in doc.Triggers)for(int n=0;n<t.Actions.Count;n++)if(t.Actions[n].Kind<=TriggerActionKind.ToggleLights)t.Actions[n]=t.Actions[n] with{Targets=t.Actions[n].Targets.Select(v=>v==id?0:v).Distinct().ToArray()};}
    internal static void Read(Layout doc,string key,string value) {
        var w=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        int Id(int n){if(n>=w.Length || !int.TryParse(w[n],NumberStyles.None,CultureInfo.InvariantCulture,out int id) || id<0 || id>1000000)throw new FormatException("Invalid trigger/AI ID.");return id;}
        float F(int n){if(n>=w.Length || !float.TryParse(w[n],NumberStyles.Float,CultureInfo.InvariantCulture,out float v) || !float.IsFinite(v))throw new FormatException("Trigger values must be finite.");return v;}
        if(key=="ai-start") {
            if(w.Length!=2 || !Enum.TryParse<AIStart>(w[1],false,out var start) || !Enum.IsDefined(typeof(AIStart),start) || !doc.AIStarts.TryAdd(Id(0),start))throw new FormatException("ai-start needs one AI ID and WaitForTrigger/Deferred, without duplicates.");
        }else if(key=="trigger") {
            if(w.Length is not (13 or 14 or 15) || !Enum.TryParse<TriggerEvent>(w[2],false,out var ev) || (ev==TriggerEvent.BreakerOff?w.Length is not (14 or 15):w.Length!=(ev==TriggerEvent.SseCollected?14:13)) || !Enum.TryParse<TriggerActors>(w[3],false,out var actors) || w[11] is not ("ONCE" or "REPEAT") || doc.Triggers.Count>=Limit || doc.Triggers.Any(t=>t.Id==Id(0)))throw new FormatException("Invalid or duplicate trigger definition.");
            var parts=w[4].Split(',');if(parts.Length>Combatants.Limit)throw new FormatException("Maximum 32 selected AI per trigger.");
            var sources=parts.Select(s=>int.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out int id) && id is >=0 and <=1000000?id:throw new FormatException("Invalid trigger AI source ID.")).ToArray();
            if(sources.Length>1 && (sources.Contains(0) || sources.Distinct().Count()!=sources.Length))throw new FormatException("Select unique positive AI source IDs.");
            var when=w.Length==15?w[14] switch{"Off"=>BreakerSignal.Off,"On"=>BreakerSignal.On,"Either"=>BreakerSignal.Either,_=>throw new FormatException("Breaker transition must be Off, On or Either.")}:BreakerSignal.Off;
            doc.Triggers.Add(new(Id(0),Uri.UnescapeDataString(w[1]),ev,actors,sources.Length==1?sources[0]:0,F(5),F(6),F(7),F(8),F(9),F(10),w[11]=="ONCE",F(12),ev==TriggerEvent.BreakerOff?Id(13):0,sources.Length>1?sources.OrderBy(id=>id).ToArray():null,ev==TriggerEvent.SseCollected?Id(13):0,when));
        }else {
            if(w.Length!=3 || !Enum.TryParse<TriggerActionKind>(w[1],false,out var kind))throw new FormatException("trigger-action needs trigger-ID action and comma-separated target IDs.");
            var trigger=doc.Triggers.FirstOrDefault(t=>t.Id==Id(0))??throw new FormatException("Action refers to a missing trigger (put definitions before actions).");
            if(trigger.Actions.Count>=ActionLimit)throw new FormatException("Maximum eight actions per trigger.");
            if(kind==TriggerActionKind.DespawnEnteringAI) {
                if(w[2]!="ENTERING")throw new FormatException("DespawnEnteringAI uses ENTERING instead of fixed AI IDs.");
                trigger.Actions.Add(new(kind,Array.Empty<int>()));return;
            }
            var parts=w[2].Split(',');if(parts.Length>32)throw new FormatException("Maximum 32 action targets.");
            trigger.Actions.Add(new(kind,parts.Select(s=>int.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out int id)?id:throw new FormatException("Invalid action target.")).ToArray()));
        }
    }
    internal static void Write(Layout doc,StringBuilder output) {
        string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
        foreach(var p in doc.AIStarts.OrderBy(p=>p.Key))output.Append($"ai-start: {p.Key} {p.Value}\n");
        foreach(var t in doc.Triggers.OrderBy(t=>t.Id))output.Append($"trigger: {t.Id} {Uri.EscapeDataString(t.Name)} {t.Event} {t.Actors} {(t.ActorIds!=null?string.Join(",",t.ActorIds.OrderBy(id=>id)):t.ActorId.ToString(CultureInfo.InvariantCulture))} {F(t.X)} {F(t.South)} {F(t.EndX)} {F(t.EndSouth)} {F(t.Bottom)} {F(t.Top)} {(t.Once?"ONCE":"REPEAT")} {F(t.Cooldown)}{(t.Event==TriggerEvent.BreakerOff?" "+t.BreakerId:t.Event==TriggerEvent.SseCollected?" "+t.SseId:"")}{(t.Event==TriggerEvent.BreakerOff && t.BreakerWhen!=BreakerSignal.Off?" "+t.BreakerWhen:"")}\n");
        foreach(var t in doc.Triggers.OrderBy(t=>t.Id))foreach(var a in t.Actions)output.Append($"trigger-action: {t.Id} {a.Kind} {(a.Kind==TriggerActionKind.DespawnEnteringAI?"ENTERING":string.Join(",",a.Targets))}\n");
    }
}

// Pure swept geometry and per-run state. New actors are baselined, never treated
// as entering just because they spawned inside a zone. Despawning is not death.
public sealed class TriggerRun
{
    private readonly Dictionary<long,TriggerActor> previous=new();
    private readonly HashSet<int> fired=new();
    private readonly Dictionary<int,double> last=new();
    // Keep the original shared-action view for existing pure callers. Runtime
    // consumes activations so entering-AI cleanup retains every source actor.
    public IReadOnlyList<MapTrigger> Tick(IReadOnlyList<MapTrigger> triggers,IReadOnlyList<TriggerActor> actors,double now)=>TickActivations(triggers,actors,now).Where(a=>a.FireSharedActions).Select(a=>a.Trigger).ToArray();
    public IReadOnlyList<TriggerActivation> TickActivations(IReadOnlyList<MapTrigger> triggers,IReadOnlyList<TriggerActor> actors,double now) {
        if(!double.IsFinite(now))return Array.Empty<TriggerActivation>();
        var result=new List<TriggerActivation>();
        foreach(var t in triggers) {
            if(t.Event is TriggerEvent.BreakerOff or TriggerEvent.SseCollected)continue;
            bool entering=ScenarioTriggers.CanDespawnEntering(t) && ScenarioTriggers.HasEnteringDespawn(t);
            bool shared=!(t.Once && fired.Contains(t.Id) || last.TryGetValue(t.Id,out double at) && now-at<t.Cooldown);
            if(!shared && !entering)continue;
            var matches=actors.Where(b=>float.IsFinite(b.X) && float.IsFinite(b.South) && float.IsFinite(b.Height) && previous.TryGetValue(b.Key,out var a) && Matches(t,b) && (t.Event==TriggerEvent.AIDeath?!a.Dead && b.Dead:!a.Dead && !b.Dead && (t.Event==TriggerEvent.EnterZone?!Inside(t,a) && Sweep(t,a,b):Cross(t,a,b)))).ToArray();
            if(matches.Length==0 || !shared && !matches.Any(a=>a.AI))continue;
            if(shared){fired.Add(t.Id);last[t.Id]=now;}
            result.Add(new(t,matches,shared));
        }
        previous.Clear();foreach(var a in actors)if(float.IsFinite(a.X) && float.IsFinite(a.South) && float.IsFinite(a.Height))previous[a.Key]=a;
        return result;
    }
    public IReadOnlyList<MapTrigger> BreakerOff(IReadOnlyList<MapTrigger> triggers,int breaker,double now)=>BreakerChanged(triggers,breaker,false,now);
    public IReadOnlyList<MapTrigger> BreakerChanged(IReadOnlyList<MapTrigger> triggers,int breaker,bool on,double now)=>Signal(triggers,TriggerEvent.BreakerOff,breaker,now,on);
    public IReadOnlyList<MapTrigger> SseCollected(IReadOnlyList<MapTrigger> triggers,int id,double now)=>Signal(triggers,TriggerEvent.SseCollected,id,now);
    private IReadOnlyList<MapTrigger> Signal(IReadOnlyList<MapTrigger> triggers,TriggerEvent kind,int breaker,double now,bool on=false) {
        if(!double.IsFinite(now) || breaker<=0)return Array.Empty<MapTrigger>();
        var result=new List<MapTrigger>();
        foreach(var t in triggers) {
            if(t.Event!=kind || (kind==TriggerEvent.BreakerOff?t.BreakerId:t.SseId)!=breaker || kind==TriggerEvent.BreakerOff && t.BreakerWhen!=BreakerSignal.Either && (t.BreakerWhen==BreakerSignal.On)!=on || t.Once && fired.Contains(t.Id) || last.TryGetValue(t.Id,out double at) && now-at<t.Cooldown)continue;
            fired.Add(t.Id);last[t.Id]=now;result.Add(t);
        }
        return result;
    }
    private static bool Matches(MapTrigger t,TriggerActor a)=>t.Event==TriggerEvent.AIDeath?a.AI && ScenarioTriggers.IncludesAI(t,a.Placement):t.Actors switch{TriggerActors.Players=>!a.AI,TriggerActors.AI=>a.AI,TriggerActors.Anyone=>true,TriggerActors.SpecificAI=>a.AI && ScenarioTriggers.IncludesAI(t,a.Placement),_=>false};
    private static bool Inside(MapTrigger t,TriggerActor p)=>p.X>=Math.Min(t.X,t.EndX) && p.X<=Math.Max(t.X,t.EndX) && p.South>=Math.Min(t.South,t.EndSouth) && p.South<=Math.Max(t.South,t.EndSouth) && p.Height>=t.Bottom && p.Height<=t.Top;
    private static bool Sweep(MapTrigger t,TriggerActor a,TriggerActor b) {
        float lo=0,hi=1;
        bool Slab(float from,float to,float min,float max){float d=to-from;if(Math.Abs(d)<.000001f)return from>=min && from<=max;float p=(min-from)/d,q=(max-from)/d;if(p>q)(p,q)=(q,p);lo=Math.Max(lo,p);hi=Math.Min(hi,q);return lo<=hi;}
        return Slab(a.X,b.X,Math.Min(t.X,t.EndX),Math.Max(t.X,t.EndX)) && Slab(a.South,b.South,Math.Min(t.South,t.EndSouth),Math.Max(t.South,t.EndSouth)) && Slab(a.Height,b.Height,t.Bottom,t.Top);
    }
    private static bool Cross(MapTrigger t,TriggerActor a,TriggerActor b) {
        float dx=t.EndX-t.X,dz=t.EndSouth-t.South;
        float sa=dx*(a.South-t.South)-dz*(a.X-t.X),sb=dx*(b.South-t.South)-dz*(b.X-t.X);
        if(Math.Abs(sa)<.00001f || sa*sb>0 || Math.Abs(sa-sb)<.00001f)return false;
        float u=sa/(sa-sb),x=a.X+(b.X-a.X)*u,z=a.South+(b.South-a.South)*u,h=a.Height+(b.Height-a.Height)*u;
        float along=((x-t.X)*dx+(z-t.South)*dz)/(dx*dx+dz*dz);
        return along>=0 && along<=1 && h>=t.Bottom && h<=t.Top;
    }
}
