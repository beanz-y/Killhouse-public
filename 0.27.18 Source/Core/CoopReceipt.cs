namespace OperatorKillhouse.Core;

// Only a recently completed exact selection can replay release. This never
// activates geometry, adds a peer, or acknowledges a newer transaction.
public sealed class CoopReceipt {
    private readonly CoopPacket selection;
    private readonly Dictionary<ulong,ulong> peers;
    private readonly double started;
    public CoopReceipt(CoopPacket selection,IReadOnlyDictionary<ulong,ulong> peers,double now){this.selection=selection;this.peers=new(peers);started=now;}
    public bool Matches(ulong actor,CoopPacket packet,double now)=>double.IsFinite(now) && now>=started && now-started<60 && packet.Kind==CoopKind.Applied &&
        peers.TryGetValue(actor,out ulong session) && packet.Peer==session && CoopProtocol.SameSelection(selection,packet);
    public CoopPacket Release=>selection with{Kind=CoopKind.Release};
}
