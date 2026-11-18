using Pathfinding;
using OperatorKillhouse.Core;

namespace OperatorKillhouse;

internal static class CombatantNavigation
{
    // FollowerEntity.pathfindingSettings returns a native ref, which this
    // IL2CPP binding cannot marshal as a managed value. ManagedSettings' field
    // getter instead boxes a copy; edits must be written back to that field.
    internal static int OpenDoorTags(FollowerEntity follower) {
        var settings=follower.managedSettings??throw new InvalidOperationException("AI path settings are missing.");
        return DoorTags(settings.pathfindingSettings.traversableTags,BrainAI.DoorInteractionMode.CanOpenAndGoThrough);
    }
    private static int DoorTags(int tags,BrainAI.DoorInteractionMode mode) {
        int cleared=tags & ~((1<<AgentController.doorWalkLayer)|(1<<AgentController.doorOpenLayer)|(1<<AgentController.doorBreachLayer));
        return cleared | (mode==BrainAI.DoorInteractionMode.CanOpenAndGoThrough?(1<<AgentController.doorWalkLayer)|(1<<AgentController.doorOpenLayer):mode==BrainAI.DoorInteractionMode.CanGoThroughButNotOpen?1<<AgentController.doorWalkLayer:0);
    }

    internal static void Configure(AgentController navigation,GraphMask graphMask,BrainAI.DoorInteractionMode mode) {
        var follower=navigation.Agent;
        // Native OnDisable completes ECS work and disposes the old path before
        // changing its shared settings. SetDoorMode then updates door tags and
        // recreates the entity with this same ManagedSettings component.
        follower.enabled=false;
        follower.radius=EncounterClearance.AgentRadius;
        var settings=follower.managedSettings??throw new InvalidOperationException("AI path settings are missing.");
        var request=settings.pathfindingSettings;
        request.graphMask=graphMask;
        request.traversableTags=DoorTags(request.traversableTags,mode);
        settings.pathfindingSettings=request;
        // Native SetDoorMode removes the previous mode's single tag before
        // adding the new one. Synchronize its bookkeeping with our complete
        // tag set while disabled, so leaving walk-only mode cannot remove the
        // walk permission again. SetDoorMode still recreates/searches normally.
        navigation.currentDoorInteractionMode=mode;
        navigation.SetDoorMode(mode);
    }
}
