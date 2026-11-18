namespace OperatorKillhouse.Core;

public static class PlacementClearance
{
    public const float Margin=.01f;
    // Native DoorV2 serialized maxRotationY is 110 degrees on all 37 stock
    // doors. Aperture width exceeds the measured .8-.9 m leaf; the extra radius
    // and skin below also cover leaf thickness and the small hinge offset.
    public const float DoorDegrees=110,DoorSkin=.05f;
    public static float ApertureHalf=>(DoorwayProfile.Right-DoorwayProfile.Left)/2;
    public static IReadOnlyList<BarrierSegment> WithShell(Layout doc,IReadOnlyList<BarrierSegment> architecture)=>architecture.Concat(doc.Edges.Where(p=>p.Value==Barrier.Wall && !doc.InteriorEdge(p.Key) && !architecture.Any(s=>s.Edge==p.Key) && !(p.Key.Horizontal==false && p.Key.Row==1 && (p.Key.Column==0 || p.Key.Column==doc.Columns))).Select(p=>new BarrierSegment(p.Key,Barrier.Wall,ConstructionPlan.Left+(p.Key.Column+(p.Key.Horizontal?.5f:0))*2,ConstructionPlan.North-(p.Key.Row+(p.Key.Horizontal?0:.5f))*2))).ToArray();
    public static Footprint[] Jambs(BarrierSegment opening) {
        if(DoorwayAssembly.Frame(opening))return DoorwayAssembly.Jambs(opening,ApertureHalf);
        float half=opening.Span/2,inner=Math.Min(ApertureHalf,half-.02f),center=(half+inner)/2,width=(half-inner)/2;
        return new[]{-1f,1f}.Select(s=>opening.Edge.Horizontal?new Footprint(opening.X+s*center,opening.Z,width,opening.Thickness/2):new Footprint(opening.X,opening.Z-s*center,opening.Thickness/2,width)).ToArray();
    }
    public static bool OpeningBlocked(Footprint box,BarrierSegment opening,DoorSettings? settings=null) {
        var portal=opening.Edge.Horizontal?new Footprint(opening.X,opening.Z,ApertureHalf,opening.Thickness/2):new Footprint(opening.X,opening.Z,opening.Thickness/2,ApertureHalf);
        if(box.Overlaps(portal,Margin) || Jambs(opening).Any(p=>box.Overlaps(p,Margin)))return true;
        if(opening.Kind!=Barrier.Door)return false;
        if(settings!=null)return SwingBlocked(box,opening,settings);
        // Legacy native orientation is not encoded in the file. Protect both
        // hinge/opening choices until the author chooses an explicit swing.
        return new[]{false,true}.Any(hinge=>new[]{false,true}.Any(opens=>SwingBlocked(box,opening,new(hinge,opens))));
    }
    public static bool SwingBlocked(Footprint box,BarrierSegment opening,DoorSettings settings) {
        float ax=opening.Edge.Horizontal?1:0,az=opening.Edge.Horizontal?0:-1;
        float sign=settings.HingeHigh?-1:1,hx=opening.X-ax*sign*ApertureHalf,hz=opening.Z-az*sign*ApertureHalf;
        float nx=opening.Edge.Horizontal?0:settings.OpensPositive?1:-1,nz=opening.Edge.Horizontal?(settings.OpensPositive?-1:1):0;
        var polygon=box.Corners.Select(p=>((p.X-hx)*ax*sign+(p.Z-hz)*az*sign,(p.X-hx)*nx+(p.Z-hz)*nz)).ToList();
        float skin=DoorSkin+Margin,radius=2*ApertureHalf+skin;
        polygon=Clip(polygon,0,1,-skin);
        float angle=DoorDegrees*MathF.PI/180;polygon=Clip(polygon,MathF.Sin(angle),-MathF.Cos(angle),-skin);
        if(polygon.Count==0)return false;
        float area=0;bool positive=false,negative=false;
        for(int n=0;n<polygon.Count;n++) {
            var a=polygon[n];var b=polygon[(n+1)%polygon.Count];float cross=a.Item1*b.Item2-a.Item2*b.Item1;area+=cross;positive|=cross>1e-7f;negative|=cross< -1e-7f;
            float dx=b.Item1-a.Item1,dz=b.Item2-a.Item2,length=dx*dx+dz*dz,t=length<1e-10f?0:Math.Clamp(-(a.Item1*dx+a.Item2*dz)/length,0,1);
            float x=a.Item1+t*dx,z=a.Item2+t*dz;if(x*x+z*z<=radius*radius)return true;
        }
        return Math.Abs(area)>1e-7f && !(positive && negative);
    }
    private static List<(float,float)> Clip(List<(float,float)> input,float nx,float nz,float limit) {
        var result=new List<(float,float)>();if(input.Count==0)return result;
        var a=input[^1];float da=nx*a.Item1+nz*a.Item2-limit;
        foreach(var b in input) {
            float db=nx*b.Item1+nz*b.Item2-limit;
            if((da>=0)!=(db>=0)){float t=da/(da-db);result.Add((a.Item1+(b.Item1-a.Item1)*t,a.Item2+(b.Item2-a.Item2)*t));}
            if(db>=0)result.Add(b);a=b;da=db;
        }
        return result;
    }
    public static (float X,float Z) Align(Footprint box,IReadOnlyList<BarrierSegment> architecture,float reach=.30f,float margin=Margin) {
        float dx=0,dz=0,bestX=float.PositiveInfinity,bestZ=float.PositiveInfinity;var corners=box.Corners;
        foreach(var wall in architecture.Where(s=>s.Kind==Barrier.Wall)) {
            bool h=wall.Edge.Horizontal;float center=h?box.Z:box.X,face=h?wall.Z:wall.X,half=wall.Thickness/2,offset=center-face;
            if(Math.Abs(offset)<half+.01f)continue; // A cursor in the wall stays red.
            float low=h?corners.Min(p=>p.X):corners.Min(p=>p.Z),high=h?corners.Max(p=>p.X):corners.Max(p=>p.Z),along=h?wall.X:wall.Z;
            if(high<along-wall.Span/2 || low>along+wall.Span/2)continue;
            float extent=h?corners.Max(p=>Math.Abs(p.Z-box.Z)):corners.Max(p=>Math.Abs(p.X-box.X));
            float delta=Math.Sign(offset)*(half+extent+margin+.001f)-offset,distance=Math.Abs(delta);
            if(distance>reach)continue;
            if(h && distance<bestZ){bestZ=distance;dz=delta;}else if(!h && distance<bestX){bestX=distance;dx=delta;}
        }
        return(dx,dz);
    }
}
