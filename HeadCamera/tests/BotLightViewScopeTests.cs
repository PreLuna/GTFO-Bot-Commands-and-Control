using System;
using System.Collections.Generic;
using PreLuna.HeadCamera;
static class BotLightViewScopeTests
{
    static int checks;
    static void Check(bool v, string why) { checks++; if (!v) throw new Exception(why); }
    static void Main()
    {
        var target = new Player.PlayerAgent(); var local = new Player.PlayerAgent(); local.Owner.IsBot = false;
        Player.PlayerManager.Local = local;
        var culler = target.Bot.Inventory.m_flashlightCLight;
        var spot = culler.m_clusterLight.TryCast<CL_SpotLight>(); var body = local.Inventory.m_flashlightCLight.m_clusterLight.TryCast<CL_SpotLight>();
        local.Inventory.m_flashlightCLight.IsFPSPlayerLight = true;
        body.IsFPSFlashlight = true;
        var priorShadow = new CL_ShadowGenerator(); CL_ShadowAtlas.ForcedShadowRender = priorShadow;
        UnityEngine.Shader.Index = 19;
        using (var scope = BotLightViewScope.For(target)) {
            Check(scope != null && culler.m_isFPSPlayerLight && spot.IsFPSFlashlight && !body.IsFPSFlashlight, "副相机独占FPS灯角色");
            Check(CL_ShadowAtlas.ForcedShadowRender == spot.ShadowGenerator, "现有原生灯阴影生成器用于本视角");
            CL_ShadowAtlas.ForcedShadowRender = null; scope.BeforeRender();
            Check(CL_ShadowAtlas.ForcedShadowRender == spot.ShadowGenerator, "原生排序之后重申副相机阴影来源");
            UnityEngine.Shader.Index = 3;
            using (var nested = BotLightViewScope.For(target)) { UnityEngine.Shader.Index = 5; }
            Check(UnityEngine.Shader.Index == 3 && spot.IsFPSFlashlight && !body.IsFPSFlashlight, "嵌套恢复外层字段／索引");
        }
        Check(!culler.m_isFPSPlayerLight && !spot.IsFPSFlashlight && body.IsFPSFlashlight, "退出恢复双方FPS灯字段");
        Check(CL_ShadowAtlas.ForcedShadowRender == priorShadow && UnityEngine.Shader.Index == 19, "恢复本体强制阴影与shader索引");
        try { using var scope = BotLightViewScope.For(target); UnityEngine.Shader.Index = 7; throw new Exception("render failed"); } catch { }
        Check(body.IsFPSFlashlight && !spot.IsFPSFlashlight && UnityEngine.Shader.Index == 19, "渲染异常仍归还全部视角状态");
        var once = BotLightViewScope.For(target); once.Dispose(); UnityEngine.Shader.Index = 21; once.Dispose();
        Check(UnityEngine.Shader.Index == 21, "重复清理不覆盖主相机新状态");
        Check(BotLightViewScope.For(local) == null && BotLightViewScope.For(null) == null, "非机器人／无对象不借用灯角色");
        spot.m_isOn = false;
        using(var retry=BotLightViewScope.For(target,out string attempted)){
            Check(retry!=null&&spot.m_isOn&&spot.ShadowGenerator.IsForced&&attempted.Contains("恢复"),"开灯意图有效时可以恢复临时禁用的灯，并同步原生阴影强制位");
        }
        Check(!spot.m_isOn&&!spot.ShadowGenerator.IsForced&&local.Inventory.m_flashlightCLight.IsFPSPlayerLight&&body.ShadowGenerator.IsForced,"恢复借用的开关及双方阴影强制状态");
        target.Inventory.WantsFlashlightEnabled=false;
        Check(BotLightViewScope.For(target,out string off)==null&&off.Contains("开灯指令"),"未收到开灯指令时不强行打开灯");target.Inventory.WantsFlashlightEnabled=true;spot.m_isOn=true;
        CullingSystem.C_CullingManager.VisibleSpotLights.Add(culler);
        Check(NativeBotLamp.Inspect(target).RenderReady,"有效参数、数据、阴影和可见灯登记才算渲染就绪");
        spot.Data.InvRangeSqr=float.NaN;Check(!NativeBotLamp.Inspect(target).RenderReady,"无效聚类数据不判定就绪");spot.Data.InvRangeSqr=.01f;
        spot.ShadowGenerator.Allocated=false;Check(NativeBotLamp.Inspect(target).Reason.Contains("分配"),"阴影未分配有明确原因");spot.ShadowGenerator.Allocated=true;
        culler.ThrowOnce=true;
        Check(BotLightViewScope.For(target,out string failed)==null&&failed.Contains("失败")&&!spot.IsFPSFlashlight&&!culler.IsFPSPlayerLight&&!spot.ShadowGenerator.IsForced&&body.IsFPSFlashlight&&body.ShadowGenerator.IsForced,"原生角色设置失败回滚双方状态，不关闭摄像头");
        spot.ShadowGenerator.IsForced=true;
        using(var mixed=BotLightViewScope.For(target)){Check(mixed!=null,"已有额外强制阴影状态仍可借用角色");}
        Check(spot.ShadowGenerator.IsForced&&!culler.IsFPSPlayerLight,"恢复与FPS位不同的原始强制阴影状态");spot.ShadowGenerator.IsForced=false;
        foreach (float value in new[]{0f,float.NaN,float.PositiveInfinity}) { spot.UnityLight.range=value;Check(BotLightViewScope.For(target)==null,"无效范围不进入副相机灯scope"); }
        spot.UnityLight.range = 11; spot.UnityLight.intensity = 0;Check(BotLightViewScope.For(target)==null,"零强度不强行激活");
        spot.UnityLight.intensity = .35f;spot.UnityLight.spotAngle = 180;Check(BotLightViewScope.For(target)==null,"非法锥角不提交");
        Check(!spot.IsFPSFlashlight && body.IsFPSFlashlight && CL_ShadowAtlas.ForcedShadowRender == priorShadow, "无效参数路径没有残留全局状态");
        var missing = new Player.PlayerAgent();missing.Bot=null;
        Check(BotLightViewScope.For(missing,out string noBot)==null&&noBot.Contains("背包"),"缺少机器人背包不再只记录inactive");
        missing = new();missing.Inventory.m_flashlightCLight=null;
        Check(BotLightViewScope.For(missing,out string noCull)==null&&noCull.Contains("裁剪器"),"裁剪器缺失有具体原因");
        missing = new();missing.Inventory.m_flashlightCLight.m_clusterLight=new CL_Light();
        Check(BotLightViewScope.For(missing,out string wrongType)==null&&wrongType.Contains("不是聚光灯"),"聚类灯类型错配有具体原因");
        missing = new();missing.Inventory.m_flashlight=new UnityEngine.Light();
        Check(BotLightViewScope.For(missing,out string mismatch)==null&&mismatch.Contains("不一致"),"背包与渲染灯不是同一对象时拒绝借用");
        Console.WriteLine("PASS: " + checks + " 原生人机灯副视角scope／嵌套／异常恢复检查（替身，不代表真实照明验收）");
    }
}
public class NativeObject {static int serial;public IntPtr Pointer=new(++serial);public T TryCast<T>() where T:class=>this as T;}
public class CL_ShadowGenerator:NativeObject {public bool IsForced;public bool Allocated=true;}
public static class CL_ShadowAtlas {public static CL_ShadowGenerator ForcedShadowRender;}
public struct sCL_SpotLight {public UnityEngine.Vector3 Position,Irradiance;public float InvRangeSqr,InvNearRange,ShadowIndex;}
public class CL_Light:NativeObject {public bool m_isOn=true,IsActive=true;public int ActiveID=2;public UnityEngine.Light UnityLight=new();}
public class CL_SpotLight:CL_Light {public bool IsFPSFlashlight;public int ShadowID=>ShadowGenerator.Allocated?0:-1;public CL_ShadowGenerator ShadowGenerator=new();public sCL_SpotLight Data=new(){Irradiance=new(1,1,1),InvRangeSqr=.01f,InvNearRange=.5f,ShadowIndex=0};}
public static class ClusteredRendering {public static int _FPSSpotIndex=5;}
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
 public class Transform {public string position="pos",forward="dir";}
 public class Light:NativeObject {public float range=11,intensity=.35f,spotAngle=40;public Color color=new(1,1,1,1);public Transform transform=new();}
 public static class Shader {public static int Index;public static int GetGlobalInt(int id)=>Index;public static void SetGlobalInt(int id,int v){Index=v;}}
}
namespace CullingSystem {
 public class C_Light:NativeObject {public bool IsShown=true;public CL_Light m_clusterLight=new CL_SpotLight();public UnityEngine.Light m_unityLight;}
 public class C_LightDynamic:C_Light {
  public bool m_isFPSPlayerLight,ThrowOnce;public int NativeRoleWrites;
  public C_LightDynamic(){m_unityLight=m_clusterLight.UnityLight;}
  public bool IsFPSPlayerLight {get=>m_isFPSPlayerLight;set{NativeRoleWrites++;m_isFPSPlayerLight=value;m_clusterLight.TryCast<CL_SpotLight>().ShadowGenerator.IsForced=value;if(value&&ThrowOnce){ThrowOnce=false;throw new Exception("native role failed");}}}
  public void SetOn(bool value){m_clusterLight.m_isOn=value;}
 }
 public static class C_CullingManager {public static List<C_Light> VisibleSpotLights=new();}
}
public class PlayerInventoryBase {public bool FlashlightEnabled=true,WantsFlashlightEnabled=true;public CullingSystem.C_LightDynamic m_flashlightCLight=new();public UnityEngine.Light m_flashlight;public PlayerInventoryBase(){m_flashlight=m_flashlightCLight.m_unityLight;}}
namespace Player {
 public class Owner {public bool IsBot=true;}
 public class PlayerAIBot {public PlayerInventoryBase Inventory=new();}
 public class PlayerAgent:NativeObject {public Owner Owner=new();public PlayerAIBot Bot=new();public PlayerInventoryBase Inventory=>Bot.Inventory;public T GetComponent<T>() where T:class=>Bot as T;}
 public static class PlayerManager {public static PlayerAgent Local;public static PlayerAgent GetLocalPlayerAgent()=>Local;}
}
