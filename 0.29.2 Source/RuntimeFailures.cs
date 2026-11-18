using BepInEx;
using OperatorKillhouse.Core;

namespace OperatorKillhouse;
internal static partial class Runtime
{
    private static FailureReport RecordFailure(string stage,Exception error,bool fatal,int house=0) {
        // Managed state only: diagnostics must also work while Unity objects
        // are uninitialized or being destroyed. Keep the original exception.
        var report=FailureReport.Capture(error,Plugin.Version,SelectionLog.Run,stage,fatal,Visible,Sessions.Count,house);
        Plugin.Logger.LogError($"Killhouse error {report.Id} at {stage}: {error}");
        try{if(fatal || !stopping)report.Save(Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../UserData/OperatorKillhouse")));}
        catch(Exception failure){Plugin.Logger.LogWarning("Could not save Killhouse last-error.json: "+failure);}
        return report;
    }
    private static void UpdateFailed(string stage,Exception error,int house) {
        stopping=true;
        var report=RecordFailure(stage,error,true,house);
        MenuFailed($"{stage}: {MenuNotice.Plain(report.Message,120)} (error {report.Id}). Details: UserData/OperatorKillhouse/last-error.json.");
        // A second cleanup error must not replace the first failure or resume
        // the broken update loop. Shutdown uses the existing stock restoration.
        try{trainingLifetime?.Depart();}catch(Exception cleanup){Plugin.Logger.LogError("Killhouse failure cleanup: "+cleanup);}
    }
    internal static void StartupFailed(Exception error) {
        NativeAssetDiagnostics.Failed("runtime",error);
        var report=RecordFailure("startup",error,true);
        try{Stop();}catch(Exception cleanup){stopping=true;Plugin.Logger.LogError("Killhouse startup cleanup: "+cleanup);}
        MenuFailed($"startup: {MenuNotice.Plain(report.Message,120)} (error {report.Id}). Details: UserData/OperatorKillhouse/last-error.json.");
    }
}
