using System;
using PreLuna.HeadCamera;
using UnityEngine;
static class CompatibleReadbackTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Main()
    {
        var previous=new RenderTexture(8,8,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
        foreach(var format in new[]{RenderTextureFormat.ARGBHalf,RenderTextureFormat.RGB111110Float}){
            RenderTexture.active=previous;GL.sRGBWrite=true;var source=new RenderTexture(1280,720,0,format,RenderTextureReadWrite.Linear);
            var copy=CompatibleReadback.Read(source);
            Check(copy.Read&&copy.width==1280&&copy.height==720,"实际Readback从格式匹配的中转读取");
            Check(RenderTexture.Last.format==RenderTextureFormat.ARGBFloat&&RenderTexture.Last.Released&&RenderTexture.Last.Destroyed,"中转格式为ARGBFloat且正常释放");
            Check(RenderTexture.active==previous&&GL.sRGBWrite,"归还激活RT和sRGB状态");
            Check(!source.Released&&!source.Destroyed,"不释放调用者的画面纹理");
        }
        foreach(var phase in new[]{"create","blit","read"}){
            RenderTexture.active=previous;GL.sRGBWrite=true;Fixture.Fail=phase;bool rejected=false;
            try{CompatibleReadback.Read(previous);}catch{rejected=true;}
            Check(rejected&&RenderTexture.active==previous&&GL.sRGBWrite,"失败阶段仍归还渲染状态 "+phase);
            Check(RenderTexture.Last.Released&&RenderTexture.Last.Destroyed,"失败阶段也释放中转 "+phase);
        }
        Fixture.Fail=null;Fixture.Supported=false;bool unsupported=false;
        try{CompatibleReadback.Read(previous);}catch(NotSupportedException){unsupported=true;}
        Check(unsupported,"不支持中转格式时明确失败，不生成未初始化数据");
        Console.WriteLine("PASS: "+checks+" actual compatible readback format/state/error checks; Unity transfer calls mocked.");
    }
}
static class Fixture {internal static string Fail;internal static bool Supported=true;}
namespace UnityEngine
{
    public class Object {public bool Destroyed;public static void Destroy(Object value)=>value.Destroyed=true;}
    public class Texture:Object {public int width,height;}
    public enum RenderTextureFormat {ARGBHalf,RGB111110Float,ARGBFloat}
    public enum RenderTextureReadWrite {Linear}
    public enum TextureFormat {RGBAFloat}
    public struct Rect {public Rect(int x,int y,int w,int h){}}
    public class RenderTexture:Texture
    {
        public static RenderTexture active,Last;public RenderTextureFormat format;public bool Released;
        public RenderTexture(int w,int h,int depth,RenderTextureFormat f,RenderTextureReadWrite rw){width=w;height=h;format=f;Last=this;}
        public bool Create()=>Fixture.Fail!="create";public void Release()=>Released=true;
    }
    public class Texture2D:Texture
    {
        public bool Read;public Texture2D(int w,int h,TextureFormat format,bool mip,bool linear){width=w;height=h;}
        public void ReadPixels(Rect r,int x,int y,bool mip){if(RenderTexture.active.format!=RenderTextureFormat.ARGBFloat)throw new Exception("Remapping unsupported");if(Fixture.Fail=="read")throw new Exception("read failed");Read=true;}
        public void Apply(bool mip,bool readable){}
    }
    public static class GL {public static bool sRGBWrite;}
    public static class SystemInfo {public static bool SupportsRenderTextureFormat(RenderTextureFormat format)=>Fixture.Supported;}
    public static class Graphics {public static void Blit(Texture src,RenderTexture dst){if(Fixture.Fail=="blit")throw new Exception("blit failed");}}
}
