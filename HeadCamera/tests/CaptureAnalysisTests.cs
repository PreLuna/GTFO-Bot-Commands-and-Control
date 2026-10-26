using System;
using PreLuna.HeadCamera;

static class CaptureAnalysisTests
{
    static int checks;
    static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
    static void Main()
    {
        var quota = new CaptureQuota(6);
        var first = new CaptureSchedule(quota);
        Check(!first.Due(false, 0), "默认关闭不采样");
        Check(!first.Due(true, 100) && !first.Due(true, 100.49), "启用后等待半秒，不沿用未启用时钟");
        Check(first.Due(true, 100.5) && !first.Due(true, 100.51), "首次一次性采样，不连续读回");
        Check(!first.Due(true, 101.99) && first.Due(true, 102), "第二次采样间隔");
        Check(!first.Due(true, 10000), "一个开窗最多尝试两次，失败不重试");
        for (int i = 0; i < 2; i++)
        {
            var next = new CaptureSchedule(quota);
            next.Due(true, 0);
            Check(next.Due(true, .5) && next.Due(true, 2), "新的开窗共享进程配额");
        }
        var exhausted = new CaptureSchedule(quota); exhausted.Due(true, 0);
        Check(!exhausted.Due(true, 1) && !exhausted.Due(true, 2), "反复关闭打开仍不能突破六组上限");
        Check(!new CaptureQuota(-1).Take(), "无效配额拒绝采样");
        var urgent = new CaptureSchedule(new CaptureQuota(6));
        Check(urgent.Due(true, 0, true) && urgent.Due(true, .01, true) && !urgent.Due(true, .02, true), "显示阶段失败可立即保存原始画面，仍受两次限额约束");

        var transitions = new CaptureSchedule(new CaptureQuota(4));transitions.View(false);
        transitions.Due(true, 0);Check(transitions.Due(true, .5) && transitions.Due(true, 2), "普通观察按原节奏采样");
        transitions.View(true);Check(!transitions.Due(true, 3) && transitions.Due(true, 3.5), "同窗口进入接管重新安排采样，避免只留普通观察样本");
        transitions.View(true);Check(!transitions.Due(true, 3.6) && transitions.Due(true, 5), "连续接管帧不重置采样间隔");
        transitions.View(false);Check(!transitions.Due(true, 6, true), "模式来回切换仍不能突破共享配额");
        var white = new CapturePixelSummary();white.Add(2, 1, .99f, 1);white.Add(3, .5f, .1f, 1);white.Add(float.NaN, 1, 1, 1);
        Check(white.WhitePixels == 1 && white.Invalid == 1 && white.Count == 2, "统计全通道近白像素，单通道HDR和非法值不误判整屏白");
        var black = new CapturePixelSummary(); black.Add(0, 0, 0, 1);
        Check(black.Count == 1 && black.NonBlack == 0 && black.Transparent == 0 && black.MeanRgb == 0, "不透明黑色与透明分开统计");
        var hidden = new CapturePixelSummary(); hidden.Add(.2f, .4f, .6f, 0);
        Check(hidden.NonBlack == 1 && hidden.Transparent == 1 && hidden.MaxRgb > .59f && hidden.MaxAlpha == 0, "RGB存在但Alpha为零不能当作无画面");
        var hdr = new CapturePixelSummary(); hdr.Add(12, 3, 0, .5f);
        Check(hdr.MaxRgb == 12 && hdr.MeanRgb == 5, "HDR统计保留大于一的值");
        hdr.Add(float.NaN, 0, 0, 1); hdr.Add(0, float.PositiveInfinity, 0, 1);
        Check(hdr.Count == 1 && hdr.Invalid == 2 && hdr.MeanRgb == 5, "无效像素不会污染正常统计");
        Check(CapturePixelSummary.Preview(float.NaN) == 0 && CapturePixelSummary.Preview(-1) == 0, "预览不生成非法颜色");
        Check(Math.Abs(CapturePixelSummary.Preview(1) - 1) < .00001 && CapturePixelSummary.Preview(100) <= 1, "预览限制显示范围，原始HDR另存EXR");
        Check(CapturePixelSummary.Preview(.001f, 16) > CapturePixelSummary.Preview(.001f), "固定加四档曝光仅用于对照暗部");
        Console.WriteLine("PASS: " + checks + " 项，覆盖诊断限额、间隔、失败不重试、RGB/Alpha分离、HDR与非法像素。");
    }
}
