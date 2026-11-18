namespace OperatorKillhouse.Core;

public static class FixtureLoad
{
    // Work on a copy. Keep the library file/ID, grid, other props and wiring
    // intact; editor saves can explicitly persist the corrected positions.
    public static Layout Snap(Layout original,IReadOnlyDictionary<string,PropShape> shapes,Func<Layout,PlacedProp,PropShape,PlacedProp> align) {
        if(!original.Props.Any(p=>FixtureMount.Mounted(p.Name)))return original;
        var doc=LayoutDraft.Copy(original);bool changed=false;
        for(int n=0;n<doc.Props.Count;n++) {
            var p=doc.Props[n];if(!FixtureMount.Mounted(p.Name) || PropPlacement.Shape(p,shapes) is not {} shape)continue;
            var next=align(doc,p,shape);if(next==p)continue;
            // Stable millimetre positions keep independently measured host and
            // guest backing samples from introducing sub-millimetre hash noise.
            next=next with{X=MathF.Round(next.X,3),Z=MathF.Round(next.Z,3),Yaw=MathF.Round(next.Yaw,3)};
            if(next!=p){doc.Props[n]=next;changed=true;}
        }
        return changed?doc:original;
    }
}
