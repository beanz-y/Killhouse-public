namespace OperatorKillhouse.Core;

public sealed record PropRule(bool CanRest,int Load,int SupportedLoad,float SurfaceY,float MinX,float MaxX,float MinZ,float MaxZ,string JoinGroup,float Seam,bool SameFamilyOnly,string Description);

public static partial class PropRules
{
    private static readonly PropRule fallback=new(false,9,0,1,0,1,0,1,"",0,false,"Unreviewed model: floor placement and touching allowed; stacking and solid intersection blocked.");
    public static IReadOnlyDictionary<string,PropRule> Reviewed=>known;
    private static readonly PropRule sseRule=new(true,1,0,1,0,1,0,1,"sse",0,false,"SSE collectible: place on the floor or a supporting surface. Fixed until collected/reset; only hard drives offer a flat stacking surface.");
    private static readonly PropRule hardDriveRule=new(true,1,1,1,0,1,0,1,"sse",0,true,"Flat hard drive supports small SSE items, including rotated/offset hard drives. Keep the centre and at least 65% of the base supported; cannot support general furniture.");
    private static readonly PropRule portableLight=new(true,2,0,1,0,1,0,1,"",0,false,"Portable light: floor or a compatible prop surface. Requires a supported base and cannot carry another prop.");
    public static PropRule For(string asset)=>FixtureMount.Portable(asset)?portableLight:asset=="SSE Hard drive"?hardDriveRule:SseObjectives.IsAsset(asset)?sseRule:known.GetValueOrDefault(asset)??known.GetValueOrDefault(PropNames.Key(asset))??fallback;
    public static float Step(float requested)=>requested==0?0:requested<=.05f?.05f:requested<=.1f?.1f:requested<=.25f?.25f:.5f;
    public static float SurfaceHeight(PlacedProp parent,PropShape shape)=>parent.Height+(shape.MaxY-shape.MinY)*For(parent.Name).SurfaceY;
    public static Footprint Surface(PlacedProp parent,PropShape shape) {
        var rule=For(parent.Name);float dx=shape.MaxX-shape.MinX,dz=shape.MaxZ-shape.MinZ;
        var patch=new PropShape(shape.MinX+rule.MinX*dx,shape.MinX+rule.MaxX*dx,0,1,shape.MinZ+rule.MinZ*dz,shape.MinZ+rule.MaxZ*dz);
        var(x,z)=PropPlacement.Position(parent);return patch.At(x,z,parent.Yaw);
    }
    public static bool Supports(PlacedProp parent,PropShape parentShape,PlacedProp child,PropShape childShape) {
        return PropSupport.Fits(child,childShape,new[]{(parent,parentShape)});
    }
    public static PlacedProp From(FurnitureItem item)=>new(item.Asset,item.Pose.X-ConstructionPlan.PropLeft,item.Pose.Z-ConstructionPlan.PropSouth,item.Pose.Yaw,item.Height,item.FixedId,item.SupportId);
    public static PlacedProp OnSurface(PlacedProp prop,PropShape shape,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<FurnitureItem> items,bool onTop) {
        if(!onTop)return prop with{Height=0,SupportId=0,SupportPeers=default};
        var(x,z)=PropPlacement.Position(prop);var box=shape.At(x,z,prop.Yaw);
        var supports=items.Where(i=>i.FixedId>0 && i.FixedId!=prop.Id).Select(From).Where(p=>PropPlacement.Shape(p,shapes)!=null && PropSupport.CanCarry(p,prop))
            .Select(p=>(Prop:p,Shape:PropPlacement.Shape(p,shapes)!)).Select(p=>(p.Prop,p.Shape,Height:SurfaceHeight(p.Prop,p.Shape),Contact:PropSupport.Contact(box,Surface(p.Prop,p.Shape))))
            .Where(p=>p.Contact>shape.Area*.001).OrderByDescending(p=>p.Height).ThenByDescending(p=>p.Contact).ThenBy(p=>p.Prop.Id).ToArray();
        var heights=new List<float>();
        foreach(var top in supports) {
            if(heights.Any(h=>Math.Abs(h-top.Height)<=.005f))continue;heights.Add(top.Height);
            var plane=supports.Where(p=>Math.Abs(p.Height-top.Height)<=.005f).OrderByDescending(p=>p.Contact).ThenBy(p=>p.Prop.Id).ToArray();
            foreach(var single in plane)if(Supports(single.Prop,single.Shape,prop,shape))return prop with{Height=top.Height,SupportId=single.Prop.Id,SupportPeers=default};
            var group=new List<(PlacedProp Prop,PropShape Shape)>();
            foreach(var p in plane.Take(8)) {
                group.Add((p.Prop,p.Shape));
                if(PropSupport.Fits(prop,shape,group))return prop with{Height=top.Height,SupportId=group[0].Prop.Id,SupportPeers=SupportPeers.Of(group.Skip(1).Select(p=>p.Prop.Id))};
            }
        }
        // This sentinel is preview-only and cannot pass commit validation.
        return prop with{SupportId=-1,SupportPeers=default};
    }
    public static bool Conflicts(PlacedProp a,PropShape sa,PlacedProp b,PropShape sb,IReadOnlyList<PlacedProp>? all=null) {
        var(ax,az)=PropPlacement.Position(a);var(bx,bz)=PropPlacement.Position(b);
        var fa=sa.At(ax,az,a.Yaw);var fb=sb.At(bx,bz,b.Yaw);
        if(!fa.Overlaps(fb,0))return false; // Butt joints have no compulsory gap.
        var ra=For(a.Name);var rb=For(b.Name);
        float vertical=Math.Min(a.Height+sa.MaxY-sa.MinY,b.Height+sb.MaxY-sb.MinY)-Math.Max(a.Height,b.Height);
        float tolerance=ra.JoinGroup=="plate" || rb.JoinGroup=="plate"?.035f:.012f;
        if(vertical<=tolerance)return false;
        if(ra.JoinGroup=="fabric" || rb.JoinGroup=="fabric")return false;
        if(a.SupportId==b.Id && b.Id>0 && Supports(b,sb,a,sa) && Math.Abs(a.Height-SurfaceHeight(b,sb))<.025f)return false;
        if(b.SupportId==a.Id && a.Id>0 && Supports(a,sa,b,sb) && Math.Abs(b.Height-SurfaceHeight(a,sa))<.025f)return false;
        bool Above(PlacedProp child,PropShape cs,PlacedProp parent,PropShape ps) {
            var seen=new HashSet<int>();var pending=new Stack<int>(PropSupport.Parents(child));
            while(pending.TryPop(out int id)) {
                if(id<=0 || !seen.Add(id))continue;
                if(id==parent.Id)return Supports(parent,ps,child,cs) && child.Height>=SurfaceHeight(parent,ps)-.025f;
                if(all?.FirstOrDefault(p=>p.Id==id) is {} next)foreach(int ancestor in PropSupport.Parents(next))pending.Push(ancestor);
            }
            return false;
        }
        if(all!=null && (Above(a,sa,b,sb) || Above(b,sb,a,sa)))return false;
        if(ra.JoinGroup.Length>0 && ra.JoinGroup==rb.JoinGroup && ra.Seam>0 && rb.Seam>0 && !fa.Overlaps(fb,-Math.Max(ra.Seam,rb.Seam)))return false;
        return true;
    }
}
