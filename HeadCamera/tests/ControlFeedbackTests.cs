using System;
using PreLuna.HeadCamera;

static class ControlFeedbackTests
{
    static int checks;
    static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    static void Main()
    {
        var feedback = new ControlFeedback();
        var target = new CameraTarget(1, 2, 3, "bot", true);
        int notifications = 0, calls = 0, errors = 0;
        float now = 10;
        void Notify(string text) { notifications++; feedback.Store(text, now); }
        feedback.Request(target, null, false, Notify, _ => errors++);
        Check(feedback.Read(10).Contains("入口未就绪"), "缺少模块回调时解释原因，仍保持普通观看");
        Check(feedback.Read(14.9f) != null && feedback.Read(15) == null, "拒绝提示五秒后消失，不被拍摄帧率状态立即盖掉");
        feedback.Request(target, _ => { calls++; return true; }, true, Notify, _ => errors++);
        Check(calls == 0 && feedback.Read(10).Contains("彩条"), "显示测试不发起真实接管");
        feedback.Clear();
        feedback.Request(target, _ => { Notify("机器人正在救援"); return false; }, false, Notify, _ => errors++);
        Check(feedback.Read(10) == "机器人正在救援", "服务的具体拒绝原因不会被通用原因覆盖");
        feedback.Request(target, _ => false, false, Notify, _ => errors++);
        Check(feedback.Read(10).Contains("未被接受"), "旧回调静默拒绝仍有兜底提示");
        int before = notifications;
        feedback.Request(target, _ => true, false, Notify, _ => errors++);
        Check(notifications == before, "成功申请不伪造拒绝提示");
        feedback.Request(target, _ => throw new Exception("service failure"), false, Notify, _ => errors++);
        Check(errors == 1 && feedback.Read(10).Contains("service failure"), "接管回调异常被捕获并保留观看能力");
        Check(feedback.Read(9) == null, "时钟倒退清除过期提示");
        Check(!feedback.Store(" ", 1) && !feedback.Store("x", float.NaN), "拒绝无效提示和时间");
        Check(feedback.Store(new string('x', 1000), 1) && feedback.Read(1).Length == 400, "限制UI提示长度");
        feedback.Clear(); Check(feedback.Read(1) == null, "关闭会话清除旧提示");
        Console.WriteLine("PASS: " + checks + " 项接管提示测试；覆盖模块缺失、身份变化、回调失败与提示寿命。");
    }
}
