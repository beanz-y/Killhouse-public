using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OperatorKillhouse.Core;

public enum CoopKind { Hello=1,Welcome,Prepare,Ready,Commit,Applied,Abort,Fault,Release,LayoutChunk,EditorPermission,ProposalChunk,ProposalReply,EncounterClean,PropState,PropPush,CircuitState,BreakerToggle,LightShot,BreakerAnimation,SseState,SseCollect }
public enum ProposalDecision { Waiting=1,Approved,Declined,Cancelled,Failed,Queued }
public sealed record CoopPacket(CoopKind Kind,ulong Host,ulong Peer,long Sequence,long Revision=0,int House=0,uint Range=0,int Index=-1,
    string Layout="",string Geometry="",string Manifest="",string[]? Contracts=null,int[][]? Masks=null,string Detail="",bool Pause=false,string[][]? Parts=null,
    int Part=-1,int Total=0,string? Data=null,bool? Allowed=null,ProposalDecision? Decision=null,bool Catchup=false,MapLighting? Lighting=null,PropState[]? PropStates=null,PropPush? Push=null,int? Power=null,int? Breaker=null,int[]? BrokenLights=null,LightShot? LightShot=null,BreakerAnimation? BreakerAnimation=null,int[]? SseCollected=null,int? SseId=null);
