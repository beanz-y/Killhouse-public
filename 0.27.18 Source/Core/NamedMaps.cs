using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OperatorKillhouse.Core;

public static class NamedMaps
{
    public static string Key(string name)=>Regex.Replace(name.Normalize(NormalizationForm.FormC).Trim(),@"\s+"," ").ToUpperInvariant();
    public static IReadOnlyList<CatalogItem> Visible(IEnumerable<CatalogItem> source) {
        var items=source.ToArray();var latest=new Dictionary<string,int>(StringComparer.Ordinal);
        for(int n=0;n<items.Length;n++)if(items[n].Id.Length>0)latest[Key(items[n].Layout.Name)]=n;
        return items.Where((item,n)=>item.Id.Length>0 && latest[Key(item.Layout.Name)]==n).ToArray();
    }
    public static string Save(Layout doc,string folder,bool strict) {
        doc=LayoutDraft.Copy(doc);
        var issues=LayoutValidation.Check(doc,strict);if(issues.Any(i=>i.Fatal))throw new InvalidOperationException(string.Join(" ",issues.Where(i=>i.Fatal).Select(i=>i.Message)));
        string key=Key(doc.Name);if(key.Length==0)throw new InvalidOperationException("Give the map a name before saving.");
        folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);
        var files=Directory.EnumerateFiles(folder,"*.txt",SearchOption.TopDirectoryOnly).Take(1025).ToArray();
        if(files.Length>1024)throw new InvalidOperationException("Too many files in this library folder.");
        string? existing=null;
        foreach(string file in files.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(f=>f,StringComparer.Ordinal)) {
            if(Path.GetFileNameWithoutExtension(file).Equals("README",StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length>262144)continue;
            try {var parsed=LayoutFormat.Read(File.ReadAllText(file),file);if(parsed.Success && Key(parsed.Layout!.Name)==key){existing=file;break;}}
            catch(IOException){continue;}
        }
        if(existing==null && files.Length>=256)throw new InvalidOperationException("This library folder has reached its 256-file limit. Existing named maps can still be updated.");
        string slug=new(doc.Name.Where(c=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_').Take(40).ToArray());if(slug.Length==0)slug="layout";
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12].ToLowerInvariant();
        string path=existing??Path.Combine(folder,"editor-"+slug+"-"+hash+".txt"),temp=Path.Combine(folder,".map-save-"+Guid.NewGuid().ToString("N")+".tmp");
        try {File.WriteAllText(temp,LayoutFormat.Write(doc));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        return path;
    }
}

// Native/session slots remain immutable, while only the latest named revision
// appears in selection and randomization. Re-saving an older identity can make
// it current again without mutating an active or queued revision.
public sealed class MapNameIndex
{
    private readonly Dictionary<string,string> latest=new(StringComparer.Ordinal);
    private readonly HashSet<string> deleted=new(StringComparer.Ordinal);
    // Only published session revisions are retained, never transient filter inputs.
    private readonly Dictionary<string,(string Name,string Key)> publishedNames=new(StringComparer.Ordinal);
    public int Revision {get;private set;}
    public void Hide(string name){if(deleted.Add(NamedMaps.Key(name)))Revision++;}
    public void Publish(CatalogItem item){if(item.Id.Length>0){string key=NamedMaps.Key(item.Layout.Name);publishedNames[item.Id]=(item.Layout.Name,key);bool changed=!latest.TryGetValue(key,out var prior) || prior!=item.Id;latest[key]=item.Id;if(deleted.Remove(key))changed=true;if(changed)Revision++;}}
    public bool IsCurrent(CatalogItem item){
        if(item.Id.Length==0)return false;
        string key=publishedNames.TryGetValue(item.Id,out var cached) && cached.Name==item.Layout.Name?cached.Key:NamedMaps.Key(item.Layout.Name);
        return !deleted.Contains(key) && (!latest.TryGetValue(key,out string? id) || id==item.Id);
    }
}
