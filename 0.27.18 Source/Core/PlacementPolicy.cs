namespace OperatorKillhouse.Core;

public readonly record struct FloorBox(float MinX,float MaxX,float MinZ,float MaxZ)
{
    public bool Overlaps(FloorBox other,float gap=.05f)=>MinX<other.MaxX+gap && MaxX>other.MinX-gap && MinZ<other.MaxZ+gap && MaxZ>other.MinZ-gap;
}
public static class PlacementPolicy
{
    public static bool CanFit(Layout layout,FloorBox box,IEnumerable<FloorBox> occupied) {
        var footprint=Footprint.From(box);var space=new FurnitureSpace(layout);
        return footprint.Valid && !occupied.Any(o=>Footprint.From(o).Overlaps(footprint)) && space.Clear(footprint);
    }
}
