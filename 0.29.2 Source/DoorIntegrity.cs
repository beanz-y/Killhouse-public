using UnityEngine;

namespace OperatorKillhouse;

// Disable accidental swing-limit breakage only. Damage, kicks, hinge shots and
// explosives keep their native paths, including the flag set by a slap charge.
internal static class DoorIntegrity
{
    private sealed class Borrowed {
        internal readonly DoorV2 Door;
        internal readonly bool BreakOnOverswing;
        internal int Users=1;
        internal Borrowed(DoorV2 door){Door=door;BreakOnOverswing=door.breakOnceMaxRotation;}
    }
    private static readonly Dictionary<IntPtr,Borrowed> doors=new();
    internal static int Count=>doors.Count;
    internal static void Reset(DoorV2 door){if(door && doors.ContainsKey(door.Pointer))door.breakOnceMaxRotation=false;}
    internal static Action Track(DoorV2 door) {
        var key=door.Pointer;
        if(doors.TryGetValue(key,out var prior))prior.Users++;
        else {
            var entry=new Borrowed(door);doors.Add(key,entry);
            try{door.breakOnceMaxRotation=false;}
            catch{doors.Remove(key);if(door)door.breakOnceMaxRotation=entry.BreakOnOverswing;throw;}
        }
        bool released=false;
        return ()=>{if(released)return;released=true;if(!doors.TryGetValue(key,out var entry) || --entry.Users>0)return;doors.Remove(key);if(entry.Door)entry.Door.breakOnceMaxRotation=entry.BreakOnOverswing;};
    }
}
