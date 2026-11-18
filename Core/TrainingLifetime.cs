namespace OperatorKillhouse.Core;

// Owns one training scene's resources. Departure is latched because the old
// scene may remain active for several frames after the game begins loading.
public sealed class TrainingLifetime
{
    private readonly Action enter,leave;
    private int? blockedScene;
    public bool Active {get;private set;}
    public bool NeedsCleanup {get;private set;}
    public int SceneHandle {get;private set;}
    public TrainingLifetime(Action enter,Action leave){this.enter=enter;this.leave=leave;}
    public static bool IsTraining(string? name)=>string.Equals(name,"Outdoors area",StringComparison.Ordinal);
    public void Observe(int handle,bool loaded,string? name,bool enabled) {
        bool wanted=loaded && enabled && IsTraining(name);
        if(NeedsCleanup && !Active)Cleanup();
        if(Active && (!wanted || handle!=SceneHandle))Deactivate();
        if(!wanted || Active || blockedScene==handle)return;
        SceneHandle=handle;Active=true;NeedsCleanup=true;
        try{enter();}
        catch(Exception error) {
            blockedScene=handle;Active=false;
            try{Cleanup();}catch(Exception cleanup){throw new AggregateException("Training activation and cleanup failed.",error,cleanup);}
            throw;
        }
    }
    public void Depart() {
        if(Active || NeedsCleanup)blockedScene=SceneHandle;
        Deactivate();
    }
    public void Unloaded(int handle) {
        if((Active || NeedsCleanup) && SceneHandle==handle)Depart();
        if(blockedScene==handle)blockedScene=null;
    }
    public void Stop(){Deactivate();blockedScene=null;}
    private void Deactivate(){Active=false;if(NeedsCleanup)Cleanup();}
    private void Cleanup(){leave();NeedsCleanup=false;}
}
