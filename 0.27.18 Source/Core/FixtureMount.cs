using System.Numerics;

namespace OperatorKillhouse.Core;

// Wall models have a normalized back plate at local Z=0 and face local +Z.
// Permanent-shell attachment is measured by NativeShell, never a guessed grid
// boundary. These managed rules describe only the actual constructed panels.
public static class FixtureMount
{
    public const float Reach=1,Gap=.002f,Tolerance=.006f;
    public static bool Wall(string name)=>name is "Bulkhead wall light" or "Shade wall sconce" or "Glass wall sconce";
    public static bool Mounted(string name)=>Wall(name) || name==PowerCircuits.BreakerAsset;
    public static bool Portable(string name)=>name is "Wall lamp" or "Desk lamp";
    public static string Label(string name)=>name=="Wall lamp"?"Portable ground light":name;
    public static IEnumerable<PlacedProp> InteriorCandidates(PlacedProp prop,PropShape shape,IReadOnlyList<BarrierSegment> architecture,float reach=Reach) {
        var(x,z)=PropPlacement.Position(prop);var center=shape.At(x,z,prop.Yaw);
        var walls=architecture.SelectMany(ReinforcedPanels.Solids).Select(WindowGeometry.Volume).ToArray();
        foreach(var wall in walls)foreach(int axis in new[]{0,2})foreach(int sign in new[]{-1,1}) {
            var normal=wall.Axes[axis]*sign;var tangent=wall.Axes[axis==0?2:0];
            float yaw=TargetPlacement.Angle(MathF.Atan2(normal.X,normal.Z)*180/MathF.PI);
            var origin=shape.Origin(center.X,center.Z,yaw);
            var oriented=prop with{X=origin.X-ConstructionPlan.PropLeft,Z=origin.Z-ConstructionPlan.PropSouth,Yaw=yaw,SupportId=0,SupportPeers=default};
            var box=TargetGeometry.PropVolume(oriented,shape);
            float along=axis==0?wall.Half.Z:wall.Half.X;
            if(box.MinY<wall.MinY || box.MaxY>wall.MaxY)continue;
            float low=box.Corners.Min(p=>Vector3.Dot(p-wall.Center,tangent)),high=box.Corners.Max(p=>Vector3.Dot(p-wall.Center,tangent));
            if(high-low>2*along)continue;
            float slide=Math.Clamp(0,-along-low,along-high);
            oriented=oriented with{X=oriented.X+tangent.X*slide,Z=oriented.Z+tangent.Z*slide};box=TargetGeometry.PropVolume(oriented,shape);
            float plane=Vector3.Dot(wall.Center,normal)+(axis==0?wall.Half.X:wall.Half.Z);
            float back=box.Corners.Min(p=>Vector3.Dot(p,normal));float delta=plane+Gap-back;
            if(Math.Abs(delta)>reach)continue;
            var next=oriented with{X=oriented.X+normal.X*delta,Z=oriented.Z+normal.Z*delta};
            if(Distance(prop,next)>reach)continue;
            if(walls.Any(w=>TargetGeometry.PropVolume(next,shape).Overlaps(w,0)))continue;
            yield return next;
        }
    }
    public static bool InteriorAttached(PlacedProp prop,PropShape shape,IReadOnlyList<BarrierSegment> architecture)=>InteriorCandidates(prop,shape,architecture).Any(p=>Distance(p,prop)<Tolerance && MathF.Cos((p.Yaw-prop.Yaw)*MathF.PI/180)>.99999f);
    public static float Distance(PlacedProp a,PlacedProp b)=>MathF.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    public static PlacedProp Nearest(PlacedProp original,IEnumerable<PlacedProp> candidates) {
        var next=candidates.OrderBy(p=>MathF.Round(Distance(p,original),5)).ThenByDescending(p=>MathF.Cos((p.Yaw-original.Yaw)*MathF.PI/180)).FirstOrDefault();
        if(next==null)return original;
        return Distance(next,original)<.00001f && Math.Abs(next.Yaw-original.Yaw)<.00001f && original.SupportId==0 && original.SupportPeers==default?original:next;
    }
    public static bool Clear(Layout doc,PlacedProp prop,PropShape shape,IReadOnlyList<BarrierSegment> architecture,IEnumerable<Footprint>? targets) {
        var volume=TargetGeometry.PropVolume(prop,shape);var(x,z)=PropPlacement.Position(prop);var box=shape.At(x,z,prop.Yaw);
        // Shell recesses may sit beyond the editor's nominal lattice. A native
        // five-point backing check, required before saving/staging, supplies the
        // permanent-wall constraint for these small wall-only fixtures.
        bool bounded=OuterWallFloor.Bounds(doc).Contains(box);
        return bounded && !architecture.SelectMany(ReinforcedPanels.Solids).Any(w=>volume.Overlaps(WindowGeometry.Volume(w),0)) &&
            !architecture.Where(w=>w.Kind is Barrier.Door or Barrier.Frame).Any(w=>PlacementClearance.OpeningBlocked(box,w,doc.DoorOptions.GetValueOrDefault(w.Edge))) && !(targets??Array.Empty<Footprint>()).Any(t=>t.Overlaps(box,PlacementClearance.Margin));
    }
}
