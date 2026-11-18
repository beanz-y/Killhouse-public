namespace OperatorKillhouse.Core;

public static class EditorWheel
{
    // Unity IMGUI supplies signed wheel units (including fractional trackpad
    // motion). Preserve those units as degrees rather than entering a detent grid.
    public static float Turn(float angle,float delta) {
        if(!float.IsFinite(angle) || !float.IsFinite(delta))throw new ArgumentException("Invalid wheel rotation.");
        return TargetPlacement.Angle(TargetPlacement.Angle(angle)+delta%360);
    }
    public static bool CanRotate(bool readOnly,bool overBoard,float delta)=>!readOnly && overBoard && float.IsFinite(delta) && delta!=0;
    public static bool BlocksGameplay(bool visible,int frame,int through)=>visible || frame<=through;
    public static bool BlocksAxis(bool blocked,string? axis)=>blocked && axis=="Mouse ScrollWheel";
}
