using BepInEx;
using System.Text.Json;

namespace OperatorKillhouse;
internal static class CoopLog
{
    private static bool warned;
    internal static void Event(string name,object detail) {
        bool verbose=DiagnosticLog.Enabled,important=DiagnosticLog.Important(name);
        if(!verbose && !important)return;
        var text=JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,version=Plugin.Version,run=SelectionLog.Run,name,detail});
        if(important)Plugin.Logger.LogWarning("co-op "+text);else Plugin.Logger.LogInfo("co-op "+text);
        if(!verbose)return;
        try {string folder=Path.Combine(Paths.BepInExRootPath,"../UserData/OperatorKillhouse");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"multiplayer.jsonl"),text+Environment.NewLine);}
        catch(Exception error){if(!warned){warned=true;Plugin.Logger.LogWarning("Co-op diagnostics: "+error.GetBaseException().Message);}}
    }
}
