using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal sealed partial class MapEditor
{
    private void SseInspector(PlacedProp? selected) {
        if(selected==null || !SseObjectives.IsAsset(selected.Name))return;
        var current=Draft.Document.Sse.FirstOrDefault(s=>s.PropId==selected.Id);if(current==null)return;
        KillhouseUi.BeginSection("SSE objective #"+current.PropId);
        try {
            GUI.SetNextControlName("kh-sse-label");GUILayout.Label("Native interaction name");
            string label=GUILayout.TextField(current.Label,64);
            bool required=GUILayout.Toggle(current.Required,"Required objective");
            if(!string.IsNullOrWhiteSpace(label) && !label.Any(char.IsControl) && (label!=current.Label || required!=current.Required))Draft.Change(d=>{int at=d.Sse.FindIndex(s=>s.PropId==current.PropId);d.Sse[at]=current with{Label=label,Required=required};});
            KillhouseUi.Note("Uses the native interaction prompt. Interact to collect for the whole team. Range reset restores it. Arm SSE-collected triggers before collection.");
        }finally{KillhouseUi.EndSection();}
    }
}
