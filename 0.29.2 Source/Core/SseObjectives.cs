using System.Globalization;
using System.Text;

namespace OperatorKillhouse.Core;

public sealed record SseObjective(int PropId,string Label,bool Required=true);
public static class SseObjectives
{
    public const int Limit=32;
    public static readonly IReadOnlyList<string> Assets=Array.AsReadOnly(new[]{"SSE Thumbdrive","SSE SIM cards","SSE Phone","SSE Folder","SSE Encrypted radio","SSE Hard drive","SSE Laptop"});
    public static bool IsAsset(string asset)=>Assets.Contains(asset,StringComparer.Ordinal);
    public static void EnsureProp(Layout doc,PlacedProp prop){if(IsAsset(prop.Name) && !doc.Sse.Any(s=>s.PropId==prop.Id))doc.Sse.Add(new(prop.Id,prop.Name[4..]));}
    public static void RemoveMissing(Layout doc) {
        var ids=doc.Props.Where(p=>IsAsset(p.Name)).Select(p=>p.Id).ToHashSet();doc.Sse.RemoveAll(s=>!ids.Contains(s.PropId));
        doc.Triggers.RemoveAll(t=>t.Event==TriggerEvent.SseCollected && !doc.Sse.Any(s=>s.PropId==t.SseId));
    }
    public static IEnumerable<Issue> Check(Layout doc) {
        if(doc.Sse.Count>Limit || doc.Sse.Select(s=>s.PropId).Distinct().Count()!=doc.Sse.Count)yield return new("Use at most 32 SSE objectives, with unique prop IDs.",true);
        foreach(var s in doc.Sse)if(s.PropId is <1 or >1000000 || string.IsNullOrWhiteSpace(s.Label) || s.Label.Length>64 || s.Label.Any(char.IsControl) || !doc.Props.Any(p=>p.Id==s.PropId && IsAsset(p.Name)))yield return new("SSE objectives need an existing SSE prop and a label of 1–64 characters.",true);
        foreach(var p in doc.Props.Where(p=>IsAsset(p.Name)))if(p.Id<=0 || !doc.Sse.Any(s=>s.PropId==p.Id))yield return new("Every SSE prop needs an individual ID and objective settings.",true);
        // Objectives stay fixed so the interaction target and host reach check agree.
        if(doc.Props.Any(p=>PropSupport.Parents(p).Any(id=>doc.Props.Any(parent=>parent.Id==id && IsAsset(parent.Name) && !PropSupport.CanCarry(parent,p)))))yield return new("Only flat SSE hard drives can support other SSE; they cannot support general furniture.",true);
    }
    internal static void Read(Layout doc,string value) {
        var w=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        if(w.Length!=3 || !int.TryParse(w[0],NumberStyles.None,CultureInfo.InvariantCulture,out int id) || id is <1 or >1000000 || w[2] is not ("REQUIRED" or "OPTIONAL") || doc.Sse.Count>=Limit || doc.Sse.Any(s=>s.PropId==id))throw new FormatException("sse needs a unique prop ID, encoded label and REQUIRED/OPTIONAL.");
        doc.Sse.Add(new(id,Uri.UnescapeDataString(w[1]),w[2]=="REQUIRED"));
    }
    internal static void Write(Layout doc,StringBuilder output){foreach(var s in doc.Sse.OrderBy(s=>s.PropId))output.Append($"sse: {s.PropId} {Uri.EscapeDataString(s.Label)} {(s.Required?"REQUIRED":"OPTIONAL")}\n");}
    public static bool ValidIds(int[]? ids)=>ids is {Length:<=Limit} && ids.All(id=>id is >=1 and <=1000000) && ids.Distinct().Count()==ids.Length;
}

// Reliable complete snapshots. Only the host advances a run epoch. Within a
// run collection is monotonic, and unknown IDs cannot partially apply a packet.
public sealed class SseRun
{
    private readonly HashSet<int> known=new(),collected=new();
    public long Epoch {get;private set;}
    public long Sequence {get;private set;}
    private long minimumEpoch=1;
    public bool Ready {get;private set;}
    public IReadOnlySet<int> Collected=>collected;
    public void Bind(Layout doc,bool authority){known.Clear();foreach(var s in doc.Sse)known.Add(s.PropId);collected.Clear();if(authority)Epoch++;minimumEpoch=authority?Epoch:Epoch+1;Ready=authority;}
    public bool Collect(int id,long epoch)=>Epoch>0 && epoch==Epoch && known.Contains(id) && collected.Add(id);
    public bool Receive(long epoch,long sequence,int[] ids) {
        if(epoch<minimumEpoch || epoch<Epoch || sequence<=Sequence || !SseObjectives.ValidIds(ids) || ids.Any(id=>!known.Contains(id)) || epoch==Epoch && collected.Any(id=>!ids.Contains(id)))return false;
        Epoch=epoch;Sequence=sequence;Ready=true;collected.Clear();foreach(int id in ids)collected.Add(id);return true;
    }
}
