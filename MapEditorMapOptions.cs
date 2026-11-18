using UnityEngine;
using OperatorKillhouse.Core;

namespace OperatorKillhouse;
internal sealed partial class MapEditor
{
    private string tagInput="",tagsSeen="",tagError="";
    private Layout? tagsDocument;
    private void MapOptionsInspector() {
        KillhouseUi.BeginSection("Map settings");
        try {
            SceneLightingOptions();
            string current=string.Join(", ",Draft.Document.Tags);
            if(!ReferenceEquals(tagsDocument,Draft.Document) || current!=tagsSeen){tagsDocument=Draft.Document;tagInput=tagsSeen=current;tagError="";}
            GUILayout.Label("Custom tags (comma separated)");
            tagInput=GUILayout.TextField(tagInput,800);
            if(GUILayout.Button("Apply tags")) {
                try {
                    var tags=MapTags.Parse(tagInput);
                    Draft.Change(d=>{d.Tags.Clear();d.Tags.AddRange(tags);});
                    tagInput=tagsSeen=string.Join(", ",Draft.Document.Tags);tagError="";
                }catch(FormatException error){tagError=error.Message;}
            }
            KillhouseUi.Note(tagError.Length>0?tagError:"Up to 16 tags, 40 characters each. Clear the field and Apply to remove all tags. Save the map to keep these changes.");
        }finally{KillhouseUi.EndSection();}
    }
    private void SceneLightingOptions() {
            GUILayout.Label("Scene lighting (whole lobby)");
            var preset=(LightingPreset)KillhouseUi.Choices((int)Draft.Document.Lighting,new[]{"Normal","Night","Dawn","Dusk"},2);
            if(preset!=Draft.Document.Lighting)Draft.Change(d=>{d.Lighting=preset;d.Darkness=0;});
            if(preset!=LightingPreset.Normal)KillhouseUi.Note("Night uses a dark sky with no sun. Dawn and Dusk use brighter, warm horizons and a low sun. Save and load to apply.");
            if(preset!=LightingPreset.Normal)KillhouseUi.Note("AI unaided sight: Night up to 8 m; Dawn/Dusk up to 16 m, with slower visual detection. NVG-equipped AI retain native vision.");
            KillhouseUi.Note(preset==LightingPreset.Normal?"Restores the lobby's original lighting. The last map loaded controls both houses.":"These settings save and share with this map. Loading it changes the whole scene for everyone.");
    }

}
