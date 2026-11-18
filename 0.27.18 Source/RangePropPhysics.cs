using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class RangeSession
{
    private long propEpoch=1,propSequence;
    private float propTickAt,playerAt;
    private Vector3 playerPosition;
    private readonly List<PropPhysics.Item> propCandidates=new();
    private GameObject? ActivePropRoot=>Computer && roots.TryGetValue(Computer.activeLayoutIndex,out var root) && root?root:null;
    // Only the host assigns reset epochs. A guest can run native reset callbacks
    // more often (late join/cached map activation) and must not outrun the host.
    internal void ResetProps() {var root=ActivePropRoot;if(!root)return;if(NetworkServer.active)propEpoch++;PropPhysics.Restore(root!);}
    internal bool PropContext(CoopPacket packet)=>!disposed && Computer && packet.Range==Computer.netId && packet.House==House && packet.Index==Computer.activeLayoutIndex && packet.Layout==LayoutId(packet.Index) && ActivePropRoot;
    internal bool PushProp(PropPhysics.Item item,Vector3 impulse,Vector3 point,bool bump,bool kick=false) {
        var root=ActivePropRoot;if(!root || root!.Pointer!=item.Root.Pointer || !item.Body || !item.Body.gameObject.activeInHierarchy)return false;
        if(NetworkServer.active){item.Body.isKinematic=false;PropPhysics.WakeStack(item);item.Body.AddForceAtPosition(impulse,point,ForceMode.Impulse);item.Sent=-10;}
        else CoopNetwork.PushProp(this,new(item.Id,Pack(anchor.InverseTransformPoint(point)),Pack(impulse),bump,kick),propEpoch);
        return true;
    }
    private static float[] Pack(Vector3 v)=>new[]{v.x,v.y,v.z};
    internal void ReceivePropPush(CoopPacket packet,PlayerNetworking player) {
        if(!NetworkServer.active || !PropContext(packet) || packet.Revision!=propEpoch || packet.Push is not {} push || !player || !player.health || player.health.isDead)return;
        var item=PropPhysics.Find(ActivePropRoot!,push.Id);if(item==null)return;
        var point=anchor.TransformPoint(new(push.Point[0],push.Point[1],push.Point[2]));
        if((point-item.Body.worldCenterOfMass).sqrMagnitude>4 || (push.Bump || push.Kick) && (player.transform.position-item.Body.position).sqrMagnitude>4)return;
        if(push.Bump && Time.unscaledTime<item.PushAt)return;
        if(push.Kick && Time.unscaledTime<item.KickAt)return;
        if(push.Bump)item.PushAt=Time.unscaledTime+.12f;
        if(push.Kick)item.KickAt=Time.unscaledTime+.25f;
        PushProp(item,new(push.Impulse[0],push.Impulse[1],push.Impulse[2]),point,push.Bump,push.Kick);
    }
    internal void ReceivePropState(CoopPacket packet) {
        if(NetworkServer.active || !PropContext(packet) || packet.Revision<propEpoch || packet.Sequence<=propSequence || packet.PropStates==null)return;
        if(packet.Revision>propEpoch)PropPhysics.Restore(ActivePropRoot!);
        propEpoch=packet.Revision;propSequence=packet.Sequence;
        var root=ActivePropRoot!;var rotation=anchor.rotation;float now=Time.unscaledTime;
        foreach(var state in packet.PropStates)if(PropPhysics.Find(root,state.Id) is {} item) {
            var p=state.Pose;item.Body.isKinematic=true;
            item.Playback.Receive(packet.Sequence,now,anchor.TransformPoint(new(p[0],p[1],p[2])),rotation*Quaternion.Normalize(new(p[3],p[4],p[5],p[6])));
        }
    }
    internal void TickProps() {
        var root=ActivePropRoot;if(!root || !PropPhysics.HasRoot(root!))return;
        bool host=NetworkServer.active;float now=Time.unscaledTime;
        if(!host)PropPhysics.TickPlayback(root!,now);
        if(now<propTickAt)return;propTickAt=now+.1f;
        bool send=host && CoopNetwork.PropPhysicsPeers;
        var player=PlayerMaster.MyPlayerMaster?PlayerMaster.MyPlayerMaster!.PlayerSpawnedObject:null;
        bool alive=player && player!.health && !player.health.isDead;
        var playerTransform=alive?player!.transform:null;
        var position=playerTransform?playerTransform!.position:Vector3.zero;
        Vector3 velocity=Vector3.zero;
        if(alive) {
            float dt=now-playerAt;
            if(playerAt>0 && dt is >.02f and <.5f){velocity=(position-playerPosition)/dt;if(velocity.sqrMagnitude>36)velocity=Vector3.zero;}
            playerPosition=position;playerAt=now;
        }else playerAt=0;
        var controller=alive?player!.masterController:null;
        if(controller && controller!.walkerController) {
            var intended=controller.walkerController.GetMovementVelocity();intended.y=0;
            if(float.IsFinite(intended.sqrMagnitude))velocity=Vector3.ClampMagnitude(intended,6);
        }
        var capsule=controller?controller!.characterCollider:null;
        float ground=capsule?capsule!.bounds.min.y:0;Collider? support=null;
        if(controller && controller!.mover && controller.mover.IsGrounded()){ground=controller.mover.GetGroundPoint().y;support=controller.mover.GetGroundCollider();}
        var flat=new Vector3(velocity.x,0,velocity.z);
        bool bump=capsule && capsule!.enabled && flat.sqrMagnitude>.04f;
        var supportBody=support?support!.attachedRigidbody:null;
        propCandidates.Clear();
        foreach(var item in PropPhysics.For(root!)) {
            var body=item.Body;
            if(host) {
                var at=anchor.InverseTransformPoint(body.position);
                if(!float.IsFinite(at.sqrMagnitude) || at.sqrMagnitude>16384 || at.y<floor-5){PropPhysics.Restore(item);continue;}
            } else body.isKinematic=true;
            if(bump && now>=item.PushAt && (position-body.position).sqrMagnitude<4 && (!supportBody || supportBody!.Pointer!=body.Pointer)) {
                foreach(var collider in item.Colliders)if(collider && collider.enabled) {
                    if(!PropContacts.Bump(capsule!,collider,flat,ground,out var contact,playerTransform!))continue;
                    var direction=flat.normalized;float speed=Vector3.Dot(body.linearVelocity,direction);
                    float change=Math.Clamp(Math.Min(1.5f,flat.magnitude)-speed,0,1.25f);
                    if(change<.05f)break;
                    item.PushAt=now+.12f;PushProp(item,direction*Math.Min(4,body.mass*change),contact,true);break;
                }
            }
            if(send) {
                bool sleeping=body.IsSleeping();if(!sleeping)item.SentSleeping=false;
                item.PendingRest=sleeping && !item.SentSleeping;
                if(!sleeping || item.PendingRest || now-item.Sent>=2)propCandidates.Add(item);
            }
        }
        if(propCandidates.Count==0)return;
        propCandidates.Sort((a,b)=>{int rest=b.PendingRest.CompareTo(a.PendingRest);return rest!=0?rest:a.Sent.CompareTo(b.Sent);});
        // Keep the wire cap at 16, but allow four bounded batches so a stack
        // does not lose most of its update rate behind other moving pieces.
        int total=Math.Min(64,propCandidates.Count);
        var inverseRotation=Quaternion.Inverse(anchor.rotation);
        for(int start=0;start<total;start+=16) {
            var states=new PropState[Math.Min(16,total-start)];
            for(int n=0;n<states.Length;n++) {
                var item=propCandidates[start+n];var body=item.Body;
                var local=anchor.InverseTransformPoint(body.position);var q=inverseRotation*body.rotation;
                states[n]=new(item.Id,new[]{local.x,local.y,local.z,q.x,q.y,q.z,q.w});item.Sent=now;item.SentSleeping=body.IsSleeping();
            }
            CoopNetwork.PropStates(this,states,propEpoch);
        }
    }
}
