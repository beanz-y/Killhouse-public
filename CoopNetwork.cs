using Il2CppInterop.Runtime;
using Mirror;
using Mirror.RemoteCalls;
using OperatorKillhouse.Core;
using UnityEngine;
using Random=System.Random;

namespace OperatorKillhouse;
internal static partial class CoopNetwork
{
    private const string CommandName="OperatorKillhouse.V1.Client",RpcName="OperatorKillhouse.V1.Host";
    private static readonly int commandHash=CoopProtocol.RpcHash(CommandName),rpcHash=CoopProtocol.RpcHash(RpcName);
    private static RemoteCallDelegate? command,rpc;
    private static bool installed,welcomed,paused;
    private static string failure="";
    private static string transportFailure="";
    private static string hostMenuBlock="";
    private static readonly Dictionary<ulong,string> peerFailures=new();
    private static string loggedStatus="";
    private static ulong context,hostContext;
    private static long sequence,revision;
    private static float helloAt;
    private static RangeSession? contractFirst,contractSecond;
    private static string[]? cachedContracts;
    private static string[][]? cachedParts;
    private static readonly CoopReplay replay=new();
    private static readonly Dictionary<ulong,ulong> welcomedPeers=new();
    private static readonly Dictionary<int,HostJob> hostJobs=new();
    private static readonly Dictionary<int,ClientJob> clientJobs=new();
    private static readonly Dictionary<int,long> committed=new();
    private static readonly Dictionary<(int House,long Revision),CoopReceipt> releases=new();
    private static readonly Dictionary<ulong,(int Window,int Count)> rates=new();
    private static LayoutTransfer? incomingMap;
    private static readonly Dictionary<int,string> resumeAfterJoin=new();
    private sealed class HostJob {internal SelectionIdentity Identity=null!;internal RangeSession Session=null!;internal int Index;internal long Revision;internal float Started,NextControl;internal bool Catchup;internal Dictionary<ulong,ulong> Peers=new();internal int[][]? Masks;internal CoopTransaction? Transaction;internal readonly Queue<(CoopParticipant Peer,CoopPacket Packet)> Maps=new();}
    private sealed class ClientJob {internal CoopPacket Packet=null!;internal RangeSession Session=null!;internal float Started,NextAck;internal bool Ready,Applied;}
    internal static bool Available=>installed && Plugin.Coop.Value && !paused;
    internal static bool CanAttach=>Available && (NetworkServer.active?CoopDirectory.AllRemotesMatching:CoopDirectory.HostCompatible);
    internal static bool Ready=>Available && CoopDirectory.AllRemotesMatching && Contracts()!=null && CoopDirectory.Remotes.All(p=>welcomedPeers.TryGetValue(p.Steam,out ulong nonce) && nonce==p.Ad.Session);
    internal static bool ClientReady=>Available && !NetworkServer.active && welcomed && CoopDirectory.HostCompatible;
    internal static bool Busy=>hostJobs.Count>0 || clientJobs.Count>0 || incomingMap!=null || joinQueue.Count>0;
    internal static bool Switching=>hostJobs.Values.Any(j=>j.Transaction!=null && !j.Catchup) || clientJobs.Count>0 || incomingMap!=null;
    private static string OperationStatus=>incomingMap!=null?$"Receiving host map: {incomingMap.Received}/{incomingMap.Total} parts…":
        NetworkServer.active && hostJobs.Values.FirstOrDefault() is {} job?job.Maps.Count>0?$"Sending {job.Identity.Layout}: {job.Maps.Count} parts left…":job.Transaction==null?"Preparing the saved map revision…":job.Transaction.Phase==CoopPhase.Waiting?Awaiting(job.Transaction):"Applying the map revision. "+Awaiting(job.Transaction):
        joinQueue.Count>0?"Synchronizing the current range to joining players. The current run continues.":"Preparing the host's map revision. Training resumes after everyone applies it.";
    internal static string Status=>paused?"Co-op paused: "+failure+" Re-enter the range to retry.":!Available?"Co-op is disabled or its network handlers are unavailable.":
        Busy?OperationStatus:!CoopDirectory.HostCompatible?CoopDirectory.Reason:NetworkServer.active?Ready?$"Co-op ready: {CoopDirectory.Remotes.Count} other player(s).":!CoopDirectory.AllRemotesMatching?CoopDirectory.Reason:failure.Length>0?failure:Contracts()==null?"Waiting for both range houses and matching furniture settings.":"Checking range assets with players…":
        incomingMap!=null?$"Receiving host map: {incomingMap.Received}/{incomingMap.Total} parts…":welcomed?"Following the host's range selections. Maps arrive automatically.":"Checking range assets with the host…";
    internal static string? MenuBlockReason {
        get {
            if(!Plugin.Coop.Value)return "Co-op ranges are turned off on this PC. Enable Killhouse > Co-op ranges in Mod Controls.";
            if(!installed)return "Killhouse networking could not start. "+transportFailure+" Resolve any conflicting mods, then restart the game.";
            if(paused)return "Co-op paused: "+failure+" Re-enter the range to retry.";
            if(!CanAttach)return CoopDirectory.MenuBlockReason;
            foreach(var peer in CoopDirectory.Remotes)if(peerFailures.TryGetValue(peer.Steam,out var reason))return CoopDirectory.PlayerLabel(peer.Steam)+": "+reason;
            if(Runtime.RangeSetupFailure.Length>0)return Runtime.RangeSetupFailure+" Re-enter the range to retry.";
            if(!NetworkServer.active) {
                if(hostMenuBlock.Length>0)return "The host cannot enable Killhouse yet: "+hostMenuBlock;
                if(!ClientReady)return "Waiting for the host to finish Killhouse asset checks. Everyone needs the same version and Furniture setting. Wait a moment, or rejoin the range if this persists.";
            }else if(!Ready)return Contracts()==null?"Waiting for both training houses to finish loading their range assets. Wait a moment, or re-enter the range if this persists.":
                "Checking range assets with the other players. Wait a moment, or rejoin the range if this persists.";
            return null;
        }
    }
    private static string LobbyMenuNotice() {
        // The host knows which connections block the whole lobby. Carry that
        // explanation in the existing authenticated renewal response so guests
        // need not guess another player's readiness from cached Steam data.
        string reason=!CoopDirectory.AllRemotesMatching?CoopDirectory.MenuBlockReason:"";
        if(reason.Length==0)foreach(var peer in CoopDirectory.Remotes)if(peerFailures.TryGetValue(peer.Steam,out var error)){reason=CoopDirectory.PlayerLabel(peer.Steam)+": "+error;break;}
        if(reason.Length==0 && !Ready)reason="Waiting for the remaining players to finish Killhouse asset checks.";
        if(reason.Length>240){int end=reason.LastIndexOf(' ',236,237);reason=reason[..(end>0?end:236)]+"...";}
        return MenuNotice.Plain(reason,240);
    }
    private static bool Own(ushort hash,RemoteCallDelegate? handler)=>handler!=null && RemoteProcedureCalls.GetDelegate(hash)?.Pointer==handler.Pointer;
    internal static void Install() {
        try {
            if((ushort)commandHash==(ushort)rpcHash || RemoteProcedureCalls.GetDelegate((ushort)commandHash)!=null || RemoteProcedureCalls.GetDelegate((ushort)rpcHash)!=null)throw new InvalidOperationException("Killhouse network hash collision.");
            command=(Action<NetworkBehaviour,NetworkReader,NetworkConnectionToClient>)ReceiveCommand;
            rpc=(Action<NetworkBehaviour,NetworkReader,NetworkConnectionToClient>)ReceiveRpc;
            RemoteProcedureCalls.RegisterCommand(Il2CppType.Of<PlayerMaster>(),CommandName,command,true);
            RemoteProcedureCalls.RegisterRpc(Il2CppType.Of<PlayerMaster>(),RpcName,rpc);
            if(!Own((ushort)commandHash,command) || !Own((ushort)rpcHash,rpc))throw new InvalidOperationException("Co-op delegates were not retained.");
            VerifyWire();installed=true;transportFailure="";
        }catch(Exception error){Dispose();transportFailure=MenuNotice.Plain(error.GetBaseException().Message,180);CoopLog.Event("transport-unavailable",new{error=error.ToString()});}
    }
    internal static void Tick() {
        try{TickCore();}
        finally {
            string status=Status;
            if(status!=loggedStatus){loggedStatus=status;CoopLog.Event("readiness",new{host=NetworkServer.active,status,houses=Runtime.Sessions.Count,connected=Runtime.RemoteCount(),compatible=CoopDirectory.Remotes.Count});}
        }
    }
    private static void TickCore() {
        CoopDirectory.Tick();
        if(!Available)return;
        if(!Own((ushort)commandHash,command) || !Own((ushort)rpcHash,rpc)){Fail("Another plugin replaced a Killhouse network handler.");return;}
        if(context!=CoopDirectory.Session || hostContext!=CoopDirectory.HostSession) {
            bool interrupted=Busy || hostContext!=0 && hostContext!=CoopDirectory.HostSession;ClearOperations();ResetEditors();hostMenuBlock="";peerFailures.Clear();welcomedPeers.Clear();replay.Clear();rates.Clear();committed.Clear();welcomed=false;
            context=CoopDirectory.Session;hostContext=CoopDirectory.HostSession;sequence=revision=0;helloAt=0;
            if(interrupted)Runtime.SuspendCustom("Co-op session changed.");
        }
        // A matching native player/advertisement can recover an expired Steam
        // lease over the reliable connection; ordinary packets still require
        // fresh proof and the completed asset handshake.
        if(!NetworkServer.active && CoopDirectory.HostKnown && Time.unscaledTime>=helloAt && Contracts() is {} renewal) {
            helloAt=Time.unscaledTime+3;SendCommand(new(CoopKind.Hello,hostContext,context,Next(),Manifest:Runtime.CatalogState.Manifest,Contracts:renewal,Parts:cachedParts));
        }
        if(!CoopDirectory.HostCompatible) {
            if(Busy || Runtime.HasCustomActive && Runtime.RemoteCount()>0)Runtime.SuspendCustom(Status);
            return;
        }
        var contracts=Contracts();
        if(contracts==null){if(Busy)Fail("Range assets or furniture settings changed during preparation.");return;}
        if(!NetworkServer.active) {
            if(incomingMap?.Expired(Time.unscaledTime)==true){Fail("Shared-map transfer timed out.");return;}
            TickProposalUpload();TickClients();return;
        }
        TickEditorAuthority();
        if((Runtime.HasCustomActive || Busy) && !Ready && !PreservingJoin){QueueActiveForJoin();Runtime.SuspendCustom("Killhouse compatibility checks failed or timed out; ranges restored to stock.");return;}
        TickJoinQueue();
        if(Ready && Runtime.RemoteCount()>0 && !Busy && resumeAfterJoin.Count>0) {
            var next=resumeAfterJoin.First();resumeAfterJoin.Remove(next.Key);
            var session=Runtime.Sessions.Values.FirstOrDefault(s=>s.House==next.Key);
            if(session!=null){int n=session.Items.ToList().FindIndex(i=>i.Id==next.Value);if(n>=0)Runtime.Select(session.Computer,session.StockCount+n,"late-join-map-share");}
        }
        foreach(var job in hostJobs.Values.ToArray()) {
            if(job.Catchup && !JobPeers(job).Any()){hostJobs.Remove(job.Session.House);CoopLog.Event("join-catchup-cancelled",new{house=job.Session.House,reason="Joining player disconnected."});continue;}
            if(!job.Session.Computer || job.Transaction==null && Time.unscaledTime-job.Started>30){Fail("Host map transfer/build timed out or the range unloaded.");return;}
            string? error=job.Session.BuildError(job.Index);if(error!=null){Fail(error);return;}
            int sent=0;while(job.Maps.Count>0 && sent++<4){var item=job.Maps.Dequeue();SendRpc(item.Peer,item.Packet);}
            if(job.Maps.Count>0)continue;
            if(!job.Session.IsPrepared(job.Index))continue;
            if(job.Transaction==null) {
                var computer=job.Session.Computer;
                int[][] masks=job.Catchup?job.Masks!:Runtime.NewTargetMasks(job.Session,job.Index,job.Masks);
                if(!Runtime.ValidateMasks(job.Session,job.Index,masks,job.Catchup)){Fail("Host target mask validation failed.");return;}
                var packet=new CoopPacket(CoopKind.Prepare,context,context,Next(),job.Revision,job.Session.House,computer.netId,job.Index,job.Session.LayoutId(job.Index),job.Session.Digest(job.Index),Masks:masks,Catchup:job.Catchup,Lighting:job.Catchup?MapNightMode.Current:job.Session.LightingMap(job.Index));
                job.Transaction=new(packet,job.Peers,Time.unscaledTime);
                foreach(var peer in JobPeers(job))SendRpc(peer,packet);
                CoopLog.Event("prepare-sent",new{packet.House,packet.Revision,packet.Index,packet.Layout,packet.Geometry,peers=CoopDirectory.Remotes.Count});
            }
            var tx=job.Transaction;
            string awaiting=Awaiting(tx);
            if(!tx.Check(Time.unscaledTime,CurrentJobPeers(job)) || tx.Phase==CoopPhase.Aborted){Fail(tx.Failure+" "+awaiting);return;}
            if(tx.Phase==CoopPhase.Waiting && tx.Commit(Time.unscaledTime)) {
                if(!JoinSynchronization.ApplyHost(tx.Selection,()=>Runtime.ApplyPrepared(job.Session,job.Index,job.Identity,tx.Selection.Masks!))){Fail("Host native activation failed.");return;}
                if(!job.Catchup)committed[job.Session.House]=tx.Selection.Revision;
                foreach(var peer in JobPeers(job))SendRpc(peer,tx.Selection with{Kind=CoopKind.Commit});
                job.NextControl=Time.unscaledTime+2;
                CoopLog.Event("commit-sent",new{tx.Selection.House,tx.Selection.Revision});
            }
            if(tx.Phase==CoopPhase.Committing && Time.unscaledTime>=job.NextControl) {
                job.NextControl=Time.unscaledTime+2;
                foreach(var peer in JobPeers(job).Where(p=>tx.AwaitingApplied.Contains(p.Steam)))SendRpc(peer,tx.Selection with{Kind=CoopKind.Commit});
            }
            if(tx.Phase==CoopPhase.Complete) {
                if(releases.Count>=66)releases.Remove(releases.Keys.First());
                releases[(job.Session.House,tx.Selection.Revision)]=new(tx.Selection,job.Peers,Time.unscaledTime);
                foreach(var peer in JobPeers(job)) {
                    SendRpc(peer,tx.Selection with{Kind=CoopKind.Release});
                    caughtUp[(peer.Steam,job.Session.House)]=(peer.Ad.Session,tx.Selection.Layout);
                    if(job.Catchup)job.Session.SendJoinState(peer.Master.connectionToClient);
                }
                hostJobs.Remove(job.Session.House);job.Session.TrimCache(job.Index);
                Runtime.Status=$"House {job.Session.House}: {job.Identity.Layout} ready for all players.";
                if(!job.Catchup)FinishQueuedLoad(tx.Selection.House,tx.Selection.Layout,true);
                CoopLog.Event("commit-complete",new{tx.Selection.House,tx.Selection.Revision,peers=CoopDirectory.Remotes.Count});
            }
            break; // Never put two native range activations into the same frame.
        }
    }
    private static string Awaiting(CoopTransaction tx) {
        var missing=tx.Phase==CoopPhase.Waiting?tx.AwaitingReady:tx.AwaitingApplied;
        if(missing.Count==0)return "All expected acknowledgements received.";
        return "Awaiting "+(tx.Phase==CoopPhase.Waiting?"ready":"applied")+" from "+string.Join(", ",missing.Select(id=>CoopDirectory.Remotes.FirstOrDefault(p=>p.Steam==id) is {} peer?$"{PlayerName(peer)} (player {peer.Master.netId})":"disconnected player"))+".";
    }
    private static string[]? Contracts() {
        RangeSession? first=null,second=null;
        foreach(var session in Runtime.Sessions.Values) {
            if(session.FurnitureMode!=Plugin.Furniture.Value)return null;
            if(session.House==1 && first==null)first=session;
            else if(session.House==2 && second==null)second=session;
            else return null;
        }
        if(first==null || second==null)return null;
        if(first!=contractFirst || second!=contractSecond || cachedContracts==null) {
            contractFirst=first;contractSecond=second;cachedContracts=new[]{first.Contract,second.Contract};
            cachedParts=new[]{first.Assets.Parts,second.Assets.Parts};
        }
        return cachedContracts;
    }
    internal static bool Request(RangeSession session,int index,SelectionIdentity identity,int[][]? masks=null) {
        if(!NetworkServer.active || !Ready || Busy || !session.BeginStage(index))return false;
        resumeAfterJoin.Remove(session.House);
        var job=new HostJob{Session=session,Index=index,Identity=identity,Started=Time.unscaledTime,Masks=masks,Revision=++revision,Peers=CoopDirectory.Participants()};
        if(index>=session.StockCount) {
            var doc=session.Items[index-session.StockCount].Layout;
            var template=new CoopPacket(CoopKind.LayoutChunk,context,context,Next(),job.Revision,session.House,session.Computer.netId,index);
            IReadOnlyList<CoopPacket> parts;
            try{parts=LayoutTransfer.Packets(doc,template);}catch(Exception error){session.CancelStage();failure=error.GetBaseException().Message;SelectionLog.Note(identity,"failed","map-share",failure);return false;}
            foreach(var peer in CoopDirectory.Remotes)foreach(var part in parts)job.Maps.Enqueue((peer,part));
            CoopLog.Event("map-share-queued",new{session.House,index,layout=doc.Name,id=doc.Identity,parts=parts.Count,peers=CoopDirectory.Remotes.Count});
        }
        hostJobs[session.House]=job;
        SelectionLog.Note(identity,"queued","co-op-prepare","Preparing geometry before peer agreement.");return true;
    }
    private static void TickClients() {
        foreach(var job in clientJobs.Values.ToArray()) {
            if(Time.unscaledTime-job.Started>30 || !job.Session.Computer){ClientFault(job,"Client preparation/commit timed out.");return;}
            string? error=job.Session.BuildError(job.Packet.Index);if(error!=null){ClientFault(job,error);return;}
            if(!job.Ready) {
                if(!job.Session.IsPrepared(job.Packet.Index))continue;
                if(job.Session.Digest(job.Packet.Index)!=job.Packet.Geometry){ClientFault(job,"Prepared geometry differs from the host.");return;}
                if(!Runtime.ValidateMasks(job.Session,job.Packet.Index,job.Packet.Masks!,job.Packet.Catchup)){ClientFault(job,"Target masks differ from the host.");return;}
                if(!MapNightMode.Ready(job.Packet.Lighting??MapLighting.Normal,out string lightingReason)){ClientFault(job,lightingReason);return;}
                job.Ready=true;job.Started=Time.unscaledTime;
                CoopLog.Event("client-ready",new{job.Packet.House,job.Packet.Revision,job.Packet.Geometry});
            }
            if(Time.unscaledTime>=job.NextAck){job.NextAck=Time.unscaledTime+2;SendCommand(job.Packet with{Kind=job.Applied?CoopKind.Applied:CoopKind.Ready,Peer=context,Sequence=Next()});}
        }
    }
    private static long Next()=>++sequence;
    private static byte[] Read(NetworkReader reader) {
        if(reader.Remaining is <=0 or >CoopProtocol.MaxBytes)throw new InvalidDataException("Invalid co-op payload size.");
        var segment=reader.ReadBytesSegment(reader.Remaining);var array=segment.Array??throw new InvalidDataException();
        var bytes=new byte[segment.Count];for(int n=0;n<bytes.Length;n++)bytes[n]=array[segment.Offset+n];return bytes;
    }
    private static void SendCommand(CoopPacket packet) {
        var local=PlayerMaster.MyPlayerMaster;
        if(!Available || !(CoopDirectory.HostCompatible || packet.Kind==CoopKind.Hello && CoopDirectory.HostKnown) || !local || !local.isOwned)return;
        var bytes=CoopProtocol.Encode(packet);var writer=NetworkWriterPool.Get();
        try{writer.WriteBytes(bytes,0,bytes.Length);local.SendCommandInternal(CommandName,commandHash,writer,Channels.Reliable,true);}
        finally{writer.Dispose();}
    }
    private static void SendRpc(CoopParticipant peer,CoopPacket value) {
        var connection=peer.Master.connectionToClient;if(connection==null || !connection.isAuthenticated || !connection.isReady)return;
        var bytes=CoopProtocol.Encode(value with{Peer=peer.Ad.Session,Sequence=Next()});var payload=NetworkWriterPool.Get();var writer=NetworkWriterPool.Get();
        try {
            payload.WriteBytes(bytes,0,bytes.Length);
            WriteRpc(writer,new RpcMessage{netId=peer.Master.netId,componentIndex=peer.Master.ComponentIndex,functionHash=(ushort)rpcHash,payload=payload.ToArraySegment()});
            connection.Send(writer.ToArraySegment(),Channels.Reliable);
        }finally{writer.Dispose();payload.Dispose();}
    }
    internal static void CleanEncounter(RangeSession session,int index) {
        if(!Available || !NetworkServer.active || context==0 || !session.Computer || index<0 || index>=session.Computer.shootHouseLayouts.Length)return;
        foreach(var peer in CoopDirectory.Remotes)if(welcomedPeers.TryGetValue(peer.Steam,out ulong nonce) && nonce==peer.Ad.Session)
            SendRpc(peer,new(CoopKind.EncounterClean,context,peer.Ad.Session,Next(),1,session.House,session.Computer.netId,index,session.LayoutId(index)));
    }
    private static void ReceiveCommand(NetworkBehaviour behaviour,NetworkReader reader,NetworkConnectionToClient sender) {
        try {
            var master=behaviour?behaviour.TryCast<PlayerMaster>():null;
            if(!Available || !NetworkServer.active || !master || sender==null || !sender.isAuthenticated || !sender.isReady || master!.connectionToClient==null || master.connectionToClient.connectionId!=sender.connectionId)return;
            ulong steam=master.thisPlayerSteam64;
            int window=(int)Time.unscaledTime;var rate=rates.GetValueOrDefault(steam);int count=rate.Window==window?rate.Count+1:1;rates[steam]=(window,count);if(count>32)return;
            var packet=CoopProtocol.Decode(Read(reader));
            var peer=packet==null?null:CoopDirectory.Peer(master,packet.Kind==CoopKind.Hello);
            if(peer==null || packet==null || packet.Host!=context || packet.Peer!=peer.Ad.Session || !replay.Accept(peer.Steam,packet.Peer,packet.Sequence))return;
            if(packet.Kind==CoopKind.Hello) {
                if(!CoopDirectory.AllRemotesMatching && !PreservingJoin) {
                    // There may be no attached range contracts while the host
                    // has restored stock for an incompatible third player.
                    SendRpc(peer,new(CoopKind.Abort,context,peer.Ad.Session,Next(),Detail:LobbyMenuNotice(),Pause:false));return;
                }
                var contracts=Contracts();if(contracts==null)return;
                if(!CoopProtocol.Agree(Runtime.CatalogState.Manifest,contracts,packet)){
                    string differences=AssetAgreement.Differences(cachedParts!,packet.Parts);
                    failure="Native assets differ ("+differences+"). Check asset-load-report.json. Enable Diagnostic logging and re-enter the range for detailed house reports.";
                    foreach(ulong departed in peerFailures.Keys.Where(id=>!CoopDirectory.Remotes.Any(p=>p.Steam==id)).ToArray())peerFailures.Remove(departed);
                    if(peerFailures.Count>=32)peerFailures.Remove(peerFailures.Keys.First());
                    peerFailures[peer.Steam]=failure;
                    welcomedPeers.Remove(peer.Steam);
                    CoopLog.Event("peer-rejected",new{player=master.netId,reason=failure,local=contracts,remote=packet.Contracts,differences});
                    SendRpc(peer,new(CoopKind.Abort,context,peer.Ad.Session,Next(),Detail:failure.Length>230?failure[..230]:failure,Pause:true));return;
                }
                peerFailures.Remove(peer.Steam);if(peerFailures.Count==0)failure="";
                CoopDirectory.Confirm(peer);
                bool changed=!welcomedPeers.TryGetValue(peer.Steam,out ulong nonce) || nonce!=peer.Ad.Session;welcomedPeers[peer.Steam]=peer.Ad.Session;
                SendRpc(peer,new(CoopKind.Welcome,context,peer.Ad.Session,Next(),Manifest:Runtime.CatalogState.Manifest,Contracts:contracts,Parts:cachedParts,Detail:LobbyMenuNotice(),Lighting:MapNightMode.Current));
                if(changed)SendEditorPermission(peer);
                if(changed)QueueCatchup(peer);
                if(changed)CoopLog.Event("peer-agreed",new{player=master.netId});return;
            }
            if(!welcomedPeers.TryGetValue(peer.Steam,out ulong session) || session!=packet.Peer)return;
            if(packet.Kind==CoopKind.SseCollect){var range=Runtime.Sessions.Values.FirstOrDefault(s=>s.PropContext(packet));if(range!=null && master.PlayerSpawnedObject)range.ReceiveSseCollect(packet,master.PlayerSpawnedObject);return;}
            if(packet.Kind==CoopKind.BreakerToggle){var range=Runtime.Sessions.Values.FirstOrDefault(s=>s.PropContext(packet));if(range!=null && master.PlayerSpawnedObject)range.ReceiveBreaker(packet,master.PlayerSpawnedObject);return;}
            if(packet.Kind==CoopKind.LightShot){var range=Runtime.Sessions.Values.FirstOrDefault(s=>s.PropContext(packet));if(range!=null && master.PlayerSpawnedObject)range.ReceiveLightShot(packet,master.PlayerSpawnedObject);return;}
            if(packet.Kind==CoopKind.PropPush){var range=Runtime.Sessions.Values.FirstOrDefault(s=>s.PropContext(packet));if(range!=null && master.PlayerSpawnedObject)range.ReceivePropPush(packet,master.PlayerSpawnedObject);return;}
            if(packet.Kind==CoopKind.ProposalChunk){ReceiveProposal(peer,packet);return;}
            if(packet.Kind==CoopKind.Fault){Fail("A peer could not activate the range: "+packet.Detail);return;}
            if(releases.TryGetValue((packet.House,packet.Revision),out var receipt) && receipt.Matches(peer.Steam,packet,Time.unscaledTime)){SendRpc(peer,receipt.Release);return;}
            if(hostJobs.TryGetValue(packet.House,out var job) && job.Transaction!=null) {
                if(job.Transaction.Receive(peer.Steam,packet))CoopLog.Event(packet.Kind==CoopKind.Ready?"peer-ready":"peer-applied",new{player=master.netId,packet.House,packet.Revision,waiting=Awaiting(job.Transaction)});
                if(job.Transaction.Phase==CoopPhase.Aborted)Fail(job.Transaction.Failure);
            }
        }catch(Exception error){CoopLog.Event("command-rejected",new{error=error.GetBaseException().Message});}
    }
    private static void ReceiveRpc(NetworkBehaviour behaviour,NetworkReader reader,NetworkConnectionToClient sender) {
        try {
            var master=behaviour?behaviour.TryCast<PlayerMaster>():null;
            if(!Available || NetworkServer.active || !NetworkClient.isConnected || !master || !master!.isOwned || !CoopDirectory.HostKnown)return;
            var packet=CoopProtocol.Decode(Read(reader));
            if(packet==null || packet.Host!=hostContext || packet.Peer!=context || !replay.Accept(1,packet.Host,packet.Sequence))return;
            if(packet.Kind==CoopKind.Abort){
                bool changed=hostMenuBlock!=packet.Detail || paused!=packet.Pause;
                hostMenuBlock=packet.Detail;paused=packet.Pause;failure=paused?packet.Detail:"";
                // Renewal messages must not repeatedly tear down/rebuild the
                // same stock range while one lobby player remains incompatible.
                if(changed || Runtime.HasCustomActive || Busy)Runtime.SuspendCustom("Host cancelled co-op: "+packet.Detail);
                return;
            }
            if(packet.Kind==CoopKind.Welcome) {
                var contracts=Contracts();bool before=welcomed;welcomed=contracts!=null && CoopProtocol.Agree(Runtime.CatalogState.Manifest,contracts,packet);
                hostMenuBlock=welcomed?packet.Detail:"The host's native range assets differ. Compare the range asset reports and re-enter the range.";
                if(welcomed){CoopDirectory.ConfirmHost();MapNightMode.Apply(packet.Lighting??MapLighting.Normal);if(!before)CoopLog.Event("host-agreed",new{});}return;
            }
            if(!CoopDirectory.HostCompatible)return;
            if(!welcomed)return;
            if(packet.Kind==CoopKind.SseState){Runtime.Sessions.Values.FirstOrDefault(s=>s.PropContext(packet))?.ReceiveSseState(packet);return;}
            if(packet.Kind==CoopKind.CircuitState){Runtime.Sessions.Values.FirstOrDefault(s=>s.PropContext(packet))?.ReceivePower(packet);return;}
            if(packet.Kind==CoopKind.BreakerAnimation){Runtime.Sessions.Values.FirstOrDefault(s=>s.PropContext(packet))?.ReceiveBreakerAnimation(packet);return;}
            if(packet.Kind==CoopKind.PropState){Runtime.Sessions.Values.FirstOrDefault(s=>s.PropContext(packet))?.ReceivePropState(packet);return;}
            if(packet.Kind==CoopKind.EncounterClean) {
                var range=Runtime.Sessions.Values.FirstOrDefault(s=>s.House==packet.House && s.Computer && s.Computer.netId==packet.Range);
                if(range!=null && packet.Index>=0 && packet.Index==range.Computer.activeLayoutIndex && packet.Index<range.Computer.shootHouseLayouts.Length && range.LayoutId(packet.Index)==packet.Layout)range.CleanEncounterResidue(packet.Index);
                return;
            }
            if(packet.Kind==CoopKind.EditorPermission){ReceiveEditorPermission(packet);return;}
            if(packet.Kind==CoopKind.ProposalReply){ReceiveProposalReply(packet);return;}
            if(packet.Kind==CoopKind.LayoutChunk) {
                if(committed.TryGetValue(packet.House,out long previous) && packet.Revision<=previous)return;
                if(clientJobs.Count>0)throw new InvalidDataException("Overlapping shared-map preparation.");
                incomingMap??=new LayoutTransfer(packet,Time.unscaledTime);
                var doc=incomingMap.Accept(packet,Time.unscaledTime);
                if(doc!=null) {
                    var destination=Runtime.Sessions.Values.FirstOrDefault(s=>s.House==packet.House && s.Computer.netId==packet.Range)??throw new InvalidDataException("Shared map targets an unavailable range.");
                    destination.InstallShared(packet.Index,doc);incomingMap=null;
                    CoopLog.Event("map-share-received",new{packet.House,packet.Index,packet.Layout,packet.Revision});
                }
                return;
            }
            if(packet.Kind==CoopKind.Prepare) {
                if(committed.TryGetValue(packet.House,out long old) && packet.Revision<=old)return;
                if(clientJobs.Count>0 || incomingMap!=null){Fail("Overlapping or incomplete co-op preparation.");return;}
                var session=Runtime.Sessions.Values.FirstOrDefault(s=>s.House==packet.House && s.Computer.netId==packet.Range);
                if(session==null || packet.Index>=session.Computer.shootHouseLayouts.Length || session.LayoutId(packet.Index)!=packet.Layout || !session.BeginStage(packet.Index)){SendCommand(packet with{Kind=CoopKind.Fault,Sequence=Next(),Detail="Requested range/layout is unavailable."});return;}
                clientJobs[packet.House]=new(){Packet=packet,Session=session,Started=Time.unscaledTime};
                CoopLog.Event("prepare-received",new{packet.House,packet.Revision,packet.Layout});return;
            }
            if(!clientJobs.TryGetValue(packet.House,out var job) || !CoopProtocol.SameSelection(job.Packet,packet))return;
            if(packet.Kind==CoopKind.Commit && job.Ready && !job.Applied) {
                var identity=SelectionLog.Identity(job.Session.Computer,packet.Index,"co-op-commit",job.Session);
                if(!Runtime.ApplyPrepared(job.Session,packet.Index,identity,packet.Masks!,packet.Catchup)){ClientFault(job,"Native client activation failed.");return;}
                MapNightMode.Apply(packet.Lighting??MapLighting.Normal);
                job.Applied=true;job.Started=Time.unscaledTime;job.NextAck=Time.unscaledTime+2;committed[packet.House]=packet.Revision;
                SendCommand(packet with{Kind=CoopKind.Applied,Sequence=Next()});
            } else if(packet.Kind==CoopKind.Release && job.Applied) {
                clientJobs.Remove(packet.House);job.Session.TrimCache(packet.Index);Runtime.Status=$"House {packet.House}: {job.Session.Computer.shootHouseLayouts[packet.Index].LayoutName} ready.";
                CoopLog.Event("client-released",new{packet.House,packet.Revision});
            }
        }catch(Exception error){Fail("Co-op receive failed: "+error.GetBaseException().Message);}
    }
    private static void ClientFault(ClientJob job,string reason) {SendCommand(job.Packet with{Kind=CoopKind.Fault,Sequence=Next(),Detail=reason.Length>230?reason[..230]:reason});Fail(reason);}
    private static void Fail(string reason) {paused=true;failure=reason;CoopLog.Event("synchronization-failed",new{reason});Runtime.SuspendCustom(reason);}
    internal static void NativeFault(RangeSession session,Exception error) {
        if(!NetworkServer.active && context!=0 && hostContext!=0)SendCommand(new(CoopKind.Fault,hostContext,context,Next(),Math.Max(1,committed.GetValueOrDefault(session.House)),session.House,session.Computer.netId,Math.Max(0,session.Computer.activeLayoutIndex),Detail:"Native reset validation failed."));
        Fail("Native range failure: "+error.GetBaseException().Message);
    }
    internal static void Abort(string reason) {
        if(installed && NetworkServer.active && context!=0)foreach(var peer in CoopDirectory.Remotes)try{SendRpc(peer,new(CoopKind.Abort,context,peer.Ad.Session,Next(),Detail:reason.Length>230?reason[..230]:reason,Pause:paused));}catch{}
        CancelEditorRequests(reason);ClearOperations();
    }
    internal static void QueueActiveForJoin() {
        if(!NetworkServer.active)return;
        foreach(var session in Runtime.Sessions.Values) {
            int n=session.Computer.activeLayoutIndex-session.StockCount;
            if(n>=0 && n<session.Items.Count)resumeAfterJoin[session.House]=session.Items[n].Id;
        }
        foreach(var job in hostJobs.Values)if(job.Index>=job.Session.StockCount)resumeAfterJoin[job.Session.House]=job.Session.LayoutId(job.Index);
    }
    private static void ClearOperations(){loadQueue.CancelLoad();hostJobs.Clear();clientJobs.Clear();incomingMap=null;releases.Clear();ResetJoining();}
    internal static void SceneReset(){Abort("Range scene changed.");ResetEditors();resumeAfterJoin.Clear();peerFailures.Clear();hostMenuBlock="";paused=false;failure="";welcomed=false;welcomedPeers.Clear();committed.Clear();contractFirst=contractSecond=null;cachedContracts=null;cachedParts=null;CoopDirectory.Stop();context=hostContext=0;}
    private static void WriteRpc(NetworkWriter writer,RpcMessage packet) {
        NetworkWriterExtensions.WriteUShort(writer,NetworkMessages.GetId<RpcMessage>());NetworkWriterExtensions.WriteVarUInt(writer,packet.netId);writer.WriteByte(packet.componentIndex);
        NetworkWriterExtensions.WriteUShort(writer,packet.functionHash);NetworkWriterExtensions.WriteArraySegmentAndSize(writer,packet.payload);
    }
    private static void VerifyWire() {
        var payload=NetworkWriterPool.Get();var writer=NetworkWriterPool.Get();
        try {
            payload.WriteByte(71);payload.WriteByte(203);WriteRpc(writer,new RpcMessage{netId=0xA5B6C7D8,componentIndex=3,functionHash=(ushort)rpcHash,payload=payload.ToArraySegment()});
            var reader=NetworkReaderPool.Get(writer.ToArraySegment());
            try {
                if(NetworkReaderExtensions.ReadUShort(reader)!=NetworkMessages.GetId<RpcMessage>() || NetworkReaderExtensions.ReadVarUInt(reader)!=0xA5B6C7D8 || reader.ReadByte()!=3 || NetworkReaderExtensions.ReadUShort(reader)!=(ushort)rpcHash)throw new InvalidOperationException("Native co-op wire header mismatch.");
                var bytes=NetworkReaderExtensions.ReadArraySegmentAndSize(reader);var array=bytes.Array;
                if(bytes.Count!=2 || array==null || array[bytes.Offset]!=71 || array[bytes.Offset+1]!=203 || reader.Remaining!=0)throw new InvalidOperationException("Native co-op wire payload mismatch.");
            }finally{reader.Dispose();}
        }finally{writer.Dispose();payload.Dispose();}
    }
    internal static void Dispose() {
        var failures=new List<Exception>();
        void Release(Action action){try{action();}catch(Exception error){failures.Add(error);}}
        // A departing peer can reject the cancellation reply. Still remove
        // both owned Mirror handlers and all range state before reporting it.
        Release(()=>Abort("Killhouse stopped."));installed=false;
        Release(CoopDirectory.Stop);
        Release(()=>{if(Own((ushort)commandHash,command))RemoteProcedureCalls.RemoveDelegate((ushort)commandHash);command=null;});
        Release(()=>{if(Own((ushort)rpcHash,rpc))RemoteProcedureCalls.RemoveDelegate((ushort)rpcHash);rpc=null;});
        // Abort may have failed before reaching its normal queue cleanup.
        Release(ClearOperations);Release(ResetEditors);
        resumeAfterJoin.Clear();peerFailures.Clear();welcomedPeers.Clear();replay.Clear();rates.Clear();committed.Clear();
        contractFirst=contractSecond=null;cachedContracts=null;cachedParts=null;context=hostContext=0;sequence=revision=0;helloAt=0;
        failure=transportFailure=hostMenuBlock=loggedStatus="";paused=welcomed=false;
        // Keep a failed handler's reference so a subsequent Dispose can retry
        // removal; never remove a handler another plugin installed in its place.
        if(failures.Count>0)throw new AggregateException("Killhouse co-op cleanup failed.",failures);
    }
}
