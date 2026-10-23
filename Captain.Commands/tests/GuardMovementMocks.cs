using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
public class FakeObject { static int next;public IntPtr Pointer=new(++next);public T TryCast<T>() where T:class=>this as T; }
namespace UnityEngine {
 public static class Time { public static float time; }
 public class Object:FakeObject { public static readonly List<object> Objects=new();public static T[] FindObjectsOfType<T>(){var result=new List<T>();foreach(var x in Objects)if(x is T value)result.Add(value);return result.ToArray();} }
 public class GameObject:FakeObject {}
 public class Transform { public Vector3 position; }
 public class Component:Object { public Transform transform=new(); }
 public class Collider { public object Parent; public T GetComponentInParent<T>() where T:class=>Parent as T; }
 public struct RaycastHit { public Collider collider; }
 public enum QueryTriggerInteraction { Ignore,Collide }
 public static class Physics {
  public static bool Blocked;public static Collider Hit;public static bool Capsule;public static bool CheckCapsule(Vector3 a,Vector3 b,float r,int mask,QueryTriggerInteraction q)=>Capsule;
  public static bool Linecast(Vector3 a,Vector3 b,out RaycastHit hit,int mask,QueryTriggerInteraction query){hit=new(){collider=Hit};return Blocked;}
  public static bool Raycast(Vector3 a,Vector3 b,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction query){hit=new(){collider=Hit};return Hit!=null;}
 }
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>MathF.Sqrt(sqrMagnitude);public static Vector3 forward=>new(0,0,1);public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 up=>new(0,1,0);public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float f)=>new(a.x*f,a.y*f,a.z*f);
  public Vector3 normalized{get{float n=Distance(this,default);return n>0?this*(1/n):default;}}
  public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
 }
}
static class LayerManager { public const int MASK_DEFAULT=1,MASK_PLAYER_INTERACT_BLOCKERS=2,MASK_ENEMY_DAMAGABLE=4,MASK_ENEMY=8; }
namespace AIGraph {class AIG_CourseNode:FakeObject{public int Zone=1,Room=1;public static bool Found=true;public static AIG_CourseNode Next=new();public static bool TryGetCourseNode(int dimension,Vector3 point,int r,out AIG_CourseNode node){node=Next;return Found;}} }
namespace LevelGeneration { static class Builder {public static FakeObject CurrentFloor=new();} }
namespace SNetwork {static class SNet{public static bool IsMaster=true;} }
enum eGameStateName {InLevel,Lobby}
static class GameStateManager {public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
class AIDangerZone {public enum SeverityEnum {Harmful}}
enum eResourceContainerSpawnType {Health,AmmoWeapon,AmmoTool}
namespace HarmonyLib {
 static class Priority {public const int First=800,Last=0;}
 class HarmonyMethod {public int priority;public HarmonyMethod(Type t,string n){} }
 class Harmony {public void Patch(MethodInfo m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){if(m==null)throw new Exception("missing patch target");}}
 static class AccessTools {
  const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
  public static MethodInfo Method(Type t,string n,Type[] parameters=null)=>parameters==null?t.GetMethod(n,All):t.GetMethod(n,All,null,parameters,null);
  public static FieldInfo Field(Type t,string n)=>t.GetField(n,All);
  public static PropertyInfo Property(Type t,string n)=>t.GetProperty(n,All);
 }
}
namespace Gear {
 class Item:UnityEngine.Component {public string PublicName="工具";public object Get_pItemData()=>this;}
 class ItemEquippable:Item {}
 class ItemInLevel:Item {}
 class ResourcePackPickup:ItemInLevel {public eResourceContainerSpawnType m_packType=eResourceContainerSpawnType.AmmoTool;}
 class MeleeWeaponFirstPerson:ItemEquippable {}
 class SentryGunInstance_Sync:UnityEngine.Component {public int Calls;public float Last;public void ForceReliableAmmoUpdate(float ammo){Calls++;Last=ammo;} }
 class SentryGunInstance:Item {
  public Player.PlayerAgent Owner;public AIGraph.AIG_CourseNode CourseNode=new();public bool Alive=true,m_isDeployed=true;public float Ammo=10,AmmoMaxCap=100;public UnityEngine.Component m_syncComp=new SentryGunInstance_Sync();
  public bool NeedToolAmmo()=>Ammo<AmmoMaxCap;
  public void GiveAmmoRel(Player.PlayerAgent source,float standard,float special,float tool){Ammo=Math.Min(AmmoMaxCap,Ammo+tool);}
 }
 class ResourcePackFirstPerson {
  public static bool Consume=true,Increase=true;public static int Calls;
  public static void ApplyPackBot(Player.PlayerAgent source,Player.PlayerAgent target,ItemEquippable item){
   Calls++;if(!PreLuna.Captain.Tasks.ToolSupplyAdapter.Authorizes(source,target,item))return;
   if(Consume)source.Bot.Backpack.AmmoStorage.GetInventorySlotAmmo(Player.InventorySlot.ResourcePack).AmmoInPack-=1;
   if(Increase)target.GiveAmmoRel(source,0,0,20);
  }
 }
}
namespace Enemies {
 class Target {public FakeObject m_agent;}class AI {public int Mode=1;public Target Target=new();}class EnemyDamage {public bool IsImortal;}
 class EnemyAgent:UnityEngine.Object {public bool Alive=true;public AI AI=new();public EnemyDamage Damage=new();public uint EnemyDataID=1;public AIGraph.AIG_CourseNode CourseNode=new();public Vector3 Position;public GameObject gameObject=new();public int InstanceID;public int GetInstanceID()=>InstanceID;}
 class Dam_EnemyDamageLimb {public Base m_base;public class Base{public EnemyAgent Owner;}}
}
namespace Player {
 enum InventorySlot {None,GearClass,ResourcePack}
 class Owner:FakeObject {public bool IsBot=true;}
 class Damage {public bool GrabbedAtAll;}
 class Inventory {public Gear.ItemEquippable WieldedItem;public bool IsEquiping;}
 class PlayerAgent:FakeObject {
  public Owner Owner=new();public bool Alive=true;public Damage Damage=new();public AIGraph.AIG_CourseNode CourseNode=new();public int DimensionIndex;public int PlayerSlotIndex;public string PlayerName="bot";public PlayerAIBot Bot;public Transform transform=new();public Vector3 Position,EyePosition,TargetLookDir;public Inventory Inventory;
  public T GetComponent<T>() where T:class=>Bot as T;
  public bool NeedToolAmmo()=>PlayerBackpackManager.Bags[Owner.Pointer].AmmoStorage.GetInventorySlotAmmo(InventorySlot.GearClass).AmmoInPack<100;
  public void GiveAmmoRel(PlayerAgent source,float standard,float special,float tool){
   var method=typeof(PreLuna.Captain.Tasks.ToolSupplyAdapter).GetMethod("RedirectAmmo",BindingFlags.NonPublic|BindingFlags.Static);
   if((bool)method.Invoke(null,new object[]{this,source,standard,special,tool}))PlayerBackpackManager.Bags[Owner.Pointer].AmmoStorage.GetInventorySlotAmmo(InventorySlot.GearClass).AmmoInPack+=tool;
  }
 }
 class PlayerManager {public static PlayerManager Current=new();public PlayerAgent[] Slots=new PlayerAgent[4];public PlayerAgent GetPlayerAgentInSlot(int i)=>i>=0&&i<4?Slots[i]:null;public static PlayerAgent GetLocalPlayerAgent()=>Current.Slots[0];}
 class Ammo {public float AmmoInPack=4,CostOfBullet=1;}
 class PlayerAmmoStorage {public Dictionary<InventorySlot,Ammo> Ammo=new();public Ammo GetInventorySlotAmmo(InventorySlot slot)=>Ammo.TryGetValue(slot,out var a)?a:null;public int GetClipAmmoFromSlot(InventorySlot slot)=>0;}
 class BackpackItem {public Gear.Item Instance;}
 class PlayerBackpack {public PlayerAmmoStorage AmmoStorage=new();public Dictionary<InventorySlot,BackpackItem> Held=new();public Gear.Item Deployed;public bool IsDeployed(InventorySlot slot)=>Deployed!=null;public bool TryGetBackpackItem(InventorySlot slot,out BackpackItem item)=>Held.TryGetValue(slot,out item);public bool TryGetDeployedItem(InventorySlot slot,out Gear.Item item){item=Deployed;return item!=null;} }
 static class PlayerBackpackManager {public static Dictionary<IntPtr,PlayerBackpack> Bags=new();public static Dictionary<object,Gear.ItemInLevel> Items=new();public static bool TryGetBackpack(Owner owner,out PlayerBackpack bag)=>Bags.TryGetValue(owner.Pointer,out bag);public static bool TryGetItemInLevelFromItemData(object data,out Gear.ItemInLevel world)=>Items.TryGetValue(data,out world);}
 class PlayerBotActionBase:FakeObject {
  public enum AccessLayers {None=0,Legs=1,RootPosition=2}public PlayerBotActionBase.Descriptor DescBase;public class Descriptor:FakeObject {public AccessLayers RequiredLayers;public PlayerBotActionBase ParentActionBase;public PlayerBotActionBase ActionBase;public bool Terminated;public float Prio=10;public bool IsTerminated()=>Terminated;}
 }
 class PlayerBotActionTravel:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor {public Descriptor(PlayerAIBot bot){}public enum DestinationEnum {Position}public DestinationEnum DestinationType;public float Haste,Radius,RadiusHeightTolerance;public Vector3 DestinationPos;public bool Persistent;}}
 class PlayerBotActionEquipItem:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor {public Descriptor(PlayerAIBot bot){}public Gear.ItemEquippable Item;public float Haste;}}
 class PlayerBotActionAttack:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor {public Enemies.EnemyAgent TargetAgent;}}
 class AttackDescriptor:PlayerBotActionAttack.Descriptor {}
 class RootPlayerBotAction:PlayerBotActionBase {public PlayerAIBot m_bot;public AttackDescriptor m_attackAction=new();}
 class PlayerAIBot:FakeObject {public List<PlayerBotActionBase> Actions=new();public Sync SyncValues=new();public class Sync {public PlayerAgent Leader;}
  public PlayerAgent Agent;public PlayerBackpack Backpack;public Inventory Inventory=new();public PlayerBotActionBase.Descriptor m_rootAction;public bool Danger=true,Line=true,Accept=true;public int Requests,Stops;public PlayerBotActionBase.Descriptor Last;
  public bool TestDangerZones(Vector3 p,AIDangerZone.SeverityEnum s,out int unused){unused=0;return Danger;}
  public bool HasLineOfFireToObject(Vector3 p,GameObject g)=>Line;
  public static bool Snap=true;public static Vector3 Offset;public static bool SnapPositionToNav(Vector3 p,out Vector3 output,float r,int i){output=p+Offset;return Snap;}
  public bool RequestAction(PlayerBotActionBase.Descriptor d){Requests++;Last=d;return Accept;}
  public void StopAction(PlayerBotActionBase.Descriptor d){Stops++;d.Terminated=true;}
 }
}
namespace PreLuna.Captain {
 static class Plugin {public const string Guid="captain";public static Log Logger=new();public static Dictionary Catalog=new();public class Log{public void LogWarning(string s){}public void LogInfo(string s){} }public class Dictionary{public string TranslateItems(string s)=>s;} }
 static class KnownTypes {public static Type Get(string assembly,string name)=>typeof(KnownTypes).Assembly.GetType(name);}
}
namespace PreLuna.Captain.Tasks {
 enum ItemTaskStage {Moving,Interacting,Complete,Cancelled,Failed,Paused,Waiting,Returning}
 sealed record CaptainMenuBot(int Slot,long Owner,string Label,bool Alive);
 sealed record CaptainMenuGuardPoint(int Id,string Label,string Blocked); sealed record CaptainMenuGuardCover(int Slot,long Owner,string Label,string Blocked);sealed record CaptainMenuGuardTargets(IReadOnlyList<CaptainMenuGuardPoint> Rooms,CaptainMenuGuardPoint Entrance,IReadOnlyList<CaptainMenuGuardCover> Covers);
 static class BotTaskBridge {
  public static bool Permit=true,Auto=true;public static int Warps;public static bool Teleport(Player.PlayerAIBot b,long t,Vector3 p){Warps++;b.Agent.Position=p;return true;}public static bool ResumePosition(Player.PlayerAIBot b,long t,out Vector3 p){p=default;return false;}public static int Releases,Acquires;public static bool Acquire(Player.PlayerAIBot b,out long token,bool explicitCommand=true){token=++Acquires;return Permit;}public static bool CanExecute(Player.PlayerAIBot b,long t)=>Permit;public static bool AutoAvailable(Player.PlayerAIBot b)=>Auto;public static bool AutoAvailable(Player.PlayerAIBot b,out string reason){reason=Auto?null:"protected";return Auto;}public static bool Report(Player.PlayerAIBot b,long t,ItemTaskStage s)=>Permit;public static void Release(Player.PlayerAIBot b,long t,ItemTaskStage s){Releases++;}
 }
 static class ItemRegion {public static bool SameSpace(Player.PlayerAgent a,AIGraph.AIG_CourseNode n)=>a?.CourseNode?.Zone==n?.Zone;}
 static class SearchTraversal {public static bool Standing(Player.PlayerAgent b,Vector3 p,AIGraph.AIG_CourseNode n,out Vector3 landing){landing=p;return true;}public static bool InScope(AIGraph.AIG_CourseNode n,AIGraph.AIG_CourseNode o,int s)=>n!=null&&o!=null&&n.Zone==o.Zone&&(s!=0||n.Room==o.Room);}
 sealed record RoutePlan(bool WaitingForDoor){public List<object> Passages=new();}
 static class RouteTraversal {public static bool IsOpen=true;public static bool Open(object p)=>IsOpen;public static bool OpenPortal(object p)=>IsOpen;public static RoutePlan Build(Player.PlayerAgent a,AIGraph.AIG_CourseNode n,Vector3 p,object d,AIGraph.AIG_CourseNode o,out string reason){reason=IsOpen?null:"closed";return IsOpen?new(false):new(true);} }
 static class AutoPickupAdapter {public static void CancelPrompt(Player.PlayerAIBot b){} }
  sealed class MarkedPosition {public Vector3 Point,Direction;public AIGraph.AIG_CourseNode Node,Origin;public IntPtr Floor,Issuer;public float At;public string Label=>"point";
 public MarkedPosition(Vector3 p,AIGraph.AIG_CourseNode n,AIGraph.AIG_CourseNode o,IntPtr f,IntPtr i,float at,Vector3 d){Point=p;Node=n;Origin=o;Floor=f;Issuer=i;At=at;Direction=d;}}
 static class PositionMarks {public static void Configure(BepInEx.Configuration.ConfigFile c){}public static void Install(HarmonyLib.Harmony h){}public static void Reset(){}public static string Reason(MarkedPosition p)=>Reason(p,Player.PlayerManager.GetLocalPlayerAgent());public static string Reason(MarkedPosition p,Player.PlayerAgent source)=>p==null?"未标记":p.Floor!=LevelGeneration.Builder.CurrentFloor?.Pointer?"关卡改变":p.Issuer!=source?.Owner?.Pointer?"指挥身份改变":Time.time-p.At>120?"快照过期":!SearchTraversal.InScope(p.Node,p.Origin,1)?"不同区域":null;}
 static class ResourceUseAdapter {public static bool Owns(Player.PlayerAIBot b)=>false;}
 static class SingleKillAdapter {public static bool Owns(Player.PlayerAIBot b)=>false;public static bool Sleeping(Enemies.EnemyAgent e)=>e?.Alive==true&&e.AI.Mode==4;}
 static class ItemTaskAdapter {public static PUI_CommunicationMenu NativeMenu=>PUI_CommunicationMenu.Current;public static bool TeleportEnabled=true;public static float TeleportDistance=8;public static readonly HashSet<IntPtr> Blocked=new();public static readonly List<string> Messages=new();public static bool Busy(Player.PlayerAIBot b)=>Blocked.Contains(b.Pointer)||ToolSupplyAdapter.Owns(b);public static bool PausedWorkBlocks(Player.PlayerAIBot b)=>Busy(b);public static string BusyReason(Player.PlayerAIBot b)=>Busy(b)?"busy":null;public static void Feedback(string s)=>Messages.Add(s);}
}
namespace PreLuna.CoopBotsCustom.Combat {
 class Sight {public Enemies.EnemyAgent Enemy;public bool Visible=true;}
 class StateData {public Enemies.EnemyAgent Selected;public IntPtr SelectedID;public float SelectedAt,AssignedAt,SelectDue,OptionDue;public List<Sight> Sight=new();public int Count;public float SightAt;public object Guns=new();}
 static class CombatAdapter {
  public static readonly Dictionary<IntPtr,StateData> States=new();
  static StateData State(Player.PlayerAIBot b){if(!States.TryGetValue(b.Pointer,out var s))States[b.Pointer]=s=new();return s;}
  static Sight FindSight(StateData s,Enemies.EnemyAgent e)=>Time.time-s.SightAt>.4f?null:s.Sight.Find(x=>x.Enemy==e);
  public static void SelectTarget(Player.RootPlayerBotAction r){}
 }
}
namespace ChatterReborn.Components {
 class AutoCommunicator:UnityEngine.Object {public Player.PlayerAgent LocalPlayer{get;set;}public Enemies.EnemyAgent m_targetAgent;}
}
namespace PreLuna.CoopBotsCustom.Reliability {
 class Recorder {public Player.PlayerAIBot m_bot;}
 class Member {public Player.PlayerAIBot Bot;public bool IsReady;}
 class Session {public Player.PlayerAgent Source;public Enemies.EnemyAgent Target;public Member[] Members=new Member[3];public int Count;public bool Announced,Released;}
 static class SyncMelee {
  public static Session session;public static int Preparations,Swings,Cancels;
  public static bool Owns(Player.PlayerAIBot b){if(session==null)return false;foreach(var m in session.Members)if(m?.Bot==b)return true;return false;}
  static bool Eligible(Recorder r,Player.PlayerAgent p,Enemies.EnemyAgent e)=>true;
  static bool Ready(Member m)=>m.IsReady;
  public static void BeforeCountdown(object chatter){
   Preparations++;var c=(ChatterReborn.Components.AutoCommunicator)chatter;var next=new Session{Source=c.LocalPlayer,Target=c.m_targetAgent};
   foreach(var p in Player.PlayerManager.Current.Slots){if(p?.Owner?.IsBot!=true)continue;var r=new Recorder{m_bot=p.Bot};object[] args={r,Eligible(r,c.LocalPlayer,c.m_targetAgent)};
    typeof(PreLuna.Captain.Tasks.SharedMeleeBridge).GetMethod("FilterEligibility",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    if((bool)args[1])next.Members[next.Count++]=new(){Bot=p.Bot};
   }session=next.Count>0?next:null;
  }
  public static void PlayerSwing(Gear.MeleeWeaponFirstPerson w){Swings++;if(session?.Announced==true)session.Released=true;}
  public static void Cancel(string s,bool feedback){Cancels++;session=null;}
 }
}



namespace BepInEx.Configuration {class ConfigEntry<T>{public T Value;public ConfigEntry(T v){Value=v;}}class ConfigFile{public ConfigEntry<T> Bind<T>(string a,string b,T v,string c)=>new(v);}}
class PUI_CommunicationMenu {public static readonly PUI_CommunicationMenu Current=new();public bool ExecuteCmdCall(Player.PlayerAgent source,Player.PlayerAgent target){target.Bot.SyncValues.Leader=source;return true;}}
namespace Player {
 class PlayerBotActionLook:PlayerBotActionBase {public enum TargetTypeEnum {Direction}public new class Descriptor:PlayerBotActionBase.Descriptor{public Descriptor(PlayerAIBot b,bool p){}public TargetTypeEnum TargetType;public Vector3 TargetDir;public float Haste;}}
 class PlayerBotActionWalk:PlayerBotActionBase {}
 class PlayerBotActionRevive:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionShareResourcePack:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionCollectItem:PlayerBotActionBase {}
 class PlayerBotActionCarryExpeditionItem:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionUnlock:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionDeploySentryGun:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionDeployTripMine:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionGatherDeployables:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionUseBioscan:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionUseFirearm:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionMelee:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
 class PlayerBotActionReloadWeapon:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor{}}
}



