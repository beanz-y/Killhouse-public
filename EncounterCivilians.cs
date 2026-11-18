using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed class EncounterCivilians
{
    private sealed class State {
        internal readonly CivilianAlarm Alarm;
        internal State(float fleeDelay){Alarm=new(fleeDelay);}
        internal float HeardAge,HP,AlarmTime;
        internal Vector3 Point;
        internal bool Armed,FireReady,FlashReady,Fleeing;
    }
    private readonly Dictionary<IntPtr,State> states=new();
    internal void Bind(BrainAI brain,Combatant definition) {
        if(definition.Reaction==CivilianReaction.None){states.Remove(brain.Pointer);return;}
        if(states.TryGetValue(brain.Pointer,out var existing)) {
            if(!existing.Armed){existing.HP=brain.health?brain.health.TotalHP:0;existing.HeardAge=brain.LastTimeHeardTargetPositionChanged;}
            return;
        }
        states.Add(brain.Pointer,new(definition.FleeDelay){HeardAge=brain.LastTimeHeardTargetPositionChanged,HP=brain.health?brain.health.TotalHP:0,Point=brain.agent.Agent.position});
    }
    internal bool Alarmed(BrainAI brain)=>states.TryGetValue(brain.Pointer,out var state) && state.Alarm.Alarmed;
    internal Vector3 HoldPoint(BrainAI brain)=>states[brain.Pointer].Point;
    internal bool CanFlee(BrainAI brain)=>states.TryGetValue(brain.Pointer,out var state) && state.Alarm.CanFlee(Time.time);
    internal bool Observe(BrainAI brain,Combatant definition,bool active=true) {
        if(!states.TryGetValue(brain.Pointer,out var state))return false;
        // Instantiate/Awake/Start can expose prefab defaults and temporary health
        // values. Establish evidence baselines only after the whole encounter starts.
        if(!active)return false;
        if(!state.Armed) {
            state.Armed=true;state.HP=brain.health.TotalHP;state.HeardAge=brain.LastTimeHeardTargetPositionChanged;
            state.FireReady=!brain.underFire;state.FlashReady=!brain.isFlashed;
            CoopLog.Event("ai-civilian-ready",new{placement=definition.Id,seen=(bool)brain.CurrentSeenTarget,responding=brain.responding,underFire=brain.underFire,flashed=brain.isFlashed,hp=state.HP,heardAge=state.HeardAge});
            return false;
        }
        state.FireReady|=!brain.underFire;state.FlashReady|=!brain.isFlashed;
        // Despite its name this field is an AGE: RunBrainTick adds dt and
        // native hearing resets it to zero. An increasing idle clock is not
        // a sound. Compare consecutive samples, never the spawn baseline.
        float heardAge=brain.LastTimeHeardTargetPositionChanged,previousHeardAge=state.HeardAge;
        bool heard=float.IsFinite(heardAge) && heardAge>=0 && float.IsFinite(previousHeardAge) && heardAge+.0001f<previousHeardAge;
        state.HeardAge=heardAge;
        // 'responding' is a movement/assistance state, not sensory evidence.
        if(state.Alarm.Observe(Time.time,brain.CurrentSeenTarget,heard,state.FireReady && brain.underFire,brain.health.TotalHP<state.HP-.01f,state.FlashReady && brain.isFlashed)) {
            state.Point=brain.agent.Agent.position;state.AlarmTime=Time.time;
            CoopLog.Event("ai-civilian-alarmed",new{placement=definition.Id,reaction=definition.Reaction.ToString(),route=definition.RouteId,reason=state.Alarm.Reason,heardAge,previousHeardAge,simTime=Time.time,position=state.Point.ToString(),freezeSeconds=definition.Reaction==CivilianReaction.Flee?state.Alarm.FleeDelay:0});
        }
        if(definition.Reaction==CivilianReaction.Flee && !state.Fleeing && state.Alarm.CanFlee(Time.time)) {
            state.Fleeing=true;CoopLog.Event("ai-civilian-fleeing",new{placement=definition.Id,route=definition.RouteId,freezeSeconds=state.Alarm.FleeDelay,elapsed=Time.time-state.AlarmTime,simTime=Time.time,position=brain.agent.Agent.position.ToString()});
        }
        return state.Alarm.Alarmed;
    }
    internal static void Disarm(BrainAI brain) {
        brain.doesSuppress=false;brain.doesCounterFire=false;brain.doesReactionarySuppression=false;brain.CanCallBackup=false;brain.useComms=false;
        if(brain.weapons){brain.weapons.fireWeapon=false;brain.weapons.grenadesRemaining=0;brain.weapons.flashbangsRemaining=0;}
    }
    internal void Clear()=>states.Clear();
    internal void Remove(BrainAI brain)=>states.Remove(brain.Pointer);
}
