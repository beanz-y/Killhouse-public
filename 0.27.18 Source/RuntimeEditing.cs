using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;
internal static partial class Runtime
{
    private static void RequestGuestLoad(Layout doc,RangeSession session) {
        try{Status=CoopNetwork.Propose(doc,session.House,session.Computer.netId);}
        catch(Exception error){Status=error.GetBaseException().Message;}
    }
    private static string SaveGuestRevision(Layout doc,bool load,RangeComputer computer,int house) {
        if(!CoopNetwork.GuestCanEdit)throw new InvalidOperationException("The host has not granted map editing permission.");
        var candidate=Catalog.Publish(CatalogState,doc,"local editor revision",Plugin.Strict.Value);
        if(load)LayoutTransfer.Packets(doc,new(CoopKind.ProposalChunk,1,1,1,1,house,computer.netId,0),CoopKind.ProposalChunk);
        string file=NamedMaps.Save(doc,ownFolder,Plugin.Strict.Value);CatalogState=candidate with{Items=candidate.Items.Select(i=>i.Id==doc.Identity?i with{Origin=file}:i).ToArray()};WriteCatalogManifest();
        MapDeletion.Unhide(doc.Name,LibraryData);
        return load?CoopNetwork.Propose(doc,house,computer.netId):"Saved local map: "+doc.Name+". Same-name saves update this map.";
    }
    internal static void LoadApprovedProposal(EditorProposal proposal) {
        if(!CanAcceptGuestMap)throw new InvalidOperationException("Wait for the current map change and all players before approving a load.");
        var key=proposal.Key;var session=Sessions.Values.FirstOrDefault(s=>s.House==key.House && s.Computer && s.Computer.netId==key.Range)??throw new InvalidOperationException("The requested range changed.");
        var doc=proposal.Document();if(doc.Identity!=key.LayoutId)throw new InvalidDataException("The reviewed map identity changed.");
        SaveHostRevision(doc,true,session.Computer,"guest-approved");
    }
    internal static void LoadQueuedMap(QueuedEditorMap queued) {
        if(!CanLoadMaps)throw new InvalidOperationException("Wait for the current map change and players to be ready.");
        if(!queued.Host){LoadApprovedProposal(queued.Proposal);return;}
        var key=queued.Proposal.Key;
        var session=Sessions.Values.FirstOrDefault(s=>s.House==key.House && s.Computer && s.Computer.netId==key.Range)??throw new InvalidOperationException("The queued range changed. Remove this entry and queue the map again.");
        int index=queued.StockIndex;
        if(index>=0) {
            if(index>=session.StockCount || session.LayoutId(index)!=key.LayoutId)throw new InvalidDataException("The queued stock map changed.");
        } else {
            var doc=queued.Proposal.Document();if(doc.Identity!=key.LayoutId)throw new InvalidDataException("The queued map identity changed.");
            int found=session.Items.ToList().FindIndex(i=>i.Id==key.LayoutId);
            index=found>=0?session.StockCount+found:session.AppendAuthored(new(key.LayoutId,"host queue",doc));
        }
        Select(session.Computer,index,"host-queued");
        if(!CoopNetwork.Busy && !pendingSelections.ContainsKey(session.InstanceKey))throw new InvalidOperationException("The queued map did not begin loading. Wait for the range to be ready and retry.");
    }
    private static void DrawEditorPermissions() {
        if(!NetworkServer.active)return;
        GUI.enabled=true;
        if(CoopDirectory.Remotes.Count>0) {
        KillhouseUi.BeginSection("Player permissions");
        try {
        foreach(var peer in CoopDirectory.Remotes) {
            GUI.enabled=CoopNetwork.CanManageEditor(peer);bool allowed=CoopNetwork.HasEditorPermission(peer);
            bool next=GUILayout.Toggle(allowed,CoopNetwork.PlayerName(peer)+" — Edit / request load");
            if(next!=allowed)CoopNetwork.SetEditorPermission(peer,next);
        }
        GUI.enabled=true;KillhouseUi.Note("Granted players may edit and request maps. You confirm every load.");
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
        }
        if(CoopNetwork.LoadRequests.Count>0) {
        KillhouseUi.BeginSection($"Load requests · {CoopNetwork.LoadRequests.Count}");
        try {
        foreach(var proposal in CoopNetwork.LoadRequests) {
            var doc=proposal.Document();GUILayout.Label($"{doc.Name} · House {proposal.Key.House}");KillhouseUi.Note($"From {CoopNetwork.RequestPlayerName(proposal.Key)} · {doc.TargetCount} targets · {doc.Interior.Count(p=>p.Value==Barrier.Door)} doors");
            KillhouseUi.Note(MapDetails(doc));
            GUILayout.BeginHorizontal();try {
            if(GUILayout.Button("Preview")) {
                previewHouse=proposal.Key.House;proposalPreview??=new MapEditor("",(_,_)=>"Preview only");proposalPreview.Open(doc,true);
            }
            if(GUILayout.Button("Decline"))CoopNetwork.DecideLoad(proposal.Key,false);
            }finally{GUILayout.EndHorizontal();}
            GUILayout.BeginHorizontal();try {
            GUI.enabled=CoopNetwork.CanQueue(proposal.Key);if(GUILayout.Button("Approve & queue"))CoopNetwork.QueueLoad(proposal.Key);
            GUI.enabled=CanAcceptGuestMap;if(GUILayout.Button("Approve & load"))CoopNetwork.DecideLoad(proposal.Key,true);
            }finally{GUI.enabled=true;GUILayout.EndHorizontal();}
            GUILayout.Space(8);
        }
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
        }
        GUI.enabled=true;
    }
    private static void DrawQueuedMaps() {
        if(!NetworkServer.active)return;
        KillhouseUi.BeginSection($"Map queue · {CoopNetwork.QueuedMaps.Count} / {EditorLoadQueue.Max}");
        try {
        int position=0;
        foreach(var queued in CoopNetwork.QueuedMaps) {
            var doc=queued.Proposal.Document();bool loading=CoopNetwork.QueueLoading(queued.Proposal.Key);GUILayout.Label($"{++position}. House {queued.Proposal.Key.House}: {doc.Name} — {queued.Player}"+(loading?" · Loading…":""));
            KillhouseUi.Note(MapDetails(doc));
            GUILayout.BeginHorizontal();try {
            if(position>1 && !loading && GUILayout.Button("Move up"))CoopNetwork.MoveQueued(queued.Proposal.Key);
            if(!loading && GUILayout.Button("Remove"))CoopNetwork.RemoveQueued(queued.Proposal.Key);
            }finally{GUILayout.EndHorizontal();}
        }
        if(position==0)KillhouseUi.Note("Use Queue beside a stock or custom map. Approved player requests join this same queue.");
        GUI.enabled=CanLoadMaps && CoopNetwork.QueuedMaps.Count>0;
        if(GUILayout.Button("Load next queued map"))CoopNetwork.LoadNextQueued();
        GUI.enabled=true;KillhouseUi.Note(CanLoadMaps?"Maps load only when you choose. Host maps share the 16-map limit; guest requests keep their four-per-player limit.":"Wait for the current map change and players to be ready.");
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
        GUI.enabled=true;
    }
}
