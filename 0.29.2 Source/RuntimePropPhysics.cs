using UnityEngine;

namespace OperatorKillhouse;

internal static partial class Runtime
{
    private static bool BeforePropShot(OnBulletHitAddForce __instance,BulletInstance __0,Ray __1,RaycastHit __2)=>!CircuitFixtures.Shot(__instance,__0,__2) && PropPhysics.Shot(__instance,__0,__1,__2);
    private static void AfterPropKick(Melee __instance,Collider __0)=>PropPhysics.Kick(__instance,__0);
    private static void BeforePropBullet(BulletInstance __instance,ref float __state){
        __state=__instance?.Force??0;
        if(__instance is not null)CircuitFixtures.Shot(__instance,__instance.hit);
    }
    private static Exception? AfterPropBullet(BulletInstance __instance,float __state,Exception? __exception){if(__instance is not null)PropPhysics.RestoreBullet(__instance,__state);return __exception;}
}
