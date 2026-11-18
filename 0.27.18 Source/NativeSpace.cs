using UnityEngine;

namespace OperatorKillhouse;
internal static class NativeSpace
{
    // Bounds must not make a round trip through large world coordinates: that
    // loses precision and can place matching assets on opposite hash boundaries.
    internal static Matrix4x4 Relative(Transform descendant,Transform root) {
        var result=Matrix4x4.identity;var current=descendant;
        while(current && current!=root) {
            result=Matrix4x4.TRS(current.localPosition,current.localRotation,current.localScale)*result;
            current=current.parent;
        }
        if(!current)throw new InvalidOperationException("Native mesh is outside its measured assembly.");
        return result;
    }
}
