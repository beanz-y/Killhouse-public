using System.Reflection;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;

namespace OperatorKillhouse;

// HarmonyX removes patch callbacks but this IL2CPP bridge replaces them with a
// fresh pass-through native detour. That still changes native exception routing.
// Release that bridge as well when the last owner leaves a method. The inspected
// loader contract is pinned because the bridge exposes no public release API.
internal static class TrainingPatches
{
    private static Type? bridgeType;
    private static FieldInfo? nativeDetour,modifiedInfo,originalInfo;
    private static object? harmonyLock;
    private static readonly object validationLock=new();
    private static readonly Dictionary<string,RemovalBatch> pending=new();

    internal static void Validate() {
        lock(validationLock) {
            if(bridgeType!=null)return;
            Assembly Checked(string name,string module) {
                var assembly=Assembly.Load(name);
                if(assembly.ManifestModule.ModuleVersionId!=Guid.Parse(module))
                    throw new NotSupportedException($"Training-only hooks require the inspected BepInEx 6.0.0-be.785 loader; {name} differs. Killhouse hooks were not enabled.");
                return assembly;
            }
            Checked("0Harmony","c085dc8e-8638-4478-a7a5-48c799c9b5f8");
            Checked("Il2CppInterop.Runtime","ab9a457d-353f-4e53-b665-3c12b9f380d3");
            Checked("BepInEx.Unity.IL2CPP","bbc7391c-010a-4f1b-8966-22a54b549a1d");
            var assembly=Checked("Il2CppInterop.HarmonySupport","b5213676-8bc8-428e-8873-7cbd76235857");
            var type=assembly.GetType("Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher",true)!;
            FieldInfo Field(string name,Type expected) {
                var field=type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
                if(field==null || field.FieldType!=expected || field.IsInitOnly)
                    throw new NotSupportedException("Unsupported IL2CPP hook-release field: "+name);
                return field;
            }
            var detour=Field("nativeDetour",typeof(IDetour));
            var modified=Field("modifiedNativeMethodInfo",typeof(INativeMethodInfoStruct));
            var original=Field("originalNativeMethodInfo",typeof(INativeMethodInfoStruct));
            var gate=typeof(PatchProcessor).GetField("locker",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null)
                ?? throw new NotSupportedException("Harmony patch synchronization is unavailable.");
            nativeDetour=detour;modifiedInfo=modified;originalInfo=original;harmonyLock=gate;
            bridgeType=type;
        }
    }

    internal static void Remove(Harmony harmony) {
        Validate();
        // Use the same monitor as Harmony's patch operations. A foreign owner
        // cannot arrive between our empty-owner check and native detour release.
        lock(harmonyLock!) {
            if(!pending.TryGetValue(harmony.Id,out var batch))pending.Add(harmony.Id,batch=new());
            batch.Run(harmony.Id,harmony.GetPatchedMethods(),
                method=>harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id),
                Harmony.GetPatchInfo,method=>RestoreNative(PatchManager.GetMethodPatcher(method)));
            pending.Remove(harmony.Id);
        }
    }

    internal sealed class RemovalBatch {
        private readonly HashSet<MethodBase> methods=new();
        internal int Pending=>methods.Count;
        internal void Run(string owner,IEnumerable<MethodBase> owned,Action<MethodBase> unpatch,
            Func<MethodBase,Patches?> inspect,Action<MethodBase> release) {
            methods.UnionWith(owned);
            var failures=new List<Exception>();
            foreach(var method in methods.ToArray()) {
                try {
                    // A previous attempt may have removed our callbacks before
                    // native disposal failed. Keep that method pending and avoid
                    // making yet another bridge on its cleanup retry.
                    var remaining=inspect(method);
                    if(remaining?.Owners.Contains(owner)==true){unpatch(method);remaining=inspect(method);}
                    if(remaining?.Owners.Contains(owner)==true)
                        throw new InvalidOperationException("Killhouse hook ownership remains on "+method);
                    if(!HasPatches(remaining))release(method);
                    methods.Remove(method);
                } catch(Exception error) {
                    failures.Add(new InvalidOperationException("Could not completely release training hook "+method,error));
                }
            }
            if(failures.Count>0)throw new AggregateException("Training hook removal was incomplete.",failures);
        }
    }

    internal static bool HasPatches(Patches? patches)=>patches!=null &&
        (patches.Prefixes.Count>0 || patches.Postfixes.Count>0 || patches.Transpilers.Count>0 || patches.Finalizers.Count>0 || patches.ILManipulators.Count>0);

    // Internal for the loader-contract tests. The fixtures use an uninitialized
    // actual patcher with managed stand-ins; no native engine is executed.
    internal static void RestoreNative(object patcher) {
        Validate();
        if(patcher.GetType()!=bridgeType)
            throw new NotSupportedException("Unexpected training hook patcher: "+patcher.GetType().FullName);
        var modified=modifiedInfo!.GetValue(patcher) as INativeMethodInfoStruct
            ?? throw new InvalidOperationException("Modified native method metadata is unavailable.");
        var original=originalInfo!.GetValue(patcher) as INativeMethodInfoStruct
            ?? throw new InvalidOperationException("Original native method metadata is unavailable.");
        IntPtr entry=original.MethodPointer;
        if(entry==IntPtr.Zero)throw new InvalidOperationException("Original native entry point is unavailable.");
        var detour=nativeDetour!.GetValue(patcher) as IDetour;
        if(detour==null) {
            if(modified.MethodPointer!=entry)throw new InvalidOperationException("Released native method metadata still points at a trampoline.");
            return;
        }
        // Match DetourTo's inspected disposal order: retarget copied metadata
        // BEFORE freeing its old trampoline. Retain the cached patcher/metadata
        // so a later training entry can install normally through Harmony again.
        modified.MethodPointer=entry;
        detour.Dispose();
        nativeDetour.SetValue(patcher,null);
    }
}
