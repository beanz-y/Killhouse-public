using OperatorKillhouse.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OperatorKillhouse;

internal static class CircuitFixtures
{
    internal sealed class Fixture
    {
        internal readonly GameObject Node,Root;
        internal readonly int Id,Circuit;
        private readonly PlacedLight? settings;
        private readonly float circuitBrightness;
        private readonly bool independent;
        private readonly bool fixedLight;
        private readonly Light[] lights;
        private readonly HDAdditionalLightData[] lightData;
        private readonly int[] lightSources;
        private readonly Transform[] sources;
        private readonly bool[] cachedShadows;
        internal readonly int ShadowViews;
        private readonly List<(Material Material,Color On,int Source)> materials=new();
        private readonly NativeBreakerMotion? motion;
        private readonly NativeBreakerInteraction.Control? control;
        private readonly List<IntPtr> shotHandlers=new();
        private readonly List<IntPtr> shotColliders=new();
        private IntPtr lastBullet;
        private float lastShotAt=-1;
        private bool? applied;
        private bool wasActive;
        private int appliedDamage=-1;
        private int appliedShadowQuality=-1;
        private int appliedShadowResolution=-1;
        private bool? appliedShadowCache;
        private float motionTickAt=float.NaN;
        private bool motionTickActive;
        private bool disposed;
        internal bool Breaker=>settings==null;
        internal int SourceCount=>sources.Length;
        internal int EnabledEmitters=>lights.Count(l=>l && l.enabled);
        internal int CachedEmitters=>cachedShadows.Count(c=>c);
        internal int CacheFallbackEmitters {get{if(appliedShadowCache!=true || !fixedLight || appliedShadowQuality<=0)return 0;int count=0;for(int i=0;i<lights.Length;i++)if(!cachedShadows[i] && lights[i] && lights[i].enabled)count++;return count;}}
        internal int SourceOf(Transform target) {for(int i=0;i<sources.Length;i++)if(target.IsChildOf(sources[i]))return i;return -1;}
        internal Fixture(GameObject node,GameObject root,int id,PlacedLight? light,PlacedBreaker? breaker,float brightness,string circuitName,bool independent=false,bool fixedLight=false) {
            Node=node;Root=root;Id=id;settings=light;circuitBrightness=brightness;Circuit=light?.Circuit??breaker!.Circuit;
            this.independent=independent;
            this.fixedLight=fixedLight;
            var heads=node.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.gameObject.name is "LitLampA2" or "LitLampA2 (1)" or "LitLampA2 (2)" or "LitLampA2 (3)").OrderBy(m=>m.gameObject.name,StringComparer.Ordinal).Select(m=>m.transform).ToArray();
            sources=heads.Length==4?heads:new[]{node.transform};
            lights=node.GetComponentsInChildren<Light>(true).ToArray();
            try {
            if(light!=null && lights.Length==0) {
                var created=new List<Light>();
                foreach(var source in sources) {
                var emitter=new GameObject("Killhouse fixture emitter");emitter.transform.SetParent(source,false);
                var renderers=source.gameObject.GetComponentsInChildren<Renderer>(true);var bounds=renderers.First().bounds;
                foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                var forward=source.forward;float front=Math.Abs(forward.x)*bounds.extents.x+Math.Abs(forward.y)*bounds.extents.y+Math.Abs(forward.z)*bounds.extents.z;
                emitter.transform.position=new Vector3(bounds.center.x,bounds.max.y-.16f,bounds.center.z)+forward*(front+.12f);
                if(sources.Length>1)emitter.transform.position=bounds.center+forward*(front+.12f);
                emitter.transform.rotation=Quaternion.LookRotation(forward-Vector3.up*.25f);
                var lamp=emitter.AddComponent<Light>();lamp.type=LightType.Spot;lamp.spotAngle=100;lamp.innerSpotAngle=60;lamp.enabled=false;created.Add(lamp);
                }
                lights=created.ToArray();
            }
            lightData=new HDAdditionalLightData[lights.Length];
            cachedShadows=new bool[lights.Length];
            lightSources=new int[lights.Length];
            for(int i=0;i<lights.Length;i++) {
                var lamp=lights[i];
                lamp.enabled=false;lamp.shadows=LightShadows.Soft;lamp.cullingMask=-1;lamp.renderingLayerMask=1;lamp.bounceIntensity=1;
                var hd=lamp.GetComponent<HDAdditionalLightData>();if(!hd)hd=lamp.gameObject.AddComponent<HDAdditionalLightData>();
                hd!.lightDimmer=1;hd.volumetricDimmer=0;hd.affectDiffuse=true;hd.affectSpecular=true;
                hd.SetShadowResolutionOverride(true);
                lightData[i]=hd;
                lightSources[i]=SourceOf(lamp.transform);
                ShadowViews+=lamp.type==LightType.Point?6:1;
            }
            if(light!=null)foreach(var renderer in node.GetComponentsInChildren<MeshRenderer>(true)) {
                int sourceIndex=SourceOf(renderer.transform);if(sourceIndex<0)continue;
                var source=renderer.sharedMaterials;var owned=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(source.Length);
                for(int i=0;i<source.Length;i++) {
                    var original=source[i];owned[i]=original;
                    if(!original || !original.HasProperty("_EmissiveColor"))continue;
                    var color=original.GetColor("_EmissiveColor");
                    if(color.maxColorComponent<=.001f && !FixtureLighting.WorkLampMaterial(original.name))continue;
                    var copy=new Material(original);
                    materials.Add((copy,color.maxColorComponent>.001f?color:Color.white*20,sourceIndex));
                    copy.name=original.name+" [Killhouse circuit]";owned[i]=copy;
                    if(copy.HasProperty("_UseEmissiveIntensity"))copy.SetFloat("_UseEmissiveIntensity",0);
                    if(copy.HasProperty("_EmissiveExposureWeight"))copy.SetFloat("_EmissiveExposureWeight",0);
                }
                renderer.sharedMaterials=owned;
            }
            if(breaker!=null){motion=new NativeBreakerMotion(node);control=new(this,circuitName);}
            }catch(Exception original){try{Dispose();}catch(Exception cleanup){throw new AggregateException(original,cleanup);}throw;}
        }
        internal void Apply(int power,int damage=0,int shadowResolution=-1) {
            Tick();
            int shadowQuality=Math.Clamp(Plugin.PlacedLightShadows.Value,0,2);
            if(shadowResolution<0)shadowResolution=FixtureShadows.Resolution(shadowQuality,RootShadowViews(Root));
            bool on=Breaker?(power&(1<<Circuit))!=0:PowerCircuits.Energized(power,Circuit,independent);
            bool active=Node.activeInHierarchy,photometry=applied==null || wasActive!=active;
            bool state=photometry || applied!=on || appliedDamage!=damage;
            bool shadows=photometry || appliedShadowQuality!=shadowQuality;
            bool resolution=photometry || appliedShadowResolution!=shadowResolution;
            bool cache=Plugin.CachePlacedLightShadows.Value;
            bool caching=photometry || appliedShadowCache!=cache;
            if(!state && !shadows && !resolution && !caching)return;
            for(int i=0;i<lights.Length;i++) {var lamp=lights[i];if(lamp && settings!=null) {
                bool lit=on && (damage&(1<<lightSources[i]))==0;
                var hd=lightData[i];
                if(state){lamp.enabled=lit;hd.lightDimmer=lit?1:0;}
                // Circuit/damage changes do not change authored photometry or
                // shadow allocation. Reapply photometry after root activation.
                if(photometry){lamp.useColorTemperature=false;lamp.color=Mathf.CorrelatedColorTemperatureToRGB(FixtureLighting.Temperature(settings));lamp.range=FixtureLighting.Reach(settings);hd.EnableColorTemperature(false);hd.SetIntensity(FixtureLighting.OutputLumens(settings,circuitBrightness)/lights.Length,LightUnit.Lumen);}
                if(shadows)lamp.shadows=shadowQuality==0?LightShadows.None:LightShadows.Soft;
                if(resolution)hd.SetShadowResolution(shadowResolution==0?256:shadowResolution);
                cachedShadows[i]=FixtureShadowCache.Apply(hd,cache && fixedLight && active && lit && shadowQuality>0);
                if(lit && shadowQuality>0)hd.RequestShadowMapRendering();
            }}
            if(state){
                foreach(var pair in materials)if(pair.Material)pair.Material.SetColor("_EmissiveColor",on && (damage&(1<<pair.Source))==0 && settings!=null?Mathf.CorrelatedColorTemperatureToRGB(FixtureLighting.Temperature(settings))*(pair.On.maxColorComponent*FixtureLighting.OutputLumens(settings,circuitBrightness)/FixtureLighting.ReferenceLumens):Color.black);
                motion?.SetPower(on);control?.Update(on);
            }
            applied=on;appliedDamage=damage;appliedShadowQuality=shadowQuality;appliedShadowResolution=shadowResolution;appliedShadowCache=cache;wasActive=active;
        }
        internal void GeometryChanged(){for(int i=0;i<cachedShadows.Length;i++)if(cachedShadows[i] && lightData[i] && lights[i] && lights[i].enabled && Node && Node.activeInHierarchy)lightData[i].RequestShadowMapRendering();}
        internal bool Use(PlayerNetworking? player,Action? flip=null,Action? cancel=null,float elapsed=0,bool restore=false)=>motion?.Use(player,flip,cancel,elapsed,restore)==true;
        internal BreakerAnimation? Pending=>motion?.Pending(Id);
        internal bool Interacting=>motion?.Interacting==true;
        internal bool NeedsInactiveReset=>motion?.NeedsInactiveReset==true;
        internal void Tick(){if(motion==null)return;bool active=Node.activeInHierarchy;if(motionTickAt==Time.unscaledTime && motionTickActive==active)return;motionTickAt=Time.unscaledTime;motionTickActive=active;motion.Tick();}
        internal void CancelInteraction(){motion?.Cancel();motionTickAt=float.NaN;}
        internal void Reset(){motion?.Reset();motionTickAt=float.NaN;applied=null;lastBullet=IntPtr.Zero;lastShotAt=-1;}
        internal void RegisterShots() {
            if(Breaker)return;
            // The native work light has collision on its stand/arm, but none
            // on its four lamp heads. Give each otherwise uncollidable visible
            // mesh a local hit volume, including glass/lamp housings.
            foreach(var mesh in Node.GetComponentsInChildren<MeshFilter>(true)) {
                var renderer=mesh.GetComponent<MeshRenderer>();var existing=mesh.GetComponent<BoxCollider>();
                if(SourceOf(mesh.transform)<0 || !mesh.sharedMesh || !renderer || !renderer!.enabled || !mesh.gameObject.activeSelf || existing && existing!.enabled && !existing.isTrigger)continue;
                var hit=mesh.gameObject.AddComponent<BoxCollider>();hit.center=mesh.sharedMesh.bounds.center;hit.size=mesh.sharedMesh.bounds.size;
            }
            foreach(var collider in Node.GetComponentsInChildren<Collider>(true).Where(c=>c && !c.isTrigger)) {
                int source=SourceOf(collider.transform);if(source<0)continue;
                colliderOwners[collider.Pointer]=new(this,source);shotColliders.Add(collider.Pointer);
                var handler=collider.GetComponent<OnBulletHitAddForce>()??collider.gameObject.AddComponent<OnBulletHitAddForce>();
                shotOwners[handler.Pointer]=new(this,source);shotHandlers.Add(handler.Pointer);
            }
        }
        internal void Hit(BulletInstance bullet,Vector3 point,int source) {
            if(bullet is null || !Node || !Node.activeInHierarchy || !bullet.isOwner && (bullet.isPlayer || !Mirror.NetworkServer.active))return;
            // ProcessBullet and the native virtual receiver can both see one hit.
            if(lastBullet==bullet.Pointer && lastShotAt==Time.unscaledTime)return;
            lastBullet=bullet.Pointer;lastShotAt=Time.unscaledTime;
            foreach(var session in Runtime.Sessions.Values)if(session.BreakLight(this,point,source))break;
        }
        internal void Dispose(){
            if(disposed)return;disposed=true;
            // Drop managed ownership first. A native release failure must not
            // retain a fixture, or prevent the other owned resources releasing.
            foreach(var pointer in shotHandlers)shotOwners.Remove(pointer);shotHandlers.Clear();
            foreach(var pointer in shotColliders)colliderOwners.Remove(pointer);shotColliders.Clear();
            using var cleanup=new CleanupStack();
            foreach(var data in lightData??Array.Empty<HDAdditionalLightData>())if(data)cleanup.Add(()=>FixtureShadowCache.Apply(data,false));
            foreach(var pair in materials){var material=pair.Material;cleanup.Add(()=>{if(material)UnityEngine.Object.Destroy(material);});}
            materials.Clear();
            cleanup.Add(()=>motion?.Dispose());cleanup.Add(()=>control?.Dispose());
        }
    }
    private static readonly List<Fixture> fixtures=new();
    private static readonly Dictionary<IntPtr,List<Fixture>> byRoot=new();
    private static readonly Dictionary<IntPtr,int> shadowViewsByRoot=new();
    private static int RootShadowViews(GameObject root)=>shadowViewsByRoot.GetValueOrDefault(root.Pointer);
    private static readonly List<Fixture> breakers=new();
    internal static int ActiveEmitterCount=>fixtures.Where(f=>f.Root && f.Root.activeInHierarchy && f.Node && f.Node.activeInHierarchy).Sum(f=>f.EnabledEmitters);
    internal static int CachedEmitterCount=>fixtures.Where(f=>f.Root && f.Root.activeInHierarchy && f.Node && f.Node.activeInHierarchy).Sum(f=>f.CachedEmitters);
    internal static int ActiveCachedEmitterCount=>CachedEmitterCount;
    internal static int ActiveCacheFallbackEmitterCount=>fixtures.Where(f=>f.Root && f.Root.activeInHierarchy && f.Node && f.Node.activeInHierarchy).Sum(f=>f.CacheFallbackEmitters);
    internal static void GeometryChanged(){foreach(var fixture in fixtures)fixture.GeometryChanged();}
    private sealed record HitOwner(Fixture Fixture,int Source);
    private static readonly Dictionary<IntPtr,HitOwner> shotOwners=new();
    private static readonly Dictionary<IntPtr,HitOwner> colliderOwners=new();
    internal static void Add(GameObject node,GameObject root,Layout doc,int id) {
        var light=doc.Lights.FirstOrDefault(l=>l.PropId==id);var breaker=doc.Breakers.FirstOrDefault(b=>b.PropId==id);
        if(light==null && breaker==null)return;
        long began=PerformanceJournal.Begin(),allocated=PerformanceJournal.Allocated();
        Fixture? fixture=null;
        try{var prop=doc.Props.FirstOrDefault(p=>p.Id==id);bool fixedLight=light!=null && prop!=null && prop.SupportId==0 && MovableProps.Mass(prop.Name)<=0;fixture=new(node,root,id,light,breaker,PowerCircuits.Brightness(doc,light?.Circuit??breaker!.Circuit),PowerCircuits.Label(doc,light?.Circuit??breaker!.Circuit),PowerCircuits.Independent(doc,light?.Circuit??breaker!.Circuit),fixedLight);fixture.RegisterShots();fixtures.Add(fixture);if(!byRoot.TryGetValue(root.Pointer,out var group))byRoot[root.Pointer]=group=new();group.Add(fixture);shadowViewsByRoot[root.Pointer]=RootShadowViews(root)+fixture.ShadowViews;if(fixture.Breaker)breakers.Add(fixture);fixture.Apply(PowerCircuits.Initial(doc));}
        catch(Exception original){if(fixture!=null){fixtures.Remove(fixture);Unindex(fixture);}try{fixture?.Dispose();}catch(Exception cleanup){throw new AggregateException(original,cleanup);}throw;}
        finally{PerformanceJournal.Complete("fixture-build",began,allocated);}
    }
    internal static IEnumerable<Fixture> For(GameObject root){if(byRoot.TryGetValue(root.Pointer,out var group))foreach(var f in group)if(f.Node && f.Root)yield return f;}
    internal static void TickBreakers(GameObject root){foreach(var f in breakers)if(f.Root.Pointer==root.Pointer && f.Node && f.Root)f.Tick();}
    private static int Damage(Fixture f,IReadOnlySet<int>? broken){if(broken==null || broken.Count==0)return 0;int mask=0;for(int i=0;i<f.SourceCount;i++)if(broken.Contains(LightDamage.Key(f.Id,i)))mask|=1<<i;return mask;}
    internal static void Apply(GameObject root,int power,IReadOnlySet<int>? broken=null){if(!byRoot.TryGetValue(root.Pointer,out var group))return;int resolution=FixtureShadows.Resolution(Plugin.PlacedLightShadows.Value,RootShadowViews(root));foreach(var f in group)if(f.Node && f.Root)f.Apply(power,Damage(f,broken),resolution);}
    internal static void Reset(GameObject root,int power,IReadOnlySet<int> broken){if(!byRoot.TryGetValue(root.Pointer,out var group))return;int resolution=FixtureShadows.Resolution(Plugin.PlacedLightShadows.Value,RootShadowViews(root));foreach(var f in group)if(f.Node && f.Root){f.Reset();f.Apply(power,Damage(f,broken),resolution);}}
    internal static void TickInactive(){foreach(var f in breakers)if(f.NeedsInactiveReset && f.Node && !f.Node.activeInHierarchy)f.CancelInteraction();}
    private static void Unindex(Fixture fixture){breakers.Remove(fixture);if(byRoot.TryGetValue(fixture.Root.Pointer,out var group) && group.Remove(fixture)){if(group.Count==0){byRoot.Remove(fixture.Root.Pointer);shadowViewsByRoot.Remove(fixture.Root.Pointer);}else shadowViewsByRoot[fixture.Root.Pointer]=Math.Max(0,RootShadowViews(fixture.Root)-fixture.ShadowViews);}}
    internal static bool Shot(OnBulletHitAddForce handler,BulletInstance bullet,RaycastHit hit) {
        if(!shotOwners.TryGetValue(handler.Pointer,out var fixture))return false;
        fixture.Fixture.Hit(bullet,hit.point,fixture.Source);
        return true;
    }
    internal static bool Shot(BulletInstance bullet,RaycastHit hit) {
        var collider=hit.collider;if(!collider || !colliderOwners.TryGetValue(collider!.Pointer,out var fixture))return false;
        fixture.Fixture.Hit(bullet,hit.point,fixture.Source);return true;
    }
    internal static void Release(GameObject tree) {
        using var cleanup=new CleanupStack();
        for(int i=fixtures.Count-1;i>=0;i--)if(!fixtures[i].Node || !fixtures[i].Root || fixtures[i].Root.Pointer==tree.Pointer || fixtures[i].Node.transform.IsChildOf(tree.transform)) {
            var fixture=fixtures[i];Unindex(fixture);fixtures.RemoveAt(i);cleanup.Add(fixture.Dispose);
        }
    }
    internal static void Clear(){
        using var cleanup=new CleanupStack();foreach(var f in fixtures)cleanup.Add(f.Dispose);
        fixtures.Clear();byRoot.Clear();shadowViewsByRoot.Clear();breakers.Clear();shotOwners.Clear();colliderOwners.Clear();
    }
}
