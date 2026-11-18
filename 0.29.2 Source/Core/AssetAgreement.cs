namespace OperatorKillhouse.Core;

public sealed record AssetSignature(string Hash,string[] Parts,string[][] Rows);
public static class AssetAgreement
{
    public static readonly IReadOnlyList<string> Names=Array.AsReadOnly(new[]{"walls","frames","doors","stock","targets","furniture"});
    public static AssetSignature Make(string[][] rows) {
        if(rows.Length!=Names.Count || rows.Any(r=>r==null))throw new InvalidDataException("Incomplete native asset contract.");
        var copy=rows.Select(r=>r.ToArray()).ToArray();
        var parts=copy.Select((r,n)=>CoopProtocol.Digest(Names[n]+"\n"+string.Join("\n",r))).ToArray();
        return new(CoopProtocol.Digest("native-assets-v2\n"+string.Join("\n",parts)),parts,copy);
    }
    public static bool ValidParts(string[][]? parts)=>parts is {Length:2} && parts.All(p=>p!=null && p.Length==Names.Count && p.All(CoopProtocol.Hash));
    public static string Differences(IReadOnlyList<string[]> local,string[][]? remote) {
        if(local.Count!=2 || !ValidParts(remote))return "incomplete asset details";
        var issues=new List<string>();
        for(int house=0;house<2;house++) {
            var names=Names.Where((_,n)=>local[house][n]!=remote![house][n]).ToArray();
            if(names.Length>0)issues.Add($"house {house+1}: "+string.Join(", ",names));
        }
        return string.Join("; ",issues);
    }
    // Scene identities are authored into the game scene. Unlike hierarchy order,
    // their identity does not change when a borrowed door is reparented.
    public static T[] OrderDoors<T>(IEnumerable<T> doors,Func<T,ulong> id) {
        var values=doors.Select(d=>(Door:d,Id:id(d))).ToArray();
        if(values.Any(d=>d.Id==0) || values.Select(d=>d.Id).Distinct().Count()!=values.Length)throw new InvalidOperationException("Native doors need unique scene identities.");
        return values.OrderBy(d=>d.Id).Select(d=>d.Door).ToArray();
    }
}
