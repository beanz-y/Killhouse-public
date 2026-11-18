using OperatorKillhouse.Core;

namespace OperatorKillhouse;

internal sealed partial class RangeSession
{
    public void Dispose() {
        if(disposed)return;disposed=true;
        using var cleanup=new CleanupStack();
        cleanup.Add(prepared.Clear);
        cleanup.Add(ownership.Dispose);
        cleanup.Add(()=>{if(Computer && Computer.gameObject.scene.isLoaded)RepairStockDoors("restore");});
        cleanup.Add(RestoreDoors);
        cleanup.Add(CancelStage);
        cleanup.Add(()=>ClearEncounter());
    }
}
