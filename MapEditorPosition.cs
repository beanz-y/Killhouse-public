using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;
internal sealed partial class MapEditor
{
    private bool propCoordinatesOpen;
    private int propCoordinateIndex=-1,propCoordinateRevision=-1;
    private string propXText="0",propZText="0";
    private void PropPositionPicker(int index,PlacedProp prop) {
        propCoordinatesOpen=KillhouseUi.Foldout(propCoordinatesOpen,"Position coordinates");
        if(!propCoordinatesOpen || PropPlacement.Shape(prop,propShapes) is not {} shape)return;
        if(propCoordinateIndex!=index || propCoordinateRevision!=Draft.Revision) {
            var grid=PropPlacement.Grid(prop,shape);
            propXText=(grid.X*2).ToString("R",System.Globalization.CultureInfo.InvariantCulture);propZText=(grid.Y*2).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            propCoordinateIndex=index;propCoordinateRevision=Draft.Revision;
        }
        KillhouseUi.BeginSection("Position · metres from NW");
        try {
            GUILayout.BeginHorizontal();try{GUILayout.Label("East");GUI.SetNextControlName("kh-prop-x");propXText=GUILayout.TextField(propXText,14);}finally{GUILayout.EndHorizontal();}
            GUILayout.BeginHorizontal();try{GUILayout.Label("South");GUI.SetNextControlName("kh-prop-z");propZText=GUILayout.TextField(propZText,14);}finally{GUILayout.EndHorizontal();}
            if(GUILayout.Button("Apply position") && Number(propXText,out float x) && Number(propZText,out float z)) {
                var next=PropPlacement.At(prop.Name,shape,x/2,z/2,prop.Yaw,prop.Id>0?prop.Id:Draft.NextPropId(),prop.Height,PropPositionStep());
                var branch=LayoutDraft.PropTree(Draft.Document,prop.Id);
                next=SurfaceProp(AlignProp(next,shape),shape,propShapes,DisplayFurniture().Where(p=>!branch.Contains(p.FixedId)),propOnTop);
                Draft.CommitProp(index,next);PropFeedback("Furniture moved.");
            }
        }finally{KillhouseUi.EndSection();}
    }
}
