using UnityEngine;

namespace OperatorKillhouse;

internal static class EncounterDamage
{
    internal sealed class SafetyScope:IDisposable {
        private GameManager? manager;
        internal SafetyScope(GameManager manager){this.manager=manager;manager.bulletCondom=false;}
        public void Dispose(){var saved=manager;manager=null;if(saved)saved!.bulletCondom=true;}
    }
    // The native Outdoors scene sets bulletCondom, and Health.TakeDamage's
    // five-argument server path rejects all ordinary hits while it is set.
    // Allow native hits between players and server-owned encounter AI through
    // that gate. Native damage, armor, body parts, explicit god mode and replication
    // remain in control; player health is never edited by this adapter.
    internal static SafetyScope? Begin(Health target,float damage,bool ownedShooter,bool ownedTarget,bool playerShooter,bool server) {
        var manager=GameManager.instance;
        bool combat=target && (target.isBot?ownedTarget && playerShooter:ownedShooter);
        return server && combat && !target.isDead && float.IsFinite(damage) && damage>0 && manager && GameManager.InOutdoorArea && manager!.bulletCondom?new(manager):null;
    }
}
