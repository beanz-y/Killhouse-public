using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class RangeSession
{
    private readonly SseRun sse=new();
    private int sseRoot;
    private float sseSendAt;
    private readonly HashSet<int> sseNotified=new();
    private long sseNoticeEpoch;
    internal bool OwnsSse(SseObjects.Item item)=>ActivePropRoot && ActivePropRoot!.Pointer==item.Root.Pointer;
    internal string SseStatus {
        get {
            var doc=ActiveDocument;if(doc==null || doc.Sse.Count==0)return "";
            if(!sse.Ready)return "SSE: waiting for host state…";
            int required=doc.Sse.Count(s=>s.Required),done=doc.Sse.Count(s=>s.Required && sse.Collected.Contains(s.PropId));
            return $"SSE: {done}/{required} required · {sse.Collected.Count}/{doc.Sse.Count} collected";
        }
    }
    internal void ResetSse() {
        var root=ActivePropRoot;var doc=ActiveDocument;if(!root || doc==null)return;
        if(NetworkServer.active || sseRoot!=root!.GetInstanceID()){sse.Bind(doc,NetworkServer.active);sseRoot=root!.GetInstanceID();sseNotified.Clear();sseNoticeEpoch=sse.Epoch;}
        if(sse.Ready)SseObjects.Apply(root!,sse.Collected);else SseObjects.Pending(root!);sseSendAt=0;
    }
    internal void TickSse() {
        var doc=ActiveDocument;if(disposed || doc==null || doc.Sse.Count==0){sseRoot=0;return;}
        var root=ActivePropRoot;
        if(!root || !root!.activeInHierarchy){sseRoot=0;return;}
        if(sseRoot!=root.GetInstanceID())ResetSse();
        if(!NetworkServer.active || Time.unscaledTime<sseSendAt)return;
        sseSendAt=Time.unscaledTime+1;CoopNetwork.SendSseState(this,sse.Epoch,sse.Collected.OrderBy(id=>id).ToArray());
    }
    internal bool UseSse(int id,PlayerNetworking player,long epoch=0) {
        var root=ActivePropRoot;var doc=ActiveDocument;
        if(disposed || !root || !root!.activeInHierarchy || doc==null || CoopNetwork.Switching || !Plugin.Enabled.Value || !player || !player.health || player.health.isDead || !sse.Ready || sseRoot!=root.GetInstanceID() || sse.Collected.Contains(id))return false;
        var item=SseObjects.Find(root,id);if(item==null || !CanReachSse(player,item))return false;
        if(!NetworkServer.active){CoopNetwork.RequestSse(this,sse.Epoch,id);return true;}
        if(!sse.Collect(id,epoch==0?sse.Epoch:epoch))return false;
        SseObjects.Apply(root,sse.Collected);sseSendAt=Time.unscaledTime+1;
        // A live acquisition travels with the complete authoritative state.
        // Periodic/late-join snapshots carry no notice and never replay history.
        CoopNetwork.SendSseState(this,sse.Epoch,sse.Collected.OrderBy(n=>n).ToArray(),id);
        NotifySse(id);
        Runtime.Status="Collected SSE: "+doc.Sse.Single(s=>s.PropId==id).Label+". "+SseStatus;
        CoopLog.Event("sse-collected",new{House,id,sse.Epoch,player=player.netId});
        SseCollected(id);return true;
    }
    private static bool CanReachSse(PlayerNetworking player,SseObjects.Item item) {
        if(!item.Node || !item.Node.activeInHierarchy || !item.Hit)return false;
        var eye=player.transform.position+Vector3.up*1.55f;var delta=item.Hit.bounds.center-eye;
        if(delta.sqrMagnitude>2.75f*2.75f || delta.sqrMagnitude<.0001f)return false;
        foreach(var hit in Physics.RaycastAll(eye,delta.normalized,delta.magnitude+.1f,-1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)) {
            if(!hit.collider || hit.collider.transform.IsChildOf(player.transform))continue;
            return hit.collider.transform.IsChildOf(item.Node.transform);
        }
        return false;
    }
    internal void ReceiveSseCollect(CoopPacket packet,PlayerNetworking player){if(NetworkServer.active && PropContext(packet) && packet.Revision==sse.Epoch && packet.SseId is {} id)UseSse(id,player,packet.Revision);}
    internal void ReceiveSseState(CoopPacket packet) {
        if(NetworkServer.active || !PropContext(packet) || packet.SseCollected==null)return;
        var root=ActivePropRoot!;if(sseRoot!=root.GetInstanceID())ResetSse();
        if(sse.Receive(packet.Revision,packet.Sequence,packet.SseCollected)) {
            SseObjects.Apply(root,sse.Collected);
            if(sseNoticeEpoch!=sse.Epoch){sseNotified.Clear();sseNoticeEpoch=sse.Epoch;}
            if(packet.SseId is {} id && sse.Collected.Contains(id))NotifySse(id);
        }
    }
    private void NotifySse(int id) {
        var objective=ActiveDocument?.Sse.SingleOrDefault(s=>s.PropId==id);
        if(objective==null || !sseNotified.Add(id))return;
        // Same popup, color and wording as ObjectiveObject.OnObjectPickup.
        // Valid separate collections must not be discarded by the spam timer.
        try {var hints=HintManager.singleton;if(hints)hints.SpawnPopup(objective.Label+" Acquired",Popup.PopupType.YellowWarning,false,false);}
        catch(Exception error){Plugin.Logger.LogError(error);}
    }
    private void SseCollected(int id) {
        if(!NetworkServer.active || triggerRun==null || triggerDocument==null || disposed || CoopNetwork.Switching)return;
        var root=ActivePropRoot;if(!root || !root!.activeInHierarchy || root.GetInstanceID()!=triggerRoot)return;
        try {foreach(var trigger in triggerRun.SseCollected(triggerDocument.Triggers,id,Time.unscaledTime)) {
            foreach(var action in trigger.Actions)RunTriggerAction(action);
            TriggerStatus="Fired: "+trigger.Name+" · SSE collected";CoopLog.Event("trigger-fired",new{House,trigger.Id,sse=id});
        }}catch(Exception error){DisarmTriggers();Runtime.Status="SSE trigger stopped: "+error.GetBaseException().Message;Plugin.Logger.LogError(error);}
    }
}
internal static partial class Runtime
{
    internal static void UseNativeSse(SseObjects.Item item,PlayerNetworking player) {
        if(GameplayInputBlocked || !Plugin.Enabled.Value || CoopNetwork.Switching || !player || !player.isOwned)return;
        foreach(var session in Sessions.Values)if(session.OwnsSse(item)){session.UseSse(item.Id,player);return;}
    }
}
