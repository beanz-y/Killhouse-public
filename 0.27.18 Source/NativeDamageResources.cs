using UnityEngine;
using Object=UnityEngine.Object;
using Painter=Knife.RealBlood.Decals.CharacterDamagePainter;

namespace OperatorKillhouse;

// Native damage painters allocate Unity objects in InitAndSetResources but have
// no OnDestroy. Own only allocations belonging to explicitly cloned trees.
internal static class NativeDamageResources
{
    private sealed class Owner
    {
        internal readonly GameObject Root;
        internal readonly HashSet<IntPtr> Borrowed=new();
        internal readonly Dictionary<IntPtr,RenderTexture> Textures=new();
        internal readonly Dictionary<IntPtr,Material> Materials=new();
        internal readonly List<BulletDamageable> Bullets=new();
        internal readonly List<Painter> Painters=new();
        internal Owner(GameObject root,GameObject template) {
            Root=root;
            foreach(var renderer in template.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)if(material)Borrowed.Add(material.Pointer);
        }
    }
    private static readonly List<Owner> owners=new();
    private static readonly Dictionary<IntPtr,Owner> components=new();
    private static GameObject? staging;
    private static float nextSweep;
    internal static long CreatedTextures {get;private set;}
    internal static long ReleasedTextures {get;private set;}
    internal static long CreatedMaterials {get;private set;}
    internal static long ReleasedMaterials {get;private set;}
    internal static int OwnerCount=>owners.Count;
    internal static int TextureCount=>owners.Sum(o=>o.Textures.Count);
    internal static int MaterialCount=>owners.Sum(o=>o.Materials.Count);

    // Clone under an inactive parent so ownership is installed BEFORE Awake,
    // including CharacterDamagePainter's synchronous initialization on AI.
    private static GameObject Clone(GameObject template,Transform? parent,Vector3? position,Quaternion? rotation,Action<GameObject>? prepare=null) {
        if(!staging){staging=new GameObject("Killhouse inactive clone staging");staging.SetActive(false);}
        var clone=Object.Instantiate(template,staging!.transform);
        Owner? owner=null;
        try {
            clone.SetActive(false);clone.transform.SetParent(parent,false);
            if(position is {} p)clone.transform.position=p;
            if(rotation is {} q)clone.transform.rotation=q;
            prepare?.Invoke(clone);
            owner=new(clone,template);owners.Add(owner);
            foreach(var damage in clone.GetComponentsInChildren<BulletDamageable>(true)){owner.Bullets.Add(damage);components.Add(damage.Pointer,owner);}
            foreach(var painter in clone.GetComponentsInChildren<Painter>(true)){owner.Painters.Add(painter);components.Add(painter.Pointer,owner);}
            clone.SetActive(template.activeSelf);CaptureMaterials(owner);
            return clone;
        } catch {
            if(owner!=null){Release(owner);owners.Remove(owner);}
            Object.Destroy(clone);throw;
        }
    }
    internal static GameObject Clone(GameObject template,Transform parent)=>Clone(template,parent,null,null);
    internal static GameObject Clone(GameObject template,Transform parent,Action<GameObject>? prepare)=>Clone(template,parent,null,null,prepare);
    internal static GameObject Clone(GameObject template,Vector3 position,Quaternion rotation)=>Clone(template,null,position,rotation);

    private static void CaptureMaterials(Owner owner) {
        if(!owner.Root)return;
        // sharedMaterials observes instances made by native Renderer.material
        // without creating any further instances itself.
        foreach(var renderer in owner.Root.GetComponentsInChildren<Renderer>(true))
            foreach(var material in renderer.sharedMaterials)
                if(material && !owner.Borrowed.Contains(material.Pointer) && owner.Materials.TryAdd(material.Pointer,material))CreatedMaterials++;
    }
    private static void CaptureTextures(Owner owner,IEnumerable<RenderTexture>? textures) {
        if(textures!=null)foreach(var texture in textures)
            if(texture && owner.Textures.TryAdd(texture.Pointer,texture))CreatedTextures++;
        CaptureMaterials(owner);
    }
    private static void ReleaseTexture(Owner owner,RenderTexture texture) {
        if(!owner.Textures.Remove(texture.Pointer))return;
        if(texture){texture.Release();Object.Destroy(texture);}
        ReleasedTextures++;
    }
    internal static void BeforeBullet(BulletDamageable __instance) {
        if(!components.TryGetValue(__instance.Pointer,out var owner))return;
        // Clone fields can contain borrowed template references. Never let the
        // native reinitializer DestroyImmediate those shared textures.
        if(__instance.renderTextures is {} textures)foreach(var texture in textures)if(texture!=null)ReleaseTexture(owner,texture);
        __instance.renderTextures=null!;
    }
    internal static Exception? AfterBullet(BulletDamageable __instance,Exception? __exception) {
        if(components.TryGetValue(__instance.Pointer,out var owner))CaptureTextures(owner,__instance.renderTextures);
        return __exception;
    }
    internal static void BeforePainter(Painter __instance) {
        if(!components.TryGetValue(__instance.Pointer,out var owner))return;
        if(__instance.renderTextures is {} textures)foreach(var texture in textures)if(texture!=null)ReleaseTexture(owner,texture);
        __instance.renderTextures=null!;
    }
    internal static Exception? AfterPainter(Painter __instance,Exception? __exception) {
        if(components.TryGetValue(__instance.Pointer,out var owner))CaptureTextures(owner,__instance.renderTextures);
        return __exception;
    }
    private static void Release(Owner owner) {
        foreach(var damage in owner.Bullets){components.Remove(damage.Pointer);if(damage){damage.CancelInvoke("InitAndSetResources");damage.renderTextures=null!;}}
        foreach(var painter in owner.Painters){components.Remove(painter.Pointer);if(painter)painter.renderTextures=null!;}
        foreach(var texture in owner.Textures.Values.ToArray())ReleaseTexture(owner,texture);
        foreach(var material in owner.Materials.Values){if(material)Object.Destroy(material);ReleasedMaterials++;}
        owner.Materials.Clear();
    }
    internal static void Tick() {
        if(Time.unscaledTime<nextSweep)return;nextSweep=Time.unscaledTime+.5f;
        for(int n=owners.Count-1;n>=0;n--)if(!owners[n].Root){Release(owners[n]);owners.RemoveAt(n);}
    }
    internal static void Dispose() {
        foreach(var owner in owners)Release(owner);
        owners.Clear();components.Clear();nextSweep=0;
        if(staging)Object.Destroy(staging);staging=null;
    }
}
