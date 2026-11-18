namespace OperatorKillhouse.Core;

public readonly record struct RangePanelRegions(EditorRect Header,EditorRect Body);
public static class RangePanelLayout
{
    public static RangePanelRegions Make(float width,float height,bool tabs,bool encounter,bool triggers=false) {
        float top=tabs?68:30,bottom=Math.Max(top+40,height-96),available=bottom-top;
        float header=Math.Min(triggers?250:encounter?140:64,available*.65f);
        return new(new(12,top,width-24,header),new(12,top+header+8,width-24,Math.Max(1,available-header-8)));
    }
}
