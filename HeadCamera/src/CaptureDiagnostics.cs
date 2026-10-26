using System;
using System.IO;
using BepInEx;
using Player;
using UnityEngine;

namespace PreLuna.HeadCamera
{
    internal sealed class CaptureDiagnostics
    {
        static readonly CaptureQuota lifetimeQuota = new(6);
        readonly CaptureSchedule schedule = new(lifetimeQuota);

        internal void Observe(RenderTexture raw, RenderTexture presented, Camera camera, PlayerAgent target, Camera main, bool outputFailed = false, bool controlled = false, string captureState = null)
        {
            schedule.View(controlled);
            if (!schedule.Due(Plugin.CaptureSamples.Value, Time.unscaledTime, outputFailed)) return;
            // Deliberately bounded synchronous readback: two attempts per open, six per process.
            // Errors here must never close the camera or trip the gameplay capture circuit.
            try
            {
                string folder = Path.Combine(Paths.BepInExRootPath, "HeadCamera-Diagnostics", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
                Directory.CreateDirectory(folder);
                var rawStats = SaveRaw(raw, folder);
                var finalStats = presented != null ? SavePresented(presented, folder) : null;
                if (finalStats?.Count > 0 && finalStats.WhitePixels > finalStats.Count * .95)
                    Plugin.Instance.Log.LogWarning("[头戴摄像头] 诊断输出超过95%为近白像素；请对照同地点的原始／输出样本，并附原生接管手电状态日志，样本=" + folder);
                string outputState = presented != null ? "presentedFormat=" + presented.format + " presentedSRGB=" + presented.sRGB : "presented=unavailable (output failed)";
                string context = "slot=" + target.PlayerSlotIndex + " dimension=" + target.DimensionIndex + " targetPosition=" + target.Position
                    + " mainPosition=" + main.transform.position + " mainFOV=" + main.fieldOfView + " controlled=" + controlled + " captureState=" + captureState;
                string state = FormattableString.Invariant($"HeadCamera={Plugin.Version}\nUTC={DateTime.UtcNow:o}\nmode={Plugin.OutputMode.Value}\nbufferFix={Plugin.BufferFix.Value}\nprojectionFix={Plugin.ProjectionFix.Value}\nHDR={camera.allowHDR} renderingPath={camera.actualRenderingPath} depthTextureMode={camera.depthTextureMode}\nnear={camera.nearClipPlane} far={camera.farClipPlane} FOV={camera.fieldOfView} aspect={camera.aspect}\ncameraPosition={camera.transform.position} cameraDirection={camera.transform.forward}\nrawSize={raw.width}x{raw.height} rawFormat={raw.format} rawSRGB={raw.sRGB}\n{outputState}\nprojection={RenderUtils.GetProjectionParams(camera)} depth={RenderUtils.GetZBufferParams(camera)}\n{context}\nraw: {rawStats}\npresented: {finalStats}\nPixel statistics do not prove scene visibility. Inspect RGB, alpha and presented images together.\n");
                File.WriteAllText(Path.Combine(folder, "capture.txt"), state);
                Plugin.Instance.Log.LogInfo("[头戴摄像头] 诊断样本=" + folder + "；raw " + rawStats + "；presented " + finalStats);
            }
            catch (Exception error) { Plugin.Instance.Log.LogWarning("[头戴摄像头] 诊断采样失败（本次不重试，继续显示）：" + error); }
        }
        static Texture2D Read(RenderTexture source) => CompatibleReadback.Read(source);
        static CapturePixelSummary SaveRaw(RenderTexture source, string folder)
        {
            var copy = Read(source);
            try
            {
                try { File.WriteAllBytes(Path.Combine(folder, "raw.exr"), ImageConversion.EncodeToEXR(copy, Texture2D.EXRFlags.OutputAsFloat)); }
                catch (Exception error) { Plugin.Instance.Log.LogWarning("[头戴摄像头] EXR 导出失败，继续保存 PNG 和像素统计：" + error.Message); }
                var pixels = copy.GetPixels(); var summary = new CapturePixelSummary();
                var rgb = new Color[pixels.Length]; var alpha = new Color[pixels.Length]; var boosted = new Color[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i]; summary.Add(c.r, c.g, c.b, c.a);
                    rgb[i] = new Color(CapturePixelSummary.Preview(c.r), CapturePixelSummary.Preview(c.g), CapturePixelSummary.Preview(c.b), 1);
                    boosted[i] = new Color(CapturePixelSummary.Preview(c.r, 16), CapturePixelSummary.Preview(c.g, 16), CapturePixelSummary.Preview(c.b, 16), 1);
                    float a = float.IsFinite(c.a) ? Math.Clamp(c.a, 0, 1) : 0;
                    alpha[i] = new Color(a, a, a, 1);
                }
                SavePng(folder, "raw-rgb.png", source.width, source.height, rgb);
                SavePng(folder, "raw-alpha.png", source.width, source.height, alpha);
                SavePng(folder, "raw-plus4EV.png", source.width, source.height, boosted);
                return summary;
            }
            finally { UnityEngine.Object.Destroy(copy); }
        }
        static CapturePixelSummary SavePresented(RenderTexture source, string folder)
        {
            var copy = Read(source);
            try
            {
                var pixels = copy.GetPixels(); var rgb = new Color[pixels.Length]; var summary = new CapturePixelSummary();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i]; summary.Add(c.r, c.g, c.b, c.a);
                    rgb[i] = new Color(CapturePixelSummary.Preview(c.r), CapturePixelSummary.Preview(c.g), CapturePixelSummary.Preview(c.b), 1);
                }
                // This is the texture entering UI, not a screenshot of the composed screen.
                SavePng(folder, "presented-rgb.png", source.width, source.height, rgb);
                return summary;
            }
            finally { UnityEngine.Object.Destroy(copy); }
        }
        static void SavePng(string folder, string name, int width, int height, Color[] colors)
        {
            var image = new Texture2D(width, height, TextureFormat.RGB24, false, true);
            try { image.SetPixels(colors); image.Apply(false, false); File.WriteAllBytes(Path.Combine(folder, name), ImageConversion.EncodeToPNG(image)); }
            finally { UnityEngine.Object.Destroy(image); }
        }
    }
}
