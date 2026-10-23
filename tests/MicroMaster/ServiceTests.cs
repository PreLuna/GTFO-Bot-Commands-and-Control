using System;
using Player;
using PreLuna.Captain.Control;
using PreLuna.Captain.Tasks;
using UnityEngine;
static class ServiceTests
{
    static int checks; static PlayerAIBot bot, other; static PlayerAgent local;
    static BotControlTarget Target => new(1, bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64());
    static void Check(bool ok,string message) { checks++; if (!ok) throw new Exception(message); }
    static void Reset() {
        BotControlService.Instance.Reset(); Time.unscaledTime = 0; SNetwork.SNet.IsMaster = true; GameStateManager.CurrentStateName = eGameStateName.InLevel;
        LevelGeneration.Builder.CurrentFloor = new(); PlayerManager.Current = new(); BotTaskBridge.Reset(); PUIWorld.StopThrows = false;
        local = new() { PlayerSlotIndex = 0, Owner = new() { IsBot = false } }; PlayerManager.Current.Slots[0] = local;
        bot = MakeBot(1); other = MakeBot(2); ItemTaskAdapter.Jobs.Clear(); ItemTaskAdapter.NativeMenu = new(); MovementOrders.FollowPaused = false;
    }
    static PlayerAIBot MakeBot(int slot) { var b = new PlayerAIBot { Agent = new() { PlayerSlotIndex = slot } }; b.Agent.Bot = b; b.Actions.Add(new RootPlayerBotAction()); PlayerManager.Current.Slots[slot] = b.Agent; return b; }
    static long Begin() { Check(BotControlService.Instance.Begin(Target,"micro",out var ticket,out var reason),"申请应接受："+reason); return ticket; }
    static void Main() {
        Reset(); SNetwork.SNet.IsMaster = false;
        Check(!BotControlService.Instance.Begin(Target,"micro",out _,out _) && !MovementOrders.FollowPaused,"非房主不暂停任务");
        Reset(); bot.Agent.Owner.IsBot = false;
        Check(!BotControlService.Instance.Begin(Target,"micro",out _,out _),"真人不可接管");
        Reset(); bot.Agent.CourseNode = new object(); Check(!BotControlService.Instance.Begin(Target,"micro",out _,out _),"跨空间拒绝");
        Reset(); BotTaskBridge.UnsafeValue = true; Check(!BotControlService.Instance.Begin(Target,"micro",out _,out _),"首版危险机器人拒绝");
        Reset(); bot.Actions.Add(new PlayerBotActionBase()); Check(!BotControlService.Instance.Begin(Target,"micro",out _,out _),"未知原版动作拒绝");
        Reset(); bot.Actions.Add(new PlayerBotActionFollow()); bot.Actions.Add(new PlayerBotActionIdle()); bot.Actions.Add(new PlayerBotActionTurn());
        long routineTicket = Begin(); Check(BotControlService.Instance.Poll(routineTicket,out _) == BotControlState.Active && bot.Stops == 3,"普通跟随／空闲／转向动作可以停止后接管");
        Reset(); var job = new ItemTaskAdapter.Job { Yielded = false }; ItemTaskAdapter.Jobs[bot.Pointer] = job; long ticket = Begin();
        Check(job.Paused && BotControlService.Instance.Poll(ticket,out _) == BotControlState.Waiting && !BotControlService.Instance.Controls(Target.Agent,Target.Owner),"交互收尾期间不激活控制");
        BotControlService.Instance.Release(ticket,true); BotControlService.Tick(); Check(!job.Paused && !MovementOrders.FollowPaused && ItemTaskAdapter.NativeMenu.Calls == 0,"等待取消回滚旧任务且不召回");
        Reset(); job = new(); ItemTaskAdapter.Jobs[bot.Pointer] = job; bot.Actions.Add(new PlayerBotActionTravel()); bot.m_queuedActions.Add(new PlayerBotActionBase.Descriptor()); ticket = Begin();
        Check(BotControlService.Instance.Poll(ticket,out _) == BotControlState.Active,"获得租约后激活");
        Check(bot.Stops == 2 && other.Stops == 0,"仅停止被接管机器人的活动和排队动作");
        Check(BotControlService.Instance.Controls(Target.Agent,Target.Owner) && !BotControlService.Instance.Controls(other.Agent.Pointer.ToInt64(),other.Agent.Owner.Pointer.ToInt64()),"其他机器人控制状态不受影响");
        Check(!BotControlService.Instance.Begin(new(2,other.Agent.Pointer.ToInt64(),other.Agent.Owner.Pointer.ToInt64()),"micro",out _,out _),"单会话拒绝重复占用");
        BotControlService.Instance.Release(ticket,true); Check(job.Paused && MovementOrders.FollowPaused && BotTaskBridge.Releases == 1,"正常退出保留原记录暂停并释放租约");
        BotControlService.Tick(); Check(bot.SyncValues.Leader == local && ItemTaskAdapter.NativeMenu.SawScope && !BotControlService.RecallScope,"原版跟随调用处于保留旧任务的交接作用域");
        Check(other.SyncValues.Leader == null,"召回不影响其他机器人");
        BotControlService.Instance.Release(ticket,true); BotControlService.Tick(); Check(ItemTaskAdapter.NativeMenu.Calls == 1,"重复退出不重复下达跟随");
        Reset(); job = new(); ItemTaskAdapter.Jobs[bot.Pointer] = job; BotTaskBridge.Permit = false; ticket = Begin();
        for(int i=0;i<=8;i++) { Time.unscaledTime=i; BotControlService.Instance.Renew(ticket); BotControlService.Tick(); }
        Check(!job.Paused && !MovementOrders.FollowPaused && BotControlService.Instance.Poll(ticket,out _) == BotControlState.Failed,"获取租约超时恢复旧任务");
        Reset(); job = new(); ItemTaskAdapter.Jobs[bot.Pointer] = job; bot.Actions.Add(new PlayerBotActionWalk()); PUIWorld.StopThrows = true; ticket = Begin();
        try { BotControlService.Instance.Poll(ticket,out _); } catch { }
        Check(!job.Paused && BotTaskBridge.Releases == 1 && !BotControlService.Instance.Controls(Target.Agent,Target.Owner),"原生停止异常无残留控制");
        Reset(); ticket = Begin(); BotControlService.Instance.Poll(ticket,out _); var old = Target;
        PlayerManager.Current.Slots[1] = new() { PlayerSlotIndex = 1 }; Time.unscaledTime = 1; BotControlService.Tick();
        Check(!BotControlService.Instance.Controls(old.Agent,old.Owner) && ItemTaskAdapter.NativeMenu.Calls == 0,"同槽新身份不继承控制或召回");
        Reset(); ticket = Begin(); BotControlService.Instance.Poll(ticket,out _); Time.unscaledTime = 4; BotControlService.Tick();
        Check(BotControlService.Instance.Poll(ticket,out _) == BotControlState.Failed && BotTaskBridge.Releases == 1,"插件心跳停止自动清理");
        Reset(); job = new() { Paused = true }; ItemTaskAdapter.Jobs[bot.Pointer] = job; ticket = Begin(); BotControlService.Instance.Release(ticket,false);
        Check(job.Paused,"失败回滚不恢复原本手动暂停的任务");
        Reset(); ticket=Begin(); BotControlService.Instance.Poll(ticket,out _); local.Alive=false; BotControlService.Instance.Release(ticket,true); BotControlService.Tick();
        Check(!BotControlService.Instance.Controls(Target.Agent,Target.Owner) && ItemTaskAdapter.NativeMenu.Calls==0,"本体倒地立即释放，跟随请求等可用本体");
        local.Alive=true; Time.unscaledTime=1; BotControlService.Tick(); Check(ItemTaskAdapter.NativeMenu.Calls==1,"本体恢复后安全执行待召回");
        Reset();ticket=Begin();BotControlService.Instance.Poll(ticket,out _);var savedTarget=Target;
        SNetwork.SNet.Master=new();Check(!BotControlService.Instance.Controls(savedTarget.Agent,savedTarget.Owner),"房主对象改变即使IsMaster短暂仍真也撤销控制");BotControlService.Tick();
        Check(BotControlService.Instance.Poll(ticket,out _)==BotControlState.Failed&&ItemTaskAdapter.NativeMenu.Calls==0,"房主迁移不发旧房主召回");
        Reset();ticket=Begin();BotControlService.Instance.Poll(ticket,out _);savedTarget=Target;
        var replacement=MakeBot(1);replacement.Agent.Pointer=bot.Agent.Pointer;replacement.Agent.Owner=bot.Agent.Owner;
        Check(!BotControlService.Instance.Controls(savedTarget.Agent,savedTarget.Owner),"离队重加即使原生地址和Owner复用也不能继承会话");BotControlService.Tick();
        Check(ItemTaskAdapter.NativeMenu.Calls==0&&replacement.Stops==0,"新实例不被旧会话停动作或召回");
        Reset();ticket=Begin();BotControlService.Instance.Poll(ticket,out _);savedTarget=Target;bot.Agent.Owner.IsBot=false;
        Check(!BotControlService.Instance.Controls(savedTarget.Agent,savedTarget.Owner),"机器人转为真人时立即放弃控制");BotControlService.Tick();Check(ItemTaskAdapter.NativeMenu.Calls==0,"真人替代不能继承召回");
        Reset();ticket=Begin();BotControlService.Instance.Poll(ticket,out _);savedTarget=Target;
        var newLocal=new PlayerAgent{Pointer=local.Pointer,Owner=local.Owner};PlayerManager.Current.Slots[0]=newLocal;
        Check(!BotControlService.Instance.Controls(savedTarget.Agent,savedTarget.Owner),"本体实例重建不能继承控制");BotControlService.Tick();Check(ItemTaskAdapter.NativeMenu.Calls==0,"本体重建不使用旧本体召回");
        Reset();ticket=Begin();BotControlService.Instance.Poll(ticket,out _);savedTarget=Target;LevelGeneration.Builder.CurrentFloor=new();BotControlService.Tick();
        Check(!BotControlService.Instance.Controls(savedTarget.Agent,savedTarget.Owner)&&ItemTaskAdapter.NativeMenu.Calls==0,"楼层替换后清理而不召回");
        Reset();ticket=Begin();BotControlService.Instance.Poll(ticket,out _);savedTarget=Target;BotTaskBridge.Renewal=false;Time.unscaledTime=.1f;BotControlService.Tick();
        Check(!BotControlService.Instance.Controls(savedTarget.Agent,savedTarget.Owner)&&BotTaskBridge.Releases==1,"仲裁续租失效释放会话且只清理本机租约");
        BotControlService.Instance.Reset(); Console.WriteLine("PASS: "+checks+" 生产接管服务身份／暂停／失败回滚／退出跟随检查（原生接口替身）");
    }
}
