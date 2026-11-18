namespace OperatorKillhouse.Core;

// Low/high refer to increasing house-local X (horizontal) or Z (vertical).
public readonly record struct WallEnd(Edge Edge,bool High);
public sealed record JointPlan(BuildPlan Build,int AdjustedWalls,int Fillers,int ShellContacts);

public static class WallJunctions
{
    public const float Seam=.005f, MaxShellOffset=1.25f;
    public static bool Perimeter(Layout layout,WallEnd end)=>end.Edge.Horizontal?
        (end.High?end.Edge.Column+1==layout.Columns:end.Edge.Column==0):
        (end.High?end.Edge.Row==0:end.Edge.Row+1==layout.Rows);
    public static JointPlan Make(Layout layout,float wallThickness,float frameThickness,IReadOnlyDictionary<WallEnd,float>? shell=null)
    {
        if(!float.IsFinite(wallThickness) || !float.IsFinite(frameThickness) || wallThickness<.08f || frameThickness<.08f || wallThickness>1.2f || frameThickness>1.2f)
            throw new ArgumentOutOfRangeException(nameof(wallThickness),"Unsupported native wall/frame thickness.");
        var basis=ConstructionPlan.Make(layout);
        var edges=basis.Segments.ToDictionary(s=>s.Edge);
        var result=new List<BarrierSegment>();int adjusted=0,fillers=0,contacts=0;
        foreach(var segment in basis.Segments) {
            float center=segment.Edge.Horizontal?segment.X:segment.Z;
            float low=center-ConstructionPlan.Pitch/2,high=center+ConstructionPlan.Pitch/2;
            float lowFit=FitEnd(segment,false,low),highFit=FitEnd(segment,true,high);
            float thickness=Thickness(segment);
            if(segment.Kind is Barrier.Wall or Barrier.Window) {
                float span=highFit-lowFit;
                if(!float.IsFinite(span) || span<.25f || span>ConstructionPlan.Pitch+2*MaxShellOffset+.02f)
                    throw new InvalidOperationException("Wall junction produced an unsupported solid panel span.");
                if(Math.Abs(span-ConstructionPlan.Pitch)>.0001f || Math.Abs((lowFit+highFit)/2-center)>.0001f)adjusted++;
                var fitted=segment with {X=segment.Edge.Horizontal?(lowFit+highFit)/2:segment.X,Z=segment.Edge.Horizontal?segment.Z:(lowFit+highFit)/2,Span=span,Thickness=thickness};
                if(segment.Kind==Barrier.Window)result.AddRange(WindowGeometry.Parts(fitted,WindowGeometry.Settings(layout,segment.Edge),center));else result.Add(fitted);
            } else {
                // Keep the opening on its original grid centre, preserve native
                // frame scale, and fill only outside the corrected end posts.
                result.Add(segment with {Thickness=thickness});
                var body=DoorwayAssembly.Ends(segment);
                AddFill(lowFit,body.Low+Seam);AddFill(body.High-Seam,highFit);
            }
            void AddFill(float from,float to) {
                if(to-from<=Seam*2)return;
                float midpoint=(from+to)/2;
                result.Add(new(segment.Edge,Barrier.Wall,segment.Edge.Horizontal?midpoint:segment.X,segment.Edge.Horizontal?segment.Z:midpoint,to-from,wallThickness,true));fillers++;
            }
        }
        return new(new(result,basis.Doors),adjusted,fillers,contacts);

        float Thickness(BarrierSegment s)=>s.Kind is Barrier.Wall or Barrier.Window?wallThickness:frameThickness;
        float FitEnd(BarrierSegment segment,bool high,float nominal) {
            if(shell!=null && shell.TryGetValue(new(segment.Edge,high),out float face)) {
                if(!Perimeter(layout,new(segment.Edge,high)) || !float.IsFinite(face) || Math.Abs(face-nominal)>MaxShellOffset)throw new InvalidOperationException("Shell contact is outside the supported grid margin.");
                contacts++;
                return face+(high?Seam:-Seam);
            }
            var edge=segment.Edge;
            int row=edge.Row+(edge.Horizontal?0:high?0:1),column=edge.Column+(edge.Horizontal && high?1:0);
            var incident=new[]{new Edge(row,column-1,true),new Edge(row,column,true),new Edge(row-1,column,false),new Edge(row,column,false)}
                .Where(edges.ContainsKey).Select(e=>edges[e]).ToArray();
            var perpendicular=incident.Where(s=>s.Edge.Horizontal!=edge.Horizontal).ToArray();
            if(perpendicular.Length==0)return DoorwayAssembly.Join(segment,high,nominal,incident,Seam);
            int horizontal=incident.Count(s=>s.Edge.Horizontal),vertical=incident.Length-horizontal;
            // A through run owns a T. For corners/crosses use a stable axis, with
            // an unbroken native frame preferred over an adjustable solid panel.
            bool ownerHorizontal=horizontal==2 && vertical!=2 ? true : vertical==2 && horizontal!=2 ? false :
                incident.Any(s=>s.Edge.Horizontal && s.Kind is Barrier.Door or Barrier.Frame) && !incident.Any(s=>!s.Edge.Horizontal && s.Kind is Barrier.Door or Barrier.Frame) ? true :
                incident.Any(s=>!s.Edge.Horizontal && s.Kind is Barrier.Door or Barrier.Frame) && !incident.Any(s=>s.Edge.Horizontal && s.Kind is Barrier.Door or Barrier.Frame) ? false : true;
            bool owns=ownerHorizontal==edge.Horizontal;
            float half=perpendicular.Max(Thickness)/2;
            float outward=owns ? (incident.Count(s=>s.Edge.Horizontal==edge.Horizontal)==1?half:0) : segment.Kind is Barrier.Wall or Barrier.Window?-half+Seam:0;
            return DoorwayAssembly.Join(segment,high,nominal+(high?outward:-outward),incident,Seam);
        }
    }
}
