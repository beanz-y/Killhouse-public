using Mirror;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed record CombatantType(string Key,string Label,GameObject Prefab,uint AssetId,AgentController Navigation);
internal static class CombatantCatalog
{
    private static CombatantType[] types=Array.Empty<CombatantType>();
    private static float nextRefresh;
    internal static string Status {get;private set;}="AI types have not been scanned yet.";
    private static string lastReport="";
    internal static IReadOnlyList<CombatantType> Types {get{if(Time.unscaledTime>=nextRefresh)Refresh();return types;}}
    internal static AgentController? Navigation(BrainAI brain) {
        var navigation=brain.agent;
        return navigation?navigation:brain.GetComponentInChildren<AgentController>(true);
    }
    internal static bool Registered(GameObject prefab,uint assetId) {
        if(NetworkClient.prefabs.TryGetValue(assetId,out var registered))return registered && registered.Pointer==prefab.Pointer;
        return NetworkClient.spawnHandlers.ContainsKey(assetId);
    }
    internal static void Refresh() {
        nextRefresh=Time.unscaledTime+5;
        var found=new List<CombatantType>();int assets=0,missingIdentity=0,missingNavigation=0,missingHealth=0,unregistered=0;
        foreach(var brain in Resources.FindObjectsOfTypeAll<BrainAI>()) {
            if(!brain || brain.gameObject.scene.IsValid() || !brain.name.StartsWith("BOT ",StringComparison.OrdinalIgnoreCase) || brain.name.Contains("(Clone)",StringComparison.OrdinalIgnoreCase))continue;
            assets++;
            var identity=brain.GetComponent<NetworkIdentity>();
            if(identity is null || !identity || identity.assetId==0){missingIdentity++;continue;}
            // Native bots keep AgentController/FollowerEntity on the model
            // child. Resolve the authored reference or inactive descendants;
            // a root-only GetComponent rejects the actual game prefabs.
            var navigation=Navigation(brain);
            if(!navigation || !navigation!.Agent){missingNavigation++;continue;}
            if(!brain.health && !brain.GetComponent<Health>()){missingHealth++;continue;}
            if(!Registered(brain.gameObject,identity.assetId)){unregistered++;continue;}
            string key=identity.assetId.ToString("x8")+":"+brain.name;
            found.Add(new(key,brain.name.Replace("BOT V2 ","").Replace(" Variant",""),brain.gameObject,identity.assetId,navigation));
        }
        types=found.GroupBy(t=>t.Key,StringComparer.Ordinal).Select(g=>g.First()).OrderBy(t=>t.Label,StringComparer.Ordinal).ToArray();
        Status=types.Length>0?$"{types.Length} AI types available.":assets==0?"The game has not loaded any AI prefab assets yet.":unregistered>0?"Loaded AI types are waiting for native network registration.":"Loaded AI assets lack required components. See the Killhouse catalogue log.";
        string report=$"AI catalogue: {types.Length} available; prefabBrains={assets}; missingIdentity={missingIdentity}; missingNavigation={missingNavigation}; missingHealth={missingHealth}; unregistered={unregistered}.";
        if(report!=lastReport){lastReport=report;DiagnosticLog.Info(report);}
    }
    internal static CombatantType Require(string key)=>Types.FirstOrDefault(t=>t.Key==key && t.Prefab)??throw new InvalidOperationException("AI prefab is unavailable: "+key+". Select an available type in the editor.");
}
