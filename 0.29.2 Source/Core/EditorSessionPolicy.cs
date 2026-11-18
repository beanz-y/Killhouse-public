namespace OperatorKillhouse.Core;

public static class EditorSessionPolicy
{
    public static bool CanEdit(bool enabled,bool server,bool stopping,bool guestPermission=false)=>enabled && (server || guestPermission) && !stopping;
    public static bool CanSave(bool canEdit,bool selecting,bool sharing)=>canEdit && !selecting && !sharing;
    public static bool CanPush(bool canSave,int remotePeers,bool ready)=>canSave && (remotePeers==0 || ready);
    // Library additions are not a new network session. In-flight/old revisions
    // keep their immutable slot, identity and acknowledgement context.
    public static string LobbyContext(ulong lobby,uint player,bool server,bool furniture)=>$"{lobby}:{player}:{server}:{furniture}";
}
