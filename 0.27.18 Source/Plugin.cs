using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ModControlsIntegration;
using UnityEngine;

namespace OperatorKillhouse;

[BepInPlugin(Id,"Operator Killhouse",Version)]
[BepInDependency(ModControlsBridge.PluginId,BepInDependency.DependencyFlags.SoftDependency)]
[BepInIncompatibility("krizz.degenerate.extendedkillhouse")]
public sealed class Plugin : BasePlugin
{
    public const string Id="local.operator.killhouse",Version=OperatorKillhouse.Core.CoopProtocol.Build;
    internal static ManualLogSource Logger=null!;
    internal static ConfigEntry<bool> Enabled=null!,Legacy=null!,Furniture=null!,Strict=null!,AvoidRepeat=null!,Coop=null!,EditorGrid=null!,CombatAI=null!,OutdoorLocker=null!,CachePlacedLightShadows=null!,DiagnosticLogging=null!;
    internal static ConfigEntry<float> Density=null!;
    internal static ConfigEntry<int> MapFilter=null!,PlacedLightShadows=null!;
    internal static ConfigEntry<KeyCode> PanelKey=null!,BreakerKey=null!,GearKey=null!;
    private KillhouseHost? host;
    public override void Load() {
        Logger=Log;
        Enabled=Config.Bind("General","Enabled",true,"Enable custom ranges. Disabling restores stock ranges.");
        Coop=Config.Bind("Multiplayer","Enabled",true,"Enable Steam-lobby co-op with matching Killhouse versions and furniture settings. The host's selected map is shared automatically.");
        Legacy=Config.Bind("Layouts","ImportLegacyLayouts",true,"Read user-provided ExtendedKillhouse text layouts on the next reload. No original layouts are bundled.");
        Strict=Config.Bind("Layouts","StrictValidation",false,"Reject disconnected areas and excessive door requests on the next reload.");
        Furniture=Config.Bind("Layouts","Furniture",true,"Include props and independently generated theme furniture when geometry is next built. Reload layouts to rebuild.");
        AvoidRepeat=Config.Bind("Training","AvoidImmediateRepeat",true,"Randomize without immediately choosing the current layout.");
        Density=Config.Bind("Training","TargetDensity",.5f,new ConfigDescription("Probability of Random targets being active in the next run. Always targets are included in every run.",new AcceptableValueRange<float>(0,1)));
        PanelKey=Config.Bind("Controls","PanelKey",KeyCode.F8,"Open the range panel near a training laptop. Escape closes it.");
        BreakerKey=Config.Bind("Controls","BreakerKey",KeyCode.K,"Additional breaker shortcut. The native Interact binding also works within 2.75 m. Assign circuits in the map editor.");
        OutdoorLocker=Config.Bind("Training","OutdoorGearLocker",true,"Place a character gear locker and overhead locker/bench lights beside the outdoor weapons bench. Native Interact opens it for each player; applies live.");
        GearKey=Config.Bind("Controls","GearLockerKey",KeyCode.None,"Optional gear-locker shortcut within 2.75 m. Native Interact opens the game's customization screen. Use its normal exit to save and return to the range.");
        EditorGrid=Config.Bind("Editor","ShowGrid",true,"Show the two-metre snapping grid in the map editor.");
        MapFilter=Config.Bind("Library","Filter",0,new ConfigDescription("Map list: 0 A-Z (all maps), 1 AI (only maps with AI, highest count first), 2 Night (only Night maps, A-Z).",new AcceptableValueRange<int>(0,2)));
        CombatAI=Config.Bind("Training","AIEncounters",true,"Allow the host to explicitly start authored AI encounters from the range laptop. Disabling removes this mod's combatants.");
        PlacedLightShadows=Config.Bind("Performance","PlacedLightShadows",2,new ConfigDescription("Local placed-light shadows: 0 Off, 1 Reduced (up to 256), 2 Standard (up to 512). Dense maps automatically lower resolution to bound shadow texture cost. Applies live; light output, circuits and damage are preserved.",new AcceptableValueRange<int>(0,2)));
        CachePlacedLightShadows=Config.Bind("Performance","CachePlacedLightShadows",true,"Cache fixed wall shadows for placed lights while moving shadows update every frame. Uses normal realtime shadows if the native cache is unavailable or full. Applies live; disable to compare frame times.");
        DiagnosticLogging=Config.Bind("Diagnostics","VerboseLogging",false,"Write detailed support traces, selection/co-op journals, map reports and performance samples. Leave off for normal play. Asset validation, asset-load-report.json and actionable warnings/errors remain enabled.");
        DiagnosticLogging.SettingChanged+=OnDiagnosticLoggingChanged;OnDiagnosticLoggingChanged(null,EventArgs.Empty);
        host=AddComponent<KillhouseHost>();
        try { Runtime.Start(); Log.LogInfo($"Operator Killhouse {Version} ready. F8 near a training laptop."); }
        catch(Exception error) { Runtime.StartupFailed(error); }
        RegisterMenu();
    }
    private static void RegisterMenu() {
        try {
            const string group="Killhouse";
            ModControlsBridge.RegisterToggle(Id,group,"Enable custom ranges",Enabled,order:0);
            ModControlsBridge.RegisterBinding(Id,group,"Open range panel",PanelKey,order:1);
            ModControlsBridge.RegisterBinding(Id,group,"Additional breaker shortcut",BreakerKey,order:11);
            ModControlsBridge.RegisterToggle(Id,group,"Import legacy layout files",Legacy,"Applies on layout reload.",2);
            ModControlsBridge.RegisterToggle(Id,group,"Furniture",Furniture,"Reload layouts to rebuild existing geometry.",3);
            ModControlsBridge.RegisterToggle(Id,group,"Strict layout validation",Strict,"Applies on layout reload.",4);
            ModControlsBridge.RegisterToggle(Id,group,"Avoid immediate repeats",AvoidRepeat,order:5);
            ModControlsBridge.RegisterSlider(Id,group,"Target density",Density,.05f,"Random targets in the next run. Always targets are included.",6,"0.00");
            ModControlsBridge.RegisterToggle(Id,group,"Co-op ranges",Coop,"All players need matching versions and furniture settings. The selected map is sent automatically.",7);
            ModControlsBridge.RegisterToggle(Id,group,"Editor grid",EditorGrid,"Show the two-metre drawing grid.",8);
            ModControlsBridge.RegisterToggle(Id,group,"AI encounters",CombatAI,"Host-controlled encounters. Add AI and routes in the editor, then start them from the range laptop.",9);
            ModControlsBridge.RegisterInteger(Id,group,"Map list filter",MapFilter,"0 A-Z, 1 AI (highest first), 2 Night. Also available in F8 > Maps.",10);
            ModControlsBridge.RegisterInteger(Id,group,"Placed light shadows",PlacedLightShadows,"0 Off, 1 Reduced (up to 256), 2 Standard (up to 512). Dense maps reduce resolution automatically; applies live.",12);
            ModControlsBridge.RegisterToggle(Id,group,"Outdoor gear locker",OutdoorLocker,"Character gear beside the outdoor weapons bench. Native interaction, game equipment and multiplayer sync.",13);
            ModControlsBridge.RegisterBinding(Id,group,"Additional gear-locker shortcut",GearKey,order:14);
            ModControlsBridge.RegisterToggle(Id,group,"Cache placed light shadows",CachePlacedLightShadows,"Reuse fixed wall shadows; moving shadows stay live. Disable for a matched performance comparison.",15);
            ModControlsBridge.RegisterToggle(Id,group,"Diagnostic logging",DiagnosticLogging,"Detailed support logs and performance samples. Leave off for normal play; errors and asset checks remain active.",16);
        } catch(Exception error) { UnregisterMenu();Logger.LogWarning("Optional settings menu unavailable: "+error.GetBaseException().Message); }
    }
    private static void OnDiagnosticLoggingChanged(object? sender,EventArgs args)=>DiagnosticLog.Enabled=DiagnosticLogging.Value;
    private static void UnregisterMenu() {try{ModControlsBridge.Unregister(Id);}catch(Exception error){Logger.LogWarning(error.GetBaseException().Message);}}
    public override bool Unload() {Runtime.Stop();if(host) UnityEngine.Object.Destroy(host);host=null;UnregisterMenu();DiagnosticLogging.SettingChanged-=OnDiagnosticLoggingChanged;DiagnosticLog.Enabled=false;return true;}
}
public sealed class KillhouseHost : MonoBehaviour
{
    public KillhouseHost(IntPtr pointer):base(pointer) { }
    public void Update()=>Runtime.Update();
    public void LateUpdate()=>Runtime.GuardCursor();
    public void OnGUI()=>Runtime.Draw();
    public void OnApplicationQuit()=>Runtime.Stop();
    public void OnDestroy()=>Runtime.Stop();
}
