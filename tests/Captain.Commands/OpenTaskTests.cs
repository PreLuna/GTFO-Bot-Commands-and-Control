using System;
using PreLuna.Captain.Tasks;
static class OpenTaskTests
{
    sealed class Session : IOpenTaskSession
    {
        public bool Valid { get; set; } = true;
        public string Restriction { get; set; }
        public bool IsOpen { get; set; }
        public bool IsOpening { get; set; }
        public bool CanExecute { get; set; } = true;
        public bool NeedsUnlock { get; set; }
        public string CapabilityError { get; set; }
        public bool LockResolved { get; set; }
        public bool UnlockAtomic { get; set; }
        public bool UnlockFailed { get; set; }
        public float OpenDuration { get; set; } = 1;
        public bool Available = true, Lease = true, Near, Home, UnlockAccepted = true, OpenAccepted = true;
        public int Moves, Returns, Stops, Unlocks, Opens, Finishes, Releases, LockFinishes;
        public OpenTaskStage Result;
        bool acquired;
        public bool Acquire() { acquired = Available;return acquired; }
        public bool Renew(OpenTaskStage stage) => Lease;
        public bool AtDestination(bool returning) => returning ? Home : Near;
        public bool Move(bool returning) { if(returning)Returns++;else Moves++;return true; }
        public void StopMovement() => Stops++;
        public void Suspend() { StopMovement();if(acquired){Releases++;acquired=false;} }
        public bool BeginUnlock() { Unlocks++;return UnlockAccepted; }
        public void FinishUnlock() => LockFinishes++;
        public bool ApplyOpen() { Opens++;return OpenAccepted; }
        public void Finish(OpenTaskStage result,string reason) { Result=result;Finishes++;if(acquired)Releases++; }
    }
    static int checks;
    static void Check(bool value,string reason) { checks++;if(!value)throw new Exception(reason); }
    static OpenTaskRunner Arrive(Session s) { var r=new OpenTaskRunner(s,0);s.Near=true;r.Tick(0);return r; }
    static void Main()
    {
        Check(OpenTaskPolicy.UnlockChild(true,true,false,false,false,false),"Unlock所选的精确子动作允许");
        Check(OpenTaskPolicy.UnlockChild(true,false,false,false,true,true),"针对选定锁的熔锁子动作允许");
        Check(!OpenTaskPolicy.UnlockChild(true,false,false,false,true,false),"其他锁的熔锁动作拒绝");
        Check(!OpenTaskPolicy.UnlockChild(true,false,false,true,false,false),"普通非近战动作不能借用锁目标权限");
        Check(OpenTaskPolicy.UnlockChild(true,false,true,true,false,false),"父链未挂接但明确目标锁的近战允许");
        Check(!OpenTaskPolicy.UnlockChild(true,false,true,false,false,false),"其他近战目标不借用破锁权限");
        Check(!OpenTaskPolicy.UnlockChild(false,true,true,true,true,true),"终止后的解锁不遗留子动作权限");
        Check(OpenTaskPolicy.Lock(false,0,1)==OpenLockKind.None,"已解锁优先于锁类型");
        Check(OpenTaskPolicy.Lock(true,0,1)==OpenLockKind.Melee,"物理弱锁");
        Check(OpenTaskPolicy.Lock(true,1,2)==OpenLockKind.Hackable,"入侵锁");
        Check(OpenTaskPolicy.Lock(true,2,2)==OpenLockKind.Melting,"已贴熔锁器只等待");
        Check(OpenTaskPolicy.Lock(false,2,2)==OpenLockKind.Melting,"即便原版锁标志提前解除也等待熔锁实际状态完成");
        Check(OpenTaskPolicy.Lock(true,1,1)==OpenLockKind.Unsupported,"不一致锁状态不猜测");
        Check(OpenTaskPolicy.Lock(true,8,1)==OpenLockKind.Unsupported,"未知锁状态");
        Check(OpenTaskPolicy.CanUse(true,false,0,false,0),"无锁无需工具");
        Check(!OpenTaskPolicy.CanUse(false,true,5,true,1),"倒地不可执行");
        Check(!OpenTaskPolicy.CanUse(true,false,5,true,1),"物理锁需要近战装备");
        Check(!OpenTaskPolicy.CanUse(true,true,1,false,2),"多个入侵锁检查总余量");
        Check(OpenTaskPolicy.CanUse(true,true,2,true,2),"能力满足两个锁");
        Check(OpenTaskPolicy.Fresh(60,0,60),"标记期限边界");
        foreach(float now in new[]{61,-1,float.NaN,float.PositiveInfinity})Check(!OpenTaskPolicy.Fresh(now,0,60),"过期及异常时钟拒绝");
        var s=new Session();var r=new OpenTaskRunner(s,0);r.Tick(0);
        Check(r.Stage==OpenTaskStage.Moving&&s.Opens==0,"未到位只接近");
        s.Near=true;r.Tick(1);Check(r.Stage==OpenTaskStage.Opening&&s.Opens==0,"无锁到位先等待交互时长");
        r.Tick(1.5f);Check(s.Opens==0,"不提前开启");
        r.Tick(2);Check(r.Stage==OpenTaskStage.Verifying&&s.Opens==1,"只提交一次原版开启");
        for(int i=0;i<10;i++)r.Tick(2+i*.1f);
        Check(s.Opens==1&&!r.Finished,"等待同步不重复按钮");
        s.IsOpen=true;r.Tick(4);Check(r.Stage==OpenTaskStage.Returning&&s.Opens==1,"真实开启才返程");
        s.Home=true;r.Tick(5);Check(r.Stage==OpenTaskStage.Complete&&s.Releases==1,"完成释放权限");
        r.Tick(6);r.Cancel(false);r.Abort("late");Check(s.Finishes==1,"完成后清理幂等");
        s=new(){NeedsUnlock=true};r=Arrive(s);Check(r.Stage==OpenTaskStage.Unlocking&&s.Unlocks==1,"先调用一次开锁");
        for(int i=1;i<10;i++)r.Tick(i*.1f);
        Check(s.Unlocks==1&&s.Opens==0,"开锁期间不重发、不提前开箱");
        s.LockResolved=true;s.NeedsUnlock=false;r.Tick(2);Check(r.Stage==OpenTaskStage.Moving,"真实解锁后重新接近开启交互");
        r.Tick(3);r.Tick(4);Check(s.Opens==1,"解锁后开启一次");
        s=new(){NeedsUnlock=true,UnlockFailed=true};r=Arrive(s);r.Tick(1);Check(r.Stage==OpenTaskStage.Failed&&s.Unlocks==1&&s.Opens==0,"原生动作结束但锁没开不伪造成功");
        s=new(){NeedsUnlock=true};r=Arrive(s);r.Tick(30);Check(r.Finished&&s.Unlocks==1,"开锁有限超时且不重试");
        s=new(){NeedsUnlock=true,UnlockAccepted=false};r=Arrive(s);Check(r.Finished&&s.Unlocks==1,"原版拒绝不重发");
        s=new(){CapabilityError="no melter"};r=Arrive(s);Check(r.Finished&&s.Unlocks==0,"提交时重新检查工具");
        s=new(){NeedsUnlock=true};r=Arrive(s);s.LockResolved=true;r.Tick(1);s.LockResolved=false;s.CapabilityError="empty";r.Tick(2);
        Check(r.Finished&&s.Unlocks==1,"第一锁消耗后检查剩余工具，禁止第二锁凭空消耗");
        s=new(){OpenAccepted=false};r=Arrive(s);r.Tick(1);Check(r.Finished&&s.Opens==1,"开启被拒绝只尝试一次");
        s=new();r=Arrive(s);r.Tick(1);r.Tick(11);Check(r.Finished&&s.Opens==1,"同步超时不重开或误关门");
        s=new(){IsOpening=true};r=Arrive(s);Check(r.Stage==OpenTaskStage.Verifying&&s.Opens==0,"玩家已经按开门则只等动画，不重复按钮");
        s.IsOpening=false;s.IsOpen=true;r.Tick(1);s.Home=true;r.Tick(2);Check(r.Stage==OpenTaskStage.Complete&&s.Opens==0,"外部开门核验后返程");
        s=new(){NeedsUnlock=true,UnlockAtomic=true};r=Arrive(s);r.Cancel(false);r.Tick(1);
        Check(!r.Finished,"应用熔锁器期间保留原子窗口");
        s.UnlockAtomic=false;r.Tick(2);Check(r.Stage==OpenTaskStage.Cancelled&&s.Opens==0,"原子动作结束取消，保留真实锁状态");
        s=new();r=Arrive(s);r.Tick(1);r.Cancel(false);r.Tick(2);Check(!r.Finished,"开启提交后核验不能因普通取消丢失");
        s.IsOpen=true;r.Tick(3);Check(r.Stage==OpenTaskStage.Cancelled&&s.Opens==1,"取消仍核验已提交开启");
        s=new();r=new(s,0);r.Cancel(false);Check(r.Finished&&s.Releases==0,"等待前取消不释放别人的令牌");
        s=new();r=new(s,0);r.Tick(0);r.Cancel(true);r.Tick(1);Check(r.Stage==OpenTaskStage.Returning&&s.Opens==0,"取消并返回不打开目标");
        s.Home=true;r.Tick(2);Check(r.Stage==OpenTaskStage.Cancelled,"取消返程收尾");
        s=new(){Available=false};r=new(s,0);r.Tick(0);r.Tick(15);Check(r.Finished&&s.Releases==0,"权限等待超时不碰别人的控制权");
        foreach(int failure in new[]{0,1,2,3,4}) {
            s=new();r=new(s,0);r.Tick(0);
            if(failure==0)s.Valid=false;if(failure==1)s.Lease=false;if(failure==2)s.CanExecute=false;if(failure==3)s.Restriction="alarm door";
            r.Tick(failure==4?-1:1);Check(r.Finished&&s.Opens==0&&s.Releases==1,"异常场景清理 "+failure);
        }
        foreach(float duration in new[]{float.NaN,float.PositiveInfinity,-1,16}) { s=new(){OpenDuration=duration};r=Arrive(s);r.Tick(1);Check(r.Finished&&s.Opens==0,"非法时长不交互"); }
        s=new();r=new(s,0);r.Tick(0);r.Tick(120);Check(r.Finished,"接近目标总超时");
        s=new();r=new(s,0);r.Tick(0);r.Tick(20);r.Pause(true);r.Tick(21);r.Tick(100);
        Check(r.Stage==OpenTaskStage.Paused&&s.Releases==1&&s.Opens==0,"开门移动可暂停且释放控制，不提交按钮");
        r.Pause(false);r.Tick(101);Check(r.Stage==OpenTaskStage.Moving&&!r.Finished,"重新获取权限使用新等待窗口，不误用旧15秒预算");
        s.Near=true;r.Tick(102);r.Tick(103);r.Pause(true);r.Tick(104);
        Check(r.Stage==OpenTaskStage.Verifying&&s.Opens==1,"已提交开启后仍核验，不被手动暂停截断");
        s.IsOpen=true;r.Tick(105);r.Tick(106);Check(r.Stage==OpenTaskStage.Paused&&s.Opens==1,"确认打开后在返程边界暂停");
        r.Pause(false);s.Home=true;r.Tick(107);Check(r.Stage==OpenTaskStage.Complete&&s.Opens==1,"继续只完成返程，不重新按开门按钮");
        s=new(){NeedsUnlock=true,UnlockAtomic=true};r=Arrive(s);r.Pause(true);r.Tick(1);
        Check(r.Stage==OpenTaskStage.Unlocking&&s.Unlocks==1&&s.Releases==0,"熔锁／挥击已经开始时暂停先等待原版完成");
        s.LockResolved=true;r.Tick(2);r.Tick(3);Check(r.Stage==OpenTaskStage.Paused&&s.Releases==1&&s.Opens==0,"锁解除后在下一步开启前暂停");
        s=new();r=new(s,0);r.Pause(true);r.Tick(0);r.Tick(601);Check(r.Finished&&s.Releases==0,"长期暂停有界清理，未获取时不释放别人权限");
        Console.WriteLine($"PASS: {checks} 项标记期限／锁分类／原版交互任务状态机断言；Unity、原生锁具与联机同步未执行。");
    }
}
