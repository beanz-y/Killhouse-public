namespace OperatorKillhouse.Core;

// Placement uses the physical wooden bench's bounds. The native modding
// controller has UI/camera helpers far outside that furniture's footprint.
public static class LockerPlacement
{
    public static bool Ground(float height,float normalY,float benchBottom)=>
        float.IsFinite(height) && float.IsFinite(normalY) && float.IsFinite(benchBottom) && normalY>=.95f && Math.Abs(height-benchBottom)<=.5f;
    public static (float Side,float Forward)[] Offsets(float benchWidth,float lockerWidth) {
        if(!float.IsFinite(benchWidth) || !float.IsFinite(lockerWidth) || benchWidth<=0 || lockerWidth<=0)throw new ArgumentException("Invalid locker placement geometry.");
        float side=(benchWidth+lockerWidth)*.5f+.45f;
        return new[]{(-side,0f),(side,0f),(-side-.6f,0f),(side+.6f,0f),(-side,1.5f),(side,1.5f)};
    }
}
