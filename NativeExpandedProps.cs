using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

// Deliberate CQB-sized assets. Each source is copied as geometry/collision only.
internal static class NativeExpandedProps
{
    private static readonly (string Name,string Path)[] sources={
        ("Nine-hole barricade","range 1/9-Hole (1)"),
        ("Steel barrel","range 1/BrABarrelA3"),
        ("Barrel cluster","range 1/SM_cityBarrels_01_10"),
        ("Lockers","armory/Lockers_2"),
        ("Metal waste bin","main building props/Trashcan_metal"),
        ("Plastic waste bin","armory/Trashcan_plastic"),
        ("Portable generator","airfield/airfield props/SM_Generator_Closed3_4"),
        ("Gas cylinder rack","airfield/SM_GasCylinderRack"),
        ("Ceiling fluorescent","main building props/Lamp_fluorescent_B (1)"),
        ("Wall lamp","KillHouse 01/Layouts/Layout 02/Props/Lamp02_1"),
        ("Work light","range 1/LitLampSetA2A2Off/LitLampStandA2")
    };
    internal static void Add(RangeComputer computer,Dictionary<string,NativeProp> models) {
        foreach(var source in sources) {
            var node=Find(computer,source.Path);if(!node){Plugin.Logger.LogWarning("Expanded prop unavailable: "+source.Name);continue;}
            try {
                var model=new NativeProp(node!,source.Name,true);var shape=model.Shape;
                if(shape.MaxY-shape.MinY>2.8f || shape.MaxX-shape.MinX>3 || shape.MaxZ-shape.MinZ>3)throw new InvalidOperationException("Model exceeds indoor size limits.");
                models.Add(source.Name,model);
            }catch(Exception error){Plugin.Logger.LogWarning(source.Name+": "+error.GetBaseException().Message);}
        }
        foreach(var model in NativeFixtureAssets.Templates)models.Add(model.Key,new(model.Value,model.Key,true));
        foreach(var model in NativeSseAssets.Templates)models.Add(model.Key,new(model.Value,model.Key,true));
        if(NativeBreakerAssets.Template)models.Add(PowerCircuits.BreakerAsset,new(NativeBreakerAssets.Template!,PowerCircuits.BreakerAsset,true));
    }
    private static GameObject? Find(RangeComputer computer,string path) {
        var names=path.Split('/');
        foreach(var root in computer.gameObject.scene.GetRootGameObjects()) {
            if(root.name!=names[0])continue;
            Transform? current=root.transform;
            foreach(string name in names.Skip(1)){current=current?.Find(name);if(!current)break;}
            if(current)return current!.gameObject;
        }
        return null;
    }
}
