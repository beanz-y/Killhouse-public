using Mirror;
using UnityEngine;

namespace OperatorKillhouse;

internal static class EncounterSpawn
{
    internal static void HostOwned(GameObject bot) {
        // Match native BotSpawner's Spawn(bot, LocalPlayerMaster). WeaponsAI
        // relies on client ownership; StartBulletP2 zeros unowned bullet damage.
        var owner=NetworkServer.localConnection?.identity;
        if(!NetworkServer.active || owner is null || !owner || !owner.GetComponent<PlayerMaster>())throw new InvalidOperationException("The host player must be ready before spawning combat AI.");
        NetworkServer.Spawn(bot,owner.gameObject);
    }
}
