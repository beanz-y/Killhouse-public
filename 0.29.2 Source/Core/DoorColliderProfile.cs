namespace OperatorKillhouse.Core;

public static class DoorColliderProfile {
    public static bool Valid(float x,float y,float z)=>float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) && x is >.02f and <.12f && y is >1.8f and <1.95f && z is >.8f and <.9f;
    public static bool UseBaseline(bool nativeHitbox,float x,float y,float z)=>nativeHitbox && Valid(x,y,z);
}
