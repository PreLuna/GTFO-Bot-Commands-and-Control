using System;
using System.Collections.Generic;
using PreLuna.Captain.Tasks;
#pragma warning disable CS0649
public class NativeObject
{
    static long serial;
    public IntPtr Pointer = new(++serial);
    readonly int instance=(int)serial;
    public int GetInstanceID()=>instance;
    public T TryCast<T>() where T : class => this as T;
}
public enum eGameStateName { InLevel, Lobby }
public static class GameStateManager { public static eGameStateName CurrentStateName = eGameStateName.InLevel; }
public static class LayerManager { public const int MASK_ENEMY_DAMAGABLE=2,MASK_ENEMY=4; }
namespace Enemies { public class EnemyAgent : NativeObject { public bool Alive=true; public int EnemyDataID=17, Mode=4; public int GetInstanceID()=>Pointer.ToInt32(); } }
namespace HarmonyLib {
    public class Harmony { public void Patch(object method,HarmonyMethod prefix=null) { } }
    public class HarmonyMethod { public int priority; public HarmonyMethod(Type type,string name) { } }
    public static class Priority { public const int First = 800; }
    public static class AccessTools { public static object Method(Type t,string n) => null; }
}
namespace UnityEngine {
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static float Distance(Vector3 a,Vector3 b) => MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
    public class GameObject { public object Target; }
    public class Collider { public GameObject gameObject; }
    public struct RaycastHit { public Collider collider; }
    public static class Time { public static float unscaledTime,Scaled;public static bool Freeze; public static float time => Freeze?Scaled:unscaledTime; }
}
namespace SNetwork { public static class SNet { public static bool IsMaster = true; public static NativeObject Master=new(); } }
namespace LevelGeneration {
    public static class Builder { public static NativeObject CurrentFloor = new(); }
    public class ItemInLevel : NativeObject {
        public object ItemDataBlock = new(); public string PublicName = "机器人目标物品";
        public Player.InventorySlot Slot = Player.InventorySlot.ResourcePack;
        public bool Valid = true, Applied, Accept = true, Confirm = true; public int Submits;
        public Player.pItemData Get_pItemData() => new() { slot=Slot };
    }
}
namespace Player {
    public enum InventorySlot { None, ResourcePack=4, Consumable=5, Special=9 }
    public struct pItemData { public InventorySlot slot; }
    public class Owner : NativeObject { public bool IsBot; }
    public class Damage { public bool GrabbedAtAll; }
    public class PlayerLocomotion { public int m_currentStateEnum; }
    public class PlayerAgent : NativeObject {
        public bool Alive = true, IsBeingDestroyed, IsBeingDespawned;
        public int PlayerSlotIndex; public string PlayerName = "bot"; public Owner Owner = new() { IsBot = true };
        public object CourseNode = ItemRegion.Node; public Damage Damage = new(); public PlayerLocomotion Locomotion = new(); public PlayerAIBot Bot;
        public UnityEngine.Vector3 EyePosition = new(20,1.6f,20), Position = new(20,0,20);
        public T GetComponent<T>() where T : class => Bot as T;
    }
    public class PlayerManager {
        public static PlayerManager Current = new(); public PlayerAgent[] Slots = new PlayerAgent[4];
        public PlayerAgent GetPlayerAgentInSlot(int slot) => Slots[slot]; public static PlayerAgent GetLocalPlayerAgent() => Current.Slots[0];
    }
    public class PlayerBotActionBase : NativeObject {
        public Descriptor DescBase = new();
        public class Descriptor : NativeObject {
            internal static Dictionary<IntPtr,Descriptor> Native = new();
            public bool Terminated; public PlayerBotActionBase ParentActionBase;
            public Descriptor() { Native[Pointer]=this; }
            public Descriptor(IntPtr pointer) { Pointer=pointer; if(Native.TryGetValue(pointer,out var desc)) ParentActionBase=desc.ParentActionBase; }
            public bool IsTerminated() => Native.TryGetValue(Pointer,out var desc) ? desc.Terminated : Terminated;
        }
    }
    public class RootPlayerBotAction : PlayerBotActionBase { public PlayerAIBot m_bot; public new class Descriptor : PlayerBotActionBase.Descriptor { } }
    public class PlayerBotActionTravel : PlayerBotActionBase { }
    public class PlayerBotActionWalk : PlayerBotActionBase { }
    public class PlayerBotActionLook : PlayerBotActionBase { }
    public class PlayerBotActionFollow : PlayerBotActionBase { }
    public class PlayerBotActionIdle : PlayerBotActionBase { }
    public class PlayerBotActionTurn : PlayerBotActionBase { }
    public class PlayerBotActionCollectItem : PlayerBotActionBase { }
    public class PlayerBotActionUnlock : PlayerBotActionBase { }
    public class PlayerAIBot : NativeObject {
        public PlayerAgent Agent; public List<PlayerBotActionBase> Actions = new(); public List<PlayerBotActionBase.Descriptor> m_queuedActions = new();
        public Sync SyncValues = new(); public class Sync { public PlayerAgent Leader; } public int Stops;
        public bool StopAcknowledged = true;
        public void StopAction(PlayerBotActionBase.Descriptor desc) { if(StopAcknowledged) { desc.Terminated = true; if(PlayerBotActionBase.Descriptor.Native.TryGetValue(desc.Pointer,out var native))native.Terminated=true; } Stops++; }
    }
}
public class PUI_CommunicationMenu {
    public int Calls; public bool SawScope;
    public bool ExecuteCmdCall(Player.PlayerAgent local, Player.PlayerAgent target) {
        Calls++; SawScope = BotControlService.RecallScope; target.Bot.SyncValues.Leader = local; return true;
    }
}
namespace PreLuna.Captain {
    public static class Plugin { public static Log Logger = new(); public static CatalogImpl Catalog=new();
        public class CatalogImpl { public string TranslateItems(string s) => s; }
        public class Log { public void LogInfo(string s) { } public void LogWarning(string s) { } }
    }
}
namespace PreLuna.Captain.Tasks {
    static class ItemRegion { internal static readonly object Node = new(); internal static bool SameSpace(Player.PlayerAgent agent, object node) => ReferenceEquals(agent.CourseNode, node); }
    static class BotTaskBridge {
        internal static bool Permit=true, Renewal=true, Execute=true, UnsafeValue; internal static int Releases, Acquires; internal static ItemTaskStage LastStage;
        static long serial; static readonly Dictionary<IntPtr,long> leases = new();
        internal static void Reset() { Permit=Renewal=Execute=true;UnsafeValue=false;Releases=Acquires=0;leases.Clear(); }
        internal static bool Bind() => true; internal static bool Unsafe(Player.PlayerAIBot bot) => UnsafeValue;
        internal static bool Acquire(Player.PlayerAIBot bot,out long token) { token=0;Acquires++;if(!Permit||leases.ContainsKey(bot.Pointer))return false;token=++serial;leases[bot.Pointer]=token;return true; }
        internal static bool Report(Player.PlayerAIBot bot,long token,ItemTaskStage stage) { LastStage=stage;return Renewal&&leases.TryGetValue(bot.Pointer,out var current)&&current==token; }
        internal static bool CanExecute(Player.PlayerAIBot bot,long token) => Execute && leases.TryGetValue(bot.Pointer,out var current)&&current==token;
        internal static void Release(Player.PlayerAIBot bot,long token,ItemTaskStage stage) { Releases++;if(leases.TryGetValue(bot.Pointer,out var current)&&current==token)leases.Remove(bot.Pointer); }
        internal static bool AutoAvailable(Player.PlayerAIBot bot) => !leases.ContainsKey(bot.Pointer);
    }
    static class AutoPickupAdapter { internal static void CancelPrompt(Player.PlayerAIBot bot) { } }
    static class ItemTaskAdapter {
        internal static bool CommunicationOpen;
        internal sealed class Job { internal bool Paused,Yielded=true;internal int Revision; }
        internal static readonly Dictionary<IntPtr,Job> Jobs=new();internal static PUI_CommunicationMenu NativeMenu=new();
        internal static readonly HashSet<IntPtr> Reserved=new();internal static readonly List<string> Messages=new();
        internal static void Feedback(string text) => Messages.Add(text);
        internal static bool ReserveControlItem(LevelGeneration.ItemInLevel item,out Action cleanup) {
            cleanup=null;if(!Reserved.Add(item.Pointer))return false;cleanup=()=>Reserved.Remove(item.Pointer);return true;
        }
        internal static bool HasItemTask(Player.PlayerAIBot bot) => Jobs.ContainsKey(bot.Pointer);
        internal static void SuspendForBorrow(Player.PlayerAIBot bot,List<BorrowHold> holds) {
            if(!Jobs.TryGetValue(bot.Pointer,out var job))return;bool changed=!job.Paused;job.Paused=true;int revision=job.Revision;
            holds.Add(new(()=>job.Yielded,()=>{if(changed&&job.Revision==revision)job.Paused=false;},()=>job.Revision==revision));
        }
    }
    static class MovementOrders {
        internal static bool Accept=true, Protected, AcceptedWhileControlled;
        static long protectedAgent,protectedOwner;
        internal static bool ControlStationRecallProtected(long agent,long owner) => Protected&&agent==protectedAgent&&owner==protectedOwner;
        internal static bool AcceptControlStation(Player.PlayerAIBot b,MarkedPosition mark,out string reason){reason=Accept&&mark!=null?null:"驻守预检拒绝";if(reason==null){Protected=true;protectedAgent=b.Agent.Pointer.ToInt64();protectedOwner=b.Agent.Owner.Pointer.ToInt64();AcceptedWhileControlled=PreLuna.Captain.Control.BotControlApi.Controls(protectedAgent,protectedOwner);}return reason==null;}
        internal static bool FollowPaused;
        internal static void SuspendFollowForControl(Player.PlayerAIBot bot,List<BorrowHold> holds) { bool old=FollowPaused;FollowPaused=true;holds.Add(new(()=>true,()=>FollowPaused=old)); }
        internal static void KeepFollowPausedAfterControl(Player.PlayerAIBot bot) => FollowPaused=true;
    }
    static class RescueOrders { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class ToolSupplyAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class ResourceUseAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class NativePickupAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class SearchTaskAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class SingleKillAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false;
        internal static Enemies.EnemyAgent Resolve(Player.PlayerAgent source,UnityEngine.RaycastHit hit)=>hit.collider?.gameObject.Target as Enemies.EnemyAgent;
        internal static IControlCombatTask CreateControlled(Player.PlayerAIBot b,Enemies.EnemyAgent e,IControlCommandPermit permit,out string reason){reason=e.Mode==4?null:"已醒";return reason==null?new MockCombat(permit,e):null;}
    }
    static class FocusOrders { internal static bool Owns(Player.PlayerAIBot bot) => false;
        internal static IControlCombatTask CreateControlled(Player.PlayerAIBot b,Enemies.EnemyAgent e,IControlCommandPermit permit,out string reason){reason=e.Mode==1?null:"沉睡";return reason==null?new MockCombat(permit,e):null;}
    }
    sealed class MockCombat:IControlCombatTask {
        readonly IControlCommandPermit permit;readonly Enemies.EnemyAgent enemy;internal static MockCombat Last;long token;internal bool Atomic,CancelPending;
        public bool Finished{get;private set;}public string Phase=>"模拟战斗";
        internal MockCombat(IControlCommandPermit p,Enemies.EnemyAgent e){permit=p;enemy=e;Last=this;}
        public void Tick(float now){if(token==0&&!permit.Acquire(out token))return;permit.Report(Atomic?ItemTaskStage.Verifying:ItemTaskStage.Moving);if(!enemy.Alive||CancelPending&&!Atomic){Finished=true;permit.Hold();}}
        public void Cancel(){CancelPending=true;if(!Atomic){Finished=true;permit.Hold();}}public void Abort(string reason){Atomic=false;Cancel();}public bool Authorizes(Player.PlayerBotActionBase.Descriptor d)=>false;
    }
    static class SharedMeleeBridge { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class CollectTaskAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class CaptainMenuInput { internal static bool OwnsInput; }
    static class SpecialItemAccess { internal static bool Placed(LevelGeneration.ItemInLevel item) => item.Valid; }
    static class ItemInteractionGeometry { internal static bool Visible(UnityEngine.Vector3 eye,LevelGeneration.ItemInLevel item) => item.Valid; }
    enum SupplySource { Aimed }
    static class KnownSupplyIndex { internal static void Observe(LevelGeneration.ItemInLevel item,SupplySource source) { } }
    static class ItemTargetResolver {
        internal static object Hit;internal static Player.PlayerAgent LastSource;internal static UnityEngine.Vector3 LastOrigin,LastDirection;
        internal static bool Ray(Player.PlayerAgent source,UnityEngine.Vector3 origin,UnityEngine.Vector3 direction,out UnityEngine.RaycastHit hit,int extraMask=0) {
            LastSource=source;LastOrigin=origin;LastDirection=direction;hit=new(){collider=Hit==null?null:new(){gameObject=new(){Target=Hit}}};return Hit!=null;
        }
        internal static LevelGeneration.ItemInLevel Resolve(Player.PlayerAgent source,UnityEngine.RaycastHit hit) => hit.collider.gameObject.Target as LevelGeneration.ItemInLevel;
    }
    internal sealed class MarkedOpenTarget : NativeObject {
        internal static Player.PlayerAgent ResolvedBy;
        internal string Label="普通门",LockText="无锁",Restriction;internal bool Valid=true,IsOpen,Opening,NeedsMelee;internal int NeededMelters,Submits;
        internal IntPtr Key => Pointer;
        internal float MarkedAt=UnityEngine.Time.time;
        internal static MarkedOpenTarget Resolve(Player.PlayerAgent source,UnityEngine.GameObject target) { ResolvedBy=source;return target.Target as MarkedOpenTarget; }
    }
    sealed class MarkedPosition { }
    static class PositionMarks { internal static MarkedPosition ControlHere(Player.PlayerAgent bot)=>new(); }
    static class OpenTargetMarks {
        internal static float MarkLifetime=60;
        internal static string RejectReason(MarkedOpenTarget target,Player.PlayerAgent bot) => target==null?"没有机器人标记":!target.Valid?"目标失效":target.Restriction;
    }
    internal sealed record CaptainMenuLeaf(string Code,string Title,int Slot,long Owner,int Handler,string Blocked);
    enum TaskBoardState { Executing, Complete, Cancelled, Failed }
    static class TaskBoard { internal static JournalImpl Journal=new();internal class JournalImpl { internal void Record(object key,int slot,long owner,string name,string title,string target,TaskBoardState state,string stage,string detail,float at) { } } }
    internal sealed class ItemTaskSession : IItemTaskSession {
        readonly Player.PlayerAIBot bot;readonly LevelGeneration.ItemInLevel item;readonly Action cleanup;readonly IControlCommandPermit permit;
        readonly Action<ItemTaskStage,string> observer;long token;internal static ItemTaskSession Last;
        internal Player.PlayerBotActionBase.Descriptor Travel;
        internal static bool Near=true;
        internal ItemTaskSession(Player.PlayerAIBot bot,ItemTaskKind kind,Player.InventorySlot slot,LevelGeneration.ItemInLevel item,object container,string label,Action cleanup,
            IControlCommandPermit controlPermit=null,Action<ItemTaskStage,string> onStatus=null) { this.bot=bot;this.item=item;this.cleanup=cleanup;permit=controlPermit;observer=onStatus;Last=this; }
        public bool Valid => bot.Agent.Alive && SNetwork.SNet.IsMaster && GameStateManager.CurrentStateName==eGameStateName.InLevel;
        public bool Acquire() => permit.Acquire(out token);
        public bool Renew(ItemTaskStage stage) => permit.Report(stage);
        public bool CanExecute => BotTaskBridge.CanExecute(bot,token);
        public bool AtDestination(bool returning) => Near;
        public bool Move(bool returning) { Travel=new();ControlCommandAdapter.Authorizes(bot,Travel);return true; }
        public void StopOwnMovement() { if(Travel!=null)bot.StopAction(Travel); }
        public void Suspend() { StopOwnMovement();permit.Hold(); }
        public string Validate() => item.Valid?null:"已被别人拾取";
        public float InteractionDuration => .2f;
        public bool Apply(out string error) { error=null;if(!CanExecute||!item.Valid||item.Applied)return false;item.Applied=true;item.Submits++;return item.Accept; }
        public bool Verify() => item.Applied&&item.Confirm;
        public void Release(ItemTaskStage stage) { permit.Hold();cleanup(); }
        public void Notify(ItemTaskStage stage,string detail) { observer?.Invoke(stage,detail);if(stage>=ItemTaskStage.Complete)cleanup(); }
        internal bool OurAction(Player.PlayerBotActionBase.Descriptor desc) {
            for(int i=0;desc!=null&&i<8;i++,desc=desc.ParentActionBase?.DescBase)if(Travel!=null&&desc.Pointer==Travel.Pointer)return true;return false;
        }
    }
    static class OpenTaskAdapter {
        internal static bool Owns(Player.PlayerAIBot bot) => false;
        internal static HashSet<IntPtr> OldReservations=new();
        internal static bool Reserved(IntPtr key) => OldReservations.Contains(key)||ControlCommandAdapter.Reserved(key);
        internal sealed class Session : IOpenTaskSession {
            internal Action<OpenTaskStage,string> StatusObserver;readonly IControlCommandPermit permit;readonly Player.PlayerAIBot bot;long token;
            internal MarkedOpenTarget Target;
            internal Session(Player.PlayerAIBot bot,MarkedOpenTarget target,IControlCommandPermit permit) { this.bot=bot;Target=target;this.permit=permit; }
            public bool Valid=>Target.Valid&&bot.Agent.Alive;public string Restriction=>Target.Restriction;public bool IsOpen=>Target.IsOpen;public bool IsOpening=>Target.Opening;
            public bool Acquire()=>permit.Acquire(out token);public bool Renew(OpenTaskStage stage)=>permit.Report(stage==OpenTaskStage.Verifying?ItemTaskStage.Verifying:ItemTaskStage.Moving);
            public bool CanExecute=>BotTaskBridge.CanExecute(bot,token);public bool AtDestination(bool returning)=>true;public bool Move(bool returning)=>true;
            public void StopMovement(){}public void Suspend()=>permit.Hold();public bool NeedsUnlock=>false;public string CapabilityError=>null;
            public bool BeginUnlock()=>true;public bool LockResolved=>true;public bool UnlockAtomic=>false;public bool UnlockFailed=>false;public void FinishUnlock(){}
            public float OpenDuration=>.1f;public bool ApplyOpen(){if(!CanExecute)return false;Target.Submits++;Target.Opening=true;return true;}
            public void Finish(OpenTaskStage stage,string detail){permit.Hold();StatusObserver?.Invoke(stage,detail);}
            internal void Observe(OpenTaskStage stage)=>StatusObserver?.Invoke(stage,null);
            internal bool OurAction(Player.PlayerBotActionBase.Descriptor desc)=>false;
        }
    }
}
