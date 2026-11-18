namespace OperatorKillhouse.Core;

public static class BreakerTiming
{
    // Installed night-mission TurnOff 1 clip: PowerOff event, then FinishAnimation.
    public const float SwitchSeconds=1.6666666269302368f,Duration=2,Cooldown=2.6f;
    // Reverse begins once the native reach has paused at hand contact. Its
    // switch marker is the original PowerOff marker measured from clip end.
    public const float ReachSeconds=.5f,RestoreSwitchSeconds=ReachSeconds+Duration-SwitchSeconds,RestoreDuration=ReachSeconds+Duration;
    public static float Contact(bool restore)=>restore?RestoreSwitchSeconds:SwitchSeconds;
    public static float Complete(bool restore)=>restore?RestoreDuration:Duration;
    public static float Normalized(float elapsed,bool restore)=>restore?1-Math.Clamp((elapsed-ReachSeconds)/Duration,0,1):Math.Clamp(elapsed/Duration,0,1);
    public static bool Valid(BreakerAnimation? cue)=>cue!=null && cue.Id is >=1 and <=1000000 && cue.Player>0 && float.IsFinite(cue.Elapsed) && cue.Elapsed>=0 && cue.Elapsed<Contact(cue.Restore);
}
public sealed record BreakerAnimation(int Id,uint Player,float Elapsed=0,bool Cancelled=false,bool Restore=false);
