using UnityEngine;

namespace OperatorKillhouse;

// Native skin and explicit scopes keep all range/editor sections consistent.
internal static class KillhouseUi
{
    private static GUIStyle? heading,note;
    internal static void BeginSection(string title) {
        heading??=new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold,wordWrap=true};
        GUILayout.Space(8);
        GUILayout.BeginVertical(GUI.skin.box);
        if(title.Length>0)GUILayout.Label(title,heading);
    }
    internal static void EndSection()=>GUILayout.EndVertical();
    internal static void Note(string text) {
        note??=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true};
        GUILayout.Label(text,note);
    }
    internal static bool Foldout(bool open,string title) {
        if(!GUILayout.Button((open?"▾ ":"▸ ")+title,GUILayout.Height(28)))return open;
        GUIUtility.keyboardControl=0;return !open;
    }
    internal static int Choices(int selected,string[] labels,int columns=2) {
        for(int first=0;first<labels.Length;first+=columns) {
            GUILayout.BeginHorizontal();
            try {
                for(int n=first;n<Math.Min(first+columns,labels.Length);n++)
                    if(GUILayout.Button((n==selected?"● ":"○ ")+labels[n],GUILayout.Height(28))){selected=n;GUIUtility.keyboardControl=0;}
            }finally{GUILayout.EndHorizontal();}
        }
        return selected;
    }
}
