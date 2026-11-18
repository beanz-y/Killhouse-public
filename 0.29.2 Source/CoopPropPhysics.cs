using Mirror;
using OperatorKillhouse.Core;

namespace OperatorKillhouse;

internal static partial class CoopNetwork
{
    internal static bool PropPhysicsPeers=>Ready && CoopDirectory.Remotes.Count>0;
    internal static void PropStates(RangeSession session,PropState[] states,long epoch) {
        if(!Ready || !NetworkServer.active || Switching)return;
        foreach(var peer in CoopDirectory.Remotes)if(welcomedPeers.TryGetValue(peer.Steam,out ulong nonce) && nonce==peer.Ad.Session)
            SendRpc(peer,new(CoopKind.PropState,context,peer.Ad.Session,Next(),epoch,session.House,session.Computer.netId,session.Computer.activeLayoutIndex,session.LayoutId(session.Computer.activeLayoutIndex),PropStates:states));
    }
    internal static void PushProp(RangeSession session,PropPush push,long epoch) {
        if(!ClientReady || Switching)return;
        SendCommand(new(CoopKind.PropPush,hostContext,context,Next(),epoch,session.House,session.Computer.netId,session.Computer.activeLayoutIndex,session.LayoutId(session.Computer.activeLayoutIndex),Push:push));
    }
}
