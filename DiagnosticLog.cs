namespace OperatorKillhouse;

// Routine support traces are opt-in. Asset validation and actionable failures
// remain active independently of this switch.
internal static class DiagnosticLog
{
    internal static volatile bool Enabled;
    internal static bool Important(string name)=>name.EndsWith("-failed",StringComparison.Ordinal) ||
        name is "transport-unavailable" or "peer-rejected" or "command-rejected" or "stock-initialization-timeout";
    internal static void Info(string text){if(Enabled)Plugin.Logger.LogInfo(text);}
}
