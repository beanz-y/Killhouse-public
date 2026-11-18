using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

internal static class EncounterResidue
{
    private sealed record Sweep(Transform Anchor,EncounterVolume Volume,int House,int Frame);
    private static readonly List<Sweep> pending=new();
    internal static void Schedule(Transform anchor,EncounterVolume volume,int house) {
        if(!anchor)return;
        Clear(anchor,volume,house,"stop");
        pending.RemoveAll(p=>!p.Anchor || p.Anchor.Pointer==anchor.Pointer);
        pending.Add(new(anchor,volume,house,Time.frameCount+2));
    }
    internal static void Tick() {
        foreach(var item in pending.Where(p=>Time.frameCount>=p.Frame).ToArray()) {
            pending.Remove(item);if(item.Anchor)Clear(item.Anchor,item.Volume,item.House,"deferred");
        }
    }
    internal static void Reset()=>pending.Clear();
    internal static void Clear(Transform anchor,EncounterVolume volume,int house,string phase) {
        if(!anchor || !anchor.gameObject.scene.IsValid() || !anchor.gameObject.scene.isLoaded)return;
        int effects=0,weapons=0,magazines=0,spentGrenades=0,failures=0;
        var bloodPrefabs=new HashSet<IntPtr>();var blood=BulletEffectManager.singleton;
        void BloodPrefab(GameObject? prefab){if(prefab)bloodPrefabs.Add(prefab!.Pointer);}
        if(blood) {
            BloodPrefab(blood.BloodDropsPrefab);BloodPrefab(blood.BloodPoolVFXPrefab);BloodPrefab(blood.HeadExplosionPrefab);
            if(blood.BloodLeakDecalsPrefab!=null)foreach(var prefab in blood.BloodLeakDecalsPrefab)BloodPrefab(prefab);
            if(blood.BloodPoolDecalsPrefab!=null)foreach(var prefab in blood.BloodPoolDecalsPrefab)BloodPrefab(prefab);
        }
        bool Inside(Component item) {
            if(!item || !item.gameObject.scene.IsValid() || !item.gameObject.scene.isLoaded)return false;
            var p=anchor.InverseTransformPoint(item.transform.position);return volume.Contains(p.x,p.y,p.z);
        }
        void Safe(Action action){try{action();}catch(Exception error){failures++;Plugin.Logger.LogWarning("Encounter residue cleanup: "+error.GetBaseException().Message);}}
        // Return native pooled effects through their lifecycle, including registry
        // and timer removal. Do not destroy pooled objects or clear global pools.
        foreach(var effect in Object.FindObjectsOfType<PooledObject>())Safe(()=>{
            if(!effect || effect.IsPooled || !Inside(effect))return;
            bool bloodPrefab=effect.PrefabObject && bloodPrefabs.Contains(effect.PrefabObject.Pointer);
            if(!bloodPrefab && !effect.TryCast<PooledImpactDecal>() && !effect.TryCast<RealisticAnimatedBloodPooled>() && !effect.TryCast<BloodBulletPooled>())return;
            ObjectPooler.DestroyPooled(effect);effects++;
        });
        var manager=GameManagerNetwork.instance;
        if(manager && manager.aiWeaponPuppets!=null)foreach(var weapon in manager.aiWeaponPuppets.ToArray())Safe(()=>{
            if(!weapon){manager.aiWeaponPuppets.Remove(weapon);return;}
            if(weapon.isEquiped || weapon.GetComponentInParent<Health>() || !Inside(weapon))return;
            var identity=weapon.GetComponent<NetworkIdentity>();
            if(identity && identity.netId!=0 && !NetworkServer.active)return;
            manager.aiWeaponPuppets.Remove(weapon);
            if(identity && identity.netId!=0)NetworkServer.Destroy(weapon.gameObject);else Object.Destroy(weapon.gameObject);
            weapons++;
        });
        // Some native pickups are independent network identities rather than
        // detached local puppet guns. Only the server destroys those identities.
        if(NetworkServer.active)foreach(var pickup in Object.FindObjectsOfType<WeaponPickup>())Safe(()=>{
            if(!pickup || pickup.GetComponentInParent<Health>() || !Inside(pickup))return;
            var identity=pickup.GetComponent<NetworkIdentity>();
            if(identity && identity.netId!=0)NetworkServer.Destroy(pickup.gameObject);else Object.Destroy(pickup.gameObject);
            weapons++;
        });
        // Player guns, tools and equipment use WeaponV3 rather than AI puppets.
        // Dropped state is required: inventory/holstered gear can have colliders
        // and stale owner fields, so neither alone identifies a ground pickup.
        if(NetworkServer.active)foreach(var weapon in Object.FindObjectsOfType<WeaponV3>())Safe(()=>{
            if(!weapon || !weapon.isDropped || weapon.isEquiped || weapon.l_isEquiped || weapon.GetComponentInParent<Health>() || !Inside(weapon))return;
            var identity=weapon.GetComponent<NetworkIdentity>();
            if(identity && identity.netId!=0)NetworkServer.Destroy(weapon.gameObject);else Object.Destroy(weapon.gameObject);
            weapons++;
        });
        if(NetworkServer.active)foreach(var grenade in Object.FindObjectsOfType<GrenadeV2>())Safe(()=>{
            if(!grenade || !grenade.isDropped || grenade.isEquiped || grenade.thrown || grenade.activatedIgniter || grenade.throwGrenade || grenade.underthrowGrenade || grenade.GetComponentInParent<Health>() || !Inside(grenade))return;
            var identity=grenade.GetComponent<NetworkIdentity>();
            if(identity && identity.netId!=0)NetworkServer.Destroy(grenade.gameObject);else Object.Destroy(grenade.gameObject);
            weapons++;
        });
        // Thrown flashbang canisters are GrenadeExplosive objects, separate
        // from inventory GrenadeV2 equipment. Preserve live and multi-stage
        // grenades until their final detonation has completed.
        if(NetworkServer.active)foreach(var grenade in Object.FindObjectsOfType<GrenadeExplosive>())Safe(()=>{
            if(!grenade || !grenade.isFlashbang || !grenade._exploded || grenade.multipleExplosions && grenade.timesExploded<grenade.timesExplode || grenade.GetComponentInParent<Health>() || !Inside(grenade))return;
            var identity=grenade.GetComponent<NetworkIdentity>();
            if(identity && identity.netId!=0)NetworkServer.Destroy(grenade.gameObject);else Object.Destroy(grenade.gameObject);
            spentGrenades++;
        });
        // The native registry contains dropped magazines; never sweep all
        // magazine components, which would include loaded/animated inventory.
        if(manager && manager.droppedMagazines!=null)foreach(var magazine in manager.droppedMagazines.ToArray())Safe(()=>{
            if(!magazine){manager.droppedMagazines.Remove(magazine);return;}
            if(magazine.GetComponentInParent<Health>() || magazine.GetComponentInParent<WeaponV3>() || magazine.GetComponentInParent<PuppetWeapon>() || !Inside(magazine))return;
            var identity=magazine.GetComponent<NetworkIdentity>();
            if(identity && identity.netId!=0 && !NetworkServer.active)return;
            manager.droppedMagazines.Remove(magazine);
            if(identity && identity.netId!=0)NetworkServer.Destroy(magazine.gameObject);else Object.Destroy(magazine.gameObject);
            magazines++;
        });
        CoopLog.Event("ai-residue-cleaned",new{house,phase,effects,weapons,magazines,spentGrenades,failures});
    }
}
