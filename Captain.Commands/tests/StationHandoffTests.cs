using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Player;
using UnityEngine;
using PreLuna.Captain.Tasks;
using PreLuna.Captain.Control;
static class StationHandoffTests
{
 static int checks;static PlayerAIBot bot;static PlayerAgent host;static MarkedPosition here;static Service service=new();
 static void Check(bool ok,string msg){checks++;if(!ok)throw new Exception(msg);}
 sealed class Service:IBotControlService,IBotControlStationService{
  public bool Active;public bool Controls(long a,long o)=>Active&&bot.Agent.Pointer.ToInt64()==a&&bot.Agent.Owner.Pointer.ToInt64()==o;
  public bool RecallProtected(long a,long o)=>MovementOrders.ControlStationRecallProtected(a,o);
  public bool Begin(BotControlTarget t,string s,out long ticket,out string reason){ticket=1;reason=null;return true;}
  public BotControlState Poll(long t,out string reason){reason=null;return Active?BotControlState.Active:BotControlState.Released;}
  public bool Renew(long t)=>true;public void Release(long t,bool recall)=>Active=false;
 }
 static void Reset(){service.Active=false;MovementOrders.Reset();TaskBoard.Journal.Reset();Time.time=0;LevelGeneration.Builder.CurrentFloor=new();PlayerManager.Current=new();
  host=new(){PlayerSlotIndex=0,Owner=new(){IsBot=false},Position=new(90,0,0)};bot=new(){Agent=new(){PlayerSlotIndex=1}};bot.Agent.Bot=bot;bot.SyncValues.Leader=host;
  PlayerManager.Current.Slots[0]=host;PlayerManager.Current.Slots[1]=bot.Agent;AIGraph.AIG_CourseNode.Next=bot.Agent.CourseNode;AIGraph.AIG_CourseNode.Found=true;
  BotTaskBridge.Permit=BotTaskBridge.Auto=true;BotTaskBridge.Acquires=BotTaskBridge.Releases=BotTaskBridge.Warps=0;Physics.Capsule=false;RouteTraversal.IsOpen=true;ItemTaskAdapter.Blocked.Clear();
  here=new(default,bot.Agent.CourseNode,bot.Agent.CourseNode,LevelGeneration.Builder.CurrentFloor.Pointer,bot.Agent.Owner.Pointer,0,new(0,0,1));
 }
 static bool Allow(PlayerBotActionBase.Descriptor d)=>(bool)typeof(MovementOrders).GetMethod("Allow",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bot,d});
 static void Tick(float t){Time.time=t;MovementOrders.Tick();}
 static void Cases(){
  Check(BotControlApi.Register(service),"注册接管／驻守交接保护协议");Reset();
  var old=new MarkedPosition(default,bot.Agent.CourseNode,host.CourseNode,LevelGeneration.Builder.CurrentFloor.Pointer,host.Owner.Pointer,0,Vector3.forward);
  MovementOrders.StationAt(bot,old);Tick(0);Check(MovementOrders.HasDuty(bot),"建立接管前原驻守");MovementOrders.Control(bot,true);
  host.CourseNode.Zone=90;service.Active=true;
  Check(MovementOrders.AcceptControlStation(bot,here,out var reason),"原驻守执行器接受机器人位置，不依赖远处本体Zone："+reason);
  Check(MovementOrders.HasDuty(bot)&&MovementOrders.ControlStationRecallProtected(bot.Agent.Pointer.ToInt64(),bot.Agent.Owner.Pointer.ToInt64()),"接单记录已保存且初始召回保护生效");
  int requests=bot.Requests;Tick(.05f);Check(bot.Requests==requests&&BotControlApi.RecallProtected(bot.Agent.Pointer.ToInt64(),bot.Agent.Owner.Pointer.ToInt64()),"接管释放之前不提前移动或消耗交接保护");
  service.Active=false;Tick(.1f);Check(MovementOrders.Status(bot).Contains("保持驻守")&&!BotControlApi.RecallProtected(bot.Agent.Pointer.ToInt64(),bot.Agent.Owner.Pointer.ToInt64()),"释放后由原驻守取得权限并进入保持，交接保护自然结束");
  var rows=TaskBoard.Journal.Capture(.1f).Rows;Check(rows.Any(r=>r.State==TaskBoardState.Cancelled)&&rows.Count(r=>!r.Terminal)==1,"新驻守接替旧驻守，只有一条活跃驻守记录");
  Check(bot.SyncValues.Leader==host&&BotTaskBridge.Warps==0,"不会因驻守退出改发本体召回或立刻传送");
  Check(Allow(new PlayerBotActionUseBioscan.Descriptor()),"原驻守依旧放行扫描任务");
  BotTaskBridge.Auto=false;bot.Agent.Position=new(20,0,0);Tick(1);Check(BotTaskBridge.Warps==0,"扫描未结束时不抢回驻守点");
  BotTaskBridge.Auto=true;Tick(1.5f);Check(BotTaskBridge.Warps==0,"扫描结束后保留现有稳定等待");Tick(3.6f);Tick(3.7f);
  Check(BotTaskBridge.Warps==1&&bot.Agent.Position.x==0&&MovementOrders.Status(bot).Contains("保持驻守"),"稳定后复用原返回方式回到驻守点，目标不变成本体位置");
  Check(Allow(new PlayerBotActionRevive.Descriptor()),"原驻守依旧让位救援");BotTaskBridge.Auto=false;Tick(4);BotTaskBridge.Auto=true;Tick(5);Check(BotTaskBridge.Warps==1,"救援后的稳定等待也没有被旁路");
  Tick(7);Check(MovementOrders.HasDuty(bot),"救援结束仍由原驻守持续管理");
  ItemTaskAdapter.Blocked.Add(bot.Pointer);Tick(7.1f);Check(MovementOrders.Status(bot).Contains("临时任务优先"),"旧任务被继续后新驻守主动让位");
  ItemTaskAdapter.Blocked.Clear();Tick(8);Tick(10);Check(MovementOrders.Status(bot).Contains("保持驻守"),"旧任务完成后原驻守稳定恢复");
  MovementOrders.Control(bot,true);service.Active=true;Tick(611);Check(!MovementOrders.HasDuty(bot),"再次接管产生的手动暂停仍按原10分钟期限清理");

  Reset();service.Active=true;Physics.Capsule=true;Check(!MovementOrders.AcceptControlStation(bot,here,out _)&&!MovementOrders.HasDuty(bot)&&service.Active,"驻守站位被堵时拒绝接单，继续接管");
  Physics.Capsule=false;ItemTaskAdapter.Blocked.Add(bot.Pointer);Check(!MovementOrders.AcceptControlStation(bot,here,out _),"未完全暂停的前台任务阻止驻守交接");
  ItemTaskAdapter.Blocked.Clear();bot.Agent.Position=new(2,0,0);Check(MovementOrders.AcceptControlStation(bot,here,out _),"有效驻守目标允许距机器人超过0.75米，菜单位移校验在提交入口进行");
  Reset();service.Active=true;Check(MovementOrders.AcceptControlStation(bot,here,out _),"第二次交接接单");service.Active=false;
  bot.Agent.Owner=new();Tick(.1f);Check(!MovementOrders.HasDuty(bot)&&!BotControlApi.RecallProtected(bot.Agent.Pointer.ToInt64(),bot.Agent.Owner.Pointer.ToInt64()),"身份失效清理旧驻守及交接保护");
  MovementOrders.Reset();BotControlApi.Unregister(service);
 }
 static int Main(){try{Cases();Console.WriteLine("PASS: "+checks+" 生产驻守执行器接单／接管交接／旧驻守替换／扫描救援与稳定返回检查（原生替身）");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
