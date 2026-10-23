using System;
using UnityEngine;
using PreLuna.HeadCamera;
static class HeadAimTests
{
    static int checks;
    static void Check(bool ok,string msg){checks++;if(!ok)throw new Exception(msg);}
    static int Main(){try{
        var owner=new object();bool busy=true;var target=new CameraTarget(1,101,201,"bot",true);
        CameraRuntime.Current.Selected=target;Time.unscaledTime=1;
        Check(HeadCameraApi.RegisterControlInput(owner,_=>true,()=>busy,()=>{},()=>"",.3f),"注册接管输入");
        Check(!HeadCameraApi.TryGetControlAim(out _),"尚未渲染不能使用虚构镜头射线");
        var view=new ControlView(target,new(20.08f,1.6f,20),new(1,0,0),80,true,false);
        HeadCameraApi.RecordControlAim(view);Check(HeadCameraApi.TryGetControlAim(out var ray)&&ray.Position.x==20.08f&&ray.Direction.x==1,"返回实际渲染位置，保留防穿墙后的偏移");
        Time.unscaledTime=1.76f;Check(!HeadCameraApi.TryGetControlAim(out _),"过期画面不提供标记射线");
        Time.unscaledTime=.9f;Check(!HeadCameraApi.TryGetControlAim(out _),"倒退的时间不能恢复旧画面");
        Time.unscaledTime=2;HeadCameraApi.RecordControlAim(view);CameraRuntime.Current.Opened=false;
        Check(!HeadCameraApi.TryGetControlAim(out _),"摄像头不可见时禁止标记");CameraRuntime.Current.Opened=true;
        CameraRuntime.Current.Selected=new CameraTarget(1,102,202,"new bot",true);Check(!HeadCameraApi.TryGetControlAim(out _),"同槽身份更换不能用旧镜头");
        CameraRuntime.Current.Selected=target;busy=false;Check(!HeadCameraApi.TryGetControlAim(out _),"退出接管后不可用旧画面");busy=true;
        HeadCameraApi.ClearControlView(new object());Check(HeadCameraApi.TryGetControlAim(out _),"其他模块不能清除所属接管画面");
        HeadCameraApi.ClearControlView(owner);Check(!HeadCameraApi.TryGetControlAim(out _),"所属控制器退出立即清除画面射线");
        HeadCameraApi.RecordControlAim(view with{Suspended=true});Check(!HeadCameraApi.TryGetControlAim(out _),"界面暂停的画面不能标记");
        HeadCameraApi.RecordControlAim(null);Check(!HeadCameraApi.TryGetControlAim(out _),"无纹理／渲染失败清空缓存");
        HeadCameraApi.RecordControlAim(view);HeadCameraApi.UnregisterControlInput(owner);Check(!HeadCameraApi.TryGetControlAim(out _),"卸载清除射线和接管回调");
        Console.WriteLine("PASS: "+checks+" 生产摄像头射线缓存／过期／隐藏／身份／退出清理检查（渲染替身）");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
