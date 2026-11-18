namespace OperatorKillhouse.Core;

public readonly record struct DoorAccess(Bearing LockedFrom)
{
    public Bearing OpensFrom => (Bearing)(((int)LockedFrom+2)%4);
    public (float X,float Y) MarkerDirection => LockedFrom switch {
        Bearing.North=>(0,-1),Bearing.East=>(1,0),Bearing.South=>(0,1),_=>(-1,0)};
}

// Closed pivot forward in the H frame, before authored hinge reversal. Native
// TowardsFront uses Invert ? forward : -forward. A handle on that side fails
// its lock check because the argument points FROM the handle TO the hinge.
public readonly record struct DoorAccessProfile(float ForwardX,float ForwardZ,bool HingeHigh,bool Invert)
{
    public DoorAccess ForEdge(bool horizontal,DoorSettings? settings) {
        float x=ForwardX,z=ForwardZ;
        if(!horizontal)(x,z)=(z,-x);
        float sign=(settings?.NativeInvert(horizontal)??Invert)?1:-1;
        if(settings is not null && settings.HingeHigh!=HingeHigh)sign=-sign;
        x*=sign;z*=sign;
        if(!float.IsFinite(x) || !float.IsFinite(z) || (horizontal?Math.Abs(z)<.98f || Math.Abs(x)>.02f:Math.Abs(x)<.98f || Math.Abs(z)>.02f))
            throw new InvalidOperationException("Native door lock side is not aligned to this edge.");
        return new(horizontal?(z>0?Bearing.North:Bearing.South):(x>0?Bearing.East:Bearing.West));
    }
}
