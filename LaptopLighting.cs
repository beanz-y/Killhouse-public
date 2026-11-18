using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OperatorKillhouse;

// Own only the two native display overrides and their local spill lights.
internal static class LaptopLighting
{
    private sealed class Display : IDisposable
    {
        private readonly Renderer screen;
        private readonly Material original;
        private Material? emissive;
        private GameObject? root;
        internal bool Alive=>screen && emissive && root && screen.sharedMaterial==emissive;
        internal Display(Renderer screen) {
            this.screen=screen;original=screen.sharedMaterial;
            try {
                emissive=new Material(original){name="Killhouse laptop display (owned)",hideFlags=HideFlags.HideAndDontSave};
                emissive.SetTexture("_EmissiveColorMap",original.GetTexture("_BaseColorMap"));
                emissive.EnableKeyword("_EMISSIVE_COLOR_MAP");
                emissive.SetColor("_EmissiveColor",new Color(.55f,.7f,1));
                emissive.SetFloat("_EmissiveExposureWeight",0);
                emissive.SetFloat("_UseEmissiveIntensity",0);
                root=new GameObject("Killhouse laptop screen glow (owned)"){hideFlags=HideFlags.HideAndDontSave};
                root.SetActive(false);root.transform.SetParent(screen.transform.parent,false);
                // Native Plane is an XZ quad; its local +Y faces the user/keys.
                root.transform.position=screen.transform.position+screen.transform.up*.035f;
                root.transform.rotation=screen.transform.rotation*Quaternion.Euler(-90,0,0);
                var light=root.AddComponent<Light>();light.type=LightType.Spot;light.color=new Color(.55f,.7f,1);
                light.range=1.5f;light.spotAngle=110;light.innerSpotAngle=70;light.shadows=LightShadows.None;
                var hd=root.AddComponent<HDAdditionalLightData>();hd.SetIntensity(20,LightUnit.Lumen);hd.volumetricDimmer=0;
                screen.sharedMaterial=emissive;root.SetActive(true);
            }catch{Dispose();throw;}
        }
        public void Dispose() {
            Cleanup(()=>{if(screen && screen.sharedMaterial==emissive)screen.sharedMaterial=original;});
            Cleanup(()=>{if(root){root!.SetActive(false);UnityEngine.Object.Destroy(root);}});root=null;
            Cleanup(()=>{if(emissive)UnityEngine.Object.Destroy(emissive!);});emissive=null;
        }
    }
    private static readonly Dictionary<int,Display> displays=new();
    internal static int Count=>displays.Count;
    private static readonly HashSet<int> warned=new();
    private static void Cleanup(Action action){try{action();}catch(Exception error){Plugin.Logger.LogWarning("Laptop lighting cleanup: "+error);}}
    internal static void Refresh(IEnumerable<RangeComputer>? computers=null) {
        var seen=new HashSet<int>();
        foreach(var computer in computers??UnityEngine.Object.FindObjectsOfType<RangeComputer>()) {
            if(!computer || !computer.gameObject.scene.isLoaded)continue;
            var plane=computer.transform.Find("Plane");var screen=plane is not null && plane?plane.GetComponent<Renderer>():null;
            if(!screen)continue;
            int key=screen!.GetInstanceID();seen.Add(key);
            if(displays.TryGetValue(key,out var existing)){if(existing.Alive)continue;existing.Dispose();displays.Remove(key);}
            var material=screen.sharedMaterial;
            if(!material || !material.HasProperty("_EmissiveColor") || !material.HasProperty("_BaseColorMap"))continue;
            try {displays.Add(key,new Display(screen));}
            catch(Exception error){if(warned.Add(key))Plugin.Logger.LogWarning("Laptop lighting unavailable: "+error);}
        }
        foreach(int key in displays.Keys.Where(k=>!seen.Contains(k)).ToArray()){displays[key].Dispose();displays.Remove(key);}
    }
    internal static void Clear(){foreach(var display in displays.Values)display.Dispose();displays.Clear();warned.Clear();}
}
