using System.Numerics;

namespace OperatorKillhouse.Core;

// Unity Euler order is Y * X * Z. Box axes and SAT remain plain managed data
// so planning/preview never depend on a live Unity object or physics query.
public readonly record struct SolidBox(Vector3 Center,Vector3 Half,Quaternion Rotation)
{
    public Vector3[] Axes=>new[]{Vector3.Transform(Vector3.UnitX,Rotation),Vector3.Transform(Vector3.UnitY,Rotation),Vector3.Transform(Vector3.UnitZ,Rotation)};
    public Vector3[] Corners {get {var axes=Axes;var center=Center;var half=Half;return Enumerable.Range(0,8).Select(n=>center+axes[0]*half.X*((n&1)==0?-1:1)+axes[1]*half.Y*((n&2)==0?-1:1)+axes[2]*half.Z*((n&4)==0?-1:1)).ToArray();}}
    public float MinY=>Corners.Min(p=>p.Y);
    public float MaxY=>Corners.Max(p=>p.Y);
    public bool Overlaps(SolidBox other,float gap=.01f) {
        var a=Axes;var b=other.Axes;var delta=other.Center-Center;
        foreach(var raw in a.Concat(b).Concat(a.SelectMany(x=>b.Select(y=>Vector3.Cross(x,y))))) {
            if(raw.LengthSquared()<1e-10f)continue;var axis=Vector3.Normalize(raw);
            float Radius(Vector3[] basis,Vector3 half)=>Math.Abs(Vector3.Dot(basis[0],axis))*half.X+Math.Abs(Vector3.Dot(basis[1],axis))*half.Y+Math.Abs(Vector3.Dot(basis[2],axis))*half.Z;
            if(Math.Abs(Vector3.Dot(delta,axis))>=Radius(a,Half)+Radius(b,other.Half)+gap)return false;
        }
        return true;
    }
}
public static class TargetGeometry
{
    public const float MaximumHeight=2.8f,Ceiling=3,FloorGap=.01f,NativeYaw=90;
    // Canonical standless root: the native child mesh faces local +Z after
    // correcting its -90-degree child yaw. Bounds include its solid hitbox.
    // Independent fixture: research/stock-geometry/standless-targets.json.
    public static readonly PropShape Standless=new(-.31504f,.31504f,0,1.06729f,-.00152f,.00152f);
    // Native upright paper + role-specific solid hitboxes, with only the stand
    // excluded. Root offsets are retained; Root() normalizes their bottom/centre.
    // Independent fixture: research/standless-roles-20261020/paper-geometry.json.
    public static PropShape StandlessFor(TargetRole role)=>role switch {
        TargetRole.Threat=>Standless,
        TargetRole.Civilian=>new(-.264022f,.366021f,.709999f,1.777267f,.073500f,.076500f),
        TargetRole.Hostage=>new(-.348243f,.450241f,.709999f,1.777267f,.072002f,.076500f),
        _=>throw new ArgumentOutOfRangeException(nameof(role))
    };
    public static float StandlessYaw(TargetRole role)=>role==TargetRole.Threat?NativeYaw:0;
    public static PropShape Upright(TargetRole role)=>role==TargetRole.Hostage?new(-.348242f,.450240f,-.039f,1.778f,-.530668f,.759335f):new(-.264022f,.366021f,-.039f,1.778f,-.530668f,.759335f);
    public static Quaternion Rotation(float yaw,float pitch,float roll)=>Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,pitch*MathF.PI/180)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,roll*MathF.PI/180);
    public static (float X,float South) HeadDirection(Layout doc,Target target) {
        var head=Vector3.Transform(Vector3.UnitY,Rotation(TargetPlacement.Yaw(doc,target),target.Pitch,target.Roll));
        float length=MathF.Sqrt(head.X*head.X+head.Z*head.Z);
        return length>.0001f?(head.X/length,-head.Z/length):(0,head.Y<0?1:-1);
    }
    public static SolidBox Volume(Layout doc,Target t,PropShape? shape=null) {
        shape??=t.Style==TargetStyle.Standless?StandlessFor(t.Role):Upright(t.Role);
        var(x,z)=TargetPlacement.Position(t);var center=new Vector3((shape.MinX+shape.MaxX)/2,(shape.MinY+shape.MaxY)/2,(shape.MinZ+shape.MaxZ)/2);
        var half=new Vector3((shape.MaxX-shape.MinX)/2,(shape.MaxY-shape.MinY)/2,(shape.MaxZ-shape.MinZ)/2);
        var rotation=Rotation(TargetPlacement.Yaw(doc,t),t.Pitch,t.Roll);
        if(t.Style==TargetStyle.Standless) {
            var local=new SolidBox(Vector3.Zero,half,rotation);float up=local.Corners.Max(p=>p.Y);
            return new(new(x,t.Height+FloorGap+up,z),half,rotation);
        }
        return new(new Vector3(x,-shape.MinY,z)+Vector3.Transform(center,rotation),half,rotation);
    }
    public static Vector3 Root(Layout doc,Target t,PropShape shape) {
        var box=Volume(doc,t,shape);return box.Center-Vector3.Transform(new((shape.MinX+shape.MaxX)/2,(shape.MinY+shape.MaxY)/2,(shape.MinZ+shape.MaxZ)/2),box.Rotation);
    }
    public static Footprint Footprint(Layout doc,Target t,PropShape? shape=null) {
        if(t.Style==TargetStyle.Upright){var(x,z)=TargetPlacement.Position(t);return (shape??Upright(t.Role)).At(x,z,TargetPlacement.Yaw(doc,t));}
        var box=Volume(doc,t,shape);float yaw=TargetPlacement.Yaw(doc,t);
        var inverse=Quaternion.Inverse(Rotation(yaw,0,0));var corners=box.Corners.Select(p=>Vector3.Transform(p-box.Center,inverse)).ToArray();
        return new(box.Center.X,box.Center.Z,Math.Max(.001f,corners.Max(p=>Math.Abs(p.X))),Math.Max(.001f,corners.Max(p=>Math.Abs(p.Z))),yaw);
    }
    public static IReadOnlyList<Target> Ordered(Layout doc)=>doc.AllTargets.OrderBy(t=>t.Cell.Row).ThenBy(t=>t.Cell.Column).ThenBy(t=>t.Id).ToArray();
    public static SolidBox PropVolume(PlacedProp prop,PropShape shape) {
        var(x,z)=PropPlacement.Position(prop);var footprint=shape.At(x,z,prop.Yaw);
        return new(new(footprint.X,prop.Height+FloorGap+(shape.MaxY-shape.MinY)/2,footprint.Z),new(footprint.HalfX,(shape.MaxY-shape.MinY)/2,footprint.HalfZ),Rotation(prop.Yaw,0,0));
    }
    public static bool OnPatch(Footprint footprint,Footprint patch,float tolerance=0) {
        float radians=patch.Yaw*MathF.PI/180,c=MathF.Cos(radians),s=MathF.Sin(radians);
        return footprint.Corners.All(p=>{float x=p.X-patch.X,z=p.Z-patch.Z;return Math.Abs(c*x-s*z)<=patch.HalfX+tolerance && Math.Abs(s*x+c*z)<=patch.HalfZ+tolerance;});
    }
    public static bool PropConflict(Layout doc,Target target,PlacedProp prop,PropShape shape) {
        var volume=Volume(doc,target);if(!volume.Overlaps(PropVolume(prop,shape),-TargetContact.PropTolerance))return false;
        // A couch/bed's overall bounds include back/headboard above its reviewed
        // seat/mattress patch. The whole target projection must fit that patch.
        return !(target.Style==TargetStyle.Standless && PropRules.For(prop.Name).SupportedLoad>0 && volume.MinY>=PropRules.SurfaceHeight(prop,shape)+FloorGap-.002f && OnPatch(Footprint(doc,target),PropRules.Surface(prop,shape),-.005f));
    }
    public static Target OnFurniture(Layout doc,Target target,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<PlacedProp> props) {
        var footprint=Footprint(doc,target);
        var support=props.Where(p=>p.Id>0).Where(p=>PropPlacement.Shape(p,shapes) is {} shape && PropRules.For(p.Name).SupportedLoad>0 && OnPatch(footprint,PropRules.Surface(p,shape),-.005f)).OrderByDescending(p=>PropRules.SurfaceHeight(p,PropPlacement.Shape(p,shapes)!)).ThenBy(p=>p.Id).FirstOrDefault();
        return target with{Height=support==null?-1:PropRules.SurfaceHeight(support,PropPlacement.Shape(support,shapes)!)};
    }
    public static IReadOnlyList<Issue> PropIssues(Layout doc,PlacedProp prop,PropShape shape)=>doc.AllTargets.Where(t=>t.Style==TargetStyle.Standless && PropConflict(doc,t,prop,shape)).Select(t=>new Issue($"Furniture {prop.Id}: intersects standless target {t.Id} at its current height/tilt.",true)).ToArray();
    public static IReadOnlyList<Footprint>? FixedObstacles(Layout doc,IEnumerable<Footprint>? measured) {
        if(!doc.AllTargets.Any(t=>t.Style==TargetStyle.Standless))return measured?.ToArray();
        var targets=Ordered(doc);var boxes=measured?.ToArray()??targets.Select(t=>Footprint(doc,t)).ToArray();
        if(boxes.Length!=targets.Count)throw new ArgumentException("Target footprint order mismatch.");
        return boxes.Where((_,n)=>targets[n].Style!=TargetStyle.Standless).ToArray();
    }
}
