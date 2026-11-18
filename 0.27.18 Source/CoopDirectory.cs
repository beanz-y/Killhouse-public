using Mirror;
using OperatorKillhouse.Core;
using Steamworks;
using UnityEngine;

namespace OperatorKillhouse;
internal sealed record CoopParticipant(ulong Steam,CoopAd Ad,PlayerMaster Master);
internal static class CoopDirectory
{
    private static readonly Dictionary<ulong,CoopLease> leases=new();
    private static readonly Dictionary<ulong,string> advertisementBlocks=new();
    private static readonly HashSet<ulong> rejectedAdvertisements=new();
    private static readonly Dictionary<int,JoinAdmission> joining=new();
    private static readonly Dictionary<ulong,int> playerConnections=new();
    internal static bool CheckingJoin {get;private set;}
    internal static bool WithinJoinWindow(ulong steam)=>playerConnections.TryGetValue(steam,out int connection) && joining.TryGetValue(connection,out var admission) && admission.WithinWindow(Time.unscaledTime);
    private static CSteamID lobby;
    private static ulong self,host;
    private static uint player,revision;
    private static string context="";
    private static float nextPoll,nextPublish;
    private static bool published;
    internal static ulong Session {get;private set;}
    internal static ulong HostSession {get;private set;}
    internal static bool HostCompatible {get;private set;}
    internal static bool HostKnown {get;private set;}
    internal static string Reason {get;private set;}="Waiting for a Steam lobby.";
    internal static IReadOnlyList<CoopParticipant> Remotes {get;private set;}=Array.Empty<CoopParticipant>();
    internal static bool AllRemotesMatching=>HostCompatible && Remotes.Count<=32 && Runtime.RemoteCount()==Remotes.Count;
    internal static void Tick() {
        if(!Plugin.Coop.Value || !Plugin.Enabled.Value || !NetworkClient.active || !NetworkClient.isConnected || !SteamManager.InitializedSafe){Stop();return;}
        var manager=NetworkManagerVectorGames.instance;var steam=VectorGames.SteamLobby.singleton;var local=PlayerMaster.MyPlayerMaster;
        if(!manager || !manager.Steam || manager.transport!=manager.Steam || !steam || !steam.lobbyID.IsLobby() || !local || !local.isOwned || local.netId==0){Stop();return;}
        string next=EditorSessionPolicy.LobbyContext(steam.lobbyID.m_SteamID,local.netId,NetworkServer.active,Plugin.Furniture.Value);
        if(next!=context){Stop();context=next;lobby=steam.lobbyID;self=SteamUser.GetSteamID().m_SteamID;player=local.netId;Session=BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0)|1UL;}
        float now=Time.unscaledTime;
        if(!published || now>=nextPublish){nextPublish=now+3;var ownAd=new CoopAd(Session,++revision,player,NetworkServer.active,Runtime.CatalogState.Manifest,Plugin.Furniture.Value);SteamMatchmaking.SetLobbyMemberData(lobby,CoopAd.Key,ownAd.Encode());if(!leases.TryGetValue(self,out var ownLease))leases[self]=ownLease=new();ownLease.Accept(ownAd,now);published=true;}
        if(now>=nextPoll) {
        nextPoll=now+.25f;
        var members=new HashSet<ulong>();int count=Math.Clamp(SteamMatchmaking.GetNumLobbyMembers(lobby),0,64);
        for(int n=0;n<count;n++) {
            var member=SteamMatchmaking.GetLobbyMemberByIndex(lobby,n);ulong id=member.m_SteamID;members.Add(id);
            if(id==self)continue;
            string raw=SteamMatchmaking.GetLobbyMemberData(lobby,member,CoopAd.Key)??"";var ad=CoopAd.Parse(raw);
            string? block=CoopAdvertisementNotice.Block(raw,Plugin.Furniture.Value);
            if(block==null)advertisementBlocks.Remove(id);else advertisementBlocks[id]=block;
            if(raw.Length>0 && block!=null)rejectedAdvertisements.Add(id);else rejectedAdvertisements.Remove(id);
            if(ad==null){if(raw.Length>0)leases.Remove(id);continue;}
            if(!leases.TryGetValue(id,out var lease))leases[id]=lease=new();lease.Accept(ad,now);
        }
        foreach(ulong id in leases.Keys.Where(id=>id!=self && !members.Contains(id) && !Compatible(id,out _,false)).ToArray())leases.Remove(id);
        foreach(ulong id in advertisementBlocks.Keys.Where(id=>!members.Contains(id)).ToArray())advertisementBlocks.Remove(id);
        rejectedAdvertisements.RemoveWhere(id=>!members.Contains(id));
        }
        if(NetworkServer.active)host=self;
        else if(ulong.TryParse(SteamMatchmaking.GetLobbyData(lobby,VectorGames.SteamLobby.HostAddressKey),out ulong value) && value!=0)host=value;
        else if(!Compatible(host,out _,false))host=0;
        HostKnown=Compatible(host,out var server,false) && server!.Ad.Host && Compatible(self,out var own,false) && own!.Ad.Session==Session;
        HostCompatible=HostKnown && leases[host].Fresh(now) && leases[self].Fresh(now);
        HostSession=HostKnown?server!.Ad.Session:0;
        CheckingJoin=false;
        if(!HostCompatible){Reason=HostKnown?"Waiting for a fresh Killhouse response from the host.":"Waiting for the host's Killhouse "+CoopProtocol.Build+" advertisement, player identity and matching furniture settings.";Remotes=Array.Empty<CoopParticipant>();return;}
        var remotes=new List<CoopParticipant>();
        string? waiting=null;
        bool pending=false,rejected=false;var connections=new HashSet<int>();playerConnections.Clear();
        if(NetworkServer.active)foreach(var pair in NetworkServer.connections) {
            var connection=pair.Value;
            if(connection==null || NetworkServer.localConnection!=null && connection.Pointer==NetworkServer.localConnection.Pointer)continue;
            int key=connection.connectionId;connections.Add(key);if(!joining.TryGetValue(key,out var admission))joining[key]=admission=new();
            var identity=connection.identity;var master=identity?identity!.GetComponent<PlayerMaster>():null;
            if(!connection.isAuthenticated || !connection.isReady || !master){var verdict=admission.Observe(false,false,false,now);pending|=verdict==JoinVerdict.Pending;rejected|=verdict==JoinVerdict.Rejected;waiting??="Waiting for a joining player's game connection and player object.";continue;}
            playerConnections[master!.thisPlayerSteam64]=key;
            bool matches=Compatible(master.thisPlayerSteam64,out var peer) && peer!.Master.netId==master.netId;
            var decision=admission.Observe(true,rejectedAdvertisements.Contains(master.thisPlayerSteam64),matches,now);
            pending|=decision==JoinVerdict.Pending;rejected|=decision==JoinVerdict.Rejected;
            if(!matches){waiting??=$"Waiting for player {master.netId}: "+Missing(master.thisPlayerSteam64);continue;}
            remotes.Add(peer!);
        }
        Remotes=remotes;
        foreach(int key in joining.Keys.Where(key=>!connections.Contains(key)).ToArray())joining.Remove(key);
        CheckingJoin=pending && !rejected && connections.Count<=32;
        Reason=waiting??"Matching co-op participants found.";
    }
    private static string Missing(ulong steam) {
        if(advertisementBlocks.TryGetValue(steam,out string? block))return block;
        if(!leases.TryGetValue(steam,out var lease) || lease.Ad==null)return CoopAdvertisementNotice.Block(null,Plugin.Furniture.Value)!;
        if(!lease.Ad.Matches(Runtime.CatalogState.Manifest,Plugin.Furniture.Value))return "has a different Furniture setting. Match Killhouse > Furniture in Mod Controls, then re-enter the range.";
        if(!lease.Fresh(Time.unscaledTime))return "has stopped responding to Killhouse checks. Wait for reconnection, or rejoin the range if this persists.";
        return "has not finished loading their player in this scene. Wait for them to join the range, then try again.";
    }
    internal static string MenuBlockReason {
        get {
            if(context.Length==0)return "Co-op requires a connected Steam lobby and a loaded player. Finish joining an Outdoors range using Steam, then try again.";
            if(!HostCompatible)return host==0?"Waiting for the lobby host's player identity. Finish joining the range, then try again.":PlayerLabel(host,"The host")+" "+Missing(host);
            if(Remotes.Count>32)return "Killhouse supports up to 32 remote players. Reduce the lobby size to use custom ranges.";
            if(NetworkServer.active)foreach(var pair in NetworkServer.connections) {
                var connection=pair.Value;
                if(connection==null || NetworkServer.localConnection!=null && connection.Pointer==NetworkServer.localConnection.Pointer)continue;
                var identity=connection.identity;var master=identity?identity!.GetComponent<PlayerMaster>():null;
                if(!connection.isAuthenticated || !connection.isReady || !master)return "A player is still joining. Killhouse will become available when their game connection and mod checks finish.";
                if(!Compatible(master!.thisPlayerSteam64,out var peer) || peer!.Master.netId!=master.netId)return PlayerLabel(master.thisPlayerSteam64,"Player "+master.netId)+" "+Missing(master.thisPlayerSteam64);
            }
            return Reason;
        }
    }
    internal static string PlayerLabel(ulong steam,string fallback="A player") {
        if(steam==0 || !SteamManager.InitializedSafe)return fallback;
        try {
            string name=MenuNotice.Plain(SteamFriends.GetFriendPersonaName(new CSteamID(steam)),48).Trim();
            return name.Length==0?fallback:fallback+" ("+name+")";
        }catch{return fallback;}
    }
    private static bool Compatible(ulong steam,out CoopParticipant? peer,bool fresh=true) {
        peer=null;if(steam==0 || !leases.TryGetValue(steam,out var lease) || fresh && !lease.Fresh(Time.unscaledTime) || lease.Ad is not {} ad || !ad.Matches(Runtime.CatalogState.Manifest,Plugin.Furniture.Value))return false;
        var spawned=NetworkServer.active?NetworkServer.spawned:NetworkClient.spawned;
        if(!spawned.TryGetValue(ad.Player,out var identity) || !identity)return false;
        var master=identity.GetComponent<PlayerMaster>();
        if(!master || master.netId!=ad.Player || master.thisPlayerSteam64!=steam)return false;
        peer=new(steam,ad,master);return true;
    }
    internal static CoopParticipant? Peer(PlayerMaster master,bool discovery=false) {
        if(!master || !Compatible(master.thisPlayerSteam64,out var peer,!discovery) || peer!.Master.Pointer!=master.Pointer)return null;return peer;
    }
    internal static void Confirm(CoopParticipant peer){if(leases.TryGetValue(peer.Steam,out var lease))lease.Confirm(peer.Ad.Session,peer.Master.netId,Time.unscaledTime);}
    internal static void ConfirmHost(){if(HostKnown && leases.TryGetValue(host,out var lease) && lease.Ad is {} ad)lease.Confirm(ad.Session,ad.Player,Time.unscaledTime);}
    internal static Dictionary<ulong,ulong> Participants()=>Remotes.ToDictionary(p=>p.Steam,p=>p.Ad.Session);
    internal static void Stop() {
        var old=lobby;bool clear=published;lobby=default;self=host=Session=HostSession=0;player=revision=0;context="";published=HostKnown=HostCompatible=false;nextPoll=nextPublish=0;
        leases.Clear();advertisementBlocks.Clear();rejectedAdvertisements.Clear();joining.Clear();playerConnections.Clear();CheckingJoin=false;Remotes=Array.Empty<CoopParticipant>();Reason="Waiting for a compatible Steam lobby.";
        if(clear && old.m_SteamID!=0 && SteamManager.InitializedSafe)try{SteamMatchmaking.SetLobbyMemberData(old,CoopAd.Key,"");}catch{}
    }
}
