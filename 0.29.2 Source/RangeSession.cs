using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;

internal sealed partial class RangeSession : IDisposable
{
    internal RangeComputer Computer { get; }
    internal int InstanceKey { get; }
    internal int House { get; }
    internal string RangeName { get; }
    internal string SceneName { get; }
    internal int StockCount => stock.Length;
    private readonly List<CatalogItem> items=new();
    private readonly MapNameIndex mapNames=new();
    internal IReadOnlyList<CatalogItem> Items=>items;
    internal bool Listed(int index)=>index>=0 && (index<StockCount || index-StockCount<items.Count && mapNames.IsCurrent(items[index-StockCount]));
    private readonly Il2CppReferenceArray<ShootHouseLayout> stock;
    private readonly Transform anchor;
    private readonly Dictionary<int,GameObject> roots=new();
    private readonly Dictionary<int,ShootHouseLayout> custom=new();
    private readonly NativeFurnitureCatalog furniture;
    private readonly PropShape[] targetShapes;
    private readonly Dictionary<int,Dictionary<Edge,Transform>> frames=new();
    private readonly List<DoorPlacement> doors=new();
    private readonly Dictionary<int,DoorV2[]> stockDoorComponents=new();
    private readonly Dictionary<int,(Transform Parent,Transform[] Children)> doorParents=new();
    private readonly GameObject[] targetTemplates;
    private readonly GameObject? standlessTemplate;
    private readonly PropShape? standlessShape;
    private readonly Vector3 standlessScale;
    private readonly CleanupStack ownership=new();
    private readonly float floor;
    private readonly NativePiece wallTemplate,frameTemplate;
    private readonly NativeShell shell;
    private bool disposed;

