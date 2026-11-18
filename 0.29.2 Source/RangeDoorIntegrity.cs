namespace OperatorKillhouse;

internal sealed partial class RangeSession
{
    internal void ResetDoorSwingProtection(){foreach(var saved in doors)DoorIntegrity.Reset(saved.Door);}
    internal void ProtectDoorIntegrity() {
        foreach(var saved in doors)ownership.Add(DoorIntegrity.Track(saved.Door));
        CoopLog.Event("door-swing-protection",new{house=House,doors=doors.Count});
    }
}
