namespace OperatorKillhouse.Core;

// Editor constraints; importing an older layout preserves its authored pose.
public static class TargetEditSteps {
    public const float MinimumPositionStep=.5f;
    public const float HeightStep=.05f;
    // Divide whole centimetres once, rather than accumulating binary .05f error.
    public static float SnapHeight(float height) {
        if(!float.IsFinite(height))throw new ArgumentException("Invalid target height.");
        double centimetres=Math.Round(height*20d,MidpointRounding.AwayFromZero)*5;
        return (float)Math.Clamp(centimetres/100d,0,TargetGeometry.MaximumHeight);
    }
    public static string HeightText(float height) {
        float snapped=SnapHeight(height);
        // Clean old adjustment noise without changing the saved pose or rounding
        // intentional fractional heights typed in Free place.
        return (Math.Abs(snapped-height)<=.000001f?snapped:height).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    }
    public static float PositionStep(float requested)=>requested==0?0:requested>=1?1:MinimumPositionStep;
    public static int RotationStep(int requested)=>requested==30?30:45;
    public static float SnapAngle(float angle,int step)=>TargetPlacement.Angle(MathF.Floor(TargetPlacement.Angle(angle)/RotationStep(step)+.5f)*RotationStep(step));
    public static float? Aim(float startX,float startY,float endX,float endY,int step,float? fallback) {
        float dx=endX-startX,dy=endY-startY;
        return MathF.Sqrt(dx*dx+dy*dy)>.1f?SnapAngle(MathF.Atan2(dx,-dy)*180/MathF.PI,step):fallback;
    }
    public static float Turn(float angle,int step,bool reverse=false) {
        int degrees=RotationStep(step);float turns=TargetPlacement.Angle(angle)/degrees;
        return TargetPlacement.Angle((reverse?MathF.Ceiling(turns)-1:MathF.Floor(turns)+1)*degrees);
    }
    public static float Tilt(float angle,int step,bool reverse,float limit) {
        if(!float.IsFinite(angle) || limit is not (90 or 180))throw new ArgumentException("Invalid target tilt.");
        int degrees=RotationStep(step);float turns=angle/degrees;
        return Math.Clamp((reverse?MathF.Ceiling(turns-.00001f)-1:MathF.Floor(turns+.00001f)+1)*degrees,-limit,limit);
    }
}
