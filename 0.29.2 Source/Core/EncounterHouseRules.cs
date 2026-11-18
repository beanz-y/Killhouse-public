namespace OperatorKillhouse.Core;

public readonly record struct EncounterVolume(int Columns,int Rows,float Floor)
{
    public bool Contains(float x,float y,float z)=>float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) &&
        x>=ConstructionPlan.Left-.75f && x<=ConstructionPlan.Left+Columns*2+.75f &&
        z>=ConstructionPlan.North-Rows*2-.75f && z<=ConstructionPlan.North+.75f && y>=Floor-.5f && y<=Floor+12;
}

public static class SpectatorSupport
{
    // Installed shell collider triangles include the elevated 3.65/4.07 m
    // walkways. Height alone must never exempt someone standing on a prop.
    public const float MinimumHeight=2.5f;
    public static bool Excluded(bool permanentShell,float height,float upNormal,float footGap)=>
        permanentShell && float.IsFinite(height) && float.IsFinite(upNormal) && float.IsFinite(footGap) &&
        height>=MinimumHeight && upNormal>=.65f && footGap>=-.15f && footGap<=1;
}
