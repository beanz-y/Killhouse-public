using System.Reflection;
using BepInEx;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

// Reporting shares the loaders' existing workers. No scene scans, external
// commands, full game-file hashing or uploads are needed for support.
internal static class NativeAssetDiagnostics
{
    private static NativeAssetReportSession? report;
    private static bool warned;
    private static readonly string[] sources={"globalgamemanagers.assets","level2","resources.assets","resources.assets.resS",
        "sharedassets0.assets","sharedassets0.assets.resS","sharedassets0.resource","sharedassets1.assets","sharedassets1.assets.resS",
        "sharedassets2.assets","sharedassets2.assets.resS","sharedassets27.assets"};

    internal static void Start() {
        try {
            warned=false;
            report=new NativeAssetReportSession(Plugin.Version,Application.unityVersion,Assembly.GetExecutingAssembly().Location,
                Application.dataPath,Path.GetFullPath(Path.Combine(Paths.BepInExRootPath,"../UserData/OperatorKillhouse")),
                sourceFiles:sources,onWriteError:ReportError,gameVersion:Application.version);
            foreach(string group in new[]{"sse","gear","fixtures","breaker"})Record(group,"preparing");
        }catch(Exception error){ReportError(error);}
    }
    private static void ReportError(Exception error) {
        if(warned)return;warned=true;
        Plugin.Logger.LogWarning("Asset diagnostic report could not be saved ("+error.GetType().Name+"). Asset loading continues.");
    }
    internal static void GameAssembly(string hash) {
        try{report?.RecordGameAssembly(hash);}catch(Exception error){ReportError(error);}
    }
    internal static T Build<T>(string group,Func<T> build) {
        try{var result=build();Record(group,"prepared");return result;}
        catch(Exception error){Failed(group,error);throw;}
    }
    internal static void Ready(string group,int objects)=>Record(group,"ready",objects:objects);
    internal static void Failed(string group,Exception error)=>Record(group,"unavailable",NativeAssetMismatch.Find(error),error.GetBaseException().GetType().Name);
    private static void Record(string group,string state,NativeAssetMismatch? mismatch=null,string? reason=null,int? objects=null) {
        try{report?.Record(group,state,mismatch,reason,objects);}catch(Exception error){ReportError(error);}
    }
}
