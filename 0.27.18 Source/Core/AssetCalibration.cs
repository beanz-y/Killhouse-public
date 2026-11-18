namespace OperatorKillhouse.Core;

// The mesh pivot is not the middle or the floor of an authored wall. These
// measurements describe its two wood faces in the asset root's local space.
public readonly record struct AssetCalibration(float MinX,float MaxX,float MinY,float MaxY,float MinZ,float MaxZ)
{
    public bool AlongX => MaxX-MinX>MaxZ-MinZ;
    public float Span => AlongX?MaxX-MinX:MaxZ-MinZ;
    public float Thickness => AlongX?MaxZ-MinZ:MaxX-MinX;
    public (float X,float Y,float Z) Foot => ((MinX+MaxX)*.5f,MinY,(MinZ+MaxZ)*.5f);
    public float Yaw(bool horizontal) => (AlongX?0:90)+(horizontal?0:90);
    public (float X,float Y,float Z) Scale(BarrierSegment segment) {
        float span=segment.Kind==Barrier.Wall?segment.Span/Span:1,vertical=segment.WindowPart?segment.Height/(MaxY-MinY):1;
        return AlongX?(span,vertical,1):(1,vertical,span);
    }
    public void Validate() {
        var values=new[]{MinX,MaxX,MinY,MaxY,MinZ,MaxZ};
        if(values.Any(v=>!float.IsFinite(v)) || Math.Abs(Span-ConstructionPlan.Pitch)>.025f || Thickness<.08f || Thickness>1.2f || MaxY-MinY<2.5f || MaxY-MinY>3.5f)
            throw new InvalidOperationException("Native range geometry is not a supported two-metre assembly.");
    }
}
