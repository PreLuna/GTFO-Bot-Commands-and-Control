using System;
using System.Collections.Generic;
using System.Reflection;
using PreLuna.HeadCamera;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
static class FogCompatibilityTests {
    static int checks;static void Check(bool v,string why){checks++;if(!v)throw new Exception(why);}
    static bool Local(PreLitVolume v,bool initial=false){var args=new object[]{v,initial};typeof(FogCompatibility).GetMethod("Local",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);return (bool)args[1];}
    static void Set(string name,object value)=>typeof(FogCompatibility).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value);
    static void Main(){
        var d=Shader.PropertyToID("_FogDensity");var c=Shader.PropertyToID("_CameraFogDensity");var h=Shader.PropertyToID("_DensityHeightParams");var a=Shader.PropertyToID("_FogAmbientColorAbsorbtion");var s=Shader.PropertyToID("_FogSphereColorDensity");
        Shader.SetGlobalFloat(d,.004f);Shader.SetGlobalFloat(c,.006f);Shader.SetGlobalVector(h,new(1));Shader.SetGlobalVector(a,new(2));Shader.SetGlobalVectorArray(s,new(new[]{new Vector4(3),new Vector4(4)}));
        var saved=new FogShaderState();Shader.SetGlobalFloat(d,.02f);Shader.SetGlobalFloat(c,.03f);Shader.SetGlobalVector(h,new(10));Shader.SetGlobalVector(a,new(20));Shader.SetGlobalVectorArray(s,new(new[]{new Vector4(30),new Vector4(40)}));saved.Restore();
        Check(Shader.GetGlobalFloat(d)==.004f&&Shader.GetGlobalFloat(c)==.006f,"恢复进入副相机前的减雾值，不能用原始FogSettings覆盖");
        Check(Shader.GetGlobalVector(h).x==1&&Shader.GetGlobalVector(a).x==2,"高度与吸收颜色一并恢复");
        Check(Shader.GetGlobalVectorArray(s)[1].x==4,"局部雾球颜色密度向量恢复");
        try{Shader.SetGlobalFloat(d,7);throw new Exception("render failure");}catch{}finally{saved.Restore();}
        Check(Shader.GetGlobalFloat(d)==.004f,"异常路径恢复同样的快照");
        string[] textureNames={"_LitVolume","_FogVolume","_DensityNoise","_IndirectVolume","_ReflectionVolume","_LitBufferCopy"};
        foreach(string name in textureNames){int id=Shader.PropertyToID(name);var mainTexture=new Texture();Shader.SetGlobalTexture(id,mainTexture);var scope=new FogShaderState();
            for(int i=0;i<10;i++){var feedTexture=new Texture();try{Shader.SetGlobalTexture(id,feedTexture);throw new Exception("capture failure");}catch{}finally{scope.Restore();}feedTexture.Destroyed=true;
                Check(ReferenceEquals(Shader.GetGlobalTexture(id),mainTexture)&&!Shader.GetGlobalTexture(id).Destroyed,"Q释放副相机后全局纹理必须仍指向本体 "+name);}}
        int peak=Shader.PropertyToID("_NearPeak"),size=Shader.PropertyToID("_LitVolumeSize");Shader.SetGlobalFloat(peak,.2f);Shader.SetGlobalVector(size,new(128));var parameters=new FogShaderState();Shader.SetGlobalFloat(peak,.9f);Shader.SetGlobalVector(size,new(16));parameters.Restore();
        Check(Shader.GetGlobalFloat(peak)==.2f&&Shader.GetGlobalVector(size).x==128,"体积纹理对应的峰值和尺寸恢复");
        var empty=new FogShaderState();int optional=Shader.PropertyToID("_IndirectVolume");Shader.SetGlobalTexture(optional,new Texture());empty.Restore();
        FogCompatibility.Install(new HarmonyLib.Harmony());Check(!Local(new()),"无可选插件不扩展减雾");
        Set("ready",typeof(FogFixture).GetField("ready"));Set("master",typeof(FogFixture).GetField("masterEnabled"));
        int restores=0;Set("restore",(Action<ClusteredRendering>)(_=>restores++));
        NativeCapture.Owned=new();FogFixture.ready=true;FogFixture.masterEnabled.Value=true;
        Check(Local(NativeCapture.Owned),"已就绪且开启时仅扩展登记副相机");
        Check(!Local(new()),"普通外部相机不加入白名单");
        FogFixture.ready=false;Check(!Local(NativeCapture.Owned),"ready保护保持有效");FogFixture.ready=true;
        FogFixture.masterEnabled.Value=false;Check(!Local(NativeCapture.Owned),"用户关闭减雾时副相机也不强制开启");
        Check(Local(new(),true),"原先识别的本体相机结果保持有效");FogCompatibility.Restore(new());Check(restores==1,"异常收尾可调用原减雾作用域恢复");
        Console.WriteLine("PASS: "+checks+" actual fog shader snapshot/optional camera whitelist/configuration checks; shader and native rendering mocked.");
    }
}
public static class FogFixture {public static bool ready;public static Setting masterEnabled=new();public class Setting {public bool Value{get;set;}}}
public class ClusteredRendering {}public class PreLitVolume {}
namespace HarmonyLib {public class Harmony {public void Patch(MethodInfo m,HarmonyMethod postfix=null){}}public class HarmonyMethod {public HarmonyMethod(Type t,string n){}}public static class AccessTools {public static FieldInfo Field(Type t,string n)=>t.GetField(n,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);public static MethodInfo Method(Type t,string n,Type[] a)=>t.GetMethod(n,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public,null,a,null);}}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {public class Il2CppStructArray<T> {readonly T[] data;public Il2CppStructArray(T[] values){data=(T[])values.Clone();}public int Length=>data.Length;public T this[int i]=>data[i];public Il2CppStructArray<T> Copy()=>new(data);}}
namespace UnityEngine {
    public class Texture { public bool Destroyed; }
    public struct Vector4 {public float x;public Vector4(float v){x=v;}}
    public static class Shader { static Dictionary<int,Texture> textures=new();public static Texture GetGlobalTexture(int id)=>textures.GetValueOrDefault(id);public static void SetGlobalTexture(int id,Texture t)=>textures[id]=t;static Dictionary<string,int> ids=new();static Dictionary<int,float> floats=new();static Dictionary<int,Vector4> vectors=new();static Dictionary<int,Il2CppStructArray<Vector4>> arrays=new();public static int PropertyToID(string n){if(!ids.ContainsKey(n))ids[n]=ids.Count;return ids[n];}public static float GetGlobalFloat(int id)=>floats.GetValueOrDefault(id);public static void SetGlobalFloat(int id,float v){floats[id]=v;}public static Vector4 GetGlobalVector(int id)=>vectors.GetValueOrDefault(id);public static void SetGlobalVector(int id,Vector4 v){vectors[id]=v;}public static Il2CppStructArray<Vector4> GetGlobalVectorArray(int id)=>arrays.GetValueOrDefault(id)?.Copy();public static void SetGlobalVectorArray(int id,Il2CppStructArray<Vector4> v){arrays[id]=v.Copy();}}
}
namespace PreLuna.HeadCamera {public static class NativeCapture {public static PreLitVolume Owned;public static bool OwnsFog(PreLitVolume v)=>v==Owned;}public class Plugin {public static Plugin Instance=new();public Logger Log=new();public class Logger {public void LogInfo(string s){}public void LogWarning(string s){}}}}
