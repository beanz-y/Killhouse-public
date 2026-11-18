using System.Text.Json;

namespace OperatorKillhouse.Core;

public sealed record DeletedMap(string Name,string Folder,int Files);
public static class MapDeletion
{
    private static string State(string data)=>Path.Combine(Path.GetFullPath(data),"deleted-maps.json");
    public static HashSet<string> Read(string data) {
        string file=State(data);if(!File.Exists(file))return new(StringComparer.Ordinal);
        if(new FileInfo(file).Length>1048576)throw new InvalidDataException("Deleted-map list is too large.");
        var names=JsonSerializer.Deserialize<string[]>(File.ReadAllText(file))??throw new InvalidDataException("Invalid deleted-map list.");
        if(names.Length>4096 || names.Any(n=>n==null || n.Length>512))throw new InvalidDataException("Invalid deleted-map names.");
        return new(names.Select(NamedMaps.Key),StringComparer.Ordinal);
    }
    private static void Write(string data,HashSet<string> names) {
        Directory.CreateDirectory(data);string path=State(data),temp=path+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(names.OrderBy(n=>n,StringComparer.Ordinal)));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static void Unhide(string name,string data) {var names=Read(data);if(names.Remove(NamedMaps.Key(name)))Write(data,names);}
    public static DeletedMap Delete(string name,string library,string data) {
        string key=NamedMaps.Key(name);if(key.Length==0 || key.Length>512)throw new InvalidOperationException("Invalid map name.");
        library=Path.GetFullPath(library);data=Path.GetFullPath(data);var hidden=Read(data);
        if(!hidden.Contains(key) && hidden.Count>=4096)throw new InvalidOperationException("Deleted-map list is full.");
        var files=Directory.Exists(library)?Directory.EnumerateFiles(library,"*.txt",SearchOption.TopDirectoryOnly).Take(1025).ToArray():Array.Empty<string>();
        if(files.Length>1024)throw new InvalidOperationException("Too many library files to safely delete a named map.");
        var matching=new List<string>();
        foreach(string file in files) {
            if(!string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)),library,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Map path is outside the writable library.");
            if(Path.GetFileNameWithoutExtension(file).Equals("README",StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length>262144)continue;
            var parsed=LayoutFormat.Read(File.ReadAllText(file),file);
            if(parsed.Success && NamedMaps.Key(parsed.Layout!.Name)==key)matching.Add(file);
        }
        string trash=Path.Combine(data,"deleted-maps",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        var moved=new List<(string Original,string Backup)>();
        try {
            if(matching.Count>0)Directory.CreateDirectory(trash);
            foreach(string file in matching){string target=Path.Combine(trash,Path.GetFileName(file));File.Move(file,target);moved.Add((file,target));}
            if(moved.Count>0)File.WriteAllText(Path.Combine(trash,"deletion.json"),JsonSerializer.Serialize(new{name,time=DateTimeOffset.UtcNow,files=moved.Select(p=>new{original=p.Original,backup=p.Backup})},new JsonSerializerOptions{WriteIndented=true}));
            hidden.Add(key);Write(data,hidden);
        } catch {
            foreach(var pair in moved.AsEnumerable().Reverse())if(File.Exists(pair.Backup) && !File.Exists(pair.Original))File.Move(pair.Backup,pair.Original);
            throw;
        }
        return new(name,matching.Count>0?trash:"",matching.Count);
    }
}
