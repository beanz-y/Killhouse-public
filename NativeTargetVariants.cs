using UnityEngine;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

// Keep the native paper renderer, child hitboxes/tags and root damage hooks.
// Preparation runs on an inactive owned clone, before any native Awake.
internal static class NativeTargetVariants
{
    internal static bool IsPaper(Transform node) {
        for(Transform? current=node;current;current=current!.parent)
            if(current!.name.StartsWith("TgtTargetStandA5",StringComparison.Ordinal))return false;
        return true;
    }
    internal static void RemoveStand(GameObject clone) {
        var stands=clone.GetComponentsInChildren<Transform>(true)
            .Where(t=>t!=clone.transform && !IsPaper(t) && t.parent && IsPaper(t.parent!)).ToArray();
        if(stands.Length!=1)throw new InvalidOperationException("Expected one separate native target stand; paper variant was not activated.");
        foreach(var stand in stands) {
            stand.gameObject.SetActive(false);
            stand.SetParent(null,false);
            Object.Destroy(stand.gameObject);
        }
        // The native initializer gathers retained renderers when this flag is
        // set. Replace serialized projection references, including the removed
        // stand, without editing any borrowed template/list/material.
        foreach(var damage in clone.GetComponentsInChildren<BulletDamageable>(true)) {
            damage.GrabAllChildRenderers=true;damage.justSkinned=false;
            damage.projectingRenderers=new Il2CppSystem.Collections.Generic.List<BulletDamageable.ProjectingRenderer>();
        }
    }
}

