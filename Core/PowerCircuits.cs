using System.Globalization;
using System.Text;

namespace OperatorKillhouse.Core;

public sealed record PowerCircuit(int Id,string Name,bool StartsOn=true,float Brightness=1,bool Independent=false);
public sealed record PlacedLight(int PropId,int Circuit=0,float Lumens=1600,float Kelvin=6500,float Range=8);
public sealed record PlacedBreaker(int PropId,int Circuit=0);

// Circuit zero is the main feed. Branch settings survive a main-feed outage.
public static class PowerCircuits
{
    public const int MaxCircuits=16,MaxLights=32,MaxBreakers=8,Mask=(1<<17)-1;
    public const string BreakerAsset="Power box";
    public static bool IsLight(string name)=>name is "Ceiling fluorescent" or "Wall lamp" or "Work light" or "Desk lamp" || FixtureMount.Wall(name);
    public static bool IsElectrical(string name)=>IsLight(name) || name==BreakerAsset;
    public static bool HasData(Layout doc)=>doc.Circuits.Count>0 || doc.Lights.Count>0 || doc.Breakers.Count>0;
    public static int Initial(Layout doc) {
        int mask=1;
        foreach(var circuit in doc.Circuits)mask=circuit.StartsOn?mask|1<<circuit.Id:mask&~(1<<circuit.Id);
        return mask;
    }
    public static bool On(int mask,int circuit)=>(mask&1)!=0 && (mask&(1<<circuit))!=0;
    public static bool Independent(Layout doc,int circuit)=>circuit>0 && doc.Circuits.FirstOrDefault(c=>c.Id==circuit)?.Independent==true;
    public static bool Energized(int mask,int circuit,bool independent=false)=>(mask&(1<<circuit))!=0 && (independent || (mask&1)!=0);
    public static float Brightness(Layout doc,int circuit) {
        float main=doc.Circuits.FirstOrDefault(c=>c.Id==0)?.Brightness??1;
        return circuit==0?main:(Independent(doc,circuit)?1:main)*(doc.Circuits.FirstOrDefault(c=>c.Id==circuit)?.Brightness??1);
    }
    public static string Label(Layout doc,int circuit)=>circuit==0?"Whole house":doc.Circuits.FirstOrDefault(c=>c.Id==circuit)?.Name??"Missing circuit";
    public static IEnumerable<Issue> Check(Layout doc) {
        if(doc.Circuits.Count>MaxCircuits+1 || doc.Circuits.Any(c=>c.Id is <0 or >MaxCircuits || c.Id==0 && c.Independent || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length>48 || c.Name.Any(char.IsControl) || !float.IsFinite(c.Brightness) || c.Brightness is <0 or >2) || doc.Circuits.Select(c=>c.Id).Distinct().Count()!=doc.Circuits.Count)
            yield return new("Use unique circuit IDs from 0 to 16 and names of 1–48 characters; circuit brightness must be 0–200%.",true);
        var circuits=doc.Circuits.Select(c=>c.Id).Append(0).ToHashSet();
        var props=doc.Props.Where(p=>p.Id>0).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First());
        if(doc.Lights.Count>MaxLights || doc.Breakers.Count>MaxBreakers)yield return new($"Each house supports {MaxLights} placed lights and {MaxBreakers} breaker boxes.",true);
        if(doc.Lights.Select(l=>l.PropId).Distinct().Count()!=doc.Lights.Count || doc.Breakers.Select(b=>b.PropId).Distinct().Count()!=doc.Breakers.Count)
            yield return new("A fixture or breaker can have only one circuit assignment.",true);
        foreach(var light in doc.Lights)if(!props.TryGetValue(light.PropId,out var prop) || !IsLight(prop.Name) || !circuits.Contains(light.Circuit) ||
            !float.IsFinite(light.Lumens) || light.Lumens is <100 or >8000 || !float.IsFinite(light.Kelvin) || light.Kelvin is <2000 or >10000 || !float.IsFinite(light.Range) || light.Range is <2 or >12)
            yield return new("Lights need an existing fixture, a circuit, 100–8000 lumens, 2000–10000 K and 2–12 m range.",true);
        foreach(var breaker in doc.Breakers)if(!props.TryGetValue(breaker.PropId,out var prop) || prop.Name!=BreakerAsset || !circuits.Contains(breaker.Circuit))
            yield return new("Breakers need an existing power box and a circuit or Whole house.",true);
        foreach(var prop in doc.Props.Where(p=>IsElectrical(p.Name))) {
            if(prop.Height is <0 or >2.7f || prop.SupportId!=0 && !FixtureMount.Portable(prop.Name))yield return new("Electrical fixtures need a height from 0 to 2.7 m. Only portable ground lights and desk lamps can rest on supporting props.",true);
            if(prop.Id<=0 || IsLight(prop.Name) && !doc.Lights.Any(l=>l.PropId==prop.Id) || prop.Name==BreakerAsset && !doc.Breakers.Any(b=>b.PropId==prop.Id))
                yield return new("Every electrical prop needs an individual ID and circuit assignment.",true);
        }
    }
    public static void RemoveMissing(Layout doc) {
        var ids=doc.Props.Select(p=>p.Id).ToHashSet();doc.Lights.RemoveAll(l=>!ids.Contains(l.PropId));doc.Breakers.RemoveAll(b=>!ids.Contains(b.PropId));
        doc.Triggers.RemoveAll(t=>t.Event==TriggerEvent.BreakerOff && !doc.Breakers.Any(b=>b.PropId==t.BreakerId));
    }
    public static void EnsureProp(Layout doc,PlacedProp prop) {
        if(IsLight(prop.Name) && !doc.Lights.Any(l=>l.PropId==prop.Id))doc.Lights.Add(new(prop.Id));
        if(prop.Name==BreakerAsset && !doc.Breakers.Any(b=>b.PropId==prop.Id))doc.Breakers.Add(new(prop.Id));
    }
    public static void Read(Layout doc,string key,string value) {
        var w=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        int Id(int i){if(i>=w.Length || !int.TryParse(w[i],out int n))throw new FormatException("Invalid circuit/fixture ID.");return n;}
        float Number(int i){if(!float.TryParse(w[i],NumberStyles.Float,CultureInfo.InvariantCulture,out float n) || !float.IsFinite(n))throw new FormatException("Invalid light setting.");return n;}
        if(key=="circuit" && w.Length is 3 or 4 or 5 && w[2] is "ON" or "OFF" && (w.Length<5 || w[4]=="INDEPENDENT"))doc.Circuits.Add(new(Id(0),Uri.UnescapeDataString(w[1]),w[2]=="ON",w.Length>=4?Number(3):1,w.Length==5));
        else if(key=="light" && w.Length==5)doc.Lights.Add(new(Id(0),Id(1),Number(2),Number(3),Number(4)));
        else if(key=="breaker" && w.Length==2)doc.Breakers.Add(new(Id(0),Id(1)));
        else throw new FormatException("Expected circuit: id encoded-name ON/OFF; light: prop-id circuit lumens kelvin range; breaker: prop-id circuit.");
    }
    public static void Write(Layout doc,StringBuilder output) {
        string N(float n)=>n.ToString("R",CultureInfo.InvariantCulture);
        foreach(var c in doc.Circuits.OrderBy(c=>c.Id))output.Append($"circuit: {c.Id} {Uri.EscapeDataString(c.Name)} {(c.StartsOn?"ON":"OFF")}{(c.Brightness==1 && !c.Independent?"":" "+N(c.Brightness))}{(c.Independent?" INDEPENDENT":"")}\n");
        foreach(var l in doc.Lights.OrderBy(l=>l.PropId))output.Append($"light: {l.PropId} {l.Circuit} {N(l.Lumens)} {N(l.Kelvin)} {N(l.Range)}\n");
        foreach(var b in doc.Breakers.OrderBy(b=>b.PropId))output.Append($"breaker: {b.PropId} {b.Circuit}\n");
    }
}
