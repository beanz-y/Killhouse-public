namespace OperatorKillhouse.Core;

// Gameplay tuning for unassisted sight, in metres; native shorter ranges win.
public static class AiLighting
{
    public static float SightLimit(LightingPreset preset)=>preset==LightingPreset.Night?8:preset is LightingPreset.Dawn or LightingPreset.Dusk?16:float.PositiveInfinity;
    public static float ReactionScale(LightingPreset preset)=>preset==LightingPreset.Night?1.5f:preset is LightingPreset.Dawn or LightingPreset.Dusk?1.2f:1;
}