    private sealed class DoorPlacement
    {
        internal readonly DoorV2 Door;
        internal readonly Transform Root,Parent;
        internal readonly Vector3 Position,Scale;
        internal readonly Quaternion Rotation;
        internal readonly bool Active;
        internal readonly ulong SceneId;
        internal readonly NativePiece Frame;
        internal readonly Vector3 InFrame;
        internal readonly Quaternion FrameRotation;
        internal readonly Vector3 HingeInRoot;
        internal readonly Vector3 LeafInRoot,CenterCorrection;
        internal readonly DoorAccessProfile Access;
        private readonly bool nativeInvert;
        private DoorHandleV2 front,back;
        private bool frontFlag,backFlag;
        private bool? customInvert;
        private readonly bool nativeStartLocked,nativeLocked;
        private readonly float nativeLockChance;
        private bool? customLocked;
        private bool repairReady;
        internal bool RepairReady=>repairReady;
        internal DoorPlacement(DoorV2 door,Transform root,NativePiece frame,Transform stockRoot) {
            Door=door; Root=root; Parent=root.parent; Position=root.localPosition; Rotation=root.localRotation; Scale=root.localScale; Active=root.gameObject.activeSelf;SceneId=DoorSceneId(door);
            Frame=frame;
            var relative=NativeSpace.Relative(frame.Template,stockRoot).inverse*NativeSpace.Relative(root,stockRoot);
            FrameRotation=relative.rotation;
            if(!door.PivotTransform || !door.HandleFront || !door.HandleBack)throw new InvalidOperationException("Native door hinge/handles are unavailable.");
            var leaf=door.PivotTransform.Find("Door Model/Door");var slab=leaf?leaf.GetComponent<BoxCollider>():null;
            var savedSize=door.ColliderStartSize;
            bool baseline=slab is not null && slab && door.DoorHitBox && door.DoorHitBox.Pointer==slab.Pointer && DoorColliderProfile.UseBaseline(true,savedSize.x,savedSize.y,savedSize.z);
            var leafSize=baseline?savedSize:slab is not null && slab?slab.size:Vector3.zero;
            if(slab is null || !slab || slab.isTrigger || !DoorColliderProfile.Valid(leafSize.x,leafSize.y,leafSize.z) || (Scale-Vector3.one).sqrMagnitude>.001f)
                throw new InvalidOperationException($"Native closed door leaf needs a new measurement: sceneId={SceneId:x16}; rootScale={Scale}; slab={(slab is not null && slab?slab.size.ToString():"missing")}; trigger={(slab is not null && slab?slab.isTrigger.ToString():"unknown")}. Range setup remains blocked; this is a door asset/readiness issue, not a missing client mod.");
            if(baseline && (slab.size-savedSize).sqrMagnitude>.0001f)CoopLog.Event("door-calibration-baseline",new{sceneId=SceneId.ToString("x16"),live=slab.size.ToString(),original=savedSize.ToString()});
            // Capture the CLOSED slab, even when attaching to an already-open
            // stock door. Handles and damage fragments are not the leaf bounds.
            var pivot=door.PivotTransform;
            // Native repair returns the rigidbody pivot to local zero/identity;
            // a breached door may currently have both translation and rotation.
            var neutral=NativeSpace.Relative(pivot.parent,root)*Matrix4x4.TRS(Vector3.zero,Quaternion.identity,pivot.localScale);
            HingeInRoot=neutral.MultiplyPoint3x4(Vector3.zero);
            LeafInRoot=(neutral*NativeSpace.Relative(slab.transform,pivot)).MultiplyPoint3x4(slab.center);
            var aperture=frame.ApertureCenter??throw new InvalidOperationException("Native doorway opening is unavailable.");
            CenterCorrection=aperture-relative.MultiplyPoint3x4(LeafInRoot);
            InFrame=relative.MultiplyPoint3x4(Vector3.zero)+CenterCorrection;
            nativeInvert=door.Invert;front=door.HandleFront;back=door.HandleBack;CaptureHandles();
            var horizontal=Quaternion.Euler(0,frame.Calibration.Yaw(true),0);
            var closedForward=horizontal*(FrameRotation*neutral.MultiplyVector(Vector3.forward).normalized);
            var closedHinge=horizontal*(InFrame+FrameRotation*HingeInRoot-aperture);
            Access=new(closedForward.x,closedForward.z,closedHinge.x>0,nativeInvert);
            nativeStartLocked=door.StartLocked;nativeLockChance=door.StartLockedChance;nativeLocked=door.CurrentlyLocked;
            repairReady=door.destroyedDoorInitialPos!=null && door.destroyedDoorInitialRot!=null;
        }
        private void CaptureHandles(){front=Door.HandleFront;back=Door.HandleBack;frontFlag=front.IsFrontHandle;backFlag=back.IsFrontHandle;}
        private void Configuration(bool invert) {
            bool reverse=invert!=nativeInvert;Door.Invert=invert;Door.HandleFront=reverse?back:front;Door.HandleBack=reverse?front:back;
            front.IsFrontHandle=reverse?!frontFlag:frontFlag;back.IsFrontHandle=reverse?!backFlag:backFlag;
        }
        internal void BeforeAwake(){Configuration(nativeInvert);}
        internal void AfterAwake(){repairReady=true;CaptureHandles();Configuration(customInvert??nativeInvert);}
        internal void Override(bool invert){customInvert=invert;Configuration(invert);}
        internal void OverrideLock(bool locked){customLocked=locked;Door.StartLocked=locked;Door.StartLockedChance=0;ApplyLock(false);}
        internal void ApplyLock(bool repair) {
            if(customLocked is not {} locked)return;
            // RangeComputer.ResetDoor only clears predicted rotation. Restore
            // the native intact latch/hinges before reapplying the authored lock.
            // UpdateLayout can reset an inactive door before its first Awake.
            // Its native damage arrays/repair baselines do not exist yet.
            if(repair && repairReady)Door.UserCode_RPC_RepairDoor();
            if(NetworkServer.active)Door.Server_SetDoorLocked(locked);
        }
        internal void Restore() {
            if(!Root || !Parent)return;
            bool hadLock=customLocked.HasValue;customLocked=null;customInvert=null;Configuration(nativeInvert);
            Door.StartLocked=nativeStartLocked;Door.StartLockedChance=nativeLockChance;
            if(hadLock && NetworkServer.active)Door.Server_SetDoorLocked(nativeLocked);
            Root.SetParent(Parent,false);Root.localPosition=Position;Root.localRotation=Rotation;Root.localScale=Scale;Root.gameObject.SetActive(Active);Sync();
        }
        internal void Sync() { foreach(var body in Root.GetComponentsInChildren<Rigidbody>(true)) { body.position=body.transform.position; body.rotation=body.transform.rotation; if(!body.isKinematic) { body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero; } } }
    }

