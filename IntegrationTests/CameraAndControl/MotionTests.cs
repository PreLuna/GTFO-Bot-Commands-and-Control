using System;
using UnityEngine;
using UnityEngine.AI;
using Player;
using AIGraph;
using PreLuna.MicroMaster;
using PreLuna.HeadCamera;
static class MotionTests
{
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
    static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
    static PlayerAIBot bot;
    static NativeManualWalk adapter;
    static ManualMotionRunner runner;
    static void Reset(){
        Physics.Wall=Physics.Occupied=Physics.FloorOnHorizontal=false;NavMesh.Slope=0;Physics.WallDistance=0;Physics.Casts=0;
        NavMesh.Missing=NavMesh.Blocked=false;NavMesh.Shift=default;AIG_CourseNode.Missing=false;
        bot=new();AIG_CourseNode.Next=bot.Agent.CourseNode;ControlRuntime.Current=new();
        adapter=new(bot);ControlRuntime.Current.Walk=adapter;runner=new(adapter);
    }
    static void MathAndView(){
        var diagonal=ManualMotionPolicy.Keys(true,false,false,true);
        Check(Near(diagonal.Right*diagonal.Right+diagonal.Forward*diagonal.Forward,1),"斜向不比直行快");
        Check(!ManualMotionPolicy.Keys(true,true,true,true).Moving,"相反按键抵消");
        var w=ManualMotionPolicy.World(new(0,1),90);Check(Near(w.X,1)&&Near(w.Z,0)&&w.Y==0,"W按观察水平朝向移动");
        var a=ManualMotionPolicy.World(new(-1,0),90);Check(Near(a.X,0)&&Near(a.Z,1),"A为观察方向左移");
        var s=ManualMotionPolicy.World(new(0,-1),0);Check(Near(s.Z,-1),"S后退");
        var look=new ManualLook();look.Reset(new(0,0,1));look.Apply(90,10,1,false);
        Check(Near(look.Yaw,90)&&Near(look.Pitch,-10)&&look.Direction.Y>0,"鼠标方向与仰视");
        look.Apply(0,10000,1,false);Check(look.Pitch==-80,"俯仰上限");
        look.Apply(0,-10000,1,false);Check(look.Pitch==80,"俯仰下限");
        look.Reset(new(0,0,1));look.Apply(0,10,1,true);Check(look.Pitch==10,"反转纵轴");
        look.Reset(new(1,0,0));Check(Near(look.Yaw,90)&&Near(look.Pitch,0),"重置到机器人正前方");
        look.Apply(float.NaN,0,1,false);Check(Near(look.Yaw,90),"无效鼠标输入不污染视角");
        var slow=new ManualLook();var fast=new ManualLook();slow.Reset(new(0,0,1));fast.Reset(new(0,0,1));
        for(int i=0;i<30;i++)slow.Apply(2,0,1,false);for(int i=0;i<60;i++)fast.Apply(1,0,1,false);
        Check(Near(slow.Yaw,fast.Yaw),"观察按输入增量更新，不随拍摄帧率变化");
        var eye=new StableEye();Check(Near(eye.Sample(0,1.6f,0),1.6f),"以身体位置和固定眼高初始化");
        float filtered=eye.Sample(.2f,1.6f,.02f);Check(filtered>1.6f&&filtered<1.8f,"垂直变化平滑");
        Check(Near(eye.Sample(10,1.6f,.03f),11.6f),"大位移重置而不拖出旧视点");
        eye.Reset();Check(Near(eye.Sample(0,1.6f,0),1.6f),"重新接管不保留旧眼高状态");
        Check(!ManualMotionPolicy.InputAllowed(true,true,true,false,true,false),"Q菜单打开时禁用微调");
        Check(!ManualMotionPolicy.InputAllowed(true,true,true,false,false,true),"执行指令时的输入保护入口");
        Check(!ManualMotionPolicy.InputAllowed(false,true,true,false,false,false),"无控制权不能移动");
        Check(!ManualMotionPolicy.InputAllowed(true,true,false,false,false,false),"失焦禁用");
        Check(!ManualMotionPolicy.InputAllowed(true,true,true,true,false,false),"聊天禁用");
        Check(!ManualMotionPolicy.InputAllowed(true,false,true,false,false,false),"非FPS界面禁用");
        Check(ManualMotionPolicy.InputAllowed(true,true,true,false,false,false),"空闲正常操作可微调");
        Check(Near(WindowLayoutPolicy.Width(960,1920,1080),960),"默认放大宽度");
        float full=WindowLayoutPolicy.SpectatorWidth(1920,1080);
        Check(full>1750&&full<1920,"接管默认观战画面接近全屏，超过旧960上限");
        foreach(var screen in new[]{(1920f,1080f),(3840f,1080f),(360f,1920f),(1280f,720f)}) {
            var size=WindowLayoutPolicy.ReferenceSize(screen.Item1,screen.Item2);float spectator=WindowLayoutPolicy.SpectatorWidth(screen.Item1,screen.Item2);
            Check(spectator<=size.Width-31&&(spectator-12)*9/16+58<=size.Height-31,"观战布局保持16:9且完整留在屏幕内");
        }
        float width=WindowLayoutPolicy.Width(960,360,1920);Check(width<624&&width>80,"竖屏限制画面宽度");
        width=WindowLayoutPolicy.Width(960,3840,720);float scale=MathF.Sqrt(3840f/1920*720/1080f);
        Check((width-12)*9/16+58<720/scale,"超宽屏限制画面高度");
    }
    static void NativeAdapter(){
        Reset();Check(runner.Tick(true,new(0,1),0)&&bot.Requests==1&&adapter.IsOwn(bot.Last),"仅授权本次步行动作");
        Check(!adapter.IsOwn(new PlayerBotActionWalk.Descriptor(bot)),"不放行同机器人的其他步行动作");
        var original=bot.Last;var point=original.Path.First;Check(Near(point.z,1.2f)&&point.y==0,"短直线导航段，不发长程任务");
        for(int i=0;i<100;i++)runner.Tick(true,new(1,0),0);
        Check(bot.Requests==1&&ReferenceEquals(bot.Last,original)&&original.Path.Count==1,"一百帧复用同一动作与单段路径");
        Check(Near(original.Path.First.x,1.2f)&&Near(original.Path.First.z,0),"方向更新替换当前微调段");
        Check(bot.Agent.Position.sqrMagnitude==0,"适配器不直接写玩家位置");
        runner.Tick(true,new(0,0),0);Check(bot.Stops==1&&!runner.InputActive&&original.Path.Count==0,"松键清空路径并停止原版动作");
        runner.Stop();Check(bot.Stops==1,"重复停止幂等");
        Reset();runner.Tick(true,new(0,1),0);runner.Tick(false,new(0,1),0);
        Check(bot.Stops==1&&!runner.InputActive,"菜单打开，即使W仍按住也停止");
        Check(runner.Tick(true,new(0,1),0)&&bot.Requests==2,"菜单关闭后可创建下一动作");
        Reset();Physics.Wall=true;Physics.WallDistance=.02f;
        Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"贴墙时不提交移动");
        Reset();Physics.Wall=true;Physics.WallDistance=.5f;runner.Tick(true,new(0,1),0);
        Check(bot.Last.Path.First.z>0&&bot.Last.Path.First.z<.46f,"墙前使用更短导航段并保留安全间隙");
        Reset();NavMesh.Slope=.6f;Physics.FloorOnHorizontal=true;
        Check(runner.Tick(true,new(0,1),0)&&bot.Last.Path.First.y>0&&bot.Last.Path.First.z<1.2f,"斜坡先投影并缩短步长，沿坡面碰撞检测不把地面当墙");
        Reset();NavMesh.Slope=.4f;Check(runner.Tick(true,new(0,-1),0)&&bot.Last.Path.First.y<0,"下坡同样使用导航投影");
        Reset();NavMesh.Blocked=true;Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"导航边界不越过");
        Reset();NavMesh.Missing=true;Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"无导航不移动");
        Reset();Physics.Occupied=true;Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"终点占用不移动");
        Reset();NavMesh.Shift=new(.2f,0,0);Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"不侧向吸附到远处导航点");
        Reset();NavMesh.Shift=new(0,.5f,0);Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"不越过过高台阶");
        Reset();AIG_CourseNode.Next=new();Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"不跨关卡分层");
        Reset();AIG_CourseNode.Next.m_zone.DimensionIndex=1;Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"不跨维度");
        Reset();AIG_CourseNode.Missing=true;Check(!runner.Tick(true,new(0,1),0)&&bot.Requests==0,"无房间节点不移动");
        Reset();runner.Tick(true,new(0,1),0);ControlRuntime.Current.Owned=false;runner.Tick(true,new(0,1),0);
        Check(bot.Stops==1&&!runner.InputActive,"租约丢失立即停止");
        Reset();runner.Tick(true,new(0,1),0);bot.Agent.Owner=new();runner.Tick(true,new(0,1),0);
        Check(bot.Stops==1&&!runner.InputActive,"同槽所有者改变不延续移动");
        Reset();ControlRuntime.Current.AllowRequests=false;bool failed=false;
        try{runner.Tick(true,new(0,1),0);}catch{failed=true;}
        Check(failed&&!runner.InputActive&&bot.Requests==1,"原版拒绝后停止，不按帧重复请求");
        Reset();runner.Tick(true,new(0,1),0);bot.StopThrows=true;failed=false;
        try{runner.Stop();}catch{failed=true;}
        Check(failed&&!runner.InputActive&&bot.Last.Path.Count==0,"停止异常也先禁用输入并清空路径");
        Reset();runner.Tick(true,new(0,1),0);Physics.Occupied=true;runner.Tick(true,new(0,1),0);
        Check(bot.Stops==1&&bot.Requests==1,"移动中出现障碍则停止");
        Reset();runner.Tick(true,new(0,1),0);Check(adapter.RefreshBeforeNativeStep(new(1,0,0))&&Near(bot.Last.Path.First.x,1.2f),"原版移动前重新核验实际位置与方向");
        Physics.Occupied=true;Check(!adapter.RefreshBeforeNativeStep(new(1,0,0)),"运行顺序改变时仍在原版移动前发现新障碍");
    }
    public static int Main(){try{MathAndView();NativeAdapter();Console.WriteLine("PASS: "+checks+" 稳定视角／WASD／生产步行适配与碰撞边界检查（原生接口替身）");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
