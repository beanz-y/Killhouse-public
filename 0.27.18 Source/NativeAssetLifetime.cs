using Unity.Loading;

namespace OperatorKillhouse;

internal static class NativeAssetLifetime
{
    // Preparation workers own only managed data and disk streams. Do not wait
    // on the Unity thread; still observe a failure after Stop abandons a worker.
    internal static void Observe(Task? worker) {
        if(worker!=null)_=worker.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
    internal static void Release(ContentFile[] contents) {
        var failures=new List<Exception>();
        for(int i=0;i<contents.Length;i++) {
            var content=contents[i];contents[i]=default;
            try{if(content.IsValid)content.UnloadAsync();}
            catch(Exception error){failures.Add(error);}
        }
        if(failures.Count>0)throw new AggregateException("Native content release failed.",failures);
    }
}
