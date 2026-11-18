using BepInEx;
using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal static partial class Runtime
{
    private enum RangePage { Maps,Queue,Editor,Players }
    private static RangePage rangePage;
    private static bool stockMapsOpen,fileToolsOpen;
    private static GUIStyle? panelStatusStyle;
    private static Vector2 panelStatusScroll;
    private static string panelStatusText="";
    private static Vector2 currentRangeScroll;
    private static int currentRangeId,currentRangeIndex=-1;
    private static void DrawPanelContents(int id) {
        var computer=focused;
        var session=computer is not null && computer?Sessions.GetValueOrDefault(computer.GetInstanceID()):null;
        var pages=new List<(RangePage Page,string Label)>{(RangePage.Maps,"Maps")};
        if(NetworkServer.active)pages.Add((RangePage.Queue,"Queue"+(CoopNetwork.QueuedMaps.Count>0?$" ({CoopNetwork.QueuedMaps.Count})":"")));
        if(CanEdit)pages.Add((RangePage.Editor,"Editor"));
        if(NetworkServer.active && (CoopDirectory.Remotes.Count>0 || CoopNetwork.LoadRequests.Count>0))pages.Add((RangePage.Players,"Players & requests"));
        if(!pages.Any(p=>p.Page==rangePage))rangePage=RangePage.Maps;
        if(pages.Count>1)for(int n=0;n<pages.Count;n++) {
            float width=(window.width-24)/pages.Count;
            if(GUI.Button(new(12+n*width,30,width-4,30),(rangePage==pages[n].Page?"● ":"")+pages[n].Label)){rangePage=pages[n].Page;scroll=Vector2.zero;GUIUtility.keyboardControl=0;}
        }
        bool encounter=NetworkServer.active && session!=null;
        var regions=RangePanelLayout.Make(window.width,window.height,pages.Count>1,encounter,session?.ActiveDocument?.Triggers.Count>0);
        int rangeId=computer?computer!.GetInstanceID():0,index=computer?computer!.activeLayoutIndex:-1;
        if(currentRangeId!=rangeId || currentRangeIndex!=index){currentRangeScroll=Vector2.zero;currentRangeId=rangeId;currentRangeIndex=index;}
        GUILayout.BeginArea(new Rect(regions.Header.X,regions.Header.Y,regions.Header.Width,regions.Header.Height));
        try {
            currentRangeScroll=GUILayout.BeginScrollView(currentRangeScroll);
            try{DrawCurrentRange(computer,session);}finally{GUILayout.EndScrollView();}
        }finally{GUILayout.EndArea();}
        GUILayout.BeginArea(new Rect(regions.Body.X,regions.Body.Y,regions.Body.Width,regions.Body.Height));
        try {
            scroll=GUILayout.BeginScrollView(scroll);
            try {
                DrawMapDeletePrompt();
                if(RemoteCount()>0 || !NetworkServer.active) {
                KillhouseUi.BeginSection("Players");
                try {
                    if(RemoteCount()>0 || !NetworkServer.active)KillhouseUi.Note(CoopNetwork.Status);
                    if(EditingAsGuest)KillhouseUi.Note(CoopNetwork.EditorNotice);
                }finally{KillhouseUi.EndSection();}
                }
                switch(rangePage) {
                    case RangePage.Maps:DrawMapLibrary(computer,session);break;
                    case RangePage.Queue:DrawQueuedMaps();break;
                    case RangePage.Editor:DrawEditorActions(computer,session);break;
                    case RangePage.Players:DrawEditorPermissions();break;
                }
            }finally{GUILayout.EndScrollView();}
        }finally{GUILayout.EndArea();}
        GUI.enabled=true;
        panelStatusStyle??=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true};
        if(panelStatusText!=Status){panelStatusText=Status;panelStatusScroll=Vector2.zero;}
        GUILayout.BeginArea(new Rect(12,window.height-87,window.width-24,43));
        try {
            panelStatusScroll=GUILayout.BeginScrollView(panelStatusScroll);
            try {GUILayout.Label(Status,panelStatusStyle);}finally{GUILayout.EndScrollView();}
        }finally{GUILayout.EndArea();}
        if(GUI.Button(new(window.width-106,window.height-38,94,28),"Close"))Close();
        if(NetworkServer.active && CoopNetwork.QueuedMaps.Count>0) {
            GUI.enabled=CanLoadMaps;
            if(GUI.Button(new(12,window.height-38,Math.Min(250,window.width-140),28),$"Load next queued map ({CoopNetwork.QueuedMaps.Count})"))CoopNetwork.LoadNextQueued();
            GUI.enabled=true;
        }
        GUI.DragWindow(new Rect(0,0,window.width,24));
    }
    private static void DrawCurrentRange(RangeComputer? computer,RangeSession? session) {
        string name=PanelMapName(computer,computer is not null && computer?computer.activeLayoutIndex:-1);
        KillhouseUi.BeginSection(session==null?name:$"House {session.House} · {name}");
        try {
            if(session==null){KillhouseUi.Note("Wait for the range to initialize.");return;}
            if(session.SseStatus.Length>0)KillhouseUi.Note(session.SseStatus);
            if(NetworkServer.active && session.ActiveDocument is {} activeDoc && (activeDoc.Triggers.Count>0 || activeDoc.Combatants.Count>0 && activeDoc.Encounter!=EncounterMode.PaperOnly))DrawEncounterControls(session,CanLoadMaps);
            else {
                KillhouseUi.Note(session.ActiveDocument is {} doc?$"{doc.TargetCount} paper targets · no live AI on this map":"Stock range map");
                GUI.enabled=NetworkServer.active && CanLoadMaps;
                if(NetworkServer.active && GUILayout.Button("Reset range",GUILayout.Height(30)))ResetTrainingRange(session);
                GUI.enabled=true;
                if(NetworkServer.active)KillhouseUi.Note("Reset doors, target hits, timer/score and loose equipment in this house.");
            }
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
    }
    private static void ResetTrainingRange(RangeSession session) {
        if(!NetworkServer.active || !CanLoadMaps || !session.Computer)return;
        var computer=session.Computer;
        try {
            NativeRangeContract.Validate(computer,computer.activeLayoutIndex);
            session.ClearEncounter(true);
            TrainingRangeReset.Apply(computer);
            Status=$"House {session.House}: doors, targets, timer/score and loose equipment reset.";
            CoopLog.Event("training-range-reset",new{house=session.House,index=computer.activeLayoutIndex});
        }catch(Exception error){Status="Range reset failed: "+error.GetBaseException().Message;Plugin.Logger.LogError(error);}
    }
    private static void DrawHostMapRow(RangeComputer computer,RangeSession session,int index,bool canLoad) {
        if(PanelLayout(computer,index)==null){KillhouseUi.Note($"Map {index+1} is still initializing.");return;}
        GUILayout.BeginHorizontal();
        try {
            GUI.enabled=canLoad;
            if(GUILayout.Button((computer.activeLayoutIndex==index?"● ":"")+PanelMapName(computer,index),GUILayout.Height(30)))Select(computer,index);
            GUI.enabled=CoopNetwork.CanQueueHostMap(session,index);
            if(GUILayout.Button("Queue",GUILayout.Width(72),GUILayout.Height(30)))CoopNetwork.QueueHostMap(session,index);
            if(index>=session.StockCount && index-session.StockCount<session.Items.Count) {
                var item=session.Items[index-session.StockCount];
                GUI.enabled=CanEdit;
                if(GUILayout.Button("Edit",GUILayout.Width(64),GUILayout.Height(30)))OpenEditor(item.Layout);
                OfferMapDelete(item);
            }
        }finally{GUI.enabled=true;GUILayout.EndHorizontal();}
        if(index>=session.StockCount && index-session.StockCount<session.Items.Count)KillhouseUi.Note(MapDetails(session.Items[index-session.StockCount].Layout));
    }
    private static void DrawMapLibrary(RangeComputer? computer,RangeSession? session) {
        if(computer is null || !computer || session==null){KillhouseUi.Note("Wait for the range to initialize.");return;}
        bool canLoad=CanLoadMaps;
        DrawLibraryTools();
        if(NetworkServer.active && ShowStockMaps) {
            stockMapsOpen=KillhouseUi.Foldout(stockMapsOpen,"Stock maps");
            if(stockMapsOpen) {
                KillhouseUi.BeginSection("");
                try {
                    for(int n=0;n<session.StockCount;n++)DrawHostMapRow(computer,session,n,canLoad);
                }finally{GUI.enabled=true;KillhouseUi.EndSection();}
            }
        }
        KillhouseUi.BeginSection(NetworkServer.active?"Custom maps":"Shared maps");
        try {
            bool any=false;
            foreach(int n in SortedMapIndices(session)) {
                if(PanelLayout(computer,n)==null || n-session.StockCount>=session.Items.Count)continue;
                if(!session.Listed(n))continue;any=true;
                if(NetworkServer.active){DrawHostMapRow(computer,session,n,canLoad);continue;}
                GUI.enabled=NetworkServer.active?canLoad:CoopNetwork.GuestCanRequest;
                if(GUILayout.Button((computer.activeLayoutIndex==n?"● ":"")+PanelMapName(computer,n))) {
                    if(NetworkServer.active)Select(computer,n);else RequestGuestLoad(session.Items[n-session.StockCount].Layout,session);
                }
                KillhouseUi.Note(MapDetails(session.Items[n-session.StockCount].Layout));
            }
            GUI.enabled=true;if(!any)KillhouseUi.Note("No maps match the current filters for this house. Choose A-Z and clear tags to show all maps.");
            if(NetworkServer.active)KillhouseUi.Note("Edit opens a local draft. Save map keeps the current range running. Use the separate load button when ready.");
            if(!NetworkServer.active)KillhouseUi.Note(CanEdit?"Choosing a map requests a host-approved load.":"The host controls map loading and can grant editing permission.");
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
        if(EditingAsGuest && CanEdit) {
            var local=SortedMaps(NamedMaps.Visible(CatalogState.Items).Where(i=>(i.Layout.Houses&session.House)!=0 && !session.Items.Any(shared=>shared.Id==i.Id)));
            if(local.Length>0) {
                KillhouseUi.BeginSection("Your local maps");
                try {foreach(var item in local) {
                    GUILayout.Label(item.Layout.Name);KillhouseUi.Note(MapDetails(item.Layout));GUILayout.BeginHorizontal();try {
                        GUI.enabled=CoopNetwork.GuestCanRequest;if(GUILayout.Button("Request load"))RequestGuestLoad(item.Layout,session);
                        GUI.enabled=CanEdit;if(GUILayout.Button("Edit"))OpenEditor(item.Layout);
                        OfferMapDelete(item);
                    }finally{GUI.enabled=true;GUILayout.EndHorizontal();}
                }}finally{KillhouseUi.EndSection();}
            }
        }
        if(NetworkServer.active) {
            KillhouseUi.BeginSection("Training");
            try {GUI.enabled=canLoad;if(GUILayout.Button("Random map"))BeforeRandom(computer);}
            finally{GUI.enabled=true;KillhouseUi.EndSection();}
        }
        bool canReload=NetworkServer.active && RemoteCount()==0;
        int selected=computer.activeLayoutIndex-session.StockCount;
        bool canExport=selected>=0 && selected<session.Items.Count;
        if(canReload || canExport) {
            fileToolsOpen=KillhouseUi.Foldout(fileToolsOpen,"File tools");
            if(fileToolsOpen) {
                KillhouseUi.BeginSection("Layout files");
                try {
                    if(canReload){GUI.enabled=pendingSelections.Count==0;if(GUILayout.Button("Reload layout files"))Reload();}
                    GUI.enabled=true;
                    if(canExport && GUILayout.Button("Export current custom map")) {
                        var item=session.Items[selected];string folder=Path.Combine(Paths.BepInExRootPath,"../UserData/OperatorKillhouse/exports");Directory.CreateDirectory(folder);
                        File.WriteAllText(Path.Combine(folder,item.Id+".txt"),LayoutFormat.Write(item.Layout));Status="Exported to UserData/OperatorKillhouse/exports.";
                    }
                }finally{GUI.enabled=true;KillhouseUi.EndSection();}
            }
        }
    }
    private static void DrawEncounterControls(RangeSession session,bool ready) {
        var doc=session.ActiveDocument;if(doc==null)return;
        // Keep the primary AI action first, before any trigger status or help.
        if(doc.Combatants.Count>0) {
            GUILayout.Label($"AI encounter · {doc.Combatants.Count} AI · {doc.Routes.Count} routes");
            GUILayout.BeginHorizontal();try {
                GUI.enabled=ready && Plugin.CombatAI.Value && doc.Encounter!=EncounterMode.PaperOnly;
                if(GUILayout.Button(session.EncounterActive?"Restart AI":"Start AI",GUILayout.Height(30)))try{session.StartEncounter();Status=session.EncounterStatus;Close();}catch(Exception error){Status=error.GetBaseException().Message;}
                GUI.enabled=ready;if(GUILayout.Button("Clear AI + reset range",GUILayout.Height(30)))ResetTrainingRange(session);
            }finally{GUI.enabled=true;GUILayout.EndHorizontal();}
            if(!Plugin.CombatAI.Value)KillhouseUi.Note("Enable AI encounters in Settings > Mods > Killhouse.");
            else if(doc.Encounter==EncounterMode.PaperOnly)KillhouseUi.Note("This map is set to Paper only. Choose AI + paper or AI only in the editor to start AI.");
            KillhouseUi.Note(session.EncounterStatus);
        }else {
            GUI.enabled=ready;if(GUILayout.Button("Reset range",GUILayout.Height(30)))ResetTrainingRange(session);GUI.enabled=true;
        }
        if(doc.Triggers.Count>0)DrawTriggerControls(session,ready);
    }
    private static void DrawTriggerControls(RangeSession session,bool ready) {
        GUILayout.Space(8);GUILayout.Label("Triggers · "+(session.TriggersArmed?"armed":"disarmed"));
        GUILayout.BeginHorizontal();try {
            GUI.enabled=ready;
            if(GUILayout.Button(session.TriggersArmed?"Disarm triggers":"Arm triggers",GUILayout.Height(30)))try{if(session.TriggersArmed)session.DisarmTriggers();else session.ArmTriggers();Status=session.TriggerStatus;Close();}catch(Exception error){Status=error.GetBaseException().Message;}
            if(session.TriggersArmed && GUILayout.Button("Rearm triggers",GUILayout.Height(30)))try{session.ArmTriggers();Status=session.TriggerStatus;Close();}catch(Exception error){Status=error.GetBaseException().Message;}
        }finally{GUI.enabled=true;GUILayout.EndHorizontal();}
        KillhouseUi.Note(session.TriggerStatus);
    }
    private static void DrawEditorActions(RangeComputer? computer,RangeSession? session) {
        KillhouseUi.BeginSection("Map editor");
        try {
            GUI.enabled=CanEdit;
            if(GUILayout.Button("Open / resume draft"))OpenEditor();
            if(computer is not null && computer && session!=null) {
                int selected=computer.activeLayoutIndex-session.StockCount;
                if(selected>=0 && selected<session.Items.Count && GUILayout.Button("Edit current map"))OpenEditor(session.Items[selected].Layout);
            }
            if(EditingAsGuest)KillhouseUi.Note("Save locally or request a load. The host confirms every load.");
            else KillhouseUi.Note("Use Edit beside any custom map on the Maps tab to work on it without loading it. Save map saves locally; use the separate load button when ready.");
        }finally{GUI.enabled=true;KillhouseUi.EndSection();}
    }
}
