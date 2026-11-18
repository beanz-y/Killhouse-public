using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

// Host-owned encounter doors only. No scene searches, timers, or per-poll
// arrays: static doors do not issue physics queries.
internal sealed class EncounterDoorAwareness
{
    private sealed class Motion {
        internal readonly DoorV2 Door;
        internal Quaternion Rotation;
        internal Motion(DoorV2 door){Door=door;Rotation=door.PivotTransform.rotation;}
    }
    private readonly List<Motion> doors=new();
    private readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<RaycastHit> hits=new(32);
    private float nextPoll;
    internal void Bind(IEnumerable<DoorV2> source) {
        Clear();
        foreach(var door in source)if(door && door.PivotTransform && !doors.Any(d=>d.Door.Pointer==door.Pointer))doors.Add(new(door));
        nextPoll=Time.time+.1f;
    }
    internal void Clear(){doors.Clear();nextPoll=0;}
    internal void Tick(IEnumerable<BrainAI> brains,EncounterCaution caution,EncounterOrders orders,MapLighting lighting) {
        if(Time.time<nextPoll)return;nextPoll=Time.time+.1f;
        foreach(var motion in doors) {
            var door=motion.Door;if(!door || !door.PivotTransform)continue;
            var rotation=door.PivotTransform.rotation;
            if(Quaternion.Angle(motion.Rotation,rotation)<2)continue;
            motion.Rotation=rotation;
            if(door.isDead || !door.DoorHitBox || !door.DoorHitBox.enabled)continue;
            var point=door.DoorHitBox.bounds.center;
            foreach(var brain in brains) {
                if(!caution.Holding(brain) || !Visible(brain,door,point,lighting))continue;
                if(!caution.ObserveVision(brain,orders,true,true,"door"))continue;
                // Use native position investigation without inventing a seen
                // player or passing a door to the weapon/target pipeline.
                brain.LastHeardTargetPosition=point;
                brain.HeardTarget(point,Vector3.Distance(brain.eyesAI._eyesTransform.position,point),null);
                brain.respondtimer=0;brain.responding=true;
                CoopLog.Event("ai-cautious-door",new{bot=brain.netId,door=door.netId});
            }
        }
    }
    private bool Visible(BrainAI brain,DoorV2 door,Vector3 point,MapLighting lighting) {
        var eyes=brain.eyesAI;if(!eyes || !eyes._eyesTransform || brain.isFlashed)return false;
        var origin=eyes._eyesTransform.position;var direction=point-origin;float distance=direction.magnitude;
        float limit=Math.Min(10,brain.DetectionRange);
        if(!brain.HasNVG && lighting.Enabled)limit=Math.Min(limit,AiLighting.SightLimit(lighting.Preset));
        if(!float.IsFinite(distance) || !float.IsFinite(limit) || distance<.01f || distance>limit || !float.IsFinite(brain.EyesFOVAngle) || brain.EyesFOVAngle<=0 || eyes.EyesForward.sqrMagnitude<.01f || Vector3.Angle(eyes.EyesForward,direction)>brain.EyesFOVAngle*.5f)return false;
        int count=Physics.RaycastNonAlloc(origin,direction/distance,hits,distance+.05f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==hits.Length)return false; // Conservative when a ray overflows.
        Collider? nearest=null;float closest=float.PositiveInfinity;
        for(int n=0;n<count;n++) {
            var hit=hits[n];var collider=hit.collider;
            if(!collider || collider.transform.IsChildOf(brain.transform))continue;
            if(hit.distance<closest){nearest=collider;closest=hit.distance;}
        }
        return nearest && (nearest!.Pointer==door.DoorHitBox.Pointer || nearest.transform.IsChildOf(door.PivotTransform));
    }
}
