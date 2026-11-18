using System.Globalization;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal static partial class Runtime {
    private static bool generatorOpen=true;
    private static string generatorSeed="1",generatorAI="";
    private static Layout? generatedFavorite;
    private static string generatedSummary="";
    private static int generatedHouse;
    private static void DrawCombatGenerator(RangeComputer computer,RangeSession session) {
        generatorOpen=KillhouseUi.Foldout(generatorOpen,"Generate combat layout");
        if(!generatorOpen)return;
        KillhouseUi.BeginSection("Random interior");
        try {
            Plugin.GeneratedMode.Value=KillhouseUi.Choices(Plugin.GeneratedMode.Value,new[]{"Paper","AI","Mixed"},3);
            bool needsAI=Plugin.GeneratedMode.Value!=0;
            var types=needsAI?CombatantCatalog.Types.Where(t=>!Combatants.IsCivilian(t.Key)).ToArray():Array.Empty<CombatantType>();
            if(needsAI) {
                if(!types.Any(t=>t.Key==generatorAI))generatorAI=types.FirstOrDefault()?.Key??"";
                int selected=Array.FindIndex(types,t=>t.Key==generatorAI);
                GUILayout.BeginHorizontal();
                try {
                    GUILayout.Label(selected>=0?"AI: "+types[selected].Label:"Waiting for combat AI types");
                    if(types.Length>1 && GUILayout.Button("Next type",GUILayout.Width(85)))generatorAI=types[(selected+1)%types.Length].Key;
                }finally{GUILayout.EndHorizontal();}
                if(!Plugin.CombatAI.Value)KillhouseUi.Note("Enable AI encounters in Settings > Mods > Killhouse to start AI.");
            }
            GUILayout.Label($"Seed v{CombatGenerator.Version} (0 to 2147483647)");
            generatorSeed=GUILayout.TextField(generatorSeed,10);
            KillhouseUi.Note($"Up to {Plugin.GeneratedRooms.Value} rooms and passages / {Plugin.GeneratedTargets.Value} targets. New layouts mix bent corridors, loops, branching halls and interlocked rooms. Targets watch their likely doorway or the corner on its approach.");
            if(Plugin.GeneratedMode.Value!=1)KillhouseUi.Note("Paper targets mix hostiles, no-shoots and hostages. Three or more paper targets include all three types.");
            bool ready=CanLoadMaps && CanSaveEdited && CanPushEdited && (!needsAI || generatorAI.Length>0);
            GUILayout.BeginHorizontal();
            try {
                GUI.enabled=ready;
                if(GUILayout.Button("New random layout")) {generatorSeed=random.Next().ToString(CultureInfo.InvariantCulture);LoadGeneratedCombat(computer,session);}
                GUI.enabled=ready && int.TryParse(generatorSeed,NumberStyles.None,CultureInfo.InvariantCulture,out _);
                if(GUILayout.Button("Load seed"))LoadGeneratedCombat(computer,session);
            }finally{GUI.enabled=true;GUILayout.EndHorizontal();}
            if(!ready)KillhouseUi.Note("Wait for the current map change, matching co-op peers and the selected AI type.");
            if(generatedFavorite!=null && generatedHouse==session.House) {
                KillhouseUi.Note(generatedSummary);
                GUI.enabled=CanSaveEdited;
                if(GUILayout.Button("Save generated map to library")) {
                    try {Status=SaveHostRevision(LayoutDraft.Copy(generatedFavorite),false,computer,"generated-save");}
                    catch(Exception error){Status="Generated map was not saved: "+error.GetBaseException().Message;}
                }
                GUI.enabled=true;
            }
            KillhouseUi.Note("AI stays inactive until Start encounter. Save a favorite to keep or share it; random generation does not create layout files.");
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
    }
    private static void LoadGeneratedCombat(RangeComputer computer,RangeSession session) {
        if(!CanLoadMaps || !CanSaveEdited || !CanPushEdited)return;
        try {
            if(!int.TryParse(generatorSeed,NumberStyles.None,CultureInfo.InvariantCulture,out int seed))throw new InvalidOperationException("Enter a seed from 0 to 2147483647.");
            var mode=(GeneratedEncounter)Plugin.GeneratedMode.Value;
            if(mode!=GeneratedEncounter.Paper)CombatantCatalog.Require(generatorAI);
            var generated=CombatGenerator.Generate(new(seed,Plugin.GeneratedRooms.Value,Plugin.GeneratedTargets.Value,mode,generatorAI));
            var doc=generated.Layout;doc.Houses=session.House;
            if(Plugin.Furniture.Value && Plugin.GeneratedFurniture.Value)CombatGenerator.Furnish(generated,session.EditorPropShapes);
            var issues=session.PreviewTargetIssues(doc).Concat(session.PreviewPropIssues(doc)).Where(i=>i.Fatal).ToArray();
            if(issues.Length>0)throw new InvalidOperationException(string.Join(" ",issues.Select(i=>i.Message)));
            var favorite=LayoutDraft.Copy(doc);
            // Revisions are immutable for co-op and queued maps. One current
            // generated row per house avoids filling the visible map library.
            doc.Name="Generated combat - House "+session.House;
            SaveHostRevision(doc,true,computer,"generated-combat",persist:false);
            generatedFavorite=favorite;generatedHouse=session.House;
            generatedSummary=$"{generated.Topology} / Seed {seed}: {generated.Rooms.Count} rooms and passages, {doc.TargetCount} paper, {doc.Combatants.Count} AI, {doc.Props.Count} furnishings.";
            if(doc.TargetCount>0)generatedSummary+=$" Paper mix: {doc.AllTargets.Count(t=>t.Role==TargetRole.Threat)} hostile, {doc.AllTargets.Count(t=>t.Role==TargetRole.Civilian)} no-shoot, {doc.AllTargets.Count(t=>t.Role==TargetRole.Hostage)} hostage.";
            Status=generatedSummary+" Layout submitted for loading.";
        }catch(Exception error){Status="Generated layout was not loaded: "+error.GetBaseException().Message;}
    }
}
