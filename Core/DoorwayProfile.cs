namespace OperatorKillhouse.Core;

// Measured from the installed Wall2MeterDoor triangles, not its outer AABB.
// The mesh is not CPU-readable at runtime. The independent extraction and all
// 37 native door/frame fixtures live in research/measure_door_alignment.py.
public static class DoorwayProfile
{
    public const float Left=.577392578125f,Right=1.5008294582366943f,Top=1.957157850265503f;
    public static (float X,float Y,float Z) Center => (0,Top*.5f,(Left+Right)*.5f);
    public const float FitTolerance=.002f;
}
