using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OperatorKillhouse.Core;

// Reports reuse the asset loaders' validation results. This worker reads only
// small identities, Steam's manifest and source lengths; it never scans native
// asset payloads or touches Unity objects.
public sealed class NativeAssetReportSession
{
    private const int MaxGroups=16,MaxSources=64,MaxManifestBytes=1048576;
    private readonly object gate=new();
    private readonly string version,unityVersion,assemblyPath,gameData,reportDirectory;
    private readonly string? gameVersion;
    private readonly string[] sourceFiles;
    private readonly Action<Exception>? onWriteError;
    private readonly string session=Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset createdUtc=DateTimeOffset.UtcNow;
    private readonly Dictionary<string,LoadState> groups=new(StringComparer.Ordinal);
    private Task? worker;
    private long revision=1,processed;
    private string? gameAssemblySha256;
    private Identity? identity;
    private sealed record LoadState(string Group,string State,DateTimeOffset UpdatedUtc,NativeAssetMismatch? Mismatch,string? Reason,int? Objects);
    private sealed record FileIdentity(string Name,long? Length,string? Sha256,string? AssemblyVersion,string? Error);
    private sealed record SourceIdentity(string Name,long? Length,string? Error);
    private sealed record DepotIdentity(string DepotId,string ManifestId);
    private sealed record SteamIdentity(bool Found,string? AppId,string? BuildId,string? BetaKey,string? Language,DepotIdentity[] InstalledDepots,string? Error);
    private sealed record Identity(FileIdentity Dll,SteamIdentity Steam,SourceIdentity[] Sources);

    public NativeAssetReportSession(string version,string unityVersion,string assemblyPath,string gameData,string reportDirectory,
        IEnumerable<string>? sourceFiles=null,Action<Exception>? onWriteError=null,string? gameVersion=null)
    {
        this.version=SafeText(version,64);this.unityVersion=SafeText(unityVersion,64);
        this.gameVersion=gameVersion is null?null:SafeText(gameVersion,64);
        this.assemblyPath=assemblyPath;this.gameData=gameData;this.reportDirectory=reportDirectory;this.onWriteError=onWriteError;
        this.sourceFiles=(sourceFiles??Array.Empty<string>()).Take(256).Select(NativeAssetMismatch.FileName)
            .Where(n=>n!="(invalid source name)").Distinct(StringComparer.Ordinal).Take(MaxSources).ToArray();
        lock(gate)Queue();
    }

    // Calls from Unity or asset workers only copy bounded values under a short
    // lock. All file access, hashing and JSON serialization happens on worker.
    public void Record(string group,string state,NativeAssetMismatch? mismatch=null,string? reason=null,int? objects=null)
    {
        string key=SafeText(group,64);if(key.Length==0)return;
        var detail=mismatch is null?null:new NativeAssetMismatch(SafeText(mismatch.Stage,64),NativeAssetMismatch.FileName(mismatch.SourceFile),
            mismatch.Offset,mismatch.Length,SafeText(mismatch.Expected,256),SafeText(mismatch.Actual,256),mismatch.ObjectId,mismatch.PartIndex);
        lock(gate) {
            if(!groups.ContainsKey(key) && groups.Count>=MaxGroups)return;
            groups[key]=new(key,SafeText(state,64),DateTimeOffset.UtcNow,detail,reason is null?null:SafeText(reason,256),objects);
            revision++;Queue();
        }
    }

    public void RecordGameAssembly(string sha256)
    {
        lock(gate){gameAssemblySha256=ValidHash(sha256)?sha256.ToLowerInvariant():"(invalid hash)";revision++;Queue();}
    }

    public Task FlushAsync()
    {
        lock(gate){Queue();return worker??Task.CompletedTask;}
    }

    private void Queue(){if(worker is null && processed<revision)worker=Task.Run(WritePending);}

