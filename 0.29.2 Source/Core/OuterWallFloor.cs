namespace OperatorKillhouse.Core;

public readonly record struct OuterFloorBounds(float Left,float Right,float South,float North)
{
    public bool Contains(Footprint box,float margin=0) {
        foreach(var p in box.Corners)if(p.X<Left+margin-.00001f || p.X>Right-margin+.00001f || p.Z<South+margin-.00001f || p.Z>North-margin+.00001f)return false;
        return true;
    }
}

// Installed shell inside faces in Layouts-local coordinates, shared by both
// houses. These bounds extend prop space; they NEVER redefine the saved grid.
public static class OuterWallFloor
{
    public const float Left=1.9964f,Right=22.9964f,South=1.93867f,North=22.93867f;
    public static OuterFloorBounds Bounds(int columns,int rows)=>new(Left,columns==10?Right:ConstructionPlan.Left+columns*2,rows==10?South:ConstructionPlan.North-rows*2,North);
    public static OuterFloorBounds Bounds(Layout doc)=>Bounds(doc.Columns,doc.Rows);
    public static (float Low,float High) Portal=>(18.93867f,20.93867f);
    public static PlacedProp Align(Layout doc,PlacedProp prop,PropShape shape) {
        var b=Bounds(doc);var(x,z)=PropPlacement.Position(prop);var box=shape.At(x,z,prop.Yaw);
        if(box.X<b.Left || box.X>b.Right || box.Z<b.South || box.Z>b.North)return prop;
        var corners=box.Corners;float gap=PlacementClearance.Margin;
        float dx=Math.Max(0,b.Left+gap-corners.Min(p=>p.X))+Math.Min(0,b.Right-gap-corners.Max(p=>p.X));
        float dz=Math.Max(0,b.South+gap-corners.Min(p=>p.Z))+Math.Min(0,b.North-gap-corners.Max(p=>p.Z));
        return prop with{X=prop.X+dx,Z=prop.Z+dz};
    }
    public static bool Clear(Layout doc,Footprint box,float bottom,float top,IReadOnlyList<BarrierSegment>? architecture,IEnumerable<Footprint>? targets) {
        if(!box.Valid || !Bounds(doc).Contains(box,PlacementClearance.Margin))return false;
        architecture??=WallJunctions.Make(doc,.60f,.60f).Build.Segments;
        foreach(var wall in architecture.Where(s=>BoundaryWalls.Visible(doc,s.Edge))) {
            if(wall.Kind==Barrier.Wall && WindowGeometry.AtHeight(wall,bottom,top) && box.Overlaps(new(wall.X,wall.Z,wall.Edge.Horizontal?wall.Span/2:wall.Thickness/2,wall.Edge.Horizontal?wall.Thickness/2:wall.Span/2),PlacementClearance.Margin))return false;
            if(wall.Kind is Barrier.Door or Barrier.Frame && PlacementClearance.OpeningBlocked(box,wall,doc.DoorOptions.GetValueOrDefault(wall.Edge)))return false;
        }
        var obstacles=targets??doc.AllTargets.Select(t=>{var(x,z)=TargetPlacement.Position(t);return new Footprint(x,z,.45f,.8f,TargetPlacement.Yaw(doc,t));});
        if(obstacles.Any(t=>t.Overlaps(box,PlacementClearance.Margin)))return false;
        var b=Bounds(doc);float entryZ=ConstructionPlan.North-3,right=ConstructionPlan.Left+doc.Columns*2;
        return !box.Overlaps(new((b.Left+ConstructionPlan.Left+.9f)/2,entryZ,(ConstructionPlan.Left+.9f-b.Left)/2,.6f),PlacementClearance.Margin) &&
            !box.Overlaps(new((right-.9f+b.Right)/2,entryZ,(b.Right-right+.9f)/2,.6f),PlacementClearance.Margin);
    }
}
