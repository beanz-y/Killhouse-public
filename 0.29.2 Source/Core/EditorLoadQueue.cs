namespace OperatorKillhouse.Core;

public sealed record QueuedEditorMap(EditorProposal Proposal,string Player,bool Host=false,int StockIndex=-1);
public sealed class EditorLoadQueue {
    public const int Max=16,PerPlayer=4;
    private readonly List<QueuedEditorMap> items=new();
    public EditorRequestKey? Loading {get;private set;}
    public IReadOnlyList<QueuedEditorMap> Items=>items.ToArray();
    public bool CanAdd(EditorRequestKey key)=>items.Count<Max && items.Count(i=>i.Proposal.Key.Actor.Steam==key.Actor.Steam)<PerPlayer && items.All(i=>i.Proposal.Key!=key);
    public bool Add(EditorProposal proposal,string player){if(!CanAdd(proposal.Key))return false;items.Add(new(proposal,player));return true;}
    public bool CanAddHost(int house,uint range,string layoutId)=>items.Count<Max && items.All(i=>!i.Host || i.Proposal.Key.House!=house || i.Proposal.Key.Range!=range || i.Proposal.Key.LayoutId!=layoutId);
    public bool AddHost(EditorProposal proposal,int stockIndex=-1) {
        var key=proposal.Key;
        if(key.Actor!=default || key.Request<=0 || key.House is not (1 or 2) || key.Range==0 || stockIndex< -1 ||
            !CanAddHost(key.House,key.Range,key.LayoutId))return false;
        var parsed=LayoutFormat.Read(proposal.Canonical);
        if(!parsed.Success || (parsed.Layout!.Houses&key.House)==0 ||
            (stockIndex<0?parsed.Layout.Identity!=key.LayoutId:key.LayoutId!="stock:"+stockIndex))return false;
        items.Add(new(proposal,"Host",true,stockIndex));return true;
    }
    public QueuedEditorMap? Next=>items.FirstOrDefault();
    public void Remove(EditorRequestKey key){if(Loading!=key)items.RemoveAll(i=>i.Proposal.Key==key);}
    public void MoveUp(EditorRequestKey key){int at=items.FindIndex(i=>i.Proposal.Key==key);if(at>0)(items[at-1],items[at])=(items[at],items[at-1]);}
    public QueuedEditorMap? Begin(){if(Loading!=null || Next is not {} next)return null;Loading=next.Proposal.Key;return next;}
    public void Complete(int house,string layoutId,bool success){if(Loading is not {} key || key.House!=house || key.LayoutId!=layoutId)return;Loading=null;if(success)Remove(key);}
    public void CancelLoad()=>Loading=null;
    public void Clear(){Loading=null;items.Clear();}
}
