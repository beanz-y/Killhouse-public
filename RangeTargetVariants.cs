using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal sealed partial class RangeSession
{
    private readonly GameObject?[] standlessRoleTemplates=new GameObject?[3];
    private readonly PropShape?[] standlessRoleShapes=new PropShape?[3];
    private readonly Vector3[] standlessRoleScales=new Vector3[3];
    private void InitializeStandlessRoles() {
        standlessRoleTemplates[0]=standlessTemplate;standlessRoleShapes[0]=standlessShape;standlessRoleScales[0]=standlessScale;
        foreach(var role in new[]{TargetRole.Civilian,TargetRole.Hostage}) {
            var template=targetTemplates[(int)role];
            var shape=NativeProp.Measure(template,false,0,NativeTargetVariants.IsPaper);
            var expected=TargetGeometry.StandlessFor(role);
            if(!shape.Valid || new[]{shape.MinX-expected.MinX,shape.MaxX-expected.MaxX,shape.MinY-expected.MinY,shape.MaxY-expected.MaxY,shape.MinZ-expected.MinZ,shape.MaxZ-expected.MaxZ}.Any(d=>Math.Abs(d)>.002f)) {
                Plugin.Logger.LogWarning($"{role} paper geometry changed; this standless role is unavailable until calibrated.");continue;
            }
            standlessRoleTemplates[(int)role]=template;standlessRoleShapes[(int)role]=shape;standlessRoleScales[(int)role]=template.transform.lossyScale;
        }
    }
    internal bool StandlessAvailableFor(TargetRole role)=>Enum.IsDefined(typeof(TargetRole),role) && standlessRoleTemplates[(int)role] && standlessRoleShapes[(int)role]!=null;
    private IEnumerable<string> StandlessRoleContracts()=>Enum.GetValues<TargetRole>().Select(role=>standlessRoleShapes[(int)role] is {} shape
        ?"standless:"+role+":"+string.Join(",",new[]{shape.MinX,shape.MaxX,shape.MinY,shape.MaxY,shape.MinZ,shape.MaxZ,TargetGeometry.StandlessYaw(role)}.Select(BuildPolicy.Number))
        :"standless:"+role+":unavailable");
}
