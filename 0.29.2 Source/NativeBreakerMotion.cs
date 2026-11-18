using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RootMotion.FinalIK;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

// Reuse the installed night-mission transform clip and native FinalIK hand
// interaction without adding a scene-bound BreakerSwitch/NetworkIdentity.
internal sealed class NativeBreakerMotion : IDisposable
{
    private readonly GameObject model;
    private readonly InteractionObject hand;
    private readonly Animator animator;
    private InteractionSystem? playerSystem;
    private PlayerNetworking? player;
    private float started=-1,handStarted=-1;
    private bool? power;
    private bool bound,faulted,animating,restoring;
    private int bindAttempts;
    private float bindAt;
    private float heldPose=float.NaN;
    private Action? flip,cancel;
    private uint actor;
    internal bool Interacting=>started>=0;
    internal bool NeedsInactiveReset=>bound || faulted || started>=0 || playerSystem is not null;
    internal BreakerAnimation? Pending(int id)=>flip!=null && started>=0?new(id,actor,Math.Max(0,Time.unscaledTime-started),Restore:restoring):null;
    internal NativeBreakerMotion(GameObject node) {
        model=node.transform.Find("PowerBox_V2").gameObject;
        animator=model.AddComponent<Animator>();
        animator.runtimeAnimatorController=NativeBreakerAssets.Controller;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.updateMode=AnimatorUpdateMode.UnscaledTime;
        animator.applyRootMotion=false;animator.fireEvents=false;
        var interaction=model.transform.Find("Animated/InteractionL");
        if(!interaction)throw new InvalidOperationException("Native breaker hand target is missing.");
        var target=interaction!.Find("hand_l").gameObject.AddComponent<InteractionTarget>();
        target.effectorType=FullBodyBipedEffector.LeftHand;target.multipliers=new Il2CppReferenceArray<InteractionTarget.Multiplier>(0);
        target.interactionSpeedMlp=1;target.twistAxis=Vector3.up;target.twistWeight=1;target.swingWeight=1;target.threeDOFWeight=1;target.rotateOnce=true;
        hand=interaction.gameObject.AddComponent<InteractionObject>();
        // Reviewed native InteractionL curve, including its pause at contact.
        var keys=new[]{new Keyframe(0,.026881754f,1.9333475f,1.9333475f),new Keyframe(.50333333f,1,-.012975514f,-.012975514f),new Keyframe(1,.026881754f,-1.9592985f,-1.9592985f)};
        hand.weightCurves=new Il2CppReferenceArray<InteractionObject.WeightCurve>(1);
        hand.weightCurves[0]=new InteractionObject.WeightCurve{type=(InteractionObject.WeightCurve.Type)0,curve=new AnimationCurve(keys)};
        hand.multipliers=new Il2CppReferenceArray<InteractionObject.Multiplier>(2);
        hand.multipliers[0]=new InteractionObject.Multiplier{curve=(InteractionObject.WeightCurve.Type)0,multiplier=1,result=(InteractionObject.WeightCurve.Type)7};
        hand.multipliers[1]=new InteractionObject.Multiplier{curve=(InteractionObject.WeightCurve.Type)0,multiplier=1,result=(InteractionObject.WeightCurve.Type)10};
        hand.events=new Il2CppReferenceArray<InteractionObject.InteractionEvent>(1);
        hand.events[0]=new InteractionObject.InteractionEvent{time=.5f,pause=true,pickUp=false,animations=new Il2CppReferenceArray<InteractionObject.AnimatorEvent>(0),messages=new Il2CppReferenceArray<InteractionObject.Message>(0),unityEvent=new UnityEngine.Events.UnityEvent()};
    }
    internal bool Use(PlayerNetworking? player,Action? flip=null,Action? cancel=null,float elapsed=0,bool restore=false) {
        if(power!=!restore || !float.IsFinite(elapsed) || elapsed<0 || elapsed>=BreakerTiming.Contact(restore) || !TryBind())return false;
        if(animating && (started>=0 || power!=restoring))return restoring==restore; // Duplicate cues cannot restart a gesture or replace its commit.
        FinishHand();started=Time.unscaledTime-elapsed;animating=true;restoring=restore;
        if(player && player!.interactionSystem) {
            this.player=player;playerSystem=player.interactionSystem;handStarted=started;
            if(!playerSystem!.StartInteraction(FullBodyBipedEffector.LeftHand,hand,true)){playerSystem=null;this.player=null;handStarted=-1;}
        }
        SafePose(BreakerTiming.Normalized(elapsed,restore),!restore);
        if(faulted)return false;
        this.flip=flip;this.cancel=cancel;actor=player?player!.netId:0;
        return true;
    }
    internal void SetPower(bool on) {
        if(power==on)return;
        bool animate=power==true && !on && bound && model.activeInHierarchy;power=on;
        // The host snapshot arrives at contact, while this clip is already playing.
        if(animating && on==restoring){if(started<0)SafePose(restoring?0:1,false);return;}
        if(started>=0 || playerSystem){started=-1;CancelFlip();FinishHand();}
        if(!TryBind()){started=-1;return;}
        started=animate?Time.unscaledTime:-1;animating=animate;restoring=false;
        SafePose(on?0:animate?0:1,animate);
    }
    internal void Tick() {
        if(!model.activeInHierarchy){Cancel();return;}
        if(!TryBind())return;
        if(started>=0) {
            float elapsed=Time.unscaledTime-started;
            // Seek the installed non-legacy clip backwards with a paused
            // Animator. This avoids negative-speed recorder requirements and
            // keeps scene AnimationEvents disabled in both directions.
            if(restoring && elapsed>=BreakerTiming.ReachSeconds)SafePose(BreakerTiming.Normalized(elapsed,true),false);
            if(started>=0 && flip!=null && Time.unscaledTime>=started+BreakerTiming.Contact(restoring)) {
                var contact=flip;flip=null;contact();
            }
            if(started>=0 && elapsed>=BreakerTiming.Complete(restoring)){started=-1;cancel=null;SafePose(restoring?0:1,false);}
        }
        if(playerSystem && handStarted>=0 && (Time.unscaledTime-handStarted>=BreakerTiming.Complete(restoring) || !model.activeInHierarchy || !player || !player!.health || player.health.isDead))FinishHand();
    }
    private bool TryBind() {
        if(faulted || !model.activeInHierarchy || Time.unscaledTime<bindAt)return false;
        if(bound)return true;
        long began=PerformanceJournal.Begin(),allocated=PerformanceJournal.Allocated();
        try {
            // Range staging constructs fixtures under an inactive map root.
            // Unity cannot expose the Animator's states until it is enabled.
            heldPose=float.NaN;animator.Rebind();animator.Update(0);
            if(!animator.HasState(0,Animator.StringToHash("Base Layer.TurnOff")) && !animator.HasState(0,Animator.StringToHash("TurnOff"))) {
                if(++bindAttempts<3){bindAt=Time.unscaledTime+.05f;return false;}
                throw new InvalidOperationException($"Native breaker TurnOff state unavailable after activation (controller={animator.runtimeAnimatorController?.name}, layers={animator.layerCount}).");
            }
            hand.Initiate();bound=true;if(power is {} on)Pose(on?0:1,false);
            DiagnosticLog.Info("Native breaker animator bound after map activation: "+animator.runtimeAnimatorController?.name);
            return true;
        }catch(Exception error){AnimationFailed(error);return false;}
        finally{PerformanceJournal.Complete("breaker-bind",began,allocated);}
    }
    private void SafePose(float normalized,bool play) {
        try{Pose(normalized,play);}catch(Exception error){AnimationFailed(error);}
    }
    private void AnimationFailed(Exception error) {
        if(faulted)return;
        faulted=true;bound=false;started=-1;heldPose=float.NaN;
        CancelFlip();
        try{FinishHand();}catch(Exception cleanup){Plugin.Logger.LogWarning("Breaker hand cleanup: "+cleanup);}
        Plugin.Logger.LogWarning("Breaker animation unavailable; map loading and native circuit controls remain usable. "+error);
    }
    private void Pose(float normalized,bool play) {
        // A successful bind already applied this paused pose. Running forward
        // playback and reverse seeking must still reach the native Animator.
        if(!play && started<0 && heldPose==normalized)return;
        heldPose=float.NaN;
        // Native non-legacy clips need their Animator/controller binding.
        // Disable the scene's AnimationEvents: our circuits own power changes.
        animator.speed=1;animator.Play("TurnOff",0,normalized);animator.Update(0);animator.speed=play?1:0;
        if(!play && started<0)heldPose=normalized;
    }
    private void FinishHand() {
        var system=playerSystem;var owner=player;playerSystem=null;player=null;handStarted=-1;
        if(!system)return;
        var current=system!.GetInteractionObject(FullBodyBipedEffector.LeftHand);
        if(current && current!=hand)return; // Another interaction owns the hand now.
        if(current==hand)system.StopInteraction(FullBodyBipedEffector.LeftHand);
        if(!owner || !owner!.isOwned || !owner.health || owner.health.isDead)return;
        // Native BreakerSwitch.FinishAnimation re-grips the held weapon/grenade;
        // resuming a paused interaction alone does not restore a weapon grip.
        if(owner.c_activeWeapon && owner.c_activeWeapon.mods) {
            owner.c_activeWeapon.mods.Grab_Grip();owner.c_activeWeapon.mods.CMD_GrabGrip();
        }else if(owner.c_activeGrenade)owner.c_activeGrenade.GripGrenade();
        else system.ResumeInteraction(FullBodyBipedEffector.LeftHand);
    }
    private void CancelFlip(){var stopped=cancel;flip=null;cancel=null;stopped?.Invoke();}
    internal void Cancel(){
        started=-1;animating=false;restoring=false;bound=false;faulted=false;bindAttempts=0;bindAt=0;heldPose=float.NaN;
        using var cleanup=new CleanupStack();cleanup.Add(FinishHand);cleanup.Add(CancelFlip);
    }
    internal void Reset(){
        // Reset changes the authored pose, not this active hierarchy/controller.
        // Inactive cached maps and failed bindings still take the rebind path.
        bool keepBinding=bound && !faulted && model.activeInHierarchy;
        try{Cancel();}finally{bound=keepBinding;power=null;}
    } // Saved reset state is an immediate pose, never a new gesture.
    public void Dispose()=>Cancel();
}
