using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal static class FixtureSupports
{
    internal static void Attach(Layout doc,IReadOnlyDictionary<int,GameObject> nodes) {
        foreach(var prop in doc.Props.Where(p=>FixtureMount.Portable(p.Name) && p.SupportId>0)) {
            if(!nodes.TryGetValue(prop.Id,out var fixture) || !nodes.TryGetValue(prop.SupportId,out var support))throw new InvalidOperationException("Portable light support did not enter the prepared map.");
            // A light follows the authored support through host physics, guest
            // interpolation and range reset. Its wiring/damage owner stays the
            // layout root. Dynamic supports require convex compound collision.
            if(support.GetComponentInParent<Rigidbody>())foreach(var surface in fixture.GetComponentsInChildren<MeshCollider>(true))if(surface.sharedMesh)surface.convex=true;
            fixture.transform.SetParent(support.transform,true);
        }
    }
}
