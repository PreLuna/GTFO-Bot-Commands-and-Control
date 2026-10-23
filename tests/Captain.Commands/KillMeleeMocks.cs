using System;
using System.Collections.Generic;
namespace HarmonyLib {
 public class Harmony { public void Patch(object m,HarmonyMethod prefix=null,HarmonyMethod finalizer=null){} }
 public class HarmonyMethod { public int priority; public HarmonyMethod(Type t,string n){} }
 public static class AccessTools { public static object Method(Type t,string n)=>null; }
 public static class Priority { public const int Last=0,First=800; }
}
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 up=>new(0,1,0); public float sqrMagnitude=>x*x+y*y+z*z;
  public Vector3 normalized { get { var n=MathF.Sqrt(sqrMagnitude);return n>0?this*(1/n):default; } }
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a-b).sqrMagnitude);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 }
 public struct Quaternion { float angle; public static Quaternion Euler(float a,float y,float c)=>new(){angle=y*MathF.PI/180};
  public static Vector3 operator *(Quaternion q,Vector3 p)=>new(p.x*MathF.Cos(q.angle)+p.z*MathF.Sin(q.angle),p.y,p.z*MathF.Cos(q.angle)-p.x*MathF.Sin(q.angle)); }
 public class GameObject { public object Component; public T GetComponent<T>() where T:class=>Component as T;public T GetComponentInParent<T>() where T:class=>Component as T; }
 public class Transform { public Vector3 position,forward=new(0,0,1); public GameObject gameObject=new(); }
 public enum QueryTriggerInteraction {Ignore}
 public static class Physics {
  public static Func<Vector3,bool> BlockStand=p=>false; public static Func<Vector3,Vector3,bool> BlockRay=(a,b)=>false;
  public static bool CheckCapsule(Vector3 a,Vector3 b,float radius,int mask,QueryTriggerInteraction q)=>BlockStand(a-Vector3.up*.35f);
  public static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction q)=>BlockRay(a,b);
 }
 public static class Time { public static float time; }
 public static class Mathf {public static float Lerp(float a,float b,float t)=>a+(b-a)*Math.Clamp(t,0,1);}
}
namespace Agents {public class Agent {static long next; public IntPtr Pointer=new(++next); public UnityEngine.Vector3 Position;}}
namespace Player {
 public class PlayerAgent:Agents.Agent { public string PlayerName="TestBot";public bool Alive=true;public float MeleeBuffTimer;public OwnerData Owner=new();public UnityEngine.Vector3 EyePosition=>Position+new UnityEngine.Vector3(0,1,0); }
 public class OwnerData { public bool IsBot=true; }
 public class PlayerAIBot {public PlayerAgent Agent=new();public static bool Nav=true;public static UnityEngine.Vector3? OverrideSnap;
  public static bool SnapPositionToNav(UnityEngine.Vector3 p,out UnityEngine.Vector3 result,float r,float h){result=OverrideSnap??p;return Nav;} }
 public class PlayerBotActionMelee {public static float s_strikeMaxDistanceSQ=4;}
}
namespace Enemies {
 public enum ES_StateEnum {Hibernate,Alert}
 public class LocomotionState {public ES_StateEnum CurrentStateEnum=ES_StateEnum.Hibernate;}
 public class EnemyAgent:Agents.Agent {
  public bool Alive=true;public Dam_EnemyDamageBase Damage;public UnityEngine.Transform transform=new();public LocomotionState Locomotion=new();
  public EnemyAgent(){Damage=new(){Owner=this};}
 }
}
namespace Gear {
 public class MeleeWeaponThirdPerson {static long next=10000;public IntPtr Pointer=new(++next);public Player.PlayerAgent Owner;public Archetype MeleeArchetypeData=new();}
 public class Archetype {public float LightPrecisionMulti=1,CameraDamageRayLength=2,AttackSphereRadius=.2f; public float ChargedAttackDamage=999,ChargedPrecisionMulti=9,ChargedSleeperMulti=99;}
}
public enum AgentModifier {MeleeDamage=200,MeleeResistance=6}
public static class AgentModifierManager {
 public static float DamageMultiplier=1,Resistance=1;public static int SourceCalls;
 public static float ApplyModifier(Agents.Agent a,AgentModifier m,float value){if(m==AgentModifier.MeleeDamage){SourceCalls++;return value*DamageMultiplier;}return value*Resistance;}
}
public enum eLimbDamageType {Normal,Weakspot,Armor}
public enum DamageNoiseLevel {Normal,Silent}
public static class LayerManager {public const int MASK_DEFAULT=1,MASK_PLAYER_INTERACT_BLOCKERS=2;}
public class Dam_EnemyDamageBase {
 public Enemies.EnemyAgent Owner;public bool IsImortal;public float Health=500,DamageMax=500;
 public List<Dam_EnemyDamageLimb> DamageLimbs=new();
 public static float RoundDamage(float d)=>(float)Math.Round(d,2,MidpointRounding.ToEven);
}
public class Dam_EnemyDamageLimb {
 static long next=20000; public IntPtr Pointer=new(++next);public bool IsDestroyed,ThrowAfter;public int Calls,m_limbID;
 public Dam_EnemyDamageBase m_base;public eLimbDamageType m_type;public float Weak=1,Armor=1,Behind=2;public UnityEngine.Transform transform=new();
 public object[] Args; public float Applied;
 public float ApplyWeakspotAndArmorModifiers(float dam,float precision)=>dam*(m_type==eLimbDamageType.Weakspot?Math.Max(1,Weak*precision):Weak)*Armor;
 public float ApplyDamageFromBehindBonus(float d,UnityEngine.Vector3 p,UnityEngine.Vector3 direction,float back)=>d*Behind*back;
 public void MeleeDamage(float dam,Agents.Agent source,UnityEngine.Vector3 position,UnityEngine.Vector3 direction,float stagger,float precision,float environment,float back,float sleeper,bool skip,DamageNoiseLevel noise,uint gear){
  if(!PreLuna.Captain.Tasks.KillMeleeAdapter.Hit(this,dam,source,position,direction,stagger,precision,environment,back,sleeper,skip,noise,gear))return;
  Calls++;Args=new object[]{dam,source,position,direction,stagger,precision,environment,back,sleeper,skip,noise,gear};
  float damage=dam*(m_type==eLimbDamageType.Weakspot?Math.Max(1,Weak*precision):Weak)*Armor*Behind*back*(m_base.Owner.Locomotion.CurrentStateEnum==Enemies.ES_StateEnum.Hibernate?sleeper:1);
  damage=(float)Math.Round(damage,2,MidpointRounding.ToEven);damage=Math.Clamp(damage,0,m_base.DamageMax);
  damage=(float)Math.Round(damage,2,MidpointRounding.ToEven)*AgentModifierManager.Resistance;
  Applied+=damage;m_base.Health-=damage;if(ThrowAfter)throw new Exception("partial native dispatch");
 }
}
namespace SNetwork {
 public static class SNet {public static bool IsMaster=true;}
 public struct UFloat16 {float v;public void Set(float d,float max){v=Math.Clamp(d,0,max);}public float Get(float max)=>v;}
}
namespace PreLuna.Captain {
 public static class Plugin {public static TestLog Logger=new();}
 public class TestLog {public void LogInfo(string s){}public void LogWarning(string s){} }
}
namespace PreLuna.Captain.Tasks {
 internal static class SingleKillAdapter {
  internal static KillStrikeScope Current;
  internal static bool TryStrikeScope(Gear.MeleeWeaponThirdPerson weapon,out KillStrikeScope scope){scope=Current;return scope!=null&&scope.Weapon.Pointer==weapon.Pointer;}
 }
}
