namespace OperatorKillhouse.Core;

// Keep the original serialized output scale so existing experimental layouts
// become brighter without a rewrite or an identity change.
public static class FixtureLighting
{
    public const float SceneOutputScale=50,ReferenceLumens=1600;
    // Old drafts used 4000 K as the implicit colour. Render that default as
    // neutral white without rewriting layouts or their multiplayer identities.
    public static float Temperature(PlacedLight light)=>light.Kelvin==4000?6500:light.Kelvin;
    public static float Reach(PlacedLight light)=>Math.Min(30,light.Range*2);
    public static bool Retired(string asset)=>asset is "Steel barrel" or "Ceiling fluorescent" or "Nine-hole barricade";
    public static float OutputLumens(PlacedLight light,float circuitBrightness=1)=>light.Lumens*SceneOutputScale*circuitBrightness;
    public static float Percent(PlacedLight light)=>100*light.Lumens/ReferenceLumens;
    public static float FromPercent(float percent)=>ReferenceLumens*percent/100;
    // The work light's opaque atlas has an authored lamp-only emission mask.
    // Its refractive glass alone is absent from some native NVG render paths.
    public static bool WorkLampMaterial(string name)=>name.Replace(" (Instance)","",StringComparison.Ordinal) is "LitLampsA1" or "LitLampsA1Transp";
}
