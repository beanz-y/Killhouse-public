using System.Security.Cryptography;

namespace OperatorKillhouse.Core;

public sealed record NativeUiPart(string? File,long Offset,int Size,string? Sha256,string? Data,long? ObjectId=null,long? RelativeOffset=null);
public sealed record NativeUiPlan(long Size,string Sha256,int Objects,NativeUiPart[] Parts,int SerializedFiles=1,int ScriptTypes=0,NativeUiFilePlan[]? SourceFiles=null,NativeUiObjectPlan[]? ObjectSources=null);

// The embedded recipe contains locators and authored metadata patches. Native
// UI payloads are copied from the player's reviewed installation, never shipped
// in the DLL. This is separate from the script-free prop cache policy.
public static class NativeUiCache
{
    private static bool Source(string file)=>file is "level2" or "globalgamemanagers.assets" or "resources.assets" or "resources.assets.resS" ||
        new[]{"sharedassets0.assets","sharedassets1.assets","sharedassets2.assets"}.Any(n=>file==n || file==n+".resS") || file is "sharedassets0.resource" or "sharedassets2.resource";
    private static string Hash(Stream input){using var hash=SHA256.Create();return Convert.ToHexString(hash.ComputeHash(input));}
    public static string Build(string gameData,string cacheDirectory,NativeUiPlan plan)
    {
        if(plan.Size is <48 or >64000000 || plan.Objects is <1 or >1500 || plan.Parts.Length is <1 or >12000 || plan.SerializedFiles!=1 || plan.ScriptTypes is <0 or >128 || !NativeAssetMismatch.IsSha256(plan.Sha256))throw NativeAssetMismatch.Create("Recipe","native-gear.bundle","bounded reviewed UI recipe","invalid UI recipe");
        string sourceRoot=Path.GetFullPath(gameData).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string destination=Path.GetFullPath(cacheDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(destination.StartsWith(sourceRoot,StringComparison.OrdinalIgnoreCase))throw NativeAssetMismatch.Create("Recipe","native-gear.bundle","cache outside installed assets","cache overlaps installed assets");
        long total=0,literals=0;
        for(int partIndex=0;partIndex<plan.Parts.Length;partIndex++) {
            var part=plan.Parts[partIndex];
            if(part.Data is {} encoded) {
                if(part.File!=null || encoded.Length>180000)throw NativeAssetMismatch.Create("Recipe","native-gear.bundle","bounded authored metadata patch","invalid patch",partIndex:partIndex);
                byte[] patch;
                try{patch=Convert.FromBase64String(encoded);}
                catch(FormatException ex){throw NativeAssetMismatch.Create("Recipe","native-gear.bundle","base64 metadata patch","invalid encoding",partIndex:partIndex,inner:ex);}
                total+=patch.Length;literals+=patch.Length;
            }else {
                if(part.File==null || NativeAssetMismatch.FileName(part.File)!=part.File || !Source(part.File) || part.Offset<0 || part.Size is <1 or >16000000 || !NativeAssetMismatch.IsSha256(part.Sha256))throw NativeAssetMismatch.Create("Recipe",part.File??"native-gear.bundle","reviewed UI source locator","invalid locator",part.Offset,part.Size,partIndex:partIndex);
                total+=part.Size;
            }
        }
        if(total!=plan.Size || literals>150000)throw NativeAssetMismatch.Create("Recipe","native-gear.bundle",$"total {plan.Size}, authored bytes <= 150000",$"total {total}, authored bytes {literals}");
        var identities=NativeUiIdentity.Create(plan);
        Directory.CreateDirectory(destination);string path=Path.Combine(destination,"native-gear.bundle");
        // Verify the installed ranges on every start, even with an existing
        // cache. A game update must not silently keep using stale native UI.
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        var inputs=new Dictionary<string,FileStream>(StringComparer.Ordinal);
        try {
            using(var output=System.IO.File.Create(temporary))for(int partIndex=0;partIndex<plan.Parts.Length;partIndex++) {
                var part=plan.Parts[partIndex];
                if(part.Data is {} encoded){output.Write(Convert.FromBase64String(encoded));continue;}
                if(!inputs.TryGetValue(part.File!,out var input)) {
                    try{inputs.Add(part.File!,input=System.IO.File.OpenRead(Path.Combine(sourceRoot,part.File!)));}
                    catch(IOException ex){throw NativeAssetMismatch.Create("SourceOpen",part.File!,"readable installed file",ex.GetType().Name,part.ObjectId.HasValue?null:part.Offset,part.Size,part.ObjectId,partIndex,ex);}
                    catch(UnauthorizedAccessException ex){throw NativeAssetMismatch.Create("SourceOpen",part.File!,"readable installed file",ex.GetType().Name,part.ObjectId.HasValue?null:part.Offset,part.Size,part.ObjectId,partIndex,ex);}
                }
                long offset=identities?.Resolve(input,part,partIndex)??part.Offset;
                if(offset>input.Length-part.Size)throw NativeAssetMismatch.Create("SourceRange",part.File!,$"range inside {input.Length} bytes","range outside source",offset,part.Size,part.ObjectId,partIndex);
                input.Position=offset;var bytes=new byte[part.Size];int read=0;
                while(read<bytes.Length){int n=input.Read(bytes,read,bytes.Length-read);if(n==0)throw NativeAssetMismatch.Create("SourceRead",part.File!,part.Size.ToString(),read.ToString(),offset,part.Size,part.ObjectId,partIndex);read+=n;}
                string actual=Convert.ToHexString(SHA256.HashData(bytes));
                if(!actual.Equals(part.Sha256,StringComparison.OrdinalIgnoreCase))throw NativeAssetMismatch.Create("UiSourceSha256",part.File!,part.Sha256!,actual,offset,part.Size,part.ObjectId,partIndex);
                output.Write(bytes);
            }
            using(var built=System.IO.File.OpenRead(temporary)) {
                if(built.Length!=plan.Size)throw NativeAssetMismatch.Create("CacheSize","native-gear.bundle",plan.Size.ToString(),built.Length.ToString());
                string actual=Hash(built);
                if(!actual.Equals(plan.Sha256,StringComparison.OrdinalIgnoreCase))throw NativeAssetMismatch.Create("CacheSha256","native-gear.bundle",plan.Sha256,actual);
            }
            System.IO.File.Move(temporary,path,true);return path;
        }finally {
            foreach(var input in inputs.Values)input.Dispose();
            // Only this invocation's exact staging file can be removed.
            if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);
        }
    }
}
