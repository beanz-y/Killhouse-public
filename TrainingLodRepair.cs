using OperatorKillhouse.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperatorKillhouse;

// Repair the loaded training scene's native sources before making any custom
// wall copies. This is event/attachment work, never a per-frame scene scan.
internal static class TrainingLodRepair
{
    internal static void RepairScene() {
        var scene=SceneManager.GetActiveScene();
        if(!scene.IsValid() || !scene.isLoaded || !TrainingLifetime.IsTraining(scene.name))return;
        foreach(var computer in UnityEngine.Object.FindObjectsOfType<RangeComputer>(true))
            if(computer && computer.gameObject.scene.handle==scene.handle)RepairRange(computer);
    }

    internal static void RepairRange(RangeComputer computer) {
        if(!NativeStockRepair.Applies(computer))return;
        var scene=computer.gameObject.scene;
        if(!scene.isLoaded || !TrainingLifetime.IsTraining(scene.name) || computer.shootHouseLayouts==null)return;
        var visited=new HashSet<int>();int removed=0;
        // Custom slots are added later. Only the three native layouts supply
        // reinforced wall/frame templates, including currently inactive ones.
        for(int index=0;index<Math.Min(3,computer.shootHouseLayouts.Length);index++) {
            var entry=computer.shootHouseLayouts[index];
            if(entry==null || !entry.layout)continue;
            foreach(var group in entry.layout.GetComponentsInChildren<LODGroup>(true))
                if(group && visited.Add(group.gameObject.GetInstanceID()))removed+=NativeLodOwnership.Repair(group.gameObject);
        }
        if(removed>0)Plugin.Logger.LogInfo($"Training LOD ownership repaired: {computer.name}; removed {removed} redundant parent renderer registrations; child detail levels preserved.");
    }
}
