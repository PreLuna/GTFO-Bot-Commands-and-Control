using System;
using System.Reflection;
using PreLuna.Captain.Tasks;
using PreLuna.CoopBotsCustom.Combat;
using Gear;
using Player;
using Enemies;
static class KillContinuationTests {
 static int n;static void Check(bool b,string why){n++;if(!b)throw new Exception(why);}
 static KillGunEstimate G(float t,bool ready=true,bool finish=true,bool usable=true)=>new(usable,finish,ready,ready?10:0,t);
 static void Main(){
  Check(KillContinuationPolicy.Melee(20,20,2,G(.1f),true),"one fast awake hit preferred");
  Check(!KillContinuationPolicy.Melee(90,20,2,G(1),true),"high HP switches to effective gun");
  Check(KillContinuationPolicy.Melee(90,20,2,G(1,finish:false),true),"insufficient total gun ammo falls back to melee");
  Check(KillContinuationPolicy.Melee(90,20,2,G(1,usable:false),true),"ineffective range falls back to melee");
  Check(!KillContinuationPolicy.Melee(20,20,2,G(10),false),"no fabricated melee reach");
  Check(!KillContinuationPolicy.Melee(20,float.NaN,2,G(10),true),"invalid damage never authorizes melee");
  Check(!KillContinuationPolicy.Switch(G(3),G(.3f,ready:false),true,true,5),"fast equip but inadequate ready clip preserves reload");
  Check(KillContinuationPolicy.Switch(G(3),G(1),true,true,0),"adequate alternative completes target faster than reload");
  Check(!KillContinuationPolicy.Switch(G(.5f),G(1),true,true,4),"almost complete reload beats slower alternate");
  Check(!KillContinuationPolicy.Switch(G(3),G(.1f),false,false,1),"three-second normal switch window");
  Check(KillContinuationPolicy.Switch(G(3),G(1),false,false,4),"clear sustained benefit after hold window");
  Check(!KillContinuationPolicy.Switch(G(3),G(1,finish:false),false,true,4),"no switch to gun lacking reserve demand");
  Check(KillContinuationPolicy.Switch(G(3,usable:false),G(4,ready:false),false,true,0),"ineffective current gun can choose effective reload route");
  foreach(float h in new[]{float.NaN,float.PositiveInfinity,0,-1})Check(!KillContinuationPolicy.Melee(h,20,2,G(1),true),"reject invalid HP");
  var bot=new PlayerAIBot();var enemy=new EnemyAgent();
  var a=new BulletWeaponSynced{Pointer=(IntPtr)10,Facts=new(){Clip=20,Capacity=20,Reserve=100,Damage=10,Pellets=1,Interval=.1f,Reload=2,Equip=.4f,Aim=.1f,FalloffStart=20,FalloffEnd=60,FalloffFloor=.1f,Stagger=1,Cost=1}};
  var b=new BulletWeaponSynced{Pointer=(IntPtr)11,Facts=a.Facts};b.Facts.Damage=20;
  CombatAdapter.Test.Main.Weapon=a;CombatAdapter.Test.Special.Weapon=b;bot.Inventory.WieldedItem=a;
  KillCombatBridge.Install(new HarmonyLib.Harmony());Check(KillCombatBridge.Available,"actual reflected contract resolves");
  enemy.Damage.Health=40;var choice=KillCombatBridge.Evaluate(bot,enemy,null,1000);
  Check(CombatAdapter.Observed==40,"fresh remaining HP replaces stale cached 999");
  Check(choice.Weapon==a&&choice.Estimate.ReadyKill,"native evaluator retains adequate current gun");
  a.Facts.Clip=0;a.IsReloading=true;b.Facts.Clip=1;
  choice=KillCombatBridge.Evaluate(bot,enemy,a,4);Check(choice.Weapon==a,"reflected evaluator preserves reload with insufficient other clip");
  b.Facts.Clip=5;choice=KillCombatBridge.Evaluate(bot,enemy,a,0);
  Check(choice.Weapon==b&&choice.Estimate.ReadyKill,"adequate target clip allows urgent effective switch");
  a.IsReloading=false;a.Facts.Reserve=0;b.Facts.Clip=0;b.Facts.Reserve=0;
  choice=KillCombatBridge.Evaluate(bot,enemy,a,4);Check(!choice.Estimate.CanFinish,"both empty produces explicit unusable completion route");
  a.Facts.Clip=20;var action=new PlayerBotActionAttack{m_bot=bot};SingleKillAdapter.Target=enemy;SingleKillAdapter.Weapon=a;
  var hook=typeof(KillCombatBridge).GetMethod("Option",BindingFlags.Static|BindingFlags.NonPublic);
  void Option(bool result)=>hook.Invoke(null,new object[]{action,result});
  Option(false);Check(action.m_currentAttackOption.ItemToUse==null,"native failed option preserved");
  action.Invulnerable=true;Option(true);Check(action.m_currentAttackOption.ItemToUse==null,"native invulnerability gate preserved");action.Invulnerable=false;
  SingleKillAdapter.Owned=false;Option(true);Check(action.m_currentAttackOption.ItemToUse==null,"ordinary unrelated bot not overridden");SingleKillAdapter.Owned=true;
  bot.Sight=false;Option(true);Check(action.m_currentAttackOption.ItemToUse==null,"no firing override through obstacle");bot.Sight=true;
  Option(true);Check(action.m_currentAttackOption.TargetAgent==enemy&&action.m_currentAttackOption.ItemToUse==a&&action.m_fireAction.Weapon==a,"same target and selected gun update native retained fire child");
  Console.WriteLine($"PASS: {n} continuation policy/reflection adapter assertions; gun evaluator reused, no native game actions.");
 }
}
