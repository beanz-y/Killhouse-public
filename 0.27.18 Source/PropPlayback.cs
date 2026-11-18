using UnityEngine;

namespace OperatorKillhouse;

// Guest pose playback is separate from the host's physics simulation. Arrival
// times avoid requiring synchronized peer clocks; a short buffer absorbs jitter.
internal sealed class PropPlayback
{
    private readonly record struct Sample(float At,Vector3 Position,Quaternion Rotation);
    private readonly Sample[] samples=new Sample[8];
    private readonly Rigidbody body;
    private readonly Transform transform;
    private int count;
    private long sequence;
    private float receivedAt,playedAt,delay=.1f;
    internal static long Received {get;private set;}
    internal static long Rejected {get;private set;}
    internal static long HardCorrections {get;private set;}
    internal static long BufferRestarts {get;private set;}
    internal static float MaxArrivalGap {get;private set;}
    internal int Count=>count;
    internal PropPlayback(Rigidbody body){this.body=body;transform=body.transform;}
    internal void Clear(){count=0;sequence=0;receivedAt=playedAt=0;delay=.1f;}
    internal void Receive(long next,float now,Vector3 position,Quaternion rotation) {
        if(!body || next<=sequence || !float.IsFinite(now) || now<0 || count>0 && now<receivedAt){Rejected++;return;}
        Received++;
        if(count==0 || (position-samples[count-1].Position).sqrMagnitude>16) {
            if(count>0)HardCorrections++;
            count=0;delay=.1f;playedAt=now-delay;Append(new(now,position,rotation));
            sequence=next;receivedAt=now;Apply(position,rotation);return;
        }
        float gap=now-receivedAt;MaxArrivalGap=Math.Max(MaxArrivalGap,gap);
        // Recover promptly after jitter instead of permanently keeping the
        // largest observed delay (previously up to 350 ms) for this prop.
        delay=Math.Clamp(gap,.1f,.15f);
        // Sleeping props are sent only as heartbeats. Restart from the currently
        // displayed pose when motion resumes or the buffer ran dry, rather than
        // interpolating most of a long stale interval in one rendered frame.
        if(gap>.5f || playedAt>samples[count-1].At+.0001f) {
            BufferRestarts++;count=0;if(gap>.5f)delay=.1f;
            playedAt=now-delay;Append(new(playedAt,body.position,body.rotation));
        }
        sequence=next;receivedAt=now;
        if(count>0 && now<=samples[count-1].At+.00001f)samples[count-1]=new(samples[count-1].At,position,rotation);
        else Append(new(now,position,rotation));
    }
    private void Append(Sample sample){if(count==samples.Length)DropFirst();samples[count++]=sample;}
    private void DropFirst(){Array.Copy(samples,1,samples,0,--count);}
    internal void Tick(float now) {
        if(count<2 || !float.IsFinite(now) || !body)return;
        float at=Math.Max(playedAt,now-delay);playedAt=at;
        while(count>2 && samples[1].At<=at)DropFirst();
        var a=samples[0];var b=samples[1];
        if(at>=b.At){Apply(b.Position,b.Rotation);samples[0]=b;count=1;return;}
        float blend=Math.Clamp((at-a.At)/Math.Max(.00001f,b.At-a.At),0,1);
        Apply(Vector3.Lerp(a.Position,b.Position,blend),Quaternion.Slerp(a.Rotation,b.Rotation,blend));
    }
    private void Apply(Vector3 position,Quaternion rotation) {
        body.isKinematic=true;body.interpolation=RigidbodyInterpolation.None;
        body.position=position;body.rotation=rotation;
        // Rigidbody.position alone publishes on a physics step. Publish the
        // rendered transform too so a faster display gets a pose every frame.
        transform.SetPositionAndRotation(position,rotation);
    }
}
