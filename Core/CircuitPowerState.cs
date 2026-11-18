namespace OperatorKillhouse.Core;

// The host alone advances reset epochs; guests adopt complete reliable snapshots.
public sealed class CircuitPowerState
{
    public int Mask {get;private set;}=1;
    public long Epoch {get;private set;}
    public long Sequence {get;private set;}
    private readonly HashSet<int> broken=new(),lights=new();
    public IReadOnlySet<int> BrokenLights=>broken;
    public void Reset(Layout doc,bool authority) {
        lights.Clear();foreach(var light in doc.Lights)for(int source=0;source<LightDamage.Sources(doc,light.PropId);source++)lights.Add(LightDamage.Key(light.PropId,source));
        if(authority){Epoch++;Mask=PowerCircuits.Initial(doc);broken.Clear();}
    }
    public bool BreakLight(int id,long expectedEpoch=0)=>Epoch>0 && (expectedEpoch==0 || expectedEpoch==Epoch) && lights.Contains(id) && broken.Add(id);
    public bool Set(int circuit,bool on) {
        if(circuit is <0 or >PowerCircuits.MaxCircuits || Epoch<=0)return false;
        int next=on?Mask|1<<circuit:Mask&~(1<<circuit);bool changed=next!=Mask;Mask=next;return changed;
    }
    public bool Toggle(int circuit,long expectedEpoch=0) {
        if(circuit is <0 or >PowerCircuits.MaxCircuits || Epoch<=0 || expectedEpoch!=0 && expectedEpoch!=Epoch)return false;
        Mask^=1<<circuit;return true;
    }
    public bool Receive(long epoch,long sequence,int mask,int[]? brokenLights=null) {
        if(epoch<=0 || epoch<Epoch || sequence<=Sequence || mask is <0 or >PowerCircuits.Mask || !LightDamage.ValidBroken(brokenLights))return false;
        Epoch=epoch;Sequence=sequence;Mask=mask;broken.Clear();if(brokenLights!=null)foreach(int id in brokenLights)broken.Add(id);return true;
    }
}