    internal RangeSession(RangeComputer computer,CatalogSnapshot catalog)
    {
        Computer=computer; InstanceKey=computer.GetInstanceID();RangeName=computer.name;SceneName=computer.gameObject.scene.name;House=RangeName.TrimEnd().EndsWith('2')?2:1; stock=computer.shootHouseLayouts;
        if(stock==null || stock.Length==0 || !stock[0].layout || !stock[0].layout.transform.parent) throw new InvalidOperationException("Range has no usable stock layout root.");
        anchor=stock[0].layout.transform.parent;
        if((anchor.lossyScale-Vector3.one).sqrMagnitude>.01f) throw new InvalidOperationException("Scaled range roots need calibration.");
        int house=House;
        foreach(var item in catalog.Items)mapNames.Publish(item);
        if(NetworkServer.active)items.AddRange(NamedMaps.Visible(catalog.Items).Where(i=>(i.Layout.Houses&house)!=0));
        var templates=new GameObject?[3];GameObject? standless=null; var seenDoors=new HashSet<int>();
        NativePiece? nativeWall=null,nativeFrame=null;
        for(int stockIndex=0;stockIndex<stock.Length;stockIndex++) {
            var layout=stock[stockIndex];
            var arrays=new[]{layout.targets,layout.civilians,layout.hostageTakers};
            for(int kind=0;kind<arrays.Length;kind++) if(arrays[kind]!=null && arrays[kind].Length>0 && arrays[kind][0]) {
                templates[kind]??=arrays[kind].FirstOrDefault(t=>t && !t.name.StartsWith("Shoot NoStand",StringComparison.Ordinal));
            }
            if(!layout.layout) continue;
            var pieces=layout.layout.GetComponentsInChildren<Transform>(true);
            standless??=pieces.Where(t=>PropNames.Key(t.name)=="Shoot NoStand" && t.GetComponentsInChildren<OnBulletHitRangeTarget>(true).Length>0 && !t.GetComponentInChildren<NetworkIdentity>(true)).OrderBy(t=>t.name,StringComparer.Ordinal).Select(t=>t.gameObject).FirstOrDefault();
            var nativeFrames=pieces.Where(t=>t.name.StartsWith("Reinforced Door Way",StringComparison.Ordinal) && (t.lossyScale-Vector3.one).sqrMagnitude<.001f).Select(t=>new NativePiece(t)).ToArray();
            nativeFrame??=nativeFrames.FirstOrDefault();
            if(nativeWall==null) {
                var candidate=pieces.FirstOrDefault(t=>t.name.StartsWith("Reinforced Wall",StringComparison.Ordinal) && (t.lossyScale-Vector3.one).sqrMagnitude<.001f);
                if(candidate is not null && candidate)nativeWall=new NativePiece(candidate);
            }
            var hierarchyDoors=layout.layout.GetComponentsInChildren<DoorV2>(true).ToArray();stockDoorComponents[stockIndex]=hierarchyDoors;
            var nativeDoors=AssetAgreement.OrderDoors(hierarchyDoors,DoorSceneId);
            foreach(var door in nativeDoors) {
                if(!door) continue;
                Transform moving=door.transform;
                while(moving.parent && moving.parent!=layout.layout.transform) {
                    if(moving.parent.parent==layout.layout.transform && moving.parent.name.Equals("Doors",StringComparison.OrdinalIgnoreCase)) break;
                    moving=moving.parent;
                }
                if(moving==layout.layout.transform || !seenDoors.Add(moving.GetInstanceID())) continue;
                // Capture a door in the coordinate system of its own stock
                // doorway, including the hinge offset and authored orientation.
                var matching=nativeFrames.OrderBy(f=> {
                    var delta=f.StockFoot-moving.position;delta.y=0;return delta.sqrMagnitude;
                }).FirstOrDefault();
                if(matching==null || Vector3.Distance(matching.StockFoot,moving.position)>1.5f) {
                    Plugin.Logger.LogWarning($"{door.name}: no matching stock doorway; leaving this door in its stock layout.");continue;
                }
                doors.Add(new(door,moving,matching,layout.layout.transform));
                int parentKey=moving.parent.GetInstanceID();
                if(!doorParents.ContainsKey(parentKey))doorParents.Add(parentKey,(moving.parent,Enumerable.Range(0,moving.parent.childCount).Select(moving.parent.GetChild).ToArray()));
            }
        }
        RepairStockDoors("attach");
        wallTemplate=nativeWall??throw new InvalidOperationException("Stock reinforced wall assets are unavailable.");
        frameTemplate=nativeFrame??throw new InvalidOperationException("Stock reinforced doorway assets are unavailable.");
        floor=anchor.InverseTransformPoint(wallTemplate.StockFoot).y;
        shell=new NativeShell(anchor,floor);
        DiagnosticLog.Info($"Native range geometry: {computer.name}; {doors.Count} doors calibrated to stock frames; floor {floor:F3}.");
        if(templates.Any(t=>!t)) throw new InvalidOperationException("All three native range target types are required.");
        targetTemplates=templates.Select(t=>t!).ToArray();
        targetShapes=targetTemplates.Select(t=>NativeProp.Measure(t)).ToArray();
        if(standless) {
            var shape=NativeProp.Measure(standless!,false,TargetGeometry.NativeYaw);var expected=TargetGeometry.Standless;
            if(!shape.Valid || new[]{shape.MinX-expected.MinX,shape.MaxX-expected.MaxX,shape.MinY-expected.MinY,shape.MaxY-expected.MaxY,shape.MinZ-expected.MinZ,shape.MaxZ-expected.MaxZ}.Any(d=>Math.Abs(d)>.002f))Plugin.Logger.LogWarning("Standless target geometry changed; standless placement is unavailable until calibrated.");
            else {standlessTemplate=standless;standlessShape=shape;standlessScale=standless!.transform.lossyScale;}
        }
        InitializeStandlessRoles();
        furniture=NativeFurnitureCatalog.For(computer,anchor);
        ownership.Add(()=> { if(Computer) Computer.shootHouseLayouts=stock; });
        try {
            var expanded=new Il2CppReferenceArray<ShootHouseLayout>(stock.Length+Items.Count);
            for(int n=0;n<stock.Length;n++) expanded[n]=stock[n];
            for(int i=0;i<Items.Count;i++) {
                int index=stock.Length+i; Layout doc=Items[i].Layout;
                var root=new GameObject("Operator Killhouse: "+doc.Name); root.SetActive(false); root.transform.SetParent(anchor,false);
                roots.Add(index,root); ownership.Add(()=>DeferredRelease.Tree(root));
                var entry=new ShootHouseLayout { LayoutName=doc.Name,layout=root,targets=new Il2CppReferenceArray<GameObject>(0),civilians=new Il2CppReferenceArray<GameObject>(0),hostageTakers=new Il2CppReferenceArray<GameObject>(0),doors=new Il2CppReferenceArray<DoorV2>(0) };
                custom.Add(index,entry); expanded[index]=entry;
            }
            computer.shootHouseLayouts=expanded;
        } catch { ownership.Dispose(); disposed=true; throw; }
    }
    private static Il2CppReferenceArray<GameObject> ToArray(List<GameObject> items) { var result=new Il2CppReferenceArray<GameObject>(items.Count); for(int n=0;n<items.Count;n++) result[n]=items[n]; return result; }
    internal int AppendAuthored(CatalogItem item) {
        if(!NetworkServer.active || disposed || !Computer)throw new InvalidOperationException("Only an active host can add an authored revision.");
        if((item.Layout.Houses&House)==0){mapNames.Publish(item);return -1;}
        int existing=items.FindIndex(i=>i.Id==item.Id);if(existing>=0){mapNames.Publish(item);return StockCount+existing;}
        int index=StockCount+items.Count;if(index>1027)throw new InvalidOperationException("The native map slot limit has been reached.");
        var previous=Computer.shootHouseLayouts;var expanded=new Il2CppReferenceArray<ShootHouseLayout>(previous.Length+1);
        for(int n=0;n<previous.Length;n++)expanded[n]=previous[n];
        var root=new GameObject("Operator Killhouse: "+item.Layout.Name);root.SetActive(false);root.transform.SetParent(anchor,false);
        ownership.Add(()=>DeferredRelease.Tree(root));
        var entry=new ShootHouseLayout{LayoutName=item.Layout.Name,layout=root,targets=new Il2CppReferenceArray<GameObject>(0),civilians=new Il2CppReferenceArray<GameObject>(0),hostageTakers=new Il2CppReferenceArray<GameObject>(0),doors=new Il2CppReferenceArray<DoorV2>(0)};
        roots.Add(index,root);custom.Add(index,entry);items.Add(item with{Layout=LayoutDraft.Copy(item.Layout)});expanded[index]=entry;
        Computer.shootHouseLayouts=expanded;
        mapNames.Publish(item);
        CoopLog.Event("authored-revision-added",new{house=House,index,layout=item.Layout.Name,item.Id,previousActive=Computer.activeLayoutIndex});return index;
    }
    internal void InstallShared(int index,Layout doc) {
        if(NetworkServer.active || index<StockCount || index>1027 || (doc.Houses&House)==0)throw new InvalidDataException("Invalid shared-map slot.");
        if(index-StockCount<items.Count && items[index-StockCount].Id==doc.Identity){mapNames.Publish(items[index-StockCount]);return;}
        if(index-StockCount<items.Count && items[index-StockCount].Id.Length>0)throw new InvalidDataException("Host changed a shared-map slot without renewing its session.");
        var previous=Computer.shootHouseLayouts;int length=Math.Max(previous.Length,index+1);
        var expanded=new Il2CppReferenceArray<ShootHouseLayout>(length);
        for(int n=0;n<previous.Length;n++)expanded[n]=previous[n];
        while(items.Count<=index-StockCount) {
            int slot=StockCount+items.Count;var blank=LayoutDraft.Blank();blank.Name="Waiting for host map";
            items.Add(new("","session placeholder",blank));
            var root=new GameObject("Operator Killhouse: waiting for host");root.SetActive(false);root.transform.SetParent(anchor,false);
            roots.Add(slot,root);ownership.Add(()=>DeferredRelease.Tree(root));
            var entry=new ShootHouseLayout{LayoutName=blank.Name,layout=root,targets=new Il2CppReferenceArray<GameObject>(0),civilians=new Il2CppReferenceArray<GameObject>(0),hostageTakers=new Il2CppReferenceArray<GameObject>(0),doors=new Il2CppReferenceArray<DoorV2>(0)};
            custom.Add(slot,entry);expanded[slot]=entry;
        }
        items[index-StockCount]=new(doc.Identity,"host-shared session",LayoutDraft.Copy(doc));
        mapNames.Publish(items[index-StockCount]);
        custom[index].LayoutName=doc.Name;roots[index].name="Operator Killhouse: "+doc.Name;Computer.shootHouseLayouts=expanded;
        CoopLog.Event("shared-map-installed",new{house=House,index,layout=doc.Name,id=doc.Identity,savedToDisk=false});
    }
    private static ulong DoorSceneId(DoorV2 door) {
        // This can run from RangeComputer.Awake, before Mirror has populated a
        // NetworkBehaviour's cached netIdentity. The serialized component exists.
        var identity=door.GetComponent<NetworkIdentity>();return identity?identity.sceneId:0;
    }
    internal void Prepare(int index)
    {
        ClearEncounter();
        RestoreDoors();
        RepairStockDoors("prepare");
        if(!custom.TryGetValue(index,out var gameLayout)) return;
        if(!IsPrepared(index))throw new InvalidOperationException("Layout geometry has not finished preparing.");
        GameObject root=roots[index];Layout doc=Items[index-stock.Length].Layout;
        var openings=ConstructionPlan.Make(doc).Doors;
        if(openings.Skip(doors.Count).Any(d=>doc.DoorOptions.ContainsKey(d.Edge) || doc.DoorLocks.ContainsKey(d.Edge)))throw new InvalidOperationException("Not enough native doors for the authored swing/lock settings.");
        var assigned=new List<DoorV2>();
        var fits=new List<object>();float maxFitError=0;
        for(int n=0;n<Math.Min(openings.Count,doors.Count);n++) {
            var saved=doors[n]; var opening=openings[n];
            if(!saved.Root || !saved.Door || !frames[index].TryGetValue(opening.Edge,out var frame) || !frame) continue;
            saved.Door.ResetDoor();
            saved.Door.currentRotation=0;saved.Door.PredictedRotation=0;saved.Door.Velocity=0;
            saved.Door.PivotTransform.localRotation=Quaternion.identity;
            saved.Root.SetParent(root.transform,true);
            saved.Root.localScale=saved.Scale;
            saved.Root.SetPositionAndRotation(frame.TransformPoint(saved.InFrame),frame.rotation*saved.FrameRotation);
            Vector3 center=frame.TransformPoint(saved.Frame.ApertureCenter!.Value);
            if(doc.DoorOptions.TryGetValue(opening.Edge,out var options)) {
                var hinge=saved.Root.TransformPoint(saved.HingeInRoot);var along=anchor.TransformDirection(opening.Edge.Horizontal?Vector3.right:Vector3.back);
                float offset=Vector3.Dot(hinge-center,along);
                if(Math.Abs(offset)<.1f)throw new InvalidOperationException("Native door hinge cannot be matched to the requested side.");
                if((offset>0)!=options.HingeHigh) {
                    var flip=Quaternion.AngleAxis(180,anchor.up);
                    saved.Root.SetPositionAndRotation(center+flip*(saved.Root.position-center),flip*saved.Root.rotation);
                }
                saved.Override(options.NativeInvert(opening.Edge.Horizontal));
            }
            if(doc.DoorLocks.TryGetValue(opening.Edge,out bool locked))saved.OverrideLock(locked);
            float error=Vector3.Distance(saved.Root.TransformPoint(saved.LeafInRoot),center);maxFitError=Math.Max(maxFitError,error);
            if(error>DoorwayProfile.FitTolerance)throw new InvalidOperationException("Closed door leaf did not center in its doorway.");
            var access=saved.Access.ForEdge(opening.Edge.Horizontal,doc.DoorOptions.GetValueOrDefault(opening.Edge));
            fits.Add(new{edge=opening.Edge,sceneId=saved.SceneId.ToString("x16"),centerErrorMetres=error,stockCorrectionMetres=saved.CenterCorrection.magnitude,lockOverride=doc.DoorLocks.TryGetValue(opening.Edge,out var lockChoice)?(bool?)lockChoice:null,lockedFrom=access.LockedFrom.ToString(),opensFrom=access.OpensFrom.ToString()});
            saved.Root.gameObject.SetActive(true); saved.Sync(); assigned.Add(saved.Door);
        }
        var doorArray=new Il2CppReferenceArray<DoorV2>(assigned.Count); for(int n=0;n<assigned.Count;n++) doorArray[n]=assigned[n]; gameLayout.doors=doorArray;
        if(openings.Count>assigned.Count) Plugin.Logger.LogWarning($"{doc.Name}: {openings.Count-assigned.Count} requested doors use open frames (insufficient stock doors).");
        CoopLog.Event("door-fit",new{house=House,index,layout=doc.Name,doors=assigned.Count,maxCenterErrorMetres=maxFitError,toleranceMetres=DoorwayProfile.FitTolerance,measurements=fits});
        Physics.SyncTransforms();
        cache.Touch(index);
    }
    private void ReportGeometry(int index,Layout doc,JointPlan plan,Dictionary<WallEnd,float> contacts,ShellProbeHit[] probeHits)
    {
        if(!DiagnosticLog.Enabled)return;
        var endpoints=ConstructionPlan.Make(doc).Segments.SelectMany(s=>new[]{new WallEnd(s.Edge,false),new WallEnd(s.Edge,true)})
            .Where(e=>WallJunctions.Perimeter(doc,e)).ToArray();
        var unmatched=endpoints.Where(e=>!contacts.ContainsKey(e)).ToArray();
        int faceHits=probeHits.Count(h=>h.Offset!=0);
        DiagnosticLog.Info($"Geometry fit: house={House}; index={index}; layout={doc.Name}; {plan.AdjustedWalls} solid walls fitted; {plan.Fillers} native end fills; {contacts.Count}/{endpoints.Length} shell contacts; {faceHits} panel-face contacts; wallThickness={wallTemplate.Calibration.Thickness:F4}; frameThickness={frameTemplate.Calibration.Thickness:F4}. Ends with no shell within the panel footprint remain open.");
        try {
            string data=System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../UserData/OperatorKillhouse");System.IO.Directory.CreateDirectory(data);
            System.IO.File.WriteAllText(System.IO.Path.Combine(data,"geometry-house"+House+".json"),System.Text.Json.JsonSerializer.Serialize(new {
                version=Plugin.Version,house=House,range=RangeName,index,layout=doc.Name,status="prepared; selection completion is recorded in selections.jsonl",
                wall=wallTemplate.Calibration,frame=frameTemplate.Calibration,plan.AdjustedWalls,plan.Fillers,
                shellContacts=contacts.Select(p=>new{end=p.Key,coordinate=p.Value}).ToArray(),shellProbeHits=probeHits,unmatchedShellEnds=unmatched,segments=plan.Build.Segments
            },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }catch(Exception error){Plugin.Logger.LogWarning("Geometry report: "+error.GetBaseException().Message);}
    }
    private void ReportFurniture(int index,Layout doc,FurnitureResult plan)
    {
        foreach(string issue in plan.Unresolved)Plugin.Logger.LogWarning($"house={House}; layout={doc.Name}: {issue}");
        if(!DiagnosticLog.Enabled)return;
        foreach(string note in plan.CompactThemes)DiagnosticLog.Info($"house={House}; layout={doc.Name}: compact furnishing: {note}");
        DiagnosticLog.Info($"Furniture {doc.Name}: house={House}; index={index}; range={RangeName}; {plan.Items.Count} props; {plan.Required-plan.Unresolved.Count}/{plan.Required} explicit/theme requests; {plan.Adjusted} placement adjustments; {plan.AccentOmissions} optional accents omitted.");
        try {
            string data=System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"../UserData/OperatorKillhouse");System.IO.Directory.CreateDirectory(data);
            string house=Computer.name.TrimEnd().EndsWith('2')?"2":"1";
            System.IO.File.WriteAllText(System.IO.Path.Combine(data,"furniture-house"+house+".json"),System.Text.Json.JsonSerializer.Serialize(new {version=Plugin.Version,house=House,range=RangeName,instance=InstanceKey,layout=doc.Name,status="prepared; selection completion is recorded in selections.jsonl",coordinateSystem="House-local X/Z metres; yaw degrees; height above native floor",legacyPropOrigin=new{X=ConstructionPlan.PropLeft,Z=ConstructionPlan.PropSouth},sourceProps=doc.Props,plan},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        } catch(Exception error) {Plugin.Logger.LogWarning("Furniture report: "+error.GetBaseException().Message);}
    }
    internal void RepairStockDoors(string reason)
    {
        foreach(var(index,hierarchy) in stockDoorComponents) {
            var layout=stock[index];var configured=layout.doors;
            var repaired=ReferenceRepair.Reconcile(configured,hierarchy,d=>d is not null && d,d=>d.GetInstanceID());
            bool changed=configured==null || configured.Length!=repaired.Length;
            if(!changed)for(int n=0;n<repaired.Length;n++)if(!configured![n] || configured[n].Pointer!=repaired[n].Pointer) {changed=true;break;}
            if(!changed)continue;
            int missing=configured==null?0:configured.Count(d=>!d);
            var array=new Il2CppReferenceArray<DoorV2>(repaired.Length);for(int n=0;n<repaired.Length;n++)array[n]=repaired[n];layout.doors=array;
            DiagnosticLog.Info($"stock door repair: house={House}; range={RangeName}; index={index}; layout={layout.LayoutName}; reason={reason}; missing={missing}; liveDoors={repaired.Length}.");
        }
    }
    private void RestoreDoors() {
        foreach(var door in doors)door.Restore();
        // Restore the complete original sibling order after all reparenting.
        // A partial door-only reorder can shift previously restored siblings.
        foreach(var parent in doorParents.Values)if(parent.Parent)for(int n=0;n<parent.Children.Length;n++) {
            var child=parent.Children[n];if(child && child.parent==parent.Parent && child.GetSiblingIndex()!=n)child.SetSiblingIndex(n);
        }
        if(Computer)Physics.SyncTransforms();
    }
    internal void DoorAwake(DoorV2 door,bool after) {foreach(var saved in doors)if(saved.Door.Pointer==door.Pointer){if(after)saved.AfterAwake();else saved.BeforeAwake();break;}}
    internal IReadOnlyDictionary<Edge,DoorAccess> PreviewDoorAccess(Layout doc) {
        var result=new Dictionary<Edge,DoorAccess>();var openings=ConstructionPlan.Make(doc).Doors;
        for(int n=0;n<Math.Min(openings.Count,doors.Count);n++) {
            var edge=openings[n].Edge;result.Add(edge,doors[n].Access.ForEdge(edge.Horizontal,doc.DoorOptions.GetValueOrDefault(edge)));
        }
        return result;
    }
    internal void DoorReset(DoorV2 door) {foreach(var saved in doors)if(saved.Door.Pointer==door.Pointer){saved.ApplyLock(true);break;}}
    internal void DoorRepaired(DoorV2 door) {foreach(var saved in doors)if(saved.Door.Pointer==door.Pointer){saved.ApplyLock(false);break;}}
}