    private void WritePending()
    {
        // The identity snapshot is stable for this mod session, even if files
        // change afterward. Actual loader failures retain their own identities.
        var captured=identity??CaptureIdentity();
        while(true) {
            long writing;LoadState[] states;string? binary;
            lock(gate){identity=captured;writing=revision;states=groups.Values.OrderBy(v=>v.Group,StringComparer.Ordinal).ToArray();binary=gameAssemblySha256;}
            try {
                var report=new{schema=1,session,createdUtc,updatedUtc=DateTimeOffset.UtcNow,version,unityVersion,gameVersion,
                    dll=captured.Dll,steam=captured.Steam,sources=captured.Sources,gameAssemblySha256=binary,groups=states};
                WriteAtomic(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}),writing);
            }catch(Exception error){try{onWriteError?.Invoke(error);}catch{/* Diagnostics cannot fail the game. */}}
            lock(gate){processed=writing;if(revision==writing){worker=null;return;}}
        }
    }

    private Identity CaptureIdentity()
    {
        var dll=ReadDll();var steam=ReadSteam();
        var sources=sourceFiles.Select(name=>{
            try{return new SourceIdentity(name,new FileInfo(Path.Combine(gameData,name)).Length,null);}
            catch(Exception error){return new SourceIdentity(name,null,ErrorCode(error));}
        }).ToArray();
        return new(dll,steam,sources);
    }

    private FileIdentity ReadDll()
    {
        string name=SafeFileName(assemblyPath);long? length=null;string? hash=null,assemblyVersion=null;
        try {
            using var input=new FileStream(assemblyPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            length=input.Length;
            // A mod DLL is small. Refuse a mistaken path to a native asset file.
            if(length>64000000)return new(name,length,null,null,"IdentityFileTooLarge");
            using(var sha=SHA256.Create())hash=Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant();
            try{assemblyVersion=AssemblyName.GetAssemblyName(assemblyPath).Version?.ToString();}catch(BadImageFormatException){}
            return new(name,length,hash,assemblyVersion,null);
        }catch(Exception error){return new(name,length,hash,assemblyVersion,ErrorCode(error));}
    }

    private SteamIdentity ReadSteam()
    {
        SteamIdentity Missing(string error)=>new(false,null,null,null,null,Array.Empty<DepotIdentity>(),error);
        try {
            var game=Directory.GetParent(gameData);var common=game?.Parent;
            if(game is null || common is null || !common.Name.Equals("common",StringComparison.OrdinalIgnoreCase) || common.Parent is null)return Missing("ManifestNotFound");
            KvNode? ReadCandidate(string path) {
                try {
                    using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                    if(input.Length>MaxManifestBytes)return null;
                    var bytes=new byte[(int)input.Length];int read=0;
                    while(read<bytes.Length){int count=input.Read(bytes,read,bytes.Length-read);if(count==0)throw new EndOfStreamException();read+=count;}
                    var parsed=KvNode.Parse(Encoding.UTF8.GetString(bytes));
                    var app=parsed.Children.GetValueOrDefault("AppState");
                    return app?.Values.GetValueOrDefault("installdir")?.Equals(game.Name,StringComparison.OrdinalIgnoreCase)==true?app:null;
                }catch(IOException){}catch(InvalidDataException){}catch(UnauthorizedAccessException){}
                return null;
            }
            // Resolve OPERATOR directly so a large Steam library does not need
            // enumeration. A manifest must still identify this installation.
            string knownPath=Path.Combine(common.Parent.FullName,"appmanifest_1913370.acf");
            var selected=ReadCandidate(knownPath);
            if(selected?.Values.GetValueOrDefault("appid")!="1913370")selected=null;
            if(selected is null) {
                var matches=new List<KvNode>();int scanned=0;
                foreach(string path in Directory.EnumerateFiles(common.Parent.FullName,"appmanifest_*.acf",SearchOption.TopDirectoryOnly)) {
                    if(path.Equals(knownPath,StringComparison.OrdinalIgnoreCase))continue;
                    if(++scanned>128)return Missing("ManifestSearchLimit");
                    if(ReadCandidate(path) is {} candidate)matches.Add(candidate);
                }
                if(matches.Count!=1)return Missing(matches.Count==0?"ManifestNotFound":"ManifestAmbiguous");
                selected=matches[0];
            }
            string? Number(KvNode node,string key)=>Digits(node.Values.GetValueOrDefault(key));
            string? Text(KvNode node,string key)=>node.Values.TryGetValue(key,out var value)?SafeText(value,64):null;
            var depots=selected.Children.GetValueOrDefault("InstalledDepots")?.Children
                .Select(p=>new DepotIdentity(Digits(p.Key)??"",Number(p.Value,"manifest")??""))
                .Where(p=>p.DepotId.Length>0 && p.ManifestId.Length>0).Take(128).OrderBy(p=>p.DepotId,StringComparer.Ordinal).ToArray()??Array.Empty<DepotIdentity>();
            var config=selected.Children.GetValueOrDefault("UserConfig");
            return new(true,Number(selected,"appid"),Number(selected,"buildid"),config is null?null:Text(config,"betakey"),
                config is null?null:Text(config,"language"),depots,null);
        }catch(Exception error){return Missing(ErrorCode(error));}
    }

    private void WriteAtomic(string json,long writing)
    {
        Directory.CreateDirectory(reportDirectory);
        string path=Path.Combine(reportDirectory,"asset-load-report.json"),temporary=path+"."+session+"."+writing+".tmp";
        try{File.WriteAllText(temporary,json,new UTF8Encoding(false));File.Move(temporary,path,true);}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{/* Only the owned staging file is eligible. */}}
    }

    private static bool ValidHash(string? value)=>value?.Length==64 && value.All(Uri.IsHexDigit);
    private static string? Digits(string? value)=>value is {Length:>0 and <=24} && value.All(c=>c is >= '0' and <= '9')?value:null;
    private static string SafeFileName(string path){try{return NativeAssetMismatch.FileName(Path.GetFileName(path));}catch{return "(invalid source name)";}}
    private static string SafeText(string? value,int maximum)
    {
        if(value is null)return "";
        // Raw exception text can contain player-specific installation paths.
        if(value.IndexOfAny(new[]{'/', '\\', ':'})>=0)return "(details omitted)";
        return new string(value.Where(c=>!char.IsControl(c)).Take(maximum).ToArray());
    }
    private static string ErrorCode(Exception error)=>error switch {
        FileNotFoundException or DirectoryNotFoundException=>"MissingFile",
        UnauthorizedAccessException=>"AccessDenied",
        IOException=>"ReadOrWriteFailed",
        ArgumentException or NotSupportedException=>"InvalidPathOrData",
        _=>"IdentityUnavailable"
    };

    // Steam KeyValues is a small nested text format. Only an allowlisted set of
    // scalars is exported; owner IDs, usernames and full manifests stay local.
    private sealed class KvNode
    {
        internal readonly Dictionary<string,string> Values=new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string,KvNode> Children=new(StringComparer.OrdinalIgnoreCase);
        internal static KvNode Parse(string text)
        {
            int at=text.Length>0 && text[0]=='\ufeff'?1:0,tokens=0;
            string? Token() {
                while(at<text.Length && char.IsWhiteSpace(text[at]))at++;
                if(at==text.Length)return null;
                if(++tokens>16384)throw new InvalidDataException("Manifest token limit.");
                char first=text[at++];if(first is '{' or '}')return first.ToString();
                if(first!='"')throw new InvalidDataException("Invalid manifest token.");
                var result=new StringBuilder();
                while(at<text.Length) {
                    char c=text[at++];if(c=='"')return result.ToString();
                    if(c=='\\' && at<text.Length)c=text[at++];
                    if(result.Length>=512)throw new InvalidDataException("Manifest field limit.");result.Append(c);
                }
                throw new InvalidDataException("Incomplete manifest field.");
            }
            KvNode Read(int depth,bool nested) {
                if(depth>12)throw new InvalidDataException("Manifest nesting limit.");
                var result=new KvNode();
                while(Token() is {} key) {
                    if(key=="}"){if(nested)return result;throw new InvalidDataException("Unexpected manifest boundary.");}
                    if(key=="{")throw new InvalidDataException("Missing manifest key.");
                    string value=Token()??throw new InvalidDataException("Missing manifest value.");
                    if(value=="{")result.Children.TryAdd(key,Read(depth+1,true));
                    else if(value=="}")throw new InvalidDataException("Missing manifest value.");
                    else result.Values.TryAdd(key,value);
                }
                if(nested)throw new InvalidDataException("Incomplete manifest block.");return result;
            }
            return Read(0,false);
        }
    }
}
