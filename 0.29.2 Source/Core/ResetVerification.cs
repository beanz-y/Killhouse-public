namespace OperatorKillhouse.Core;

public readonly record struct ResetSnapshot(bool Live,bool Lists,bool Counting,float Seconds,float Minutes,int Targets,int Civilians,int TargetHits,int CivilianHits) {
    public bool Clear=>Live && Lists && !Counting && Seconds==0 && Minutes==0 && Targets==0 && Civilians==0 && TargetHits==0 && CivilianHits==0;
}
public enum ResetOutcome { Pending,Complete,Failed }
public sealed class ResetVerification {
    private readonly double started;
    public ResetVerification(double started){this.started=started;}
    public ResetOutcome Check(ResetSnapshot state,double now) {
        if(!double.IsFinite(started) || !double.IsFinite(now) || now<started || !state.Live || !state.Lists)return ResetOutcome.Failed;
        if(state.Clear)return ResetOutcome.Complete;
        return now-started<2?ResetOutcome.Pending:ResetOutcome.Failed;
    }
}
