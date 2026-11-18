using Pathfinding;

namespace OperatorKillhouse;

// A closed door must cut the floor graph; an off-mesh link alone does not
// prevent the follower taking a direct floor path through its leaf.
internal sealed class EncounterDoors
{
    private sealed record Borrowed(DoorV2 Door,bool CantOpen,NavmeshCut Cut,GraphMask Mask) {
        internal bool Blocked=Door.CurrentlyLocked && !Door.IsOpenEnoughForAI;
    }
    private readonly Dictionary<IntPtr,Borrowed> doors=new();
    internal void Bind(DoorV2 door,GraphMask mask) {
        if(doors.ContainsKey(door.Pointer))return;
        var cut=door.NavMeshCut;
        if(cut is null || !cut)throw new InvalidOperationException("An encounter door has no native navigation cut.");
        doors.Add(door.Pointer,new(door,door.AiCantOpen,cut,cut.graphMask));
        Rebind(cut,mask);BeforeBlockers(door);cut.ForceUpdate();
        door.HandleAIBlockers();
    }
    internal bool BeforeBlockers(DoorV2 door) {
        if(!door || !doors.TryGetValue(door.Pointer,out var saved))return false;
        // A broken latch can leave CurrentlyLocked true. The native clearance
        // predicate covers an open leaf and destruction without unlocking or
        // modifying the door, and becomes false again if a live leaf closes.
        bool blocked=door.CurrentlyLocked && !door.IsOpenEnoughForAI,released=saved.Blocked && !blocked;
        saved.Blocked=blocked;door.AiCantOpen=blocked;return released;
    }
    internal void CheckRoutes(Action check,Action flush) {
        // Before any AI are spawned, validate future connectivity with the
        // native opening links. Do not unlock, move or enable a door itself.
        // Disabled links must be removed again, even if validation throws.
        var temporary=new List<NodeLink2>();
        try {
            foreach(var saved in doors.Values)if(saved.Door && saved.Door.CurrentlyLocked && !saved.Door.IsOpenEnoughForAI) {
                var link=saved.Door.DoorOpenableNavLink;
                if(link && !link.isActiveAndEnabled){temporary.Add(link);link.Apply();}
            }
            flush();check();
        } finally {
            foreach(var link in temporary)if(link)RefreshLink(link);
            flush();
        }
    }
    private static void Rebind(NavmeshCut cut,GraphMask mask) {
        // Mask changes need deregistration/reregistration in the native cut
        // lookup. Preserve the current enabled state, including breached doors.
        bool enabled=cut.enabled;cut.enabled=false;cut.graphMask=mask;cut.enabled=enabled;
    }
    internal static void RefreshLink(NodeLink2 link) {
        // Native Apply/TryAddLink does not check component activation. Calling
        // it on a locked door's disabled opening link recreates that link.
        if(link.isActiveAndEnabled)link.Apply();else link.RemoveLink();
    }
    internal void Restore(Action<Action> cleanup) {
        var borrowed=doors.Values.ToArray();doors.Clear();
        foreach(var saved in borrowed) {
            if(saved.Cut)cleanup(()=>Rebind(saved.Cut,saved.Mask));
            if(saved.Door)cleanup(()=>{saved.Door.AiCantOpen=saved.CantOpen;saved.Door.HandleAIBlockers();});
        }
    }
}
