using System.Security.Cryptography;
using System.Text;

namespace OperatorKillhouse.Core;

public sealed record CatalogItem(string Id, string Origin, Layout Layout);
public sealed record CatalogSnapshot(IReadOnlyList<CatalogItem> Items, IReadOnlyList<string> Notes, string Manifest);
public static class Catalog
{
    private static string Manifest(IEnumerable<CatalogItem> items)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("OperatorKillhouse/layouts/1\n"+string.Join("\n",items.Select(i=>i.Id))))).ToLowerInvariant();
    public static CatalogSnapshot Filter(CatalogSnapshot snapshot,IReadOnlySet<string> hidden) {var items=snapshot.Items.Where(i=>!hidden.Contains(NamedMaps.Key(i.Layout.Name))).ToArray();return new(items,snapshot.Notes,Manifest(items));}
    public static CatalogSnapshot Publish(CatalogSnapshot snapshot,Layout layout,string origin,bool strict) {
        layout=LayoutDraft.Copy(layout);var appended=Append(snapshot,layout,origin,strict);string id=layout.Identity;
        var current=appended.Items.Single(i=>i.Id==id) with{Origin=origin};
        var items=appended.Items.Where(i=>i.Id!=id).Append(current).ToArray();
        return new(items,appended.Notes,Manifest(items));
    }
    public static CatalogSnapshot Append(CatalogSnapshot snapshot,Layout layout,string origin,bool strict) {
        var copy=LayoutDraft.Copy(layout);var checks=LayoutValidation.Check(copy,strict);
        if(checks.Any(i=>i.Fatal))throw new InvalidOperationException(string.Join(" ",checks.Where(i=>i.Fatal).Select(i=>i.Message)));
        if(snapshot.Items.Any(i=>i.Id==copy.Identity))return snapshot;
        if(snapshot.Items.Count>=1000)throw new InvalidOperationException("The session map library is full. Save remaining drafts and reload files in solo.");
        var items=snapshot.Items.Append(new CatalogItem(copy.Identity,origin,copy)).ToArray();
        return new(items,snapshot.Notes,Manifest(items));
    }
    public static CatalogSnapshot Load(IEnumerable<string> folders,bool strict) {
        var items=new List<CatalogItem>(); var notes=new List<string>();
        void Add(Layout layout,string origin) {
            var checks=LayoutValidation.Check(layout,strict);
            foreach(var check in checks) notes.Add($"{origin}: {check.Message}");
            if(checks.Any(c=>c.Fatal)) return;
            items.RemoveAll(item=>item.Id==layout.Identity);items.Add(new(layout.Identity,origin,layout));
        }
        foreach(var layout in StarterLayouts.Create()) Add(layout,"authored starter");
        // The first configured folder (the writable user library) takes priority.
        foreach(string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase).Reverse()) {
            if(!Directory.Exists(folder)) continue;
            try {
                var files=Directory.EnumerateFiles(folder,"*.txt",SearchOption.TopDirectoryOnly).Take(257).OrderBy(p=>Path.GetFileName(p),StringComparer.Ordinal).ToArray();
                if(files.Length>256) { notes.Add($"{folder}: skipped folder containing more than 256 text files."); continue; }
                foreach(string file in files.Where(f=>!Path.GetFileNameWithoutExtension(f).Equals("README",StringComparison.OrdinalIgnoreCase)).OrderBy(File.GetLastWriteTimeUtc).ThenBy(f=>f,StringComparer.Ordinal)) {
                    try {
                        if(new FileInfo(file).Length>262144) { notes.Add($"{file}: exceeds 256 KiB."); continue; }
                        var parsed=LayoutFormat.Read(File.ReadAllText(file),file);
                        foreach(var issue in parsed.Issues) notes.Add($"{file}: {issue.Message}");
                        if(parsed.Success) Add(parsed.Layout!,file);
                    } catch(IOException e) { notes.Add($"{file}: {e.Message}"); }
                    catch(UnauthorizedAccessException e) { notes.Add($"{file}: {e.Message}"); }
                }
            } catch(IOException e) { notes.Add($"{folder}: {e.Message}"); }
            catch(UnauthorizedAccessException e) { notes.Add($"{folder}: {e.Message}"); }
        }
        var visible=NamedMaps.Visible(items);return new(visible,notes,Manifest(visible));
    }
}

// Newly authored training data. No discontinued-mod layouts are embedded.
public static class StarterLayouts
{
    public static IEnumerable<Layout> Create() {
        for(int variation=0;variation<2;variation++) {
            var layout=new Layout(10,10) {Name=variation==0?"Offset Lanes":"Side Entries",Author="Operator Killhouse"};
            layout.Enclose();
            for(int row=3;row<=7;row+=4) for(int col=0;col<10;col++) layout.Edges[new(row,col,true)]=col==(row==3?2:7) ? Barrier.Door : col==(row==3?8:1) ? Barrier.Frame : Barrier.Wall;
            if(variation==1) for(int row=3;row<7;row++) layout.Edges[new(row,5,false)]=row==5?Barrier.Door:Barrier.Wall;
            foreach(var target in new[]{new Target(new(2,3),TargetRole.Threat),new Target(new(4,2),TargetRole.Civilian),new Target(new(5,7),TargetRole.Threat),new Target(new(8,6),TargetRole.Hostage),new Target(new(9,2),TargetRole.Threat)}) layout.Targets.Add(target.Cell,target);
            layout.Themes[new(4,1)]=Theme.Office; layout.Themes[new(8,8)]=Theme.Storage;
            yield return layout;
        }
    }
}

public static class SelectionPolicy
{
    public static int Next(int count,int current,int ticket,bool avoidRepeat) {
        if(count<1) throw new ArgumentOutOfRangeException(nameof(count));
        uint value=unchecked((uint)ticket);
        if(!avoidRepeat || count==1 || current<0 || current>=count) return (int)(value%(uint)count);
        int chosen=(int)(value%(uint)(count-1)); return chosen>=current?chosen+1:chosen;
    }
    public static int[] Activation(int count,float density,Random random) {
        if(count<0 || count>60 || !float.IsFinite(density) || density<0 || density>1) throw new ArgumentOutOfRangeException();
        return Enumerable.Range(0,count).Select(_=>random.NextDouble()<density?1:0).ToArray();
    }
    public static bool CanCustomize(bool enabled,bool server,int remotePeers,bool conflictingPlugin,bool coordinated=false) => enabled && server && (remotePeers==0 || coordinated) && !conflictingPlugin;
}

public sealed class CleanupStack : IDisposable
{
    private readonly Stack<Action> actions=new(); private bool disposed;
    public void Add(Action cleanup) { if(disposed) throw new ObjectDisposedException(nameof(CleanupStack)); actions.Push(cleanup); }
    public void Dispose() {
        if(disposed) return; disposed=true; var errors=new List<Exception>();
        while(actions.Count>0) try { actions.Pop()(); } catch(Exception error) { errors.Add(error); }
        if(errors.Count>0) throw new AggregateException(errors);
    }
}
