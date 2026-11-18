using Mirror;
using OperatorKillhouse.Core;
using Steamworks;
using UnityEngine;

namespace OperatorKillhouse;
internal static partial class CoopNetwork
{
    private static readonly EditorAuthority editors=new();
    private static readonly EditorLoadQueue loadQueue=new();
    internal static IReadOnlyList<QueuedEditorMap> QueuedMaps=>loadQueue.Items;
    internal static bool CanQueue(EditorRequestKey key)=>loadQueue.CanAdd(key);
    internal static bool QueueLoading(EditorRequestKey key)=>loadQueue.Loading==key;
    private static long hostQueueNumber;
    internal static bool CanQueueHostMap(RangeSession session,int index)=>NetworkServer.active && session.Computer && index>=0 && index<session.Computer.shootHouseLayouts.Length && session.Listed(index) && loadQueue.CanAddHost(session.House,session.Computer.netId,session.LayoutId(index));
    internal static void QueueHostMap(RangeSession session,int index) {
        if(!CanQueueHostMap(session,index)){Runtime.Status="This map is already queued or the queue is full.";return;}
        bool stock=index<session.StockCount;
        var doc=stock?LayoutDraft.Blank():LayoutDraft.Copy(session.Items[index-session.StockCount].Layout);
        if(stock){doc.Name=session.Computer.shootHouseLayouts[index].LayoutName;doc.Houses=session.House;}
        var key=new EditorRequestKey(default,++hostQueueNumber,session.House,session.Computer.netId,session.LayoutId(index));
        if(!loadQueue.AddHost(new(key,LayoutFormat.Write(doc),double.PositiveInfinity),stock?index:-1)){Runtime.Status="The map could not be queued.";return;}
        Runtime.Status=$"Queued {doc.Name} for House {session.House}.";
        CoopLog.Event("host-map-queued",new{key.House,key.LayoutId,stock,count=loadQueue.Items.Count});
    }
    private static bool editorAllowed;
    private static long permissionRevision,proposalNumber;
    private sealed class OutgoingProposal {
        internal EditorRequestKey Key=null!;
        internal readonly Queue<CoopPacket> Parts=new();
        internal float NextSend,Deadline;
    }
    private static OutgoingProposal? outgoingProposal;
    internal static string EditorNotice {get;private set;}="The host can grant map editing permission in F8.";
    internal static bool GuestCanEdit=>ClientReady && editorAllowed;
    internal static bool GuestCanRequest=>GuestCanEdit && outgoingProposal==null;
    internal static IReadOnlyList<EditorProposal> LoadRequests=>editors.Pending;
    private static EditorActor Actor(CoopParticipant peer)=>new(peer.Steam,peer.Ad.Session,peer.Master.netId);
    internal static string PlayerName(CoopParticipant peer) {
        try{string name=SteamFriends.GetFriendPersonaName(new CSteamID(peer.Steam));return name.Length>0?(name.Length>48?name[..48]:name):$"Player {peer.Master.netId}";}
        catch{return $"Player {peer.Master.netId}";}
    }
    internal static string RequestPlayerName(EditorRequestKey key)=>CoopDirectory.Remotes.FirstOrDefault(p=>Actor(p)==key.Actor) is {} peer?PlayerName(peer):"Disconnected player";
    internal static bool CanManageEditor(CoopParticipant peer)=>NetworkServer.active && Ready && welcomedPeers.TryGetValue(peer.Steam,out ulong nonce) && nonce==peer.Ad.Session;
    internal static bool HasEditorPermission(CoopParticipant peer)=>editors.CanEdit(Actor(peer));
    internal static void SetEditorPermission(CoopParticipant peer,bool allowed) {
        if(!CanManageEditor(peer))return;
        var actor=Actor(peer);editors.Set(actor,allowed);
        if(!allowed)foreach(var key in editors.Cancel(actor))SendProposalReply(key,ProposalDecision.Cancelled,"Map editing permission was revoked. Your draft is preserved.");
        SendEditorPermission(peer);CoopLog.Event("editor-permission",new{player=peer.Master.netId,allowed,revision=editors.Revision});
    }
    private static void SendEditorPermission(CoopParticipant peer)=>SendRpc(peer,new(CoopKind.EditorPermission,context,peer.Ad.Session,Next(),editors.Revision,Allowed:editors.CanEdit(Actor(peer))));
    private static void ReceiveEditorPermission(CoopPacket packet) {
        if(packet.Revision<permissionRevision)return;
        permissionRevision=packet.Revision;editorAllowed=packet.Allowed==true;
        EditorNotice=editorAllowed?"Map editing allowed. Loading a map requires host approval.":"The host has not granted map editing permission.";
        if(!editorAllowed)outgoingProposal=null;
    }
    internal static string Propose(Layout doc,int house,uint range) {
        if(!GuestCanRequest)throw new InvalidOperationException(outgoingProposal!=null?"Wait for the host to decide your current load request.":"The host must grant map editing permission first.");
        var key=new EditorRequestKey(default,++proposalNumber,house,range,doc.Identity);
        var template=new CoopPacket(CoopKind.ProposalChunk,hostContext,context,1,key.Request,house,range,0);
        var job=new OutgoingProposal{Key=key,Deadline=Time.unscaledTime+(float)EditorAuthority.ReviewSeconds+35};
        foreach(var part in LayoutTransfer.Packets(doc,template,CoopKind.ProposalChunk))job.Parts.Enqueue(part);
        outgoingProposal=job;EditorNotice="Sending map to the host for approval…";return EditorNotice;
    }
    private static void TickProposalUpload() {
        var job=outgoingProposal;if(job==null)return;
        if(!GuestCanEdit || Time.unscaledTime>job.Deadline){outgoingProposal=null;EditorNotice="Load request expired or permission changed. Your draft is preserved.";return;}
        if(Time.unscaledTime<job.NextSend)return;job.NextSend=Time.unscaledTime+.25f;
        // 16 parts/second leaves room under the existing 32-command rate limit
        // for acknowledgements and normal co-op control packets.
        for(int n=0;n<4 && job.Parts.Count>0;n++)SendCommand(job.Parts.Dequeue() with{Sequence=Next()});
        if(job.Parts.Count==0)EditorNotice="Waiting for host approval. The active map is unchanged.";
    }
    private static void ReceiveProposal(CoopParticipant peer,CoopPacket packet) {
        var key=new EditorRequestKey(Actor(peer),packet.Revision,packet.House,packet.Range,packet.Layout);
        try {
            if(!Runtime.Sessions.Values.Any(s=>s.House==packet.House && s.Computer && s.Computer.netId==packet.Range))throw new InvalidDataException("Requested range is unavailable.");
            var proposal=editors.Receive(Actor(peer),context,packet,Time.unscaledTime);if(proposal==null)return;
            SendProposalReply(proposal.Key,ProposalDecision.Waiting,"Map received. Waiting for the host to approve loading it.");
            Runtime.Status=$"Load request from {PlayerName(peer)}: {proposal.Document().Name}. Review in F8.";
            CoopLog.Event("load-request-received",new{player=peer.Master.netId,request=packet.Revision,packet.House,packet.Layout});
        }catch(Exception error){SendProposalReply(key,ProposalDecision.Failed,error.GetBaseException().Message);}
    }
    private static void SendProposalReply(EditorRequestKey key,ProposalDecision decision,string message) {
        var peer=CoopDirectory.Remotes.FirstOrDefault(p=>Actor(p)==key.Actor);if(peer==null)return;
        SendRpc(peer,new(CoopKind.ProposalReply,context,peer.Ad.Session,Next(),key.Request,key.House,key.Range,0,key.LayoutId,Detail:message.Length>230?message[..230]:message,Decision:decision));
    }
    private static void ReceiveProposalReply(CoopPacket packet) {
        if(outgoingProposal is not {} job || packet.Revision!=job.Key.Request || packet.House!=job.Key.House || packet.Range!=job.Key.Range || packet.Layout!=job.Key.LayoutId)return;
        EditorNotice=packet.Detail;Runtime.Status=EditorNotice;
        if(packet.Decision==ProposalDecision.Waiting)job.Deadline=Time.unscaledTime+(float)EditorAuthority.ReviewSeconds+5;
        else outgoingProposal=null;
    }
    private static void TickEditorAuthority() {
        var current=CoopDirectory.Remotes.Where(p=>welcomedPeers.TryGetValue(p.Steam,out ulong session) && session==p.Ad.Session).Select(Actor).ToArray();
        foreach(var key in editors.Retain(current,Time.unscaledTime))SendProposalReply(key,ProposalDecision.Cancelled,"Load request expired or the player's session changed.");
    }
    internal static void DecideLoad(EditorRequestKey key,bool approve) {
        if(!NetworkServer.active)return;
        var current=CoopDirectory.Remotes.FirstOrDefault(p=>Actor(p)==key.Actor);
        if(current==null || !CanManageEditor(current))return;
        var proposal=editors.Decide(key,Time.unscaledTime,approve,Runtime.CanAcceptGuestMap);if(proposal==null)return;
        if(!approve){SendProposalReply(key,ProposalDecision.Declined,"The host declined loading this map. Your draft is preserved.");return;}
        try {
            Runtime.LoadApprovedProposal(proposal);
            SendProposalReply(key,ProposalDecision.Approved,"The host approved your map. Applying it through group synchronization…");
            CoopLog.Event("load-request-approved",new{player=current.Master.netId,key.Request,key.House,key.LayoutId});
        }catch(Exception error){Runtime.Status=error.GetBaseException().Message;SendProposalReply(key,ProposalDecision.Failed,Runtime.Status);}
    }
    internal static void QueueLoad(EditorRequestKey key) {
        if(!NetworkServer.active)return;
        var peer=CoopDirectory.Remotes.FirstOrDefault(p=>Actor(p)==key.Actor);if(peer==null || !CanManageEditor(peer))return;
        var proposal=editors.Decide(key,Time.unscaledTime,true,loadQueue.CanAdd(key));if(proposal==null)return;
        if(!loadQueue.Add(proposal,PlayerName(peer)))return;
        SendProposalReply(key,ProposalDecision.Queued,"The host approved and queued your map. You may submit another. The host chooses when to load it.");
        CoopLog.Event("load-request-queued",new{key.Request,key.House,key.LayoutId,count=loadQueue.Items.Count});
    }
    internal static void MoveQueued(EditorRequestKey key){if(NetworkServer.active)loadQueue.MoveUp(key);}
    internal static void RemoveQueued(EditorRequestKey key){if(NetworkServer.active)loadQueue.Remove(key);}
    internal static void LoadNextQueued() {
        if(!NetworkServer.active || !Runtime.CanLoadMaps || loadQueue.Begin() is not {} next)return;
        try{Runtime.LoadQueuedMap(next);CoopLog.Event("queued-map-started",new{next.Player,next.Proposal.Key.LayoutId});}
        catch(Exception error){loadQueue.CancelLoad();Runtime.Status=error.GetBaseException().Message;}
    }
    internal static void FinishQueuedLoad(int house,string layoutId,bool success){if(!NetworkServer.active)return;loadQueue.Complete(house,layoutId,success);}
    private static void CancelEditorRequests(string reason) {
        foreach(var actor in CoopDirectory.Remotes.Select(Actor).ToArray())foreach(var key in editors.Cancel(actor))if(NetworkServer.active)SendProposalReply(key,ProposalDecision.Cancelled,reason);
        outgoingProposal=null;
    }
    private static void ResetEditors(){editors.Clear();loadQueue.Clear();hostQueueNumber=0;editorAllowed=false;permissionRevision=proposalNumber=0;outgoingProposal=null;EditorNotice="The host can grant map editing permission in F8.";}
}
