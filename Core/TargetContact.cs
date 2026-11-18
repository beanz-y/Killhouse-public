using System.Numerics;

namespace OperatorKillhouse.Core;

public sealed record TargetSurfaceFace(SolidBox Solid,int Axis,int Sign,string Surface,int PropId=0)
{
    public Vector3 Normal=>Solid.Axes[Axis]*Sign;
    public float Plane=>Vector3.Dot(Solid.Center,Normal)+(Axis==0?Solid.Half.X:Solid.Half.Z);
}
public sealed record TargetSurfaceSnap(Target Target,string Surface,float Distance,TargetSurfaceFace? Face=null);
public static class TargetContact
{
    public const float Gap=.001f,Reach=1,WallTolerance=.004f,PropTolerance=.002f;
    public static Target Shift(Target target,float x,float z,float? yaw=null) {
        var(px,pz)=TargetPlacement.Position(target);
        return TargetPlacement.At(px+x-ConstructionPlan.Left,ConstructionPlan.North-pz-z,target.Role,yaw??target.Yaw??(target.Facing is {} facing?90*(int)facing:null),target.Always,target.Id,target.Style,target.Height,target.Pitch,target.Roll);
    }
    private sealed record SurfaceSolid(SolidBox Box,string Label,PlacedProp? Prop,PropShape? Shape);
    private static List<SurfaceSolid> Solids(Layout doc,IReadOnlyList<BarrierSegment> architecture,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<PlacedProp> props) {
        var solids=new List<SurfaceSolid>();
        foreach(var wall in PlacementClearance.WithShell(doc,architecture).Where(s=>s.Kind==Barrier.Wall))solids.Add(new(WindowGeometry.Volume(wall),wall.WindowPart?"window frame":"wall",null,null));
        foreach(var prop in props.Where(p=>p.Id>0))if(PropPlacement.Shape(prop,shapes) is {} shape)solids.Add(new(TargetGeometry.PropVolume(prop,shape),prop.Name,prop,shape));
        return solids;
    }
    public static TargetSurfaceSnap? Side(Layout doc,Target target,IReadOnlyList<BarrierSegment> architecture,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<PlacedProp> props,bool matchFacing=false,int rotationStep=45) {
        if(target.Style!=TargetStyle.Standless)return null;
        var solids=Solids(doc,architecture,shapes,props);
        var candidates=new List<(TargetSurfaceSnap Snap,bool Inside,float Facing)>();
        foreach(var(solid,label,prop,_) in solids)foreach(int axis in new[]{0,2})foreach(int sign in new[]{-1,1}) {
            var basis=solid.Axes;var normal=basis[axis]*sign;var tangent=basis[axis==0?2:0];
            float yaw=matchFacing?TargetEditSteps.SnapAngle(MathF.Atan2(normal.X,normal.Z)*180/MathF.PI,rotationStep):TargetPlacement.Yaw(doc,target);
            var oriented=target with{Yaw=yaw,Facing=null};var volume=TargetGeometry.Volume(doc,oriented);var points=volume.Corners;
            if(Math.Min(volume.MaxY,solid.MaxY)-Math.Max(volume.MinY,solid.MinY)<.0005f)continue;
            float half=axis==0?solid.Half.X:solid.Half.Z,along=axis==0?solid.Half.Z:solid.Half.X;
            float low=points.Min(p=>Vector3.Dot(p-solid.Center,tangent)),high=points.Max(p=>Vector3.Dot(p-solid.Center,tangent));
            if(Math.Min(high,along)-Math.Max(low,-along)<.0005f)continue;
            float radius=points.Max(p=>Math.Abs(Vector3.Dot(p-volume.Center,normal)));
            float delta=half+radius+Gap-Vector3.Dot(volume.Center-solid.Center,normal),distance=Math.Abs(delta);
            if(distance>Reach)continue;
            var next=Shift(oriented,normal.X*delta,normal.Z*delta,yaw);var footprint=TargetPlacement.Bounds(doc,next);
            var placed=TargetGeometry.Volume(doc,next);
            // Joined/stacked props and wall junctions have hidden interior
            // faces. Only exposed faces can carry a target; never snap into
            // the neighbouring solid just because its seam was nearest.
            if(solids.Any(other=>other.Prop==null?placed.Overlaps(other.Box,0):TargetGeometry.PropConflict(doc,next,other.Prop,other.Shape!)))continue;
            bool inside=footprint.Corners.All(p=>p.X>=ConstructionPlan.Left+PlacementClearance.Margin && p.X<=ConstructionPlan.Left+doc.Columns*2-PlacementClearance.Margin && p.Z<=ConstructionPlan.North-PlacementClearance.Margin && p.Z>=ConstructionPlan.North-doc.Rows*2+PlacementClearance.Margin);
            var forward=Vector3.Transform(Vector3.UnitZ,TargetGeometry.Rotation(TargetPlacement.Yaw(doc,target),0,0));
            candidates.Add((new(next,label,distance,new(solid,axis,sign,label,prop?.Id??0)),inside,Vector3.Dot(forward,normal)));
        }
        return candidates.OrderByDescending(p=>p.Inside).ThenBy(p=>MathF.Round(p.Snap.Distance,5)).ThenByDescending(p=>p.Facing).Select(p=>p.Snap).FirstOrDefault();
    }
    // Dragging keeps the original plane/side. Only the perpendicular coordinate
    // is corrected; cursor motion along a rotated face remains unrounded.
    public static TargetSurfaceSnap? Slide(Layout doc,Target target,TargetSurfaceFace face,IReadOnlyList<BarrierSegment> architecture,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<PlacedProp> props,bool matchFacing=false,int rotationStep=45) {
        if(target.Style!=TargetStyle.Standless)return null;
        var normal=face.Normal;
        float yaw=matchFacing?TargetEditSteps.SnapAngle(MathF.Atan2(normal.X,normal.Z)*180/MathF.PI,rotationStep):TargetPlacement.Yaw(doc,target);
        var oriented=target with{Yaw=yaw,Facing=null};var box=TargetGeometry.Volume(doc,oriented);
        float radius=box.Corners.Max(p=>Math.Abs(Vector3.Dot(p-box.Center,normal)));
        float delta=face.Plane+radius+Gap-Vector3.Dot(box.Center,normal);
        var next=Shift(oriented,normal.X*delta,normal.Z*delta,yaw);var placed=TargetGeometry.Volume(doc,next);
        var solids=Solids(doc,architecture,shapes,props);bool supported=false;
        foreach(var solid in solids.Where(s=>(s.Prop?.Id??0)==face.PropId))foreach(int axis in new[]{0,2})foreach(int sign in new[]{-1,1}) {
            var candidate=new TargetSurfaceFace(solid.Box,axis,sign,solid.Label,solid.Prop?.Id??0);
            if(Vector3.Dot(candidate.Normal,normal)<.9999f || Math.Abs(candidate.Plane-face.Plane)>.001f)continue;
            var tangent=solid.Box.Axes[axis==0?2:0];float along=axis==0?solid.Box.Half.Z:solid.Box.Half.X;
            float low=placed.Corners.Min(p=>Vector3.Dot(p-solid.Box.Center,tangent)),high=placed.Corners.Max(p=>Vector3.Dot(p-solid.Box.Center,tangent));
            if(Math.Min(high,along)-Math.Max(low,-along)>.0005f && Math.Min(placed.MaxY,solid.Box.MaxY)-Math.Max(placed.MinY,solid.Box.MinY)>.0005f)supported=true;
        }
        if(!supported || solids.Any(other=>other.Prop==null?placed.Overlaps(other.Box,0):TargetGeometry.PropConflict(doc,next,other.Prop,other.Shape!)))return null;
        return new(next,face.Surface,Math.Abs(delta),face);
    }
}
