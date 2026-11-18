using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class MapEditor
{
    private string mountHeight="1.2";
    private int powerProp=-1,powerRevision=-1;
    private static PlacedProp SurfaceProp(PlacedProp prop,PropShape shape,IReadOnlyDictionary<string,PropShape> shapes,IEnumerable<FurnitureItem> items,bool top)=>
        PowerCircuits.IsElectrical(prop.Name) && !FixtureMount.Portable(prop.Name)?prop with{SupportId=0,SupportPeers=default}:PropRules.OnSurface(prop,shape,shapes,items,top);
    private float DefaultPropHeight(string name)=>FixtureMount.Wall(name)?1.6f:name==PowerCircuits.BreakerAsset?1.2f:0;
    private void PowerInspector(PlacedProp? selected) {
        string? asset=selected?.Name??propAsset;
        if(asset==null || !PowerCircuits.IsElectrical(asset))return;
        var doc=Draft.Document;
        if(powerProp!=(selected?.Id??0) || powerRevision!=Draft.Revision) {
            powerProp=selected?.Id??0;powerRevision=Draft.Revision;
            mountHeight=(selected?.Height??propHeight).ToString("R",System.Globalization.CultureInfo.InvariantCulture);

        }
        KillhouseUi.BeginSection("Fixture height");
        try {
            if(FixtureMount.Portable(asset))KillhouseUi.Note("Use Floor or Prop surface in Placement. Moving a supported light finds the supporting surface and its height.");
            else {
            KillhouseUi.Note("Height measures the bottom of the fixture above the floor. Wall lights and breaker boxes snap flush to a measured solid wall and face away from it.");
            if(selected!=null && FixtureMount.Mounted(asset) && selectedProp is {} mountedIndex && GUILayout.Button("Snap to wall") && PropPlacement.Shape(selected,propShapes) is {} mountedShape) {
                Draft.CommitProp(mountedIndex,AlignProp(selected,mountedShape));PropFeedback("Fixture snapped to wall.");
            }
            mountHeight=GUILayout.TextField(mountHeight,12);
            if(GUILayout.Button("Apply height") && Number(mountHeight,out float height)) {
                if(selected!=null && selectedProp is {} index){var changed=selected with{Height=height,SupportId=0,SupportPeers=default};if(PropPlacement.Shape(changed,propShapes) is {} shape)changed=AlignProp(changed,shape);Draft.CommitProp(index,changed);PropFeedback("Fixture height changed.");}
                else if(height is >=0 and <=2.7f){propHeight=height;message="Choose a green position for the fixture.";}
                else message="Use a height from 0 to 2.7 m, keeping the whole fixture within the house height.";
            }
            }
        }finally{KillhouseUi.EndSection();}
        KillhouseUi.BeginSection("Lighting controls");
        try {
            KillhouseUi.Note("Use Lighting for circuits, breaker wiring and brightness. Fixtures are placed and moved through Props.");
            if(GUILayout.Button("Open Lighting")) {
                int? keep=selectedProp;ChangeTool(Tool.Lighting);selectedProp=keep;
            }
        }finally{KillhouseUi.EndSection();}
    }
}
