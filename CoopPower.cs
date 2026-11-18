using Mirror;
using OperatorKillhouse.Core;

namespace OperatorKillhouse;

internal static partial class CoopNetwork
{
    internal static void AnimateBreaker(RangeSession session,BreakerAnimation animation,long epoch) {
        if(!Ready || !NetworkServer.active || Switching || !BreakerTiming.Valid(animation))return;
        foreach(var peer in CoopDirectory.Remotes)if(welcomedPeers.TryGetValue(peer.Steam,out ulong nonce) && nonce==peer.Ad.Session)
            SendRpc(peer,new(CoopKind.BreakerAnimation,context,peer.Ad.Session,Next(),epoch,session.House,session.Computer.netId,session.Computer.activeLayoutIndex,session.LayoutId(session.Computer.activeLayoutIndex),BreakerAnimation:animation));
    }
    internal static void CircuitState(RangeSession session,int mask,long epoch,int[] brokenLights) {
        if(!Ready || !NetworkServer.active || Switching)return;
        foreach(var peer in CoopDirectory.Remotes)if(welcomedPeers.TryGetValue(peer.Steam,out ulong nonce) && nonce==peer.Ad.Session)
            SendRpc(peer,new(CoopKind.CircuitState,context,peer.Ad.Session,Next(),epoch,session.House,session.Computer.netId,session.Computer.activeLayoutIndex,session.LayoutId(session.Computer.activeLayoutIndex),Power:mask,BrokenLights:brokenLights));
    }
    internal static void ToggleBreaker(RangeSession session,int id,long epoch) {
        if(!ClientReady || Switching)return;
        SendCommand(new(CoopKind.BreakerToggle,hostContext,context,Next(),epoch,session.House,session.Computer.netId,session.Computer.activeLayoutIndex,session.LayoutId(session.Computer.activeLayoutIndex),Breaker:id));
    }
    internal static void ShootLight(RangeSession session,LightShot shot,long epoch) {
        if(!ClientReady || Switching || !LightDamage.Valid(shot))return;
        SendCommand(new(CoopKind.LightShot,hostContext,context,Next(),epoch,session.House,session.Computer.netId,session.Computer.activeLayoutIndex,session.LayoutId(session.Computer.activeLayoutIndex),LightShot:shot));
    }
}
