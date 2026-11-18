using Mirror;
using OperatorKillhouse.Core;
using UnityEngine;

namespace OperatorKillhouse;

internal static class PropPhysics
{
    internal sealed class Item {
        internal readonly GameObject Root;
        internal readonly Rigidbody Body;
        internal readonly Collider[] Colliders;
        internal readonly int Id;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly PhysicsMaterial? Material;
        internal readonly PhysicsMaterial?[]? OriginalMaterials;
        internal readonly PropPlayback Playback;
        internal float Sent=-10,PushAt,KickAt;
        internal bool SentSleeping,PendingRest;
        internal Item(GameObject root,Rigidbody body,Collider[] colliders,int id,PhysicsMaterial? material=null,PhysicsMaterial?[]? originals=null){Root=root;Body=body;Colliders=colliders;Id=id;Position=body.transform.localPosition;Rotation=body.transform.localRotation;Material=material;OriginalMaterials=originals;Playback=new(body);}
    }
    private static readonly Dictionary<IntPtr,Item> items=new();
    private static readonly Dictionary<IntPtr,Dictionary<int,Item>> byRoot=new();
    private static readonly Dictionary<IntPtr,Rigidbody> suppressedBullets=new();
    private static float pruneAt;
    internal static int Count=>items.Count;
    internal static bool HasRoot(GameObject root)=>byRoot.TryGetValue(root.Pointer,out var group) && group.Count>0;
    internal static int ContactMaterialCount=>items.Values.Count(p=>p.Material);
    internal static long CreatedContactMaterials {get;private set;}
    internal static long ReleasedContactMaterials {get;private set;}
    internal static void Configure(GameObject clone,string asset) {
        float mass=MovableProps.Mass(asset);if(mass<=0)return;
        var colliders=clone.GetComponentsInChildren<Collider>(true).Where(c=>c && !c.isTrigger && c.enabled).ToArray();
        if(colliders.Length==0 || clone.GetComponentInChildren<NetworkIdentity>(true))return;
        var bodies=clone.GetComponentsInChildren<Rigidbody>(true);
        // All reviewed models have one rigid assembly. Never silently break a
        // native multi-body setup or mutate its joints.
        if(bodies.Length>1 || bodies.Length==1 && bodies[0].transform.Pointer!=clone.transform.Pointer)return;
        foreach(var transform in clone.GetComponentsInChildren<Transform>(true))transform.gameObject.isStatic=false;
        foreach(var collider in colliders){var mesh=collider.TryCast<MeshCollider>();if(mesh && !mesh!.convex)mesh.convex=true;}
        var body=bodies.Length==1?bodies[0]:clone.AddComponent<Rigidbody>();
        body.mass=mass;body.useGravity=true;body.linearDamping=.25f;body.angularDamping=.5f;
        body.constraints=RigidbodyConstraints.None;
        body.maxAngularVelocity=8;body.maxLinearVelocity=8;body.maxDepenetrationVelocity=2;
        body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;body.interpolation=NetworkServer.active?RigidbodyInterpolation.Interpolate:RigidbodyInterpolation.None;
        body.isKinematic=!NetworkServer.active;
        foreach(var collider in colliders) {
            var hit=collider.GetComponent<OnBulletHitAddForce>()??collider.gameObject.AddComponent<OnBulletHitAddForce>();hit.RB=body;hit.force=5;
        }
        var root=clone.transform.parent.parent.gameObject;
        PhysicsMaterial? material=null;PhysicsMaterial?[]? originals=null;
        if(MovableProps.Rolling(asset)) {
            originals=colliders.Select(c=>c.sharedMaterial).ToArray();
            material=new PhysicsMaterial("Killhouse trolley rolling contact (owned)"){staticFriction=.05f,dynamicFriction=.035f,bounciness=0,frictionCombine=PhysicsMaterialCombine.Minimum};
            CreatedContactMaterials++;
            try{foreach(var collider in colliders)collider.sharedMaterial=material;}
            catch{ReleaseMaterial(colliders,material,originals);throw;}
            body.constraints=RigidbodyConstraints.FreezeRotationX|RigidbodyConstraints.FreezeRotationZ;
            body.linearDamping=.15f;body.angularDamping=1;
        }
        var item=new Item(root,body,colliders,clone.transform.GetSiblingIndex(),material,originals);items[body.Pointer]=item;
        if(!byRoot.TryGetValue(root.Pointer,out var group))byRoot[root.Pointer]=group=new();group.Add(item.Id,item);
    }
    internal static Item? Find(GameObject root,int id)=>byRoot.TryGetValue(root.Pointer,out var group) && group.TryGetValue(id,out var item) && item.Body && item.Body.gameObject.activeInHierarchy?item:null;
    internal static IEnumerable<Item> For(GameObject root) {
        if(!byRoot.TryGetValue(root.Pointer,out var group))yield break;
        foreach(var item in group.Values)if(item.Body && item.Body.gameObject.activeInHierarchy)yield return item;
    }
    internal static void TickPlayback(GameObject root,float now) {
        if(!byRoot.TryGetValue(root.Pointer,out var group))return;
        // Settled replicas have no interpolation work. Check managed buffer
        // state before crossing into Unity for each body on every render frame.
        foreach(var item in group.Values)if(item.Playback.Count>=2 && item.Body && item.Body.gameObject.activeInHierarchy)item.Playback.Tick(now);
    }
    internal static void WakeStack(Item struck) {
        if(!NetworkServer.active || !byRoot.TryGetValue(struck.Root.Pointer,out var group))return;
        // Reset sleeps authored stacks. Wake nearby independent bodies before
        // an impact so a box/bin can fall or tip when its support is displaced.
        // No support parenting, added force, friction change or guest simulation.
        var bounds=struck.Colliders[0].bounds;
        for(int n=1;n<struck.Colliders.Length;n++)if(struck.Colliders[n])bounds.Encapsulate(struck.Colliders[n].bounds);
        bounds.Expand(.06f);
        foreach(var other in group.Values)if(other.Body && other.Body.gameObject.activeInHierarchy && !other.Body.isKinematic) {
            foreach(var collider in other.Colliders)if(collider && collider.enabled && bounds.Intersects(collider.bounds)){other.Body.WakeUp();other.Sent=-10;other.SentSleeping=false;break;}
        }
    }
    internal static bool Shot(OnBulletHitAddForce force,BulletInstance bullet,Ray ray,RaycastHit hit) {
        if(!force.RB || !items.TryGetValue(force.RB.Pointer,out var item))return true;
        if(bullet is null)return false;
        suppressedBullets[bullet.Pointer]=item.Body;bullet.Force=0;
        // Replicated trajectories must not each apply the shooter's impulse.
        if(!bullet.isOwner && (bullet.isPlayer || !NetworkServer.active))return false;
        foreach(var session in Runtime.Sessions.Values)if(session.PushProp(item,ray.direction.normalized*5,hit.point,false))break;
        return false;
    }
    internal static void Kick(Melee melee,Collider collider) {
        if(!melee || !melee.canUse || !collider || !collider.enabled)return;
        var player=melee.playerNetworking;var local=PlayerMaster.MyPlayerMaster;
        if(!player || !local || !local!.PlayerSpawnedObject || local.PlayerSpawnedObject.Pointer!=player.Pointer || !player.health || player.health.isDead || !player.masterController || !player.masterController.characterCollider || !player.masterController.characterCollider.enabled)return;
        var body=collider.attachedRigidbody;if(!body || !items.TryGetValue(body!.Pointer,out var item) || Time.unscaledTime<item.KickAt)return;
        var capsule=player.masterController.characterCollider;
        if(player.masterController.mover && player.masterController.mover.IsGrounded()) {var support=player.masterController.mover.GetGroundCollider();if(support && support.attachedRigidbody && support.attachedRigidbody.Pointer==body.Pointer)return;}
        var point=collider.ClosestPoint(melee.thisCollider?melee.thisCollider.bounds.center:capsule.bounds.center);
        var origin=capsule.bounds.center;origin.y=point.y;
        if((point-player.transform.position).sqrMagnitude>4 || !PropContacts.Clear(capsule,collider,origin,point,player.transform))return;
        var direction=collider.bounds.center-origin;direction.y=0;if(direction.sqrMagnitude<.0001f)return;
        // Give small props a decisive kick and heavier carts useful momentum.
        // Leave room for float normalization/serialization at diagonal angles.
        foreach(var session in Runtime.Sessions.Values)if(session.PushProp(item,direction.normalized*Math.Min(MovableProps.MaxKickImpulse-.01f,body.mass*MovableProps.KickSpeedChange),point,false,true)) {
            item.KickAt=Time.unscaledTime+.25f;CoopLog.Event("prop-kicked",new{id=item.Id,host=NetworkServer.active});break;
        }
    }
    private static void ReleaseMaterial(Collider[] colliders,PhysicsMaterial? material,PhysicsMaterial?[]? originals) {
        if(!material)return;
        for(int n=0;n<colliders.Length;n++)if(colliders[n] && colliders[n].sharedMaterial==material)colliders[n].sharedMaterial=originals?[n];
        UnityEngine.Object.Destroy(material);
        ReleasedContactMaterials++;
    }
    internal static void Restore(GameObject root) {
        foreach(var item in For(root))Restore(item);
        Physics.SyncTransforms();
    }
    internal static void Restore(Item item) {
            item.Playback.Clear();
            var body=item.Body;body.isKinematic=true;body.transform.localPosition=item.Position;body.transform.localRotation=item.Rotation;
            body.position=body.transform.position;body.rotation=body.transform.rotation;
            body.isKinematic=!NetworkServer.active;if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.Sleep();}item.Sent=-10;item.SentSleeping=false;item.PushAt=item.KickAt=0;
    }
    internal static void RestoreBullet(BulletInstance bullet,float force){
        if(!suppressedBullets.Remove(bullet.Pointer,out var body))return;
        if(bullet.Force==0)bullet.Force=force;
        // Native generic impacts can unfreeze objects tagged Fragile. Restore
        // guest authority before the next physics step, including replica hits.
        if(!NetworkServer.active && body)body.isKinematic=true;
    }
    internal static void Prune() {
        if(Time.unscaledTime<pruneAt)return;pruneAt=Time.unscaledTime+1;
        foreach(var pointer in items.Where(p=>!p.Value.Root || !p.Value.Body).Select(p=>p.Key).ToArray()) {
            var item=items[pointer];items.Remove(pointer);
            ReleaseMaterial(item.Colliders,item.Material,item.OriginalMaterials);
            if(byRoot.TryGetValue(item.Root.Pointer,out var group)){group.Remove(item.Id);if(group.Count==0)byRoot.Remove(item.Root.Pointer);}
        }
    }
    internal static void Clear(){foreach(var item in items.Values)ReleaseMaterial(item.Colliders,item.Material,item.OriginalMaterials);items.Clear();byRoot.Clear();suppressedBullets.Clear();pruneAt=0;}
}
