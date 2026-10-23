using System;
using System.Reflection;
using PreLuna.CoopBotsCustom.Combat;
namespace UnityEngine { public static class Time { public static float time; } }
namespace HarmonyLib {
 public static class Priority { public const int Last=0; }
 public sealed class HarmonyMethod { public int priority; public HarmonyMethod(Type t,string n){} }
 public sealed class Harmony { public void Patch(MethodInfo m,HarmonyMethod postfix=null){} }
 public static class AccessTools {
  const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
  public static MethodInfo Method(Type t,string n,Type[] args=null)=>args==null?t.GetMethod(n,Flags):t.GetMethod(n,Flags,null,args,null);
  public static FieldInfo Field(Type t,string n)=>t.GetField(n,Flags);
 }
}
namespace Gear {
 public sealed class BulletWeaponSynced {
  public IntPtr Pointer; public bool IsReloading; public GunFacts Facts;
  public int GetCurrentClip()=>Facts.Clip;
 }
}
namespace Enemies {
 public sealed class EnemyAgent { public bool Alive=true;public readonly object gameObject=new();public DamageData Damage=new(); }
 public sealed class DamageData { public float Health=10; }
}
namespace Player {
 public sealed class PlayerAIBot { public IntPtr Pointer=(IntPtr)1;public Inventory Inventory=new();public Agent Agent=new();public bool Sight=true;
  public bool HasLineOfFireToObject(object p,object t)=>Sight; }
 public sealed class Agent { public object EyePosition=new(); }
 public sealed class Inventory { public Gear.BulletWeaponSynced WieldedItem; }
 public sealed class Fire { public Gear.BulletWeaponSynced Weapon;public bool IsTerminated()=>false; }
 public sealed class PlayerBotActionAttack {
  public PlayerAIBot m_bot;public AttackOption m_currentAttackOption=new();public Fire m_fireAction=new();public bool Invulnerable;
  public bool ChooseAttackOption()=>true;public bool IsTargetInvulnerable()=>Invulnerable;
  public enum AttackMeansEnum { Bullet, Melee }
  public sealed class AttackOption { public object TargetAgent;public AttackMeansEnum Means;public Gear.BulletWeaponSynced ItemToUse; }
 }
}
namespace PreLuna.CoopBotsCustom.Combat {
 public sealed class CombatGun { public Gear.BulletWeaponSynced Weapon; }
 public sealed class CombatState { public CombatGun Main=new(),Special=new(); }
 public static class CombatAdapter {
  public static CombatState Test=new();public static int Reads;public static float Observed;
  static CombatState State(Player.PlayerAIBot bot)=>Test;
  static void Monitor(CombatState s){}
  static void ReadGuns(CombatState s,bool force){Reads++;}
  static TargetFacts Facts(CombatState s,Enemies.EnemyAgent e)=>new(){Health=999,Distance=10,Radius=.4f,WeakDamage=1};
  static GunPlan Plan(CombatState s,CombatGun gun,TargetFacts f,Enemies.EnemyAgent e,bool current){Observed=f.Health;
   return CombatPolicy.Evaluate(gun.Weapon.Facts,f,current,current&&gun.Weapon.IsReloading,gun.Weapon.Facts.Reload); }
 }
}
namespace PreLuna.Captain.Tasks {
 internal static class KnownTypes { internal static Type Get(string a,string n)=>typeof(CombatAdapter); }
 internal static class Plugin { internal static Log Logger=new(); }
 internal sealed class Log { public void LogWarning(string t){} }
 internal static class SingleKillAdapter {
  internal static Enemies.EnemyAgent Target;internal static Gear.BulletWeaponSynced Weapon;internal static bool Owned=true;
  internal static bool TryGun(Player.PlayerBotActionAttack a,out Enemies.EnemyAgent e,out Gear.BulletWeaponSynced w){e=Target;w=Weapon;
   return Owned&&Target?.Alive==true&&a.m_bot.Sight;}
 }
}
