using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

// Single native Vision call only. Never changes shared AI parameter assets or
// the stored flashbang baseline; native contact results intentionally persist.
internal static class EncounterLighting
{
    internal sealed class VisionScope:IDisposable
    {
        private BrainAI? brain;
        private readonly GameMode? mode;
        private readonly bool night,appliedNight;
        private readonly float range,perfect,reaction,appliedRange,appliedPerfect,appliedReaction;
        internal VisionScope(BrainAI brain,LightingPreset preset) {
            this.brain=brain;mode=GameMode.singleton;
            range=brain.DetectionRange;perfect=brain.EyesFOVDistancePerfect;reaction=brain.ReactionTime;
            appliedRange=Math.Min(range,AiLighting.SightLimit(preset));appliedPerfect=Math.Min(perfect,appliedRange*.4f);appliedReaction=reaction*AiLighting.ReactionScale(preset);
            night=mode && mode!.isNight;appliedNight=preset==LightingPreset.Night;
            try {
                brain.DetectionRange=appliedRange;brain.EyesFOVDistancePerfect=appliedPerfect;brain.ReactionTime=appliedReaction;
                if(mode)mode!.isNight=appliedNight;
            }catch{Dispose();throw;}
        }
        public void Dispose() {
            var saved=brain;brain=null;if(saved is null)return;
            // Each restoration is independent; preserve intervening native/mod edits.
            try{if(saved && saved.DetectionRange==appliedRange)saved.DetectionRange=range;}
            finally{try{if(saved && saved.EyesFOVDistancePerfect==appliedPerfect)saved.EyesFOVDistancePerfect=perfect;}
            finally{try{if(saved && saved.ReactionTime==appliedReaction)saved.ReactionTime=reaction;}
            finally{if(mode && mode!.isNight==appliedNight)mode.isNight=night;}}}
        }
    }
    internal static VisionScope? Begin(EyesAI eyes,MapLighting lighting,bool owned,bool server) {
        var brain=eyes?eyes.brain:null;
        if(!server || !owned || !lighting.Valid || !lighting.Enabled || !brain || brain!.HasNVG || !brain.health || brain.health.isDead)return null;
        if(!float.IsFinite(brain.DetectionRange) || brain.DetectionRange<0 || !float.IsFinite(brain.EyesFOVDistancePerfect) || brain.EyesFOVDistancePerfect<0 || !float.IsFinite(brain.ReactionTime) || brain.ReactionTime<0)return null;
        return new(brain,lighting.Preset);
    }
    internal static bool Allows(EyesAI eyes,Transform? target,MapLighting lighting) {
        if(!lighting.Valid || !lighting.Enabled || !target || !eyes || !eyes.brain || eyes.brain.HasNVG)return true;
        var origin=eyes._eyesTransform;
        if(!origin)return true; // Let native readiness/occlusion guards decide.
        float native=eyes.brain.DetectionRange;
        if(!float.IsFinite(native) || native<0)return true;
        float limit=Math.Min(native,AiLighting.SightLimit(lighting.Preset));
        return (target!.position-origin!.position).sqrMagnitude<=limit*limit;
    }
}