public sealed record CoopAd(ulong Session,uint Revision,uint Player,bool Host,string Manifest,bool Furniture)
{
    public const string Key="operator.killhouse.coop.v1";
    public string Encode()=>$"1|{CoopProtocol.Build}|{Session:x16}|{Revision:x}|{Player:x}|{(Host?1:0)}|{Manifest}|{(Furniture?1:0)}";
    public bool Matches(string manifest,bool furniture)=>Furniture==furniture;
    public static CoopAd? Parse(string? value) {
        if(value==null || value.Length>200)return null;
        var p=value.Split('|');
        if(p.Length!=8 || p[0]!="1" || p[1]!=CoopProtocol.Build || !ulong.TryParse(p[2],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out ulong session) || session==0 ||
            !uint.TryParse(p[3],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint revision) || revision==0 || !uint.TryParse(p[4],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint player) || player==0 ||
            p[5] is not ("0" or "1") || !CoopProtocol.Hash(p[6]) || p[7] is not ("0" or "1"))return null;
        return new(session,revision,player,p[5]=="1",p[6],p[7]=="1");
    }
}
public sealed class CoopLease
{
    public CoopAd? Ad {get;private set;}
    private double advanced,confirmed=double.NegativeInfinity;
    private readonly Queue<ulong> retired=new();
    public bool Accept(CoopAd ad,double now) {
        if(!double.IsFinite(now))return false;
        if(Ad!=null && Ad.Session==ad.Session && ad.Revision<=Ad.Revision)return false;
        if(Ad!=null && Ad.Session!=ad.Session){if(retired.Contains(ad.Session))return false;retired.Enqueue(Ad.Session);if(retired.Count>16)retired.Dequeue();confirmed=double.NegativeInfinity;}
        Ad=ad;advanced=now;return true;
    }
    // Only an authenticated, session-bound Hello/Welcome with matching native
    // contracts can renew this proof. Re-reading cached lobby data cannot.
    public bool Confirm(ulong session,uint player,double now) {
        if(!double.IsFinite(now) || Ad==null || session!=Ad.Session || player!=Ad.Player || now<advanced || now<confirmed)return false;
        confirmed=now;return true;
    }
    public bool Fresh(double now)=>Ad!=null && double.IsFinite(now) && now>=advanced && (now-advanced<15 || now>=confirmed && now-confirmed<60);
}
public static class CoopProtocol
{
    public const string Build="0.27.18";
    public const int MaxBytes=8192;
    public static bool Hash(string? value)=>value is {Length:64} && value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Digest(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static int RpcHash(string name) {unchecked {uint hash=2166136261;foreach(char c in name)hash=(hash^(byte)c)*16777619;return (int)hash;}}
    public static byte[] Encode(CoopPacket packet) {
        if(!Valid(packet))throw new InvalidDataException("Invalid co-op packet.");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(packet);
        if(bytes.Length>MaxBytes)throw new InvalidDataException("Co-op packet exceeds its bound.");return bytes;
    }
    public static CoopPacket? Decode(byte[] bytes) {
        if(bytes.Length==0 || bytes.Length>MaxBytes)return null;
        try {var packet=JsonSerializer.Deserialize<CoopPacket>(bytes,new JsonSerializerOptions{MaxDepth=8});return packet!=null && Valid(packet)?packet:null;}
        catch(JsonException){return null;}
    }
    public static bool Valid(CoopPacket p) {
        if(p.SseCollected!=null && p.Kind!=CoopKind.SseState || p.SseId!=null && p.Kind is not (CoopKind.SseCollect or CoopKind.SseState))return false;
        if(p.BreakerAnimation!=null && p.Kind!=CoopKind.BreakerAnimation)return false;
        if(p.Power!=null && p.Kind!=CoopKind.CircuitState || p.Breaker!=null && p.Kind!=CoopKind.BreakerToggle)return false;
        if(p.BrokenLights!=null && p.Kind!=CoopKind.CircuitState || p.LightShot!=null && p.Kind!=CoopKind.LightShot)return false;
        if(p.PropStates!=null && p.Kind!=CoopKind.PropState || p.Push!=null && p.Kind!=CoopKind.PropPush)return false;
        if(!Enum.IsDefined(typeof(CoopKind),p.Kind) || p.Host==0 || p.Peer==0 || p.Sequence<=0 || p.Detail==null || p.Detail.Length>240 || p.Layout==null || p.Geometry==null || p.Manifest==null || p.Pause && p.Kind!=CoopKind.Abort)return false;
        if(p.Catchup && p.Kind is not (CoopKind.Prepare or CoopKind.Ready or CoopKind.Commit or CoopKind.Applied or CoopKind.Release or CoopKind.Fault))return false;
        if(p.Lighting is {} lighting && (!lighting.Valid || p.Kind is not (CoopKind.Welcome or CoopKind.Prepare or CoopKind.Ready or CoopKind.Commit or CoopKind.Applied or CoopKind.Release or CoopKind.Fault)))return false;
        bool transfer=p.Kind is CoopKind.LayoutChunk or CoopKind.ProposalChunk;
        if(!transfer && (p.Data!=null || p.Part!=-1 || p.Total!=0))return false;
        if(p.Kind!=CoopKind.EditorPermission && p.Allowed!=null || p.Kind!=CoopKind.ProposalReply && p.Decision!=null)return false;
        if(p.Kind==CoopKind.EditorPermission)return p.Revision>0 && p.Allowed!=null && p.House==0 && p.Range==0 && p.Index==-1 && p.Masks==null && p.Decision==null;
        if(p.Kind is CoopKind.Hello or CoopKind.Welcome)return Hash(p.Manifest) && p.Contracts is {Length:2} && p.Contracts.All(Hash) && AssetAgreement.ValidParts(p.Parts);
        if(p.Kind==CoopKind.Abort)return p.House is >=0 and <=2 && p.Revision>=0;
        if(p.House is <1 or >2 || p.Revision<=0 || p.Range==0 || p.Index is <0 or >1027)return false;
        if(p.Kind is CoopKind.SseState or CoopKind.SseCollect)return Hash(p.Layout) && p.Geometry.Length==0 && p.Masks==null && (p.Kind==CoopKind.SseState?SseObjectives.ValidIds(p.SseCollected) && (p.SseId==null || p.SseCollected!.Contains(p.SseId.Value)):p.SseId is >=1 and <=1000000 && p.SseCollected==null);
        if(p.Kind==CoopKind.LightShot)return Hash(p.Layout) && p.Geometry.Length==0 && p.Masks==null && LightDamage.Valid(p.LightShot);
        if(p.Kind==CoopKind.BreakerAnimation)return Hash(p.Layout) && p.Geometry.Length==0 && p.Masks==null && BreakerTiming.Valid(p.BreakerAnimation);
        if(p.Kind is CoopKind.CircuitState or CoopKind.BreakerToggle)return Hash(p.Layout) && p.Geometry.Length==0 && p.Masks==null && (p.Kind==CoopKind.CircuitState?p.Power is >=0 and <=PowerCircuits.Mask && p.Breaker==null && LightDamage.ValidBroken(p.BrokenLights):p.Breaker is >=1 and <=1000000 && p.Power==null);
        if(p.Kind is CoopKind.PropState or CoopKind.PropPush)return Hash(p.Layout) && p.Geometry.Length==0 && p.Masks==null && (p.Kind==CoopKind.PropState?MovableProps.States(p.PropStates):MovableProps.Push(p.Push));
        if(p.Kind==CoopKind.EncounterClean)return (Hash(p.Layout) || p.Index<=10 && p.Layout=="stock:"+p.Index) && p.Geometry.Length==0 && p.Masks==null;
        if(p.Kind==CoopKind.ProposalReply)return p.Index==0 && Hash(p.Layout) && p.Masks==null && p.Decision is {} decision && Enum.IsDefined(typeof(ProposalDecision),decision);
        if(transfer)return (p.Kind!=CoopKind.ProposalChunk || p.Index==0) && Hash(p.Layout) && p.Total is >=1 and <=LayoutTransfer.MaxParts && p.Part>=0 && p.Part<p.Total && p.Data is {Length:>0 and <=LayoutTransfer.EncodedPartLimit};
        if(p.Kind==CoopKind.Fault)return true;
        if(!Hash(p.Geometry) || !(Hash(p.Layout) || (p.Layout.StartsWith("stock:",StringComparison.Ordinal) && int.TryParse(p.Layout.AsSpan(6),out int n) && n is >=0 and <=10)))return false;
        return p.Masks is {Length:3} && p.Masks.All(m=>m!=null && m.Length<=60 && m.All(v=>v is 0 or 1));
    }
    public static bool SameSelection(CoopPacket a,CoopPacket b)=>a.Host==b.Host && a.Revision==b.Revision && a.House==b.House && a.Range==b.Range && a.Index==b.Index && a.Layout==b.Layout && a.Geometry==b.Geometry && a.Catchup==b.Catchup && (a.Lighting??MapLighting.Normal)==(b.Lighting??MapLighting.Normal) &&
        a.Masks!=null && b.Masks!=null && a.Masks.Length==b.Masks.Length && a.Masks.Zip(b.Masks).All(p=>p.First!=null && p.Second!=null && p.First.SequenceEqual(p.Second));
    public static bool Agree(string manifest,IReadOnlyList<string> local,CoopPacket remote)=>local.Count==2 && remote.Contracts is {Length:2} && remote.Contracts.SequenceEqual(local);
}
public sealed class CoopReplay
{
    private readonly Dictionary<(ulong,ulong),long> sequences=new();
    public bool Accept(ulong actor,ulong session,long sequence) {
        if(actor==0 || session==0 || sequence<=0 || sequences.TryGetValue((actor,session),out long last) && sequence<=last)return false;
        sequences[(actor,session)]=sequence;return true;
    }
    public void Clear()=>sequences.Clear();
}
public enum CoopPhase { Waiting,Committing,Complete,Aborted }
public sealed class CoopTransaction
{
    private readonly Dictionary<ulong,ulong> peers;
    private readonly HashSet<ulong> ready=new(),applied=new();
    public CoopPacket Selection {get;}
    public CoopPhase Phase {get;private set;}=CoopPhase.Waiting;
    public string Failure {get;private set;}="";
    private double started;
    public IReadOnlyList<ulong> AwaitingReady=>peers.Keys.Where(p=>!ready.Contains(p)).OrderBy(p=>p).ToArray();
    public IReadOnlyList<ulong> AwaitingApplied=>peers.Keys.Where(p=>!applied.Contains(p)).OrderBy(p=>p).ToArray();
    public CoopTransaction(CoopPacket selection,IReadOnlyDictionary<ulong,ulong> expected,double now) {
        if(selection.Kind!=CoopKind.Prepare || !CoopProtocol.Valid(selection) || !double.IsFinite(now) || expected.Count>32 || expected.Any(p=>p.Key==0 || p.Value==0))throw new InvalidDataException("Invalid co-op transaction.");
        Selection=selection;peers=new(expected);started=now;
    }
    public bool Receive(ulong actor,CoopPacket packet) {
        if(!peers.TryGetValue(actor,out ulong session) || packet.Peer!=session || packet.Host!=Selection.Host || packet.Revision!=Selection.Revision || packet.House!=Selection.House || packet.Range!=Selection.Range || packet.Index!=Selection.Index || packet.Layout!=Selection.Layout)return false;
        if(packet.Geometry!=Selection.Geometry){Abort("Geometry agreement failed.");return false;}
        if(!CoopProtocol.SameSelection(Selection,packet)){Abort("Activation mask agreement failed.");return false;}
        if(packet.Kind==CoopKind.Ready && Phase==CoopPhase.Waiting)return ready.Add(actor);
        if(packet.Kind==CoopKind.Applied && Phase==CoopPhase.Committing) {bool added=applied.Add(actor);if(applied.Count==peers.Count)Phase=CoopPhase.Complete;return added;}
        return false;
    }
    public bool Commit(double? now=null) {
        if(Phase!=CoopPhase.Waiting || ready.Count!=peers.Count)return false;
        if(now.HasValue){if(!double.IsFinite(now.Value) || now.Value<started)return false;started=now.Value;}
        Phase=peers.Count==0?CoopPhase.Complete:CoopPhase.Committing;return true;
    }
    public bool Check(double now,IReadOnlyDictionary<ulong,ulong> current) {
        if(Phase is CoopPhase.Complete or CoopPhase.Aborted)return Phase==CoopPhase.Complete;
        if(now<started || now-started>30){Abort("Preparation/commit timed out.");return false;}
        if(current.Count!=peers.Count || current.Any(p=>!peers.TryGetValue(p.Key,out ulong session) || session!=p.Value)){Abort("Lobby membership changed.");return false;}
        return true;
    }
    public void Abort(string reason){Phase=CoopPhase.Aborted;Failure=reason;}
}
