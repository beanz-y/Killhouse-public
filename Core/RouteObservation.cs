namespace OperatorKillhouse.Core;

// Values are compared on every movement update; format text only when a
// waypoint or phase actually changes and a diagnostic record will be emitted.
public enum RoutePhase { Patrolling,Watching,Finished,Flashed,Response,NativeState }
public readonly record struct RouteObservation(int Index,RoutePhase Phase,int NativeState=0)
{
    public string Label=>Phase switch {
        RoutePhase.Patrolling=>"patrolling",RoutePhase.Watching=>"watching",RoutePhase.Finished=>"finished",
        RoutePhase.Flashed=>"paused: flashed",RoutePhase.Response=>"paused: response",_=>"paused: "
    };
}
