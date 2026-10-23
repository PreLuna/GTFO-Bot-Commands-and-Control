using System;
using System.Reflection;
using System.Linq;
using Player;
using LevelGeneration;
using PreLuna.Captain;
using PreLuna.Captain.Tasks;
using UnityEngine;
static class OpenAdapterTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static bool Request(PlayerAIBot bot,PlayerBotActionBase.Descriptor desc) {
        object[] args={bot,desc,false};
        bool allowed=(bool)typeof(OpenTaskAdapter).GetMethod("Request",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
        return allowed;
    }
    static PlayerAIBot CreateBot(){var bot=new PlayerAIBot();PlayerManager.Current.Bot=bot.Agent;Time.time=0;return bot;}
    static void Tick(float now){Time.time=now;OpenTaskAdapter.Tick();}
    static bool Log(string value)=>Plugin.Logger.Lines.Any(x=>x.Contains(value));
    static void Main(){
        var bot=CreateBot();var target=new MarkedOpenTarget();
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.2f);
        Check(TaskBoard.Journal.Capture(.2f).Rows.Any(r=>r.Task=="开箱／开门"&&!r.Terminal),"生产开门阶段进入看板");
        Check(target.Door.Calls==1&&!target.Door.LastOnlyUnlock&&target.Button.Calls==0,"无锁门用false调用共用接口一次，绕过按钮入口");
        Tick(.3f);Tick(.4f);Tick(1);
        Check(!OpenTaskAdapter.Owns(bot)&&target.Door.Calls==1,"无锁门确认开启并释放，不重复开关");
        Check(TaskBoard.Journal.Capture(1).Rows.Any(r=>r.State==TaskBoardState.Complete&&r.Reason.Contains("返回")),"原版确认并返回后看板保存完成原因");
        bot=CreateBot();target=new(){Lock=new()};int unlocks=0;
        bool parentless=false, childAccepted=false, descendantAccepted=false;
        PlayerBotActionMelee.Descriptor child=null;PlayerBotActionUnlock.Descriptor parent=null;
        bot.OnRequest=desc=>{
            if(desc is not PlayerBotActionUnlock.Descriptor unlock)return Request(bot,desc);
            unlocks++;parent=unlock;
            var action=new PlayerBotActionUnlock{DescBase=unlock};unlock.ActionBase=action;
            child=new(){TargetGameObject=unlock.Lock.m_meleeRoot};
            parentless=child.ParentActionBase==null&&action.m_chosenMethod==null;
            bool accepted=Request(bot,child);
            childAccepted=accepted;
            action.m_chosenMethod=child;child.ActionBase=new(){DescBase=child};
            descendantAccepted=OpenTaskAdapter.Authorizes(bot,new PlayerBotActionBase.Descriptor{ParentActionBase=child.ActionBase});
            if(accepted)unlock.Lock.Locked=false;
            return true;
        };
        OpenTaskAdapter.Issue(bot,target);Tick(0);
        Check(parentless,"模拟原版在挂父链前请求破锁子动作");
        Check(childAccepted,"回归：接纳明确选定锁的无父链近战动作");
        Check(descendantAccepted,"识别所选子动作的装备／移动后代");
        Check(unlocks==1&&target.Door.Calls==0,"物理锁门只提交Unlock，不提前请求开门");
        Check(!Request(bot,new PlayerBotActionMelee.Descriptor{TargetGameObject=new()}),"不授权无关敌人或其他锁的近战");
        Tick(.1f);Tick(.2f);Tick(.4f);Tick(.5f);Tick(.6f);
        Check(target.Door.Calls==1&&unlocks==1&&!OpenTaskAdapter.Owns(bot),"物理锁确认后只提交一次共用开门并收尾");
        Check(!OpenTaskAdapter.Authorizes(bot,child),"任务结束不遗留子动作权限");
        Check(Log("phase=door-call")&&Log("phase=door-return")&&Log("onlyUnlock=False")&&Log("submitted=True"),"共用接口调用、正确参数与提交诊断完整");
        Check(Log("phase=unlock-submit")&&Log("chosen=Melee"),"原版Unlock与chosen子动作有证据");
        bot=CreateBot();target=new(){Lock=new()};bot.OnRequest=desc=>{parent=(PlayerBotActionUnlock.Descriptor)desc;parent.Terminated=true;return true;};
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.1f);Tick(2);
        Check(!OpenTaskAdapter.Owns(bot)&&target.Door.Calls==0&&target.Lock.Locked,"当前侧锁未打开就失败，不伪造解锁或请求开门");
        Check(Log("锁具未确认解锁"),"保留开锁失败诊断");
        bot=CreateBot();target=new();target.Button.Allowed=false;
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.2f);
        Check(target.Door.Calls==0&&!OpenTaskAdapter.Owns(bot)&&Log("phase=door-rejected"),"当前侧CanInteract拒绝时未调用共用接口");
        bot=CreateBot();target=new();target.Door.Accepted=false;
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.2f);Tick(1);
        Check(target.Door.Calls==1&&OpenTaskAdapter.Owns(bot),"void请求返回但门未开，继续核验而非伪造成功");
        Tick(11);Check(target.Door.Calls==1&&!OpenTaskAdapter.Owns(bot),"同步不确认开启时超时失败，不重复请求");
        Check(TaskBoard.Journal.Capture(11).Rows.Any(r=>r.State==TaskBoardState.Failed&&r.Reason.Contains("未确认目标打开")),"开门超时保存真实失败原因，不记录为成功");
        bot=CreateBot();target=new(){Restriction="不支持的门状态"};
        OpenTaskAdapter.Issue(bot,target);
        Check(!OpenTaskAdapter.Owns(bot)&&Log("phase=reject-before-accept"),"接单前拒绝原因不再静默丢失");
        bot=CreateBot();target=new(){Lock=new(){Locked=false}};
        var farButton=new LG_DoorButton{Door=target.Door};
        var farLock=new LG_WeakLock{m_holder=farButton};target.Door.WeakLocks.Add(farLock);
        Check(!target.NeedsMelee&&target.NextLock==null,"回归：当前按钮已解锁，不把对侧物理锁当成当前任务");
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.2f);Tick(.3f);Tick(.4f);
        Check(target.Door.Calls==1&&farLock.Locked&&!OpenTaskAdapter.Owns(bot),"保留对侧锁并完成开门，不去对侧破锁");
        Check(Log("selectedSide=False")&&Log("selectedSide=True"),"两侧锁诊断保留并标出当前交互侧");
        bot=CreateBot();target=new(){Lock=new()};farButton=new(){Door=target.Door};
        target.Door.WeakLocks.Insert(0,new LG_WeakLock{Locked=false,m_holder=farButton});
        Check(target.NextLock.m_holder.Pointer==target.Button.Pointer,"反向排序仍选择当前按钮的锁");
        bot.OnRequest=desc=>{if(desc is PlayerBotActionUnlock.Descriptor u)u.Lock.Locked=false;return true;};
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.1f);Tick(.2f);Tick(.4f);Tick(.5f);Tick(.6f);
        Check(target.Door.Calls==1&&!OpenTaskAdapter.Owns(bot),"当前侧有锁仍需真正解锁后开门");
        bot=CreateBot();target=new();target.Door.WeakLocks.Add(new LG_WeakLock());
        OpenTaskAdapter.Issue(bot,target);Check(!OpenTaskAdapter.Owns(bot)&&target.Door.Calls==0,"未知锁归属拒绝接单，不猜测可忽略");
        bot=CreateBot();target=new();target.Button.OperationDelay=.6f;
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.2f);Check(target.Door.Calls==0,"保留门按钮原版操作等待时长");
        Tick(.7f);Tick(.8f);Tick(.9f);Check(target.Door.Calls==1&&!OpenTaskAdapter.Owns(bot),"等待完成后正常只提交一次");
        bot=CreateBot();target=new();target.Door.OnOpen=()=>throw new Exception("模拟请求发送后异常");
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.2f);Tick(1);
        Check(target.Door.Calls==1&&!OpenTaskAdapter.Owns(bot)&&Log("task-exception"),"开门接口异常保留诊断且不重发");
        bot=CreateBot();target=new();target.Door.Accepted=false;
        OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.2f);Tick(2);target.Door.LastStatus=eDoorStatus.Open;Tick(3);Tick(4);
        Check(target.Door.Calls==1&&!OpenTaskAdapter.Owns(bot),"异步原版开门状态到达后确认完成");
        foreach(bool busy in new[]{false,true}){
            bot=CreateBot();target=new();target.Door.m_inButtonOperation=busy;target.Button.m_enabled=!busy;
            if(!busy)target.Button.gameObject.activeInHierarchy=false;
            OpenTaskAdapter.Issue(bot,target);Tick(0);Tick(.2f);
            Check(target.Door.Calls==0&&!OpenTaskAdapter.Owns(bot),"禁用／不可见按钮或正在操作的门不能直接绕过");
        }
        var box=new LG_WeakResourceContainer{WeakLockComponent=new()};
        Check(OpenTargetLocks.Selected(box,null,null).Single()==box.WeakLockComponent,"箱柜锁判定不受门侧筛选影响");
        bot=CreateBot();target=new(){Lock=new()};parent=null;
        bot.OnRequest=desc=>{parent=(PlayerBotActionUnlock.Descriptor)desc;return true;};
        OpenTaskAdapter.Issue(bot,target);Tick(0);
        var blocker=new PlayerBotActionBase.Descriptor{Allowed=false,Prio=100000};
        bot.Actions.Add(new(){DescBase=blocker});bot.m_queuedActions.Add(blocker);
        child=new(){TargetGameObject=target.Lock.m_meleeRoot};
        typeof(OpenTaskAdapter).GetMethod("RequestResult",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{bot,child,false});
        Check(Log("nativeForbidden=True")&&Log("phase=native-blocker")&&Log("source=queued")&&Log("source=active"),"原版拒绝记录实际活动／排队动作冲突来源");
        OpenTaskAdapter.Reset();
        bot=CreateBot();box=new();target=new(){Box=box,Door=null,Button=null,Key=box.Pointer,Label="物资箱 #22"};
        box.m_intOpen.OnTrigger=()=>box.ISOpen=true;
        OpenTaskAdapter.Issue(bot,target);int reservations=OpenTaskAdapter.Count;
        OpenTaskAdapter.Issue(bot,target);
        Check(OpenTaskAdapter.Count==reservations&&Log("同一开启任务正在执行"),"相同容器重复提交不重建任务");
        var otherBot=new PlayerAIBot();OpenTaskAdapter.Issue(otherBot,target);
        Check(!OpenTaskAdapter.Owns(otherBot)&&Log("已有机器人处理"),"同一容器只能被一名机器人预约");
        Tick(0);Tick(.2f);Tick(.5f);Tick(1);
        Check(box.ISOpen&&box.m_intOpen.Calls==1&&!OpenTaskAdapter.Owns(bot),"结果开箱复用原交互一次，核验原状态后完成返回");
        bot=CreateBot();box=new(){WeakLockComponent=new(){m_lockType=2,Status=1}};target=new(){Box=box,Door=null,Button=null,Key=box.Pointer};
        OpenTaskAdapter.Issue(bot,target);
        Check(!OpenTaskAdapter.Owns(bot)&&box.m_intOpen.Calls==0,"没有熔锁器时真实接单检查拒绝入侵锁容器");
        OpenTaskAdapter.Reset();
        bot=CreateBot();target=new();OpenTaskAdapter.Issue(bot,target);Tick(0);
        var holds=new System.Collections.Generic.List<BorrowHold>();OpenTaskAdapter.SuspendForBorrow(bot,holds);
        Check(holds.Count==1&&holds[0].Yielded&&target.Door.Calls==0,"原版开门提交前借调释放租约并保留目标");
        holds[0].Restore();Tick(1);Tick(1.2f);Tick(1.4f);Tick(1.6f);
        Check(target.Door.Calls==1&&!OpenTaskAdapter.Owns(bot),"恢复原开门任务只调用一次共用接口");
        bot=CreateBot();target=new();OpenTaskAdapter.Issue(bot,target);OpenTaskAdapter.Control(bot,true);holds.Clear();OpenTaskAdapter.SuspendForBorrow(bot,holds);
        holds[0].Restore();Tick(1);Check(target.Door.Calls==0&&OpenTaskAdapter.Owns(bot),"借调不解除原先手动暂停的开门任务");
        OpenTaskAdapter.Cancel(bot,false);Tick(2);
        bot=CreateBot();target=new();OpenTaskAdapter.Issue(bot,target);holds.Clear();OpenTaskAdapter.SuspendForBorrow(bot,holds);
        OpenTaskAdapter.Control(bot,true);Check(!holds[0].Retained,"玩家重复暂停废弃旧开门恢复记录");holds[0].Restore();Tick(1);
        Check(target.Door.Calls==0,"旧开门借调不会覆盖玩家后续暂停");OpenTaskAdapter.Cancel(bot,false);Tick(2);
        Console.WriteLine($"PASS: {checks} 项生产 OpenTaskAdapter 有锁／无锁门、借调及原版子动作回归；场景／原生执行由模拟替代，仍需游戏实测。");
    }
}
