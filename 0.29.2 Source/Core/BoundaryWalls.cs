namespace OperatorKillhouse.Core;

// Enclose's perimeter describes the fixed house. These are separately authored
// partitions on the original lattice, inside that house.
public static class BoundaryWalls
{
    public static bool IsBoundary(Layout doc,Edge e)=>e.Horizontal
        ? (e.Row==0 || e.Row==doc.Rows) && e.Column>=0 && e.Column<doc.Columns
        : (e.Column==0 || e.Column==doc.Columns) && e.Row>=0 && e.Row<doc.Rows;
    public static bool CanEdit(Layout doc,Edge e,Barrier kind)=>doc.InteriorEdge(e) || IsBoundary(doc,e) && kind is Barrier.Wall or Barrier.Open;
    public static bool Visible(Layout doc,Edge e)=>doc.InteriorEdge(e) || doc.BoundaryWalls.Contains(e);
    public static IEnumerable<KeyValuePair<Edge,Barrier>> Construction(Layout doc)=>doc.Interior.Concat(doc.BoundaryWalls.Select(e=>new KeyValuePair<Edge,Barrier>(e,Barrier.Wall)));
    public static IEnumerable<Issue> Check(Layout doc) {
        foreach(var e in doc.BoundaryWalls) {
            if(!IsBoundary(doc,e))yield return new("An authored boundary wall must lie on the original grid perimeter.",true);
            if(!e.Horizontal && e.Row==1 && (e.Column==0 || e.Column==doc.Columns))yield return new("An authored boundary wall blocks the fixed entry or exit.",true);
        }
    }
    public static bool BlocksEnd(Layout doc,BarrierSegment s,bool high) {
        var e=s.Edge;
        int row=e.Row+(e.Horizontal?0:high?0:1),col=e.Column+(e.Horizontal && high?1:0);
        return e.Horizontal
            ? doc.BoundaryWalls.Contains(new(row-1,col,false)) || doc.BoundaryWalls.Contains(new(row,col,false))
            : doc.BoundaryWalls.Contains(new(row,col-1,true)) || doc.BoundaryWalls.Contains(new(row,col,true));
    }
}
