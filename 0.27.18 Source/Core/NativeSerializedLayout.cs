using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OperatorKillhouse.Core;

public sealed record NativeSerializedType(int ClassId,bool Stripped,short ScriptTypeIndex,string Sha256);
public sealed record NativeSerializedObject(long Id,int EntryOffset,long Offset,int Size,int TypeIndex);
public sealed record NativeSerializedLayout(byte[] HeaderAndMetadata,long SourceSize,int MetadataSize,string UnityVersion,int TargetPlatform,
    int CountOffset,int TableOffset,int TableCount,int ExternalTableOffset,int ExternalTableSize,string[] Dependencies,
    NativeSerializedType[] Types,NativeSerializedObject[] Objects)
{
    // This reader resolves locations only. Callers must independently approve
    // each selected type descriptor and object/stream payload before loading it.
    // Unselected objects, scripts and dependencies are never loaded here.
    public static NativeSerializedLayout Read(Stream input,int maxObjects=100000) {
        if(maxObjects is <1 or >200000)throw new InvalidDataException("Native serialized object limit exceeds its approved bound.");
        if(!input.CanRead || !input.CanSeek)throw new InvalidDataException("Native asset input must be readable and seekable.");
        long saved=input.Position;
        try {
            long length=input.Length;
            if(length<48)throw new InvalidDataException("Truncated Unity serialized asset header.");
            input.Position=0;var header=new byte[48];ReadExact(input,header,0,header.Length);
            if(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8))!=22 || header[16]!=0)
                throw new InvalidDataException("Native layout resolution supports only little-endian Unity format 22.");
            int metadataSize=BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20));
            long declaredSize=BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(24));
            long dataOffset=BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(32));
            if(metadataSize is <1 or >8000000 || declaredSize!=length || dataOffset<48L+metadataSize || dataOffset>length)
                throw new InvalidDataException("Invalid Unity serialized asset boundaries.");
            var bytes=new byte[48+metadataSize];header.CopyTo(bytes,0);ReadExact(input,bytes,48,metadataSize);
            var cursor=new Cursor(bytes,48);
            string unityVersion=cursor.CString(128);
            if(unityVersion.Length==0)throw new InvalidDataException("Missing Unity serialized asset version.");
            int platform=cursor.Int32();byte typeTree=cursor.Byte();
            if(typeTree!=0)throw new InvalidDataException("Native layout resolution requires the reviewed stripped type-tree format.");
            int typeCount=cursor.Count(10000,"type");
            if(typeCount==0)throw new InvalidDataException("Missing Unity serialized types.");
            var types=new NativeSerializedType[typeCount];
            for(int i=0;i<types.Length;i++)types[i]=cursor.Type(false);
            int countOffset=cursor.Position,objectCount=cursor.Count(maxObjects,"object");cursor.Align4();
            int tableOffset=cursor.Position;
            cursor.Require((long)objectCount*24);
            var objects=new NativeSerializedObject[objectCount];var ids=new HashSet<long>();
            for(int i=0;i<objects.Length;i++) {
                int entry=cursor.Position;long id=cursor.Int64(),relative=cursor.Int64();int size=cursor.Int32(),type=cursor.Int32();
                if(!ids.Add(id))throw new InvalidDataException("Duplicate Unity serialized object ID.");
                if(relative<0 || size<0 || relative>length-dataOffset || size>length-dataOffset-relative || type<0 || type>=types.Length)
                    throw new InvalidDataException("Invalid Unity serialized object range or type.");
                objects[i]=new(id,entry,dataOffset+relative,size,type);
            }
            int scriptCount=cursor.Count(100000,"script reference");
            cursor.Skip((long)scriptCount*12);
            int externalOffset=cursor.Position,externalCount=cursor.Count(64,"external dependency");
            var dependencies=new string[externalCount];
            for(int i=0;i<dependencies.Length;i++) {
                if(cursor.CString(1024).Length!=0)throw new InvalidDataException("Unexpected Unity external dependency identifier.");
                cursor.Skip(16);int kind=cursor.Int32();
                if(kind is <0 or >3)throw new InvalidDataException("Unknown Unity external dependency type.");
                dependencies[i]=cursor.CString(1024);
            }
            int externalSize=cursor.Position-externalOffset,referenceCount=cursor.Count(10000,"reference type");
            for(int i=0;i<referenceCount;i++)cursor.Type(true);
            cursor.CString(4096);
            if(cursor.Position!=bytes.Length)throw new InvalidDataException("Unexpected Unity serialized metadata suffix.");
            return new(bytes,length,metadataSize,unityVersion,platform,countOffset,tableOffset,objectCount,externalOffset,externalSize,dependencies,types,objects);
        }catch(EndOfStreamException error){throw new InvalidDataException("Truncated Unity serialized asset metadata.",error);}
        finally{input.Position=saved;}
    }

    private static void ReadExact(Stream input,byte[] bytes,int offset,int length) {
        int read=0;while(read<length){int count=input.Read(bytes,offset+read,length-read);if(count==0)throw new EndOfStreamException();read+=count;}
    }

    private sealed class Cursor
    {
        private static readonly UTF8Encoding Utf8=new(false,true);
        private readonly byte[] bytes;
        internal int Position {get;private set;}
        internal Cursor(byte[] bytes,int position){this.bytes=bytes;Position=position;}
        internal void Require(long length) {if(length<0 || length>bytes.Length-Position)throw new InvalidDataException("Unity serialized metadata exceeds its boundary.");}
        internal byte Byte(){Require(1);return bytes[Position++];}
        internal short Int16(){Require(2);short value=BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(Position));Position+=2;return value;}
        internal int Int32(){Require(4);int value=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(Position));Position+=4;return value;}
        internal long Int64(){Require(8);long value=BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(Position));Position+=8;return value;}
        internal int Count(int limit,string label){int count=Int32();if(count<0 || count>limit)throw new InvalidDataException("Unity serialized "+label+" count exceeds its bound.");return count;}
        internal void Skip(long length){Require(length);Position+=(int)length;}
        internal void Align4(){Skip((4-Position%4)%4);}
        internal string CString(int limit) {
            int start=Position;
            for(int i=0;i<limit;i++)if(Byte()==0) {
                try{return Utf8.GetString(bytes,start,Position-start-1);}
                catch(DecoderFallbackException error){throw new InvalidDataException("Invalid UTF-8 in Unity serialized metadata.",error);}
            }
            throw new InvalidDataException("Unity serialized metadata string exceeds its bound.");
        }
        internal NativeSerializedType Type(bool reference) {
            int start=Position,classId=Int32();byte stripped=Byte();short script=Int16();
            if(stripped>1)throw new InvalidDataException("Invalid Unity serialized type flag.");
            if((reference && script>=0) || (!reference && classId==114))Skip(16);
            Skip(16);
            return new(classId,stripped!=0,script,Convert.ToHexString(SHA256.HashData(bytes.AsSpan(start,Position-start))));
        }
    }
}
