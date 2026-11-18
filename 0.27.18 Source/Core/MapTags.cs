namespace OperatorKillhouse.Core;

public static class MapTags
{
    public const int Maximum=16,Length=40;
    public static string[] Normalize(IEnumerable<string> tags) {
        var result=new List<string>();
        foreach(string tag in tags) {
            string value=string.Join(" ",tag.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
            if(value.Length==0 || value.Length>Length || tag.Any(char.IsControl) || value.Contains(','))throw new FormatException("Tags need 1–40 characters, without commas or control characters.");
            if(!result.Contains(value,StringComparer.OrdinalIgnoreCase))result.Add(value);
            if(result.Count>Maximum)throw new FormatException("A map can have up to 16 tags.");
        }
        return result.OrderBy(t=>t,StringComparer.OrdinalIgnoreCase).ThenBy(t=>t,StringComparer.Ordinal).ToArray();
    }
    public static string[] Parse(string text)=>string.IsNullOrWhiteSpace(text)?Array.Empty<string>():Normalize(text.Split(','));
    public static bool Matches(Layout doc,string filter)=>filter.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).All(term=>doc.Tags.Any(tag=>tag.Contains(term,StringComparison.OrdinalIgnoreCase)));
}
