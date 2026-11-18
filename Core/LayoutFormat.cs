using System.Globalization;
using System.Text;

namespace OperatorKillhouse.Core;

// Compatibility reader implemented from the documented text interface.
public static class LayoutFormat
{
    private static readonly CultureInfo Numbers=CultureInfo.InvariantCulture;
    public static ImportResult Read(string text, string source="layout") {
        var issues=new List<Issue>();
        try {
            if(text.Length>262144) throw new FormatException("Layout exceeds 256 KiB.");
            var lines=text.TrimStart('\ufeff').Replace("\r\n","\n").Replace('\r','\n').Split('\n');
            var metadata=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var directives=new List<(string Key,string Value)>(); var grid=new List<string>();
            bool started=false,ended=false;
            foreach(string raw in lines) {
                string line=raw.Trim();
                if(line.StartsWith(';') || line.StartsWith("//")) continue;
                if(line.Length==0) { if(started) ended=true; continue; }
                int colon=line.IndexOf(':');
                if(colon>0) {
                    string key=line[..colon].Trim().ToLowerInvariant(), value=line[(colon+1)..].Trim();
                    if(key is "stand" or "prop" or "door" or "lock" or "presence" or "target" or "pose" or "furniture" or "mount" or "window" or "combatant" or "route" or "waypoint" or "encounter" or "circuit" or "light" or "breaker" or "trigger" or "trigger-action" or "ai-start" or "sse" or "boundary-wall") { directives.Add((key,value)); if(started) ended=true; }
                    else if(!started) metadata[key]=value;
                    else throw new FormatException("Unexpected metadata after the grid.");
                    continue;
                }
                if(ended) throw new FormatException("Unexpected text after the grid.");
                started=true; grid.Add(raw.TrimEnd());
            }
            if(grid.Count<3 || grid.Count%2!=1) throw new FormatException("Grid needs an odd number of alternating edge and cell rows.");
            int indent=grid.Min(s=>s.TakeWhile(c=>c==' ').Count());
            grid=grid.Select(s=>s[indent..]).ToList();
            int pitch=grid[0].StartsWith('+') ? grid[0].IndexOf('+',1) : 2;
            if(pitch<2 || pitch>16) throw new FormatException("Invalid cell spacing.");
            int width=(grid.Max(s=>s.Length)-1+pitch-1)/pitch, height=(grid.Count-1)/2;
            if(width<1 || height<1 || width>32 || height>32) throw new FormatException("Grid dimensions exceed parser limits.");
            var layout=new Layout(width,height) {
                Name=metadata.GetValueOrDefault("name",Path.GetFileNameWithoutExtension(source)).Trim(),
                Author=metadata.GetValueOrDefault("author","")
            };
            layout.Houses=metadata.GetValueOrDefault("house","both").ToLowerInvariant() switch {
                "1"=>1,"2"=>2,"both" or "any" or "*" or ""=>3,_=>throw new FormatException("house must be 1, 2 or both.") };
            if(metadata.TryGetValue("format",out string? version) && version is not ("2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27")) throw new FormatException("Unsupported Killhouse layout format.");
            if(metadata.TryGetValue("night",out string? night)) {
                if(version is not ("15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27") || !bool.TryParse(night,out bool enabled))throw new FormatException("Night mode requires format: 15 or newer and night: true/false.");
                layout.Night=enabled;
            }
            if(metadata.TryGetValue("lighting",out string? lighting)) {
                if(version is not ("16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27") || metadata.ContainsKey("night") || !Enum.TryParse<LightingPreset>(lighting,true,out var preset) || !Enum.IsDefined(typeof(LightingPreset),preset))throw new FormatException("Lighting requires format: 16 and one NORMAL/NIGHT/DAWN/DUSK preset, without night metadata.");
                layout.Lighting=preset;
            }
            if(metadata.TryGetValue("darkness",out string? darkness)) {
                if(version is not ("16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27") || !metadata.ContainsKey("lighting") || !float.TryParse(darkness,NumberStyles.Float,Numbers,out float level))throw new FormatException("Darkness requires format: 16, lighting metadata and a number from 0 to 6.");
                layout.Darkness=level;
            }
            if(!MapLighting.For(layout).Valid)throw new FormatException("Invalid lighting preset or darkness level (0–6; Normal requires 0).");
            if(metadata.TryGetValue("tags",out string? tags)) {
                if(version is not ("15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Map tags require format: 15 or newer.");
                layout.Tags.AddRange(MapTags.Normalize(tags.Split(',').Select(Uri.UnescapeDataString)));
            }
            bool formerPerimeter=metadata.GetValueOrDefault("perimeter")=="measured" && version is "13" or "14";
            if(metadata.ContainsKey("construction") || metadata.ContainsKey("perimeter"))issues.Add(new("Using restored 0.15.1 grid and placement; newer perimeter calibration is ignored."));
            for(int n=0;n<grid.Count;n++) {
                string row=grid[n].PadRight(width*pitch+1);
                if(n%2==0) { for(int x=0;x<width;x++) layout.Edges[new(n/2,x,true)]=ParseBarrier(row.Substring(x*pitch+1,pitch-1)); }
                else {
                    for(int x=0;x<=width;x++) layout.Edges[new(n/2,x,false)]=ParseBarrier(row.Substring(x*pitch,1));
                    for(int x=0;x<width;x++) {
                        var cell=new Cell(n/2,x); string contents=row.Substring(x*pitch+1,pitch-1);
                        foreach(char token in contents) {
                            TargetRole? target=Role(token);
                            if(target.HasValue && !layout.Targets.ContainsKey(cell)) layout.Targets.Add(cell,new(cell,target.Value));
                            Theme? theme=token switch {'b'=>Theme.Bedroom,'o'=>Theme.Office,'l'=>Theme.Lounge,'s'=>Theme.Storage,'c'=>Theme.Cover,_=>null};
                            if(theme.HasValue && !layout.Themes.ContainsKey(cell)) layout.Themes.Add(cell,theme.Value);
                        }
                    }
                }
            }
            if(layout.Edges.Any(p=>p.Value==Barrier.Window && (version is not ("9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27") || !layout.InteriorEdge(p.Key))))throw new FormatException("Windows require format: 9 and an interior edge.");
            layout.Enclose();
            var presence=new Dictionary<Cell,bool>();
            var poses=new Dictionary<Cell,(float X,float Z,float? Yaw)>();
            var mounts=new Dictionary<TargetRef,(float Height,float Pitch,float Roll)>();
            var routePoints=new List<(int Id,RoutePoint Point)>();
            bool encounterSeen=false;
            foreach(var (key,value) in directives) {
                if(key=="boundary-wall") {
                    if(version!="27")throw new FormatException("Authored boundary walls require format: 27.");
                    var words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length!=2)throw new FormatException("boundary-wall needs row,column H/V.");
                    var xy=words[0].Split(',');
                    if(words.Length!=2 || xy.Length!=2 || !int.TryParse(xy[0],out int row) || !int.TryParse(xy[1],out int col) || words[1] is not ("H" or "V"))throw new FormatException("boundary-wall needs row,column H/V.");
                    var edge=new Edge(row,col,words[1]=="H");
                    if(!BoundaryWalls.IsBoundary(layout,edge) || !layout.BoundaryWalls.Add(edge))throw new FormatException("Boundary walls need unique original grid perimeter edges.");
                }else if(key=="sse") {
                    if(version is not ("25" or "26" or "27"))throw new FormatException("SSE objectives require format: 25.");
                    SseObjectives.Read(layout,value);
                }else if(key is "trigger" or "trigger-action" or "ai-start") {
                    if(version is not ("20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Scenario triggers require format: 20 or newer.");
                    if(key=="trigger" && value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length==15 && version!="27")throw new FormatException("Breaker transition choices require format: 27.");
                    ScenarioTriggers.Read(layout,key,value);
                    if(version is not ("25" or "26" or "27") && key=="trigger" && layout.Triggers[^1].Event==TriggerEvent.SseCollected)throw new FormatException("SSE-collected triggers require format: 25.");
                    if(version is not ("24" or "25" or "26" or "27") && key=="trigger-action" && layout.Triggers.Any(ScenarioTriggers.HasEnteringDespawn))throw new FormatException("Despawn entering AI requires format: 24.");
                    if(version is not ("22" or "23" or "24" or "25" or "26" or "27") && key=="trigger" && layout.Triggers[^1].ActorIds!=null)throw new FormatException("Multiple selected AI require format: 22.");
                    if(version is not ("21" or "22" or "23" or "24" or "25" or "26" or "27") && key=="trigger" && layout.Triggers[^1].Event==TriggerEvent.BreakerOff)throw new FormatException("Breaker-off triggers require format: 21.");
                }else if(key is "circuit" or "light" or "breaker") {
                    if(version is not ("18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Electrical circuits require format: 18 or newer.");
                    if(key=="circuit" && value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length==4 && version is not ("19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Circuit dimmers require format: 19.");
                    if(key=="circuit" && value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length==5 && version!="27")throw new FormatException("Independent power requires format: 27.");
                    PowerCircuits.Read(layout,key,value);
                }else if(key is "combatant" or "route" or "waypoint" or "encounter" or "circuit" or "light" or "breaker") {
                    if(version is not ("10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("AI encounters require format: 10 or newer.");
                    if(key=="encounter" && encounterSeen)throw new FormatException("Duplicate encounter mode.");
                    if(key=="encounter")encounterSeen=true;
                    if(key=="combatant" && version is not ("11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27") && value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length==8)throw new FormatException("Civilian reactions require format: 11.");
                    if(key=="route" && version is not ("12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27") && value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length==5)throw new FormatException("Patrol pacing choices require format: 12.");
                    if(key=="combatant" && version is not ("23" or "24" or "25" or "26" or "27") && value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length==9)throw new FormatException("Custom flee delays require format: 23.");
                    CombatantFormat.Read(layout,key,value,routePoints);
                    if(key=="combatant" && version is not ("13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27") && layout.Combatants[^1].Order==CombatOrder.Cautious)throw new FormatException("Cautious combat orders require format: 13.");
                }else if(key=="window") {
                    if(version is not ("9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Windows require format: 9.");
                    var words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length!=5)throw new FormatException("window needs row,column H/V width sill height.");
                    var xy=words[0].Split(',');
                    if(xy.Length!=2 || !int.TryParse(xy[0],out int r) || !int.TryParse(xy[1],out int c) || words[1] is not ("H" or "V"))throw new FormatException("Invalid window edge or axis.");
                    float Number(int n){if(!float.TryParse(words[n],NumberStyles.Float,Numbers,out float v) || !float.IsFinite(v))throw new FormatException("Window dimensions must be finite.");return v;}
                    var edge=new Edge(r,c,words[1]=="H");var settings=new WindowSettings(Number(2),Number(3),Number(4));
                    if(!layout.InteriorEdge(edge) || layout.Edges.GetValueOrDefault(edge)!=Barrier.Window || !settings.Valid || !layout.WindowOptions.TryAdd(edge,settings))throw new FormatException("Window settings need one interior window edge with supported dimensions, without duplicates.");
                }else if(key=="mount") {
                    if(version is not ("8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Standless targets require format: 8 or newer.");
                    var words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length!=5 || words[1]!="STANDLESS")throw new FormatException("mount needs target-id/row,column STANDLESS height pitch roll.");
                    TargetRef reference;
                    var xy=words[0].Split(',');
                    if(xy.Length==2 && int.TryParse(xy[0],out int row) && int.TryParse(xy[1],out int col))reference=new(new(row,col));
                    else if(xy.Length==1 && int.TryParse(words[0],out int id) && id>0)reference=new(default,id);
                    else throw new FormatException("Invalid mounted target reference.");
                    float Number(int n){if(!float.TryParse(words[n],NumberStyles.Float,Numbers,out float v) || !float.IsFinite(v))throw new FormatException("Target mount numbers must be finite.");return v;}
                    var pose=(Height:Number(2),Pitch:Number(3),Roll:Number(4));
                    if(pose.Height<0 || pose.Height>TargetGeometry.MaximumHeight || Math.Abs(pose.Pitch)>90 || Math.Abs(pose.Roll)>180 || !mounts.TryAdd(reference,pose))throw new FormatException("Invalid or duplicate target height/tilt.");
                }else if(key is "target" or "pose") {
                    if(version is not ("5" or "6" or "7" or "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Fine targets require format: 5.");
                    var words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    float Number(int n){if(n>=words.Length || !float.TryParse(words[n],NumberStyles.Float,Numbers,out float v) || !float.IsFinite(v))throw new FormatException("Target coordinates/angles must be finite.");return v;}
                    float? Angle(int n)=>words[n].ToUpperInvariant()=="AUTO"?null:TargetPlacement.Angle(Number(n));
                    if(key=="target") {
                        if(words.Length!=6 || !int.TryParse(words[0],out int id) || id<1 || id>1000000 || words[1].Length!=1 || !Role(words[1][0]).HasValue || words[5] is not ("RANDOM" or "ALWAYS") || layout.FineTargets.Any(t=>t.Id==id) || layout.FineTargets.Count>=60)throw new FormatException("target needs unique-id T/N/H x-metres south-metres AUTO/degrees RANDOM/ALWAYS.");
                        var t=TargetPlacement.At(Number(2),Number(3),Role(words[1][0])!.Value,Angle(4),words[5]=="ALWAYS",id);
                        if(!layout.Contains(t.Cell) && !(formerPerimeter && t.Cell.Row>=-1 && t.Cell.Row<=layout.Rows && t.Cell.Column>=-1 && t.Cell.Column<=layout.Columns))throw new FormatException("Fine target is outside the supported house boundary.");
                        layout.FineTargets.Add(t);
                    }else {
                        if(words.Length!=4)throw new FormatException("pose needs row,column offset-x offset-south AUTO/degrees.");
                        var xy=words[0].Split(',');if(xy.Length!=2 || !int.TryParse(xy[0],out int r) || !int.TryParse(xy[1],out int c) || !poses.TryAdd(new(r,c),(Number(1),Number(2),Angle(3))))throw new FormatException("Invalid or duplicate target pose.");
                    }
                }else if(key=="furniture") {
                    if(version is not ("6" or "7" or "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Individual furniture requires format: 6.");
                    var words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if((words.Length!=6 && !(words.Length==8 && version is "26" or "27") && (version is not ("7" or "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27") || words.Length!=7)) || !int.TryParse(words[0],out int id) || id<1 || id>1000000 || layout.Props.Any(p=>p.Id==id) || layout.Props.Count(p=>p.Id>0)>=128)throw new FormatException("furniture needs unique-id encoded-name x z yaw height (maximum 128 items).");
                    float Number(int n){if(!float.TryParse(words[n],NumberStyles.Float,Numbers,out float v) || !float.IsFinite(v))throw new FormatException("Furniture numbers must be finite.");return v;}
                    string name=Uri.UnescapeDataString(words[1]);
                    if(string.IsNullOrWhiteSpace(name) || name.Length>160 || name.Any(char.IsControl))throw new FormatException("Invalid furniture asset name.");
                    int support=0;if(words.Length>=7 && (!int.TryParse(words[6],out support) || support<0 || support>1000000))throw new FormatException("Invalid furniture support ID.");
                    var peers=words.Length==8?new SupportPeers(words[7]):default;
                    if(!peers.Valid || !peers.Empty && (support==0 || peers.Ids.Contains(support)))throw new FormatException("Invalid additional furniture supports.");
                    layout.Props.Add(new(name,Number(2),Number(3),TargetPlacement.Angle(Number(4)),Number(5),id,support,peers));
                }else if(key=="stand") {
                    string[] words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length<2 || words[0].Length!=1 || !Role(words[0][0]).HasValue) throw new FormatException("stand needs T/N/H row,column [N/E/S/W].");
                    string[] xy=words[1].Split(',');
                    if(xy.Length!=2 || !int.TryParse(xy[0],out int r) || !int.TryParse(xy[1],out int c)) throw new FormatException("Invalid stand cell.");
                    var cell=new Cell(r,c); if(!layout.Contains(cell)) throw new FormatException("Stand is outside the grid.");
                    Bearing? facing=words.Length>2 ? words[2].ToUpperInvariant() switch {"N" or "NORTH"=>Bearing.North,"E" or "EAST"=>Bearing.East,"S" or "SOUTH"=>Bearing.South,"W" or "WEST"=>Bearing.West,_=>throw new FormatException("Invalid stand facing.")} : null;
                    layout.Targets[cell]=new(cell,Role(words[0][0])!.Value,facing);
                } else if(key=="door") {
                    if(metadata.GetValueOrDefault("format") is not ("2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27")) throw new FormatException("Custom door settings require format: 2 or newer.");
                    string[] words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length!=4) throw new FormatException("door needs row,column H W/E N/S or row,column V N/S W/E.");
                    string[] xy=words[0].Split(',');
                    if(xy.Length!=2 || !int.TryParse(xy[0],out int r) || !int.TryParse(xy[1],out int c)) throw new FormatException("Invalid door edge.");
                    string axis=words[1].ToUpperInvariant(),hinge=words[2].ToUpperInvariant(),opens=words[3].ToUpperInvariant();
                    if(axis is not ("H" or "V") || (axis=="H" ? hinge is not ("W" or "E") || opens is not ("N" or "S") : hinge is not ("N" or "S") || opens is not ("W" or "E")))
                        throw new FormatException("Door hinge/opening directions do not match the edge axis.");
                    var edge=new Edge(r,c,axis=="H");
                    if(!layout.InteriorEdge(edge) || layout.Edges.GetValueOrDefault(edge)!=Barrier.Door || layout.DoorOptions.ContainsKey(edge)) throw new FormatException("Door settings need one existing interior door edge, without duplicates.");
                    layout.DoorOptions.Add(edge,new(hinge is "E" or "S",opens is "S" or "E"));
                } else if(key=="lock") {
                    if(metadata.GetValueOrDefault("format") is not ("3" or "4" or "5" or "6" or "7" or "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Door locks require format: 3 or newer.");
                    var words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length!=3)throw new FormatException("lock needs row,column H/V LOCKED/UNLOCKED.");
                    var xy=words[0].Split(',');
                    if(xy.Length!=2 || !int.TryParse(xy[0],out int r) || !int.TryParse(xy[1],out int c))throw new FormatException("Invalid lock edge.");
                    string axis=words[1].ToUpperInvariant(),state=words[2].ToUpperInvariant();
                    if(axis is not ("H" or "V") || state is not ("LOCKED" or "UNLOCKED"))throw new FormatException("Invalid door lock state or axis.");
                    var edge=new Edge(r,c,axis=="H");
                    if(!layout.InteriorEdge(edge) || layout.Edges.GetValueOrDefault(edge)!=Barrier.Door || !layout.DoorLocks.TryAdd(edge,state=="LOCKED"))throw new FormatException("Lock settings need one existing interior door edge, without duplicates.");
                } else if(key=="presence") {
                    if(metadata.GetValueOrDefault("format") is not ("4" or "5" or "6" or "7" or "8" or "9" or "10" or "11" or "12" or "13" or "14" or "15" or "16" or "17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Target presence requires format: 4 or newer.");
                    var words=value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length!=2)throw new FormatException("presence needs row,column RANDOM/ALWAYS.");
                    var xy=words[0].Split(',');
                    if(xy.Length!=2 || !int.TryParse(xy[0],out int r) || !int.TryParse(xy[1],out int c))throw new FormatException("Invalid target presence cell.");
                    string state=words[1].ToUpperInvariant();var cell=new Cell(r,c);
                    if(state is not ("RANDOM" or "ALWAYS") || !layout.Contains(cell) || !presence.TryAdd(cell,state=="ALWAYS"))throw new FormatException("Invalid or duplicate target presence setting.");
                } else {
                    string[] words=value.Split(new[]{' ','\t',','},StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length<3 || words.Length>5) throw new FormatException("prop needs name x z [yaw] [height].");
                    float Number(int index) {
                        if(index>=words.Length) return 0;
                        if(!float.TryParse(words[index],NumberStyles.Float,Numbers,out float number) || !float.IsFinite(number)) throw new FormatException("Prop numbers must be finite.");
                        return number;
                    }
                    float yaw=Number(3)%360;if(yaw<0)yaw+=360;if(yaw==0)yaw=0;
                    layout.Props.Add(new(words[0],Number(1),Number(2),yaw,Number(4)));
                }
            }
            foreach(var (cell,always) in presence) {
                if(!layout.Targets.TryGetValue(cell,out var target))throw new FormatException("Target presence must refer to an existing target.");
                layout.Targets[cell]=target with{Always=always};
            }
            foreach(var(cell,pose) in poses){if(!layout.Targets.TryGetValue(cell,out var target))throw new FormatException("Target pose must refer to an existing grid target.");layout.Targets[cell]=target with{OffsetX=pose.X,OffsetZ=pose.Z,Yaw=pose.Yaw};}
            foreach(var(reference,pose) in mounts) {
                var target=layout.Find(reference);if(target==null)throw new FormatException("Standless mounts must refer to an existing target.");
                if(target.Role!=TargetRole.Threat && version is not ("17" or "18" or "19" or "20" or "21" or "22" or "23" or "24" or "25" or "26" or "27"))throw new FormatException("Standless no-shoot and hostage targets require format: 17.");
                var next=target with{Style=TargetStyle.Standless,Height=pose.Height,Pitch=pose.Pitch,Roll=pose.Roll};
                if(reference.Id==0)layout.Targets[reference.Cell]=next;else layout.FineTargets[layout.FineTargets.FindIndex(t=>t.Id==reference.Id)]=next;
            }
            CombatantFormat.Finish(layout,routePoints);
            var sseError=SseObjectives.Check(layout).FirstOrDefault(i=>i.Fatal);if(sseError!=null)throw new FormatException(sseError.Message);
            var powerError=PowerCircuits.Check(layout).FirstOrDefault(i=>i.Fatal);if(powerError!=null)throw new FormatException(powerError.Message);
            var triggerError=ScenarioTriggers.Check(layout).FirstOrDefault(i=>i.Fatal);if(triggerError!=null)throw new FormatException(triggerError.Message);
            return new(layout,issues);
        } catch(Exception error) when(error is FormatException or ArgumentException or OverflowException) { issues.Add(new(error.Message,true)); return new(null,issues); }
    }
    private static TargetRole? Role(char token)=>char.ToUpperInvariant(token) switch {'T'=>TargetRole.Threat,'N'=>TargetRole.Civilian,'H'=>TargetRole.Hostage,_=>null};
    private static Barrier ParseBarrier(string text) {
        if(text.IndexOfAny(new[]{'W','w'})>=0)return Barrier.Window;
        if(text.IndexOfAny(new[]{'D','d'})>=0) return Barrier.Door;
        if(text.IndexOfAny(new[]{'O','o'})>=0) return Barrier.Frame;
        return text.IndexOfAny(new[]{'-','|','#','=','+'})>=0 ? Barrier.Wall : Barrier.Open;
    }
    public static string Write(Layout layout) {
        if(!MapLighting.For(layout).Valid)throw new FormatException("Invalid map lighting settings.");
        if(layout.AllTargets.Any(t=>t.Style is not (TargetStyle.Upright or TargetStyle.Standless) || t.Style==TargetStyle.Upright && (t.Height!=0 || t.Pitch!=0 || t.Roll!=0)))throw new ArgumentException("Height/tilt requires a valid standless target style.");
        var sseIssues=SseObjectives.Check(layout).Where(i=>i.Fatal).ToArray();if(sseIssues.Length>0)throw new ArgumentException(string.Join(" ",sseIssues.Select(i=>i.Message)));
        var powerIssues=PowerCircuits.Check(layout).Where(i=>i.Fatal).ToArray();
        if(powerIssues.Length>0)throw new FormatException(powerIssues[0].Message);
        var output=new StringBuilder();
        output.Append("name: ").Append(layout.Name.Replace('\n',' ').Replace('\r',' ')).Append('\n');
        output.Append("author: ").Append(layout.Author.Replace('\n',' ').Replace('\r',' ')).Append('\n');
        if(layout.BoundaryWalls.Count>0 || layout.Circuits.Any(c=>c.Independent) || layout.Triggers.Any(t=>t.BreakerWhen!=BreakerSignal.Off))output.Append("format: 27\n");else if(layout.Props.Any(p=>!p.SupportPeers.Empty))output.Append("format: 26\n");else if(layout.Sse.Count>0 || layout.Triggers.Any(t=>t.Event==TriggerEvent.SseCollected))output.Append("format: 25\n");else if(layout.Triggers.Any(ScenarioTriggers.HasEnteringDespawn))output.Append("format: 24\n");else if(layout.Combatants.Any(b=>b.FleeDelay!=Combatants.DefaultFleeDelay))output.Append("format: 23\n");else if(layout.Triggers.Any(t=>t.ActorIds!=null))output.Append("format: 22\n");else if(layout.Triggers.Any(t=>t.Event==TriggerEvent.BreakerOff))output.Append("format: 21\n");else if(ScenarioTriggers.HasData(layout))output.Append("format: 20\n");else if(layout.Circuits.Any(c=>c.Brightness!=1))output.Append("format: 19\n");else if(PowerCircuits.HasData(layout))output.Append("format: 18\n");else if(layout.AllTargets.Any(t=>t.Style==TargetStyle.Standless && t.Role!=TargetRole.Threat))output.Append("format: 17\n");else if(layout.Lighting is LightingPreset.Dawn or LightingPreset.Dusk || layout.Darkness!=0)output.Append("format: 16\n");else if(layout.Night || layout.Tags.Count>0)output.Append("format: 15\n");else if(layout.Combatants.Any(b=>b.Order==CombatOrder.Cautious))output.Append("format: 13\n");else if(layout.Routes.Any(r=>r.Pacing!=PatrolPacing.Natural))output.Append("format: 12\n");else if(layout.Combatants.Any(b=>b.Reaction!=CivilianReaction.None))output.Append("format: 11\n");else if(layout.Combatants.Count>0 || layout.Routes.Count>0 || layout.Encounter!=EncounterMode.Mixed)output.Append("format: 10\n");else if(layout.Edges.Any(p=>p.Value==Barrier.Window) || layout.WindowOptions.Count>0)output.Append("format: 9\n");else if(layout.AllTargets.Any(t=>t.Style!=TargetStyle.Upright))output.Append("format: 8\n");else if(layout.Props.Any(p=>p.SupportId!=0))output.Append("format: 7\n");else if(layout.Props.Any(p=>p.Id>0))output.Append("format: 6\n");else if(layout.FineTargets.Count>0 || layout.Targets.Values.Any(t=>t.Yaw.HasValue || t.OffsetX!=0 || t.OffsetZ!=0))output.Append("format: 5\n");else if(layout.Targets.Values.Any(t=>t.Always))output.Append("format: 4\n");else if(layout.DoorLocks.Count>0)output.Append("format: 3\n");else if(layout.DoorOptions.Count>0) output.Append("format: 2\n");
        output.Append("house: ").Append(layout.Houses switch {1=>"1",2=>"2",_=>"both"}).Append("\n\n");
        if(layout.Lighting is LightingPreset.Dawn or LightingPreset.Dusk || layout.Darkness!=0)output.Append("lighting: ").Append(layout.Lighting.ToString().ToUpperInvariant()).Append("\ndarkness: ").Append(layout.Darkness.ToString("R",Numbers)).Append('\n');
        else if(layout.Night)output.Append("night: true\n");
        if(layout.Tags.Count>0)output.Append("tags: ").Append(string.Join(",",MapTags.Normalize(layout.Tags).Select(Uri.EscapeDataString))).Append('\n');
        char EdgeGlyph(Barrier edge,bool horizontal) => edge switch {Barrier.Window=>'W',Barrier.Door=>'D',Barrier.Frame=>'O',Barrier.Wall=>horizontal?'-':'|',_=>' '};
        for(int row=0;row<=layout.Rows;row++) {
            for(int col=0;col<layout.Columns;col++) { var e=layout.Edges.GetValueOrDefault(new(row,col,true)); output.Append('+').Append(e==Barrier.Open?"   ":e==Barrier.Wall?"---":$"-{EdgeGlyph(e,true)}-"); }
            output.Append("+\n"); if(row==layout.Rows) break;
            for(int col=0;col<=layout.Columns;col++) {
                output.Append(EdgeGlyph(layout.Edges.GetValueOrDefault(new(row,col,false)),false)); if(col==layout.Columns) break;
                var cell=new Cell(row,col);
                char role=layout.Targets.TryGetValue(cell,out var target) ? "TNH"[(int)target.Role] : '.';
                char theme=layout.Themes.TryGetValue(cell,out var style) ? "bolsc"[(int)style] : ' ';
                output.Append(role).Append(' ').Append(theme);
            }
            output.Append('\n');
        }
        foreach(var edge in layout.BoundaryWalls.OrderBy(e=>e.Horizontal).ThenBy(e=>e.Row).ThenBy(e=>e.Column))output.Append($"boundary-wall: {edge.Row},{edge.Column} {(edge.Horizontal?"H":"V")}\n");
        foreach(var target in layout.Targets.Values.Where(t=>t.Facing.HasValue).OrderBy(t=>t.Cell.Row).ThenBy(t=>t.Cell.Column)) output.Append($"stand: {"TNH"[(int)target.Role]} {target.Cell.Row},{target.Cell.Column} {"NESW"[(int)target.Facing!.Value]}\n");
        foreach(var target in layout.Targets.Values.Where(t=>t.Always).OrderBy(t=>t.Cell.Row).ThenBy(t=>t.Cell.Column))output.Append($"presence: {target.Cell.Row},{target.Cell.Column} ALWAYS\n");
        foreach(var t in layout.Targets.Values.Where(t=>t.Yaw.HasValue || t.OffsetX!=0 || t.OffsetZ!=0).OrderBy(t=>t.Cell.Row).ThenBy(t=>t.Cell.Column))output.Append($"pose: {t.Cell.Row},{t.Cell.Column} {t.OffsetX.ToString("R",Numbers)} {t.OffsetZ.ToString("R",Numbers)} {(t.Yaw.HasValue?t.Yaw.Value.ToString("R",Numbers):"AUTO")}\n");
        foreach(var t in layout.FineTargets.OrderBy(t=>t.Id))output.Append($"target: {t.Id} {"TNH"[(int)t.Role]} {((t.Cell.Column+.5f)*2+t.OffsetX).ToString("R",Numbers)} {((t.Cell.Row+.5f)*2+t.OffsetZ).ToString("R",Numbers)} {(t.Yaw.HasValue?t.Yaw.Value.ToString("R",Numbers):t.Facing.HasValue?(90*(int)t.Facing.Value).ToString(Numbers):"AUTO")} {(t.Always?"ALWAYS":"RANDOM")}\n");
        foreach(var t in TargetGeometry.Ordered(layout).Where(t=>t.Style==TargetStyle.Standless))output.Append($"mount: {(t.Id>0?t.Id.ToString(Numbers):$"{t.Cell.Row},{t.Cell.Column}")} STANDLESS {t.Height.ToString("R",Numbers)} {t.Pitch.ToString("R",Numbers)} {t.Roll.ToString("R",Numbers)}\n");
        foreach(var (edge,door) in layout.DoorOptions.OrderBy(p=>p.Key.Horizontal).ThenBy(p=>p.Key.Row).ThenBy(p=>p.Key.Column))
            output.Append($"door: {edge.Row},{edge.Column} {(edge.Horizontal?"H":"V")} {(edge.Horizontal?(door.HingeHigh?"E":"W"):(door.HingeHigh?"S":"N"))} {(edge.Horizontal?(door.OpensPositive?"S":"N"):(door.OpensPositive?"E":"W"))}\n");
        foreach(var (edge,locked) in layout.DoorLocks.OrderBy(p=>p.Key.Horizontal).ThenBy(p=>p.Key.Row).ThenBy(p=>p.Key.Column))
            output.Append($"lock: {edge.Row},{edge.Column} {(edge.Horizontal?"H":"V")} {(locked?"LOCKED":"UNLOCKED")}\n");
        foreach(var (edge,settings) in layout.WindowOptions.OrderBy(p=>p.Key.Horizontal).ThenBy(p=>p.Key.Row).ThenBy(p=>p.Key.Column))
            output.Append($"window: {edge.Row},{edge.Column} {(edge.Horizontal?"H":"V")} {settings.Width.ToString("R",Numbers)} {settings.Sill.ToString("R",Numbers)} {settings.Height.ToString("R",Numbers)}\n");
        foreach(var prop in layout.Props) {output.Append(prop.Id>0?"furniture: "+prop.Id.ToString(Numbers)+" ":"prop: ").Append(prop.Id>0?Uri.EscapeDataString(prop.Name):prop.Name).Append(' ').Append(prop.X.ToString("R",Numbers)).Append(' ').Append(prop.Z.ToString("R",Numbers)).Append(' ').Append(prop.Yaw.ToString("R",Numbers)).Append(' ').Append(prop.Height.ToString("R",Numbers));if(prop.Id>0 && layout.Props.Any(p=>p.SupportId!=0))output.Append(' ').Append(prop.SupportId.ToString(Numbers));if(!prop.SupportPeers.Empty)output.Append(' ').Append(prop.SupportPeers.Value);output.Append('\n');}
        CombatantFormat.Write(layout,output);
        PowerCircuits.Write(layout,output);
        SseObjectives.Write(layout,output);
        ScenarioTriggers.Write(layout,output);
        return output.ToString();
    }
}
