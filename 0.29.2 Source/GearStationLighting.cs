using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

// Local scenery/preview resources, owned by the locker and its native screen.
internal sealed class GearStationLighting : IDisposable
{
    private readonly GameObject root;
    private readonly List<Material> materials=new();
    private Light[] preview=Array.Empty<Light>();
    private GearStationLighting(Transform owner,string name) {
        root=new GameObject(name);root.SetActive(false);root.transform.SetParent(owner,false);
    }
    private Light Spot(string name,Vector3 position,Vector3 direction,float lumens,float angle,float reach) {
        var node=new GameObject(name);node.transform.SetParent(root.transform,false);
        node.transform.SetPositionAndRotation(position,Quaternion.LookRotation(direction,Math.Abs(direction.y)>.95f?Vector3.forward:Vector3.up));
        var light=node.AddComponent<Light>();light.type=LightType.Spot;light.color=new Color(1,.96f,.9f);
        light.range=reach;light.spotAngle=angle;light.innerSpotAngle=angle*.65f;
        light.cullingMask=-1;light.renderingLayerMask=-1;light.shadows=LightShadows.None;
        var hd=node.AddComponent<HDAdditionalLightData>();hd.volumetricDimmer=0;hd.lightDimmer=1;
        hd.affectDiffuse=true;hd.affectSpecular=true;hd.SetIntensity(lumens,LightUnit.Lumen);
        return light;
    }
    private void Cube(Transform parent,string name,Vector3 position,Vector3 size,Material material) {
        var node=GameObject.CreatePrimitive(PrimitiveType.Cube);node.name=name;node.transform.SetParent(parent,false);
        node.transform.localPosition=position;node.transform.localScale=size;
        var collider=node.GetComponent<Collider>();if(collider){collider.enabled=false;Object.Destroy(collider);}
        var mesh=node.GetComponent<Renderer>();mesh.sharedMaterial=material;mesh.shadowCastingMode=ShadowCastingMode.Off;mesh.receiveShadows=false;
    }
    internal static GearStationLighting World(Transform owner,Vector3 benchFloor,Vector3 lockerFloor) {
        var result=new GearStationLighting(owner,"Outdoor locker and bench lights (owned)");
        try {
            var shader=Shader.Find("HDRP/Lit");if(!shader)throw new InvalidOperationException("The native lighting shader is unavailable.");
            var housing=new Material(shader){name="Gear station light housing (owned)"};result.materials.Add(housing);housing.SetColor("_BaseColor",new Color(.055f,.055f,.055f));
            var lens=new Material(shader){name="Gear station light lens (owned)"};result.materials.Add(lens);
            lens.SetColor("_BaseColor",Color.white);lens.SetColor("_EmissiveColor",new Color(1,.96f,.9f)*4);
            lens.SetFloat("_UseEmissiveIntensity",0);lens.SetFloat("_EmissiveExposureWeight",0);
            foreach(var site in new[]{(Name:"Weapons bench downlight",Floor:benchFloor),(Name:"Gear locker downlight",Floor:lockerFloor)}) {
                // Below the native awning (~3.1m), above the locker and bench.
                var light=result.Spot(site.Name,site.Floor+Vector3.up*2.75f,Vector3.down,9000,110,5);
                result.Cube(light.transform,"Downlight housing",Vector3.zero,new Vector3(.32f,.19f,.065f),housing);
                result.Cube(light.transform,"Downlight lens",Vector3.forward*.036f,new Vector3(.26f,.13f,.008f),lens);
            }
            result.root.SetActive(true);return result;
        }catch{result.Dispose();throw;}
    }
    internal static GearStationLighting Preview(Transform owner) {
        var result=new GearStationLighting(owner,"Native gear preview fill lights (owned)");
        try {
            // Photo-studio outputs remain visible at the native night exposure.
            // These short-range lights exist only while this screen is active.
            result.preview=new[]{result.Spot("Preview key",Vector3.zero,Vector3.forward,14000,100,4.5f),result.Spot("Preview fill",Vector3.zero,Vector3.forward,9000,110,4.5f)};
            result.root.SetActive(true);return result;
        }catch{result.Dispose();throw;}
    }
    internal void Frame(Vector3 feet,Transform camera) {
        if(preview.Length!=2)return;
        var target=feet+Vector3.up*1.15f;var front=camera.position-target;front.y=0;
        front=front.sqrMagnitude>.01f?front.normalized:Vector3.back;var side=Vector3.Cross(Vector3.up,front);
        var key=target+front*1.8f+side*1.15f+Vector3.up*1.35f;
        var fill=target+front*1.65f-side*1.35f+Vector3.up*.6f;
        preview[0].transform.SetPositionAndRotation(key,Quaternion.LookRotation(target-key));
        preview[1].transform.SetPositionAndRotation(fill,Quaternion.LookRotation(target-fill));
    }
    public void Dispose() {
        if(root){root.SetActive(false);Object.Destroy(root);}
        foreach(var material in materials)if(material)Object.Destroy(material);materials.Clear();preview=Array.Empty<Light>();
    }
}
