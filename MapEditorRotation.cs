using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class MapEditor
{
    // A wheel/R change during a drag belongs to that gesture's preview and
    // release. Do not mutate the old item while the destination is still red.
    private float? dragYaw;
    private void HandleWheel() {
        var e=Event.current;
        if(e.type!=EventType.ScrollWheel || !EditorWheel.CanRotate(readOnly,MapPointer(e.mousePosition),e.delta.y))return;
        ClearFocus();RotateAtPointer(e.delta.y);e.Use();
    }
    private void RotateAtPointer(float? wheelDelta,bool reverse=false) {
        var doc=Draft.Document;var g=Grid(Event.current.mousePosition);var origin=drawing?start:g;
        bool prop=tool==Tool.Props || tool!=Tool.Target && selectedProp.HasValue;
        Target? target=null;float angle;
        if(prop) {
            if(tool==Tool.Props)angle=dragYaw??TargetEditSteps.Aim(origin.x,origin.y,g.x,g.y,rotationStep,propYaw)??propYaw;
            else if(selectedProp is {} index && index>=0 && index<doc.Props.Count)angle=dragYaw??doc.Props[index].Yaw;
            else return;
        }else {
            if(tool==Tool.Target)target=Draft.PrepareFine(origin.x,origin.y,role,dragYaw??TargetEditSteps.Aim(origin.x,origin.y,g.x,g.y,rotationStep,yaw),always,TargetPositionStep(),targetStyle,targetHeight,targetPitch,targetRoll);
            else if(selectedTarget is {} key && doc.Find(key) is {} selected) {
                var at=TargetPlacement.Grid(selected);
                target=drawing?Draft.PrepareMove(key,at.X+g.x-start.x,at.Y+g.y-start.y,TargetPositionStep(),dragYaw):selected;
            }
            if(target==null)return;
            angle=TargetPlacement.Yaw(doc,target);
        }
        float next=wheelDelta is {} delta?EditorWheel.Turn(angle,delta):TargetEditSteps.Turn(angle,rotationStep,reverse);
        if(drawing) {
            dragYaw=next;
            if(prop){if(tool==Tool.Props)propYaw=next;}
            else{targetMatchSurface=false;if(tool==Tool.Target)yaw=next;}
            return;
        }
        if(prop)RotateProp(reverse,next);
        else SetYaw(tool==Tool.Target?null:selectedTarget,next,false);
    }
}
