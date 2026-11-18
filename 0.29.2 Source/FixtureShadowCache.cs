using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace OperatorKillhouse;

// Cache only the fixed geometry pass. Actors, doors and physics props still
// cast dynamic shadows every frame, at the selected resolution and brightness.
internal static class FixtureShadowCache
{
    private static bool warned;
    internal static bool Apply(HDAdditionalLightData data,bool cache) {
        data.preserveCachedShadow=false;
        if(cache)try {
            data.alwaysDrawDynamicShadows=true;
            data.SetShadowUpdateMode(ShadowUpdateMode.OnDemand);
            var manager=HDCachedShadowManager.instance;
            manager.ForceRegisterLight(data);
            // Resolve placement before rendering: a full cached atlas otherwise
            // silently removes shadows. Never resize/defragment the game's atlas.
            manager.AssignSlotsInAtlases();
            if(manager.LightHasBeenPlacedInAtlas(data))return true;
        }catch(Exception error) {
            if(!warned){warned=true;Plugin.Logger.LogWarning("Placed light shadow cache unavailable; retaining realtime shadows: "+error.GetBaseException().Message);}
        }
        data.SetShadowUpdateMode(ShadowUpdateMode.EveryFrame);
        return false;
    }
}
