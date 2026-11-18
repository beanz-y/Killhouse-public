namespace OperatorKillhouse.Core;

// A single transient message: repeated attempts replace/refresh it rather than
// retaining a queue of notifications for a long-running lobby.
public sealed class MenuNotice
{
    public const float HoldSeconds=6f,FadeSeconds=.5f;
    private double shown=double.NegativeInfinity;
    public string Text {get;private set;}="";
    public void Show(string reason,double now) {
        if(!double.IsFinite(now) || string.IsNullOrWhiteSpace(reason)){Reset();return;}
        Text="Killhouse unavailable\n"+Plain(reason,520);shown=now;
    }
    public float Alpha(double now)=>Text.Length==0 || !double.IsFinite(now) || now<shown?0:
        (float)Math.Clamp(1-Math.Max(0,now-shown-HoldSeconds)/FadeSeconds,0,1);
    public void Reset(){Text="";shown=double.NegativeInfinity;}
    public static string Plain(string? value,int limit)=>new((value??"").Where(c=>!char.IsControl(c) && c is not ('\u2028' or '\u2029') && !(c>='\u202a' && c<='\u202e') && !(c>='\u2066' && c<='\u2069')).Take(limit).ToArray());
}

public static class CoopAdvertisementNotice
{
    // This is diagnostic only. CoopAd.Parse and the authenticated lease/asset
    // handshake remain the only sources of compatibility/authorization.
    public static string? Block(string? raw,bool furniture) {
        if(string.IsNullOrEmpty(raw))return $"is not advertising Killhouse {CoopProtocol.Build}. Their mod may be missing, disabled, or still joining. Everyone must install the same version and enable custom ranges and co-op.";
        if(raw.Length>200)return Invalid;
        var parts=raw.Split('|');
        if(parts.Length!=8 || parts[1].Length is <1 or >24 || parts[1].Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '.' or '-' or '+')))return Invalid;
        string build=parts[1];parts[1]=CoopProtocol.Build;
        var shape=CoopAd.Parse(string.Join("|",parts));
        if(shape==null)return Invalid;
        if(build!=CoopProtocol.Build)return $"has Killhouse {build}; this PC has {CoopProtocol.Build}. Everyone must install the same Killhouse version, then restart the game.";
        if(shape.Furniture!=furniture)return "has a different Furniture setting. Match Killhouse > Furniture in Mod Controls, then re-enter the range.";
        return null;
    }
    private const string Invalid="has an unreadable or unsupported Killhouse response. Install the same Killhouse version on every PC, then restart the game.";
}
