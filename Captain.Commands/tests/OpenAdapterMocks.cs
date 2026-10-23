using System;
using System.Collections.Generic;
// External scene fixture fields intentionally use default values unless a scenario changes them.
#pragma warning disable CS0649
class Native
{
    static int sequence;
    public IntPtr Pointer = (IntPtr)(++sequence);
    public T TryCast<T>() where T:class => this as T;
}
namespace HarmonyLib {
    class Harmony { public void Patch(object method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){} }
    class HarmonyMethod:Attribute { public HarmonyMethod(Type type,string name){} }
    static class AccessTools { public static object Method(Type type,string name)=>null; }
}
namespace UnityEngine {
    class GameObject:Native {
        public bool activeInHierarchy=true;
        public Transform transform=new();
        public object ParentComponent;
        public T GetComponentInParent<T>() where T:class => ParentComponent as T;
    }
    class Transform:Native { public Vector3 position; public bool IsChildOf(Transform other)=>false; }
    struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new(0,1,0); public static Vector3 forward=>new(0,0,1);
        public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized=>magnitude==0?default:this*(1/magnitude);
        public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator-(Vector3 a)=>new(-a.x,-a.y,-a.z);
        public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
        public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    }
    enum QueryTriggerInteraction{Ignore}
    static class Physics { public static bool CheckCapsule(Vector3 a,Vector3 b,float r,int m,QueryTriggerInteraction q)=>false; }
    static class Time { public static float time; }
}
namespace AIGraph {
    class AIG_CourseNode:Native {
        public static bool TryGetCourseNode(int dimension,UnityEngine.Vector3 point,int radius,out AIG_CourseNode node){node=new();return true;}
    }
}
namespace Gear {
    class MeleeWeaponThirdPerson:Item{}
    class LockMelterFirstPerson:Item{}
}
namespace SNetwork {
    static class SNet { public static bool IsMaster=true; }
    class SNet_Player:Native {public bool IsBot=true;}
}
enum eGameStateName{InLevel,Other}
static class GameStateManager {public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
static class LayerManager {public static int MASK_DEFAULT;}
class ItemData {public List<string> FirstPersonPrefabs;}
class Item:Native {public ItemData ItemDataBlock=new();}
namespace Player {
    enum InventorySlot {GearMelee,Consumable}
    class BackpackItem {public Item Instance;}
    class Ammo {public bool HasBulletsLeft;public int BulletsInPack;}
    class AmmoStorage {public Ammo GetInventorySlotAmmo(InventorySlot slot)=>new();}
    class PlayerBackpack {public AmmoStorage AmmoStorage=new();public bool TryGetBackpackItem(InventorySlot slot,out BackpackItem item){item=new(){Instance=new Gear.MeleeWeaponThirdPerson()};return true;}}
    class PlayerAgent:Native {
        public bool Alive=true;public SNetwork.SNet_Player Owner=new();public int PlayerSlotIndex,DimensionIndex;
        public UnityEngine.Vector3 Position,EyePosition;
        public AIGraph.AIG_CourseNode CourseNode=new();public string PlayerName="测试机器人";
    }
    class PlayerManager {
        public static PlayerManager Current=new();public PlayerAgent Bot;
        public PlayerAgent GetPlayerAgentInSlot(int slot)=>Bot;
    }
    class PlayerAIBot:Native {
        public PlayerAgent Agent=new();public PlayerBackpack Backpack=new();public Sync SyncValues=new();
        public List<PlayerBotActionBase> Actions=new();public Func<PlayerBotActionBase.Descriptor,bool> OnRequest;
        public List<PlayerBotActionBase.Descriptor> m_queuedActions=new();
        public bool IsActionForbidden(PlayerBotActionBase.Descriptor desc)=>Actions.Exists(x=>!x.IsActionAllowed(desc))||m_queuedActions.Exists(x=>!x.IsActionAllowed(desc));
        public bool RequestAction(PlayerBotActionBase.Descriptor desc)=>OnRequest?.Invoke(desc)??true;
        public void StopAction(PlayerBotActionBase.Descriptor desc){desc.Terminated=true;}
        public static bool SnapPositionToNav(UnityEngine.Vector3 desired,out UnityEngine.Vector3 actual,int a,int b){actual=desired;return true;}
    }
    class Sync{public PlayerAgent Leader=new();}
    class PlayerBotActionBase:Native {
        public Descriptor DescBase;
        public bool IsActionAllowed(Descriptor desc)=>DescBase?.IsActionAllowed(desc)??true;
        public class Descriptor:Native {
            public float Prio;public int RequiredLayers;public bool Terminated,Allowed=true;public PlayerBotActionBase ActionBase,ParentActionBase;
            public bool IsTerminated()=>Terminated;
            public bool IsActionAllowed(Descriptor desc)=>Allowed;
        }
    }
    class PlayerBotActionTravel:PlayerBotActionBase {
        public new class Descriptor:PlayerBotActionBase.Descriptor {
            public Descriptor(PlayerAIBot bot){}public enum DestinationEnum{Position}
            public float Haste,Radius,RadiusHeightTolerance;public bool Persistent;
            public DestinationEnum DestinationType;public UnityEngine.Vector3 DestinationPos;
        }
    }
    class PlayerBotActionUnlock:PlayerBotActionBase {
        public PlayerBotActionBase.Descriptor m_chosenMethod;public Descriptor.MethodEnum m_method;
        public new class Descriptor:PlayerBotActionBase.Descriptor {
            public Descriptor(PlayerAIBot bot){}public enum TargetTypeEnum{Container,Door}public enum MethodEnum{Melt,Melee}
            public TargetTypeEnum TargetType;public MethodEnum Method,UsedMethod;public LevelGeneration.LG_WeakLock Lock;
            public UnityEngine.GameObject TargetGO;public UnityEngine.Vector3 TargetPosition;public AIGraph.AIG_CourseNode CourseNode;
        }
    }
    class PlayerBotActionMelee:PlayerBotActionBase {
        public new class Descriptor:PlayerBotActionBase.Descriptor {
            public enum StateEnum{Charge,Strike,Cooldown}public StateEnum State;
            public UnityEngine.GameObject TargetGameObject;public Native TargetAgent;
        }
    }
    class PlayerBotActionUseLockMelter:PlayerBotActionBase {
        public enum State{ApplyMelter,Other}public State m_state;
        public new class Descriptor:PlayerBotActionBase.Descriptor{public LevelGeneration.LG_WeakLock Lock;}
    }
    class PlayerBotActionRevive {public class Descriptor:PlayerBotActionBase.Descriptor{}}
    class PlayerBotActionCollectItem:PlayerBotActionBase{}
    class PlayerBotActionShareResourcePack:PlayerBotActionBase{}
    class PlayerBotActionGatherDeployables:PlayerBotActionBase{}
    class PlayerBotActionDeploySentryGun:PlayerBotActionBase{}
    class PlayerBotActionDeployTripMine:PlayerBotActionBase{}
    class PlayerBotActionCarryExpeditionItem:PlayerBotActionBase{}
}
namespace LevelGeneration {
    enum eDoorStatus{Closed,Open}
    class LG_WeakDoor:Native {
        public bool InAnimation,m_inButtonOperation,InteractionAllowed=true;public eDoorStatus LastStatus;
        public UnityEngine.GameObject gameObject=new();
        public List<LG_WeakLock> WeakLocks=new();
        public int Calls;public bool LastOnlyUnlock,Accepted=true;public Action OnOpen;
        public void AttemptOpenCloseInteraction(bool onlyUnlock){Calls++;LastOnlyUnlock=onlyUnlock;OnOpen?.Invoke();if(Accepted&&!onlyUnlock)LastStatus=eDoorStatus.Open;}
    }
    class LG_DoorButton:Native {
        public bool m_enabled=true,Allowed=true,Accepted=true;public float OperationDelay=.1f;public object OnOperate=new();
        public UnityEngine.GameObject gameObject=new();public LG_WeakDoor Door;public int Calls;
        public LG_WeakDoor m_door=>Door;
        public bool CanInteract()=>Allowed;
        public bool Interact(Player.PlayerAgent agent){Calls++;if(Accepted)Door.LastStatus=eDoorStatus.Open;return Accepted;}
    }
    class Interact:Native {
        public float InteractDuration=.1f;public bool IsActive=true,IsBlocked;public bool PlayerCanInteract(Player.PlayerAgent agent)=>true;
        public int Calls;public Action OnTrigger;
        public void TriggerInteractionAction(Player.PlayerAgent agent){Calls++;OnTrigger?.Invoke();}
    }
    class LG_WeakResourceContainer:Native {public Interact m_intOpen=new();public LG_WeakLock WeakLockComponent;public bool ISOpen;public UnityEngine.GameObject gameObject=new();}
    class LG_WeakLock:Native {
        public UnityEngine.GameObject gameObject=new(),m_meleeRoot=new();public Damage m_meleeDamage=new();
        public UnityEngine.Transform m_lockMelterAlignHackable,transform=new();public int m_lockType=1,Status;
        public bool Locked=true;public bool IsLocked()=>Locked;
        public Native m_holder;
    }
    class Damage{public UnityEngine.Vector3 DamageTargetPos;public UnityEngine.GameObject gameObject=new();}
    static class Builder {public static Native CurrentFloor=new();}
}
namespace PreLuna.Captain {
    class TestLogger {public List<string> Lines=new();public void LogInfo(object text)=>Lines.Add(text.ToString());public void LogWarning(object text)=>Lines.Add(text.ToString());public void LogError(object text)=>Lines.Add(text.ToString());}
    static class Plugin {public static TestLogger Logger=new();}
}
namespace PreLuna.Captain.Tasks {
    using LevelGeneration;using Player;using UnityEngine;
    // Mock only the external scene and broker. Compile the actual task adapter/diagnostics/runner.
    class MarkedOpenTarget {
        public LG_WeakResourceContainer Box;public LG_WeakDoor Door=new();public LG_DoorButton Button;
        public AIGraph.AIG_CourseNode Node=new();public IntPtr Floor=Builder.CurrentFloor.Pointer,Key=(IntPtr)77;
        public string Label="普通门 #77";string restriction;public bool Valid=true;
        public string Restriction {get=>restriction??OpenTargetLocks.Error(Box,Door,Button);set=>restriction=value;}
        public LG_WeakLock Lock {get=>Box!=null?Box.WeakLockComponent:Door.WeakLocks.Count>0?Door.WeakLocks[0]:null;set{Door.WeakLocks.Clear();if(value!=null){value.m_holder??=Button;Door.WeakLocks.Add(value);}}}
        public MarkedOpenTarget(){Button=new(){Door=Door};}
        public bool IsOpen=>Box!=null?Box.ISOpen:Door.LastStatus==eDoorStatus.Open;public bool Opening=>Door?.InAnimation==true;
        public GameObject Root=>Box?.gameObject??Door.gameObject;public Vector3 OpenPoint;
        public static OpenLockKind Kind(LG_WeakLock item)=>item==null?OpenLockKind.None:OpenTaskPolicy.Lock(item.IsLocked(),item.Status,item.m_lockType);
        public LG_WeakLock NextLock {get{foreach(var item in OpenTargetLocks.Selected(Box,Door,Button))if(Kind(item)!=OpenLockKind.None)return item;return null;}}
        public bool NeedsMelee {get{foreach(var item in OpenTargetLocks.Selected(Box,Door,Button))if(Kind(item)==OpenLockKind.Melee)return true;return false;}}
        public int NeededMelters {get{int count=0;foreach(var item in OpenTargetLocks.Selected(Box,Door,Button))if(Kind(item)==OpenLockKind.Hackable)count++;return count;}}
        public string LockText=>Lock==null?"无锁":"物理弱锁";
        public bool SameSpace(PlayerAgent agent)=>Valid;public bool Visible(Vector3 eye,Vector3 point)=>true;
        public IEnumerable<LG_WeakLock> Locks()=>OpenTargetLocks.All(Box,Door);
    }
    static class OpenTargetMarks {
        public static void Install(HarmonyLib.Harmony harmony){}public static void Reset(){}
        public static bool Fresh(MarkedOpenTarget target)=>target?.Valid==true;
        public static string RejectReason(MarkedOpenTarget target)=>target==null?"未标记":target.Restriction;
    }
    static class ItemTaskAdapter {
        public static bool Busy(PlayerAIBot bot)=>false;public static float PickupReach=2.2f,TeleportDistance=8;public static bool TeleportEnabled;
        public static void Feedback(string text)=>PreLuna.Captain.Plugin.Logger.LogInfo(text);
    }
    static class SingleKillAdapter {public static bool Owns(PlayerAIBot bot)=>false;}
    static class AutoPickupAdapter {public static void CancelPrompt(PlayerAIBot bot){}}
    static class ItemActionAuthorization {public static void Quiesce(PlayerAIBot bot){}}
    enum ItemTaskStage{Moving,Verifying,Returning,Complete,Cancelled,Failed}
    static class BotTaskBridge {
        public static bool Acquire(PlayerAIBot bot,out long token){token=1;return true;}
        public static bool Report(PlayerAIBot bot,long token,ItemTaskStage stage)=>true;
        public static bool CanExecute(PlayerAIBot bot,long token)=>true;public static bool Unsafe(PlayerAIBot bot)=>false;
        public static bool ResumePosition(PlayerAIBot bot,long token,out Vector3 point){point=default;return true;}
        public static bool Teleport(PlayerAIBot bot,long token,Vector3 point)=>false;
        public static void Release(PlayerAIBot bot,long token,ItemTaskStage result){}
    }
    static class ItemRegion {public static bool SameSpace(PlayerAgent agent,AIGraph.AIG_CourseNode node)=>true;}
    static class ItemArrivalPolicy {public static bool Interactable(float x,float y,float z,bool returning,float reach)=>x*x+z*z<=reach*reach&&Math.Abs(y)<2;}
}
