using System;
using Player;
using UnityEngine;
using PreLuna.HeadCamera;
static class FirstPersonCaptureTests
{
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static bool Near(float a,float b)=>Math.Abs(a-b)<.00001f;
    static void Main()
    {
        var target=new PlayerAgent(); var camera=new Camera(); camera.transform.position=new(10,1.8f,20);camera.transform.rotation=new(){Marker=7};
        var head=new SkinnedMeshRenderer();var torso=new MeshRenderer();var arms=new MeshRenderer{forceRenderingOff=true};var leg=new MeshRenderer();var other=new MeshRenderer();
        target.PlayerSyncModel.m_gfxHead=new[]{new GameObject{Renderers=new(){head}}};
        target.PlayerSyncModel.m_gfxTorso=new[]{new GameObject{Renderers=new(){torso,head}}};
        target.PlayerSyncModel.m_gfxArms=new[]{new GameObject{Renderers=new(){arms}}};
        target.PlayerSyncModel.m_gfxLegs=new[]{new GameObject{Renderers=new(){leg}}};
        var inv=target.Inventory;var light=inv.m_flashlight;var spot=inv.m_flashlightCLight.m_clusterLight.TryCast<CL_SpotLight>();
        light.transform.localPosition=new(2,3,4);light.transform.localRotation=new(){Marker=2};inv.m_flashlightCameraOffset=new(.05f,-.1f,0);
        using(var scope=new FirstPersonRenderScope(target,camera))
        {
            Check(head.forceRenderingOff&&torso.forceRenderingOff&&arms.forceRenderingOff,"只对拍摄隐藏头、上身、远端手臂");
            Check(!leg.forceRenderingOff&&!other.forceRenderingOff,"腿和其他玩家不受影响");
            Check(Near(light.transform.position.x,10.05f)&&Near(light.transform.position.y,1.7f)&&light.transform.rotation.Marker==7,"同一相机姿态加原生灯偏移");
            Check(spot.CachedPosition.x==light.transform.position.x&&spot.Transforms==1,"原生灯变换缓存随副视角更新");
            using(var nested=new FirstPersonRenderScope(target,camera)){}
            Check(head.forceRenderingOff&&light.transform.rotation.Marker==7,"嵌套归还外层视角");
        }
        Check(!head.forceRenderingOff&&!torso.forceRenderingOff&&arms.forceRenderingOff,"精确保留已有隐藏位，重复网格不覆盖快照");
        Check(light.transform.localPosition.x==2&&light.transform.localRotation.Marker==2&&spot.CachedPosition.x==2,"正常退出归还灯姿态和缓存");
        Check(Near(light.range,11)&&Near(light.intensity,.35f)&&inv.WantsFlashlightEnabled,"不改原生灯参数和常亮意图");
        try{using var scope=new FirstPersonRenderScope(target,camera);throw new Exception("camera render");}catch{}
        Check(!head.forceRenderingOff&&light.transform.localPosition.x==2,"拍摄异常照样恢复");
        inv.m_flashlightCLight.ThrowTransformOnce=true;
        bool failed=false;try{using var scope=new FirstPersonRenderScope(target,camera);}catch{failed=true;}
        Check(failed&&!head.forceRenderingOff&&!torso.forceRenderingOff&&light.transform.localRotation.Marker==2,"构造中原生刷新异常回滚");
        var once=new FirstPersonRenderScope(target,camera);once.Dispose();head.forceRenderingOff=true;once.Dispose();
        Check(head.forceRenderingOff,"重复释放不覆盖后续状态");head.forceRenderingOff=false;
        var badRestore=new FirstPersonRenderScope(target,camera);torso.ThrowRestore=true;
        failed=false;try{badRestore.Dispose();}catch{failed=true;}
        Check(failed&&!head.forceRenderingOff&&light.transform.localPosition.x==2,"单网格恢复异常不阻断其他模型和灯归还");torso.forceRenderingOff=false;
        inv.m_flashlightCLight.ThrowTransformOnce=true;torso.ThrowRestore=true;failed=false;
        try{using var scope=new FirstPersonRenderScope(target,camera);}catch(CaptureRestoreException){failed=true;}
        Check(failed&&!head.forceRenderingOff&&light.transform.localPosition.x==2,"准备失败加恢复失败升级为不可继续拍摄，避免继续污染本体");torso.forceRenderingOff=false;
        inv.WantsFlashlightEnabled=false;
        using(var scope=new FirstPersonRenderScope(target,camera)){Check(light.transform.localPosition.x==2,"AI关灯模式不借用灯姿态");}inv.WantsFlashlightEnabled=true;
        Check(Near(FirstPersonCapture.Aspect(false,21f/9),16f/9)&&Near(FirstPersonCapture.Aspect(true,21f/9),21f/9),"观察保持16:9、接管跟随原生宽高比");
        Check(FirstPersonCapture.Height(1280,21f/9)==549&&FirstPersonCapture.Height(1280,4f/3)==960,"纹理尺寸覆盖超宽和4:3");
        var eye=new Vector3(10,1.8f,20);float radius=0;int calls=0;
        Physics.Check=(p,r,m,q)=>{radius=r;calls++;Check(p.x==eye.x&&p.y==eye.y&&p.z==eye.z,"近裁剪检测不前移眼点");Check(m==11&&q==QueryTriggerInteraction.Ignore,"覆盖墙、交互阻挡和动态层，忽略触发器");return false;};
        Check(Near(FirstPersonCapture.SafeNear(eye,.06f,67,16f/9),.06f)&&calls==1,"空旷场景保留原生近裁剪");
        Check(radius>.06f,"检测包含近裁剪面角点而非仅中心射线");
        Physics.Check=(p,r,m,q)=>r>.05f;
        float reduced=FirstPersonCapture.SafeNear(eye,.06f,90,21f/9);
        Check(reduced<.06f&&reduced>=.01f,"贴墙收近裁剪面，不向墙外挪眼点");
        Physics.Check=(p,r,m,q)=>true;
        Check(FirstPersonCapture.SafeNear(eye,.06f,67,16f/9)==0,"眼点嵌入几何时临时遮挡，不穿墙也不触发故障冷却");
        Physics.Check=(p,r,m,q)=>false;
        Check(Near(FirstPersonCapture.SafeNear(eye,.06f,67,16f/9),.06f),"移开后立即恢复原生裁剪距离");
        Console.WriteLine("PASS: "+checks+" 第一人称拍摄、模型、灯姿态、异常恢复与近裁剪检查（替身，非GPU实测）");
    }
}
namespace PreLuna.HeadCamera {internal class CaptureRestoreException:Exception{internal CaptureRestoreException(string message,Exception error):base(message,error){}}}
