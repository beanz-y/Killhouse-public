using Mirror;
using OperatorKillhouse.Core;

namespace OperatorKillhouse;
internal static partial class CoopNetwork
{
    internal static void SendSseState(RangeSession range,long epoch,int[] ids,int? acquired=null) {
        if(!Ready || !NetworkServer.active || Switching || !SseObjectives.ValidIds(ids) || acquired is {} id && !ids.Contains(id))return;
        foreach(var peer in CoopDirectory.Remotes)if(welcomedPeers.TryGetValue(peer.Steam,out ulong nonce) && nonce==peer.Ad.Session)
            SendRpc(peer,new(CoopKind.SseState,context,peer.Ad.Session,Next(),epoch,range.House,range.Computer.netId,range.Computer.activeLayoutIndex,range.LayoutId(range.Computer.activeLayoutIndex),SseCollected:ids,SseId:acquired));
    }
    internal static void RequestSse(RangeSession range,long epoch,int id) {
        if(!ClientReady || Switching)return;
        SendCommand(new(CoopKind.SseCollect,hostContext,context,Next(),epoch,range.House,range.Computer.netId,range.Computer.activeLayoutIndex,range.LayoutId(range.Computer.activeLayoutIndex),SseId:id));
    }
}
