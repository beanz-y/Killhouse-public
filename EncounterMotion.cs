using Pathfinding;
using UnityEngine;

namespace OperatorKillhouse;

internal static class EncounterMotion
{
    // BrainAI.transform is the stationary prefab container. Both physical
    // arrival measurements must follow the character child instead.
    internal static float ArrivalDistance(FollowerEntity follower,Vector3 destination)=>Math.Max(Vector3.Distance(follower.position,destination),Vector3.Distance(follower.transform.position,destination));
    internal static bool SetGoal(FollowerEntity follower,Vector3 destination) {
        if(follower.isTraversingOffMeshLink)return false;
        follower.isStopped=false;
        follower.stopDistance=.2f;
        bool changed=(follower.destination-destination).sqrMagnitude>.0025f;
        if(changed)follower.destination=destination;
        // Native path/door traversal keeps control of movement. Requesting a
        // new path does not count as arrival or skip a blocked waypoint.
        return changed;
    }
    internal static bool FaceTravel(BrainAI brain,bool civilian=false) {
        var follower=brain.agent.Agent;
        if(brain.isFlashed || follower.isStopped || follower.isTraversingOffMeshLink || !brain._rotationController)return false;
        if(!civilian && (brain.responding || brain.CurrentState!=BrainAI.States.Idle || brain.CurrentSeenTarget || brain.underFire || brain.LastTimeHeardTargetPositionChanged<.3f))return false;
        // Use the steered velocity, including corners, rather than looking
        // straight through a wall at the next waypoint. Keep native leg/spine
        // smoothing and network animation in charge of the physical turn.
        var velocity=follower.velocity;var direction=new Vector3(velocity.x,0,velocity.z);
        if(!float.IsFinite(direction.x) || !float.IsFinite(direction.z) || direction.sqrMagnitude<.0025f)return false;
        var rotation=Quaternion.LookRotation(direction);
        brain._rotationController.SetBotRotationTarget(rotation);
        // Native idle logic otherwise restores the original spawn yaw when
        // a route briefly has no path, including its final waypoint.
        brain.DefensiveInitialRotation=rotation;
        return true;
    }
}
