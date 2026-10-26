using System;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEngine {
    public class Object { public static long Next; public IntPtr Pointer = new(++Next); public static List<Object> All=new(); public Object(){All.Add(this);} public T TryCast<T>() where T:class=>this as T; }
    public class Component : Object {
        Transform ownTransform; public Transform transform => this is Transform t ? t : ownTransform ??= new();
        public Dictionary<Type,Component> Parents=new(); public List<Component> Children=new();
        public T GetComponentInParent<T>() where T:Component => this is T t ? t : Parents.GetValueOrDefault(typeof(T)) as T;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T:Component => Children.FindAll(c=>c is T).ConvertAll(c=>(T)c).ToArray();
    }
    public class Transform : Component { public Vector3 position; public Quaternion rotation; public bool IsChildOf(Transform other)=>Pointer==other.Pointer; }
    public class GameObject { public bool activeInHierarchy = true; }
    public struct Bounds { public Vector3 center; }
    public class Collider : Component { public Bounds bounds; }
    public struct RaycastHit { public Collider collider; }
    public struct Quaternion { }
    public struct Vector3 { public float x,y,z; public Vector3(float value){x=value;y=z=0;} public static Vector3 up=>new(){y=1}; public static Vector3 forward=>new(){z=1}; public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z; public static Vector3 Cross(Vector3 a,Vector3 b)=>new(){x=a.y*b.z-a.z*b.y,y=a.z*b.x-a.x*b.z,z=a.x*b.y-a.y*b.x}; public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude; public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>MathF.Sqrt(sqrMagnitude); public Vector3 normalized=>magnitude>0?this*(1/magnitude):default; public static Vector3 operator -(Vector3 a,Vector3 b)=>new(){x=a.x-b.x,y=a.y-b.y,z=a.z-b.z}; public static Vector3 operator -(Vector3 a)=>new(){x=-a.x,y=-a.y,z=-a.z}; public static Vector3 operator +(Vector3 a,Vector3 b)=>new(){x=a.x+b.x,y=a.y+b.y,z=a.z+b.z}; public static Vector3 operator *(Vector3 a,float b)=>new(){x=a.x*b,y=a.y*b,z=a.z*b}; }
    public enum QueryTriggerInteraction { Ignore, Collide }
    public static class Physics {
        public static bool Blocked, BodyBlocked; public static Collider HitCollider;
        public static Func<Vector3, bool> BodyTest;
        public static bool Raycast(Vector3 from,Vector3 dir,float length,int mask,QueryTriggerInteraction query)=>Blocked;
        public static bool Raycast(Vector3 from,Vector3 dir,out RaycastHit hit,float length,int mask,QueryTriggerInteraction query){hit=new(){collider=HitCollider};return Blocked||HitCollider!=null;}
        public static bool CheckCapsule(Vector3 a,Vector3 b,float radius,int mask,QueryTriggerInteraction query)=>BodyBlocked || (BodyTest?.Invoke(a) ?? false);
    }
    public static class Resources { public static Object[] FindObjectsOfTypeAll(Type type)=>Object.All.FindAll(x=>type.IsInstanceOfType(x)).ToArray(); }
    public static class Time { public static float time; public static float unscaledTime=>time; }
}
namespace HarmonyLib {
    public class Patch { public string owner; public MethodInfo PatchMethod; }
    public class Patches { public List<Patch> Prefixes=new(),Postfixes=new(); }
    public class Harmony {public void Patch(MethodInfo method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){} public static Patches GetPatchInfo(MethodBase method)=>new(){Prefixes=PreLuna.Captain.Tasks.StackTestRuntime.Prefixes()};}
    public class HarmonyMethod:Attribute {public HarmonyMethod(Type type,string name){}}
    public static class AccessTools { public static Type TypeByName(string name)=>Type.GetType(name); public static MethodInfo Method(Type type,string name,Type[] args=null)=>type?.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance); }
}
namespace Il2CppInterop.Runtime { public static class Il2CppType {public static Type Of<T>()=>typeof(T);} }
namespace AIGraph { public class Zone : UnityEngine.Object { public int DimensionIndex; public UnityEngine.Object Layer = Shared; static readonly UnityEngine.Object Shared = new(); } public struct pCourseNode { public AIG_CourseNode Node; public bool TryGet(out AIG_CourseNode node){node=Node;return node!=null;} } public class AIG_CourseNode : UnityEngine.Object { public Zone m_zone = new(); public UnityEngine.Object m_area=new(); public static bool TryGetCourseNode(int dimension,UnityEngine.Vector3 p,float r,out AIG_CourseNode n){n=new();return true;} } }
public enum eGameStateName { Lobby, InLevel }
public static class LayerManager { public static int MASK_PLAYER_INTERACT_BLOCKERS=1,MASK_DEFAULT=2,MASK_PLAYER_INTERACT_SPHERE=4; }
public class Interact_Base : UnityEngine.Component { public UnityEngine.Collider m_colliderToOwn; public UnityEngine.Transform Root; public bool IsSelected; }
public class PlayerInteraction { public Interact_Base m_bestSelectedInteract; public bool m_currentSelected; public int m_pickupMask,m_interactionMask; }
public static class GameStateManager { public static eGameStateName CurrentStateName=eGameStateName.InLevel; }
namespace SNetwork { public static class SNet { public static bool IsMaster=true; } public class SNet_Player : UnityEngine.Object { public bool IsBot=true; } }
public class Item : UnityEngine.Component {
    public Player.pItemData Data; public GameData.ItemDataBlock ItemDataBlock = new(); public UnityEngine.GameObject gameObject = new();
    public Interact_Base PickupInteraction; public string PublicName="Ammo Pack";
    public Player.pItemData Get_pItemData()=>Data; public T TryCast<T>() where T:class=>this as T;
}
public class CarryItemPickup_Core : LevelGeneration.ItemInLevel {
    public LevelGeneration.ePickupItemStatus PickupItemStatus=LevelGeneration.ePickupItemStatus.PlacedInLevel;
    public void Setup(GameData.ItemDataBlock data){} public void OnSyncStateChange(){}
}
namespace GameData { public class ItemDataBlock { public float ConsumableAmmoMax=8; public uint persistentID; } }
namespace Player {
    public enum InventorySlot { None, GearStandard, GearSpecial, GearClass, ResourcePack, Consumable, ConsumableHeavy, InPocket, InLevelCarry }
    public struct pItemData_Custom { public float ammo; }
    public struct pItemData { public uint itemID_gearCRC; public InventorySlot slot; }
    public class InventorySlotAmmo { public float AmmoInPack; }
    public class PlayerAmmoStorage { public Dictionary<InventorySlot,InventorySlotAmmo> Counts=new(); public InventorySlotAmmo GetInventorySlotAmmo(InventorySlot slot)=>Counts.GetValueOrDefault(slot); }
    public class BackpackItem { public Item Instance; }
    public class PlayerBackpack { public Dictionary<InventorySlot,BackpackItem> Held=new(); public PlayerAmmoStorage AmmoStorage=new(); public bool TryGetBackpackItem(InventorySlot slot,out BackpackItem item)=>Held.TryGetValue(slot,out item); }
    public class PlayerAgent : UnityEngine.Object { public SNetwork.SNet_Player Owner=new(); public bool Alive=true,IsInBioscan; public AIGraph.AIG_CourseNode CourseNode=new(); public int DimensionIndex,PlayerSlotIndex; public UnityEngine.Vector3 Position; public UnityEngine.Vector3 EyePosition=>Position; public UnityEngine.Vector3 TargetLookDir=new(1); public PlayerInteraction Interaction=new(); public string PlayerName="Bishop"; }
    public class PlayerAIBot : UnityEngine.Object {
        public PlayerAgent Agent=new(); public SyncData SyncValues=new(); public int Requests,Stops; public bool Accept=true;
        public List<PlayerBotActionBase> Actions=new();
        public static bool SnapPositionToNav(UnityEngine.Vector3 desired,out UnityEngine.Vector3 point,float x,float y){point=desired;return true;}
        public bool RequestAction(PlayerBotActionBase.Descriptor d){Requests++; d.Terminated=!Accept; return Accept;}
        public void StopAction(PlayerBotActionBase.Descriptor d){Stops++; d.Terminated=true;}
    }
    public class SyncData { public PlayerAgent Leader=new(){ Position=new UnityEngine.Vector3(30) }; }
    public class PlayerBotActionBase { public Descriptor DescBase; public T TryCast<T>() where T:class=>this as T; public class Descriptor { static long next; public IntPtr Pointer=new(++next); public PlayerBotActionBase ParentActionBase; public bool Terminated; public float Prio; public bool IsTerminated()=>Terminated; } }
    public class PlayerBotActionTravel { public class Descriptor : PlayerBotActionBase.Descriptor {
        public enum DestinationEnum { Position }
        public Descriptor(PlayerAIBot bot){} public DestinationEnum DestinationType; public UnityEngine.Vector3 DestinationPos; public float Radius,Haste; public bool Persistent;
    } }
    public class PlayerBotActionCollectItem : PlayerBotActionBase { public static float s_transferDuration=.5f; }
    public class PlayerBotActionShareResourcePack : PlayerBotActionBase { }
    public class PlayerBotActionGatherDeployables : PlayerBotActionBase { }
    public class PlayerBotActionDeploySentryGun : PlayerBotActionBase { }
    public class PlayerBotActionDeployTripMine : PlayerBotActionBase { }
    public class PlayerBotActionCarryExpeditionItem : PlayerBotActionBase { }
    public class RootPlayerBotAction { public static PrioSettings m_prioSettings=new(); public class PrioSettings { public float CollectItem=3; } }
    public static class PlayerBackpackManager {
        public static Dictionary<IntPtr,PlayerBackpack> Bags=new(); public static Dictionary<uint,Item> Worlds=new();
        public static bool TryGetBackpack(SNetwork.SNet_Player owner,out PlayerBackpack bag)=>Bags.TryGetValue(owner.Pointer,out bag);
        public static bool TryGetItemInLevelFromItemData(pItemData data,out Item item)=>Worlds.TryGetValue(data.itemID_gearCRC,out item);
    }
}
namespace LevelGeneration {
    public static class Builder { public static UnityEngine.Object CurrentFloor=new(); }
    public enum ePickupItemStatus { PlacedInLevel, PickedUp }
    public enum ePickupItemInteractionType { Pickup, Place }
    public struct pPickupPlacement { public AIGraph.pCourseNode node; }
    public struct pPickupItemState { public ePickupItemStatus status; public pPickupPlacement placement; }
    public struct pPickupItemInteraction { }
    public class ItemInLevel : Item {
        public LG_ResourceContainer_Storage container;
        public AIGraph.AIG_CourseNode CourseNode=new(); public LG_PickupItem_Sync Sync;
        public LG_PickupItem_Sync GetSyncComponent()=>Sync; public Player.pItemData_Custom GetCustomData()=>Sync.Custom;
    }
    public class LG_PickupItem_Sync : UnityEngine.Component {
        public ItemInLevel Item; public pPickupItemState State; public Player.pItemData_Custom Custom;
        public Item item=>Item;
        public int Attempts; public SNetwork.SNet_Player LastOwner; public AIGraph.AIG_CourseNode LastNode; public bool Accept=true;
        public T TryCast<T>() where T:class=>this as T; public pPickupItemState GetCurrentState()=>State; public Player.pItemData_Custom GetCustomData()=>Custom;
        public void AttemptInteract(pPickupItemInteraction interaction) { }
        public void AttemptPickupInteraction(ePickupItemInteractionType type,SNetwork.SNet_Player owner,Player.pItemData_Custom custom,UnityEngine.Vector3 position,UnityEngine.Quaternion rotation,AIGraph.AIG_CourseNode node,bool dropped,bool force) {
            Attempts++; LastOwner=owner; LastNode=node; if(!Accept)return;
            var bag=Player.PlayerBackpackManager.Bags[owner.Pointer]; var slot=Item.Data.slot;
            if(type==ePickupItemInteractionType.Place){ State.status=ePickupItemStatus.PlacedInLevel; Custom=custom; Item.transform.position=position; bag.Held.Remove(slot); bag.AmmoStorage.Counts.Remove(slot); return; }
            float before=bag.AmmoStorage.Counts.GetValueOrDefault(slot)?.AmmoInPack??0;
            float total=before+custom.ammo, cap=slot==Player.InventorySlot.ResourcePack?100:Item.ItemDataBlock.ConsumableAmmoMax;
            if(PreLuna.Captain.Tasks.StackTestRuntime.FullMerge && bag.Held.ContainsKey(slot)){
                // Model the observed result of the direct-transfer provider;
                // production transaction behavior is tested in its own suite.
                State.status=ePickupItemStatus.PickedUp;Custom.ammo=0;
                bag.AmmoStorage.Counts[slot]=new(){AmmoInPack=total};return;
            }
            if(bag.Held.TryGetValue(slot,out var held) && (TheArchive.Core.FeaturesAPI.FeatureManager.Enabled||PreLuna.Captain.Tasks.StackTestRuntime.Standalone)){
                var old=(ItemInLevel)held.Instance;
                if(total>cap){old.Sync.State.status=ePickupItemStatus.PlacedInLevel;old.Sync.Custom.ammo=total-cap;}
                else old.Sync.State.status=ePickupItemStatus.PickedUp;
                total=Math.Min(total,cap);
            }
            State.status=ePickupItemStatus.PickedUp; Custom=custom;
            bag.Held[slot]=new(){Instance=Item}; bag.AmmoStorage.Counts[slot]=new(){AmmoInPack=total};
            Player.PlayerBackpackManager.Worlds[Item.Data.itemID_gearCRC]=Item;
        }
    }
    public enum eResourceContainerStatus { NotSetup, Locked, Closed, Open, PlayerClose, PlayerFar }
    public class LG_ResourceContainer_Storage : UnityEngine.Component { public Core m_core=new(); public class Core { public AIGraph.AIG_CourseNode SpawnNode=new(); public eResourceContainerStatus Status = eResourceContainerStatus.Open; } }
}
namespace Hikaria.Core.Features.Accessibility { public class ResourceStack {
    public static void TryStackItem(Item item,SNetwork.SNet_Player player){}
    public class LG_PickupItem_Sync__AttemptInteract__Patch { public static void Prefix(LevelGeneration.LG_PickupItem_Sync sync,LevelGeneration.pPickupItemInteraction interaction){} }
} }
namespace Hikaria.ResourceStack.Patches { public class Patch_LG_PickupItem_Sync {
    public static void LG_PickupItem_Sync__AttemptInteract__Prefix(LevelGeneration.LG_PickupItem_Sync sync,LevelGeneration.pPickupItemInteraction interaction){}
    public static void TryStackItem(Item item,SNetwork.SNet_Player player){}
} }
namespace TheArchive.Core.FeaturesAPI { public static class FeatureManager { public static bool Enabled; public static bool IsFeatureEnabled<T>()=>Enabled; } }
namespace PreLuna.CoopBotsCustom.Commands {
    public enum CommandKind { Collect=30, ExplicitTask=40 }
    public enum CommandStage { Queued, Moving, Interacting, Verifying, Paused, Returning }
    public enum CommandOutcome { Success,Failed,Cancelled }
    public static class BotCommandBroker {
        public const int ApiVersion=1; public static bool AllowAcquire=true,AllowExecute=true,AllowTeleport=true,Lease=true,CloseEnough; public static int Releases,Warps; public static CommandStage Stage;
        public static bool Resume; public static UnityEngine.Vector3 ResumeAt; public static CommandKind LastKind;
        public static bool RejectAfterRelease; public static int Acquires;
        public static bool TryAcquire(Player.PlayerAIBot bot,string source,CommandKind kind,out long token){token=1;LastKind=kind;Acquires++;return AllowAcquire&&!(RejectAfterRelease&&Releases>0);}
        public static bool TryGetResumeDestination(Player.PlayerAIBot bot,string source,long token,out UnityEngine.Vector3 point){point=ResumeAt;return Resume;}
        public static bool IsUnsafe(Player.PlayerAIBot bot)=>false;
        public static bool CanAutoCollect(Player.PlayerAIBot bot)=>AllowAcquire;
        public static bool ReportStage(Player.PlayerAIBot bot,string source,long token,CommandStage stage){Stage=stage;return Lease;}
        public static bool CanExecute(Player.PlayerAIBot bot,string source,long token)=>AllowExecute;
        public static bool Release(Player.PlayerAIBot bot,string source,long token,CommandOutcome outcome){Releases++;return true;}
        public static bool TryTeleport(Player.PlayerAIBot bot,string source,long token,UnityEngine.Vector3 position){if(!AllowTeleport)return false;if(CloseEnough)return true;Warps++;bot.Agent.Position=position;return true;}
    }
}
namespace PreLuna.Captain { public class MockLogger { public void LogInfo(string value){} public void LogWarning(string value){} } public static class Plugin { public const string Guid="PreLuna.Captain"; public static MockLogger Logger=new(); } internal static class KnownTypes { public static Type Get(string assembly,string name)=>Type.GetType(name); } }
namespace PreLuna.Captain.Tasks {
    // Ordinary item regression fixture; native special behavior has its own fixture.
    internal static class SpecialItemAccess { public static bool Placed(LevelGeneration.ItemInLevel item)
        => item?.Sync != null ? item.Sync.State.status==LevelGeneration.ePickupItemStatus.PlacedInLevel
            : item is CarryItemPickup_Core carry && carry.PickupItemStatus==LevelGeneration.ePickupItemStatus.PlacedInLevel; }
    internal static class StackTestRuntime {
        public static bool Standalone,CorePatched=true,Unknown,CoreReadable=true,ChangedCode,WrongOwner,Duplicate,FullMerge;
        public static int HashReads;
        public static void Reset(){Standalone=Unknown=ChangedCode=WrongOwner=Duplicate=FullMerge=false;CorePatched=CoreReadable=true;HashReads=0;StackCompatibility.Shared=Adapter();}
        public static List<HarmonyLib.Patch> Prefixes(){
            var patches=new List<HarmonyLib.Patch>();
            if(CorePatched)patches.Add(new(){owner="TheArchive",PatchMethod=typeof(Hikaria.Core.Features.Accessibility.ResourceStack.LG_PickupItem_Sync__AttemptInteract__Patch).GetMethod("Prefix")});
            if(Standalone){var patch=new HarmonyLib.Patch{owner=WrongOwner?"different":"Hikaria.ResourceStack",PatchMethod=typeof(Hikaria.ResourceStack.Patches.Patch_LG_PickupItem_Sync).GetMethod("LG_PickupItem_Sync__AttemptInteract__Prefix")};patches.Add(patch);if(Duplicate)patches.Add(patch);}
            if(Unknown)patches.Add(new(){owner="OtherStack",PatchMethod=typeof(StackTestRuntime).GetMethod("OtherStackPrefix")});
            return patches;
        }
        public static void OtherStackPrefix(){}
        public static string Hash(MethodInfo method){HashReads++;if(ChangedCode)return "changed";bool standalone=method.DeclaringType==typeof(Hikaria.ResourceStack.Patches.Patch_LG_PickupItem_Sync);return method.Name=="TryStackItem"?(standalone?StackRuntime.StandaloneRuleHash:StackRuntime.CoreRuleHash):(standalone?StackRuntime.StandalonePrefixHash:StackRuntime.CorePrefixHash);}
        public static StackObservation Observe()=>FullMerge?new(1,true,StackRuntime.LocalProvider,null,float.MaxValue,1,true):StackRuntime.Probe(Prefixes(),()=>CoreReadable?TheArchive.Core.FeaturesAPI.FeatureManager.Enabled:null,Hash);
        public static StackCompatibility Adapter()=>new(Observe);
    }
    internal static class ItemActionAuthorization { public static void Quiesce(Player.PlayerAIBot bot){} }
    internal static partial class ItemTaskAdapter { public static bool TeleportEnabled=true; public static float PickupReach=2.2f,TeleportDistance=8; public static readonly List<string> Messages=new(); public static void Feedback(string text)=>Messages.Add(text); public static void Diagnose(Player.PlayerAIBot bot,LevelGeneration.ItemInLevel item,Player.InventorySlot slot,string label,string phase,string reason,bool feedback=true)=>Messages.Add("[拾取诊断] "+phase+" "+reason); }
    internal static class AutoPickupAdapter { public static bool Open(LevelGeneration.ItemInLevel item)=>item.container==null||item.container.m_core.Status==LevelGeneration.eResourceContainerStatus.Open; public static void Forget(IntPtr pointer){} public static void CancelPrompt(Player.PlayerAIBot bot){} }
    internal sealed class ContainerBridge { public IntPtr Key=new(999); public bool Available=true; public LevelGeneration.LG_ResourceContainer_Storage Storage=new(); public UnityEngine.Transform Pose=new(){position=new UnityEngine.Vector3(10)}; public UnityEngine.Transform Placement(Player.InventorySlot slot)=>Pose; }
}

namespace PreLuna.Captain.Tasks { internal static class BrokerHeartbeatPatch { internal static void Install(System.Type t){} internal static void Uninstall(){} } }
