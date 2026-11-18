using Mirror;
using OperatorKillhouse.Core;
using IntList=Il2CppSystem.Collections.Generic.List<int>;

namespace OperatorKillhouse;
internal static partial class Runtime
{
    private static void BeforeClientReady(ref JoinSpawnLease<RangeSession>? __state) {
        __state=null;if(!NetworkServer.active)return;
        var active=Sessions.Values.Where(s=>s.Computer && s.Computer.activeLayoutIndex>=s.StockCount).ToArray();
        if(active.Length==0)return;
        __state=new(active,s=>s.Computer.activeLayoutIndex,StockForJoin,(s,index)=>{if(s.Computer)s.Computer.activeLayoutIndex=index;});
    }
    private static int StockForJoin(RangeSession session) {
        for(int n=0;n<session.StockCount;n++)try{NativeRangeContract.Validate(session.Computer,n);return n;}catch{}
        throw new InvalidOperationException("No valid stock baseline is available for the joining player.");
    }
    private static void AfterClientReady(JoinSpawnLease<RangeSession>? __state)=>__state?.Dispose();
    private static Exception? ClientReadyFinalizer(Exception? __exception,JoinSpawnLease<RangeSession>? __state){__state?.Dispose();return __exception;}
    private static bool BeforeSync(RangeComputer __instance,NetworkConnectionToClient? __0) {
        if(!NetworkServer.active || !__instance || __0==null || !__0.isAuthenticated || NetworkServer.localConnection!=null && __0.Pointer==NetworkServer.localConnection.Pointer || !Sessions.TryGetValue(__instance.GetInstanceID(),out var session) || __instance.activeLayoutIndex<session.StockCount)return true;
        var master=__0.identity?__0.identity.GetComponent<PlayerMaster>():null;
        if(master && CoopNetwork.NativeSyncReady(master!.thisPlayerSteam64,session.House,session.LayoutId(__instance.activeLayoutIndex)))return true;
        if(master && CoopNetwork.CatchingUp(master!.thisPlayerSteam64,session.House))return false;
        int stock=StockForJoin(session);var layout=__instance.shootHouseLayouts[stock];
        __instance.TargetRPC_SyncLayoutWithNewPlayer(__0,stock,All(layout.targets.Length),All(layout.civilians.Length),All(layout.hostageTakers.Length));
        return false;
    }
    private static bool BeforeSyncDispatch(NetworkBehaviour __0,NetworkConnectionToClient __2) {
        var computer=__0.TryCast<RangeComputer>();return computer is null || !computer || BeforeSync(computer,__2);
    }
}

internal sealed partial class RangeSession
{
    internal void SendJoinState(NetworkConnectionToClient? connection) {
        if(connection==null || !connection.isAuthenticated || !connection.isReady || !Computer)return;
        NetworkServer.SendSpawnMessage(Computer.netIdentity,connection);
        foreach(var door in Computer.shootHouseLayouts[Computer.activeLayoutIndex].doors)if(door)door.UserCode_CMD_RequestCurrentDoorState__NetworkConnectionToClient(connection);
    }
}
