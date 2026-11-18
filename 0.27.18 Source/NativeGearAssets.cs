using System.Reflection;
using System.Collections.Concurrent;
using System.Text.Json;
using BepInEx;
using Il2CppInterop.Runtime;
using OperatorKillhouse.Core;
using UnityEngine;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

internal static class NativeGearAssets
{
    private static Task<string>? cache;
    private static AssetBundleCreateRequest? request;
    private static AssetBundleRequest? assetRequest;
    private static AssetBundle? bundle;
    private static GameObject? template;
    private static int generation;
    private static readonly NativeLoadDeadline loadDeadline=new();
    private static Application.LogCallback? unityLog;
    private static readonly ConcurrentQueue<string> loadMessages=new();
    private static int messageCount;
    private static void CaptureLoadMessages() {
        while(loadMessages.TryDequeue(out _)){}messageCount=0;
        unityLog=(Action<string,string,LogType>)OnUnityLog;
        Application.add_logMessageReceivedThreaded(unityLog);
    }
    private static void OnUnityLog(string message,string stack,LogType type) {
        // Loader errors may arrive from worker threads. Queue bounded strings;
        // never log from this callback and recursively enter Unity logging.
        if(type==LogType.Log || Interlocked.Increment(ref messageCount)>8)return;
        loadMessages.Enqueue((message??"")[..Math.Min(message?.Length??0,1500)]);
    }
    private static void ReleaseLoadMessages() {
        if(unityLog==null)return;
        Application.remove_logMessageReceivedThreaded(unityLog);unityLog=null;
    }
    private static string LoaderDetails()=>string.Join(" | ",loadMessages.ToArray());
    internal static string Status {get;private set;}="Preparing native customization screen…";
    internal static bool Ready=>template;
    internal static void Start() {
        Stop();Status="Preparing native customization screen…";
        using var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("OperatorKillhouse.NativeGearPlan.json")??throw new InvalidDataException("Missing native gear recipe.");
        var plan=JsonSerializer.Deserialize<NativeUiPlan>(input)??throw new InvalidDataException("Invalid native gear recipe.");
        if(plan.SerializedFiles!=1 || plan.ScriptTypes!=33)throw new InvalidDataException("Native customization requires its complete local script table.");
        string data=Application.dataPath,output=Path.Combine(Paths.CachePath,"OperatorKillhouse",Plugin.Version);
        cache=Task.Run(()=>NativeAssetDiagnostics.Build("gear",()=>{
            string built=NativeUiCache.Build(data,output,plan);
            if(plan.ObjectSources is {Length:>0})DiagnosticLog.Info($"Native customization sources verified by identity: {plan.ObjectSources.Length} objects; reviewed bundle SHA256 retained.");
            return built;
        }));
    }
    internal static void Tick() {
        if(template || cache==null)return;
        try {
            if(!cache.IsCompleted)return;
            if(request==null){
                string path=cache.GetAwaiter().GetResult();CaptureLoadMessages();
                int owner=generation;var pending=AssetBundle.LoadFromFileAsync(path);request=pending;loadDeadline.Restart();
                pending.add_completed((Action<AsyncOperation>)(_=>{if(owner!=generation && pending.assetBundle)pending.assetBundle.Unload(true);}));
                Status="Loading native customization screen…";return;
            }
            if(!request.isDone){if(loadDeadline.Expired)throw new TimeoutException("Native customization load timed out.");return;}
            bundle=request.assetBundle;if(!bundle)throw new InvalidDataException("Unity rejected the native customization cache.");
            // The synchronous LoadAsset binding is recovered managed code and
            // contains a broken IL2CPP ReadOnlySpan facade. This asynchronous
            // entry invokes the actual AOT method, including string marshaling.
            if(assetRequest==null){assetRequest=bundle!.LoadAssetAsync("assets/killhouse/native-gear.prefab",Il2CppType.Of<GameObject>());loadDeadline.Restart();return;}
            if(!assetRequest.isDone){if(loadDeadline.Expired)throw new TimeoutException("Native customization prefab load timed out.");return;}
            var loaded=assetRequest.asset;
            if(!loaded)throw new InvalidDataException("Unity returned no native customization prefab. Bundle assets: "+string.Join(", ",bundle.GetAllAssetNames()));
            template=loaded!.TryCast<GameObject>();
            if(!template)throw new InvalidDataException("The native customization asset is not a GameObject: "+loaded.name);
            if(!template!.GetComponent<CharacterCustomisationController>())throw new InvalidDataException("The native customization prefab loaded without CharacterCustomisationController. Root components: "+string.Join(", ",template.GetComponents<Component>().Select(c=>c?c.ToString():"missing script")));
            if(template.GetComponentInChildren<Mirror.NetworkIdentity>(true))throw new InvalidDataException("Unexpected network object in native customization UI.");
            ReleaseLoadMessages();Status="Native customization screen ready.";NativeAssetDiagnostics.Ready("gear",1);DiagnosticLog.Info(Status);
        }catch(Exception error){NativeAssetDiagnostics.Failed("gear",error);string details=LoaderDetails();Stop();Status="Native customization unavailable: "+error.GetBaseException().Message;Plugin.Logger.LogWarning(Status+" See UserData/OperatorKillhouse/asset-load-report.json.\n"+error+(details.Length>0?"\nUnity loader: "+details:""));}
    }
    internal static GameObject Create(Transform parent,Vector3 position,Quaternion rotation) {
        if(!Ready)throw new InvalidOperationException(Status);
        var instance=Object.Instantiate(template!);instance.name="Outdoor native customization [Operator Killhouse]";
        instance.transform.SetParent(parent,false);instance.transform.SetPositionAndRotation(position,rotation);return instance;
    }
    internal static void Stop() {
        loadDeadline.Clear();
        generation++;
        NativeAssetLifetime.Observe(cache);cache=null;template=null;
        var pending=request;var pendingAsset=assetRequest;var owned=bundle;
        bundle=null;request=null;assetRequest=null;
        try{ReleaseLoadMessages();}
        finally {
            if(pending!=null && pending.isDone && !owned)owned=pending.assetBundle;
            if(owned) {
                if(pendingAsset!=null && !pendingAsset.isDone)pendingAsset.add_completed((Action<AsyncOperation>)(_=>{if(owned)owned!.Unload(true);}));
                else owned!.Unload(true);
            }
        }
    }
}
