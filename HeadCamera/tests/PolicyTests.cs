using System;
using System.Collections.Generic;
using PreLuna.HeadCamera;

static class PolicyTests
{
    static int checks;
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); checks++; }
    static void Main()
    {
        Check(ViewPolicy.Display(true,true,false,true,false), "Q菜单拥有输入时保持接管画面");
        Check(!ViewPolicy.Display(true,true,false,false,false), "普通观看仍遵守界面输入限制");
        Check(!ViewPolicy.Display(true,false,false,true,false), "失焦不显示接管画面");
        Check(!ViewPolicy.Display(false,true,false,true,false), "离开关卡不保留接管画面");
        Check(!ViewPolicy.Display(true,true,false,true,true), "终端仍关闭摄像头");
        var a = new CameraTarget(1, 100, 11, "A", true);
        var b = new CameraTarget(2, 200, 22, "B", false);
        var c = new CameraTarget(3, 300, 33, "C", true);
        var candidates = new List<CameraTarget> { a, b, c };
        Check(ViewPolicy.Select(candidates, null, 0) == 0, "首次打开选择第一位队友");
        Check(ViewPolicy.Select(candidates, b, 0) == 1, "周期刷新保留当前观看对象");
        Check(ViewPolicy.Select(candidates, c, 1) == 0, "向后切换循环");
        Check(ViewPolicy.Select(candidates, a, -1) == 2, "向前切换循环");
        candidates.Remove(b);
        Check(ViewPolicy.Select(candidates, b, 0) == 0, "队友离队后选择有效对象");
        var replacement = new CameraTarget(1, 101, 12, "New A", true);
        Check(!a.Same(replacement), "重新加入同一槽位不可复用旧机器人身份");
        Check(!a.Same(new CameraTarget(1, 100, 99, "Owner changed", true)), "相同角色指针但所有者改变视作新身份");
        candidates = new() { c, replacement };
        Check(ViewPolicy.Select(candidates, a, 0) == 0, "不通过旧槽位静默匹配新身份");
        Check(ViewPolicy.Select(Array.Empty<CameraTarget>(), a, 1) == -1, "没有队友时不访问无效索引");

        var clock = new RenderClock();
        Check(clock.Due(0, 12), "第一帧允许拍摄");
        Check(!clock.Due(.02, 12), "主画面高帧率不会扩大拍摄频率");
        Check(clock.Due(.084, 12), "到期允许拍摄");
        Check(clock.Due(50, 12) && !clock.Due(50, 12), "暂停后不补拍积压帧");
        clock.Reset(); Check(clock.Due(50, 12), "切换队友后立即拍摄");
        clock.Reset();
        int frames = 0;
        for (int i = 0; i < 3600; i++) if (clock.Due((float)(i / 60d), 30)) frames++;
        Check(frames == 1800, "60FPS 主画面浮点时钟下保持30FPS，不因每次推迟截止时间而降帧");
        clock.Reset(); frames = 0;
        for (int i = 0; i < 3600; i++) if (clock.Due((float)(3600 + i / 60d), 30)) frames++;
        Check(frames == 1800, "一小时后仍保持30FPS拍摄节奏");
        clock.Reset(); frames = 0;
        for (int i = 0; i < 1200; i++) if (clock.Due((float)(i / 120d), 60)) frames++;
        Check(frames == 600, "可配置60FPS且不超过目标上限");
        clock.Reset(); frames = 0;
        for (int i = 0; i < 600; i++) if (clock.Due((float)(i / 20d), 30)) frames++;
        Check(frames == 600, "主画面低于目标时每帧至多拍摄一次，不补拍积压帧");
        Check(clock.Due(30, 60), "修改刷新率时立即重设节奏");

        Check(DefaultUpgrade.Replace(0, 480, 480), "旧版默认分辨率自动升级");
        Check(!DefaultUpgrade.Replace(0, 800, 480), "保留旧版自定义分辨率");
        Check(!DefaultUpgrade.Replace(1, 480, 480), "迁移完成后主动设回旧值不再次被覆盖");
        Check(DefaultUpgrade.CurrentVersion==2,"本轮只推进诊断迁移版本");
        Check(DefaultUpgrade.DisableLegacyCapture(0,true)&&DefaultUpgrade.DisableLegacyCapture(1,true),"升级时一次性关闭遗留同步采样");
        Check(!DefaultUpgrade.DisableLegacyCapture(1,false),"原本关闭的采样不产生额外变化");
        Check(!DefaultUpgrade.DisableLegacyCapture(2,true)&&!DefaultUpgrade.DisableLegacyCapture(3,true),"迁移后主动重开诊断不被每次启动覆盖");
        Check(!DefaultUpgrade.Replace(1,12,12)&&!DefaultUpgrade.Replace(1,360,360),"诊断迁移不重复修改用户主动设定的刷新率和小窗尺寸");

        var session = new ViewSession();
        session.SetVisible(true);
        Check(!session.ObserveTerminal(false) && session.Opened, "地图等其他界面保留打开状态");
        Check(session.ObserveTerminal(true) && !session.Opened, "进入终端关闭观看会话");
        Check(!session.ObserveTerminal(true), "连续观察终端不重复执行关闭");
        Check(!session.ObserveTerminal(false) && !session.Opened, "退出终端不自动重新打开");
        session.SetVisible(true);
        Check(session.Opened, "退出终端后可手动重新打开");

        var meter = new FrameRateMeter();
        meter.Sample(0);
        for (int i = 1; i <= 30; i++) meter.Sample(i / 30d);
        Check(Math.Abs(meter.Actual - 30) < .001, "显示基于成功提交的实际拍摄帧率");
        meter.Reset(); Check(meter.Actual == 0, "切换对象及暂停后清除旧帧率");

        var failures = new FailureCircuit();
        failures.Fail(0); Check(!failures.Ready(4.99) && failures.Ready(5), "异常冷却期间不每帧重试");
        failures.Fail(5); failures.Fail(10);
        Check(failures.Tripped && !failures.Ready(1000), "连续异常熔断后不会自动反复初始化");
        failures.Reset(); Check(failures.Ready(10), "主动关闭再打开可重新尝试");
        failures.Fail(10); failures.Success(); failures.Fail(20); failures.Fail(25);
        Check(!failures.Tripped, "成功拍摄清除连续失败计数");
        failures.Trip(); Check(!failures.Ready(double.MaxValue), "恢复主画面失败立即停止渲染");
        Console.WriteLine("PASS: " + checks + " 项，覆盖队友身份、限频节奏、旧配置迁移、终端会话、实际帧率与失败熔断。");
    }
}
