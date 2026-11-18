namespace OperatorKillhouse.Core;

// Deliberately contains only file names, numeric locators and identities. It is
// safe to include in the ordinary support report without disclosing game paths
// or copying any native asset payload.
public sealed record NativeAssetMismatch(string Stage,string SourceFile,long? Offset,long? Length,string Expected,string Actual,long? ObjectId=null,int? PartIndex=null)
{
    private const string Key="OperatorKillhouse.NativeAssetMismatch";
    public static string FileName(string? name)=>!string.IsNullOrEmpty(name) && name.Length<=160 && name is not ("." or "..") &&
        name.All(c=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-')?name:"(invalid source name)";
    internal static bool IsSha256(string? value)=>value is {Length:64} && value.All(c=>c is >= 'a' and <= 'f' or >= 'A' and <= 'F' or >= '0' and <= '9');
    public static InvalidDataException Create(string stage,string sourceFile,string expected,string actual,long? offset=null,long? length=null,long? objectId=null,int? partIndex=null,Exception? inner=null)
    {
        var detail=new NativeAssetMismatch(stage,FileName(sourceFile),offset,length,expected,actual,objectId,partIndex);
        string location=offset.HasValue?$" at offset {offset.Value}, length {length}":"";
        if(objectId.HasValue)location+=$", object {objectId.Value}";
        if(partIndex.HasValue)location+=$", recipe part {partIndex.Value}";
        var error=new InvalidDataException($"Native asset check {stage}: {detail.SourceFile}{location}; expected {expected}; actual {actual}.",inner);
        error.Data[Key]=detail;return error;
    }
    public static NativeAssetMismatch? Find(Exception error)
    {
        var pending=new Stack<Exception>();var visited=new HashSet<Exception>(ReferenceEqualityComparer.Instance);pending.Push(error);
        while(pending.Count>0 && visited.Count<128) {
            var current=pending.Pop();if(!visited.Add(current))continue;
            if(current.Data[Key] is NativeAssetMismatch detail)return detail;
            if(current is AggregateException aggregate)foreach(var child in aggregate.InnerExceptions.Take(128).Reverse())pending.Push(child);
            else if(current.InnerException is {} inner)pending.Push(inner);
        }
        return null;
    }
}
