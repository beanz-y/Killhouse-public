using BepInEx;
using OperatorKillhouse.Core;
using UnityEngine;
using Barrier=OperatorKillhouse.Core.Barrier;

namespace OperatorKillhouse;
internal static partial class Runtime
{
    private static string LibraryData=>Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../UserData/OperatorKillhouse"));
    private static string? pendingDeleteName;
    private static bool libraryToolsOpen;
    private static string tagFilter="";
    private static readonly string[] mapSortLabels={"A-Z","AI","Night"};
    private static bool ShowStockMaps=>Plugin.MapFilter.Value==(int)MapSort.Name && string.IsNullOrWhiteSpace(tagFilter);
    private static CatalogItem[] SortedMaps(IEnumerable<CatalogItem> items)=>MapLibrarySorting.Order(items.Where(i=>MapTags.Matches(i.Layout,tagFilter)),(MapSort)Plugin.MapFilter.Value);
    private static IReadOnlyList<int> SortedMapIndices(RangeSession session)=>session.LibraryIndices((MapSort)Plugin.MapFilter.Value,tagFilter);
    private static string MapDetails(Layout doc)=>$"{doc.Combatants.Count} AI · {doc.TargetCount} Paper · {doc.Interior.Count(e=>e.Value==Barrier.Door)} Doors · {doc.Props.Count} Props"+(doc.Lighting!=LightingPreset.Normal?$" · {doc.Lighting} (whole lobby)":"")+(doc.Tags.Count>0?"\nTags: "+string.Join(", ",doc.Tags):"");
    private static void DrawLibraryTools() {
        libraryToolsOpen=KillhouseUi.Foldout(libraryToolsOpen,"Filter: "+mapSortLabels[Math.Clamp(Plugin.MapFilter.Value,0,2)]+(tagFilter.Length>0?" · Tag filter active":""));
        if(!libraryToolsOpen)return;
        KillhouseUi.BeginSection("Filter custom maps");
        try {
            Plugin.MapFilter.Value=KillhouseUi.Choices(Plugin.MapFilter.Value,mapSortLabels,3);
            KillhouseUi.Note("A-Z: all maps alphabetically. AI: only maps with AI, highest count first. Night: only Night maps, alphabetically.");
            GUILayout.Label("Filter tags (comma separated; match every tag)");
            tagFilter=GUILayout.TextField(tagFilter,800);
            if(tagFilter.Length>0 && GUILayout.Button("Clear tag filter"))tagFilter="";
            KillhouseUi.Note("Tag searches ignore case and match part of a tag. Add or remove tags in Editor > Map settings.");
        }finally{KillhouseUi.EndSection();}
    }
    private static CatalogSnapshot FilterDeletedMaps(CatalogSnapshot snapshot) {
        try{return Catalog.Filter(snapshot,MapDeletion.Read(LibraryData));}
        catch(Exception error){Plugin.Logger.LogWarning("Deleted-map filter could not load: "+error);return snapshot;}
    }
    private static void OfferMapDelete(CatalogItem item) {
        GUI.enabled=true;
        if(GUILayout.Button("Delete",GUILayout.Width(66),GUILayout.Height(30))){pendingDeleteName=item.Layout.Name;scroll=Vector2.zero;}
    }
    private static void DrawMapDeletePrompt() {
        if(pendingDeleteName is not {} name)return;
        KillhouseUi.BeginSection("Delete map: "+name);
        try {
            KillhouseUi.Note("Remove this named map from this PC's library? Saved copies move to UserData/OperatorKillhouse/deleted-maps. Imported originals stay untouched. The running map and queued revisions keep working.");
            GUILayout.BeginHorizontal();
            try {
                if(GUILayout.Button("Delete map")) {
                    try {
                        var deleted=MapDeletion.Delete(name,ownFolder,LibraryData);
                        CatalogState=FilterDeletedMaps(CatalogState);
                        foreach(var session in Sessions.Values)session.HideNamedMap(name);
                        WriteCatalogManifest();pendingDeleteName=null;
                        Status=$"Deleted {name} from this PC's library. "+(deleted.Files>0?$"{deleted.Files} saved file(s) moved to UserData/OperatorKillhouse/deleted-maps.":"Imported originals were preserved.");
                    }catch(Exception error){Status="Map was not deleted: "+error.GetBaseException().Message;}
                }
                if(GUILayout.Button("Cancel"))pendingDeleteName=null;
            }finally{GUILayout.EndHorizontal();}
        }finally{KillhouseUi.EndSection();}
    }
}
internal sealed partial class RangeSession
{
    private readonly MapLibraryView libraryView=new();
    internal IReadOnlyList<int> LibraryIndices(MapSort sort,string tags)=>libraryView.Indices(items,mapNames,sort,tags,StockCount);
    internal void HideNamedMap(string name)=>mapNames.Hide(name);
    internal MapLighting LightingMap(int index)=>index>=StockCount && index-StockCount<items.Count?MapLighting.For(items[index-StockCount].Layout).Fixed:MapLighting.Normal;
}
