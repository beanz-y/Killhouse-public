using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OperatorKillhouse.Core;

namespace OperatorKillhouse;

// A short-lived subscription to Unity's existing before-Awake callback array.
// No detour, Harmony patch, executable-memory write, or game-file write occurs.
// See research/runtime-initial-lod-20261029/native-findings.txt for the exact
// inspected player, callback ABI, load order and native vector evidence.
internal static class NativeInitialLod
{
    private const string PlayerHash="D935627D3AC843293F1C51EE9D85538191F0CB98DEF4271C1A29302B52A021D4";
    private const string StockSceneHash="531F1961620FB650A528E087B26525AF186778759BCE22BA4D1C8B395F287ABC";
    private const string RepairedSceneHash="C3625E5187252FDFA955965565D2A8C0002CBCCE5751096132A6E9370983E742";
    private static IntPtr module,array;
    private static RegisterCallback? register;
    private static UnregisterCallback? unregister;
    private static Registration? active;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetCallbacks();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RegisterCallback(IntPtr array,IntPtr function,IntPtr contextual,IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void UnregisterCallback(IntPtr array,ref IntPtr function,IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BeforeAwake(int handle,IntPtr queue,int mode,byte flag);
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetModuleFileNameW(IntPtr module,StringBuilder path,int size);

    internal static IDisposable Subscribe(Action<int,IntPtr,int> callback)
    {
        if(active!=null)throw new InvalidOperationException("An initial training LOD callback is already registered.");
        Validate();
        var memory=new CheckedNativeMemory();
        memory.Require(array.ToInt64(),0xCA8,true);
        int count=memory.ReadInt32(array.ToInt64()+0xC90);
        if(count<0 || count>=134)throw new NotSupportedException("Unity before-Awake callback capacity is unavailable.");
        if(memory.ReadInt64(array.ToInt64()+0xC98)!=0)throw new NotSupportedException("Unity before-Awake dispatch is already in progress.");
        var registration=new Registration(callback);
        // Root before crossing the native boundary; a failed verification must
        // not leave Unity holding a collectible reverse-P/Invoke thunk.
        active=registration;
        try {
            register!(array,registration.Pointer,IntPtr.Zero,IntPtr.Zero);
            if(!Contains(registration.Pointer))throw new InvalidOperationException("Unity did not retain the initial LOD callback.");
            return registration;
        }catch {
            registration.Dispose();
            throw;
        }
    }

    internal static int Repair(IntPtr queue)
    {
        if(module==IntPtr.Zero)throw new InvalidOperationException("Initial LOD native contract was not validated.");
        return InitialLodMemoryRepair.Repair(new CheckedNativeMemory(),queue.ToInt64(),
            checked(module.ToInt64()+0x2058DC0),checked(module.ToInt64()+0x1D10740));
    }

    internal static void Cancel()=>active?.Dispose();

    private static bool Contains(IntPtr function)
    {
        var memory=new CheckedNativeMemory();long start=array.ToInt64();memory.Require(start,0xCA8);
        int count=memory.ReadInt32(start+0xC90);
        if(count<0 || count>134)throw new InvalidOperationException("Unity callback count changed outside the inspected contract.");
        for(int index=0;index<count;index++)if(memory.ReadInt64(start+index*24)==function.ToInt64())return true;
        return false;
    }

    private sealed class Registration : IDisposable
    {
        private readonly BeforeAwake thunk;
        private readonly Action<int,IntPtr,int> callback;
        private readonly int thread=Environment.CurrentManagedThreadId;
        internal readonly IntPtr Pointer;
        private bool disposed;
        internal Registration(Action<int,IntPtr,int> callback){this.callback=callback;thunk=Invoke;Pointer=Marshal.GetFunctionPointerForDelegate(thunk);}
        private void Invoke(int handle,IntPtr queue,int mode,byte flag)
        {
            try {
                // Native callback arrays support tombstoning the current entry.
                // Remove it before data inspection or any user-level callback.
                Dispose();
                callback(handle,queue,mode);
            }catch(Exception error){Plugin.Logger.LogWarning("Initial training LOD callback failed: "+error.GetBaseException().Message);}
            finally{GC.KeepAlive(thunk);}
        }
        public void Dispose()
        {
            if(disposed)return;
            if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Initial LOD callback removal requires the scene thread.");
            var memory=new CheckedNativeMemory();memory.Require(array.ToInt64(),0xCA8,true);
            int count=memory.ReadInt32(array.ToInt64()+0xC90);
            if(count<0 || count>134)throw new InvalidOperationException("Cannot remove initial LOD callback from an invalid registry.");
            var pointer=Pointer;
            unregister!(array,ref pointer,IntPtr.Zero);
            if(Contains(Pointer))throw new InvalidOperationException("Initial training LOD callback removal was incomplete.");
            disposed=true;
            if(ReferenceEquals(active,this))active=null;
            GC.KeepAlive(thunk);
        }
    }

    private static void Validate()
    {
        if(!OperatingSystem.IsWindows() || IntPtr.Size!=8)throw new NotSupportedException("Initial LOD repair requires the inspected Windows x64 player.");
        string game=Path.GetDirectoryName(Environment.ProcessPath)!;
        // Accept the earlier local scene repair as an idempotent migration case.
        // These files are read only; the distributed fix changes runtime memory.
        using(var scene=File.OpenRead(Path.Combine(game,"OPERATOR_Data","level18"))) {
            using var digest=SHA256.Create();
            string hash=Convert.ToHexString(digest.ComputeHash(scene));
            if(hash!=StockSceneHash && hash!=RepairedSceneHash)throw new NotSupportedException("Initial LOD repair skipped: training scene differs from the inspected stock build.");
        }
        if(module!=IntPtr.Zero)return;
        IntPtr loaded=GetModuleHandleW("UnityPlayer.dll");
        if(loaded==IntPtr.Zero)throw new NotSupportedException("UnityPlayer is not loaded.");
        var path=new StringBuilder(32768);
        uint length=GetModuleFileNameW(loaded,path,path.Capacity);
        if(length==0 || length>=path.Capacity)throw new NotSupportedException("UnityPlayer module path is unavailable.");
        byte[] bytes=File.ReadAllBytes(path.ToString());
        if(Convert.ToHexString(SHA256.HashData(bytes))!=PlayerHash)throw new NotSupportedException("Initial LOD repair skipped: UnityPlayer differs from the inspected build.");
        int pe=BitConverter.ToInt32(bytes,0x3C),sections=BitConverter.ToUInt16(bytes,pe+6);
        int table=pe+24+BitConverter.ToUInt16(bytes,pe+20);
        var memory=new CheckedNativeMemory();
        foreach(var method in new[]{(Rva:0x3D2A10,Size:0x60),(Rva:0x2F0590,Size:0x180),(Rva:0x2F04C0,Size:0xD0)}) {
            int file=-1;
            for(int index=0;index<sections;index++) {
                int at=table+index*40,rva=BitConverter.ToInt32(bytes,at+12),size=BitConverter.ToInt32(bytes,at+16);
                if(method.Rva>=rva && method.Rva+method.Size<=rva+size){file=BitConverter.ToInt32(bytes,at+20)+method.Rva-rva;break;}
            }
            if(file<0)throw new NotSupportedException("Initial LOD native function is outside the inspected image.");
            long address=checked(loaded.ToInt64()+method.Rva);memory.Require(address,method.Size);
            var resident=new byte[method.Size];Marshal.Copy(new IntPtr(address),resident,0,resident.Length);
            if(!resident.AsSpan().SequenceEqual(bytes.AsSpan(file,method.Size)))throw new NotSupportedException("Initial LOD native callback API has been modified in memory.");
        }
        var get=Marshal.GetDelegateForFunctionPointer<GetCallbacks>(IntPtr.Add(loaded,0x3D2A10));
        IntPtr registry=get();
        if(registry==IntPtr.Zero)throw new NotSupportedException("Unity callback registry is unavailable.");
        IntPtr candidate=new(checked(registry.ToInt64()+0xB130));memory.Require(candidate.ToInt64(),0xCA8,true);
        register=Marshal.GetDelegateForFunctionPointer<RegisterCallback>(IntPtr.Add(loaded,0x2F0590));
        unregister=Marshal.GetDelegateForFunctionPointer<UnregisterCallback>(IntPtr.Add(loaded,0x2F04C0));
        array=candidate;module=loaded;
    }

    private sealed class CheckedNativeMemory : IInitialLodMemory
    {
        [StructLayout(LayoutKind.Sequential)] private struct Region
        {
            public IntPtr Base,Allocation;
            public uint AllocationProtection;
            public UIntPtr Size;
            public uint State,Protection,Type;
        }
        [DllImport("kernel32",SetLastError=true)] private static extern UIntPtr VirtualQuery(IntPtr address,out Region region,UIntPtr size);
        public void Require(long address,int bytes,bool write=false)
        {
            if(address<0x10000 || bytes<=0 || bytes>16000000)throw new InvalidOperationException("Invalid initial LOD memory range.");
            long end=checked(address+bytes),position=address;
            while(position<end) {
                if(VirtualQuery(new IntPtr(position),out var region,(UIntPtr)Marshal.SizeOf<Region>())==UIntPtr.Zero || region.State!=0x1000 ||
                    (region.Protection&0x101)!=0 || (region.Protection&0xEE)==0 || (write && (region.Protection&0xCC)==0))
                    throw new InvalidOperationException("Initial LOD memory is not accessible with the required permissions.");
                long regionEnd=checked(region.Base.ToInt64()+(long)region.Size.ToUInt64());
                if(region.Base.ToInt64()>position || regionEnd<=position)throw new InvalidOperationException("Invalid initial LOD memory region.");
                position=Math.Min(end,regionEnd);
            }
        }
        public int ReadInt32(long address)=>Marshal.ReadInt32(new IntPtr(address));
        public long ReadInt64(long address)=>Marshal.ReadInt64(new IntPtr(address));
        public void WriteInt32(long address,int value)=>Marshal.WriteInt32(new IntPtr(address),value);
        public void WriteInt64(long address,long value)=>Marshal.WriteInt64(new IntPtr(address),value);
    }
}
