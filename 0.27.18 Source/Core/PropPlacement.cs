namespace OperatorKillhouse.Core;

public sealed record PropPreview(PlacedProp Prop,int? Previous,string? Rejection) {public bool CanPlace=>Rejection==null;}

public static class PropPlacement
{
    public const int Limit=128;
    public static (float X,float Z) Position(PlacedProp prop)=>(ConstructionPlan.PropLeft+prop.X,ConstructionPlan.PropSouth+prop.Z);
    public static (float X,float Y) Grid(PlacedProp prop,PropShape shape) {
        var(x,z)=Position(prop);var box=shape.At(x,z,prop.Yaw);
        return((box.X-ConstructionPlan.Left)/2,(ConstructionPlan.North-box.Z)/2);
    }
    // The footprint centre snaps, so asymmetric model pivots do not make the
    // grid or cursor lie about where a chair/table will land.
    public static PlacedProp At(string asset,PropShape shape,float gridX,float gridY,float yaw,int id,float height=0,float step=.5f) {
        yaw=TargetPlacement.Angle(yaw);
        var(x,z)=shape.Origin(ConstructionPlan.Left+TargetPlacement.Snap(gridX*2,PropRules.Step(step)),ConstructionPlan.North-TargetPlacement.Snap(gridY*2,PropRules.Step(step)),yaw);
        return new(asset,x-ConstructionPlan.PropLeft,z-ConstructionPlan.PropSouth,yaw,height,id);
    }
    public static PropShape? Shape(PlacedProp prop,IReadOnlyDictionary<string,PropShape> shapes)=>shapes.GetValueOrDefault(prop.Name)??shapes.GetValueOrDefault(PropNames.Key(prop.Name));
    public static IReadOnlyList<Issue> CheckAgainst(PlacedProp prop,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<FurnitureItem> existing,int? previous=null) {
        var shape=Shape(prop,shapes);if(shape==null)return new[]{new Issue("Native furniture model is unavailable.",true)};
        return existing.Any(p=>(previous==null || p.PropIndex!=previous) && Shape(PropRules.From(p),shapes) is {} other && PropRules.Conflicts(prop,shape,PropRules.From(p),other))?new[]{new Issue("Intersects solid furniture beyond this prop's contact/stacking allowance.",true)}:Array.Empty<Issue>();
    }
    public static IReadOnlyList<Issue> Check(Layout doc,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<Footprint>? targets=null,IReadOnlyList<BarrierSegment>? architecture=null) {
        if(!doc.Props.Any(p=>p.Id>0))return Array.Empty<Issue>();
        var issues=new List<Issue>();
        var fixedProps=doc.Props.Where(p=>p.Id>0).ToArray();
        foreach(var prop in fixedProps) {
            var shape=Shape(prop,shapes);
            if(shape==null || !shape.Valid){issues.Add(new($"Furniture {prop.Id}: native model '{prop.Name}' is unavailable.",true));continue;}
            if(PowerCircuits.IsElectrical(prop.Name) && prop.Height+shape.MaxY-shape.MinY>2.85f)issues.Add(new($"Fixture {prop.Id}: keep the entire light or breaker below 2.85 m.",true));
            var(x,z)=Position(prop);var footprint=shape.At(x,z,prop.Yaw);
            issues.AddRange(TargetGeometry.PropIssues(doc,prop,shape));
            bool clear=FixtureMount.Mounted(prop.Name)?FixtureMount.Clear(doc,prop,shape,architecture??WallJunctions.Make(doc,.60f,.60f).Build.Segments,TargetGeometry.FixedObstacles(doc,targets)):OuterWallFloor.Clear(doc,footprint,prop.Height+TargetGeometry.FloorGap,prop.Height+TargetGeometry.FloorGap+shape.MaxY-shape.MinY,architecture,TargetGeometry.FixedObstacles(doc,targets));
            if(!clear)issues.Add(new($"Furniture {prop.Id}: keep the footprint inside the house, clear of walls, window frames/sills, doorways and targets.",true));
            if(prop.SupportId!=0) {
                if(!PropRules.For(prop.Name).CanRest)issues.Add(new($"Furniture {prop.Id}: this model stays on the floor; it cannot be stacked.",true));
                else if(!PropSupport.Declared(prop,shape,fixedProps,shapes))issues.Add(new($"Furniture {prop.Id}: keep the centre and at least 65% of the base on compatible support surfaces at the same height. Convert imported furniture to individual props first.",true));
            }
            foreach(var other in fixedProps.Where(p=>p.Id<prop.Id))if(Shape(other,shapes) is {} otherShape && PropRules.Conflicts(prop,shape,other,otherShape,fixedProps)){issues.Add(new($"Furniture {prop.Id}: intersects furniture {other.Id} beyond its allowed contact or stacking region.",true));break;}
        }
        return issues;
    }
    public static bool Hit(Footprint box,float x,float z) {
        float angle=box.Yaw*MathF.PI/180,c=MathF.Cos(angle),s=MathF.Sin(angle),dx=x-box.X,dz=z-box.Z;
        return Math.Abs(c*dx-s*dz)<=box.HalfX+.06f && Math.Abs(s*dx+c*dz)<=box.HalfZ+.06f;
    }
}

public sealed class PropPreviewCache
{
    private LayoutDraft? document;private int revision,context;private bool strict;private PropPreview? preview;
    public PropPreview Get(LayoutDraft draft,PlacedProp prop,int? previous,int validationContext) {
        if(document!=draft || revision!=draft.Revision || context!=validationContext || strict!=draft.StrictValidation || preview==null || preview.Prop!=prop || preview.Previous!=previous) {
            document=draft;revision=draft.Revision;context=validationContext;strict=draft.StrictValidation;preview=draft.PreviewProp(previous,prop);
        }
        return preview;
    }
    public void Clear(){document=null;preview=null;}
}
