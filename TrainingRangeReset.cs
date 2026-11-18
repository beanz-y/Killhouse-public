using Mirror;

namespace OperatorKillhouse;

internal static class TrainingRangeReset
{
    internal static void Apply(RangeComputer computer) {
        if(!NetworkServer.active || !computer)throw new InvalidOperationException("Only the host can reset a range.");
        // Native reset cleans BulletDamageable target hit textures, restores
        // target pose and clears scoring. It keeps the current target masks.
        computer.UserCode_RPC_StopTimer();computer.UserCode_RPC_ResetRange();
        computer.RPC_StopTimer();computer.RPC_ResetRange();
    }
}
