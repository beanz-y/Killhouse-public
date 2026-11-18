using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal sealed partial class NativeShell
{
    private Vector3? FixtureHit(Vector3 from,Vector3 normal,float distance) {
        var ray=new Ray(anchor.TransformPoint(from),anchor.TransformDirection(-normal));
        float nearest=float.PositiveInfinity;Vector3? point=null;
        foreach(var collider in colliders) {
            if(!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || !collider.Raycast(ray,out var hit,distance) || hit.distance>=nearest)continue;
            if(Vector3.Dot(hit.normal,anchor.TransformDirection(normal))<.995f)continue;
            nearest=hit.distance;point=anchor.InverseTransformPoint(hit.point);
        }
        return point;
    }
    internal IEnumerable<PlacedProp> FixtureCandidates(Layout doc,PlacedProp prop,PropShape shape,float reach=FixtureMount.Reach) {
        var(x,z)=PropPlacement.Position(prop);var center=shape.At(x,z,prop.Yaw);
        foreach(float yaw in new[]{0f,90,180,270}) {
            var normal=Quaternion.Euler(0,yaw,0)*Vector3.forward;var origin=shape.Origin(center.X,center.Z,yaw);
            var oriented=prop with{X=origin.X-ConstructionPlan.PropLeft,Z=origin.Z-ConstructionPlan.PropSouth,Yaw=yaw,SupportId=0,SupportPeers=default};
            var back=new Vector3(origin.X,floor+prop.Height+.01f+(shape.MaxY-shape.MinY)/2,origin.Z)+Quaternion.Euler(0,yaw,0)*new Vector3((shape.MinX+shape.MaxX)/2,0,shape.MinZ);
            var hit=FixtureHit(back+normal*reach,normal,reach*2);
            if(hit is not {} face)continue;
            float delta=Vector3.Dot(face-back,normal)+FixtureMount.Gap;
            var next=oriented with{X=oriented.X+normal.x*delta,Z=oriented.Z+normal.z*delta};
            if(FixtureMount.Distance(prop,next)<=reach && FixtureAttached(doc,next,shape))yield return next;
        }
    }
    internal bool FixtureAttached(Layout doc,PlacedProp prop,PropShape shape) {
        var(x,z)=PropPlacement.Position(prop);var rotation=Quaternion.Euler(0,prop.Yaw,0);var normal=rotation*Vector3.forward;var across=rotation*Vector3.right;
        // Only the inward face of a permanent wall can carry an indoor light.
        var inward=new Vector3(ConstructionPlan.Left+doc.Columns-x,0,ConstructionPlan.North-doc.Rows-z);
        if(Vector3.Dot(normal,inward)<=0)return false;
        float height=shape.MaxY-shape.MinY,width=shape.MaxX-shape.MinX;
        var back=new Vector3(x,floor+prop.Height+.01f+height/2,z)+rotation*new Vector3((shape.MinX+shape.MaxX)/2,0,shape.MinZ);
        foreach(var sample in new[]{Vector3.zero,across*(width*.45f)+Vector3.up*(height*.45f),across*(width*.45f)-Vector3.up*(height*.45f),-across*(width*.45f)+Vector3.up*(height*.45f),-across*(width*.45f)-Vector3.up*(height*.45f)}) {
            var at=back+sample;var hit=FixtureHit(at+normal*.1f,normal,.1f+FixtureMount.Gap+FixtureMount.Tolerance);
            if(hit is not {} point || Math.Abs(Vector3.Dot(at-point,normal)-FixtureMount.Gap)>.005f)return false;
        }
        return true;
    }
}
