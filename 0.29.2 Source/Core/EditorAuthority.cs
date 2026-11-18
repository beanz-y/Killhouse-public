namespace OperatorKillhouse.Core;

public readonly record struct EditorActor(ulong Steam,ulong Session,uint Player);
public sealed record EditorRequestKey(EditorActor Actor,long Request,int House,uint Range,string LayoutId);
public sealed record EditorProposal(EditorRequestKey Key,string Canonical,double Expires) {
    public Layout Document()=>LayoutFormat.Read(Canonical,"client load request").Layout!;
}

// Grants belong to one authenticated player/session. Receiving a proposal only
// creates immutable review data; it cannot register or activate a native map.
public sealed class EditorAuthority
{
    public const int MaxRequests=8;
    public const double ReviewSeconds=120,RequestInterval=5;
    private readonly HashSet<EditorActor> allowed=new();
    private readonly Dictionary<EditorActor,(EditorRequestKey Key,LayoutTransfer Transfer)> uploads=new();
    private readonly Dictionary<EditorActor,EditorProposal> pending=new();
    private readonly Dictionary<EditorActor,(long Request,double Time)> previous=new();
    public long Revision {get;private set;}=1;
    public IReadOnlyList<EditorProposal> Pending=>pending.Values.OrderBy(p=>p.Expires).ToArray();
    public bool CanEdit(EditorActor actor)=>allowed.Contains(actor);
    public bool Set(EditorActor actor,bool value) {
        if(actor.Steam==0 || actor.Session==0 || actor.Player==0)throw new ArgumentException("Invalid editor identity.");
        bool changed=value?allowed.Add(actor):allowed.Remove(actor);if(changed)Revision++;
        return changed;
    }
    public EditorProposal? Receive(EditorActor actor,ulong host,CoopPacket packet,double now) {
        if(!CanEdit(actor) || !double.IsFinite(now) || host==0 || packet.Host!=host || packet.Peer!=actor.Session || packet.Kind!=CoopKind.ProposalChunk || !CoopProtocol.Valid(packet))throw new InvalidDataException("Map editing permission is required.");
        if(packet.Part==0) {
            if(uploads.ContainsKey(actor) || pending.ContainsKey(actor))throw new InvalidDataException("One load request per player may be pending.");
            if(uploads.Count+pending.Count>=MaxRequests)throw new InvalidDataException("The host already has eight load requests awaiting review.");
            if(previous.TryGetValue(actor,out var last) && (packet.Revision<=last.Request || now<last.Time || now-last.Time<RequestInterval))throw new InvalidDataException("Wait before submitting another load request.");
            previous[actor]=(packet.Revision,now);
            uploads.Add(actor,(new(actor,packet.Revision,packet.House,packet.Range,packet.Layout),new(packet,now)));
        }
        if(!uploads.TryGetValue(actor,out var upload))throw new InvalidDataException("No matching map upload is pending.");
        Layout? doc;
        try{doc=upload.Transfer.Accept(packet,now);}catch{uploads.Remove(actor);throw;}
        if(doc==null)return null;
        uploads.Remove(actor);var result=new EditorProposal(upload.Key,LayoutFormat.Write(doc),now+ReviewSeconds);pending.Add(actor,result);return result;
    }
    public EditorProposal? Decide(EditorRequestKey key,double now,bool approve,bool canLoad) {
        if(!pending.TryGetValue(key.Actor,out var proposal) || proposal.Key!=key)return null;
        if(!CanEdit(key.Actor) || now>=proposal.Expires || now<proposal.Expires-ReviewSeconds){pending.Remove(key.Actor);return null;}
        if(approve && !canLoad)return null;
        pending.Remove(key.Actor);return proposal;
    }
    public IReadOnlyList<EditorRequestKey> Cancel(EditorActor actor) {
        var result=new List<EditorRequestKey>();
        if(uploads.Remove(actor,out var upload))result.Add(upload.Key);
        if(pending.Remove(actor,out var proposal))result.Add(proposal.Key);
        return result;
    }
    public IReadOnlyList<EditorRequestKey> Retain(IReadOnlyCollection<EditorActor> actors,double now) {
        var result=new List<EditorRequestKey>();
        foreach(var actor in allowed.Concat(uploads.Keys).Concat(pending.Keys).Concat(previous.Keys).Distinct().ToArray()) {
            if(!actors.Contains(actor)){Set(actor,false);result.AddRange(Cancel(actor));previous.Remove(actor);continue;}
            if(!CanEdit(actor) || uploads.TryGetValue(actor,out var upload) && upload.Transfer.Expired(now) || pending.TryGetValue(actor,out var proposal) && now>=proposal.Expires)result.AddRange(Cancel(actor));
        }
        return result;
    }
    public void Clear(){allowed.Clear();uploads.Clear();pending.Clear();previous.Clear();Revision=1;}
}
