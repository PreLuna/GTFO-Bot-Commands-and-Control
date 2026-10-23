using System;
using PreLuna.Captain.Tasks;
static class SingleKillTests
{
    sealed class Session : ISingleKillSession
    {
        public bool Valid { get; set; } = true;
        public bool TargetAlive { get; set; } = true;
        public bool SleepingStable { get; set; } = true;
        public bool Unsafe { get; set; }
        public bool CanExecute { get; set; } = true;
        public bool Arrived { get; set; }
        public bool TravelFailed { get; set; }
        public bool Ready { get; set; }
        public string StrikeFailure { get; set; }
        public bool MeleeTerminated { get; set; }
        public bool ContinuationAtomic { get; set; }
        public bool Lease = true, Accept = true;
        public int Strikes, Begins, Finishes, Reports;
        public int Continuations;
        public string ContinuationFailure;
        public string ContinueAttack(float now) { Continuations++; return ContinuationFailure; }
        public SingleKillStage Result;
        public bool Renew(SingleKillStage stage) { Reports++;return Lease; }
        public bool BeginMelee() { Begins++;return Accept; }
        public void Strike() => Strikes++;
        public void Finish(SingleKillStage result,string reason) { Finishes++;Result=result; }
    }
    static int count;
    static void Check(bool value,string reason){count++;if(!value)throw new Exception(reason);}
    static SingleKillRunner Charge(Session s){var r=new SingleKillRunner(s,0,30);s.Arrived=true;r.Tick(1);Check(r.Stage==SingleKillStage.Charging,"到达后才能蓄力");return r;}
    static SingleKillRunner Strike(Session s){var r=Charge(s);s.Ready=true;r.Tick(2);Check(r.Stage==SingleKillStage.Striking&&s.Strikes==1,"只在就绪后释放一次");return r;}
    static void Main()
    {
        var s=new Session();var r=new SingleKillRunner(s,0,30);
        for(int i=0;i<20;i++)r.Tick(i*.1f);
        Check(s.Begins==0&&s.Strikes==0&&!r.Finished,"到达前不会近战或攻击");
        s.Arrived=true;r.Tick(3);Check(s.Begins==1,"接近完成只创建一次近战");
        for(int i=0;i<20;i++)r.Tick(4+i*.1f);
        Check(s.Begins==1&&s.Strikes==0,"未满蓄力和有效距离不挥击");
        s.Ready=true;r.Tick(7);s.SleepingStable=false;s.Unsafe=true;s.CanExecute=false;
        for(int i=0;i<20;i++)r.Tick(7+i*.1f);
        Check(s.Strikes==1&&!r.Finished,"挥击后的惊醒不会重启近战，等待原生结果");
        s.TargetAlive=false;r.Tick(9);Check(r.Stage==SingleKillStage.Complete&&s.Finishes==1,"通过死亡核验才报告目标死亡");
        r.Tick(10);r.Abort("late");r.Cancel();Check(s.Finishes==1&&s.Strikes==1,"结束后的多次调用不会重复释放或攻击");
        foreach(int condition in new[]{0,1,2,3,4,5}){
            s=new();r=new(s,0,30);
            if(condition==0)s.Valid=false;if(condition==1)s.SleepingStable=false;if(condition==2)s.Unsafe=true;
            if(condition==3)s.CanExecute=false;if(condition==4)s.Lease=false;if(condition==5)s.TravelFailed=true;
            r.Tick(1);Check(r.Stage==SingleKillStage.Failed&&s.Strikes==0&&s.Finishes==1,"接近阶段失败清理 "+condition);
        }
        s=new();r=Charge(s);s.SleepingStable=false;s.Ready=true;r.Tick(2);
        Check(r.Stage==SingleKillStage.Failed&&s.Strikes==0,"就绪同刻目标惊醒也不攻击");
        s=new();r=new(s,0,30);r.Tick(30);Check(r.Finished&&s.Strikes==0,"接近总超时");
        s=new();r=Charge(s);r.Tick(30);Check(r.Finished&&s.Strikes==0,"蓄力沿用总超时，不无限续期");
        s=new(){Accept=false};r=new(s,0,30);s.Arrived=true;r.Tick(1);Check(r.Finished&&s.Begins==1,"原生近战拒绝不重试");
        s=new();r=Charge(s);s.MeleeTerminated=true;s.Ready=true;r.Tick(2);Check(r.Finished&&s.Strikes==0,"中断先于就绪判定");
        s=new();r=Charge(s);s.Ready=true;s.Lease=false;r.Tick(2);Check(r.Finished&&s.Strikes==0,"权限失效不释放攻击");
        s=new();r=Strike(s);s.MeleeTerminated=true;r.Tick(3);Check(r.Stage==SingleKillStage.Continuing&&s.Strikes==1&&s.Finishes==0,"存活目标继续接战，不返程、不误报死亡");
        s.SleepingStable=false;s.Unsafe=true;r.Tick(100);Check(!r.Finished&&s.Continuations==2,"苏醒／交战不沿用潜行限制，接战不受首轮准备超时限制");
        s.TargetAlive=false;r.Tick(101);Check(r.Stage==SingleKillStage.Complete&&s.Finishes==1,"补击后实际死亡才结束借调");
        s=new();r=Strike(s);r.Tick(7);Check(r.Stage==SingleKillStage.Continuing,"挥击最多等待五秒后进入接战而非失败返程");
        r.Cancel();Check(r.Stage==SingleKillStage.Cancelled&&s.Finishes==1,"持续接战可以明确取消");
        s=new();r=Strike(s);s.MeleeTerminated=true;r.Tick(3);s.ContinuationAtomic=true;r.Cancel();r.Tick(4);
        Check(!r.Finished&&s.Continuations==1,"已释放补刀取消时保留原子窗口，不启动新攻击");
        s.ContinuationAtomic=false;r.Tick(5);Check(r.Stage==SingleKillStage.Cancelled&&s.Finishes==1,"补刀原子窗口结束后取消一次");
        s=new();r=Strike(s);s.MeleeTerminated=true;r.Tick(3);s.CanExecute=false;r.Tick(4);Check(r.Stage==SingleKillStage.Failed&&s.Continuations==1,"紧急救援夺走权限则停止接战");
        s=new(){ContinuationFailure="目标跨越Zone"};r=Strike(s);s.MeleeTerminated=true;r.Tick(3);Check(r.Stage==SingleKillStage.Failed&&s.Finishes==1,"真实接战失败保留明确诊断");
        s=new();r=Charge(s);r.Cancel();Check(r.Stage==SingleKillStage.Cancelled&&s.Strikes==0,"蓄力前取消立即清理");
        s=new();r=Strike(s);r.Cancel();Check(!r.Finished,"已经挥出的动作保留原子窗口");
        s.MeleeTerminated=true;r.Tick(3);Check(r.Stage==SingleKillStage.Cancelled&&s.Finishes==1,"取消在挥击结束后生效");
        s=new();r=Strike(s);s.Valid=false;r.Tick(3);Check(r.Finished&&s.Strikes==1,"换关卡或房主不等待五秒");
        s=new();r=new(s,0,30);s.TargetAlive=false;r.Tick(1);Check(r.Stage==SingleKillStage.Complete&&s.Strikes==0,"别人先击杀目标，不攻击尸体");
        Check(SingleKillRunner.DamageEnough(20,10,2),"估计等于生命可以接受");
        Check(!SingleKillRunner.DamageEnough(21,10,2),"估计不足不能接单");
        s=new();r=Charge(s);s.StrikeFailure="弱点被破坏，当前伤害不足";r.Tick(2);
        Check(r.Stage==SingleKillStage.Failed&&s.Strikes==0&&s.Finishes==1,"挥击前失败有明确原因，不能等到笼统超时");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,0,-1}){
            Check(!SingleKillRunner.DamageEnough(invalid,10,2),"拒绝异常生命");
            Check(!SingleKillRunner.DamageEnough(20,invalid,2),"拒绝异常伤害");
            Check(!SingleKillRunner.DamageEnough(20,10,invalid),"拒绝异常倍率");
        }
        Console.WriteLine($"PASS: {count} 项指定击杀实际状态机断言；未执行Unity原生攻击和路径。");
    }
}
