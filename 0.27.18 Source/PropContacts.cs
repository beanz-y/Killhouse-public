using UnityEngine;

namespace OperatorKillhouse;

// The mover shortens its physical capsule for step clearance. Use its cached
// ground point to include ankles/low boxes, without adding ground raycasts.
internal static class PropContacts
{
    private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<RaycastHit> hits=new(32);
    internal static bool Bump(CapsuleCollider capsule,Collider target,Vector3 velocity,float ground,out Vector3 contact,Transform? actor=null) {
        contact=Vector3.zero;if(!capsule || !capsule.enabled || !target || !target.enabled)return false;
        var body=capsule.bounds;var bounds=target.bounds;
        if(!float.IsFinite(ground) || bounds.max.y<=ground+.02f || bounds.min.y>=body.max.y || body.max.y<=ground+.04f)return false;
        var origin=body.center;origin.y=Math.Clamp(bounds.center.y,ground+.03f,body.max.y-.01f);
        contact=target.ClosestPoint(origin);var direction=contact-origin;direction.y=0;
        float radius=Math.Max(body.extents.x,body.extents.z)+.12f;
        if(direction.sqrMagnitude>radius*radius)return false;
        if(direction.sqrMagnitude<.0001f){direction=bounds.center-body.center;direction.y=0;}
        if(direction.sqrMagnitude>.0001f && Vector3.Dot(velocity,direction)<=0)return false;
        return Clear(capsule,target,origin,contact,actor);
    }
    internal static bool Clear(CapsuleCollider capsule,Collider target,Vector3 origin,Vector3 point,Transform? actor=null) {
        var direction=point-origin;float distance=direction.magnitude;
        if(!float.IsFinite(distance))return false;if(distance<.01f)return true;
        int count=Physics.RaycastNonAlloc(origin,direction/distance,hits,distance+.02f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==hits.Length)return false;
        Collider? nearest=null;float closest=float.PositiveInfinity;
        for(int n=0;n<count;n++) {
            var hit=hits[n];var collider=hit.collider;
            if(!collider || collider.Pointer==capsule.Pointer || collider.transform.IsChildOf(actor?actor!:capsule.transform))continue;
            if(hit.distance<closest){nearest=collider;closest=hit.distance;}
        }
        if(!nearest)return true;
        var targetBody=target.attachedRigidbody;var hitBody=nearest!.attachedRigidbody;
        return nearest.Pointer==target.Pointer || targetBody && hitBody && targetBody.Pointer==hitBody.Pointer;
    }
}
