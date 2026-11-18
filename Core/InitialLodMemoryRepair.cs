namespace OperatorKillhouse.Core;

// This layout belongs only to the SHA-pinned Windows Unity 6000.3.8f1 player.
// The adapter calls us synchronously before the incoming scene's Awake queue.
// Renderer entries are trivial 32-bit instance IDs, not owning pointers. Shrink
// their existing vector in place; never allocate native storage or invoke an
// engine setter (SetLODs also recalculates bounds before transforms are ready).
public interface IInitialLodMemory
{
    void Require(long address,int bytes,bool write=false);
    int ReadInt32(long address);
    long ReadInt64(long address);
    void WriteInt32(long address,int value);
    void WriteInt64(long address,long value);
}

public static class InitialLodMemoryRepair
{
    public const int ExpectedGroups=13052,ExpectedParents=347,ExpectedRemoved=10757;
    private sealed record Level(long Vector,long Data,int[] Ids);
    private sealed record Group(long Address,int Id,Level[] Levels);
    private sealed record Edit(Group Parent,Level Level,int[] Keep);
    private static InvalidOperationException Mismatch(string reason)=>new("Initial training LOD layout differs: "+reason+". No repair can be assumed safe.");

    private static (long Data,int Count) Vector(IInitialLodMemory memory,long address,int stride,int maximum)
    {
        memory.Require(address,32);
        long data=memory.ReadInt64(address),count=memory.ReadInt64(address+16);
        ulong capacity=(ulong)memory.ReadInt64(address+24)>>1;
        if(count<0 || count>maximum || capacity<(ulong)count || capacity>1000000)
            throw Mismatch("vector size/capacity");
        if(count>0) {
            if(data==0 || (data&3)!=0)throw Mismatch("vector alignment");
            memory.Require(data,checked((int)count*stride));
        }
        return(data,(int)count);
    }

