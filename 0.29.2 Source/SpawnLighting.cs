using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OperatorKillhouse;

// The Outdoors spawn building's authored fluorescent lights are baked-only.
// Own a realtime contribution under the existing fixtures for dark presets;
// never change the baked lights, materials, lightmaps, or range circuits.
internal static class SpawnLighting
{
    internal const float Lumens=9000,Reach=7;
    private sealed class Fixture : IDisposable
    {
        internal readonly GameObject Source;
        private readonly GameObject root;
        internal bool Alive=>Source && root;
        internal Fixture(GameObject source) {
            Source=source;root=new GameObject("Killhouse spawn fixture light (owned)");root.SetActive(false);root.transform.SetParent(source.transform,false);
            try {
                var native=source.GetComponentsInChildren<Light>(true).FirstOrDefault(l=>l && l.type!=LightType.Directional);
                if(native)root.transform.SetPositionAndRotation(native!.transform.position,native.transform.rotation);
                else {
                    var renderers=source.GetComponentsInChildren<MeshRenderer>(true);var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    root.transform.position=new(bounds.center.x,bounds.min.y-.035f,bounds.center.z);
                    root.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
                }
                var light=root.AddComponent<Light>();light.type=LightType.Spot;light.spotAngle=110;light.innerSpotAngle=75;light.range=Reach;
                light.color=new Color(1,.96f,.9f);light.cullingMask=-1;light.renderingLayerMask=-1;light.shadows=LightShadows.None;
                var hd=root.AddComponent<HDAdditionalLightData>();hd.volumetricDimmer=0;hd.lightDimmer=1;hd.affectDiffuse=true;hd.affectSpecular=true;
                // Set photometry after activation: HDRP OnEnable may reapply the
                // serialized Light baseline when an inactive root is activated.
                root.SetActive(true);hd.SetIntensity(Lumens,LightUnit.Lumen);light.enabled=true;
            }catch{Dispose();throw;}
        }
        public void Dispose(){if(root){root.SetActive(false);UnityEngine.Object.Destroy(root);}}
    }
    private static readonly Dictionary<int,Fixture> fixtures=new();
    private static readonly HashSet<int> warned=new();
    private sealed record Building(GameObject Root,int Children,GameObject[] Sources);
    private static readonly Dictionary<int,Building> buildings=new();
    private static readonly Dictionary<int,GameObject[]> sceneRoots=new();
    internal static int Count=>fixtures.Count;
    internal static bool FixtureName(string name)=>name.StartsWith("Lamp_ceiling_square",StringComparison.Ordinal) || name.StartsWith("Lamp_fluorescent_B",StringComparison.Ordinal) && !name.Contains("ceilingpart",StringComparison.Ordinal);
    internal static void Refresh(IEnumerable<RangeComputer>? computers=null) {
        foreach(var key in fixtures.Where(p=>!p.Value.Alive).Select(p=>p.Key).ToArray()){fixtures[key].Dispose();fixtures.Remove(key);}
        foreach(var key in buildings.Where(p=>!p.Value.Root).Select(p=>p.Key).ToArray())buildings.Remove(key);
        var scenes=new HashSet<int>();
        foreach(var computer in computers??UnityEngine.Object.FindObjectsOfType<RangeComputer>()) {
            if(!computer || !computer.gameObject.scene.isLoaded || !scenes.Add(computer.gameObject.scene.handle))continue;
            int scene=computer.gameObject.scene.handle;
            if(!sceneRoots.TryGetValue(scene,out var roots) || roots.Any(r=>!r)) {
                roots=computer.gameObject.scene.GetRootGameObjects().Where(r=>r.name=="main building props").ToArray();
                // An absent building may still be loading. Keep retrying until
                // there is a real root to cache for this scene.
                if(roots.Length>0)sceneRoots[scene]=roots;
            }
            foreach(var root in roots)AddRoot(root);
        }
        foreach(var scene in sceneRoots.Keys.Where(s=>!scenes.Contains(s)).ToArray())sceneRoots.Remove(scene);
    }
    internal static void AddRoot(GameObject root) {
        int rootId=root.GetInstanceID(),children=root.transform.childCount;
        if(!buildings.TryGetValue(rootId,out var building) || !building.Root || building.Children!=children || building.Sources.Length==0) {
            // Native building props are scene-authored. Discover their lamp
            // sources once; owned light children must never trigger a rescan.
            var sources=root.GetComponentsInChildren<Transform>(true).Select(t=>t.gameObject)
                .Where(source=>FixtureName(source.name)).ToArray();
            buildings[rootId]=building=new(root,children,sources);
        }
        foreach(var source in building.Sources) {
            if(!source || !source.activeInHierarchy)continue;
            int id=source.GetInstanceID();if(fixtures.ContainsKey(id))continue;
            if(!source.GetComponent<MeshRenderer>())continue;
            try{fixtures.Add(id,new(source));}
            catch(Exception error){if(warned.Add(id))Plugin.Logger.LogWarning("Spawn fixture unavailable: "+source.name+": "+error.GetBaseException().Message);}
        }
    }
    internal static void Clear(){foreach(var f in fixtures.Values)f.Dispose();fixtures.Clear();warned.Clear();buildings.Clear();sceneRoots.Clear();}
}
