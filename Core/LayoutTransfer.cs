using System.Text;

namespace OperatorKillhouse.Core;

// Reliable, ordered map data: selected host maps or guest load proposals.
// Receiving content only returns managed data; callers decide how it is used.
public sealed class LayoutTransfer
{
    public const int PartBytes=2048,MaxBytes=262144,MaxParts=MaxBytes/PartBytes,EncodedPartLimit=2732;
    private static readonly UTF8Encoding Utf8=new(false,true);
    private readonly CoopPacket identity;
    private readonly MemoryStream buffer=new();
    private readonly double started;
    private int next;
    public int Received=>next;
    public int Total=>identity.Total;
    public LayoutTransfer(CoopPacket first,double now) {
        if(first.Kind is not (CoopKind.LayoutChunk or CoopKind.ProposalChunk) || first.Part!=0 || !CoopProtocol.Valid(first) || !double.IsFinite(now))throw new InvalidDataException("Invalid shared-map header.");
        identity=first;started=now;
    }
    public bool Expired(double now)=>now<started || now-started>30;
    public Layout? Accept(CoopPacket packet,double now) {
        if(Expired(now) || packet.Kind!=identity.Kind || !CoopProtocol.Valid(packet) || packet.Host!=identity.Host || packet.Peer!=identity.Peer || packet.Revision!=identity.Revision ||
            packet.House!=identity.House || packet.Range!=identity.Range || packet.Index!=identity.Index || packet.Layout!=identity.Layout || packet.Total!=identity.Total || packet.Part!=next)
            throw new InvalidDataException("Shared-map transfer changed, arrived out of order, or timed out.");
        byte[] part;try{part=Convert.FromBase64String(packet.Data!);}catch(FormatException){throw new InvalidDataException("Invalid shared-map encoding.");}
        if(part.Length<1 || part.Length>PartBytes || next<packet.Total-1 && part.Length!=PartBytes || buffer.Length+part.Length>MaxBytes)throw new InvalidDataException("Shared-map data exceeds its limits.");
        buffer.Write(part);next++;if(next<packet.Total)return null;
        string text;try{text=Utf8.GetString(buffer.ToArray());}catch(DecoderFallbackException){throw new InvalidDataException("Shared map is not valid UTF-8.");}
        if(CoopProtocol.Digest(text)!=packet.Layout)throw new InvalidDataException("Shared-map content hash failed.");
        var parsed=LayoutFormat.Read(text,"host-shared-map");
        if(!parsed.Success || parsed.Layout!.Identity!=packet.Layout || (parsed.Layout.Houses&packet.House)==0 || LayoutValidation.Check(parsed.Layout).Any(i=>i.Fatal))throw new InvalidDataException("Shared map failed layout validation.");
        return parsed.Layout;
    }
    public static IReadOnlyList<CoopPacket> Packets(Layout layout,CoopPacket selection,CoopKind kind=CoopKind.LayoutChunk) {
        if(kind is not (CoopKind.LayoutChunk or CoopKind.ProposalChunk))throw new ArgumentException("Invalid map transfer kind.");
        byte[] bytes=Utf8.GetBytes(LayoutFormat.Write(layout));
        if(bytes.Length is <1 or >MaxBytes)throw new InvalidDataException("The selected map exceeds the 256 KiB sharing limit.");
        int count=(bytes.Length+PartBytes-1)/PartBytes;
        return Enumerable.Range(0,count).Select(n=>selection with{Kind=kind,Layout=layout.Identity,Geometry="",Masks=null,Part=n,Total=count,Data=Convert.ToBase64String(bytes,n*PartBytes,Math.Min(PartBytes,bytes.Length-n*PartBytes))}).ToArray();
    }
}
