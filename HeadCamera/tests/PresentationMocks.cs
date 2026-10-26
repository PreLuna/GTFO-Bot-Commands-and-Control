using System;
using System.Collections.Generic;

// GPU operations are simulated; tests exercise the production ownership/state flow only.
namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed;
        public static Material Instantiate(Material source) { var result = new Material { shader = source.shader }; return result; }
        public static void Destroy(Object value) { value.Destroyed = true; }
    }
    public class Texture : Object { }
    public enum RenderTextureFormat { ARGBHalf, RGB111110Float }
    public enum RenderTextureReadWrite { Linear }
    public enum TextureFormat { RGB24 }
    public enum FilterMode { Bilinear, Point }
    public class RenderTexture : Texture
    {
        public static RenderTexture active;
        public static readonly List<RenderTexture> Created = new();
        public int width, height;
        public bool useMipMap, autoGenerateMips, Released;
        public FilterMode filterMode;
        public RenderTextureFormat format;
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite mode = RenderTextureReadWrite.Linear)
        { this.width = width; this.height = height; this.format = format; Created.Add(this); }
        public bool Create() => true;
        public void Release() { Released = true; }
    }
    public class Texture2D : Texture
    {
        public static Texture2D Last;
        public static bool FailUpload;
        public FilterMode filterMode;
        public Color[] Pixels;
        public Texture2D(int width, int height, TextureFormat format, bool mips, bool linear) { Last = this; }
        public void SetPixels(Color[] pixels) { if (FailUpload) throw new Exception("upload failure"); Pixels = pixels; }
        public void Apply(bool mips, bool unreadable) { }
    }
    public class Shader : Object { public bool isSupported = true; }
    public class Material : Object { public Shader shader = new(); }
    public static class SystemInfo
    {
        public static bool Supported = true;
        public static bool SupportsRenderTextureFormat(RenderTextureFormat format) => Supported;
    }
    public static class GL { public static bool sRGBWrite; }
    public static class Graphics
    {
        public static bool Fail;
        public static Material LastMaterial;
        public static Texture LastSource;
        public static void Blit(Texture source, RenderTexture dest) => Blit(source, dest, null);
        public static void Blit(Texture source, RenderTexture dest, Material material)
        {
            RenderTexture.active = dest; LastSource = source; LastMaterial = material;
            if (Fail) throw new Exception("GPU failure");
        }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1, 1, 1, 1);
        public static Color yellow => new(1, 1, 0, 1);
        public static Color cyan => new(0, 1, 1, 1);
        public static Color green => new(0, 1, 0, 1);
        public static Color magenta => new(1, 0, 1, 1);
        public static Color red => new(1, 0, 0, 1);
        public static Color blue => new(0, 0, 1, 1);
        public static Color black => new(0, 0, 0, 1);
    }
}
public static class RenderPipe
{
    public static UnityEngine.Material TonemapMaterial = new();
}
