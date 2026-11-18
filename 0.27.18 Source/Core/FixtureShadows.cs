namespace OperatorKillhouse.Core;

// A point light renders six shadow faces; a spot renders one. Keep the shadow
// texel budget at the former maximum of sixteen point lights at 512 per face.
public static class FixtureShadows
{
    public const int ReferenceViews=16*6,ReferenceResolution=512,MinimumResolution=128;
    public const long TexelBudget=(long)ReferenceViews*ReferenceResolution*ReferenceResolution;
    public static int Resolution(int quality,int views) {
        if(quality<=0)return 0;
        int resolution=quality==1?256:ReferenceResolution;
        while(resolution>MinimumResolution && (long)Math.Max(0,views)*resolution*resolution>TexelBudget)resolution/=2;
        return resolution;
    }
}
