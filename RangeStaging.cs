using System.Diagnostics;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;
internal sealed partial class RangeSession
{
    internal bool FurnitureMode {get;}=Plugin.Furniture.Value;
    private readonly Dictionary<int,PreparedLayout> prepared=new();
    private readonly LayoutCachePolicy cache=new();
    private StageJob? staging;
    private readonly Dictionary<int,string> buildErrors=new();
    private AssetSignature? assets;
    internal bool Building=>staging!=null;
    private sealed class StageJob
    {
        internal int Index,Steps,Frames;
        internal Layout Doc=null!;internal Task<PreparedLayout> Task=null!;
        internal JointPlan Joints=null!;internal Dictionary<WallEnd,float> Contacts=null!;internal ShellProbeHit[] Hits=null!;
        internal IEnumerator<Action>? Actions;
        internal GameObject? Geometry,Targets;
        internal readonly Dictionary<int,GameObject> SseSupports=new();
        internal readonly Stopwatch Watch=Stopwatch.StartNew();
        internal double MaxSlice;
        internal double MaxAction;
        internal string Kind="",Slowest="";
    }
    internal string Contract=>Assets.Hash;
    internal AssetSignature Assets {
        get {
            if(assets!=null)return assets;
            static string Calibration(AssetCalibration c)=>string.Join(",",new[]{c.MinX,c.MaxX,c.MinY,c.MaxY,c.MinZ,c.MaxZ}.Select(BuildPolicy.Number));
            var stockRows=new List<string>{$"house={House};stock={StockCount};furniture={FurnitureMode}"};
            for(int n=0;n<stock.Length;n++)stockRows.Add($"stock:{n}:{stock[n].LayoutName}:{stock[n].targets.Length}:{stock[n].civilians.Length}:{stock[n].hostageTakers.Length}:{stock[n].doors.Length}");
            assets=AssetAgreement.Make(new[]{new[]{Calibration(wallTemplate.Calibration)},new[]{Calibration(frameTemplate.Calibration)},
                doors.Select(d=>d.SceneId.ToString("x16",System.Globalization.CultureInfo.InvariantCulture)+":"+Calibration(d.Frame.Calibration)+":"+string.Join(",",new[]{d.InFrame.x,d.InFrame.y,d.InFrame.z,d.FrameRotation.x,d.FrameRotation.y,d.FrameRotation.z,d.FrameRotation.w,d.HingeInRoot.x,d.HingeInRoot.y,d.HingeInRoot.z,d.LeafInRoot.x,d.LeafInRoot.y,d.LeafInRoot.z,d.Frame.ApertureCenter!.Value.x,d.Frame.ApertureCenter.Value.y,d.Frame.ApertureCenter.Value.z,d.Access.ForwardX,d.Access.ForwardZ,d.Access.HingeHigh?1:0,d.Access.Invert?1:0}.Select(BuildPolicy.Number))).ToArray(),stockRows.ToArray(),
                targetShapes.Select(s=>string.Join(",",new[]{s.MinX,s.MaxX,s.MinY,s.MaxY,s.MinZ,s.MaxZ}.Select(BuildPolicy.Number))).Concat(StandlessRoleContracts()).ToArray(),
                furniture.Shapes.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+":"+string.Join(",",new[]{p.Value.MinX,p.Value.MaxX,p.Value.MinY,p.Value.MaxY,p.Value.MinZ,p.Value.MaxZ}.Select(BuildPolicy.Number))).ToArray()});
            if(DiagnosticLog.Enabled) {
              CoopLog.Event("asset-contract",new{house=House,assets.Hash,parts=AssetAgreement.Names.Select((name,n)=>new{name,hash=assets.Parts[n]})});
              try {
                string folder=Path.Combine(BepInEx.Paths.BepInExRootPath,"../UserData/OperatorKillhouse");Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,$"assets-house{House}.json"),JsonSerializer.Serialize(new{version=Plugin.Version,house=House,assets.Hash,categories=AssetAgreement.Names.Select((name,n)=>new{name,hash=assets.Parts[n],rows=assets.Rows[n]})},new JsonSerializerOptions{WriteIndented=true}));
              }catch(Exception error){Plugin.Logger.LogWarning("Native asset report: "+error.GetBaseException().Message);}
            }
            return assets;
        }
    }
    internal string LayoutId(int index)=>index<StockCount?"stock:"+index:Items[index-StockCount].Id;
    internal bool IsPrepared(int index)=>index>=0 && index<StockCount || prepared.ContainsKey(index);
    internal string Digest(int index)=>index<StockCount?CoopProtocol.Digest(Contract+"\nstock:"+index):prepared.TryGetValue(index,out var plan)?plan.Digest:"";
    internal string? BuildError(int index)=>buildErrors.GetValueOrDefault(index);
    internal bool BeginStage(int index) {
        if(disposed || !Computer || index<0 || index>=Computer.shootHouseLayouts.Length)return false;
        if(IsPrepared(index)){if(index>=StockCount)cache.Touch(index);return true;}
        if(staging!=null)return staging.Index==index;
        buildErrors.Remove(index);
        try {
            var doc=LayoutFormat.Read(LayoutFormat.Write(Items[index-StockCount].Layout)).Layout!;
            doc=SnapLoadedFixtures(doc);
            RequireTargetAssets(doc);
            var mountIssues=FixtureMountIssues(doc);if(mountIssues.Length>0)throw new InvalidOperationException(mountIssues[0].Message);
            var contacts=shell.Contacts(doc,wallTemplate.Calibration.Thickness,frameTemplate.Calibration.Thickness,out var hits);
            var joints=WallJunctions.Make(doc,wallTemplate.Calibration.Thickness,frameTemplate.Calibration.Thickness,contacts);
            var targets=TargetFootprints(doc);
            var targetIssues=TargetPlacement.Check(doc,targets,joints.Build.Segments);if(targetIssues.Any(i=>i.Fatal))throw new InvalidOperationException(string.Join(" ",targetIssues.Select(i=>i.Message)));
            var shapes=furniture.Shapes;string assets=Contract;bool furnishings=FurnitureMode;
            staging=new(){Index=index,Doc=doc,Joints=joints,Contacts=contacts,Hits=hits,
                Task=Task.Run(()=>BuildPolicy.Plan(doc,joints.Build,shapes,targets,assets,furnishings))};
            return true;
        }catch(Exception error){buildErrors[index]=error.ToString();return false;}
    }
    internal void TickBuild(FrameBudget budget) {
        var job=staging;if(job==null || disposed)return;
        var slice=Stopwatch.StartNew();
        try {
            if(!job.Task.IsCompleted)return;
            if(job.Task.IsFaulted || job.Task.IsCanceled)throw new InvalidOperationException("Layout planning failed.",job.Task.Exception);
            job.Frames++;job.Actions??=BuildActions(job,job.Task.Result).GetEnumerator();
            while(budget.Take()) {
                if(!job.Actions.MoveNext()) {
                    job.MaxSlice=Math.Max(job.MaxSlice,slice.Elapsed.TotalMilliseconds);
                    prepared[job.Index]=job.Task.Result;cache.Touch(job.Index);staging=null;job.Actions.Dispose();
                    var reports=Stopwatch.StartNew();
                    if(job.Task.Result.Furniture is {} furnished)ReportFurniture(job.Index,job.Doc,furnished);
                    ReportGeometry(job.Index,job.Doc,job.Joints,job.Contacts,job.Hits);
                    double reportMs=reports.Elapsed.TotalMilliseconds;
                    CoopLog.Event("build-ready",new{house=House,index=job.Index,layout=job.Doc.Name,digest=job.Task.Result.Digest,planningMs=job.Task.Result.PlanningMs,cloningFrames=job.Frames,cloneSteps=job.Steps,maxCloneSliceMs=job.MaxSlice,maxCloneActionMs=job.MaxAction,slowestCloneAction=job.Slowest,reportMs,totalMs=job.Watch.Elapsed.TotalMilliseconds,retainedLayouts=prepared.Count});
                    break;
                }
                long began=Stopwatch.GetTimestamp();job.Actions.Current();job.Steps++;
                double actionMs=(Stopwatch.GetTimestamp()-began)*1000d/Stopwatch.Frequency;
                if(actionMs>job.MaxAction){job.MaxAction=actionMs;job.Slowest=job.Kind;}
            }
        }catch(Exception error){buildErrors[job.Index]=error.ToString();CancelStage();CoopLog.Event("build-failed",new{house=House,index=job.Index,error=error.ToString()});}
        finally {if(staging==job)job.MaxSlice=Math.Max(job.MaxSlice,slice.Elapsed.TotalMilliseconds);}
    }
    private IEnumerable<Action> BuildActions(StageJob job,PreparedLayout plan) {
        var root=roots[job.Index];
        job.Kind="containers";
        yield return ()=>{job.Geometry=new GameObject("Geometry");job.Geometry.transform.SetParent(root.transform,false);job.Targets=new GameObject("Targets");job.Targets.transform.SetParent(root.transform,false);};
        var groups=new List<GameObject>[] {new(),new(),new()};
        foreach(var target in job.Doc.AllTargets.OrderBy(t=>t.Cell.Row).ThenBy(t=>t.Cell.Column).ThenBy(t=>t.Id)) {
            job.Kind="target";
            yield return ()=> {
            var template=target.Style==TargetStyle.Standless?standlessRoleTemplates[(int)target.Role]??throw new InvalidOperationException("This standless target type is unavailable."):targetTemplates[(int)target.Role];
            if(template.GetComponentInChildren<NetworkIdentity>(true))throw new InvalidOperationException("A range target unexpectedly has network identity.");
            var clone=NativeDamageResources.Clone(template,job.Targets!.transform,target.Style==TargetStyle.Standless && target.Role!=TargetRole.Threat?NativeTargetVariants.RemoveStand:null);clone.name=template.name;
            var(x,z)=TargetPlacement.Position(target);
            if(target.Style==TargetStyle.Standless) {
                var position=TargetGeometry.Root(job.Doc,target,TargetGeometry.StandlessFor(target.Role));
                clone.transform.localScale=standlessRoleScales[(int)target.Role];
                clone.transform.position=anchor.TransformPoint(new Vector3(position.X,floor+position.Y,position.Z));
                clone.transform.rotation=anchor.rotation*Quaternion.Euler(target.Pitch,TargetPlacement.Yaw(job.Doc,target),target.Roll)*Quaternion.Euler(0,TargetGeometry.StandlessYaw(target.Role),0);
                foreach(var body in clone.GetComponentsInChildren<Rigidbody>(true))body.isKinematic=true;
                foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true)){renderer.lightmapIndex=-1;renderer.realtimeLightmapIndex=-1;}
            }else {
                clone.transform.position=anchor.TransformPoint(new Vector3(x,anchor.InverseTransformPoint(template.transform.position).y,z));
                clone.transform.rotation=anchor.rotation*Quaternion.Euler(0,TargetPlacement.Yaw(job.Doc,target),0);
            }
            clone.SetActive(true);groups[(int)target.Role].Add(clone);
            };
        }
        var builtFrames=new Dictionary<Edge,Transform>();int doorIndex=0;
        foreach(var segment in job.Joints.Build.Segments) {
            var template=segment.Kind==Barrier.Wall?wallTemplate:segment.Kind==Barrier.Door && doorIndex<doors.Count?doors[doorIndex].Frame:frameTemplate;
            if(segment.Kind==Barrier.Door)doorIndex++;
            job.Kind=segment.Kind==Barrier.Wall?"wall":"frame";
            yield return ()=>{var piece=template.Place(job.Geometry!.transform,anchor,segment,floor);if(segment.Kind==Barrier.Door)builtFrames.Add(segment.Edge,piece);};
        }
        foreach(var item in plan.Furniture?.Items??Array.Empty<FurnitureItem>()) {job.Kind="furniture:"+item.Asset;yield return ()=>{var node=furniture.Models[item.Asset].Place(item,job.Geometry!.transform,anchor,floor);CircuitFixtures.Add(node,roots[job.Index],job.Doc,item.FixedId);SseObjects.Add(node,roots[job.Index],job.Doc,item.FixedId);if(item.FixedId>0)job.SseSupports[item.FixedId]=node;};}
        if(job.Doc.Lights.Count>0){job.Kind="lighting budget";yield return ()=>CircuitFixtures.Apply(root,PowerCircuits.Initial(job.Doc));}
        if(job.Doc.Sse.Count>0){job.Kind="SSE supports";yield return ()=>SseObjects.AttachSupports(job.Doc,job.SseSupports);}
        if(job.Doc.Props.Any(p=>FixtureMount.Portable(p.Name) && p.SupportId>0)){job.Kind="light supports";yield return ()=>FixtureSupports.Attach(job.Doc,job.SseSupports);}
        job.Kind="target arrays";
        yield return ()=>{var entry=custom[job.Index];entry.targets=ToArray(groups[0]);entry.civilians=ToArray(groups[1]);entry.hostageTakers=ToArray(groups[2]);frames[job.Index]=builtFrames;};
    }
    private Footprint[] TargetFootprints(Layout doc)=>TargetGeometry.Ordered(doc).Select(t=>TargetGeometry.Footprint(doc,t,t.Style==TargetStyle.Standless?TargetGeometry.StandlessFor(t.Role):targetShapes[(int)t.Role])).ToArray();
    private void RequireTargetAssets(Layout doc){if(doc.AllTargets.Any(t=>t.Style==TargetStyle.Standless && !StandlessAvailableFor(t.Role)))throw new InvalidOperationException("Requested standless target measurements are not ready in this house.");}
    internal bool StandlessAvailable=>standlessTemplate && standlessShape!=null;
    private string? previewArchitectureKey;
    private IReadOnlyList<BarrierSegment>? previewArchitecture;
    internal IReadOnlyList<BarrierSegment> PreviewArchitecture(Layout doc) {
        string key=doc.Columns+"x"+doc.Rows+":"+WindowGeometry.Key(doc);
        if(previewArchitecture==null || previewArchitectureKey!=key) {
            var contacts=shell.Contacts(doc,wallTemplate.Calibration.Thickness,frameTemplate.Calibration.Thickness,out _);
            previewArchitecture=WallJunctions.Make(doc,wallTemplate.Calibration.Thickness,frameTemplate.Calibration.Thickness,contacts).Build.Segments;previewArchitectureKey=key;
        }
        return previewArchitecture;
    }
    private IReadOnlyList<Issue> TargetIssues(Layout doc,TargetRef? only) {
        if(doc.AllTargets.Any(t=>t.Style==TargetStyle.Standless && !StandlessAvailableFor(t.Role)))return new[]{new Issue("Requested standless target measurements are not ready in this house.",true)};
        return TargetPlacement.Check(doc,TargetFootprints(doc),PreviewArchitecture(doc),only).Concat(PropPlacement.Check(doc,furniture.Shapes,TargetFootprints(doc),PreviewArchitecture(doc))).ToArray();
    }
    internal Target AlignEditorTarget(Layout doc,Target target) {
        var(x,z)=TargetPlacement.Position(target);var volume=TargetGeometry.Volume(doc,target);
        var delta=PlacementClearance.Align(TargetGeometry.Footprint(doc,target,target.Style==TargetStyle.Standless?TargetGeometry.StandlessFor(target.Role):targetShapes[(int)target.Role]),PlacementClearance.WithShell(doc,PreviewArchitecture(doc)).Where(s=>WindowGeometry.AtHeight(s,volume.MinY,volume.MaxY)).ToArray(),margin:target.Style==TargetStyle.Standless?0:PlacementClearance.Margin);
        return TargetPlacement.At(x+delta.X-ConstructionPlan.Left,ConstructionPlan.North-z-delta.Z,target.Role,target.Yaw??(target.Facing is {} facing?90*(int)facing:null),target.Always,target.Id,target.Style,target.Height,target.Pitch,target.Roll);
    }
    internal IReadOnlyList<Issue> PreviewTargetIssues(Layout doc)=>TargetIssues(doc,null);
    internal IReadOnlyList<Issue> PreviewTargetEditIssues(Layout doc,TargetRef key)=>TargetIssues(doc,key).Concat(PreviewPropIssues(doc)).Distinct().ToArray();
    internal IReadOnlyDictionary<string,PropShape> EditorPropShapes=>furniture.Shapes;
    internal PlacedProp AlignFixture(Layout doc,PlacedProp prop,PropShape shape)=>FixtureMount.Nearest(prop,FixtureMount.InteriorCandidates(prop,shape,PreviewArchitecture(doc)).Concat(shell.FixtureCandidates(doc,prop,shape)));
    internal Layout SnapLoadedFixtures(Layout doc) {
        var architecture=PreviewArchitecture(doc);
        return FixtureLoad.Snap(doc,furniture.Shapes,(layout,prop,shape)=>FixtureMount.InteriorAttached(prop,shape,architecture) || shell.FixtureAttached(layout,prop,shape)?prop:FixtureMount.Nearest(prop,FixtureMount.InteriorCandidates(prop,shape,architecture,1.5f).Concat(shell.FixtureCandidates(layout,prop,shape,1.5f))));
    }
    private Issue[] FixtureMountIssues(Layout doc)=>doc.Props.Where(p=>FixtureMount.Mounted(p.Name)).Where(p=>PropPlacement.Shape(p,furniture.Shapes) is {} shape && !FixtureMount.InteriorAttached(p,shape,PreviewArchitecture(doc)) && !shell.FixtureAttached(doc,p,shape)).Select(p=>new Issue($"Fixture {p.Id}: snap the back plate to a solid wall at this height. Select it and use Snap to wall to repair an older placement.",true)).ToArray();
    internal FurnitureResult PreviewFurniture(Layout doc) {
        return FurniturePlanner.Make(doc,furniture.Shapes,TargetFootprints(doc),PreviewArchitecture(doc));
    }
    internal IReadOnlyList<Issue> PreviewPropIssues(Layout doc) {
        if(!doc.Props.Any(p=>p.Id>0))return Array.Empty<Issue>();
        if(!FurnitureMode)return new[]{new Issue("Enable furniture in Mod Controls, then reopen the range editor.",true)};
        var mounts=FixtureMountIssues(doc);if(mounts.Length>0)return mounts;
        var clearance=TargetIssues(doc,null);
        if(clearance.Any(i=>i.Fatal))return clearance;
        return FurniturePlanner.Make(doc,furniture.Shapes,TargetFootprints(doc),previewArchitecture).Unresolved.Select(s=>new Issue(s,true)).ToArray();
    }
    internal void TrimCache(int active) {foreach(int victim in cache.Trim(active))Retire(victim);}
    private void Retire(int index) {
        if(!roots.TryGetValue(index,out var root) || !root)return;
        foreach(string name in new[]{"Geometry","Targets"}) {var node=root.transform.Find(name);if(node){node.name="Retired "+name;DeferredRelease.Tree(node.gameObject);}}
        if(custom.TryGetValue(index,out var entry)){entry.targets=new Il2CppReferenceArray<GameObject>(0);entry.civilians=new Il2CppReferenceArray<GameObject>(0);entry.hostageTakers=new Il2CppReferenceArray<GameObject>(0);entry.doors=new Il2CppReferenceArray<DoorV2>(0);}
        frames.Remove(index);prepared.Remove(index);cache.Remove(index);
    }
    internal void CancelStage() {
        var job=staging;staging=null;if(job==null)return;
        // Observe failures even after cancellation; workers hold only copied,
        // managed planning data and never touch a retired Unity object.
        NativeAssetLifetime.Observe(job.Task);
        using var cleanup=new CleanupStack();
        cleanup.Add(()=>{if(job.Targets){job.Targets!.name="Retired Targets";DeferredRelease.Tree(job.Targets);}});
        cleanup.Add(()=>{if(job.Geometry){job.Geometry!.name="Retired Geometry";DeferredRelease.Tree(job.Geometry);}});
        cleanup.Add(()=>job.Actions?.Dispose());
    }
}
