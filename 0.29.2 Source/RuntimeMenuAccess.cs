using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal static partial class Runtime
{
    private static readonly MenuNotice menuNotice=new();
    private static GUIStyle? noticeStyle;
    private static GUIContent? noticeContent;
    private static string menuFailure="";
    internal static void MenuFailed(string reason){menuFailure=MenuNotice.Plain(reason,300);}

    private static void UpdateMenuAccess() {
        try{HandleMenuAccess();}
        catch(Exception error) {
            RecordFailure("menu-open",error,false);
            string reason="The range menu could not open: "+MenuNotice.Plain(error.GetBaseException().Message,300);
            menuNotice.Show(reason,Time.unscaledTime);Plugin.Logger.LogWarning(reason);
        }
    }
    private static void HandleMenuAccess() {
        if(!Application.isFocused){menuNotice.Reset();Close();return;}
        if(Visible && Input.GetKeyDown(KeyCode.Escape)){if(ActiveEditor is {} activeEditor)activeEditor.Hide();else Close();return;}
        if(Plugin.PanelKey.Value==KeyCode.None || !Input.GetKeyDown(Plugin.PanelKey.Value))return;
        if(Visible){Close();return;}
        if(Cursor.visible || InputLayer.InputDisabled || InputLayer.MouseInputSuppressed)return;
        string? reason=!Plugin.Enabled.Value?"Custom ranges are turned off on this PC. Enable Killhouse > Enable custom ranges in Mod Controls.":
            stopping?menuFailure.Length>0?"Killhouse stopped after an error: "+menuFailure+" Restart the game after resolving the error.":"Enter an Outdoors training range to use Killhouse.":
            !NetworkServer.active && !NetworkClient.active?"Enter an Outdoors training range to use the Killhouse menu.":
            RemoteCount()>0 || !NetworkServer.active?CoopNetwork.MenuBlockReason:null;
        if(reason==null && !CanAttach())reason="The range is not ready. Wait for the lobby to finish loading, then try again.";
        if(reason==null && (!focused || !Sessions.ContainsKey(focused!.GetInstanceID()) && !quarantined.ContainsKey(focused.GetInstanceID()))) {
            // Discovery may not have reached the nearby laptop yet. Search
            // only on the key press; notification drawing never scans the scene.
            Camera camera=Camera.main;
            focused=camera?UnityEngine.Object.FindObjectsOfType<RangeComputer>().Where(c=>NativeStockRepair.Applies(c) && (c.transform.position-camera.transform.position).sqrMagnitude<36)
                .OrderBy(c=>(c.transform.position-camera.transform.position).sqrMagnitude).FirstOrDefault():null;
            if(focused)Attach(focused!);
        }
        if(reason==null && !focused)reason="Move within six metres of an Outdoors training laptop, then press "+Plugin.PanelKey.Value+".";
        if(reason==null && focused && !Sessions.ContainsKey(focused!.GetInstanceID()) && !quarantined.ContainsKey(focused.GetInstanceID()))reason="The training laptop is still initializing. Wait a moment, then try again.";
        if(reason!=null){Status=reason;menuNotice.Show(reason,Time.unscaledTime);return;}
        menuNotice.Reset();savedCursor=Cursor.visible;savedLock=Cursor.lockState;Visible=true;GuardCursor();
    }

    private static void DrawMenuNotice() {
        if(!Application.isFocused || Cursor.visible || InputLayer.InputDisabled || InputLayer.MouseInputSuppressed){menuNotice.Reset();return;}
        if(Event.current.type!=EventType.Repaint)return;
        float alpha=menuNotice.Alpha(Time.unscaledTime);if(alpha<=0)return;
        noticeStyle??=new GUIStyle(GUI.skin.label){alignment=TextAnchor.UpperLeft,richText=false,wordWrap=true};
        noticeContent??=new GUIContent();noticeContent.text=menuNotice.Text;
        float scale=Math.Max(.5f,Screen.height/1080f),margin=24*scale;
        noticeStyle.fontSize=Math.Max(10,(int)(18*scale));noticeStyle.normal.textColor=Color.white;
        float width=Math.Max(1,Math.Min(680*scale,Screen.width-2*margin));
        float height=Math.Min(noticeStyle.CalcHeight(noticeContent,width),Math.Max(1,Screen.height-2*margin));
        // Above the Radio Channels notice, using the same shadow and fade.
        var rect=new Rect(margin,Math.Max(margin,Screen.height-150*scale-height),width,height);
        var color=GUI.color;var matrix=GUI.matrix;var enabled=GUI.enabled;var contentColor=GUI.contentColor;int depth=GUI.depth;
        try {
            GUI.matrix=Matrix4x4.identity;GUI.enabled=true;GUI.contentColor=Color.white;GUI.depth=-89;
            GUI.color=new Color(0,0,0,.9f*alpha);GUI.Label(new Rect(rect.x+scale,rect.y+scale,rect.width,rect.height),noticeContent,noticeStyle);
            GUI.color=new Color(1,1,1,alpha);GUI.Label(rect,noticeContent,noticeStyle);
        }finally{GUI.color=color;GUI.matrix=matrix;GUI.enabled=enabled;GUI.contentColor=contentColor;GUI.depth=depth;}
    }
}
