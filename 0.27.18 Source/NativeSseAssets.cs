using System.Reflection;
using System.Text.Json;
using BepInEx;
using OperatorKillhouse.Core;
using Unity.Content;
using Unity.Loading;
using UnityEngine;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

// Only meshes/textures enter the native content loader. Scene/mission scripts,
// network identities and progression handlers are never instantiated.
internal static class NativeSseAssets
{
    // Unity content namespaces allow only alphanumeric names up to 16 chars.
    internal const string ContentName="KillhouseSse";
    private sealed record Ref(string File,ulong Id);
    private sealed record TextureSetting(string Name,Ref Texture,float[] Scale,float[] Offset);
    private sealed record MaterialSpec(string Key,string Name,string[] Keywords,Dictionary<string,float> Floats,Dictionary<string,float[]> Colors,TextureSetting[] Textures);
    private sealed record Node(string Name,int Parent,float[] Position,float[] Rotation,float[] Scale,Ref? Mesh,string[] Materials);
    private sealed record Model(string Name,Node[] Nodes);
    private sealed record Recipe(Model[] Models,MaterialSpec[] Materials);
    private static NativeSubsetPlan[] plans=Array.Empty<NativeSubsetPlan>();
    private static Task<NativeSubsetCache[]>? cache;
    private static ContentFile[] contents=Array.Empty<ContentFile>();
    private static readonly Dictionary<string,Material> materials=new();
    internal static readonly Dictionary<string,GameObject> Templates=new(StringComparer.Ordinal);
    internal static bool Ready {get;private set;}
    private static bool requested;
    private static readonly NativeLoadDeadline loadDeadline=new();
    internal static string Status {get;private set;}="Preparing SSE models…";
    private static T Resource<T>(string name){using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("OperatorKillhouse."+name)??throw new InvalidDataException("Missing SSE asset recipe.");return JsonSerializer.Deserialize<T>(stream)??throw new InvalidDataException("Invalid SSE asset recipe.");}
    internal static void Start() {
        Stop();Ready=false;requested=false;Status="Preparing SSE models…";
        plans=Resource<NativeSubsetPlan[]>("NativeSsePlans.json");contents=new ContentFile[plans.Length];
        string data=Application.dataPath,output=Path.Combine(Paths.CachePath,"OperatorKillhouse",Plugin.Version);
        var preparationPlans=plans;
        cache=Task.Run(()=>NativeAssetDiagnostics.Build("sse",()=>{
            var result=preparationPlans.Select((p,i)=>NativeAssetSubset.Build(data,output,p,$"killhouse-sse-{i}.assets")).ToArray();
            if(result.Any(c=>c.Relocated))DiagnosticLog.Info("SSE assets resolved from installed object tables; reviewed model and texture bytes verified.");
            return result;
        }));
    }
    private static T Get<T>(Ref resource) where T:UnityEngine.Object {
        int index=Array.FindIndex(plans,p=>p.SourceFile==resource.File);
        if(index<0)throw new InvalidDataException("Unknown SSE resource file.");
        return contents[index].GetObject(resource.Id)?.TryCast<T>()??throw new InvalidDataException("SSE resource did not resolve: "+resource.File+":"+resource.Id);
    }
    internal static unsafe void Tick() {
        if(Ready || cache==null)return;
        try {
            if(!cache.IsCompleted)return;
            if(!requested) {
                var built=cache.GetAwaiter().GetResult();
                if(sizeof(ContentFile)!=8 || plans.Length is <1 or >8)throw new InvalidDataException("Unexpected SSE content layout.");
                for(int i=0;i<plans.Length;i++) {
                    contents[i]=ContentLoadInterface.LoadContentFileAsync(ContentNamespace.GetOrCreateNamespace(ContentName),built[i].Assets,null,0,default,false);
                }
                requested=true;loadDeadline.Restart();Status="Loading SSE models…";return;
            }
            if(contents.Any(c=>c.LoadingStatus==LoadingStatus.InProgress)){if(loadDeadline.Expired)throw new TimeoutException("SSE content load timed out.");return;}
            if(contents.Any(c=>c.LoadingStatus!=LoadingStatus.Completed))throw new InvalidDataException("Unity rejected the reviewed SSE content.");
            var recipe=Resource<Recipe>("NativeSseRecipe.json");
            foreach(var spec in recipe.Materials)CreateMaterial(spec);
            foreach(var model in recipe.Models)Build(model);
            Ready=true;Status="SSE models ready.";NativeAssetDiagnostics.Ready("sse",Templates.Count);DiagnosticLog.Info("Seven SSE models ready; no mission scripts loaded.");
        }catch(Exception error){NativeAssetDiagnostics.Failed("sse",error);try{Stop();}catch(Exception cleanup){Plugin.Logger.LogWarning("SSE asset cleanup: "+cleanup);}Ready=true;Status="SSE models unavailable: "+error.GetBaseException().Message;Plugin.Logger.LogWarning(Status+" See UserData/OperatorKillhouse/asset-load-report.json.");}
    }
    private static void CreateMaterial(MaterialSpec spec) {
        var shader=Shader.Find("HDRP/Lit");if(!shader || !shader!.isSupported)throw new InvalidDataException("HDRP/Lit is unavailable for SSE.");
        var material=new Material(shader){name="Killhouse SSE [owned] "+spec.Name};materials.Add(spec.Key,material);
        if(!material.HasProperty("_BaseColorMap") || !spec.Textures.Any(t=>t.Name=="_BaseColorMap"))throw new InvalidDataException("SSE material has no compatible base-color texture: "+spec.Name);
        foreach(var p in spec.Floats)if(material.HasProperty(p.Key))material.SetFloat(p.Key,p.Value);
        foreach(var p in spec.Colors)if(material.HasProperty(p.Key))material.SetColor(p.Key,new(p.Value[0],p.Value[1],p.Value[2],p.Value[3]));
        foreach(var p in spec.Textures)if(material.HasProperty(p.Name)){material.SetTexture(p.Name,Get<Texture2D>(p.Texture));material.SetTextureScale(p.Name,new(p.Scale[0],p.Scale[1]));material.SetTextureOffset(p.Name,new(p.Offset[0],p.Offset[1]));}
        foreach(string keyword in spec.Keywords)material.EnableKeyword(keyword);
        material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        if(spec.Textures.Any(t=>t.Name=="_NormalMap"))material.EnableKeyword("_NORMALMAP");
        if(spec.Textures.Any(t=>t.Name=="_MaskMap"))material.EnableKeyword("_MASKMAP");
        var baseMap=material.GetTexture("_BaseColorMap");
        if(!baseMap || baseMap!.width<1 || baseMap.height<1)throw new InvalidDataException("SSE base-color texture did not load: "+spec.Name);
    }
    private static void Build(Model model) {
        var root=new GameObject(model.Name);root.SetActive(false);Object.DontDestroyOnLoad(root);
        Templates.Add(model.Name,root);
        var nodes=new List<GameObject>();
        foreach(var n in model.Nodes) {
            var go=new GameObject(n.Name);go.transform.SetParent(n.Parent<0?root.transform:nodes[n.Parent].transform,false);nodes.Add(go);
            go.transform.localPosition=new(n.Position[0],n.Position[1],n.Position[2]);go.transform.localRotation=new(n.Rotation[0],n.Rotation[1],n.Rotation[2],n.Rotation[3]);go.transform.localScale=new(n.Scale[0],n.Scale[1],n.Scale[2]);
            if(n.Mesh is {} reference) {
                var mesh=Get<Mesh>(reference);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(n.Materials.Select(k=>materials[k]).ToArray());
            }
        }
        var shape=NativeProp.Measure(root);if(!shape.Valid || shape.MaxX-shape.MinX>2 || shape.MaxZ-shape.MinZ>2 || shape.MaxY-shape.MinY>2)throw new InvalidDataException("SSE model exceeds placement bounds: "+model.Name);
        // A bounded solid box also works as a compound collider when the item
        // rests on a movable prop; no nonconvex mesh enters its rigidbody.
        var box=root.AddComponent<BoxCollider>();box.center=new((shape.MinX+shape.MaxX)*.5f,(shape.MinY+shape.MaxY)*.5f,(shape.MinZ+shape.MaxZ)*.5f);box.size=new(shape.MaxX-shape.MinX,shape.MaxY-shape.MinY,shape.MaxZ-shape.MinZ);
    }
    internal static void Stop() {
        loadDeadline.Clear();
        var templates=Templates.Values.ToArray();Templates.Clear();
        var ownedMaterials=materials.Values.ToArray();materials.Clear();
        var loaded=contents;contents=Array.Empty<ContentFile>();
        plans=Array.Empty<NativeSubsetPlan>();NativeAssetLifetime.Observe(cache);cache=null;Ready=true;
        using var cleanup=new CleanupStack();
        cleanup.Add(()=>NativeAssetLifetime.Release(loaded));
        foreach(var material in ownedMaterials.Reverse())cleanup.Add(()=>{if(material)Object.Destroy(material);});
        foreach(var template in templates.Reverse())cleanup.Add(()=>{if(template)Object.Destroy(template);});
    }
}
