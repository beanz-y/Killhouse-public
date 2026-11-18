using OperatorKillhouse.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OperatorKillhouse;
// Scene-local HDRP resources, synchronized by authenticated host selections.
internal static class MapNightMode
{
    private sealed record Sun(Light Light,float Intensity,Color Color,Quaternion Rotation,bool Enabled,Flare? Flare,HDAdditionalLightData? Data,bool Sky,float Applied,Color Tint,Quaternion Direction,bool AppliedEnabled);
    private sealed record Glow(LensFlareComponentSRP Flare,bool Enabled,float Intensity,bool AppliedEnabled,float AppliedIntensity);
    private static readonly List<Sun> suns=new();
    private static readonly List<Glow> glows=new();
    private static GameObject? root;
    private static VolumeProfile? profile;
    internal static bool Active=>root;
    internal static MapLighting Current {get;private set;}=MapLighting.Normal;
    private static Light[] Directionals()=>UnityEngine.Object.FindObjectsOfType<Light>().Where(l=>l && l.isActiveAndEnabled && l.type==LightType.Directional && l.gameObject.scene.isLoaded).ToArray();
    internal static bool Ready(MapLighting settings,out string reason) {
        reason="";
        if(!settings.Valid){reason="Invalid map lighting preset or darkness level.";return false;}
        if(!settings.Enabled || Active)return true;
        if(root is not null || profile is not null || suns.Count>0)Release();
        if(Directionals().Length>0)return true;
        reason="The training scene's directional lighting has not loaded. Your current map is unchanged.";return false;
    }
    internal static void Apply(MapLighting settings) {
        if(!settings.Valid)throw new InvalidOperationException("Invalid map lighting settings.");
        settings=settings.Fixed;
        if(!settings.Enabled){Release();return;}
        if(Active && Current==settings){LaptopLighting.Refresh();SpawnLighting.Refresh();return;}
        Release();
        if(!Ready(settings,out string reason))throw new InvalidOperationException(reason);
        bool night=settings.Preset==LightingPreset.Night;
        var tint=night?new Color(.48f,.59f,.85f):settings.Preset==LightingPreset.Dawn?new Color(1,.64f,.36f):new Color(1,.43f,.22f);
        var direction=Quaternion.Euler(settings.SunElevation,settings.SunAzimuth,0);
        try {
            profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="Killhouse lighting (owned)";
            var sky=profile.Add<GradientSky>(true);
            bool dawn=settings.Preset==LightingPreset.Dawn;
            sky.top.Override(night?new Color(.0005f,.001f,.003f):dawn?new Color(.05f,.10f,.20f):new Color(.025f,.035f,.10f));
            sky.middle.Override(night?new Color(.001f,.002f,.005f):dawn?new Color(.45f,.23f,.19f):new Color(.5f,.16f,.035f));
            sky.bottom.Override(night?new Color(.002f,.003f,.007f):dawn?new Color(.28f,.12f,.08f):new Color(.2f,.05f,.015f));
            sky.exposure.Override(night?0:8);
            var environment=profile.Add<VisualEnvironment>(true);environment.skyType.Override((int)SkyType.Gradient);environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var indirect=profile.Add<IndirectLightingController>(true);
            indirect.indirectDiffuseLightingMultiplier.Override(settings.Ambient);indirect.reflectionLightingMultiplier.Override(night?.001f:.03f);indirect.reflectionProbeIntensityMultiplier.Override(night?.001f:.03f);
            var exposure=profile.Add<Exposure>(true);exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(settings.Exposure);
            root=new GameObject("Operator Killhouse: scene lighting");root.SetActive(false);
            var volume=root.AddComponent<Volume>();volume.isGlobal=true;volume.priority=MapLighting.VolumePriority;volume.weight=1;volume.sharedProfile=profile;
            var seen=new HashSet<int>();
            foreach(var light in Directionals()) {
                var data=light.GetComponent<HDAdditionalLightData>();
                suns.Add(new(light,light.intensity,light.color,light.transform.rotation,light.enabled,light.flare,data,data is not null && data && data.interactsWithSky,settings.SunLux,tint,direction,!night));
                foreach(var flare in light.GetComponentsInChildren<LensFlareComponentSRP>(true))if(flare && seen.Add(flare.GetInstanceID())) {
                    float level=night?0:flare.intensity*.02f;bool enabled=!night && flare.enabled;
                    glows.Add(new(flare,flare.enabled,flare.intensity,enabled,level));flare.intensity=level;flare.enabled=enabled;
                }
                light.intensity=settings.SunLux;light.color=tint;light.transform.rotation=direction;light.enabled=!night;
                if(night)light.flare=null;
                if(data is not null && data){if(night)data.interactsWithSky=false;else data.RequestShadowMapRendering();}
            }
            root.SetActive(true);Current=settings;LaptopLighting.Refresh();SpawnLighting.Refresh();
            CoopLog.Event("map-night-mode",new{lighting=settings,backend="training-hdrp",priority=volume.priority,exposure=settings.Exposure,directionals=suns.Count,sunFlares=glows.Count,laptopScreens=LaptopLighting.Count,unaidedAiSightLimit=AiLighting.SightLimit(settings.Preset),scope="whole training scene"});
        }catch{Release();throw;}
    }
    private static void Cleanup(Action action){try{action();}catch(Exception error){Plugin.Logger.LogWarning("Map lighting restoration: "+error);}}
    internal static void Release() {
        LaptopLighting.Clear();SpawnLighting.Clear();
        bool changed=root || profile || suns.Count>0;
        if(root is {} current && current)Cleanup(()=>{current.SetActive(false);UnityEngine.Object.Destroy(current);});root=null;
        foreach(var glow in glows)Cleanup(()=>{
            if(!glow.Flare)return;
            if(glow.Flare.enabled==glow.AppliedEnabled)glow.Flare.enabled=glow.Enabled;
            if(glow.Flare.intensity==glow.AppliedIntensity)glow.Flare.intensity=glow.Intensity;
        });glows.Clear();
        foreach(var sun in suns)Cleanup(()=>{
            if(!sun.Light)return;
            if(sun.Light.intensity==sun.Applied)sun.Light.intensity=sun.Intensity;
            if(sun.Light.color==sun.Tint)sun.Light.color=sun.Color;
            if(sun.Light.transform.rotation==sun.Direction)sun.Light.transform.rotation=sun.Rotation;
            if(sun.Light.enabled==sun.AppliedEnabled)sun.Light.enabled=sun.Enabled;
            if(sun.Light.flare==null)sun.Light.flare=sun.Flare;
            if(sun.Data is {} data && data){if(!data.interactsWithSky)data.interactsWithSky=sun.Sky;if(sun.Light.enabled)data.RequestShadowMapRendering();}
        });suns.Clear();
        if(profile is {} owned && owned) {
            Cleanup(()=>{foreach(var component in owned.components)if(component)UnityEngine.Object.Destroy(component);});
            Cleanup(()=>UnityEngine.Object.Destroy(owned));
        }profile=null;Current=MapLighting.Normal;
        if(changed)CoopLog.Event("map-night-mode",new{lighting=Current,backend="training-hdrp",scope="whole training scene"});
    }
}
