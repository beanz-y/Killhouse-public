using System.Text.Json.Serialization;

namespace OperatorKillhouse.Core;

public enum LightingPreset { Normal,Night,Dawn,Dusk }
// Shared scene state is immutable and travels with host-approved selections.
public sealed record MapLighting(LightingPreset Preset=LightingPreset.Normal,float Darkness=0)
{
    public static readonly MapLighting Normal=new();
    public const float VolumePriority=1_000_000_000_000f;
    [JsonIgnore] public bool Enabled=>Preset!=LightingPreset.Normal;
    [JsonIgnore] public bool Valid=>Enum.IsDefined(typeof(LightingPreset),Preset) && float.IsFinite(Darkness) && Darkness is >=0 and <=6 && (Enabled || Darkness==0);
    public static MapLighting For(Layout doc)=>new(doc.Lighting,doc.Darkness);
    // Darkness remains readable for 0.19.0 documents. Runtime presets are fixed.
    [JsonIgnore] public MapLighting Fixed=>Darkness==0?this:new(Preset);
    [JsonIgnore] public float Exposure=>Preset==LightingPreset.Night?9.25f:9;
    [JsonIgnore] public float SunLux=>Preset==LightingPreset.Night?0:Preset==LightingPreset.Dawn?250:400;
    [JsonIgnore] public float SunElevation=>Preset==LightingPreset.Night?-35:5;
    [JsonIgnore] public float SunAzimuth=>Preset==LightingPreset.Dawn?90:270;
    [JsonIgnore] public float Ambient=>Preset==LightingPreset.Night?.0005f:.03f;
}
