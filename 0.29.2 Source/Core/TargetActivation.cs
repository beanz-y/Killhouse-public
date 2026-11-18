namespace OperatorKillhouse.Core;

// Native target arrays are grouped by role, each in row/column order. Only the
// host rolls random presence; clients validate and apply those exact masks.
public static class TargetActivation
{
    public static Target[] Ordered(Layout doc,TargetRole role)=>doc.AllTargets.Where(t=>t.Role==role).OrderBy(t=>t.Cell.Row).ThenBy(t=>t.Cell.Column).ThenBy(t=>t.Id).ToArray();
    public static int[][] Make(Layout doc,float density,Random random)=>Enforce(doc,Enum.GetValues<TargetRole>().Select(role=>SelectionPolicy.Activation(Ordered(doc,role).Length,density,random)).ToArray());
    public static int[][] Enforce(Layout doc,int[][] masks) {
        if(!ValidShape(doc,masks))throw new ArgumentException("Target masks do not match the authored map.");
        var result=masks.Select(m=>(int[])m.Clone()).ToArray();
        foreach(var role in Enum.GetValues<TargetRole>()) {
            var targets=Ordered(doc,role);for(int n=0;n<targets.Length;n++)if(doc.Encounter==EncounterMode.AIOnly)result[(int)role][n]=0;else if(targets[n].Always)result[(int)role][n]=1;
        }
        return result;
    }
    public static bool Matches(Layout doc,int[][] masks) {
        if(!ValidShape(doc,masks))return false;
        foreach(var role in Enum.GetValues<TargetRole>()) {
            var targets=Ordered(doc,role);for(int n=0;n<targets.Length;n++)if(doc.Encounter==EncounterMode.AIOnly?masks[(int)role][n]!=0:targets[n].Always && masks[(int)role][n]!=1)return false;
        }
        return true;
    }
    public static bool LiveState(Layout doc,int[][] masks)=>ValidShape(doc,masks) && (doc.Encounter!=EncounterMode.AIOnly || masks.All(m=>m.All(v=>v==0)));
    private static bool ValidShape(Layout doc,int[][] masks)=>masks is {Length:3} && Enum.GetValues<TargetRole>().All(role=>masks[(int)role]!=null && masks[(int)role].Length==Ordered(doc,role).Length && masks[(int)role].All(v=>v is 0 or 1));
}
