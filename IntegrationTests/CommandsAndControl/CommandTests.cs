using System;
using Player;
using LevelGeneration;
using UnityEngine;
using PreLuna.Captain.Control;
using PreLuna.Captain.Tasks;
using PreLuna.MicroMaster;
static class CommandTests
{
    static int checks;static long ticket;static PlayerAIBot bot,other;static ItemTaskAdapter.Job old;
    static BotControlTarget Target=>new(1,bot.Agent.Pointer.ToInt64(),bot.Agent.Owner.Pointer.ToInt64());
    static BotControlRay Ray=>new(20,1.6f,20,0,0,1);
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static CaptainMenuLeaf Entry(int handler=1)=>new("W01","测试",1,Target.Owner,handler,null);
    static PlayerAIBot MakeBot(int slot){var b=new PlayerAIBot{Agent=new(){PlayerSlotIndex=slot}};b.Agent.Bot=b;b.Actions.Add(new RootPlayerBotAction());PlayerManager.Current.Slots[slot]=b.Agent;return b;}
    static void Reset(){
        BotControlService.Instance.Reset();Time.unscaledTime=0;Time.Freeze=false;SNetwork.SNet.IsMaster=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;
        Builder.CurrentFloor=new();PlayerManager.Current=new();BotTaskBridge.Reset();ItemTaskAdapter.Jobs.Clear();ItemTaskAdapter.Reserved.Clear();ItemTaskAdapter.Messages.Clear();
        ItemTaskAdapter.NativeMenu=new();OpenTaskAdapter.OldReservations.Clear();ItemTaskSession.Near=true;ItemTargetResolver.Hit=null;
        PositionMarks.Standable=true;MovementOrders.Accept=true;MovementOrders.Protected=false;MovementOrders.AcceptedWhileControlled=false;
        PlayerManager.Current.Slots[0]=new(){Owner=new(){IsBot=false},EyePosition=new(100,1.6f,100),Position=new(100,0,100)};
        bot=MakeBot(1);other=MakeBot(2);old=new();ItemTaskAdapter.Jobs[bot.Pointer]=old;
        Check(BotControlApi.Begin(Target,"micro",out ticket,out _),"接管申请");Check(BotControlApi.Poll(ticket,out _)==BotControlState.Active,"激活接管");
    }
    static void Tick(float time){Time.unscaledTime=time;BotControlApi.Renew(ticket);BotControlService.Tick();}
    static ControlMenuSnapshot Mark(object target){ItemTargetResolver.Hit=target;Check(BotControlApi.Mark(ticket,Ray,out _),"机器人镜头标记");return ControlCommandAdapter.Snapshot(ticket,bot);}
    static bool Busy=>BotControlApi.CommandBusy(ticket,out _);
    static bool MicroAllowed=>ManualMotionPolicy.InputAllowed(BotControlApi.Controls(Target.Agent,Target.Owner),true,true,false,false,Busy);
    static void Cases(){
        BotControlService.Install();Reset();var item=new ItemInLevel();var saved=Mark(item);
        Check(ItemTargetResolver.LastSource==bot.Agent&&ItemTargetResolver.LastOrigin.x==20&&ItemTargetResolver.LastDirection.z==1,"射线使用机器人画面而非100米外本体准星");
        Check(saved.Item==item&&saved.Bot==Target&&saved.Ticket==ticket,"标记绑定机器人身份和接管票据");
        ControlCommandAdapter.Issue(saved,Entry() with{Slot=2,Owner=other.Agent.Owner.Pointer.ToInt64()});Check(!Busy,"禁止把接管菜单任务转给别的机器人");
        ControlCommandAdapter.Issue(saved,Entry(47));Check(!Busy,"未接入协同不能穿透接管入口");
        ControlCommandAdapter.Issue(saved,Entry());Check(Busy&&!MicroAllowed,"接单立即屏蔽WASD与鼠标，而不是等待下一次更新");
        var oldMark=ControlCommandAdapter.Snapshot(ticket,bot);ItemTargetResolver.Hit=new ItemInLevel();
        Check(!BotControlApi.Mark(ticket,Ray,out _)&&ReferenceEquals(oldMark,ControlCommandAdapter.Snapshot(ticket,bot)),"执行中重新标记被拒绝，当前目标不变");
        ControlCommandAdapter.Issue(saved,Entry());Check(ItemTaskAdapter.Reserved.Count==1,"重复提交不能建立第二条任务");
        Tick(0);Tick(.3f);Check(item.Submits==1&&Busy,"仅提交一次拾取，核验之前保持输入锁");
        Tick(.4f);Tick(.5f);Check(!Busy&&MicroAllowed,"拾取确认后恢复微调且不自动返程");
        Check(BotTaskBridge.Acquires==1&&BotTaskBridge.Releases==0,"子指令共享接管租约，未再次申请或提前释放");
        Check(ReferenceEquals(old,ItemTaskAdapter.Jobs[bot.Pointer])&&old.Paused,"旧任务原记录仍保留暂停");
        Check(!BotControlApi.CommandOwns(other.Agent.Pointer.ToInt64(),other.Agent.Owner.Pointer.ToInt64())&&other.Stops==0,"其他机器人保持工作");
        BotControlApi.Release(ticket,true);Tick(.6f);Check(BotTaskBridge.Releases==1&&bot.SyncValues.Leader==PlayerManager.GetLocalPlayerAgent()&&old.Paused,"普通退出召回跟随并保持旧任务暂停");

        Reset();saved=Mark(new object());var ground=ControlCommandAdapter.MenuSnapshot(ticket,bot);
        Check(ground.HasPosition&&ground.Station!=null&&bot.Agent.Pings==1,"地面中键记录位置、显示原生Ping并可驻守");
        ControlCommandAdapter.Issue(ground,Entry(55));Check(!BotControlApi.Controls(Target.Agent,Target.Owner)&&MovementOrders.AcceptedWhileControlled,"地面标记经Q确认驻守接单后退出");
        Reset();PositionMarks.Standable=false;saved=Mark(new object());var wall=ControlCommandAdapter.MenuSnapshot(ticket,bot);
        Check(wall.HasPosition&&wall.Station==null&&bot.Agent.Pings==1,"墙面可标记但没有落点时不回退到机器人脚下");
        Reset();saved=Mark(new object());var moved=ControlCommandAdapter.MenuSnapshot(ticket,bot);bot.Agent.Position=new(25,0,20);
        ControlCommandAdapter.Issue(moved,Entry(55));Check(BotControlApi.Controls(Target.Agent,Target.Owner)&&!MovementOrders.AcceptedWhileControlled,"菜单后机器人位移仍拒绝过时交接");
        Reset();saved=Mark(new object());Check(ControlPresentation.TryGetMark(ticket,out var visual)&&visual.CanStation,"相同机器人和票据公开现有标记，不重发Ping");
        Check(bot.Agent.Pings==1&&!ControlPresentation.TryGetMark(ticket+1,out _),"其他会话不能读取旧标记");
        Time.unscaledTime=61;Check(!ControlPresentation.TryGetMark(ticket,out _),"过期标记从展示层移除");
        Reset();PositionMarks.Standable=false;saved=Mark(new object());Check(ControlPresentation.TryGetMark(ticket,out visual)&&!visual.CanStation&&visual.Label.Contains("不可驻守"),"墙面原始命中仍可见，不需要可驻守导航点");
        Reset();var movingEnemy=new Enemies.EnemyAgent();saved=Mark(movingEnemy);movingEnemy.Position=new(8,2,3);
        Check(ControlPresentation.TryGetMark(ticket,out visual)&&visual.X==8,"敌人标记跟随同一敌人当前位置");movingEnemy.Alive=false;Check(!ControlPresentation.TryGetMark(ticket,out _),"死亡敌人不会留下有效标记");
        Reset();saved=Mark(new object());Time.unscaledTime=61;Check(ControlCommandAdapter.MenuSnapshot(ticket,bot).Station==null,"过期地面标记不能用于驻守交接");
        Reset();item=new();saved=Mark(item);item.Valid=false;ControlCommandAdapter.Issue(saved,Entry());
        Check(!Busy&&MicroAllowed&&ItemTaskAdapter.Reserved.Count==0,"菜单后目标被取走：拒绝交互并释放临时预约，不锁微调");
        Reset();item=new();saved=Mark(item);ItemTaskAdapter.Reserved.Add(item.Pointer);ControlCommandAdapter.Issue(saved,Entry());
        Check(!Busy&&ItemTaskAdapter.Reserved.Contains(item.Pointer)&&old.Paused,"旧任务和其他任务的物品预约不能被覆盖");
        Reset();saved=Mark(new ItemInLevel());ItemTargetResolver.Hit=null;Check(!BotControlApi.Mark(ticket,Ray,out _),"新的中键没有命中时失败");
        Check(ControlCommandAdapter.Snapshot(ticket,bot).Item==null,"空标记不回退到旧目标或本体准星");
        Reset();saved=Mark(new ItemInLevel());for(int i=1;i<=61;i++)Tick(i);ControlCommandAdapter.Issue(saved,Entry());
        Check(!Busy&&MicroAllowed,"原有标记期限到期时拒绝接单");

        Reset();var door=new MarkedOpenTarget();saved=Mark(door);Check(MarkedOpenTarget.ResolvedBy==bot.Agent,"门按钮的当前侧按机器人位置解析");
        ControlCommandAdapter.Issue(saved,Entry(10));Tick(0);Tick(.2f);Check(door.Submits==1&&Busy,"门提交一次但实际未开时不能恢复微调");
        Tick(.3f);Check(door.Submits==1&&Busy,"开启动画等待不会重复切换门按钮");door.IsOpen=true;Tick(.4f);
        Check(!Busy&&MicroAllowed&&old.Paused,"开门真实状态核验后原地交回微调，旧任务仍暂停");
        Reset();door=new();saved=Mark(door);OpenTaskAdapter.OldReservations.Add(door.Key);ControlCommandAdapter.Issue(saved,Entry(10));
        Check(!Busy&&door.Submits==0,"旧开启任务的预约不被接管子任务覆盖");

        Reset();item=new(){Confirm=false};saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);Tick(.3f);
        BotTaskBridge.Execute=false;Tick(.4f);Check(BotControlApi.Controls(Target.Agent,Target.Owner)&&Busy&&BotTaskBridge.LastStage==ItemTaskStage.Verifying,"核验阶段续期保留原子阶段，不被接管心跳改成移动");
        BotControlApi.Release(ticket,true);
        Check(!BotControlApi.Controls(Target.Agent,Target.Owner)&&BotTaskBridge.Releases==0&&Busy,"原子交互中退出先归还本体，租约留给收尾");
        Tick(.5f);Check(ItemTaskAdapter.NativeMenu.Calls==0&&BotControlApi.CommandOwns(Target.Agent,Target.Owner),"提交后核验尚未完成不能抢先召回");
        item.Confirm=true;BotTaskBridge.Execute=true;Tick(.6f);Check(!Busy&&BotTaskBridge.Releases==1&&ItemTaskAdapter.NativeMenu.Calls==1&&item.Submits==1,"核验完成后只释放一次、召回一次，不重新提交拾取");
        Check(old.Paused,"紧急退出也不恢复旧任务");
        Reset();door=new();saved=Mark(door);ControlCommandAdapter.Issue(saved,Entry(10));Tick(0);Tick(.2f);BotControlApi.Release(ticket,true);
        Tick(.3f);Check(!BotControlApi.Controls(Target.Agent,Target.Owner)&&Busy&&ItemTaskAdapter.NativeMenu.Calls==0,"开门提交后退出立即归还本体并等待门结果");
        door.IsOpen=true;Tick(.4f);Check(!Busy&&door.Submits==1&&ItemTaskAdapter.NativeMenu.Calls==1,"门结果收尾后召回，未再次开关门");

