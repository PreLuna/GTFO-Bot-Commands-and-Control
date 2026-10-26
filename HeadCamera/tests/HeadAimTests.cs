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
        string exitReason=null;
        Check(!HeadCameraApi.RegisterControlExitReason(new object(),reason=>{}),"其他模块不能改写退出来源接收器");
        Check(HeadCameraApi.RegisterControlExitReason(owner,reason=>exitReason=reason),"所属控制器登记退出来源");
        HeadCameraApi.ExitControlFor("T键退出接管");Check(exitReason=="T键退出接管","真实来源传到所属控制器");
        Check(!HeadCameraApi.ReportControlNotice(new object(),"foreign"),"其他模块不能伪造接管拒绝提示");
        Check(HeadCameraApi.ReportControlNotice(owner,"仅房主可接管")&&CameraRuntime.Current.LastControlNotice=="仅房主可接管","所属控制器可以发布具体拒绝原因");
        CameraRuntime.Current.OnMainThread=false;
        Check(!HeadCameraApi.ReportControlNotice(owner,"wrong thread"),"非主线程不能写原生提示UI");CameraRuntime.Current.OnMainThread=true;
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
        Check(!HeadCameraApi.RegisterControlMarker(new object(),()=>null),"标记展示只接受登记控制器");
        Check(HeadCameraApi.RegisterControlMarker(owner,()=>new ControlMarker(target,new(.4f,.6f,2),"地面",true)),"登记原标记提供者");
        HeadCameraApi.ProjectMarker(new UnityEngine.Camera());Check(HeadCameraApi.MarkerProjected&&HeadCameraApi.MarkerViewport.x==.4f,"使用拍摄相机投影标记");
        HeadCameraApi.ClearControlView(owner);HeadCameraApi.ProjectMarker(new UnityEngine.Camera());Check(HeadCameraApi.MarkerProjected,"结束会话清显示状态，保留控制器注册供下次使用");
        Check(HeadCameraApi.ReportControlFeedback(owner,"已标记地面")&&CameraRuntime.Current.LastControlNotice=="已标记地面","指令成功进入窗口反馈");
        CameraRuntime.Current.Selected=new CameraTarget(target.Slot,999,target.OwnerIdentity,target.Name,target.IsBot);Check(!HeadCameraApi.TryMarker(out _),"展示标记不能跨机器人身份");CameraRuntime.Current.Selected=target;
        HeadCameraApi.RecordControlAim(view);HeadCameraApi.UnregisterControlInput(owner);Check(!HeadCameraApi.TryGetControlAim(out _),"卸载清除射线和接管回调");
        exitReason=null;HeadCameraApi.ExitControlFor("after unload");Check(exitReason==null,"卸载清理旧退出来源回调");
        Check(!HeadCameraApi.ReportControlNotice(owner,"after unload"),"注销后旧控制器不能发布提示");
        Console.WriteLine("PASS: "+checks+" 生产摄像头射线缓存／过期／隐藏／身份／退出清理检查（渲染替身）");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
