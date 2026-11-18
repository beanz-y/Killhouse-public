namespace OperatorKillhouse.Core;

public readonly record struct ShellContact(float Coordinate,float Offset);
public static class ShellProbes
{
    // Stay inside the actual panel face so a jamb hit has physical overlap,
    // rather than touching a floating-point corner of the measured bounds.
    public const float FaceInset=.02f;
    public static ShellContact? Find(float thickness,Func<float,float?> cast)
    {
        if(!float.IsFinite(thickness) || thickness<.08f || thickness>1.2f)throw new ArgumentOutOfRangeException(nameof(thickness));
        float edge=thickness/2-FaceInset;
        foreach(float offset in new[]{0f,-edge,edge}) {
            float? coordinate=cast(offset);
            if(coordinate.HasValue && float.IsFinite(coordinate.Value))return new(coordinate.Value,offset);
        }
        return null;
    }
}
