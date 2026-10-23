using System;
using System.Collections.Generic;
using UnityEngine;
using Player;
using Enemies;
using Gear;
using PreLuna.Captain.Tasks;
#pragma warning disable CS0649
public class NativeObject { static long serial;public IntPtr Pointer=new(++serial);public T TryCast<T>() where T:class=>this as T; }
namespace UnityEngine {
 public class GameObject:NativeObject{public object Parent;public T GetComponentInParent<T>() where T:class=>Parent as T;}
 public class Transform:NativeObject{public Vector3 position;public GameObject gameObject=new();public object Component;public T GetComponent<T>() where T:class=>Component as T;}
 public class Collider{public object Parent;public T GetComponentInParent<T>() where T:class=>Parent as T;}
 public struct RaycastHit{public Collider collider;}
 public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>sqrMagnitude>.0001f?this*(1/MathF.Sqrt(sqrMagnitude)):default;public static Vector3 up=>new(0,1,0);public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);public static Vector3 operator*(Vector3 a,float n)=>new(a.x*n,a.y*n,a.z*n);public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a-b).sqrMagnitude);}
 public static class Time{public static float time;}
 public enum QueryTriggerInteraction{Ignore,Collide}
 public static class Physics{public static bool CheckCapsule(Vector3 a,Vector3 b,float r,int mask,QueryTriggerInteraction q)=>false;public static bool Raycast(Vector3 a,Vector3 b,out RaycastHit hit,float d,int mask,QueryTriggerInteraction q){hit=default;return false;}}
}
namespace BepInEx.Configuration{public class ConfigEntry<T>{public T Value;}public class ConfigFile{public ConfigEntry<T> Bind<T>(string s,string k,T v,string d)=>new(){Value=v};}}
namespace HarmonyLib{
 public class HarmonyMethod{public int priority;public HarmonyMethod(Type t,string n){}}
 public static class Priority{public const int First=800,Last=0;}
 public class Harmony{public void Patch(object m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}}
 public static class AccessTools{const System.Reflection.BindingFlags Flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;public static System.Reflection.MethodInfo Method(Type t,string n,Type[] p=null)=>p==null?t.GetMethod(n,Flags):t.GetMethod(n,Flags,null,p,null);public static System.Reflection.FieldInfo Field(Type t,string n)=>t.GetField(n,Flags);}
}
public enum eGameStateName{InLevel,Lobby}public static class GameStateManager{public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
public static class LayerManager{public const int MASK_ENEMY_DAMAGABLE=1,MASK_ENEMY=2,MASK_DEFAULT=4,MASK_PLAYER_INTERACT_BLOCKERS=8;}
namespace SNetwork{public static class SNet{public static bool IsMaster=true;}}
namespace AIGraph{public class AIG_CourseNode:NativeObject{public int Zone=1,Layer=1;public static bool TryGetCourseNode(int d,Vector3 p,int r,out AIG_CourseNode node){node=new();return true;}}}
namespace LevelGeneration{public static class Builder{public static NativeObject CurrentFloor=new();}}
namespace Enemies{
 public class EnemyAI{public int Mode=4;}
 public class EnemyDamage{public float Health=30;public bool IsImortal;public List<Dam_EnemyDamageLimb> DamageLimbs=new();}
 public class EnemyAgent:NativeObject{public bool Alive=true;public int EnemyDataID=12,DimensionIndex,Instance=19;public EnemyAI AI=new();public EnemyDamage Damage=new();public AIGraph.AIG_CourseNode CourseNode=new();public Vector3 Position=new(10,0,0);public GameObject gameObject=new();public int GetInstanceID()=>Instance;}
}
public class Dam_EnemyDamageLimb:NativeObject{public Transform transform=new();public LimbBase m_base=new();public class LimbBase{public EnemyAgent Owner;}}
namespace Gear{
 public class ItemEquippable:NativeObject{public PlayerAgent Owner;public float EquipTransitionTime=.1f;}
 public class MeleeWeaponThirdPerson:ItemEquippable{public object MeleeArchetypeData=new();}
 public class BulletWeaponSynced:ItemEquippable{public string PublicName="已评分枪械";public bool IsReloading;public int Clip=3;public int GetCurrentClip()=>Clip;}
}
namespace Player{
 public enum InventorySlot{GearMelee,GearStandard,GearSpecial}
 public class Owner:NativeObject{public bool IsBot=true;}
 public class PlayerAgent:NativeObject{public Owner Owner=new();public bool Alive=true;public int PlayerSlotIndex=1,DimensionIndex;public string PlayerName="bot";public Vector3 Position,EyePosition,TargetLookDir;public AIGraph.AIG_CourseNode CourseNode=new();public PlayerAIBot Bot;public T GetComponent<T>() where T:class=>Bot as T;}
 public class PlayerManager{public static PlayerManager Current=new();public PlayerAgent[] Slots=new PlayerAgent[4];public static PlayerAgent GetLocalPlayerAgent()=>Current.Slots[0];public PlayerAgent GetPlayerAgentInSlot(int slot)=>Slots[slot];}
 public class Inventory{public ItemEquippable WieldedItem;public bool IsEquiping;}
 public class BackpackItem{public ItemEquippable Instance;}
 public class PlayerBackpack{public MeleeWeaponThirdPerson Melee;public bool TryGetBackpackItem(InventorySlot slot,out BackpackItem item){item=new(){Instance=Melee};return Melee!=null;}}
 public class PlayerBotActionBase:NativeObject{
  public Descriptor DescBase;public enum AccessLayers{Legs=1,RootPosition=2}
  public class Descriptor:NativeObject{public enum StatusType{Queued,Active,Successful,Cancelled}public StatusType Status;public PlayerBotActionBase ActionBase,ParentActionBase;public AccessLayers RequiredLayers;public float Prio,Haste;public bool IsTerminated()=>Status is StatusType.Successful or StatusType.Cancelled;}
 }
 public class RootPlayerBotAction:PlayerBotActionBase{public PlayerAIBot m_bot;public PlayerBotActionAttack.Descriptor m_attackAction;}
 public class PlayerBotActionTravel:PlayerBotActionBase{public new class Descriptor:PlayerBotActionBase.Descriptor{public Descriptor(PlayerAIBot b){}public enum DestinationEnum{Position}public DestinationEnum DestinationType;public Vector3 DestinationPos;public float Radius,RadiusHeightTolerance;public bool Persistent;public PlayerBotActionWalk.Descriptor.PostureEnum WalkPosture;}}
 public class PlayerBotActionWalk{public class Descriptor{public enum PostureEnum{Crouch}}}
 public class PlayerBotActionMelee:PlayerBotActionBase{
  public static float[] s_chargeTimes={.1f,1};public static float s_strikeDuration=.1f;
  public bool CheckStrikeDistance()=>true;
  public new class Descriptor:PlayerBotActionBase.Descriptor{public Descriptor(PlayerAIBot b){}public enum StateEnum{Charge,Strike,Cooldown}public StateEnum State=StateEnum.Charge;public bool IsCharged=true,Strike,Loop,Travel,Push;public float Force;public MeleeWeaponThirdPerson Weapon;public EnemyAgent TargetAgent;public GameObject TargetGameObject;}
 }
 public class PlayerBotActionAttack:PlayerBotActionBase{
  public PlayerAIBot m_bot;public Descriptor m_desc;public AttackOption m_currentAttackOption=new();
  public enum AttackMeansEnum{Bullet,Melee}
  public class AttackOption{public EnemyAgent TargetAgent;public AttackMeansEnum Means;public ItemEquippable ItemToUse;}
  public new class Descriptor:PlayerBotActionBase.Descriptor{public Descriptor(PlayerAIBot b){}public EnemyAgent TargetAgent;public AttackMeansEnum Means;public bool MovementAllowed;}
 }
 public class PlayerBotActionEquipItem:PlayerBotActionBase{public new class Descriptor:PlayerBotActionBase.Descriptor{public ItemEquippable Item;}}
 public class PlayerBotActionLook:PlayerBotActionBase{public enum TargetTypeEnum{Direction}public new class Descriptor:PlayerBotActionBase.Descriptor{public Descriptor(PlayerAIBot bot,bool instant){}public TargetTypeEnum TargetType;public Vector3 TargetDir;}}
 public class PlayerBotActionReloadWeapon:PlayerBotActionBase{public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 public class PlayerBotActionUseFirearm:PlayerBotActionBase{public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 public class PlayerBotActionRevive:PlayerBotActionBase{public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 public class PlayerBotActionUseBioscan:PlayerBotActionBase{public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 public class PlayerBotActionCollectItem:PlayerBotActionBase{}
 public class PlayerBotActionUnlock:PlayerBotActionBase{}
 public class PlayerBotActionGatherDeployables:PlayerBotActionBase{}
 public class PlayerBotActionDeploySentryGun:PlayerBotActionBase{}
 public class PlayerBotActionDeployTripMine:PlayerBotActionBase{}
 public class PlayerBotActionShareResourcePack:PlayerBotActionBase{}
 public class PlayerBotActionCarryExpeditionItem:PlayerBotActionBase{}
 public class PlayerAIBot:NativeObject{
  public PlayerAgent Agent=new();public PlayerBackpack Backpack=new();public Inventory Inventory=new();public List<PlayerBotActionBase> Actions=new();public PlayerBotActionBase.Descriptor m_rootAction;public int Requests,Stops;public bool Line=true,Accept=true;
  public PlayerBotActionBase.Descriptor Last;public List<PlayerBotActionBase.Descriptor> Issued=new();
  public bool RequestAction(PlayerBotActionBase.Descriptor d){Requests++;Last=d;Issued.Add(d);if(CombatScene.Task!=null&&!CombatScene.Task.Authorizes(d))return false;if(!Accept)return false;
   d.Status=PlayerBotActionBase.Descriptor.StatusType.Active;
   if(d is PlayerBotActionTravel.Descriptor travel){Agent.Position=travel.DestinationPos;d.Status=PlayerBotActionBase.Descriptor.StatusType.Successful;d.ActionBase=new PlayerBotActionTravel();}
   else if(d is PlayerBotActionMelee.Descriptor)d.ActionBase=new PlayerBotActionMelee();
   else if(d is PlayerBotActionAttack.Descriptor attack)d.ActionBase=new PlayerBotActionAttack{m_bot=this,m_desc=attack,m_currentAttackOption=new(){ItemToUse=CombatScene.Gun,TargetAgent=attack.TargetAgent}};
   else d.ActionBase=new PlayerBotActionBase();d.ActionBase.DescBase=d;return true;}
  public void StopAction(PlayerBotActionBase.Descriptor d){Stops++;d.Status=PlayerBotActionBase.Descriptor.StatusType.Cancelled;}
  public bool HasLineOfFireToObject(Vector3 p,GameObject target)=>Line;
 }
}
namespace PreLuna.Captain{
 public static class Plugin{public static Log Logger=new();public class Log{public void LogInfo(string s){}public void LogWarning(string s){}}}
 public static class KnownTypes{public static Type Get(string assembly,string name)=>typeof(KnownTypes).Assembly.GetType(name);}
}
namespace PreLuna.Captain.Tasks{
 internal static class CombatScene{internal static bool Geometry=true,Safe=true,Execute=true,Runnable;internal static IControlCombatTask Task;internal static BulletWeaponSynced Gun;}
 internal sealed class KillStrikeScope{internal readonly MeleeWeaponThirdPerson Weapon;internal bool RequireBehind=true;internal KillStrikeScope(PlayerAgent b,EnemyAgent e,MeleeWeaponThirdPerson w){Weapon=w;}}
 internal static class KillPositionAdapter{
  internal static bool Find(PlayerAIBot b,EnemyAgent e,MeleeWeaponThirdPerson w,out Transform aim,out Vector3 p,out float d){aim=e.Damage.DamageLimbs[0].transform;p=e.Position;d=30;return CombatScene.Geometry;}
  internal static bool CanSwing(PlayerAgent b,EnemyAgent e,MeleeWeaponThirdPerson w,Dam_EnemyDamageLimb l,Vector3 p,Vector3 eye,bool requireBehind=true)=>CombatScene.Geometry;
 }
 internal static class KillMeleeAdapter{internal static void Install(HarmonyLib.Harmony h){}internal static float Preview(PlayerAgent b,EnemyAgent e,MeleeWeaponThirdPerson w,Dam_EnemyDamageLimb l,Vector3 p,Vector3 eye,bool awake=false,float force=1)=>force==0?5:30;}
 internal static class KillCombatBridge{internal sealed record Choice(BulletWeaponSynced Weapon,KillGunEstimate Estimate);internal static int Evaluations;internal static void Install(HarmonyLib.Harmony h){}internal static Choice Evaluate(PlayerAIBot b,EnemyAgent e,BulletWeaponSynced chosen,float held){Evaluations++;return new(CombatScene.Gun,new(true,true,true,1,.2f));}}
 internal static class BotTaskBridge{internal static int Acquires,Releases,Reports;internal static bool Bind()=>true;internal static bool Acquire(PlayerAIBot b,out long t){Acquires++;t=1;return true;}internal static bool Report(PlayerAIBot b,long t,ItemTaskStage s){Reports++;return true;}internal static bool CanExecute(PlayerAIBot b,long t)=>CombatScene.Execute;internal static bool Unsafe(PlayerAIBot b)=>!CombatScene.Safe;internal static void Release(PlayerAIBot b,long t,ItemTaskStage s)=>Releases++;internal static bool Teleport(PlayerAIBot b,long t,Vector3 p){b.Agent.Position=p;return true;}internal static bool ResumePosition(PlayerAIBot b,long t,out Vector3 p){p=default;return false;}}
 internal static class ItemRegion{internal static bool SameSpace(PlayerAgent b,AIGraph.AIG_CourseNode node)=>b.CourseNode.Layer==node.Layer;}
 internal static class SearchTraversal{internal static bool InScope(AIGraph.AIG_CourseNode n,AIGraph.AIG_CourseNode o,int s)=>n!=null&&o!=null&&n.Zone==o.Zone;}
 internal sealed class RoutePlan{internal bool WaitingForDoor;internal List<object> Passages=new();}
 internal static class RouteTraversal{internal static bool Opened=true;internal static RoutePlan Build(PlayerAgent b,AIGraph.AIG_CourseNode n,Vector3 p,object x,AIGraph.AIG_CourseNode o,out string reason){reason=Opened?null:"门关闭";return new(){WaitingForDoor=!Opened};}internal static bool Open(object p)=>Opened;}
 internal static class ItemTaskAdapter{internal static bool TeleportEnabled;internal static float TeleportDistance=8;internal static int Suspends;internal static List<string> Messages=new();internal static bool Busy(PlayerAIBot b)=>false;internal static bool PausedWorkBlocks(PlayerAIBot b)=>CombatScene.Runnable;internal static void SuspendForBorrow(PlayerAIBot b,List<BorrowHold> h)=>Suspends++;internal static void Feedback(string text)=>Messages.Add(text);}
 internal static class MovementOrders{internal static string Status(PlayerAIBot b)=>null;internal static bool CombatAvailable(PlayerAIBot b)=>true;}
 internal static class AutoPickupAdapter{internal static void CancelPrompt(PlayerAIBot b){}}
 internal static class RescueOrders{internal static bool Owns(PlayerAIBot b)=>false;}
 internal static class SharedMeleeBridge{internal static bool Owns(PlayerAIBot b)=>false;}
 internal static class NativePickupAdapter{internal static bool Owns(PlayerAIBot b)=>false;}
 internal static class ResourceUseAdapter{internal static bool Owns(PlayerAIBot b)=>false;}
 internal static class ToolSupplyAdapter{internal static bool Owns(PlayerAIBot b)=>false;}
 internal static class SearchTaskAdapter{internal static bool Owns(PlayerAIBot b)=>false;}
}
namespace PreLuna.CoopBotsCustom.Combat{
 internal sealed class Sight{public bool Visible=true;}
 internal sealed class StateData{public EnemyAgent Selected;public IntPtr SelectedID;public float SelectedAt,AssignedAt,SelectDue,OptionDue;public Sight Sight=new();}
 internal static class CombatAdapter{internal static StateData Data=new();internal static StateData State(PlayerAIBot b)=>Data;internal static Sight FindSight(StateData s,EnemyAgent e)=>s.Sight;public static void SelectTarget(RootPlayerBotAction r){}}
}

