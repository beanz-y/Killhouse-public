namespace OperatorKillhouse.Core;

// One current view per session. Session documents are immutable revisions;
// publishing/replacing a revision, hiding a name or changing filters invalidates it.
public sealed class MapLibraryView
{
    private IReadOnlyList<CatalogItem>? source;
    private MapNameIndex? nameIndex;
    private int count=-1,revision=-1,offset;
    private MapSort sort;
    private string tags="";
    private int[] indices=Array.Empty<int>();
    public IReadOnlyList<int> Indices(IReadOnlyList<CatalogItem> items,MapNameIndex names,MapSort order,string filter,int nativeOffset=0) {
        if(ReferenceEquals(source,items) && ReferenceEquals(nameIndex,names) && count==items.Count && revision==names.Revision && sort==order && tags==filter && offset==nativeOffset)return indices;
        return Rebuild(items,names,order,filter,nativeOffset);
    }
    private IReadOnlyList<int> Rebuild(IReadOnlyList<CatalogItem> items,MapNameIndex names,MapSort order,string filter,int nativeOffset) {
        var slots=items.Select((item,n)=>(item,n)).Where(p=>names.IsCurrent(p.item)).ToArray();
        var positions=slots.ToDictionary(p=>p.item.Id,p=>nativeOffset+p.n);
        indices=MapLibrarySorting.Order(slots.Select(p=>p.item).Where(i=>MapTags.Matches(i.Layout,filter)),order).Select(i=>positions[i.Id]).ToArray();
        source=items;nameIndex=names;count=items.Count;revision=names.Revision;sort=order;tags=filter;offset=nativeOffset;
        return indices;
    }
}
