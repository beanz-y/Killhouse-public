namespace OperatorKillhouse.Core;

public sealed record PropState(int Id,float[] Pose);
public sealed record PropPush(int Id,float[] Point,float[] Impulse,bool Bump=false,bool Kick=false);
public static class MovableProps
{
    public const float KickSpeedChange=4,MaxKickImpulse=24;
    // Deliberate allowlist: architecture, cover, loaded bundles and large
    // furniture remain fixed. Masses are conservative gameplay approximations.
    private static readonly Dictionary<string,float> masses=new(StringComparer.OrdinalIgnoreCase) {
        ["SM_FoldingChair_LOD16"]=6,["Cardboard_box_B"]=1.5f,["Cardboard_box_C"]=1.5f,
        ["Cardboard_box_D"]=1,["Cardboard_box_E"]=1,["Vase_A"]=.8f,
        ["Paintbucket_B"]=2,["Papers_box_C"]=1,["Milk_crate"]=2,
        ["Storage_box_B"]=2,["Cooler_box"]=5,["PrpFireExtFastA01"]=6,["Garage_trolley"]=12
    };
    public static float Mass(string asset)=>masses.GetValueOrDefault(PropNames.Key(asset));
    public static bool Rolling(string asset)=>PropNames.Key(asset).Equals("Garage_trolley",StringComparison.OrdinalIgnoreCase);
    public static bool Pose(float[]? pose)=>pose is {Length:7} && pose.All(float.IsFinite) && pose.Take(3).All(v=>Math.Abs(v)<=256) && pose.Skip(3).Sum(v=>v*v) is >.8f and <1.2f;
    public static bool States(PropState[]? states)=>states is {Length:>0 and <=16} && states.All(s=>s!=null && s.Id is >=0 and <4096 && Pose(s.Pose)) && states.Select(s=>s.Id).Distinct().Count()==states.Length;
    public static bool Push(PropPush? push)=>push!=null && !(push.Bump && push.Kick) && push.Id is >=0 and <4096 && push.Point is {Length:3} && push.Point.All(v=>float.IsFinite(v) && Math.Abs(v)<=256) && push.Impulse is {Length:3} && push.Impulse.All(float.IsFinite) && push.Impulse.Sum(v=>v*v) is var strength && strength>.0001f && strength<=(push.Kick?MaxKickImpulse*MaxKickImpulse:36);
}
