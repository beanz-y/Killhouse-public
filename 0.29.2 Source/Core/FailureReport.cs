using System.Text.Json;

namespace OperatorKillhouse.Core;

public sealed record FailureReport(string Id,DateTimeOffset Time,string Version,string Run,string Stage,bool Fatal,bool MenuOpen,int Houses,int House,string ErrorType,string Message,string Exception)
{
    public const string FileName="last-error.json";
    public static FailureReport Capture(Exception error,string version,string run,string stage,bool fatal,bool menuOpen,int houses,int house=0)=>new(
        Guid.NewGuid().ToString("N")[..12],DateTimeOffset.UtcNow,version,run,stage,fatal,menuOpen,houses,house,
        error.GetType().FullName??error.GetType().Name,error.GetBaseException().Message,error.ToString());
    public void Save(string folder) {
        Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,FileName),pending=file+".tmp";
        File.WriteAllText(pending,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(pending,file,true);
    }
}