        Reset();ItemTaskSession.Near=false;item=new();saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);
        var travel=ItemTaskSession.Last.Travel;var parent=new PlayerBotActionBase{DescBase=travel};var walk=new PlayerBotActionBase.Descriptor{ParentActionBase=parent};
        Check(BotControlApi.AllowsAction(Target.Agent,Target.Owner,travel.Pointer.ToInt64())&&BotControlApi.AllowsAction(Target.Agent,Target.Owner,walk.Pointer.ToInt64()),"只放行当前任务的旅行及其原版子动作");
        Check(!BotControlApi.AllowsAction(Target.Agent,Target.Owner,new PlayerBotActionBase.Descriptor().Pointer.ToInt64()),"不属于指令的自主动作仍被拦截");
        Check(!BotControlApi.AllowsTeleport(Target.Agent,Target.Owner),"远距离自动跟随召回仍禁止");
        bool scoped=ControlCommandAdapter.Teleport(bot,()=>BotControlApi.AllowsTeleport(Target.Agent,Target.Owner));
        Check(scoped&&!BotControlApi.AllowsTeleport(Target.Agent,Target.Owner),"只在原有指令传送调用范围内临时放行");
        try{ControlCommandAdapter.Teleport(bot,()=>throw new Exception("warp"));}catch{}
        Check(!BotControlApi.AllowsTeleport(Target.Agent,Target.Owner),"传送异常也能撤销临时许可");
        bot.StopAcknowledged=false;BotControlApi.Release(ticket,true);Tick(.2f);
        Check(Busy&&ItemTaskAdapter.NativeMenu.Calls==0&&!BotControlApi.Controls(Target.Agent,Target.Owner),"原版停止未确认时不交回机器人微调或召回，本体已恢复");
        bot.StopAcknowledged=true;Tick(.6f);Tick(.9f);Check(!Busy&&BotTaskBridge.Releases==1&&ItemTaskAdapter.NativeMenu.Calls==1,"确认停止后结束收尾并召回");

        Reset();saved=Mark(new ItemInLevel());BotControlApi.Release(ticket,false);ControlCommandAdapter.Issue(saved,Entry());Check(!Busy,"退出后的旧菜单不能复活接管任务");
        Reset();saved=Mark(new ItemInLevel());PlayerManager.Current.Slots[1]=new(){PlayerSlotIndex=1};ControlCommandAdapter.Issue(saved,Entry());
        Check(!Busy&&!BotControlApi.Controls(Target.Agent,Target.Owner),"相同槽位的新机器人不能继承目标或控制权");
        // Fourth batch: the same scheduler and lease own combat, then hand the station to Captain.
        Reset();var enemy=new Enemies.EnemyAgent();saved=Mark(enemy);ControlCommandAdapter.Issue(saved,Entry(9));
        Check(Busy&&!MicroAllowed,"指定击杀接单后屏蔽微调");Tick(0);enemy.Alive=false;Tick(.2f);
        Check(!Busy&&MicroAllowed&&old.Paused&&BotTaskBridge.Acquires==1&&BotTaskBridge.Releases==0,"击杀完成只交回微调，旧任务暂停且租约仍属于接管");
        Reset();enemy=new(){Mode=1};saved=Mark(enemy);BotTaskBridge.UnsafeValue=true;ControlCommandAdapter.Issue(saved,Entry(43));Tick(0);
        Check(Busy&&MicroAllowed==false&&BotTaskBridge.Acquires==1,"已警觉目标的集火借用现有接管租约，不被潜行安全门槛误拒绝");enemy.Alive=false;Tick(.2f);Check(!Busy&&MicroAllowed,"集火实际完成后恢复微调");
        Reset();enemy=new();saved=Mark(enemy);ControlCommandAdapter.Issue(saved,Entry(9));Tick(0);MockCombat.Last.Atomic=true;Tick(.2f);
        BotControlApi.Release(ticket,true);Check(!BotControlApi.Controls(Target.Agent,Target.Owner)&&Busy&&BotTaskBridge.Releases==0,"挥击原子阶段退出先恢复本体，保留任务收尾");
        Tick(.3f);Check(ItemTaskAdapter.NativeMenu.Calls==0,"挥击未结束不能抢先召回");MockCombat.Last.Atomic=false;Tick(.5f);
        Check(!Busy&&BotTaskBridge.Releases==1&&ItemTaskAdapter.NativeMenu.Calls==1&&old.Paused,"挥击结束后原召回流程只执行一次");
        Reset();var here=ControlCommandAdapter.MenuSnapshot(ticket,bot);MovementOrders.Accept=false;ControlCommandAdapter.Issue(here,Entry(56));
        Check(BotControlApi.Controls(Target.Agent,Target.Owner)&&MicroAllowed&&ItemTaskAdapter.NativeMenu.Calls==0,"驻守接单拒绝时继续接管，不召回或锁住本体");
        PositionMarks.Standable=true;MovementOrders.Accept=true;ControlCommandAdapter.Issue(here,Entry(56));Check(MovementOrders.AcceptedWhileControlled,"驻守必须在接管仍持有权限时确认接单");
        Check(!BotControlApi.Controls(Target.Agent,Target.Owner)&&BotTaskBridge.Releases==1&&BotControlApi.Poll(ticket,out var outcome)==BotControlState.Released&&outcome.Contains("驻守"),"确认接单后才释放接管并记录驻守退出结果");
        BotControlApi.Release(ticket,true);Tick(.2f);Check(ItemTaskAdapter.NativeMenu.Calls==0&&old.Paused,"重复客户端退出也不能给已接单的驻守排入召回");
        Check(BotControlApi.RecallProtected(Target.Agent,Target.Owner)&&!BotControlApi.RecallProtected(other.Agent.Pointer.ToInt64(),other.Agent.Owner.Pointer.ToInt64()),"驻守交接召回保护只作用于目标机器人");
        Reset();here=ControlCommandAdapter.MenuSnapshot(ticket,bot);enemy=new();saved=Mark(enemy);ControlCommandAdapter.Issue(saved,Entry(9));ControlCommandAdapter.Issue(here,Entry(56));
        Check(Busy&&BotControlApi.Controls(Target.Agent,Target.Owner)&&!MovementOrders.AcceptedWhileControlled,"战斗任务未完成时拒绝覆盖为驻守退出");
        // Fifth batch: pre-commit cancellation, lost leases/identities, delayed verification and paused clocks.
        Reset();item=new();saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());BotControlApi.Release(ticket,true);
        Check(!BotControlApi.Controls(Target.Agent,Target.Owner),"拾取提交前退出立刻归还本体");Tick(.1f);
        Check(!Busy&&item.Submits==0&&ItemTaskAdapter.NativeMenu.Calls==1&&old.Paused,"取消未提交的拾取不消耗物品，收尾后按原流程召回");
        Reset();item=new();saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);BotControlApi.Release(ticket,true);
        Check(!BotControlApi.Controls(Target.Agent,Target.Owner)&&Busy,"已进入原有拾取原子阶段退出仍立即归还本体");Tick(.3f);Tick(.4f);
        Check(!Busy&&item.Submits==1&&ItemTaskAdapter.NativeMenu.Calls==1,"沿用原有交互窗口完成一次拾取与核验后再召回");
        Reset();item=new(){Confirm=false};saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);Tick(.3f);BotControlApi.Release(ticket,true);
        Tick(6);Check(!Busy&&item.Submits==1&&ItemTaskAdapter.Reserved.Count==0&&ItemTaskAdapter.NativeMenu.Calls==1,"已提交拾取核验超时有界退出，释放预约且不重复提交");
        Reset();door=new();saved=Mark(door);ControlCommandAdapter.Issue(saved,Entry(10));Tick(0);Tick(.2f);BotControlApi.Release(ticket,true);
        Tick(11);Check(!Busy&&door.Submits==1&&ItemTaskAdapter.NativeMenu.Calls==1,"开启动画不返回时有界失败，不再次切换门按钮");
        Reset();item=new(){Confirm=false};saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);Tick(.3f);BotTaskBridge.Renewal=false;Tick(.4f);
        Check(!BotControlApi.Controls(Target.Agent,Target.Owner)&&!Busy&&item.Submits==1&&BotTaskBridge.Releases==1,"原子交互途中租约失效即归还本体，停止后续提交并结束收尾");
        Reset();item=new(){Confirm=false};saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);Tick(.3f);BotControlApi.Release(ticket,true);
        Time.Freeze=true;Time.Scaled=.3f;Tick(36);
        Check(!Busy&&item.Submits==1&&ItemTaskAdapter.Reserved.Count==0&&!BotControlApi.CommandOwns(Target.Agent,Target.Owner),"缩放时钟冻结也不能无限占用退出后的子任务／预约");
        Check(ItemTaskAdapter.NativeMenu.Calls==1&&BotTaskBridge.Releases==1,"交互收尾超过原召回等待窗口也不丢失召回，收尾确认后才开始原30秒等待");
        Reset();item=new(){Confirm=false};saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);Tick(.3f);var previous=Target;
        SNetwork.SNet.Master=new();Check(!BotControlApi.CommandOwns(previous.Agent,previous.Owner),"房主变化立刻撤销原子子任务动作授权");Tick(.4f);
        Check(!Busy&&item.Submits==1&&ItemTaskAdapter.Reserved.Count==0&&ItemTaskAdapter.NativeMenu.Calls==0,"房主变化清理子任务，不用旧控制者召回");
        Reset();item=new(){Confirm=false};saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);Tick(.3f);previous=Target;
        var newBot=MakeBot(1);newBot.Agent.Pointer=bot.Agent.Pointer;newBot.Agent.Owner=bot.Agent.Owner;
        Check(!BotControlApi.CommandOwns(previous.Agent,previous.Owner),"同槽同地址的新实例不能继承原子子任务");Tick(.4f);
        Check(!Busy&&ItemTaskAdapter.Reserved.Count==0&&newBot.Stops==0&&ItemTaskAdapter.NativeMenu.Calls==0,"离队重加清理旧预约，不停止或召回新机器人");
        ControlCommandAdapter.Issue(saved,Entry());Check(!Busy,"离队前保存的菜单不能在重加后生效");
        Reset();item=new(){Confirm=false};saved=Mark(item);ControlCommandAdapter.Issue(saved,Entry());Tick(0);Tick(.3f);LevelGeneration.Builder.CurrentFloor=new();Tick(.4f);
        Check(!Busy&&ItemTaskAdapter.Reserved.Count==0&&ItemTaskAdapter.NativeMenu.Calls==0&&item.Submits==1,"切关中已提交交互只保留实际状态，不返程或重发");
        BotControlService.Shutdown();
    }
    static int Main(){try{Cases();Console.WriteLine("PASS: "+checks+" 生产接管服务／指令子任务／目标身份／原子退出交接检查（场景与原生交互替身）");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
