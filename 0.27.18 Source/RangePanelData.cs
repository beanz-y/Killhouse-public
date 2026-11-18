namespace OperatorKillhouse;
internal static partial class Runtime
{
    private static ShootHouseLayout? PanelLayout(RangeComputer? computer,int index) {
        if(computer is null || !computer)return null;
        var layouts=computer.shootHouseLayouts;
        return layouts!=null && index>=0 && index<layouts.Length?layouts[index]:null;
    }
    private static int PanelLayoutCount(RangeComputer? computer)=>computer is not null && computer?computer.shootHouseLayouts?.Length??0:0;
    private static string PanelMapName(RangeComputer? computer,int index) {
        var layout=PanelLayout(computer,index);
        return layout==null?"Waiting for the range":string.IsNullOrWhiteSpace(layout.LayoutName)?$"Map {index+1}":layout.LayoutName;
    }
}
