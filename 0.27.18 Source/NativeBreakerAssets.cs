using System.Reflection;
using System.Text.Json;
using BepInEx;
using OperatorKillhouse.Core;
using Unity.Content;
using Unity.Loading;
using UnityEngine;

namespace OperatorKillhouse;

// Load a script-free subset of the owner's installed assets, never a game scene.
internal static class NativeBreakerAssets
{
    private sealed record Node(string Name,int Parent,float[] Position,float[] Rotation,float[] Scale,ulong Mesh,ulong Material,float[]? Box);
    private sealed record MaterialPlan(string[] Keywords,Dictionary<string,float> Floats,Dictionary<string,float[]> Colors,Dictionary<string,float[]> TextureTransforms,int RenderQueue,bool Instancing,bool DoubleSided,int LightmapFlags,Dictionary<string,string> Tags,string[] DisabledPasses);
    private static Task<NativeSubsetCache[]>? cache;
    private static NativeSubsetPlan[] plans=Array.Empty<NativeSubsetPlan>();
    private static readonly ContentFile[] contents=new ContentFile[2];
    private static ContentFile content=>contents[0];
    private static Material? ownedMaterial;
    private static bool requested,finished;
    private static readonly NativeLoadDeadline loadDeadline=new();
    internal static bool Ready=>finished;
    internal static string Status {get;private set;}="Preparing native breaker assets.";
    internal static GameObject? Template {get;private set;}
    internal static AnimationClip? Animation {get;private set;}
    internal static RuntimeAnimatorController? Controller {get;private set;}
    private static T Resource<T>(string name) {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("OperatorKillhouse."+name)??throw new InvalidDataException("Missing asset locator.");
        return JsonSerializer.Deserialize<T>(stream)??throw new InvalidDataException("Invalid asset locator.");
    }
    internal static void Start() {
        Stop();
        finished=false;requested=false;Status="Preparing native breaker assets.";
        plans=new[]{Resource<NativeSubsetPlan>("NativeBreakerPlan.json"),Resource<NativeSubsetPlan>("NativeBreakerAnimationPlan.json")};
        string data=Application.dataPath,output=Path.Combine(Paths.CachePath,"OperatorKillhouse",Plugin.Version);
        var preparationPlans=plans;
        cache=Task.Run(()=>NativeAssetDiagnostics.Build("breaker",()=>preparationPlans.Select((p,i)=>NativeAssetSubset.Build(data,output,p,i==0?"killhouse-breaker-v2.assets":"killhouse-breaker-motion.assets")).ToArray()));
    }
    internal static unsafe void Tick() {
        if(finished || cache==null)return;
        try {
            if(!cache.IsCompleted)return;
            if(!requested) {
                var result=cache.GetAwaiter().GetResult();
                // The caches contain no external PPtrs or dependency entries.
                // Keep the working native pointer/count entry; do not call the
                // generated NativeArray overload with its missing interop type.
                for(int file=0;file<plans.Length;file++) {
                contents[file]=ContentLoadInterface.LoadContentFileAsync(ContentNamespace.GetOrCreateNamespace("KillhouseAssets"),Path.GetFullPath(result[file].Assets),null,0,default,false);
                }
                Status="Loading native breaker model.";
                requested=true;loadDeadline.Restart();return;
            }
            if(contents.Any(c=>c.LoadingStatus==LoadingStatus.InProgress)) {
                if(loadDeadline.Expired)throw new TimeoutException("Native content load did not complete.");
                return;
            }
            if(contents.Any(c=>c.LoadingStatus!=LoadingStatus.Completed))throw new InvalidDataException("Unity rejected the reviewed breaker content file.");
            Animation=contents[1].GetObject(24)?.TryCast<AnimationClip>();
            Controller=contents[1].GetObject(52)?.TryCast<RuntimeAnimatorController>();
            if(!Animation || !Controller)throw new InvalidDataException("Native breaker animation/controller did not resolve.");
            Template=Build();finished=true;Status="Breaker box ready.";NativeAssetDiagnostics.Ready("breaker",4);
            DiagnosticLog.Info("Native breaker visuals ready: four meshes; no scene, network identity or native gameplay scripts loaded.");
        }catch(Exception error) {
            NativeAssetDiagnostics.Failed("breaker",error);
            try{Stop();}catch(Exception cleanup){Plugin.Logger.LogWarning("Native breaker cleanup: "+cleanup);}
            finished=true;Status=error.GetBaseException().Message;
            Plugin.Logger.LogWarning("Native breaker unavailable; other props remain usable: "+error.GetBaseException().Message);
        }
    }
    private static GameObject Build() {
        var nodes=Resource<Node[]>("NativeBreakerRecipe.json");var built=new List<GameObject>();
        try {
            // Preserve reviewed native material values without importing its
            // external shader dependency from globalgamemanagers.assets.
            var shader=Shader.Find("HDRP/Lit");
            if(!shader || !shader!.isSupported)throw new InvalidDataException("HDRP/Lit is unavailable for the breaker.");
            var spec=Resource<MaterialPlan>("NativeBreakerMaterial.json");
            ownedMaterial=new Material(shader!){name="Killhouse breaker [owned]",renderQueue=spec.RenderQueue,enableInstancing=spec.Instancing,doubleSidedGI=spec.DoubleSided,globalIlluminationFlags=(MaterialGlobalIlluminationFlags)spec.LightmapFlags};
            foreach(var p in spec.Floats)if(ownedMaterial.HasProperty(p.Key))ownedMaterial.SetFloat(p.Key,p.Value);
            foreach(var p in spec.Colors)if(ownedMaterial.HasProperty(p.Key))ownedMaterial.SetColor(p.Key,new(p.Value[0],p.Value[1],p.Value[2],p.Value[3]));
            foreach(string keyword in spec.Keywords)ownedMaterial.EnableKeyword(keyword);
            foreach(var tag in spec.Tags)ownedMaterial.SetOverrideTag(tag.Key,tag.Value);
            foreach(string pass in spec.DisabledPasses)ownedMaterial.SetShaderPassEnabled(pass,false);
            foreach(var (property,id) in new[]{("_BaseColorMap",151ul),("_MaskMap",179ul),("_NormalMap",195ul),("_EmissiveColorMap",237ul)}) {
                var texture=content.GetObject(id)?.TryCast<Texture2D>();
                if(!texture)throw new InvalidDataException("Native breaker texture did not resolve: "+property);
                ownedMaterial.SetTexture(property,texture);
                var transform=spec.TextureTransforms[property];ownedMaterial.SetTextureScale(property,new(transform[0],transform[1]));ownedMaterial.SetTextureOffset(property,new(transform[2],transform[3]));
            }
            foreach(var n in nodes) {
                var go=new GameObject(n.Name);go.SetActive(false);built.Add(go);
                if(n.Parent>=0)go.transform.SetParent(built[n.Parent].transform,false);
                go.transform.localPosition=new(n.Position[0],n.Position[1],n.Position[2]);
                go.transform.localRotation=new(n.Rotation[0],n.Rotation[1],n.Rotation[2],n.Rotation[3]);
                go.transform.localScale=new(n.Scale[0],n.Scale[1],n.Scale[2]);
                if(n.Mesh!=0) {
                    var mesh=content.GetObject(n.Mesh)?.TryCast<Mesh>();
                    if(!mesh)throw new InvalidDataException("Native breaker mesh did not resolve.");
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=ownedMaterial;
                }
                if(n.Box is {} b){var box=go.AddComponent<BoxCollider>();box.center=new(b[0],b[1],b[2]);box.size=new(b[3],b[4],b[5]);}
                if(n.Parent>=0)go.SetActive(true);
            }
            // The native front points -Z. The editor's facing arrow is +Z.
            var root=new GameObject("Power box");root.SetActive(false);
            built[0].transform.SetParent(root.transform,false);built[0].transform.localRotation=Quaternion.Euler(0,180,0);built[0].SetActive(true);
            UnityEngine.Object.DontDestroyOnLoad(root);return root;
        }catch{if(built.Count>0)UnityEngine.Object.Destroy(built[0]);throw;}
    }
    internal static void Stop() {
        loadDeadline.Clear();
        var template=Template;Template=null;finished=true;NativeAssetLifetime.Observe(cache);cache=null;plans=Array.Empty<NativeSubsetPlan>();
        using var cleanup=new CleanupStack();
        cleanup.Add(ReleaseContent);cleanup.Add(ReleaseMaterial);
        cleanup.Add(()=>{if(template){template!.SetActive(false);UnityEngine.Object.Destroy(template);}});
    }
    private static void ReleaseMaterial(){var material=ownedMaterial;ownedMaterial=null;if(material)UnityEngine.Object.Destroy(material);}
    private static void ReleaseContent(){Animation=null;Controller=null;NativeAssetLifetime.Release(contents);}
}
