using System;
using System.Reflection;
using Player;
using UnityEngine;
using PreLuna.Captain.Tasks;
static class GuardMovementTests {
 static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static PlayerAIBot bot;static PlayerAgent host;static MarkedPosition point;
 static bool Allow(PlayerBotActionBase.Descriptor d)=>(bool)typeof(MovementOrders).GetMethod("Allow",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{bot,d});
 static void Reset(){MovementOrders.Reset();Time.time=0;LevelGeneration.Builder.CurrentFloor=new();PlayerManager.Current=new();
  host=new(){PlayerSlotIndex=0,Owner=new(){IsBot=false}};bot=new(){Agent=new(){PlayerSlotIndex=1}};bot.Agent.Bot=bot;bot.SyncValues.Leader=host;
  PlayerManager.Current.Slots[0]=host;PlayerManager.Current.Slots[1]=bot.Agent;bot.Agent.Position=default;
  AIGraph.AIG_CourseNode.Next=bot.Agent.CourseNode;point=new(default,bot.Agent.CourseNode,host.CourseNode,LevelGeneration.Builder.CurrentFloor.Pointer,host.Owner.Pointer,0,Vector3.forward);
  BotTaskBridge.Permit=true;BotTaskBridge.Auto=true;BotTaskBridge.Warps=0;Physics.Capsule=false;RouteTraversal.IsOpen=true;ItemTaskAdapter.Blocked.Clear();}
 static void Issue(GuardDutyKind kind=GuardDutyKind.Room){MovementOrders.GuardAt(bot,new(kind,point,3,kind==GuardDutyKind.Cover?host:null));MovementOrders.Tick();}
 static void Main(){
  Reset();Issue();Check(MovementOrders.Guard(bot)?.Kind==GuardDutyKind.Room,"实际MovementOrders进入固定守卫");int requests=bot.Requests;
  for(int i=1;i<100;i++){Time.time=i*.05f;MovementOrders.Tick();}Check(bot.Requests==requests&&BotTaskBridge.Warps==0,"固定点100帧不重发移动／传送／看向");
  BotTaskBridge.Auto=false;Check(MovementOrders.CombatAvailable(bot),"自己的租约允许战斗，不依赖自动拾取");
  bot.Agent.Position=new(2,0,0);Time.time=5;MovementOrders.Tick();Check(MovementOrders.Guard(bot)!=null&&bot.Requests==requests,"半径内不回拉");
  Check(!Allow(new PlayerBotActionTravel.Descriptor(bot){RequiredLayers=PlayerBotActionBase.AccessLayers.Legs}),"拒绝无关自主追逐位移");
  Check(Allow(new PlayerBotActionUseFirearm.Descriptor{RequiredLayers=PlayerBotActionBase.AccessLayers.RootPosition}),"枪械动作保留");
  Check(Allow(new PlayerBotActionReloadWeapon.Descriptor{RequiredLayers=PlayerBotActionBase.AccessLayers.Legs}),"换弹动作保留");
  Check(Allow(new PlayerBotActionUseBioscan.Descriptor())&&MovementOrders.Guard(bot)==null,"扫描让位、释放守卫权限");
  Time.time=6;MovementOrders.Tick();Check(MovementOrders.Guard(bot)==null,"扫描后稳定窗口不立即争抢");
  BotTaskBridge.Auto=true;Time.time=9;MovementOrders.Tick();Check(MovementOrders.Guard(bot)!=null,"稳定后重新验证并恢复职责");
  MovementOrders.Control(bot,true);MovementOrders.Tick();Check(MovementOrders.Guard(bot)==null,"暂停释放移动和选敌权限");
  MovementOrders.Control(bot,false);Time.time=12;MovementOrders.Tick();Check(MovementOrders.Guard(bot)!=null,"继续恢复固定点");
  requests=bot.Requests;MovementOrders.GuardAt(bot,new(GuardDutyKind.Room,point,3));MovementOrders.Tick();Check(bot.Requests==requests,"重复职责不重启动作");
  MovementOrders.Cancel(bot,false);MovementOrders.Tick();Check(!MovementOrders.HasDuty(bot)&&MovementOrders.Guard(bot)==null,"取消删除职责");
  Reset();point.Point=new(20,0,0);Issue();Check(bot.Last is PlayerBotActionTravel.Descriptor&&BotTaskBridge.Warps==0,"远处固定职责只走原版一次接近，不传送");requests=bot.Requests;
  for(int i=1;i<30;i++){Time.time=i*.05f;MovementOrders.Tick();}Check(bot.Requests==requests,"原版旅行有效时不反复取消请求");
  bot.Agent.Position=point.Point;Time.time=2;MovementOrders.Tick();Check(MovementOrders.Guard(bot)!=null,"到达真实点后开始守卫");
  Reset();Issue(GuardDutyKind.Cover);host.Position=new(20,0,0);Time.time=1;MovementOrders.Tick();Check(MovementOrders.Guard(bot)!=null&&MovementOrders.Guard(bot).Anchor.Point.x==0,"队友远离不重算或追随");
  PlayerManager.Current.Slots[0]=new();Time.time=2;MovementOrders.Tick();Check(!MovementOrders.HasDuty(bot),"目标玩家身份改变清理职责");
  Reset();Issue();LevelGeneration.Builder.CurrentFloor=new();Time.time=1;MovementOrders.Tick();Check(!MovementOrders.HasDuty(bot),"关卡变化清理职责");
  Reset();point.Point=new(20,0,0);RouteTraversal.IsOpen=false;Issue();Check(!MovementOrders.HasDuty(bot)&&bot.Requests==0,"普通关闭门拒绝，不穿门");
  Reset();Issue();bot.Agent.CourseNode=new(){Room=2};Time.time=1;MovementOrders.Tick();Check(MovementOrders.Guard(bot)==null,"相邻房间即使坐标很近也让位重新核验");
  Reset();Issue();Check(Allow(new PlayerBotActionRevive.Descriptor())&&MovementOrders.Guard(bot)==null,"原版救援让位而非取消守卫目标");
  Reset();Issue();Check(Allow(new PlayerBotActionDeploySentryGun.Descriptor())&&MovementOrders.Guard(bot)==null,"原版部署动作允许执行并让位");
  Reset();Issue();var holds=new System.Collections.Generic.List<BorrowHold>();MovementOrders.SuspendForBorrow(bot,holds);
  Check(holds.Count==1&&holds[0].Yielded&&MovementOrders.Guard(bot)==null,"实际守卫借调暂停并释放租约，保留职责对象");
  holds[0].Restore();Time.time=4;MovementOrders.Tick();Check(MovementOrders.Guard(bot)!=null,"借调恢复原守卫目标与活动半径");
  Reset();Issue();MovementOrders.Control(bot,true);holds.Clear();MovementOrders.SuspendForBorrow(bot,holds);holds[0].Restore();Time.time=4;MovementOrders.Tick();
  Check(MovementOrders.Guard(bot)==null&&MovementOrders.HasDuty(bot),"借调不解除玩家原先手动暂停的守卫");
  Reset();Issue();holds.Clear();MovementOrders.SuspendForBorrow(bot,holds);MovementOrders.Control(bot,true);
  Check(!holds[0].Retained,"重复手动暂停使旧借调恢复记录失效");holds[0].Restore();Time.time=4;MovementOrders.Tick();Check(MovementOrders.Guard(bot)==null,"旧借调不会覆盖玩家后来暂停");
  Reset();Issue();holds.Clear();MovementOrders.SuspendForBorrow(bot,holds);MovementOrders.Cancel(bot,false);holds[0].Restore();Time.time=4;MovementOrders.Tick();
  Check(!MovementOrders.HasDuty(bot),"取消后的守卫对象不会被借调重新创建");
  Reset();point.Point=new(20,0,0);ItemTaskAdapter.TeleportEnabled=false;MovementOrders.GoTo(bot,point);MovementOrders.Tick();holds.Clear();MovementOrders.SuspendForBorrow(bot,holds);
  Check(holds.Count==1&&holds[0].Yielded&&MovementOrders.Owns(bot),"实际前往任务暂停旅行并保留任务记录");
  holds[0].Restore();Time.time=4;MovementOrders.Tick();Check(bot.Last is PlayerBotActionTravel.Descriptor&&MovementOrders.Owns(bot),"继续原前往任务重新核验目标并请求旅行");
  Reset();bot.SyncValues.Leader=null;MovementOrders.FollowPlayer(bot,host);holds.Clear();MovementOrders.SuspendForBorrow(bot,holds);Time.time=4;MovementOrders.Tick();
  Check(bot.SyncValues.Leader==null,"借调期间待生效的跟随职责不重新指派领队");
  holds[0].Restore();MovementOrders.Tick();Check(bot.SyncValues.Leader==host,"解除借调后原跟随职责继续核验领队");
  Console.WriteLine("PASS "+checks+" actual MovementOrders guard/borrow integration assertions with native doubles; no game execution.");
 }
}



