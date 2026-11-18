using System.Security.Cryptography;

namespace OperatorKillhouse.Core;

public sealed record NativeUiFilePlan(string File,string UnityVersion,int TargetPlatform,int ObjectLimit=200000);
public sealed record NativeUiObjectPlan(string File,long Id,int Size,int ClassId,string TypeSha256,string Sha256);

// Resolves reviewed UI objects without trusting their old byte addresses. The
// native UI imports scripts, so both complete object bodies and serialized type
// descriptors remain hash locked before any original source slice is copied.
internal sealed class NativeUiIdentity
{
    private sealed record FileIndex(NativeSerializedLayout Layout,Dictionary<long,NativeSerializedObject> Objects);
    private readonly Dictionary<string,NativeUiFilePlan> files;
    private readonly Dictionary<(string File,long Id),NativeUiObjectPlan> objects;
    private readonly Dictionary<string,FileIndex> indexes=new(StringComparer.Ordinal);
    private readonly HashSet<(string File,long Id)> verified=new();

    internal static bool IsSerialized(string file)=>file is "level2" or "globalgamemanagers.assets" or "resources.assets" or
        "sharedassets0.assets" or "sharedassets1.assets" or "sharedassets2.assets";
    private static bool IsResource(string file)=>file.EndsWith(".resS",StringComparison.Ordinal) || file.EndsWith(".resource",StringComparison.Ordinal);

    internal static NativeUiIdentity? Create(NativeUiPlan plan)
    {
        bool enabled=plan.SourceFiles!=null || plan.ObjectSources!=null || plan.Parts.Any(p=>p.ObjectId.HasValue || p.RelativeOffset.HasValue);
        return enabled?new NativeUiIdentity(plan):null;
    }

    private NativeUiIdentity(NativeUiPlan plan)
    {
        if(plan.SourceFiles is not {Length:>=1 and <=6} || plan.ObjectSources is not {Length:>=1 and <=1500})
            throw NativeAssetMismatch.Create("Recipe","native-gear.bundle","complete bounded UI file and object identities","missing or oversized identity proof");
        files=new(StringComparer.Ordinal);objects=new();
        foreach(var file in plan.SourceFiles) {
            if(file is null || NativeAssetMismatch.FileName(file.File)!=file.File || !IsSerialized(file.File) ||
                string.IsNullOrEmpty(file.UnityVersion) || file.UnityVersion.Length>128 || file.UnityVersion.Any(char.IsControl) ||
                file.ObjectLimit is <1 or >200000 || !files.TryAdd(file.File,file))
                throw NativeAssetMismatch.Create("Recipe",file?.File??"native-gear.bundle","unique reviewed serialized file, engine and object budget","invalid file identity");
        }
        long bytes=0;
        foreach(var obj in plan.ObjectSources) {
            if(obj is null || NativeAssetMismatch.FileName(obj.File)!=obj.File || !files.ContainsKey(obj.File) || obj.Size is <1 or >16000000 || obj.ClassId<1 ||
                !NativeAssetMismatch.IsSha256(obj.TypeSha256) || !NativeAssetMismatch.IsSha256(obj.Sha256) || !objects.TryAdd((obj.File,obj.Id),obj))
                throw NativeAssetMismatch.Create("Recipe",obj?.File??"native-gear.bundle","unique bounded object with file, class and SHA256 proofs","invalid object identity",objectId:obj?.Id);
            bytes+=obj.Size;
        }
        if(bytes>128000000)throw NativeAssetMismatch.Create("Recipe","native-gear.bundle","reviewed source object bytes <= 128000000",bytes.ToString());
        for(int i=0;i<plan.Parts.Length;i++) {
            var part=plan.Parts[i];
            if(part.Data!=null || (part.File is {} raw && IsResource(raw))) {
                if(part.ObjectId.HasValue || part.RelativeOffset.HasValue)
                    throw NativeAssetMismatch.Create("Recipe",part.File??"native-gear.bundle","no object locator on authored patches or raw resource spans","unexpected object locator",partIndex:i);
                continue;
            }
            if(part.File is null || !IsSerialized(part.File) || !part.ObjectId.HasValue || !part.RelativeOffset.HasValue ||
                !objects.TryGetValue((part.File,part.ObjectId.Value),out var source) || part.RelativeOffset.Value<0 ||
                part.RelativeOffset.Value>(long)source.Size-part.Size)
                throw NativeAssetMismatch.Create("Recipe",part.File??"native-gear.bundle","complete reviewed source object and relative byte range","invalid object locator",part.RelativeOffset,part.Size,part.ObjectId,i);
        }
    }

