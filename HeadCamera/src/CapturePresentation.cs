using System;
using UnityEngine;

namespace PreLuna.HeadCamera
{
    internal sealed class CapturePresentation : IDisposable
    {
        RenderTexture output;
        Material tonemap;
        internal RenderTexture Texture { get; private set; }

        internal void Update(RenderTexture raw, FeedOutput mode)
        {
            Texture = null; // A failed output must not expose the previous frame as current.
            if (mode == FeedOutput.OriginalAlpha) { Texture = raw; return; }
            var previous = RenderTexture.active;
            bool previousSRGB = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false; // Both raw and presentation targets store linear RGB.
                if (output == null)
                {
                    // An RGB-only GPU target samples with alpha=1; no per-frame CPU readback
                    // or custom UI shader is needed to stop scene alpha hiding the video.
                    if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float))
                        throw new NotSupportedException("GPU 不支持 RGB111110Float；可用 OriginalAlpha 诊断原始画面");
                    output = new RenderTexture(raw.width, raw.height, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear)
                    { name = "HeadCamera.OpaqueFeed", useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear };
                    if (!output.Create() || output.format != RenderTextureFormat.RGB111110Float)
                        throw new InvalidOperationException("副相机不透明输出纹理创建失败或格式被替换");
                }
                if (mode == FeedOutput.RawOpaque) Graphics.Blit(raw, output);
                else
                {
                    if (tonemap == null)
                    {
                        var source = RenderPipe.TonemapMaterial;
                        if (source == null || source.shader == null || !source.shader.isSupported)
                            throw new InvalidOperationException("原版色调映射材质不可用；可用 RawOpaque 诊断原始画面");
                        tonemap = UnityEngine.Object.Instantiate(source); // Never edit the shared game material.
                    }
                    Graphics.Blit(raw, output, tonemap);
                }
                Texture = output;
            }
            finally { GL.sRGBWrite = previousSRGB; RenderTexture.active = previous; }
        }
        internal static Texture2D CreatePattern()
        {
            var pattern = new Texture2D(8, 4, TextureFormat.RGB24, false, true) { name = "HeadCamera.TestPattern", filterMode = FilterMode.Point };
            try
            {
                Color[] bars = { Color.white, Color.yellow, Color.cyan, Color.green, Color.magenta, Color.red, Color.blue, Color.black };
                var pixels = new Color[32];
                for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++) pixels[y * 8 + x] = y == 0 ? new Color(x / 7f, x / 7f, x / 7f, 1) : bars[x];
                pattern.SetPixels(pixels); pattern.Apply(false, false);
                return pattern;
            }
            catch { UnityEngine.Object.Destroy(pattern); throw; }
        }
        public void Dispose()
        {
            Texture = null;
            try { if (output != null) { output.Release(); UnityEngine.Object.Destroy(output); } }
            finally
            {
                if (tonemap != null) UnityEngine.Object.Destroy(tonemap);
                output = null; tonemap = null;
            }
        }
    }
}
