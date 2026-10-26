using System;
using PreLuna.HeadCamera;
using UnityEngine;

static class PresentationTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Throws(Action action, string message) { try { action(); } catch { checks++; return; } throw new Exception(message); }
    static void Main()
    {
        var mainTarget = new RenderTexture(10, 10, 0, RenderTextureFormat.ARGBHalf);
        var raw = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGBHalf);
        RenderTexture.active = mainTarget; GL.sRGBWrite = true;
        var display = new CapturePresentation();
        int initial = RenderTexture.Created.Count;
        display.Update(raw, FeedOutput.OriginalAlpha);
        Check(ReferenceEquals(display.Texture, raw) && RenderTexture.Created.Count == initial, "旧显示路径直通不分配输出");
        display.Update(raw, FeedOutput.RawOpaque);
        var owned = display.Texture;
        Check(owned.format == RenderTextureFormat.RGB111110Float && owned.width == 1280 && owned.height == 720, "不透明输出保留输入尺寸且使用无Alpha格式");
        Check(Graphics.LastMaterial == null && ReferenceEquals(Graphics.LastSource, raw), "RawOpaque不经过色调处理");
        Check(ReferenceEquals(RenderTexture.active, mainTarget) && GL.sRGBWrite, "成功输出后恢复主渲染目标及颜色写入状态");
        display.Update(raw, FeedOutput.ToneMapped);
        var copy = Graphics.LastMaterial;
        Check(copy != null && !ReferenceEquals(copy, RenderPipe.TonemapMaterial), "正常输出拥有独立材质，不更改共享材质");
        display.Update(raw, FeedOutput.ToneMapped);
        Check(ReferenceEquals(Graphics.LastMaterial, copy) && ReferenceEquals(display.Texture, owned) && RenderTexture.Created.Count == initial + 1, "重复帧不反复分配纹理和材质");
        Graphics.Fail = true;
        Throws(() => display.Update(raw, FeedOutput.RawOpaque), "模拟GPU失败应向拍摄熔断传播");
        Check(ReferenceEquals(RenderTexture.active, mainTarget) && GL.sRGBWrite && display.Texture == null, "输出异常也恢复主渲染状态且不暴露旧帧");
        Graphics.Fail = false;
        display.Dispose(); display.Dispose();
        Check(owned.Released && owned.Destroyed && copy.Destroyed && !raw.Destroyed && !RenderPipe.TonemapMaterial.Destroyed && display.Texture == null, "只释放副相机输出，不破坏输入和共享材质；重复清理安全");

        SystemInfo.Supported = false;
        var unavailable = new CapturePresentation();
        Throws(() => unavailable.Update(raw, FeedOutput.RawOpaque), "不支持目标格式时明确失败");
        Check(ReferenceEquals(RenderTexture.active, mainTarget) && GL.sRGBWrite, "格式失败不泄漏渲染状态");
        unavailable.Update(raw, FeedOutput.OriginalAlpha);
        Check(ReferenceEquals(unavailable.Texture, raw), "GPU格式不可用时仍能使用原始显示诊断");
        unavailable.Dispose(); SystemInfo.Supported = true;

        RenderPipe.TonemapMaterial = null;
        var missingShader = new CapturePresentation();
        Throws(() => missingShader.Update(raw, FeedOutput.ToneMapped), "色调材质缺失不静默降级");
        missingShader.Update(raw, FeedOutput.RawOpaque);
        Check(missingShader.Texture != null && Graphics.LastMaterial == null, "材质缺失仍可诊断原始RGB");
        missingShader.Dispose();

        var pattern = CapturePresentation.CreatePattern();
        Check(pattern.Pixels.Length == 32 && pattern.Pixels[0].a == 1 && pattern.Pixels[7].r == 1 && pattern.Pixels[8].r == 1, "彩条独立创建，不需要任何场景贴图或色调材质");
        UnityEngine.Object.Destroy(pattern);
        Texture2D.FailUpload = true;
        Throws(() => CapturePresentation.CreatePattern(), "彩条上传失败应可见");
        Check(Texture2D.Last.Destroyed, "彩条创建失败不泄漏纹理");
        Console.WriteLine("PASS: " + checks + " 项生产输出流程检查；GPU与Unity资源由替身模拟，未验证实际像素或游戏画面。");
    }
}
