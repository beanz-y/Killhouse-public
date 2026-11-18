using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed class NativeProp
{
    internal GameObject Template { get; }
    internal PropShape Shape { get; }
    private readonly bool snapshot;
    private readonly string key;
    private readonly Quaternion tilt;
    private readonly Vector3 scale;
    internal NativeProp(GameObject template,string? key=null,bool snapshot=false)
    {
        Template=template;this.key=key??PropNames.Key(template.name);this.snapshot=snapshot;
        scale=template.transform.lossyScale;
        var yaw=Quaternion.Euler(0,template.transform.eulerAngles.y,0);
        tilt=Quaternion.Inverse(yaw)*template.transform.rotation;
        Shape=Measure(template);
        if(!Shape.Valid)throw new InvalidOperationException("No usable native prop geometry for "+template.name);
    }
    internal static PropShape Measure(GameObject model,bool preserveTilt=true,float canonicalYaw=0,Func<Transform,bool>? include=null)
    {
        var points=new List<Vector3>();
        var inverseYaw=Quaternion.Inverse(Quaternion.Euler(0,model.transform.eulerAngles.y,0));
        var basis=Matrix4x4.TRS(Vector3.zero,Quaternion.Euler(0,canonicalYaw,0)*(preserveTilt?inverseYaw*model.transform.rotation:Quaternion.identity),model.transform.lossyScale);
        void Bounds(Transform transform,Vector3 center,Vector3 extents) {
            var relative=basis*NativeSpace.Relative(transform,model.transform);
            for(int bits=0;bits<8;bits++) {
                var sign=new Vector3((bits&1)==0?-1:1,(bits&2)==0?-1:1,(bits&4)==0?-1:1);
                points.Add(relative.MultiplyPoint3x4(center+Vector3.Scale(extents,sign)));
            }
        }
        foreach(var collider in model.GetComponentsInChildren<Collider>(true)) {
            if(include!=null && !include(collider.transform))continue;
            if(collider.isTrigger)continue;
            var box=collider.TryCast<BoxCollider>();var mesh=collider.TryCast<MeshCollider>();
            if(box is not null && box)Bounds(box.transform,box.center,box.size*.5f);
            else if(mesh is not null && mesh && mesh.sharedMesh)Bounds(mesh.transform,mesh.sharedMesh.bounds.center,mesh.sharedMesh.bounds.extents);
        }
        // Render bounds include bed frames and sandbag mesh outlines; checking
        // only colliders either missed these details or silently omitted props.
        foreach(var mesh in model.GetComponentsInChildren<MeshFilter>(true))
            if(mesh.sharedMesh && (include==null || include(mesh.transform)))Bounds(mesh.transform,mesh.sharedMesh.bounds.center,mesh.sharedMesh.bounds.extents);
        if(points.Count==0)throw new InvalidOperationException("No measurable mesh or collider: "+model.name);
        return new(points.Min(p=>p.x),points.Max(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.y),points.Min(p=>p.z),points.Max(p=>p.z));
    }
    internal GameObject Place(FurnitureItem item,Transform parent,Transform anchor,float floor)
    {
        if(!snapshot && Template.GetComponentInChildren<NetworkIdentity>(true))throw new InvalidOperationException("Networked objects cannot be used as furniture templates.");
        var clone=snapshot?NativePropSnapshot.Build(Template,parent,PowerCircuits.IsLight(key)):NativeDamageResources.Clone(Template,parent);clone.SetActive(false);
        clone.name=key+" [Operator Killhouse]";
        clone.transform.localScale=scale;
        clone.transform.rotation=anchor.rotation*Quaternion.Euler(0,item.Pose.Yaw,0)*tilt;
        clone.transform.position=anchor.TransformPoint(new Vector3(item.Pose.X,floor+item.Height-Shape.MinY+.01f,item.Pose.Z));
        foreach(var body in clone.GetComponentsInChildren<Rigidbody>(true))body.isKinematic=true;
        if(!snapshot)PropPhysics.Configure(clone,key);
        foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true)) {renderer.lightmapIndex=-1;renderer.realtimeLightmapIndex=-1;}
        clone.SetActive(true);return clone;
    }
}

internal sealed class NativeFurnitureCatalog
{
    private static readonly Dictionary<int,NativeFurnitureCatalog> sceneCatalogs=new();
    internal static NativeFurnitureCatalog For(RangeComputer computer,Transform anchor) {
        int key=computer.gameObject.scene.handle;
        if(!sceneCatalogs.TryGetValue(key,out var catalog))sceneCatalogs[key]=catalog=new(computer,anchor);
        return catalog;
    }
    internal static void Clear()=>sceneCatalogs.Clear();
    internal readonly Dictionary<string,NativeProp> Models=new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyDictionary<string,PropShape> Shapes=>Models.ToDictionary(p=>p.Key,p=>p.Value.Shape,StringComparer.OrdinalIgnoreCase);
    internal NativeFurnitureCatalog(RangeComputer computer,Transform anchor)
    {
        var roots=new List<Transform>();
        if(anchor.parent)roots.Add(anchor.parent);
        foreach(string name in new[]{"KillHouse 01","KillHouse 02"}) {
            var root=GameObject.Find(name);
            if(root && root.scene==computer.gameObject.scene && !roots.Any(t=>t.GetInstanceID()==root.transform.GetInstanceID()))roots.Add(root.transform);
        }
        var candidates=new Dictionary<int,GameObject>();
        foreach(var root in roots)foreach(var group in root.GetComponentsInChildren<Transform>(true)) {
            if(!group.name.Equals("Props",StringComparison.Ordinal))continue;
            for(int n=0;n<group.childCount;n++) {
                var prop=group.GetChild(n).gameObject;
                if(!prop.GetComponentInChildren<NetworkIdentity>(true))candidates.TryAdd(prop.GetInstanceID(),prop);
            }
        }
        foreach(var prop in candidates.Values.OrderBy(p=>Path(p.transform),StringComparer.Ordinal)) {
            NativeProp model;
            try{model=new NativeProp(prop);}catch(InvalidOperationException){continue;}
            Models.TryAdd(prop.name.Replace("(Clone)","",StringComparison.Ordinal).Trim(),model);
            Models.TryAdd(PropNames.Key(prop.name),model);
        }
        NativeExpandedProps.Add(computer,Models);
        DiagnosticLog.Info($"Native furniture catalog: {computer.name}; {Models.Values.Select(p=>p.Template.GetInstanceID()).Distinct().Count()} safe measured models; Basement_table available={Models.ContainsKey("Basement_table")}.");
    }
    private static string Path(Transform transform) {
        var names=new List<string>();while(transform) {names.Add(transform.name);transform=transform.parent;}
        names.Reverse();return string.Join("/",names);
    }
}