    internal long Resolve(FileStream input,NativeUiPart part,int partIndex)
    {
        if(IsResource(part.File!))return part.Offset;
        string file=part.File!;long id=part.ObjectId!.Value;
        var expected=objects[(file,id)];var filePlan=files[file];
        if(!indexes.TryGetValue(file,out var index)) {
            NativeSerializedLayout layout;
            try{layout=NativeSerializedLayout.Read(input,filePlan.ObjectLimit);}
            catch(InvalidDataException error){throw NativeAssetMismatch.Create("UiSourceLayout",file,$"reviewed Unity format 22, at most {filePlan.ObjectLimit} objects","invalid serialized layout",objectId:id,partIndex:partIndex,inner:error);}
            if(layout.UnityVersion!=filePlan.UnityVersion || layout.TargetPlatform!=filePlan.TargetPlatform)
                throw NativeAssetMismatch.Create("UiEngineVersion",file,$"Unity {filePlan.UnityVersion}, platform {filePlan.TargetPlatform}",$"Unity {layout.UnityVersion}, platform {layout.TargetPlatform}",objectId:id,partIndex:partIndex);
            index=new(layout,layout.Objects.ToDictionary(o=>o.Id));indexes.Add(file,index);
        }
        if(!index.Objects.TryGetValue(id,out var actual))
            throw NativeAssetMismatch.Create("UiObjectMissing",file,"reviewed native object ID","object not present",objectId:id,partIndex:partIndex);
        var type=index.Layout.Types[actual.TypeIndex];
        if(type.ClassId!=expected.ClassId || !type.Sha256.Equals(expected.TypeSha256,StringComparison.OrdinalIgnoreCase))
            throw NativeAssetMismatch.Create("UiObjectType",file,$"class {expected.ClassId}, SHA256 {expected.TypeSha256}",$"class {type.ClassId}, SHA256 {type.Sha256}",actual.Offset,actual.Size,id,partIndex);
        if(actual.Size!=expected.Size)
            throw NativeAssetMismatch.Create("UiObjectSize",file,expected.Size.ToString(),actual.Size.ToString(),actual.Offset,actual.Size,id,partIndex);
        if(!verified.Contains((file,id))) {
            long saved=input.Position;
            try {
                input.Position=actual.Offset;int read=0;var buffer=new byte[Math.Min(actual.Size,65536)];
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while(read<actual.Size) {
                    int count=input.Read(buffer,0,Math.Min(actual.Size-read,buffer.Length));
                    if(count==0)throw NativeAssetMismatch.Create("SourceRead",file,actual.Size.ToString(),read.ToString(),actual.Offset,actual.Size,id,partIndex);
                    hash.AppendData(buffer,0,count);read+=count;
                }
                string actualHash=Convert.ToHexString(hash.GetHashAndReset());
                if(!actualHash.Equals(expected.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw NativeAssetMismatch.Create("UiObjectSha256",file,expected.Sha256,actualHash,actual.Offset,actual.Size,id,partIndex);
                verified.Add((file,id));
            }finally{input.Position=saved;}
        }
        return actual.Offset+part.RelativeOffset!.Value;
    }
}
