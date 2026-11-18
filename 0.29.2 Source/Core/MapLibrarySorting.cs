namespace OperatorKillhouse.Core;

public enum MapSort { Name,AI,Night }
public static class MapLibrarySorting
{
    public static CatalogItem[] Order(IEnumerable<CatalogItem> items,MapSort sort) {
        var filtered=items.Where(i=>sort switch{MapSort.AI=>i.Layout.Combatants.Count>0,MapSort.Night=>i.Layout.Night,_=>true});
        var ordered=sort==MapSort.AI?filtered.OrderByDescending(i=>i.Layout.Combatants.Count).ThenBy(i=>NamedMaps.Key(i.Layout.Name),StringComparer.Ordinal):filtered.OrderBy(i=>NamedMaps.Key(i.Layout.Name),StringComparer.Ordinal);
        return ordered.ThenBy(i=>i.Id,StringComparer.Ordinal).ToArray();
    }
}
