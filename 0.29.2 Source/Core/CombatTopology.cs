namespace OperatorKillhouse.Core;

public static partial class CombatGenerator {
    // Four-metre tiles make irregular rooms without one-cell sawtooth walls.
    // The passage reserves useful circulation before the room territories grow.
    private static (List<CombatRoom> Rooms,string Name) MakeRooms(Dice dice,int count) {
        int family=dice.Next(4),rotation=dice.Next(4);bool mirror=dice.Next(2)==0;
        var owners=Enumerable.Repeat(-1,25).ToArray();var passage=new HashSet<int>();
        void Hall(int x,int y)=>passage.Add(y*5+x);
        if(family==0) {
            for(int y=0;y<=2;y++)Hall(1,y);
            for(int x=1;x<=3;x++)Hall(x,2);
            for(int y=2;y<5;y++)Hall(3,y);
        } else if(family==1) {
            for(int n=1;n<=3;n++){Hall(n,1);Hall(n,3);Hall(1,n);Hall(3,n);}
        } else if(family==2) {
            int spine=1+dice.Next(3),branch=1+dice.Next(3);
            for(int y=0;y<5;y++)Hall(spine,y);
            for(int x=spine;x<5;x++)Hall(x,branch);
        }
        int next=0;
        if(passage.Count>0){foreach(int i in passage)owners[i]=0;next=1;}
        IEnumerable<int> Neighbours(int i)=>new[]{i%5>0?i-1:-1,i%5<4?i+1:-1,i>=5?i-5:-1,i<20?i+5:-1}.Where(n=>n>=0);
        // Every disconnected side of a corridor/ring gets a room seed.
        var remaining=Enumerable.Range(0,25).Where(i=>owners[i]<0).ToHashSet();
        while(remaining.Count>0) {
            var component=new List<int>();var queue=new Queue<int>();int first=remaining.Min();queue.Enqueue(first);remaining.Remove(first);
            while(queue.Count>0){int i=queue.Dequeue();component.Add(i);foreach(int n in Neighbours(i))if(remaining.Remove(n))queue.Enqueue(n);}
            owners[component[dice.Next(component.Count)]]=next++;
        }
        while(next<count) {
            var empty=Enumerable.Range(0,25).Where(i=>owners[i]<0).ToArray();if(empty.Length==0)break;
            var seeds=Enumerable.Range(0,25).Where(i=>owners[i]>=0 && !passage.Contains(i)).ToArray();
            int Distance(int i)=>seeds.Min(s=>Math.Abs(i%5-s%5)+Math.Abs(i/5-s/5));
            int far=empty.Max(Distance);var choices=empty.Where(i=>Distance(i)>=Math.Max(1,far-1)).ToArray();
            owners[choices[dice.Next(choices.Length)]]=next++;
        }
        while(owners.Any(o=>o<0)) {
            var moves=Enumerable.Range(0,25).Where(i=>owners[i]<0).SelectMany(i=>Neighbours(i).Where(n=>owners[n]>=0 && !passage.Contains(n)).Select(n=>(Cell:i,Owner:owners[n]))).Distinct().ToList();
            if(moves.Count==0)throw new InvalidOperationException("Unseeded combat region.");
            dice.Shuffle(moves);
            // Balanced growth keeps one start from consuming the whole floor;
            // prefer shared edges so rooms remain readable rather than spindly.
            var chosen=moves.OrderBy(m=>owners.Count(o=>o==m.Owner)*3-Neighbours(m.Cell).Count(n=>owners[n]==m.Owner)*2).First();
            owners[chosen.Cell]=chosen.Owner;
        }
        Cell Transform(int x,int y) {
            if(mirror)x=9-x;
            for(int n=0;n<rotation;n++)(x,y)=(9-y,x);
            return new(y,x);
        }
        var rooms=new List<CombatRoom>();
        for(int owner=0;owner<next;owner++) {
            var cells=Enumerable.Range(0,25).Where(i=>owners[i]==owner).SelectMany(i=>Enumerable.Range(0,2).SelectMany(y=>Enumerable.Range(0,2).Select(x=>Transform(i%5*2+x,i/5*2+y)))).OrderBy(c=>c.Row).ThenBy(c=>c.Column).ToArray();
            int x=cells.Min(c=>c.Column),y=cells.Min(c=>c.Row);
            rooms.Add(new(x,y,cells.Max(c=>c.Column)-x+1,cells.Max(c=>c.Row)-y+1,cells,owner==0 && passage.Count>0));
        }
        return (rooms,new[]{"Bent spine","Loop corridor","Branching hall","Interlocked rooms"}[family]);
    }

    private static (float X,float South,bool Corner) Approach(Layout doc,CombatRoom room,Cell cell,float x,float south,Cell entrance,float ingressX,float ingressSouth,IReadOnlyList<BarrierSegment> architecture) {
        bool Visible(float tx,float tz)=>!architecture.Where(s=>s.Kind==Barrier.Wall).Any(s=>new Footprint(s.X,s.Z,s.Edge.Horizontal?s.Span/2:s.Thickness/2,s.Edge.Horizontal?s.Thickness/2:s.Span/2).Crosses(ConstructionPlan.Left+x,ConstructionPlan.North-south,ConstructionPlan.Left+tx,ConstructionPlan.North-tz,.10f));
        if(Visible(ingressX,ingressSouth))return(ingressX,ingressSouth,false);
        var distances=new Dictionary<Cell,int>{{entrance,0}};var queue=new Queue<Cell>();queue.Enqueue(entrance);
        while(queue.Count>0) {
            var at=queue.Dequeue();foreach(var direction in Enum.GetValues<Bearing>()) {
                var next=Layout.Adjacent(at,direction);
                if(!room.Contains(next) || doc.At(at,direction)!=Barrier.Open || distances.ContainsKey(next))continue;
                distances[next]=distances[at]+1;queue.Enqueue(next);
            }
        }
        var cursor=cell;float aimX=x,aimSouth=south;
        while(distances.GetValueOrDefault(cursor,0)>0) {
            cursor=Enum.GetValues<Bearing>().Where(d=>doc.At(cursor,d)==Barrier.Open).Select(d=>Layout.Adjacent(cursor,d)).First(c=>distances.GetValueOrDefault(c,int.MaxValue)==distances[cursor]-1);
            float tx=cursor.Column*2+1,tz=cursor.Row*2+1;
            if(!Visible(tx,tz))break;
            aimX=tx;aimSouth=tz;
        }
        return(aimX,aimSouth,true);
    }
}
