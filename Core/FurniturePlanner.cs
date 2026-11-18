namespace OperatorKillhouse.Core;

public sealed record FurnitureItem(string Asset,FurniturePose Pose,float Height,string Request,bool Explicit,int PropIndex=-1,int FixedId=0,int SupportId=0);
public sealed record FurnitureResult(IReadOnlyList<FurnitureItem> Items,IReadOnlyList<string> Unresolved,int Required,int Adjusted,int AccentOmissions,IReadOnlyList<string> CompactThemes);
public static class FurniturePlanner
{
    public static FurnitureResult Make(Layout doc,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<Footprint>? targets=null,IReadOnlyList<BarrierSegment>? architecture=null)
    {
        var space=new FurnitureSpace(doc,targets,architecture);var items=new List<FurnitureItem>();var unresolved=new List<string>();var compact=new List<string>();int adjusted=0,accents=0;
        string? Resolve(IEnumerable<string> names)=>names.FirstOrDefault(shapes.ContainsKey);
        void Add(string key,FurniturePose pose,float height,string label,bool explicitProp,int propIndex=-1,int fixedId=0,int supportId=0) {
            space.Commit(pose,navigation:fixedId==0);items.Add(new(key,pose,height,label,explicitProp,propIndex,fixedId,supportId));if(pose.Adjusted)adjusted++;
        }
        var fixedIssues=PropPlacement.Check(doc,shapes,targets,architecture);unresolved.AddRange(fixedIssues.Where(i=>i.Fatal).Select(i=>i.Message));
        // Fixed items reserve their exact poses before imported/theme furniture.
        // A failed fixed placement is an error; it must never be moved to fit.
        foreach(var (prop,index) in doc.Props.Select((prop,index)=>(prop,index)).OrderBy(p=>p.prop.Id==0?1:0)) {
            string? key=Resolve(new[]{prop.Name,PropNames.Key(prop.Name)});
            if(key==null) {unresolved.Add($"Explicit prop '{prop.Name}': no safe native asset found.");continue;}
            float x=ConstructionPlan.PropLeft+prop.X,z=ConstructionPlan.PropSouth+prop.Z;
            var center=shapes[key].At(x,z,prop.Yaw);
            var pose=prop.Id>0?(fixedIssues.Any(i=>i.Fatal)?null:new FurniturePose(x,z,prop.Yaw,center,false)):space.Find(shapes[key],x,z,prop.Yaw,space.CellAt(center.X,center.Z),true,rotate:false);
            if(pose==null)unresolved.Add($"Explicit prop '{prop.Name}': no clear placement in its original room.");
            else Add(key,pose,prop.Height,"prop: "+prop.Name,true,index,prop.Id,prop.SupportId);
        }
        // Large primary items get room before small decorative pieces. A theme
        // requests one main furnishing, rather than two duplicate beds/desks.
        var themes=doc.Themes.OrderByDescending(t=> {
            string? key=Resolve(PropNames.Primary(t.Value));return key==null?0:shapes[key].Area;
        }).ThenBy(t=>t.Key.Row).ThenBy(t=>t.Key.Column).ToArray();
        foreach(var(cell,theme) in themes) {
            bool found=false;var names=PropNames.Primary(theme);
            foreach(string key in names.Where(shapes.ContainsKey)) {
                var(x,z)=ConstructionPlan.Center(cell);var(originX,originZ)=shapes[key].Origin(x,z,0);
                var pose=space.Find(shapes[key],originX,originZ,0,cell,true);if(pose==null)continue;
                Add(key,pose,0,$"{theme} {cell.Row},{cell.Column}",false);found=true;
                if(key!=names[0])compact.Add($"{theme} {cell.Row},{cell.Column}: {key} used where {names[0]} has no safe fit.");break;
            }
            if(!found)unresolved.Add($"{theme} at {cell.Row},{cell.Column}: no clear native furnishing in its room.");
        }
        foreach(var(cell,theme) in themes) {
            string? key=Resolve(PropNames.Accent(theme));if(key==null) {accents++;continue;}
            var(x,z)=ConstructionPlan.Center(cell);var(originX,originZ)=shapes[key].Origin(x,z,0);
            var pose=space.Find(shapes[key],originX,originZ,0,cell,true);
            if(pose==null)accents++;else Add(key,pose,0,$"{theme} accent {cell.Row},{cell.Column}",false);
        }
        return new(items,unresolved,doc.Props.Count+doc.Themes.Count,adjusted,accents,compact);
    }
}
