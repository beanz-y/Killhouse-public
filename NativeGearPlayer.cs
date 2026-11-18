namespace OperatorKillhouse;

// The gameplay actor is DontDestroyOnLoad. It intentionally does not share the
// outdoor scenery's scene handle; identity comes from the game's local actor.
internal static class NativeGearPlayer
{
    internal static bool Owned(PlayerNetworking? player)=>player && player!.isOwned && player.health && !player.health.isDead && GameManager.myPlayerNetworking && player.Pointer==GameManager.myPlayerNetworking.Pointer;
}
