namespace OperatorKillhouse.Core;

public static class EncounterClearance
{
    // Matches the editor's 0.65 m wall clearance: 0.30 m wall half-width,
    // 0.30 m character radius and 0.05 m authoring margin. Native mission
    // prefabs use a 0.75 m crowd radius, too wide for range doorways/corridors.
    public static float AgentRadius=>.30f;
    public const float ProjectionLimit=.55f;
}

public sealed class CivilianAlarm
{
    public const double FreezeBeforeFlee=Combatants.DefaultFleeDelay, SightConfirmation=.35;
    public double FleeDelay {get;}
    public CivilianAlarm(double fleeDelay=FreezeBeforeFlee) {
        if(!double.IsFinite(fleeDelay) || fleeDelay<0 || fleeDelay>Combatants.MaxFleeDelay)throw new ArgumentOutOfRangeException(nameof(fleeDelay),"Flee delay must be 0–30 seconds.");
        FleeDelay=fleeDelay;
    }
    public bool Alarmed {get;private set;}
    public string Reason {get;private set;}="";
    private double? seenSince,alarmedAt;
    public bool CanFlee(double now)=>Alarmed && double.IsFinite(now) && now-alarmedAt>=FleeDelay;
    public bool Observe(double now,bool seen,bool heard,bool underFire,bool hurt,bool flashed) {
        if(Alarmed || !double.IsFinite(now))return false;
        if(!seen)seenSince=null;else if(seenSince==null || now<seenSince)seenSince=now;
        Reason=hurt?"hurt":flashed?"flashed":underFire?"under fire":heard?"heard danger":seen && now-seenSince>=SightConfirmation?"sighted danger":"";
        if(Reason.Length==0)return false;
        Alarmed=true;alarmedAt=now;return true;
    }
}

public readonly record struct NavigationBlock(float East,float South,float Width,float Depth);
public static class EncounterBoundary
{
    // Cuts extend beyond the scan on their outer side, so no floor strip
    // connects around a wall end outside the house. NPCs stay inside the range.
    public static IEnumerable<NavigationBlock> Blocks(Layout doc) {
        float width=doc.Columns*2,depth=doc.Rows*2;
        yield return new(width/2,-1,width+4,2.1f);
        yield return new(width/2,depth+1,width+4,2.1f);
        yield return new(-1,depth/2,2.1f,depth+4);
        yield return new(width+1,depth/2,2.1f,depth+4);
        foreach(var (edge,kind) in doc.Interior)if(kind is Barrier.Wall or Barrier.Window) {
            float east=(edge.Column+(edge.Horizontal?.5f:0))*2,south=(edge.Row+(edge.Horizontal?0:.5f))*2;
            yield return new(east,south,edge.Horizontal?2.1f:.6f,edge.Horizontal?.6f:2.1f);
        }
    }
}
