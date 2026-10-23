using System;
using System.Linq;
using Player;
using Enemies;
using Gear;
using UnityEngine;
using PreLuna.Captain.Tasks;
static class NativeCombatTests
{
 static int checks;static PlayerAIBot bot;static EnemyAgent enemy;static Permit permit;static RootPlayerBotAction root;static PlayerBotActionAttack.Descriptor original;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 sealed class Permit:IControlCommandPermit{public int Acquires,Holds,Reports;public ItemTaskStage Stage;public bool Valid=true;
  public bool IdentityValid=>Valid;
  public bool Acquire(out long token){Acquires++;token=777;return Valid;}public bool Report(ItemTaskStage stage){Reports++;Stage=stage;return Valid;}public void Hold()=>Holds++;}
 static void Reset(int mode=4,float hp=30){
  CombatScene.Task=null;SingleKillAdapter.Reset();FocusOrders.Reset();Time.time=0;LevelGeneration.Builder.CurrentFloor=new();PlayerManager.Current=new();
  CombatScene.Geometry=CombatScene.Safe=CombatScene.Execute=true;CombatScene.Runnable=false;RouteTraversal.Opened=true;
  ItemTaskAdapter.Suspends=0;BotTaskBridge.Acquires=BotTaskBridge.Releases=BotTaskBridge.Reports=0;KillCombatBridge.Evaluations=0;
  var host=new PlayerAgent{PlayerSlotIndex=0,Owner=new(){IsBot=false}};host.CourseNode.Zone=90;PlayerManager.Current.Slots[0]=host;
  bot=new();bot.Agent.Bot=bot;PlayerManager.Current.Slots[1]=bot.Agent;bot.Backpack.Melee=new(){Owner=bot.Agent};bot.Inventory.WieldedItem=bot.Backpack.Melee;
  root=new(){m_bot=bot};original=new(bot);root.m_attackAction=original;bot.m_rootAction=new(){ActionBase=root};root.DescBase=bot.m_rootAction;bot.Actions.Add(root);
  CombatScene.Gun=new(){Owner=bot.Agent};enemy=new(){AI=new(){Mode=mode},Damage=new(){Health=hp}};
  var limb=new Dam_EnemyDamageLimb();limb.m_base.Owner=enemy;limb.transform.Component=limb;enemy.Damage.DamageLimbs.Add(limb);
  permit=new();PreLuna.CoopBotsCustom.Combat.CombatAdapter.Data=new();FocusOrders.Install(new HarmonyLib.Harmony());
 }
 static IControlCombatTask Kill(){var task=SingleKillAdapter.CreateControlled(bot,enemy,permit,out var reason);Check(task!=null,"接管指定击杀接单："+reason);CombatScene.Task=task;return task;}
 static IControlCombatTask Focus(){var task=FocusOrders.CreateControlled(bot,enemy,permit,out var reason);Check(task!=null,"接管集火接单："+reason);CombatScene.Task=task;return task;}
 static void Tick(IControlCombatTask task,float now){Time.time=now;task.Tick(now);}
 static PlayerBotActionMelee.Descriptor Strike(IControlCombatTask task){Tick(task,0);Tick(task,.1f);Tick(task,.2f);var melee=bot.Issued.OfType<PlayerBotActionMelee.Descriptor>().Last();Check(melee.Strike,"沿用真实SingleKillRunner蓄力后释放一次挥击");return melee;}
 static void Cases(){
  Reset();Check(SingleKillAdapter.ControlReason(bot,enemy,out _) == null,"击杀资格以机器人Zone为准，远处本体不参与准星判断");
  var task=Kill();var melee=Strike(task);Check(permit.Acquires==1&&BotTaskBridge.Acquires==0&&ItemTaskAdapter.Suspends==0,"实际击杀适配器共享租约，不再借调或暂停旧任务");
  Check(SingleKillAdapter.TryStrikeScope(bot.Backpack.Melee,out var scope)&&scope.Weapon==bot.Backpack.Melee,"指定击杀继续使用原近战伤害与命中范围");
  Check(permit.Stage==ItemTaskStage.Verifying,"挥击使用原子核验阶段");enemy.Alive=false;enemy.Damage.Health=0;Tick(task,.3f);
  Check(task.Finished&&SingleKillAdapter.Count==0&&permit.Holds==1&&BotTaskBridge.Releases==0,"真实死亡后交回共享权限，不建立借调返程或释放接管租约");
  Reset();task=Kill();Tick(task,0);Tick(task,.1f);Check(task.Authorizes(new PlayerBotActionEquipItem.Descriptor{ParentActionBase=root,Item=bot.Backpack.Melee}),"蓄力前只允许所选近战武器的根装备请求");
  Check(!task.Authorizes(new PlayerBotActionEquipItem.Descriptor{ParentActionBase=root,Item=CombatScene.Gun}),"准备潜行击杀时不能被闲置切枪覆盖");task.Cancel();

  Reset();task=Kill();melee=Strike(task);task.Cancel();Check(!task.Finished&&permit.Holds==0,"紧急退出不能中断已经释放的原生挥击");Tick(task,.3f);Check(!task.Finished,"挥击尚未结束时继续等待");
  melee.Status=PlayerBotActionBase.Descriptor.StatusType.Successful;Tick(task,.4f);Check(task.Finished&&permit.Holds==1&&SingleKillAdapter.Count==0,"挥击结束取消，交回协调器收尾");

  Reset(hp:100);task=Kill();melee=Strike(task);melee.Status=PlayerBotActionBase.Descriptor.StatusType.Successful;enemy.AI.Mode=1;enemy.Damage.Health=70;Tick(task,.4f);
  var attack=bot.Issued.OfType<PlayerBotActionAttack.Descriptor>().Last();Check(KillCombatBridge.Evaluations>0&&attack.TargetAgent==enemy&&!attack.MovementAllowed,"首击存活沿用原两把枪评分与目标攻击，不无界自主追逐");
  Check(root.m_attackAction==attack,"把明确攻击交给已有战斗／换弹上下文");bot.Inventory.WieldedItem=CombatScene.Gun;
  var equip=new PlayerBotActionEquipItem.Descriptor{Item=CombatScene.Gun,ParentActionBase=root};
  Check(task.Authorizes(equip),"仅为已评分的枪放行根动作下的选枪请求");
  Check(!task.Authorizes(new PlayerBotActionEquipItem.Descriptor{Item=new BulletWeaponSynced{Owner=bot.Agent},ParentActionBase=root}),"不放行无关闲置切枪");
  var reload=new PlayerBotActionReloadWeapon.Descriptor{ParentActionBase=root};Check(task.Authorizes(reload),"放行该已装备枪的原生换弹");
  Check(!task.Authorizes(new PlayerBotActionTravel.Descriptor(bot){ParentActionBase=root}),"依然屏蔽不属于任务的自主跟随移动");
  var fire=new PlayerBotActionUseFirearm{DescBase=new PlayerBotActionUseFirearm.Descriptor{ParentActionBase=attack.ActionBase}};
  Check(task.Authorizes(fire.DescBase)&&task.Authorizes(new PlayerBotActionReloadWeapon.Descriptor{ParentActionBase=fire}),"原生开火、瞄准与换弹子动作可以沿本次攻击继承授权");
  var nativeAttack=attack.ActionBase.TryCast<PlayerBotActionAttack>();Check(SingleKillAdapter.TryGun(nativeAttack,out var target,out var gun)&&target==enemy&&gun==CombatScene.Gun,"旧枪械选项补丁仍识别接管击杀的真实目标与枪");
  enemy.Alive=false;Tick(task,.5f);Check(task.Finished&&root.m_attackAction==original&&BotTaskBridge.Releases==0,"接战结束恢复原根动作上下文后交回微调");

  Reset();RouteTraversal.Opened=false;Check(SingleKillAdapter.CreateControlled(bot,enemy,permit,out _) == null&&bot.Requests==0,"关闭通路拒绝击杀，不穿门传送");
  Reset();task=Kill();CombatScene.Geometry=false;Tick(task,0);Tick(task,.1f);Tick(task,.2f);Check(task.Finished&&!bot.Issued.OfType<PlayerBotActionMelee.Descriptor>().Any(d=>d.Strike),"挥击前几何失效不释放攻击");
  Reset();task=Kill();enemy.Instance++;Tick(task,0);Check(task.Finished&&bot.Requests==0,"同一个敌人引用的身份变化也会终止");

  Reset(1,80);Check(FocusOrders.ControlReason(bot,enemy)==null,"已有集火接口以机器人Zone检查目标");
  task=Focus();PreLuna.CoopBotsCustom.Combat.CombatAdapter.Data.Sight.Visible=false;Tick(task,0);Check(!bot.Issued.OfType<PlayerBotActionAttack.Descriptor>().Any()&&bot.Issued.OfType<PlayerBotActionLook.Descriptor>().Any(),"原有探测未确认时只用原版观察转向，不提前提交枪械动作");
  enemy.Position=new(10,0,10);int lookRequests=bot.Requests;Tick(task,.1f);Check(bot.Requests==lookRequests&&bot.Issued.OfType<PlayerBotActionLook.Descriptor>().Last().TargetDir.z>.5f,"更新原观察描述符跟踪移动目标，不重复创建观察动作");
  PreLuna.CoopBotsCustom.Combat.CombatAdapter.Data.Sight.Visible=true;Tick(task,.2f);attack=bot.Issued.OfType<PlayerBotActionAttack.Descriptor>().Last();
  Check(attack.TargetAgent==enemy&&root.m_attackAction==attack,"实际集火提交与共享根攻击使用同一目标");
  var data=PreLuna.CoopBotsCustom.Combat.CombatAdapter.Data;Check(data.Selected==enemy&&data.SelectedID==enemy.Pointer,"复用FocusOrders提交已有战斗评分字段");
  int requests=bot.Requests;Tick(task,.3f);Check(bot.Requests==requests,"不每帧重发攻击或重启换弹");
  CombatScene.Gun.IsReloading=true;Check(task.Phase.Contains("换弹"),"等待原版换弹完成，保留指令状态");
  Check(task.Authorizes(new PlayerBotActionEquipItem.Descriptor{Item=CombatScene.Gun,ParentActionBase=root}),"集火仅允许所选武器的装备链");
  CombatScene.Gun.IsReloading=false;enemy.Damage.Health=0;Tick(task,.4f);Check(!task.Finished,"生命归零还需实际死亡标志确认");enemy.Alive=false;Tick(task,.5f);
  Check(task.Finished&&!FocusOrders.HasState&&root.m_attackAction==original&&permit.Holds==1,"集火死亡核验后清除自己记录，原位交回微调");

  Reset(1);task=Focus();Tick(task,0);bot.Line=false;requests=bot.Requests;Tick(task,.1f);Check(bot.Requests==requests&&!task.Finished,"遮挡时停止攻击并等待，不穿墙开火");
  Tick(task,61);Check(task.Finished&&!FocusOrders.HasState&&root.m_attackAction==original,"长期无伤害按失败返回微调，不永久占用");
  Reset(1);task=Focus();Tick(task,0);task.Cancel();Check(task.Finished&&!FocusOrders.HasState&&permit.Holds==1,"退出停止集火并恢复共享上下文");
  Reset(1);task=Focus();Tick(task,0);enemy.Instance++;Tick(task,.1f);Check(task.Finished&&!FocusOrders.HasState,"集火敌人身份改变停止旧指令");
  Reset(1);CombatScene.Task=null;var unused=new PlayerBotActionAttack.Descriptor(bot);var binding=new ControlCombatRoot(bot);binding.Bind(unused);
  PlayerManager.Current.Slots[1]=new(){PlayerSlotIndex=1};binding.Restore();Check(root.m_attackAction==unused,"身份失效时不写回新会话的根动作字段");
  Reset();PlayerManager.GetLocalPlayerAgent().CourseNode.Zone=1;SingleKillAdapter.Issue(bot,enemy);
  Time.time=0;SingleKillAdapter.Tick();Time.time=.1f;SingleKillAdapter.Tick();Time.time=.2f;SingleKillAdapter.Tick();
  Check(ItemTaskAdapter.Suspends==1&&BotTaskBridge.Acquires==1&&bot.Issued.OfType<PlayerBotActionMelee.Descriptor>().Any(d=>d.Strike),"非接管指定击杀仍走原借调与原租约路径");
  enemy.Alive=false;Time.time=.3f;SingleKillAdapter.Tick();Check(SingleKillAdapter.Count==0&&BotTaskBridge.Releases==1,"普通击杀保留原释放与结果收尾");
  Reset(1);PlayerManager.GetLocalPlayerAgent().CourseNode.Zone=1;FocusOrders.Issue(bot,enemy);
  typeof(FocusOrders).GetMethod("SelectTarget",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{root});
  Check(root.m_attackAction.TargetAgent==enemy&&bot.Requests==0,"非接管集火仍仅选择目标，不自行创建高优先级攻击");
  enemy.Alive=false;FocusOrders.Tick();Check(!FocusOrders.HasState,"普通集火继续按原生命周期清理");
 }
 static int Main(){try{Cases();Console.WriteLine("PASS: "+checks+" 生产指定击杀／集火适配器／武器与换弹授权／实际死亡／共享租约检查（原生场景替身）");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