    public static int Repair(IInitialLodMemory memory,long queue,long lodRtti,long lodVtable)
    {
        if(queue==0 || (queue&7)!=0 || lodRtti==0 || lodVtable==0)throw Mismatch("queue identity");
        memory.Require(queue,25*32);
        var storage=new List<(long Start,long End)> { (queue,checked(queue+25*32)) };
        void Storage(long start,int size){if(size>0)storage.Add((start,checked(start+size)));}
        var groups=new List<Group>(ExpectedGroups);
        var addresses=new HashSet<long>();var ids=new HashSet<int>();
        int totalObjects=0;
        for(int bucket=0;bucket<25;bucket++) {
            var entries=Vector(memory,checked(queue+bucket*32),24,500000);
            Storage(entries.Data,checked(entries.Count*24));
            totalObjects=checked(totalObjects+entries.Count);
            if(totalObjects>500000)throw Mismatch("awake object count");
            for(int index=0;index<entries.Count;index++) {
                long entry=checked(entries.Data+index*24);
                if(memory.ReadInt64(entry+16)!=lodRtti)continue;
                long pointer=memory.ReadInt64(entry);
                if(pointer==0 || (pointer&7)!=0 || !addresses.Add(pointer))throw Mismatch("LOD object identity");
                memory.Require(pointer,104);
                Storage(pointer,104);
                int id=memory.ReadInt32(pointer+8);
                if(id==0 || id!=memory.ReadInt32(entry+8) || !ids.Add(id) || memory.ReadInt64(pointer)!=lodVtable)
                    throw Mismatch("LOD object type/instance identity");
                if(memory.ReadInt32(pointer+96)!=0)throw Mismatch("LOD group has already registered");
                var nativeLevels=Vector(memory,pointer+64,40,8);
                Storage(nativeLevels.Data,checked(nativeLevels.Count*40));
                var levels=new Level[nativeLevels.Count];
                for(int level=0;level<levels.Length;level++) {
                    long vector=checked(nativeLevels.Data+level*40+8);
                    var renderers=Vector(memory,vector,4,4096);
                    Storage(renderers.Data,checked(renderers.Count*4));
                    var rendererIds=new int[renderers.Count];
                    for(int r=0;r<rendererIds.Length;r++)rendererIds[r]=memory.ReadInt32(checked(renderers.Data+r*4));
                    levels[level]=new(vector,renderers.Data,rendererIds);
                }
                groups.Add(new(pointer,id,levels));
                if(groups.Count>ExpectedGroups)throw Mismatch("too many LOD groups");
            }
        }
        if(groups.Count!=ExpectedGroups)throw Mismatch($"expected {ExpectedGroups} scene LOD groups, found {groups.Count}");
        storage.Sort((a,b)=>a.Start.CompareTo(b.Start));
        for(int index=1;index<storage.Count;index++)
            if(storage[index].Start<storage[index-1].End)throw Mismatch("aliased native storage");

        var owners=new Dictionary<int,List<Group>>();
        foreach(var group in groups)
            foreach(int renderer in group.Levels.SelectMany(l=>l.Ids).Where(id=>id!=0).Distinct()) {
                if(!owners.TryGetValue(renderer,out var list))owners.Add(renderer,list=new());
                list.Add(group);
            }
        var edits=new List<Edit>();var removedIds=new HashSet<int>();var childIds=new HashSet<int>();
        foreach(var parent in groups.Where(g=>g.Levels.Length==2 && g.Levels[0].Ids.Length==33 && g.Levels[1].Ids.Length==2)) {
            int[] first=parent.Levels[0].Ids,second=parent.Levels[1].Ids;
            if(first.Contains(0) || first.Distinct().Count()!=33 || second.Contains(0) || second.Distinct().Count()!=2)
                throw Mismatch("reinforced parent renderer identity");
            var keep=new List<int>();int removed=0;
            foreach(int renderer in first) {
                var shared=owners[renderer];
                if(shared.Count==1){keep.Add(renderer);continue;}
                if(shared.Count!=2)throw Mismatch("renderer has multiple child owners");
                var child=shared.Single(g=>g.Id!=parent.Id);
                if(child.Levels.Length!=2 || child.Levels[0].Ids.Length!=1 || child.Levels[1].Ids.Length!=1 ||
                    child.Levels[0].Ids[0]!=renderer || child.Levels[1].Ids[0]==0 || child.Levels[1].Ids[0]==renderer ||
                    owners[child.Levels[1].Ids[0]].Count!=1 || !childIds.Add(child.Id) || !removedIds.Add(renderer))
                    throw Mismatch("child high/low ownership");
                removed++;
            }
            if(removed!=31 || keep.Count!=2 || !keep.ToHashSet().SetEquals(second) || second.Any(id=>owners[id].Count!=1))
                throw Mismatch("reinforced wood/child topology");
            edits.Add(new(parent,parent.Levels[0],keep.ToArray()));
        }
        // A repaired stock scene is a legitimate no-op. Any other partial or
        // foreign ownership graph is rejected before touching even one byte.
        int conflicts=owners.Count(pair=>pair.Value.Count>1);
        if(edits.Count==0 && conflicts==0)return 0;
        if(edits.Count!=ExpectedParents || removedIds.Count!=ExpectedRemoved || conflicts!=ExpectedRemoved ||
            owners.Any(pair=>pair.Value.Count>1 && !removedIds.Contains(pair.Key)))
            throw Mismatch("complete stock ownership graph");

        foreach(var edit in edits) {
            memory.Require(edit.Level.Data,8,true);
            memory.Require(edit.Level.Vector+16,8,true);
        }
        // Recheck the complete snapshot before the first mutation, including
        // the unchanged children that will own the removed parent references.
        foreach(var group in groups) {
            if(memory.ReadInt32(group.Address+96)!=0)throw Mismatch("registration changed during planning");
            foreach(var level in group.Levels) {
                if(memory.ReadInt64(level.Vector)!=level.Data || memory.ReadInt64(level.Vector+16)!=level.Ids.Length)
                    throw Mismatch("renderer vector changed during planning");
                for(int index=0;index<level.Ids.Length;index++)
                    if(memory.ReadInt32(level.Data+index*4)!=level.Ids[index])throw Mismatch("renderer changed during planning");
            }
        }
        var started=new List<Edit>();
        try {
            foreach(var edit in edits) {
                started.Add(edit);
                memory.WriteInt32(edit.Level.Data,edit.Keep[0]);
                memory.WriteInt32(edit.Level.Data+4,edit.Keep[1]);
                memory.WriteInt64(edit.Level.Vector+16,2);
            }
            foreach(var edit in edits)
                if(memory.ReadInt64(edit.Level.Vector+16)!=2 || memory.ReadInt32(edit.Level.Data)!=edit.Keep[0] || memory.ReadInt32(edit.Level.Data+4)!=edit.Keep[1])
                    throw Mismatch("write verification");
        } catch(Exception error) {
            var failures=new List<Exception>{error};
            foreach(var edit in started.AsEnumerable().Reverse())
                try {
                    memory.WriteInt32(edit.Level.Data,edit.Level.Ids[0]);
                    memory.WriteInt32(edit.Level.Data+4,edit.Level.Ids[1]);
                    memory.WriteInt64(edit.Level.Vector+16,edit.Level.Ids.Length);
                }catch(Exception rollback){failures.Add(rollback);}
            throw new AggregateException("Initial training LOD repair failed; original vector fields were restored where possible.",failures);
        }
        return ExpectedRemoved;
    }
}
