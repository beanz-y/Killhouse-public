using Mirror;

namespace OperatorKillhouse;

internal sealed record EncounterDoorState(DoorV2 Door,bool Locked);
internal readonly record struct DoorResetResult(int Reset,int Skipped,int Failures);
internal static class EncounterDoorReset
{
    internal static DoorResetResult Restore(IEnumerable<EncounterDoorState> snapshot,Func<DoorV2,bool> ready,Action<Exception> failed) {
        if(!NetworkServer.active)return default;
        int reset=0,skipped=0,failures=0;var seen=new HashSet<IntPtr>();
        foreach(var state in snapshot) {
            var door=state.Door;
            if(!door || !seen.Add(door.Pointer))continue;
            if(!ready(door)){skipped++;continue;}
            try {
                // ResetDoor alone only zeroes currentRotation. Native repair
                // restores hinges, latch, fragments, rigidbody and leaf pose.
                door.UserCode_RPC_RepairDoor();
                door.SetRotationHotReload(0);
                door.Server_SetDoorLocked(state.Locked);
                if(door.netId!=0)door.RPC_RepairDoor();
                door.HandleAIBlockers();reset++;
            }catch(Exception error){failures++;failed(error);}
        }
        return new(reset,skipped,failures);
    }
}
