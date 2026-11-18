using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OperatorKillhouse.Core;

public sealed record NativeStreamPlan(int FieldOffset,string SourceFile,long Offset,int Size,string Sha256);
public sealed record NativeObjectPlan(long Id,string Type,int EntryOffset,long Offset,int Size,string Sha256,NativeStreamPlan? Stream,int? ClassId=null,string? TypeSha256=null);
public sealed record NativeSubsetPlan(string SourceFile,long SourceSize,int MetadataSize,string MetadataSha256,int CountOffset,int TableOffset,int TableCount,string[] Dependencies,NativeObjectPlan[] Objects,int ExternalTableOffset=0,int ExternalTableSize=0,string? UnityVersion=null,int? TargetPlatform=null);
public sealed record NativeSubsetCache(string Assets,string Stream,string AssetsSha256,string StreamSha256,int Objects,long StreamBytes,bool Relocated=false);

// Repackages only reviewed mesh/material/texture objects from installed files.
// Native object IDs/type metadata are retained; no scene or script instances
// enter this cache. This code never writes to the source game asset files.
public static class NativeAssetSubset
{
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static string Input(string directory,string name) {
        if(NativeAssetMismatch.FileName(name)!=name)throw NativeAssetMismatch.Create("Recipe",name,"direct source file name","invalid source name");
        return Path.Combine(directory,name);
    }
    private static FileStream Open(string directory,string name,long? objectId=null) {
        string path=Input(directory,name);
        try{return new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);}
        catch(IOException ex){throw NativeAssetMismatch.Create("SourceOpen",name,"readable installed file",ex.GetType().Name,objectId:objectId,inner:ex);}
        catch(UnauthorizedAccessException ex){throw NativeAssetMismatch.Create("SourceOpen",name,"readable installed file",ex.GetType().Name,objectId:objectId,inner:ex);}
    }
    private static byte[] Read(FileStream input,string sourceFile,long offset,int length,long? objectId=null) {
        if(offset<0 || length<0 || offset>input.Length-length)throw NativeAssetMismatch.Create("SourceRange",sourceFile,$"range inside {input.Length} bytes","range outside source",offset,length,objectId);
        input.Position=offset;var bytes=new byte[length];int read=0;
        while(read<length){int n=input.Read(bytes,read,length-read);if(n==0)throw NativeAssetMismatch.Create("SourceRead",sourceFile,length.ToString(),read.ToString(),offset,length,objectId);read+=n;}
        return bytes;
    }
    private static void Verify(byte[] bytes,string expected,string stage,string sourceFile,long offset,long? objectId=null) {
        string actual=Hash(bytes);
        if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))throw NativeAssetMismatch.Create(stage,sourceFile,expected,actual,offset,bytes.Length,objectId);
    }
    private static int Align(int value,int alignment)=>(value+alignment-1)&~(alignment-1);
    private static void Pad(Stream stream,int alignment){while(stream.Position%alignment!=0)stream.WriteByte(0);}
    private static string CString(BinaryReader reader) {
        var bytes=new List<byte>();for(int i=0;i<1024;i++){byte value=reader.ReadByte();if(value==0)return Encoding.UTF8.GetString(bytes.ToArray());bytes.Add(value);}
        throw new InvalidDataException("Dependency name exceeds its reviewed bound.");
    }
    private static bool CanResolve(NativeSubsetPlan plan) {
        bool proof=plan.UnityVersion!=null || plan.TargetPlatform.HasValue || plan.Objects.Any(o=>o.ClassId.HasValue || o.TypeSha256!=null);
        if(proof && (string.IsNullOrEmpty(plan.UnityVersion) || plan.UnityVersion.Length>128 || !plan.TargetPlatform.HasValue ||
            plan.Objects.Any(o=>o.Type is not ("Mesh" or "Texture2D") || !NativeAssetMismatch.IsSha256(o.TypeSha256) || o.ClassId!=(o.Type switch{"Mesh"=>43,"Texture2D"=>28,_=>-1}))))
            throw NativeAssetMismatch.Create("Recipe",plan.SourceFile,"complete reviewed native visual type identities and engine/platform","incomplete or unsupported relocation proof");
        return proof;
    }
    private static (NativeSubsetPlan Plan,byte[] Metadata) Resolve(FileStream input,NativeSubsetPlan reviewed) {
        NativeSerializedLayout layout;
        try{layout=NativeSerializedLayout.Read(input);}
        catch(InvalidDataException error){throw NativeAssetMismatch.Create("SerializedLayout",reviewed.SourceFile,"bounded reviewed Unity format 22 layout",error.Message,inner:error);}
        if(layout.UnityVersion!=reviewed.UnityVersion || layout.TargetPlatform!=reviewed.TargetPlatform)
            throw NativeAssetMismatch.Create("EngineVersion",reviewed.SourceFile,$"Unity {reviewed.UnityVersion}, platform {reviewed.TargetPlatform}",$"Unity {layout.UnityVersion}, platform {layout.TargetPlatform}");
        var index=layout.Objects.ToDictionary(o=>o.Id);var objects=new NativeObjectPlan[reviewed.Objects.Length];
        for(int i=0;i<objects.Length;i++) {
            var expected=reviewed.Objects[i];
            if(!index.TryGetValue(expected.Id,out var actual))throw NativeAssetMismatch.Create("ObjectMissing",reviewed.SourceFile,"reviewed native object ID","object not present",objectId:expected.Id);
            var type=layout.Types[actual.TypeIndex];
            if(type.ClassId!=expected.ClassId || !type.Sha256.Equals(expected.TypeSha256,StringComparison.OrdinalIgnoreCase))
                throw NativeAssetMismatch.Create("ObjectType",reviewed.SourceFile,$"class {expected.ClassId}, SHA256 {expected.TypeSha256}",$"class {type.ClassId}, SHA256 {type.Sha256}",actual.EntryOffset,24,expected.Id);
            if(actual.Size!=expected.Size)throw NativeAssetMismatch.Create("ObjectSize",reviewed.SourceFile,expected.Size.ToString(),actual.Size.ToString(),actual.EntryOffset,24,expected.Id);
            objects[i]=expected with{EntryOffset=actual.EntryOffset,Offset=actual.Offset};
        }
        // Only locations and unused metadata may change. The normal copy path
        // still verifies every selected body and resource block before writing
        // a cache; stream fields and visual payloads are never inferred.
        var resolved=reviewed with{SourceSize=layout.SourceSize,MetadataSize=layout.MetadataSize,MetadataSha256=Hash(layout.HeaderAndMetadata),
            CountOffset=layout.CountOffset,TableOffset=layout.TableOffset,TableCount=layout.TableCount,Dependencies=layout.Dependencies,
            Objects=objects,ExternalTableOffset=layout.ExternalTableOffset,ExternalTableSize=layout.ExternalTableSize};
        return(resolved,layout.HeaderAndMetadata);
    }
    private static void VerifyDependencies(byte[] metadata,NativeSubsetPlan plan,int objectTableEnd) {
        if(plan.Dependencies.Length>64 || plan.ExternalTableOffset<objectTableEnd+4 || plan.ExternalTableSize<4 ||
            (long)plan.ExternalTableOffset+plan.ExternalTableSize>metadata.Length)throw NativeAssetMismatch.Create("Recipe",plan.SourceFile,"reviewed dependency table locator","invalid locator",plan.ExternalTableOffset,plan.ExternalTableSize);
        int scripts=BinaryPrimitives.ReadInt32LittleEndian(metadata.AsSpan(objectTableEnd));
        if(scripts<0 || scripts>100000 || objectTableEnd+4L+scripts*12L!=plan.ExternalTableOffset)throw NativeAssetMismatch.Create("DependencyTable",plan.SourceFile,plan.ExternalTableOffset.ToString(),(objectTableEnd+4L+scripts*12L).ToString(),objectTableEnd,4);
        using var stream=new MemoryStream(metadata,false);using var reader=new BinaryReader(stream,Encoding.UTF8);
        stream.Position=plan.ExternalTableOffset;
        int actualCount=reader.ReadInt32();
        if(actualCount!=plan.Dependencies.Length)throw NativeAssetMismatch.Create("DependencyCount",plan.SourceFile,plan.Dependencies.Length.ToString(),actualCount.ToString(),plan.ExternalTableOffset,4);
        foreach(string name in plan.Dependencies) {
            if(CString(reader)!="" || reader.ReadBytes(16).Length!=16)throw NativeAssetMismatch.Create("DependencyIdentifier",plan.SourceFile,"reviewed dependency identifier","different identifier",stream.Position);
            reader.ReadInt32();if(CString(reader)!=name)throw NativeAssetMismatch.Create("DependencyOrder",plan.SourceFile,"reviewed dependency order","different order",stream.Position);
        }
        if(stream.Position!=plan.ExternalTableOffset+plan.ExternalTableSize)throw NativeAssetMismatch.Create("DependencyBoundary",plan.SourceFile,(plan.ExternalTableOffset+plan.ExternalTableSize).ToString(),stream.Position.ToString(),plan.ExternalTableOffset,plan.ExternalTableSize);
    }
    public static NativeSubsetCache Build(string gameData,string outputDirectory,NativeSubsetPlan plan,string cacheName) {
        if(Path.GetFileName(cacheName)!=cacheName || !cacheName.EndsWith(".assets",StringComparison.Ordinal))throw new ArgumentException("Invalid cache file name.");
        if(plan.Objects.Length is <1 or >128 || plan.TableCount is <1 or >100000 || plan.Objects.Select(o=>o.Id).Distinct().Count()!=plan.Objects.Length ||
            plan.Objects.Any(o=>o.Type is not ("Mesh" or "Texture2D" or "AnimationClip" or "AnimatorController") || !NativeAssetMismatch.IsSha256(o.Sha256) || (o.Stream is {} s && !NativeAssetMismatch.IsSha256(s.Sha256))))throw NativeAssetMismatch.Create("Recipe",plan.SourceFile,"bounded script-free visual object set with SHA256 identities","invalid selected object set");
        string inputRoot=Path.GetFullPath(gameData).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string outputRoot=Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(outputRoot.StartsWith(inputRoot,StringComparison.OrdinalIgnoreCase))throw NativeAssetMismatch.Create("Recipe",plan.SourceFile,"cache outside installed assets","cache overlaps installed assets");
        if(plan.SourceSize<48 || !NativeAssetMismatch.IsSha256(plan.MetadataSha256) || plan.MetadataSize is <1 or >8000000 || plan.TableOffset<48 || plan.CountOffset<48 ||
            (long)plan.CountOffset+4>plan.TableOffset || (long)plan.TableOffset-plan.CountOffset-4>3 ||
            (long)plan.TableOffset+plan.TableCount*24>48L+plan.MetadataSize)throw NativeAssetMismatch.Create("Recipe",plan.SourceFile,"reviewed serialized metadata/table bounds","invalid recipe bounds");
        bool canResolve=CanResolve(plan),relocated=false;
        using var input=Open(gameData,plan.SourceFile);
        byte[] headerAndMetadata;
        if(input.Length!=plan.SourceSize) {
            if(!canResolve)throw NativeAssetMismatch.Create("SourceSize",plan.SourceFile,plan.SourceSize.ToString(),input.Length.ToString());
            (plan,headerAndMetadata)=Resolve(input,plan);relocated=true;
        }else {
            headerAndMetadata=Read(input,plan.SourceFile,0,48+plan.MetadataSize);
            if(canResolve && !Hash(headerAndMetadata).Equals(plan.MetadataSha256,StringComparison.OrdinalIgnoreCase)) {
                (plan,headerAndMetadata)=Resolve(input,plan);relocated=true;
            }else Verify(headerAndMetadata,plan.MetadataSha256,"MetadataSha256",plan.SourceFile,0);
        }
        int format=BinaryPrimitives.ReadInt32BigEndian(headerAndMetadata.AsSpan(8)),endian=headerAndMetadata[16],tableCount=BinaryPrimitives.ReadInt32LittleEndian(headerAndMetadata.AsSpan(plan.CountOffset));
        if(format!=22 || endian!=0 || tableCount!=plan.TableCount)throw NativeAssetMismatch.Create("SerializedFormat",plan.SourceFile,$"format 22, endian 0, table count {plan.TableCount}",$"format {format}, endian {endian}, table count {tableCount}");
        int originalTableEnd=plan.TableOffset+plan.TableCount*24;
        VerifyDependencies(headerAndMetadata,plan,originalTableEnd);
        using var streamData=new MemoryStream();using var objectData=new MemoryStream();
        var entries=new List<byte[]>();string streamName=cacheName+".resS";
        foreach(var obj in plan.Objects.OrderBy(o=>o.EntryOffset)) {
            if(obj.EntryOffset<plan.TableOffset || (obj.EntryOffset-plan.TableOffset)%24!=0 || obj.EntryOffset>=plan.TableOffset+plan.TableCount*24 || obj.Size is <1 or >16000000)throw NativeAssetMismatch.Create("Recipe",plan.SourceFile,"reviewed selected object record","invalid record",obj.EntryOffset,24,obj.Id);
            var entry=headerAndMetadata.AsSpan(obj.EntryOffset,24).ToArray();
            long originalDataOffset=BinaryPrimitives.ReadInt64BigEndian(headerAndMetadata.AsSpan(32));
            long actualId=BinaryPrimitives.ReadInt64LittleEndian(entry),actualOffset=BinaryPrimitives.ReadInt64LittleEndian(entry.AsSpan(8))+originalDataOffset;int actualSize=BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(16));
            if(actualId!=obj.Id || actualOffset!=obj.Offset || actualSize!=obj.Size)throw NativeAssetMismatch.Create("ObjectLocator",plan.SourceFile,$"id {obj.Id}, offset {obj.Offset}, size {obj.Size}",$"id {actualId}, offset {actualOffset}, size {actualSize}",obj.EntryOffset,24,obj.Id);
            var body=Read(input,plan.SourceFile,obj.Offset,obj.Size,obj.Id);Verify(body,obj.Sha256,"ObjectSha256",plan.SourceFile,obj.Offset,obj.Id);
            if(obj.Stream is {} block) {
                if(block.Size is <1 or >64000000 || block.FieldOffset<0 || (long)block.FieldOffset+16>body.Length || streamData.Length+block.Size>128000000)throw NativeAssetMismatch.Create("Recipe",block.SourceFile,"bounded resource stream and valid patch field","invalid stream recipe",block.Offset,block.Size,obj.Id);
                using var resource=Open(gameData,block.SourceFile,obj.Id);
                var bytes=Read(resource,block.SourceFile,block.Offset,block.Size,obj.Id);Verify(bytes,block.Sha256,"ResourceSha256",block.SourceFile,block.Offset,obj.Id);
                int oldLength=BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(block.FieldOffset+12));
                if(oldLength<0 || oldLength>256 || block.FieldOffset+16+Align(oldLength,4)>body.Length ||
                    BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(block.FieldOffset))!=block.Offset || BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(block.FieldOffset+8))!=block.Size ||
                    Encoding.UTF8.GetString(body,block.FieldOffset+16,oldLength)!=block.SourceFile)throw NativeAssetMismatch.Create("StreamField",plan.SourceFile,"reviewed stream field","different stream field",obj.Offset+block.FieldOffset,16,obj.Id);
                using var replacement=new MemoryStream();using var writer=new BinaryWriter(replacement,Encoding.UTF8,true);
                writer.Write(body,0,block.FieldOffset);writer.Write(streamData.Position);writer.Write(block.Size);
                var path=Encoding.UTF8.GetBytes(streamName);writer.Write(path.Length);writer.Write(path);Pad(replacement,4);
                int after=block.FieldOffset+16+Align(oldLength,4);writer.Write(body,after,body.Length-after);body=replacement.ToArray();
                streamData.Write(bytes);Pad(streamData,16);
            }
            Pad(objectData,8);BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(8),objectData.Position);
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(16),body.Length);objectData.Write(body);entries.Add(entry);
        }
        using var metadata=new MemoryStream();metadata.Write(headerAndMetadata,48,plan.TableOffset-48);
        foreach(var entry in entries)metadata.Write(entry);
        // The selected hash-locked objects have only local references. Materials
        // are recreated with the loaded pipeline shader instead of importing its
        // external PPtr. Do not inherit unused script/external dependency tables.
        metadata.Write(new byte[8]); // zero script-reference count, zero external-file count
        int suffix=plan.ExternalTableOffset+plan.ExternalTableSize;
        metadata.Write(headerAndMetadata,suffix,headerAndMetadata.Length-suffix);
        var metadataBytes=metadata.ToArray();BinaryPrimitives.WriteInt32LittleEndian(metadataBytes.AsSpan(plan.CountOffset-48),entries.Count);
        int dataOffset=Align(48+metadataBytes.Length,16);long fileLength=dataOffset+objectData.Length;
        var header=headerAndMetadata.AsSpan(0,48).ToArray();
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20),metadataBytes.Length);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(24),fileLength);BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(32),dataOffset);
        using var result=new MemoryStream();result.Write(header);result.Write(metadataBytes);Pad(result,16);objectData.Position=0;objectData.CopyTo(result);
        Directory.CreateDirectory(outputDirectory);string assets=Path.Combine(outputDirectory,cacheName),stream=Path.Combine(outputDirectory,streamName);
        // Unique staging files avoid damaging an existing usable cache if a
        // source check fails or two startup attempts overlap.
        string token="."+Guid.NewGuid().ToString("N")+".tmp";
        var assetBytes=result.ToArray();var streamBytes=streamData.ToArray();
        File.WriteAllBytes(assets+token,assetBytes);File.WriteAllBytes(stream+token,streamBytes);
        File.Move(stream+token,stream,true);File.Move(assets+token,assets,true);
        return new(assets,stream,Hash(assetBytes),Hash(streamBytes),entries.Count,streamBytes.Length,relocated);
    }
}
