using HarmonyLib;
using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

// One local scenery/interaction replica per outdoor scene. Equipment changes
// use the game's own CharacterModSync, independently of custom map selection.
internal static class OutdoorGearLocker
{
    private static GameObject? root;
    private static BoxCollider? hit;
    private static InteractableComponent? interaction;
    private static NativeGearScreen? screen;
    private static GearStationLighting? lighting;
    private static int sceneHandle;
    private static float scanAt;
    private static string lastFailure="";
    internal static void Install(Harmony harmony)=>harmony.Patch(AccessTools.Method(typeof(InteractableComponent),nameof(InteractableComponent.AccessInteractable)),prefix:new HarmonyMethod(typeof(OutdoorGearLocker),nameof(Access)));
    private static bool Access(InteractableComponent __instance,PlayerNetworking __0) {
        if(interaction==null || __instance.Pointer!=interaction.Pointer)return true;
        Open(__0,true);return false;
    }
    internal static bool Near(PlayerNetworking player)=>root && hit && GameManager.InOutdoorArea && NativeGearScreen.Owned(player) && player.Camera && (hit!.ClosestPoint(player.Camera.transform.position)-player.Camera.transform.position).sqrMagnitude<=2.75f*2.75f;
    private static void CheckNativeHit(PlayerNetworking player) {
        // ProcessInteractable already raycast from the native view and selected
        // this collider. Recasting from a guessed standing eye rejects valid
        // prompts when crouched or under the bench's awning.
        var contact=player._interactableRayHit;
        if(!contact.collider || contact.collider.Pointer!=hit!.Pointer || !float.IsFinite(contact.distance) || contact.distance<0 || !float.IsFinite(player.interactionDetectionRange) || contact.distance>player.interactionDetectionRange+.1f)throw new InvalidOperationException("Look at the locker until its interaction prompt appears.");
    }
    private static void CheckShortcutHit(PlayerNetworking player) {
        if(!Near(player))throw new InvalidOperationException("Move closer to the gear locker.");
        var eye=player.Camera.transform.position;var delta=hit!.bounds.center-eye;
        bool reached=false;
        foreach(var contact in Physics.RaycastAll(eye,delta.normalized,delta.magnitude+.1f,-1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)) {
            if(!contact.collider || contact.collider.transform.IsChildOf(player.transform))continue;
            if(!contact.collider.transform.IsChildOf(root!.transform))break;reached=true;break;
        }
        if(!reached)throw new InvalidOperationException("The gear locker is obstructed.");
    }
    private static void Open(PlayerNetworking player,bool nativeInteraction=false) {
        if(!Plugin.Enabled.Value || !Plugin.OutdoorLocker.Value || screen?.Busy==true)return;
        try {
            if(!root || !hit || !GameManager.InOutdoorArea)throw new InvalidOperationException("The gear locker is no longer available in this area.");
            if(!NativeGearScreen.Owned(player))throw new InvalidOperationException("The gear locker requires your active, living local character.");
            if(nativeInteraction)CheckNativeHit(player);else CheckShortcutHit(player);
            if(!NativeGearAssets.Ready)throw new InvalidOperationException(NativeGearAssets.Status);
            screen??=new NativeGearScreen(root!.transform);screen.Open(player);
        }
        catch(Exception error){Runtime.Status="Gear locker: "+error.GetBaseException().Message;Plugin.Logger.LogWarning(Runtime.Status);if(HintManager.singleton)HintManager.singleton.SpawnPopup(Runtime.Status,Popup.PopupType.YellowWarning,true,false);}
    }
    internal static void Tick() {
        if(!Plugin.Enabled.Value || !Plugin.OutdoorLocker.Value || !GameManager.InOutdoorArea || !(NetworkClient.active || NetworkServer.active)){Clear();return;}
        screen?.Tick();
        if(root && interaction) {
            if(Plugin.GearKey.Value!=KeyCode.None && Input.GetKeyDown(Plugin.GearKey.Value)) {
                if(screen?.Busy==true)screen.Close();else if(GameManager.myPlayerNetworking)Open(GameManager.myPlayerNetworking);
            }
            return;
        }
        if(Time.unscaledTime<scanAt)return;scanAt=Time.unscaledTime+2;
        var computer=Object.FindObjectsOfType<RangeComputer>().FirstOrDefault(c=>c && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded);
        if(!computer)return;
        try{Create(computer!.gameObject.scene);}catch(Exception error){Clear();scanAt=Time.unscaledTime+5;string reason=error.GetBaseException().Message;if(reason!=lastFailure){lastFailure=reason;Plugin.Logger.LogWarning("Outdoor locker: "+reason);}}
    }
    private static GameObject? Find(Scene scene,string path) {
        var parts=path.Split('/');var current=scene.GetRootGameObjects().FirstOrDefault(go=>go.name==parts[0])?.transform;
        foreach(string part in parts.Skip(1))current=current?.Find(part);
        return current?current!.gameObject:null;
    }
    private static void Create(Scene scene) {
        var source=Find(scene,"armory/Lockers_2");var bench=Find(scene,"range 1/Garage_table_6");
        if(!source || !bench)return;
        var model=new NativeProp(source!,"Lockers",true);var shape=model.Shape;var table=NativeProp.Measure(bench!);
        float yaw=bench!.transform.eulerAngles.y,width=shape.MaxX-shape.MinX,depth=shape.MaxZ-shape.MinZ,height=shape.MaxY-shape.MinY;
        var rotation=Quaternion.Euler(0,yaw,0);var right=rotation*Vector3.right;var forward=rotation*Vector3.forward;
        var center=bench.transform.position+rotation*new Vector3((table.MinX+table.MaxX)*.5f,0,(table.MinZ+table.MaxZ)*.5f);
        float benchBottom=bench.transform.position.y+table.MinY;
        var rejected=new List<string>();
        Vector3? position=null;
        foreach(var offset in LockerPlacement.Offsets(table.MaxX-table.MinX,shape.MaxX-shape.MinX)) {
            var at=center+right*offset.Side+forward*offset.Forward;
            // Filter height before choosing a hit: roofs and awnings can be
            // above the same pavement in this two-storey outdoor structure.
            var ground=Physics.RaycastAll(at+Vector3.up*3,Vector3.down,8,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Where(h=>Static(h.collider) && LockerPlacement.Ground(h.point.y,h.normal.y,benchBottom)).OrderBy(h=>h.distance).FirstOrDefault();
            if(!ground.collider){rejected.Add($"({at.x:F2},{at.z:F2}) no ground near {benchBottom:F2}m");continue;}
            var p=ground.point;
            var blockers=Physics.OverlapBox(p+Vector3.up*(height*.5f+.04f),new Vector3(width*.5f-.02f,height*.5f-.04f,depth*.5f-.02f),rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            var solid=blockers.FirstOrDefault(Static);
            if(solid){rejected.Add($"({at.x:F2},{at.z:F2}) blocked by {solid!.name}");continue;}
            position=p;break;
        }
        if(position is not {} point)throw new InvalidOperationException("No clear locker site beside range 1/Garage_table_6: "+string.Join("; ",rejected));
        root=new GameObject("Outdoor gear locker [Operator Killhouse]");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);sceneHandle=scene.handle;
        var(x,z)=shape.Origin(point.x,point.z,yaw);
        model.Place(new("Lockers",new(x,z,yaw,shape.At(x,z,yaw),false),0,"outdoor gear locker",true),root.transform,root.transform,point.y);
        var target=new GameObject("Gear locker native interaction"){layer=9};target.transform.SetParent(root.transform,false);
        target.transform.position=point+Vector3.up*(height*.5f+.01f);target.transform.rotation=rotation;
        hit=target.AddComponent<BoxCollider>();hit.size=new(width+.05f,height+.05f,depth+.05f);
        interaction=target.AddComponent<InteractableComponent>();interaction.InteractableName="Gear locker";interaction.InteractableContext="Customize character";
        lighting=GearStationLighting.World(root.transform,new Vector3(center.x,benchBottom,center.z),point);
        root.SetActive(true);lastFailure="";DiagnosticLog.Info($"Outdoor character gear locker ready beside the wooden weapons bench near the range laptop at ({point.x:F2}, {point.y:F2}, {point.z:F2}).");
    }
    private static bool Static(Collider c)=>c && !c.attachedRigidbody && !c.GetComponentInParent<PlayerNetworking>() && !c.GetComponentInParent<Health>();
    internal static void SceneUnloaded(int handle){if(sceneHandle==handle)Clear();}
    internal static void Clear(){try{screen?.Dispose();}finally{screen=null;try{lighting?.Dispose();}finally{lighting=null;interaction=null;hit=null;sceneHandle=0;if(root){root!.SetActive(false);Object.Destroy(root);}root=null;}}}
}
