using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal static partial class CoopNetwork
{
    private static readonly Dictionary<(ulong Steam,int House),ulong> joinQueue=new();
    private static readonly Dictionary<(ulong Steam,int House),(ulong Session,string Layout)> caughtUp=new();
    internal static bool PreservingJoin=>NetworkServer.active && Available && CoopDirectory.HostCompatible &&
        !CoopDirectory.Remotes.Any(p=>peerFailures.ContainsKey(p.Steam)) &&
        (CoopDirectory.CheckingJoin || CoopDirectory.AllRemotesMatching && CoopDirectory.Remotes.Any(p=>!Welcomed(p)) &&
            CoopDirectory.Remotes.All(p=>Welcomed(p) || CoopDirectory.WithinJoinWindow(p.Steam)));
    private static bool Welcomed(CoopParticipant p)=>welcomedPeers.TryGetValue(p.Steam,out ulong session) && session==p.Ad.Session;
    internal static bool NativeSyncReady(ulong steam,int house,string layout)=>CoopDirectory.Remotes.FirstOrDefault(p=>p.Steam==steam) is {} peer &&
        caughtUp.TryGetValue((steam,house),out var proof) && proof.Session==peer.Ad.Session && proof.Layout==layout;
    internal static bool CatchingUp(ulong steam,int house)=>joinQueue.ContainsKey((steam,house)) || hostJobs.TryGetValue(house,out var job) && job.Catchup && job.Peers.ContainsKey(steam);
    private static IEnumerable<CoopParticipant> JobPeers(HostJob job)=>CoopDirectory.Remotes.Where(p=>job.Peers.TryGetValue(p.Steam,out ulong session) && session==p.Ad.Session);
    private static Dictionary<ulong,ulong> CurrentJobPeers(HostJob job)=>JobPeers(job).ToDictionary(p=>p.Steam,p=>p.Ad.Session);
    private static void QueueCatchup(CoopParticipant peer) {
        foreach(var session in Runtime.Sessions.Values) {
            if(!session.Computer || session.Computer.activeLayoutIndex<session.StockCount)continue;
            if(!NativeSyncReady(peer.Steam,session.House,session.LayoutId(session.Computer.activeLayoutIndex)))joinQueue[(peer.Steam,session.House)]=peer.Ad.Session;
        }
    }
    private static void TickJoinQueue() {
        foreach(var key in caughtUp.Keys.Where(key=>!CoopDirectory.Remotes.Any(p=>p.Steam==key.Steam && p.Ad.Session==caughtUp[key].Session)).ToArray())caughtUp.Remove(key);
        foreach(var key in joinQueue.Keys.Where(key=>!CoopDirectory.Remotes.Any(p=>p.Steam==key.Steam && p.Ad.Session==joinQueue[key])).ToArray())joinQueue.Remove(key);
        if(hostJobs.Count>0 || !Ready || joinQueue.Count==0)return;
        var item=joinQueue.First();joinQueue.Remove(item.Key);
        var peer=CoopDirectory.Remotes.FirstOrDefault(p=>p.Steam==item.Key.Steam && p.Ad.Session==item.Value && Welcomed(p));
        var session=Runtime.Sessions.Values.FirstOrDefault(s=>s.House==item.Key.House && s.Computer);
        if(peer==null || session==null)return;
        int index=session.Computer.activeLayoutIndex;if(index<session.StockCount || !session.IsPrepared(index))return;
        var computer=session.Computer;
        int[][] masks={NativeRangeContract.Mask(computer._targetindexdata)!,NativeRangeContract.Mask(computer._civeindexdata)!,NativeRangeContract.Mask(computer._hostagetakerindexdata)!};
        if(!Runtime.ValidateMasks(session,index,masks,true)){Fail("The active range has invalid target state for a joining player.");return;}
        var job=new HostJob{Session=session,Index=index,Identity=SelectionLog.Identity(computer,index,"late-join-catchup",session),Started=Time.unscaledTime,Revision=++revision,Masks=masks,Catchup=true,Peers=new(){{peer.Steam,peer.Ad.Session}}};
        var template=new CoopPacket(CoopKind.LayoutChunk,context,context,Next(),job.Revision,session.House,computer.netId,index);
        foreach(var packet in LayoutTransfer.Packets(session.Items[index-session.StockCount].Layout,template))job.Maps.Enqueue((peer,packet));
        hostJobs[session.House]=job;
        CoopLog.Event("join-catchup-queued",new{player=peer.Master.netId,house=session.House,index,layout=job.Identity.Layout});
    }
    private static void ResetJoining(){joinQueue.Clear();caughtUp.Clear();}
}
