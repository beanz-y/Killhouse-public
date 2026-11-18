using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;

// Templates come exclusively from the running game's own stock layouts. No
// meshes, textures, prefab exports or discontinued-mod assets ship in our DLL.
internal sealed class NativePiece
{
    internal Transform Template { get; }
    internal AssetCalibration Calibration { get; }
    internal Vector3? ApertureCenter { get; }
    internal NativePiece(Transform template)
    {
        Template=template;
        if(template.GetComponentInChildren<NetworkIdentity>(true)) throw new InvalidOperationException("Static range geometry unexpectedly has network identity.");
        var points=new List<Vector3>();var apertures=new List<Vector3>();
        foreach(var mesh in template.GetComponentsInChildren<MeshFilter>(true)) {
            // Concrete extends beyond the wood faces in places. Use the authored
            // panel span and floor, while cloning the complete reinforced asset.
            if(!mesh.sharedMesh || !mesh.name.StartsWith("Wall2Meter",StringComparison.Ordinal))continue;
            var bounds=mesh.sharedMesh.bounds;
            var relative=NativeSpace.Relative(mesh.transform,template);
            if(mesh.name.StartsWith("Wall2MeterDoor",StringComparison.Ordinal)) {
                if(mesh.sharedMesh.name!="Wall2MeterDoor" || Vector3.Distance(bounds.center,new(0,1.5f,1))>.001f || Vector3.Distance(bounds.extents,new(.10015808f,1.5f,1))>.001f)
                    throw new InvalidOperationException("Native doorway mesh needs a new opening measurement.");
                var(x,y,z)=DoorwayProfile.Center;apertures.Add(relative.MultiplyPoint3x4(new(x,y,z)));
            }
            for(int bits=0;bits<8;bits++) {
                var sign=new Vector3((bits&1)==0?-1:1,(bits&2)==0?-1:1,(bits&4)==0?-1:1);
                points.Add(relative.MultiplyPoint3x4(bounds.center+Vector3.Scale(bounds.extents,sign)));
            }
        }
        if(points.Count==0)throw new InvalidOperationException("Native wall panel measurements are unavailable.");
        Calibration=new(points.Min(p=>p.x),points.Max(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.y),points.Min(p=>p.z),points.Max(p=>p.z));
        Calibration.Validate();
        if(apertures.Count>0) {
            if(apertures.Count!=2)throw new InvalidOperationException("Native doorway must have two measured panel faces.");
            ApertureCenter=(apertures[0]+apertures[1])*.5f;
        }
    }
    internal Vector3 StockFoot {
        get {var(x,y,z)=Calibration.Foot;return Template.TransformPoint(new Vector3(x,y,z));}
    }
    internal Transform Place(Transform parent,Transform anchor,BarrierSegment segment,float floor)
    {
        var clone=NativeDamageResources.Clone(Template.gameObject,parent);
        clone.SetActive(false);
        clone.name=Template.name+" [Operator Killhouse]";
        var transform=clone.transform;
        // Only solid walls fit their span to adjacent faces. Thickness/height,
        // doorway apertures and borrowed door leaves keep their native scale.
        var(sx,sy,sz)=Calibration.Scale(segment);
        transform.localScale=new Vector3(sx,sy,sz);
        transform.rotation=anchor.rotation*Quaternion.Euler(0,Calibration.Yaw(segment.Edge.Horizontal),0);
        var(x,y,z)=Calibration.Foot;
        if(ApertureCenter is {} aperture)(x,y,z)=DoorwayAssembly.Foot(Calibration,aperture.x,aperture.z);
        transform.position=anchor.TransformPoint(new Vector3(segment.X,floor+segment.Bottom,segment.Z))-transform.TransformVector(new Vector3(x,y,z));
        foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true)) {
            // Keep native shared materials, textures, Surface/Armour components,
            // LODs and colliders. Baked lighting belongs to the source position.
            renderer.lightmapIndex=-1;renderer.realtimeLightmapIndex=-1;
            // These owned reinforced wall/frame meshes never move during a run.
            // Borrowed door leaves, targets and physics furniture remain dynamic.
            renderer.staticShadowCaster=true;
        }
        clone.SetActive(true);
        return transform;
    }
}

internal sealed record ShellProbeHit(WallEnd End,float Coordinate,float Offset);
internal sealed partial class NativeShell
{
    private readonly Collider[] colliders;
    private readonly Transform anchor;
    private readonly float floor;
    internal NativeShell(Transform anchor,float floor) {
        this.anchor=anchor;this.floor=floor;
        // The permanent shell is separate from inactive stock layouts and props.
        var shell=anchor.parent?anchor.parent.Find("Killhouse Mesh"):null;
        colliders=shell?shell!.GetComponentsInChildren<Collider>(true).Where(c=>c && !c.isTrigger).ToArray():Array.Empty<Collider>();
        if(colliders.Length==0)throw new InvalidOperationException("Permanent house shell colliders are unavailable for wall fitting.");
    }
    internal Dictionary<WallEnd,float> Contacts(Layout doc,float wallThickness,float frameThickness,out ShellProbeHit[] probeHits) {
        var result=new Dictionary<WallEnd,float>();var hits=new List<ShellProbeHit>();
        foreach(var segment in ConstructionPlan.Make(doc).Segments)foreach(bool high in new[]{false,true}) {
            var e=segment.Edge;
            if(BoundaryWalls.BlocksEnd(doc,segment,high))continue;
            if(!WallJunctions.Perimeter(doc,new(e,high)))continue;
            float nominal=(e.Horizontal?segment.X:segment.Z)+(high?1:-1);
            var outward=e.Horizontal?Vector3.right:Vector3.forward;if(!high)outward=-outward;
            var endpoint=new Vector3(segment.X,floor+1.5f,segment.Z)+(e.Horizontal?Vector3.right:Vector3.forward)*(high?1:-1);
            var across=e.Horizontal?Vector3.forward:Vector3.right;
            var contact=ShellProbes.Find(segment.Kind is Barrier.Wall or Barrier.Window?wallThickness:frameThickness,
                offset=>Cast(endpoint+across*offset,outward,nominal,e.Horizontal));
            if(contact.HasValue) {
                result.Add(new(e,high),contact.Value.Coordinate);
                hits.Add(new(new(e,high),contact.Value.Coordinate,contact.Value.Offset));
            }
        }
        probeHits=hits.ToArray();
        return result;
    }
    private float? Cast(Vector3 endpoint,Vector3 outward,float nominal,bool horizontal) {
        var ray=new Ray(anchor.TransformPoint(endpoint-outward*1.5f),anchor.TransformDirection(outward));
        float distance=float.PositiveInfinity,face=0;
        foreach(var collider in colliders) {
            if(!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || !collider.Raycast(ray,out var hit,3))continue;
            var point=anchor.InverseTransformPoint(hit.point);
            float coordinate=horizontal?point.x:point.z;
            if(Vector3.Dot(hit.normal,ray.direction)>-.9f || Math.Abs(coordinate-nominal)>WallJunctions.MaxShellOffset || hit.distance>=distance)continue;
            distance=hit.distance;face=coordinate;
        }
        return float.IsFinite(distance)?face:null;
    }
}
