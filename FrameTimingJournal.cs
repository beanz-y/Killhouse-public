using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace OperatorKillhouse;

// Sparse point samples, not a GPU profiler or a history of every frame. Disabled
// player builds and unsupported GPU timers are reported as unavailable, not zero.
internal static class FrameTimingJournal
{
    internal sealed record Snapshot(bool FeatureAvailable,int CpuSamples,int GpuSamples,double? CpuMeanMs,double? MainThreadMeanMs,double? RenderThreadMeanMs,double? PresentWaitMeanMs,double? GpuMeanMs);
    private static Il2CppStructArray<FrameTiming>? buffer;
    private static float next;
    private static bool available,failed;
    private static bool collecting;
    private static ulong last;
    private static int cpuCount,gpuCount;
    private static double cpu,main,render,present,gpu;
    private static bool Collecting() {
        if(!DiagnosticLog.Enabled){if(collecting)Reset();return false;}
        if(!collecting){Reset();collecting=true;}
        return true;
    }
    internal static void Tick() {
        if(!Collecting())return;
        if(failed || Time.unscaledTime<next)return;next=Time.unscaledTime+1;
        try {
            available=FrameTimingManager.IsFeatureEnabled();if(!available)return;
            buffer??=new(1);FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,buffer)==0)return;
            var timing=buffer[0];if(timing.frameStartTimestamp==0 || timing.frameStartTimestamp==last)return;last=timing.frameStartTimestamp;
            bool Valid(double value)=>double.IsFinite(value) && value>=0 && value<60000;
            if(Valid(timing.cpuFrameTime) && timing.cpuFrameTime>0 && Valid(timing.cpuMainThreadFrameTime) && Valid(timing.cpuRenderThreadFrameTime) && Valid(timing.cpuMainThreadPresentWaitTime)) {
                cpuCount++;cpu+=timing.cpuFrameTime;main+=timing.cpuMainThreadFrameTime;render+=timing.cpuRenderThreadFrameTime;present+=timing.cpuMainThreadPresentWaitTime;
            }
            if(Valid(timing.gpuFrameTime) && timing.gpuFrameTime>0){gpuCount++;gpu+=timing.gpuFrameTime;}
        }catch{available=false;failed=true;}
    }
    internal static Snapshot Take() {
        Collecting();
        var result=new Snapshot(available,cpuCount,gpuCount,cpuCount>0?cpu/cpuCount:null,cpuCount>0?main/cpuCount:null,cpuCount>0?render/cpuCount:null,cpuCount>0?present/cpuCount:null,gpuCount>0?gpu/gpuCount:null);
        cpuCount=gpuCount=0;cpu=main=render=present=gpu=0;return result;
    }
    internal static void Reset(){buffer=null;next=0;available=failed=collecting=false;last=0;cpuCount=gpuCount=0;cpu=main=render=present=gpu=0;}
}
