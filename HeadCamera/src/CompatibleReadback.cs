using System;
using UnityEngine;
namespace PreLuna.HeadCamera
{
    internal static class CompatibleReadback
    {
        internal static Texture2D Read(RenderTexture source)
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                throw new NotSupportedException("诊断采样需要ARGBFloat中转，当前GPU不支持；不生成无效像素统计");
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture staging = null;
            Texture2D copy = null;
            try
            {
                // Unity 2019 cannot ReadPixels RGB111110Float directly into
                // RGBAFloat (actual player log: formats 74 -> 52 unsupported).
                staging = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                if (!staging.Create()) throw new InvalidOperationException("诊断采样中转纹理创建失败");
                GL.sRGBWrite = false;
                Graphics.Blit(source, staging);
                RenderTexture.active = staging;
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                copy.Apply(false, false);
                return copy;
            }
            catch { if (copy != null) UnityEngine.Object.Destroy(copy); throw; }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (staging != null) { staging.Release(); UnityEngine.Object.Destroy(staging); }
            }
        }
    }
}
