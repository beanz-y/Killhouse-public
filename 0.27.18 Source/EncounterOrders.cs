using Pathfinding;
using UnityEngine;

namespace OperatorKillhouse;

// Native Defensive is a reaction style. A no-route position hold also needs
// to constrain movement goals from native combat/hearing and optional AI mods.
internal sealed class EncounterOrders
{
    private sealed class Hold {
        internal readonly BrainAI Brain;
        internal readonly Vector3 Point;
        internal readonly int Placement;
        internal bool Reported;
        internal Hold(BrainAI brain,Vector3 point,int placement){Brain=brain;Point=point;Placement=placement;}
    }
    private readonly Dictionary<IntPtr,Hold> holds=new();
    private readonly Dictionary<IntPtr,IntPtr> followers=new();
    internal void Bind(BrainAI brain,bool hold,Vector3 point,int placement=0) {
        if(!hold){Remove(brain);return;}
        var follower=brain.agent.Agent.Pointer;
        if(holds.TryGetValue(follower,out var current) && current.Brain.Pointer==brain.Pointer && current.Placement==placement && (current.Point-point).sqrMagnitude<.000001f)return;
        Remove(brain);
        // A native follower can be recreated. Release both sides of an old
        // binding, including an address reused by a different brain.
        if(holds.TryGetValue(follower,out current))followers.Remove(current.Brain.Pointer);
        holds[follower]=new(brain,point,placement);followers[brain.Pointer]=follower;
    }
    internal bool FilterDestination(FollowerEntity follower,ref Vector3 destination) {
        if(!follower || !holds.TryGetValue(follower.Pointer,out var order) || !order.Brain || !order.Brain.health || order.Brain.health.isDead)return false;
        if(!order.Reported && (destination-order.Point).sqrMagnitude>.0025f) {
            order.Reported=true;
            CoopLog.Event("ai-hold-goal-blocked",new{bot=order.Brain.netId,placement=order.Placement,requested=destination.ToString(),hold=order.Point.ToString()});
        }
        destination=order.Point;
        return true;
    }
    internal void Clear(){holds.Clear();followers.Clear();}
    internal void Remove(BrainAI brain){if(followers.Remove(brain.Pointer,out var follower))holds.Remove(follower);}
}
