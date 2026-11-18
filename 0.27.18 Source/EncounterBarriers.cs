using OperatorKillhouse.Core;
using Pathfinding;
using UnityEngine;

namespace OperatorKillhouse;

internal static class EncounterBarriers
{
    internal static GameObject Create(Transform anchor,float floor,Layout doc,GraphMask mask) {
        var root=new GameObject("Killhouse encounter wall cuts");root.SetActive(false);root.transform.SetParent(anchor,false);
        try {
            foreach(var block in EncounterBoundary.Blocks(doc)) {
                var go=new GameObject("Authored navigation barrier");go.transform.SetParent(root.transform,false);
                go.transform.localPosition=new(ConstructionPlan.Left+block.East,floor+1.5f,ConstructionPlan.North-block.South);
                var cut=go.AddComponent<NavmeshCut>();cut.graphMask=mask;cut.type=NavmeshCut.MeshType.Rectangle;
                cut.rectangleSize=new(block.Width,block.Depth);cut.height=8;cut.center=Vector3.zero;
                cut.useRotationAndScale=true;cut.radiusExpansionMode=NavmeshCut.RadiusExpansionMode.ExpandByAgentRadius;
                cut.isDual=false;cut.cutsAddedGeom=true;
            }
            root.SetActive(true);foreach(var cut in root.GetComponentsInChildren<NavmeshCut>())cut.ForceUpdate();
            return root;
        }catch{root.SetActive(false);UnityEngine.Object.Destroy(root);throw;}
    }
}
