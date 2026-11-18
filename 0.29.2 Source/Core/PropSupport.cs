using System.Globalization;
using System.Text.Json.Serialization;

namespace OperatorKillhouse.Core;

// Canonical value equality keeps layout copies, undo and build digests stable.
// The first support remains SupportId for older single-support layouts.
public readonly record struct SupportPeers(string? Value)
{
    [JsonIgnore] public int[] Ids=>string.IsNullOrEmpty(Value)?Array.Empty<int>():Value.Split(',').Select(s=>int.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out int id)?id:0).ToArray();
    [JsonIgnore] public bool Empty=>string.IsNullOrEmpty(Value);
    [JsonIgnore] public bool Valid=>Empty || Value!.Length<=55 && Ids is {Length:>=1 and <=7} ids && ids.All(i=>i is >=1 and <=1000000) && Value==Of(ids).Value;
    public static SupportPeers Of(IEnumerable<int> ids){string value=string.Join(",",ids.Distinct().OrderBy(i=>i));return value.Length==0?default:new(value);}
}

public static class PropSupport
{
    public const float MinimumCoverage=.65f;
    public static IEnumerable<int> Parents(PlacedProp p)=>p.SupportId==0?p.SupportPeers.Ids:new[]{p.SupportId}.Concat(p.SupportPeers.Ids);
    public static IEnumerable<Issue> Check(Layout doc) {
        var props=doc.Props.Where(p=>p.Id>0).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First());
        foreach(var p in doc.Props) {
            if(!p.SupportPeers.Valid || !p.SupportPeers.Empty && (p.SupportId<=0 || p.SupportPeers.Ids.Contains(p.SupportId))) {
                yield return new("Shared supports need a primary support and up to seven distinct additional supports.",true);continue;
            }
            var visiting=new HashSet<int>();var done=new HashSet<int>();
            bool Visit(PlacedProp at) {
                if(done.Contains(at.Id))return true;
                if(!visiting.Add(at.Id))return false;
                foreach(int id in Parents(at))if(id<=0 || !props.TryGetValue(id,out var parent) || !Visit(parent))return false;
                visiting.Remove(at.Id);done.Add(at.Id);return true;
            }
            if((p.SupportId!=0 || !p.SupportPeers.Empty) && (p.Id<=0 || !Visit(p)))yield return new("Furniture supports must refer to existing individual pieces without cycles.",true);
        }
    }
    public static bool CanCarry(PlacedProp parent,PlacedProp child) {
        var lower=PropRules.For(parent.Name);var upper=PropRules.For(child.Name);
        return upper.CanRest && lower.SupportedLoad>0 && lower.SupportedLoad>=upper.Load && (!lower.SameFamilyOnly || lower.JoinGroup.Length>0 && lower.JoinGroup==upper.JoinGroup);
    }
    public static bool Fits(PlacedProp child,PropShape shape,IReadOnlyList<(PlacedProp Prop,PropShape Shape)> parents) {
        if(parents.Count is <1 or >8 || parents.Any(p=>!CanCarry(p.Prop,child)))return false;
        var(x,z)=PropPlacement.Position(child);var box=shape.At(x,z,child.Yaw);
        var patches=parents.Select(p=>PropRules.Surface(p.Prop,p.Shape)).ToArray();
        if(PropRules.For(child.Name).JoinGroup=="fabric")return parents.Count==1 && PropPlacement.Hit(patches[0],box.X,box.Z) && box.HalfX*box.HalfZ<=patches[0].HalfX*patches[0].HalfZ*2;
        // Recessed seats/mattresses keep the old edge check: their back/arms
        // occupy the conservative body bounds above the usable surface.
        if(parents.Any(p=>PropRules.For(p.Prop.Name).SurfaceY<.98f))return parents.Count==1 && box.Corners.All(c=>Contains(patches[0],c.X,c.Z,.015f));
        float tolerance=Math.Min(.015f,Math.Min(box.HalfX,box.HalfZ)*.08f);
        return patches.Any(p=>Contains(p,box.X,box.Z,tolerance)) && Coverage(box,patches)>=MinimumCoverage-.00001;
    }
    public static bool Declared(PlacedProp child,PropShape shape,IReadOnlyList<PlacedProp> all,IReadOnlyDictionary<string,PropShape> shapes) {
        var parents=new List<(PlacedProp,PropShape)>();
        foreach(int id in Parents(child)) {
            var parent=all.FirstOrDefault(p=>p.Id==id);var ps=parent==null?null:PropPlacement.Shape(parent,shapes);
            if(parent==null || ps==null || Math.Abs(child.Height-PropRules.SurfaceHeight(parent,ps))>(child.SupportPeers.Empty?.025f:.005f))return false;
            parents.Add((parent,ps));
        }
        return Fits(child,shape,parents);
    }
    private static bool Contains(Footprint p,float x,float z,float tolerance) {
        float r=p.Yaw*MathF.PI/180,c=MathF.Cos(r),s=MathF.Sin(r),dx=x-p.X,dz=z-p.Z;
        return Math.Abs(c*dx-s*dz)<=p.HalfX+tolerance && Math.Abs(s*dx+c*dz)<=p.HalfZ+tolerance;
    }
    private readonly record struct Point(double X,double Z);
    private static List<Point> Polygon(Footprint box)=>box.Corners.Select(c=>new Point(c.X,c.Z)).ToList();
    private static double Side(Point a,Point b,Point p)=>(b.X-a.X)*(p.Z-a.Z)-(b.Z-a.Z)*(p.X-a.X);
    private static List<Point> Clip(List<Point> polygon,Point a,Point b,bool inside) {
        var result=new List<Point>();if(polygon.Count==0)return result;
        var prior=polygon[^1];double ps=Side(a,b,prior);bool pi=inside?ps>=0:ps<=0;
        foreach(var at in polygon) {
            double side=Side(a,b,at);bool keep=inside?side>=0:side<=0;
            if(keep!=pi){double t=ps/(ps-side);result.Add(new(prior.X+(at.X-prior.X)*t,prior.Z+(at.Z-prior.Z)*t));}
            if(keep)result.Add(at);prior=at;ps=side;pi=keep;
        }
        return result;
    }
    private static double Area(List<Point> polygon) {
        double area=0;for(int n=0;n<polygon.Count;n++){var a=polygon[n];var b=polygon[(n+1)%polygon.Count];area+=a.X*b.Z-b.X*a.Z;}return Math.Abs(area)*.5;
    }
    public static double Contact(Footprint child,Footprint parent) {
        var polygon=Polygon(child);var edges=Polygon(parent);
        for(int n=0;n<4;n++)polygon=Clip(polygon,edges[n],edges[(n+1)%4],true);
        return Area(polygon);
    }
    public static double Coverage(Footprint child,IEnumerable<Footprint> parents) {
        // Subtract each covered convex region. Remaining pieces do not overlap,
        // so adjacent/overlapping supports cannot double-count their contact.
        var remaining=new List<List<Point>>{Polygon(child)};
        foreach(var parent in parents) {
            var edges=Polygon(parent);var next=new List<List<Point>>();
            foreach(var piece in remaining) {
                var inside=piece;
                for(int n=0;n<4 && inside.Count>0;n++) {
                    var outside=Clip(inside,edges[n],edges[(n+1)%4],false);
                    if(outside.Count>=3 && Area(outside)>1e-12)next.Add(outside);
                    inside=Clip(inside,edges[n],edges[(n+1)%4],true);
                }
            }
            remaining=next;if(remaining.Count==0)return 1;
        }
        return Math.Clamp(1-remaining.Sum(Area)/(4*child.HalfX*child.HalfZ),0,1);
    }
}
