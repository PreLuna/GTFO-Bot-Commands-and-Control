using System;
using System.Threading;
using PreLuna.Captain.Control;
using PreLuna.HeadCamera;
using PreLuna.MicroMaster;
static class ControlTests
{
    static int checks;
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    sealed class Session : IControlTransaction
    {
        public bool Valid { get; set; } = true;
        public bool Paused { get; set; } = true;
        public bool AllowAcquire = true, AllowRenew = true, ThrowQuiesce, ThrowRelease;
        public int Acquires, Stops, Releases, Restores, Keeps, Recalls;
        public bool Acquire() { Acquires++; return AllowAcquire; }
        public bool Renew() => AllowRenew;
        public void Quiesce() { Stops++; if (ThrowQuiesce) throw new Exception("native stop failed"); }
        public void ReleaseLease() { Releases++; if (ThrowRelease) throw new Exception("release failed"); }
        public void RestoreBeforeActivation() => Restores++;
        public void KeepPaused() => Keeps++;
        public void Recall() => Recalls++;
    }
    sealed class Service : IBotControlService
    {
        public bool Begin(BotControlTarget target, string source, out long ticket, out string reason) { ticket = 1; reason = null; return true; }
        public BotControlState Poll(long ticket, out string reason) { reason = null; return BotControlState.Waiting; }
        public bool Renew(long ticket) => true;
        public void Release(long ticket, bool recall) { }
        public bool Controls(long agent, long owner) => agent == 10 && owner == 20;
    }
    static void Transactions()
    {
        var s = new Session { Paused = false }; var t = new ControlTransaction(s, 0);
        t.Tick(0); Check(t.State == BotControlState.Waiting && s.Acquires == 0, "暂停未完成时不申请租约");
        s.Paused = true; s.AllowAcquire = false; t.Tick(1);
        Check(t.State == BotControlState.Waiting && s.Stops == 0, "租约申请失败不激活输入隔离");
        s.AllowAcquire = true; t.Tick(2);
        Check(t.State == BotControlState.Active && s.Stops == 1, "停掉剩余自主动作后才激活");
        t.Exit(true);
        Check(t.State == BotControlState.Released && s.Releases == 1 && s.Keeps == 1 && s.Recalls == 1 && s.Restores == 0, "成功退出保留旧任务暂停并召回");
        t.Exit(true); t.Tick(10); Check(s.Releases == 1 && s.Recalls == 1, "退出幂等，不重复召回");
        s = new Session(); t = new ControlTransaction(s, 0); t.Exit(true);
        Check(s.Restores == 1 && s.Keeps == 0 && s.Recalls == 0, "等待期间取消回滚，不召回");
        s = new Session { ThrowQuiesce = true }; t = new ControlTransaction(s, 0);
        try { t.Tick(0); } catch { }
        Check(t.State == BotControlState.Failed && s.Releases == 1 && s.Restores == 1 && s.Keeps == 0, "停止动作异常释放租约并恢复旧任务");
        s = new Session { AllowRenew = false }; t = new ControlTransaction(s, 0); t.Tick(0);
        Check(t.State == BotControlState.Failed && s.Restores == 1 && s.Releases == 1, "提交前续期失败仍属于回滚");
        s = new Session(); t = new ControlTransaction(s, 0); t.Tick(0); s.AllowRenew = false; t.Tick(1);
        Check(t.State == BotControlState.Failed && s.Keeps == 1 && s.Recalls == 1, "已激活后租约丢失保持暂停并退出");
        s = new Session(); t = new ControlTransaction(s, 0); t.Tick(0); s.Valid = false; t.Tick(1);
        Check(t.State == BotControlState.Failed && s.Recalls == 0, "机器人身份失效不能召回同槽新对象");
        s = new Session(); t = new ControlTransaction(s, 0); t.Tick(0);
        Check(!t.Renew(3.1f), "过期心跳不能被复活"); t.Tick(3.1f);
        Check(t.State == BotControlState.Failed && s.Recalls == 1, "心跳失效自动释放并召回");
        s = new Session { Paused = false }; t = new ControlTransaction(s, 0);
        for (int i = 0; i <= 8; i++) { t.Renew(i); t.Tick(i); }
        Check(t.State == BotControlState.Failed && s.Restores == 1 && s.Stops == 0, "有心跳的等待仍受八秒上限约束");
        s = new Session { ThrowRelease = true }; t = new ControlTransaction(s, 0); t.Tick(0);
        try { t.Exit(true); } catch { }
        Check(t.State == BotControlState.Released && s.Keeps == 1 && s.Recalls == 1, "原生释放异常不能保留Active状态");
        s = new Session(); t = new ControlTransaction(s, 10); t.Tick(10); t.Tick(9);
        Check(t.State == BotControlState.Failed && s.Recalls == 0, "时钟倒退清理旧控制会话");
        s = new Session(); t = new ControlTransaction(s, 0); t.Tick(0); t.Exit(false);
        Check(s.Keeps == 1 && s.Recalls == 0, "生命周期退出不发起召回");
        s = new Session(); t = new ControlTransaction(s, 0); t.Tick(float.NaN);
        Check(t.State == BotControlState.Failed, "无效时间不能持有控制权");
    }
    static void Gestures()
    {
        var a = new CameraTarget(1, 10, 20, "bot", true); var b = new CameraTarget(1, 11, 21, "replacement", true);
        var g = new ToggleGesture();
        Check(g.Step(0, true, false, false, null, .3f) == ToggleIntent.Open, "关窗时首击立即打开");
        g.CaptureOpenedTarget(a);
        Check(g.Step(.4f, false, true, false, a, .3f) == ToggleIntent.None, "单击打开不在延迟后自动关闭");
        Check(g.Step(1, true, true, false, a, .3f) == ToggleIntent.None, "已打开时首击等待双击判定");
        Check(g.Step(1.2f, true, true, false, a, .3f) == ToggleIntent.RequestControl && g.RequestTarget.Value.AgentIdentity == 10, "双击接管固定的机器人身份");
        g.Reset(); g.Step(2, true, true, false, a, .3f);
        Check(g.Step(2.4f, false, true, false, a, .3f) == ToggleIntent.Close, "已打开时单击延迟关闭");
        g.Reset(); g.Step(3, true, true, false, a, .3f);
        Check(g.Step(3.1f, true, true, false, b, .3f) == ToggleIntent.None, "同槽身份替换不接管新对象");
        g.Reset(); g.Step(4, true, true, false, a, .3f); g.Reset();
        Check(g.Step(4.2f, true, true, false, a, .3f) == ToggleIntent.None, "焦点变化重置双击序列");
        Check(g.Step(5, true, true, true, a, .3f) == ToggleIntent.ExitControl, "已接管或正在申请时T直接退出");
        g.Reset(); g.Step(7, true, true, false, a, .3f);
        Check(g.Step(7.5f, true, true, false, a, .3f) == ToggleIntent.Close, "超时第二击不变成接管");
        g.Reset(); g.Step(10, true, true, false, a, .3f);
        Check(g.Step(9, false, true, false, a, .3f) == ToggleIntent.None, "时钟倒退清除待定单击");
        g.Reset(); g.Step(11, true, false, false, null, .3f); g.CaptureOpenedTarget(a);
        Check(g.Step(11.2f, true, true, false, a, .3f) == ToggleIntent.RequestControl, "关闭状态双击可打开后接管");
    }
    static void Safety()
    {
        Check(ControlSafety.HealthDropped(100, 99), "实际生命下降触发退出");
        Check(!ControlSafety.HealthDropped(100, 100) && !ControlSafety.HealthDropped(90, 100), "零伤害和治疗不触发退出");
        Check(!ControlSafety.HealthDropped(float.NaN, 1), "无效生命不被当作有效伤害事件");
        Check(ControlSafety.Check(true,true,true,true,true,true,false,true,false) == null, "正常状态允许控制");
        Check(ControlSafety.Check(true,false,true,true,true,true,false,true,false) != null, "非房主退出");
        Check(ControlSafety.Check(true,true,true,true,true,false,false,true,false) != null, "本体倒地退出");
        Check(ControlSafety.Check(true,true,true,true,true,true,true,true,false) != null, "本体被抓退出");
        Check(ControlSafety.Check(true,true,true,false,true,true,false,true,false) != null, "失焦退出");
        Check(ControlSafety.Check(true,true,true,true,true,true,false,true,true) != null, "手动紧急退出不依赖界面");
        Check(ControlSafety.Check(true,true,true,true,false,true,false,true,false) != null, "本体身份改变退出");
        Check(ControlSafety.Check(true,true,false,true,true,true,false,true,false) != null, "切关退出");
        Check(ControlSafety.Check(true,true,true,true,true,true,false,false,false) != null, "特殊运动状态退出");
        foreach (int action in new[] {1,2,3,4,5,6,7,8,9,10,11,12,23,24,25,26,27,28,29,30,31,32,45,46,47,58,65,66,67})
            Check(ControlSafety.BlockAction(action), "隔离角色操作 " + action);
        foreach (int action in new[] {15,20,33,36,40,43,44,55,56,57,63,64}) Check(!ControlSafety.BlockAction(action), "保留界面与通讯操作 " + action);
    }
    static void Protocol()
    {
        var s = new Service(); Check(BotControlApi.Register(s), "服务注册");
        Check(!BotControlApi.Register(new Service()), "重复服务不能抢占");
        Check(BotControlApi.Controls(10,20) && !BotControlApi.Controls(10,21) && !BotControlApi.Controls(11,20), "控制身份包含Agent与Owner");
        bool otherThread = true; var thread = new Thread(() => otherThread = BotControlApi.Available); thread.Start(); thread.Join();
        Check(!otherThread, "非Unity主线程不能调用服务");
        BotControlApi.Unregister(new Service()); Check(BotControlApi.Available, "其他服务不能注销当前服务");
        BotControlApi.Unregister(s); Check(!BotControlApi.Available && !BotControlApi.Renew(1), "卸载后关闭入口");
    }
    public static void Main() { Transactions(); Gestures(); Safety(); Protocol(); Console.WriteLine("PASS: " + checks + " 接管事务／双击／输入与紧急退出检查"); }
}
