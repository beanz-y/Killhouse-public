using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

// Construct new components explicitly. Instantiating a native scenery tree can
// run networking, AI, interaction or light-controller Awake before sanitizing it.
internal static class NativePropSnapshot
{
    internal static GameObject Build(GameObject template,Transform parent,bool lamp) {
        GameObject? root=null;
        try {
            var selected=new HashSet<int>();var excluded=new HashSet<int>();var visiblePaths=new HashSet<int>();
            foreach(var group in template.GetComponentsInChildren<LODGroup>(true)) {
                var lods=group.GetLODs();
                if(lods.Length==0)continue;
                foreach(var renderer in lods[0].renderers)if(renderer) {
                    selected.Add(renderer.GetInstanceID());
                    for(var t=renderer.transform;t && t!=template.transform;t=t.parent)visiblePaths.Add(t.GetInstanceID());
                }
                foreach(var lod in lods.Skip(1))foreach(var renderer in lod.renderers)if(renderer)excluded.Add(renderer.GetInstanceID());
            }
            var lightPaths=new HashSet<int>();
            if(lamp)foreach(var light in template.GetComponentsInChildren<Light>(true)) {
                for(var t=light.transform;t && t!=template.transform;t=t.parent)lightPaths.Add(t.GetInstanceID());
            }
            int colliders=0,meshes=0;
            GameObject Copy(Transform source,Transform destination,bool top) {
                var node=new GameObject(source.name);node.SetActive(false);
                if(top)root=node;
                node.layer=source.gameObject.layer;// Do not copy native scoring or interaction tags.
                node.transform.SetParent(destination,false);
                if(!top){node.transform.localPosition=source.localPosition;node.transform.localRotation=source.localRotation;node.transform.localScale=source.localScale;}
                var mesh=source.GetComponent<MeshFilter>();var renderer=source.GetComponent<MeshRenderer>();
                if(mesh && mesh.sharedMesh && renderer && (!excluded.Contains(renderer.GetInstanceID()) || selected.Contains(renderer.GetInstanceID()))) {
                    node.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;
                    var copy=node.AddComponent<MeshRenderer>();copy.sharedMaterials=renderer.sharedMaterials;
                    copy.renderingLayerMask=renderer.renderingLayerMask;
                    copy.shadowCastingMode=renderer.shadowCastingMode;copy.receiveShadows=renderer.receiveShadows;
                    copy.lightmapIndex=-1;copy.realtimeLightmapIndex=-1;
                    copy.enabled=selected.Contains(renderer.GetInstanceID()) || renderer.enabled;meshes++;
                }
                foreach(var collider in source.GetComponents<Collider>()) {
                    if(collider.isTrigger || !collider.enabled)continue;
                    Collider? copy=null;
                    if(collider.TryCast<BoxCollider>() is {} box && box){var c=node.AddComponent<BoxCollider>();c.center=box.center;c.size=box.size;copy=c;}
                    else if(collider.TryCast<SphereCollider>() is {} sphere && sphere){var c=node.AddComponent<SphereCollider>();c.center=sphere.center;c.radius=sphere.radius;copy=c;}
                    else if(collider.TryCast<CapsuleCollider>() is {} capsule && capsule){var c=node.AddComponent<CapsuleCollider>();c.center=capsule.center;c.radius=capsule.radius;c.height=capsule.height;c.direction=capsule.direction;copy=c;}
                    else if(collider.TryCast<MeshCollider>() is {} surface && surface && surface.sharedMesh){var c=node.AddComponent<MeshCollider>();c.convex=surface.convex;c.cookingOptions=surface.cookingOptions;c.sharedMesh=surface.sharedMesh;copy=c;}
                    if(copy){copy!.sharedMaterial=collider.sharedMaterial;colliders++;}
                }
                if(lamp && source.GetComponent<Light>() is {} native && native && native.type!=LightType.Directional) {
                    var light=node.AddComponent<Light>();light.type=native.type==LightType.Spot?LightType.Spot:LightType.Point;
                    light.color=native.color;light.range=native.range;light.spotAngle=native.spotAngle;light.innerSpotAngle=native.innerSpotAngle;
                    light.enabled=false;
                }
                for(int i=0;i<source.childCount;i++)Copy(source.GetChild(i),node.transform,false);
                if(!top)node.SetActive(source.gameObject.activeSelf || lightPaths.Contains(source.GetInstanceID()) || visiblePaths.Contains(source.GetInstanceID()));
                return node;
            }
            Copy(template.transform,parent,true);
            if(meshes==0)throw new InvalidOperationException("The native prop has no static mesh: "+template.name);
            if(colliders==0) {
                // Mesh collision preserves openings instead of filling an envelope.
                foreach(var filter in root!.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
            }
            return root!;
        }catch{if(root)Object.Destroy(root!);throw;}
    }
}
