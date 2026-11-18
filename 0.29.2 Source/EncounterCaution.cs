using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

// Per-brain, per-encounter visual contact. Hearing, radio responses and path
// requests cannot release the hold. Visible door motion can. Native Start may rebind the same brain.
internal sealed class EncounterCaution
{
    private sealed class State {
        internal readonly Vector3 Origin;
        internal readonly int Placement;
        internal bool Engaged;
        internal State(Vector3 origin,int placement){Origin=origin;Placement=placement;}
    }
    private readonly Dictionary<IntPtr,State> states=new();
    internal void Bind(BrainAI brain,Combatant definition,Vector3 origin) {
        if(definition.Order!=CombatOrder.Cautious || definition.Reaction!=CivilianReaction.None){states.Remove(brain.Pointer);return;}
        if(!states.TryGetValue(brain.Pointer,out var state) || state.Placement!=definition.Id)states[brain.Pointer]=new(origin,definition.Id);
    }
    internal bool Apply(BrainAI brain,EncounterOrders orders) {
        if(!states.TryGetValue(brain.Pointer,out var state) || !brain.health || brain.health.isDead)return false;
        brain.reactType=state.Engaged?BrainAI.ReactType.Offensive:BrainAI.ReactType.Defensive;
        if(!state.Engaged){brain.responding=false;orders.Bind(brain,true,state.Origin,state.Placement);}
        return true;
    }
    internal bool Holding(BrainAI brain)=>brain && brain.health && !brain.health.isDead && states.TryGetValue(brain.Pointer,out var state) && !state.Engaged;
    internal bool ObserveVision(BrainAI brain,EncounterOrders orders,bool encounterActive,bool visualContact,string stimulus="player") {
        if(!encounterActive || !visualContact || !states.TryGetValue(brain.Pointer,out var state) || state.Engaged || !brain.health || brain.health.isDead)return false;
        state.Engaged=true;orders.Bind(brain,false,state.Origin,state.Placement);
        brain.reactType=BrainAI.ReactType.Offensive;brain.responding=false;
        CoopLog.Event("ai-cautious-contact",new{bot=brain.netId,placement=state.Placement,stimulus});return true;
    }
    internal void Clear()=>states.Clear();
    internal void Remove(BrainAI brain)=>states.Remove(brain.Pointer);
}
