namespace OperatorKillhouse.Core;

// Managed callers use the same measured constructed-panel mounting as lights.
// Permanent outer walls are supplied by NativeShell, never a synthetic border.
public static class BreakerMount
{
    public const float Reach=FixtureMount.Reach,Gap=FixtureMount.Gap;
    public static PlacedProp Snap(Layout doc,PlacedProp prop,PropShape shape,IReadOnlyList<BarrierSegment> architecture)=>
        prop.Name==PowerCircuits.BreakerAsset?FixtureMount.Nearest(prop,FixtureMount.InteriorCandidates(prop,shape,architecture)):prop;
}
