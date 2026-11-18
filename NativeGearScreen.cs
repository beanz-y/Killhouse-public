using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

namespace OperatorKillhouse;

// Owns the imported native station's lifetime. Selection, attachment menus,
// saving, character replication, camera controls and normal exit run inside
// CharacterCustomisationController and its original persistent UI callbacks.
internal sealed class NativeGearScreen : IDisposable
{
    private readonly GameObject root;
    private readonly CharacterCustomisationController controller;
    private GearStationLighting? lighting;
    private Camera? renderCamera;
    private HDAdditionalCameraData? renderData;
    private CameraClearFlags priorClear;
    private HDAdditionalCameraData.ClearColorMode priorHdClear;
    private Color priorBackground,priorHdBackground;
    private PlayerNetworking? player;
    private CursorLockMode priorLock;
    private bool priorCursor,priorKinematic,disposed,closing;
    internal bool Busy=>player is not null;
    internal static bool Owned(PlayerNetworking? p)=>NativeGearPlayer.Owned(p);
    internal NativeGearScreen(Transform station) {
        root=NativeGearAssets.Create(station,Vector3.zero,Quaternion.identity);
        try {
            controller=root.GetComponent<CharacterCustomisationController>();
            if(!controller || !controller.playerPosition || !controller._camera || !controller.Canvas || !controller.CameraAnimator || !controller.modUIPrefab || !controller.modParentUIPrefab)throw new InvalidDataException("Native customization references are incomplete.");
            foreach(var collider in root.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            var camera=controller._camera.GetComponent<Camera>();
            if(!camera)throw new InvalidDataException("Native customization camera is missing.");
            lighting=GearStationLighting.Preview(root.transform);
            controller.Canvas.SetActive(false);controller._camera.SetActive(false);root.SetActive(false);
        }catch {lighting?.Dispose();Object.Destroy(root);throw;}
    }
    internal void Open(PlayerNetworking target) {
        if(disposed)throw new ObjectDisposedException(nameof(NativeGearScreen));
        if(Busy)return;
        if(!Owned(target))throw new InvalidOperationException("Wait for your local character to finish spawning.");
        if(!Runtime.CanOpenGear)throw new InvalidOperationException("Close the current menu before using the gear locker.");
        var settings=target.GetComponent<CharacterSettings>();
        if(!settings || !settings.characterCustomisation || !target.masterController || !target.masterController.rigidBody || !target.Camera || !MainCameraSingleton.instance)throw new InvalidOperationException("Wait for your character and camera to finish loading.");
        Runtime.CloseForGear();player=target;closing=false;priorLock=Cursor.lockState;priorCursor=Cursor.visible;priorKinematic=target.masterController.rigidBody.isKinematic;
        try {
            // The native station uses a marker one metre above the feet. Keep
            // this player's current safe position, and move its camera/lighting
            // rig by the same amount. Other players can use the locker too.
            root.transform.rotation=Quaternion.identity;
            root.transform.position+=target.transform.position+Vector3.up-controller.playerPosition.position;
            controller.liftUpButton=false;
            root.SetActive(true);controller.StartCustomisation(target);
            ApplyBackdrop();
            lighting?.Frame(target.transform.position,renderCamera?renderCamera!.transform:controller._camera.transform);
            DiagnosticLog.Info("Outdoor locker opened the native character customization screen. Black backdrop applied to render camera: "+(renderCamera?renderCamera!.name:"unavailable"));
        }catch {Recover(false);throw;}
    }
    internal void Close() {
        if(disposed || !Busy || closing || !controller || !controller.isLocalCustomising)return;
        closing=true;controller.button_StopCustomisation();
    }
    internal void Tick() {
        if(disposed || !Busy)return;
        if(!Owned(player)){Recover(false);return;}
        // Native exit saves first, restores its camera/physics, then clears
        // Player after its final fixed-update yield. Keep it alive until then.
        if(controller.Player){
            if(controller.isLocalCustomising)ApplyBackdrop();else RestoreBackdrop();
            lighting?.Frame(player!.transform.position,renderCamera?renderCamera!.transform:controller._camera.transform);return;
        }
        RestoreBackdrop();root.SetActive(false);player=null;closing=false;Runtime.SuppressGearCloseClick();
    }
    private void ApplyBackdrop() {
        // The native station camera is a settings/animation source. Native
        // CopyCameraSettings copies only culling/occlusion to MainCam; HDRP
        // continues rendering the sky on that separate, persistent camera.
        var camera=MainCameraSingleton.MainCam;
        if(!camera)throw new InvalidOperationException("The active customization camera is unavailable.");
        if(renderCamera && renderCamera!.Pointer==camera.Pointer)return;
        RestoreBackdrop();
        renderCamera=camera;priorClear=camera.clearFlags;priorBackground=camera.backgroundColor;
        renderData=camera.GetComponent<HDAdditionalCameraData>();
        if(renderData){priorHdClear=renderData!.clearColorMode;priorHdBackground=renderData.backgroundColorHDR;}
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        if(renderData){renderData!.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;renderData.backgroundColorHDR=Color.black;}
    }
    private void RestoreBackdrop() {
        try {
            if(renderCamera){renderCamera!.clearFlags=priorClear;renderCamera.backgroundColor=priorBackground;}
            if(renderData){renderData!.clearColorMode=priorHdClear;renderData.backgroundColorHDR=priorHdBackground;}
        }finally {renderCamera=null;renderData=null;}
    }
    private void Recover(bool save) {
        if(player is null){RestoreBackdrop();return;}
        var current=player;player=null;closing=false;
        try {
            if(controller) {
                controller.StopAllCoroutines();
                if(save && Owned(current) && controller.characterSettings)controller.Preset_SAVE();
                controller.isLocalCustomising=false;controller.Player=null;
                if(controller.Canvas)controller.Canvas.SetActive(false);
                if(controller._camera)controller._camera.SetActive(false);
            }
        }catch(Exception error){Plugin.Logger.LogWarning("Outdoor gear exit: "+error.GetBaseException().Message);}
        finally {
            CharacterCustomisationController.isCustomising=false;if(controller)controller.liftUpButton=false;
            try {
                if(current) {
                    if(current!.masterController && current.masterController.rigidBody)current.masterController.rigidBody.isKinematic=priorKinematic;
                    current.SetShadowCharacter();
                    if(current.isOwned)current.CMD_SetRelaxed(false);
                }
            }catch(Exception error){Plugin.Logger.LogWarning("Outdoor gear character recovery: "+error.GetBaseException().Message);}
            finally {
                try {
                    RestoreBackdrop();
                    if(current && current!.Camera)current.Camera.SetActive(true);
                    if(MainCameraSingleton.instance)MainCameraSingleton.instance.ResetFPSCameraSettings();
                }finally {
                    Cursor.lockState=priorLock;Cursor.visible=priorCursor;
                    if(root)root.SetActive(false);Runtime.SuppressGearCloseClick();
                }
            }
        }
    }
    public void Dispose() {
        if(disposed)return;
        try{Recover(true);}finally {disposed=true;lighting?.Dispose();lighting=null;if(root){root.SetActive(false);Object.Destroy(root);}}
    }
}
