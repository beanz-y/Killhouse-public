using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class MapEditor
{
    private TargetStyle targetStyle;
    private float targetHeight,targetPitch,targetRoll;
    private bool targetOnFurniture;
    private bool targetAgainstSide,targetMatchSurface=true;
    private Target? sideInput;
    private TargetSurfaceSnap? sideResult;
    private int sideRevision=-1,sideContext,sideStep;
    private bool sideMatch;
    private TargetSurfaceFace? dragFace,sideAnchor;
    private TargetRef? relocatingTarget;
    private TargetSurfaceSnap? SideSnap(Layout layout,Target target) {
        if(sideRevision!=Draft.Revision || sideContext!=Runtime.EditorPreviewContext || sideInput!=target || sideStep!=rotationStep || sideMatch!=targetMatchSurface || sideAnchor!=dragFace) {
            EnsureFurniture();var props=DisplayFurniture().Select(PropRules.From).ToArray();
            sideResult=dragFace is {} face?TargetContact.Slide(layout,target,face,EditorArchitecture(),propShapes,props,targetMatchSurface,rotationStep):TargetContact.Side(layout,target,EditorArchitecture(),propShapes,props,targetMatchSurface,rotationStep);
            if(drawing && dragFace==null)dragFace=sideResult?.Face;
            sideRevision=Draft.Revision;sideContext=Runtime.EditorPreviewContext;sideInput=target;sideStep=rotationStep;sideMatch=targetMatchSurface;sideAnchor=dragFace;
        }
        return sideResult;
    }
    private void BeginTargetDrag() {
        dragFace=null;sideRevision=-1;
        if(relocatingTarget==null && targetAgainstSide && selectedTarget is {} key && Draft.Document.Find(key) is {Style:TargetStyle.Standless} old) {
            EnsureFurniture();dragFace=TargetContact.Side(Draft.Document,old,EditorArchitecture(),propShapes,DisplayFurniture().Select(PropRules.From),false,rotationStep)?.Face;
        }
    }
    private void EndTargetDrag(){dragFace=null;sideRevision=-1;}
    private void SelectTargetSurface() {
        targetOnFurniture=false;targetAgainstSide=false;targetMatchSurface=false;
        if(selectedTarget is not {} key || Draft.Document.Find(key) is not {Style:TargetStyle.Standless} target)return;
        EnsureFurniture();var top=TargetGeometry.OnFurniture(Draft.Document,target,propShapes,DisplayFurniture().Select(PropRules.From));
        targetOnFurniture=top.Height>=0 && Math.Abs(top.Height-target.Height)<.025f;
        targetAgainstSide=!targetOnFurniture;
    }
    private void RelocateTarget(TargetRef key) {
        ClearSelection();selectedTarget=key;relocatingTarget=key;targetAgainstSide=true;targetOnFurniture=false;tool=Tool.Select;
        message="Choose a green position on another face. Click to reattach the selected target.";
    }
    private string heightText="0";
    private TargetRef? poseSelection;
    private int poseRevision=-1;
    private int targetPickCycle;
    private Vector2 targetPickPoint=new(-100,-100);
    private TargetRef? PickTarget(Vector2 grid,bool cycle) {
        if(!cycle || (grid-targetPickPoint).magnitude>.15f)targetPickCycle=0;else targetPickCycle++;
        targetPickPoint=grid;return TargetPlacement.Hit(Draft.Document,grid.x,grid.y,cycle:targetPickCycle);
    }
    private static string TargetMarker(Target t)=>t.Style==TargetStyle.Standless?(t.Role==TargetRole.Threat?"S":"S"+"TNH"[(int)t.Role]):"TNH"[(int)t.Role].ToString();
    private static string PoseLabel(Target t)=>$"Standless · bottom {t.Height:0.##} m · lean {t.Pitch:0.##}° · sideways {t.Roll:0.##}°";
    private Target PrepareEditorTarget(Layout layout,Target target) {
        if(target.Style==TargetStyle.Standless && targetAgainstSide) {
            return SideSnap(layout,target)?.Target??target with{Height=dragFace==null?-2:-3};
        }
        if(!targetFree && alignWalls)target=Runtime.AlignEditorTarget(layout,target);
        if(target.Style!=TargetStyle.Standless || !targetOnFurniture)return target;
        EnsureFurniture();return TargetGeometry.OnFurniture(layout,target,propShapes,DisplayFurniture().Select(PropRules.From));
    }
    private void SnapSelectedTarget(TargetRef key,bool side) {
        var target=Draft.Document.Find(key);if(target==null)return;
        EnsureFurniture();var next=side?SideSnap(Draft.Document,target)?.Target:TargetGeometry.OnFurniture(Draft.Document,target,propShapes,DisplayFurniture().Select(PropRules.From));
        if(next==null){message="No nearby wall or prop side at this height. Move closer, then snap again.";return;}
        selectedTarget=Draft.RepositionTarget(key,next);TargetFeedback(side?"Target snapped against the surface.":"Target snapped onto the furniture surface.");
    }
    private void ChangeTargetPose(TargetRef? at,TargetStyle style,float height,float pitch,float roll) {
        bool becameStandless=style==TargetStyle.Standless && (at is {} prior?Draft.Document.Find(prior)?.Style:targetStyle)!=TargetStyle.Standless;
        if(style==TargetStyle.Upright){height=0;pitch=0;roll=0;targetOnFurniture=false;targetAgainstSide=false;}
        if(at is {} key) {
            EditSelectedPose(key,t=>t with{Style=style,Height=height,Pitch=pitch,Roll=roll});
            TargetFeedback("Target style/height/tilt updated.");if(Draft.LastRejection!=null)return;
        }
        if(becameStandless){targetAgainstSide=true;targetOnFurniture=false;targetMatchSurface=true;}
        targetStyle=style;targetHeight=height;targetPitch=pitch;targetRoll=roll;
        heightText=TargetEditSteps.HeightText(height);
    }
    private void EditSelectedPose(TargetRef key,Func<Target,Target> edit) {
        var old=Draft.Document.Find(key);if(old==null)return;var next=edit(old);
        if(old.Style==TargetStyle.Standless && next.Style==TargetStyle.Standless && targetAgainstSide) {
            EnsureFurniture();var props=DisplayFurniture().Select(PropRules.From).ToArray();
            var face=TargetContact.Side(Draft.Document,old,EditorArchitecture(),propShapes,props,false,rotationStep)?.Face;
            next=face==null?next with{Height=-2}:TargetContact.Slide(Draft.Document,next,face,EditorArchitecture(),propShapes,props,false,rotationStep)?.Target??next with{Height=-3};
            selectedTarget=Draft.RepositionTarget(key,next);
        }else Draft.EditTarget(key,edit);
    }
    private void ChangeTargetHeight(TargetRef? at,TargetStyle style,float height,float pitch,float roll){targetOnFurniture=false;ChangeTargetPose(at,style,height,pitch,roll);}
    private void TargetPosePicker(TargetRef? at) {
        var selected=at is {} key?Draft.Document.Find(key):null;
        var style=selected?.Style??targetStyle;float height=selected?.Height??targetHeight,pitch=selected?.Pitch??targetPitch,roll=selected?.Roll??targetRoll;
        if(poseSelection!=at || poseRevision!=Draft.Revision){heightText=TargetEditSteps.HeightText(height);poseSelection=at;poseRevision=Draft.Revision;}
        KillhouseUi.BeginSection(selected==null?"Target type":"Selected target");
        try {
            int choice=Choices((int)style,new[]{"On stand","Standless"});
            if(choice!=(int)style){ChangeTargetPose(at,(TargetStyle)choice,height,pitch,roll);return;}
            RolePicker(at);
            if(style==TargetStyle.Standless && !Runtime.StandlessAvailableFor(selected?.Role??role))KillhouseUi.Note("This standless target type is not ready in this house.");
        }finally{KillhouseUi.EndSection();}
        KillhouseUi.BeginSection("Placement");
        try {
            GUILayout.Label("Position");targetFree=Choices(targetFree?0:1,new[]{"Free place","Snap to grid"})==0;
            if(!targetFree)snap=TargetEditSteps.PositionStep(Choices(snap==1?1:0,new[]{"0.5 m","1 m"})==1?1:.5f);
            RotationStepPicker();
            if(style==TargetStyle.Standless) {
                GUILayout.Label("Surface");
                int surface=Choices(targetAgainstSide?0:targetOnFurniture?1:2,new[]{"Wall / prop face","Prop top / seat","No surface"},1);
                targetOnFurniture=surface==1;targetAgainstSide=surface==0;
                if(targetAgainstSide) {
                    targetMatchSurface=GUILayout.Toggle(targetMatchSurface,"Match facing to side");
                    if(at is {} targetKey) {
                        if(GUILayout.Button("Snap to nearest face"))SnapSelectedTarget(targetKey,true);
                        if(GUILayout.Button("Move to another face…"))RelocateTarget(targetKey);
                    }
                    KillhouseUi.Note("Drag to slide along this face. Free place keeps the exact along-face position.");
                }else if(targetOnFurniture) {
                    if(at is {} targetKey && GUILayout.Button("Snap selected onto prop top / seat"))SnapSelectedTarget(targetKey,false);
                    if(at!=null)KillhouseUi.Note($"Bottom: {height:0.##} m");
                    KillhouseUi.Note("Use individual props. The target stays in place when its support moves.");
                }
            }
            if(!targetFree && (style!=TargetStyle.Standless || !targetAgainstSide))alignWalls=GUILayout.Toggle(alignWalls,"Align near wall faces");
        }finally{KillhouseUi.EndSection();}
        if(style!=TargetStyle.Standless)return;
        if(!targetOnFurniture) {
            KillhouseUi.BeginSection("Height above floor");
            try {
                GUILayout.BeginHorizontal();try {
                    if(GUILayout.Button("− 5 cm"))ChangeTargetHeight(at,style,TargetEditSteps.SnapHeight(height-TargetEditSteps.HeightStep),pitch,roll);
                    GUI.SetNextControlName("kh-target-height");heightText=GUILayout.TextField(heightText,12);
                    if(GUILayout.Button("+ 5 cm"))ChangeTargetHeight(at,style,TargetEditSteps.SnapHeight(height+TargetEditSteps.HeightStep),pitch,roll);
                }finally{GUILayout.EndHorizontal();}
                if(GUILayout.Button("Apply height (m)")) {
                    if(Number(heightText,out float value) && value>=0 && value<=TargetGeometry.MaximumHeight)ChangeTargetHeight(at,style,targetFree?value:TargetEditSteps.SnapHeight(value),pitch,roll);
                    else message=$"Height must be between 0 and {TargetGeometry.MaximumHeight:0.0} metres.";
                }
            }finally{KillhouseUi.EndSection();}
        }
        KillhouseUi.BeginSection("Tilt");
        try {
            GUILayout.Label($"Lean: {pitch:0.##}°");
            GUILayout.BeginHorizontal();try {
                if(GUILayout.Button($"− {rotationStep}°"))ChangeTargetPose(at,style,height,TiltStep(pitch,-1,90),roll);
                if(GUILayout.Button($"+ {rotationStep}°"))ChangeTargetPose(at,style,height,TiltStep(pitch,1,90),roll);
            }finally{GUILayout.EndHorizontal();}
            GUILayout.Label($"Sideways: {roll:0.##}°");
            GUILayout.BeginHorizontal();try {
                if(GUILayout.Button($"− {rotationStep}°"))ChangeTargetPose(at,style,height,pitch,TiltStep(roll,-1,180));
                if(GUILayout.Button($"+ {rotationStep}°"))ChangeTargetPose(at,style,height,pitch,TiltStep(roll,1,180));
            }finally{GUILayout.EndHorizontal();}
            GUILayout.BeginHorizontal();try {
                if(GUILayout.Button("Sideways left"))ChangeTargetPose(at,style,height,0,90);
                if(GUILayout.Button("Sideways right"))ChangeTargetPose(at,style,height,0,-90);
            }finally{GUILayout.EndHorizontal();}
            if((pitch!=0 || roll!=0) && GUILayout.Button("Reset tilt"))ChangeTargetPose(at,style,height,0,0);
        }finally{KillhouseUi.EndSection();}
    }
    private float TiltStep(float value,int direction,float maximum)=>TargetEditSteps.Tilt(value,rotationStep,direction<0,maximum);
}
