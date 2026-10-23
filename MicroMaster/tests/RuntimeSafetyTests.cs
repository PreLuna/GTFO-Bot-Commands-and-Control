using System;
using Player;
using UnityEngine;
using PreLuna.Captain.Control;
using PreLuna.HeadCamera;
using PreLuna.MicroMaster;
static class RuntimeSafetyTests
{
    static int checks;
    static ControlRuntime runtime;
    static PlayerAgent body,bot;
    static readonly Service service=new();
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    sealed class Session:IControlTransaction {
        public bool Valid{get;set;}=true;public bool Paused=>true;
        public bool Acquire()=>true;public bool Renew()=>service.Lease;
        public void Quiesce(){}public void ReleaseLease(){}public void RestoreBeforeActivation(){}public void KeepPaused(){}public void Recall(){}
    }
    sealed class Service:IBotControlService,IBotControlCommandService,IBotControlStationService {
        internal ControlTransaction Transaction;
        internal Session Session;
        internal bool Lease=true,ThrowRelease,ReleasedWithBodyFree,LastRecall;
        internal int Releases;
        internal BotControlTarget Target;
        internal bool Guard,Detached,AllowAction,AllowTeleport;
        bool Same(long a,long o)=>a==Target.Agent&&o==Target.Owner;
        public bool Begin(BotControlTarget target,string source,out long ticket,out string reason){Target=target;Session=new();Transaction=new(Session,Time.unscaledTime);ticket=1;reason=null;return true;}
        public BotControlState Poll(long ticket,out string reason){Transaction.Tick(Time.unscaledTime);reason=Transaction.Reason;return Transaction.State;}
        public bool Renew(long ticket)=>Transaction.Renew(Time.unscaledTime);
        public void Release(long ticket,bool recall){Releases++;ReleasedWithBodyFree=!runtime.BlocksBody;LastRecall=recall;Transaction.Exit(recall);if(ThrowRelease)throw new Exception("service release");}
        public bool Controls(long a,long o)=>Same(a,o)&&Lease&&Session?.Valid==true&&Transaction?.ActiveAt(Time.unscaledTime)==true;
        public bool Mark(long t,BotControlRay r,out string reason){reason=null;return true;}
        public bool CommandBusy(long t,out string label){label=null;return false;}
        public bool CommandOwns(long a,long o)=>Detached&&Same(a,o);
        public bool AllowsAction(long a,long o,long d)=>AllowAction&&Same(a,o);
        public bool AllowsTeleport(long a,long o)=>AllowTeleport&&Same(a,o);
        public bool RecallProtected(long a,long o)=>Guard&&Same(a,o);
    }
    static PlayerAgent Agent(int slot,bool robot){var a=new PlayerAgent{PlayerSlotIndex=slot,Owner=new(){IsBot=robot}};a.Damage=new(){Owner=a};if(robot){a.Bot=new(){Agent=a};}return a;}
    static void Reset(bool begin=true){
        HeadCameraApi.ThrowClear=false;NativeManualWalk.ThrowStop=false;service.ThrowRelease=false;runtime?.Shutdown();
        Time.unscaledTime=0;Time.frameCount=0;Input.Held.Clear();Input.Down.Clear();Application.isFocused=true;FocusStateManager.CurrentState=eFocusState.FPS;
        Plugin.Enabled.Value=Plugin.ThreatExit.Value=true;Plugin.ThreatCount.Value=3;Plugin.ThreatRadius.Value=15;Plugin.ThreatConfirmation.Value=.25f;
        PlayerChatManager.InChatMode=false;HeadCameraApi.IsVisible=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;
        SNetwork.SNet.IsMaster=true;SNetwork.SNet.Master=new();LevelGeneration.Builder.CurrentFloor=new();PlayerManager.Current=new();
        body=Agent(0,false);bot=Agent(1,true);PlayerManager.Current.Slots[0]=body;PlayerManager.Current.Slots[1]=bot;
        HeadCameraApi.Selected=new(1,bot.Pointer.ToInt64(),bot.Owner.Pointer.ToInt64(),"bot",true);
        service.Lease=true;service.Releases=0;service.Transaction=null;service.Session=null;service.Guard=service.Detached=service.AllowAction=service.AllowTeleport=false;
        runtime=new((IntPtr)10000);runtime.Awake();runtime.OnEnable();runtime.Update();
        if(begin){Check(HeadCameraApi.Request(HeadCameraApi.Selected),"生产接管入口接受安全场景");runtime.Update();Check(runtime.BlocksBody,"生产运行器在激活后隔离本体");}
    }
    static void Tick(float now){Time.unscaledTime=now;Time.frameCount++;runtime.Update();}
    static void Exited(string reason,bool recall=true){Check(!runtime.Busy&&!runtime.BlocksBody,reason+"立即归还本体");Check(service.Releases==1&&service.ReleasedWithBodyFree&&service.LastRecall==recall,reason+"释放前本体已自由，召回参数正确");runtime.LateUpdate();Check(service.Releases==1,reason+"不重复释放");}
    static Enemies.EnemyAgent Enemy(float x=1,int mode=1){var e=new Enemies.EnemyAgent{Position=new(x,0,0),CourseNode=body.CourseNode,DimensionIndex=body.DimensionIndex};e.AI.Mode=mode;body.Dimension.AgentsInDimension.Add(e);return e;}
    static void Cases(){
        Check(BotControlApi.Register(service),"测试服务注册");
        Reset();runtime.OnApplicationFocus(false);Exited("失焦回调");runtime.OnApplicationFocus(true);Check(!runtime.Busy,"返回窗口不自动重接管");
        Reset();runtime.OnApplicationPause(true);Exited("后台暂停回调");
        Reset();runtime.OnDisable();Exited("组件停用");Check(!HeadCameraApi.Request(HeadCameraApi.Selected),"停用组件拒绝摄像头申请");
        Reset();runtime.StateChanging(eGameStateName.Lobby);Exited("切关前回调",false);
        Reset();runtime.Departing(bot);Exited("机器人离队前回调",false);
        Reset();runtime.Departing(body);Exited("本体离队前回调",false);
        Reset();runtime.Departing(Agent(2,true));Check(runtime.BlocksBody,"其他队员离队不影响当前接管");
        Reset();SNetwork.SNet.IsMaster=false;Check(!runtime.BlocksBody,"失去房主当帧输入门失效");Tick(.1f);Exited("房主丢失");
        Reset();SNetwork.SNet.Master=new();Check(!runtime.BlocksBody,"房主对象替换直接关闭输入门");Tick(.1f);Exited("房主对象改变",false);
        Reset();LevelGeneration.Builder.CurrentFloor=new();Tick(.1f);Exited("楼层更换",false);
        Reset();bot.IsBeingDespawned=true;Check(!runtime.BlocksBody,"离队标志直接归还输入");Tick(.1f);Exited("机器人销毁标志",false);
        Reset();var replacement=Agent(1,true);replacement.Pointer=bot.Pointer;replacement.Owner=bot.Owner;PlayerManager.Current.Slots[1]=replacement;Tick(.1f);Exited("同槽同地址不同实例重加",false);
        Reset();var local=Agent(0,false);local.Pointer=body.Pointer;local.Owner=body.Owner;PlayerManager.Current.Slots[0]=local;Tick(.1f);Exited("本体同地址新实例",false);
        Reset();service.Lease=false;runtime.LateUpdate();Exited("Update后租约失效");
        Reset();service.Session.Valid=false;Tick(.1f);Exited("服务主动判定身份无效");
        Reset();Input.Down.Add(KeyCode.F8);HeadCameraApi.ThrowClear=true;Tick(.1f);Exited("摄像头清理异常");
        Reset();Input.Held.Add(KeyCode.W);Tick(.1f);NativeManualWalk.ThrowStop=true;HeadCameraApi.ThrowClear=true;service.ThrowRelease=true;runtime.Exit("全部清理回调报错",true);Exited("清理异常");
        Reset();body.Damage.Health=99;Tick(.1f);Exited("本体受伤");
        Reset();body.Alive=false;Tick(.1f);Exited("本体倒地");
        Reset();Enemy();Enemy();Tick(.1f);Tick(.4f);Check(runtime.BlocksBody,"两名激怒敌人不足阈值");
        Enemy();Tick(.51f);Tick(.62f);Check(runtime.BlocksBody,"达到阈值仍有确认窗口");Tick(.82f);Exited("本体附近敌群");
        Reset();Enemy(1,4);Enemy(1,2);Enemy(1,3);Enemy(30);var dead=Enemy();dead.Alive=false;var farDimension=Enemy();farDimension.DimensionIndex=1;
        var farLayer=Enemy();farLayer.CourseNode=new();Enemy().m_isBeingDestroyed=true;Tick(.1f);Tick(.5f);Check(runtime.BlocksBody,"沉睡巡逻侦察死亡远处异维度异层和销毁中敌人不计入");
        Reset();var duplicate=Enemy();body.Dimension.AgentsInDimension.Add(duplicate);body.Dimension.AgentsInDimension.Add(duplicate);Tick(.1f);Tick(.5f);Check(runtime.BlocksBody,"登记表重复项不会虚增敌群数量");
        Reset();Enemy();Enemy();var third=Enemy();Tick(.1f);third.Alive=false;Tick(.21f);third.Alive=true;Tick(.32f);Tick(.51f);Check(runtime.BlocksBody,"数量降回阈值以下重置连续窗口");Tick(.65f);Exited("重新持续达到敌群阈值");
        Reset();Enemy();Enemy();Enemy();Plugin.ThreatExit.Value=false;Tick(.1f);Tick(.5f);Check(runtime.BlocksBody,"敌群保护可配置关闭");body.Damage.Health=98;Tick(.6f);Exited("关闭敌群检测仍保留受伤保护");
        Reset(false);Enemy();Enemy();Enemy();Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&!runtime.Busy&&service.Releases==0,"申请时已有敌群直接拒绝，不暂停任务或锁输入");
        Reset();Enemy();Enemy();Enemy();Tick(.1f);Tick(1);Check(runtime.BlocksBody,"长采样空档不能误充确认时间");Tick(1.11f);Tick(1.31f);Exited("重新采样确认");
        Reset();body.Dimension.AgentsInDimension=null;Tick(.1f);Exited("原生敌群登记表不可用");
        Reset();Application.isFocused=false;Check(!runtime.BlocksBody,"失焦回调前也不能锁本体");Tick(.1f);Exited("轮询失焦");
        Reset();runtime.Shutdown();Check(!runtime.Busy&&!runtime.BlocksBody&&HeadCameraApi.Request==null,"卸载注销入口并归还输入");
        Hooks();
        BotControlApi.Unregister(service);
    }
    static int Main(){try{Cases();Console.WriteLine("PASS: "+checks+" 生产ControlRuntime／原生敌群适配／生命周期回调与异常清理检查（Unity场景与服务替身）");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static object Hook(string name,params object[] args)=>typeof(ControlHooks).GetMethod(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,args);
    static bool Gate(string name,PlayerAIBot b,PlayerBotActionBase.Descriptor d)=>name=="Request"?(bool)Hook(name,b,d,true):(bool)Hook(name,b,d);
    static void UpdateActionFollowPlayer(){}
    static void UpdateActionUseBioscan(){}
    static void UpdateActionRevive(){}
    sealed class AiWork { public PlayerAgent Agent; }
    static void Hooks(){
        Reset();var other=Agent(2,true);var travel=new PlayerBotActionTravel.Descriptor();
        Check(!Gate("Request",bot.Bot,travel)&&!Gate("Start",bot.Bot,travel),"生产动作补丁阻止被接管机器人的自主位移");
        Check(Gate("Request",other.Bot,travel)&&Gate("Start",other.Bot,travel),"生产动作补丁不拦截其他机器人");
        Check(!(bool)Hook("Button",InputAction.Weapon,true)&&!(bool)Hook("Axis",InputAction.LookHorizontal,1f)&&!(bool)Hook("Inventory"),"本体武器／视角／背包输入实际补丁隔离");
        Check((bool)Hook("Button",InputAction.Communication,true),"普通通讯输入保留给Q菜单");
        Check(!(bool)Hook("Look",body.FPSCamera)&&(bool)Hook("Look",other.FPSCamera),"只拦本体摄像头转向");
        Check(!(bool)Hook("Teleport",bot)&&!(bool)Hook("Warp",bot,true)&&(bool)Hook("Teleport",other),"接管期自动传送拦截仅作用于当前机器人");
        typeof(ControlHooks).GetField("workAgent",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null,typeof(AiWork).GetField("Agent"));
        Check(!(bool)Hook("AiWarp",new AiWork{Agent=bot},true)&&(bool)Hook("AiWarp",new AiWork{Agent=other},true),"更好AI的自动Warp同样仅屏蔽目标机器人");
        var scan=typeof(RuntimeSafetyTests).GetMethod("UpdateActionUseBioscan",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var revive=typeof(RuntimeSafetyTests).GetMethod("UpdateActionRevive",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var follow=typeof(RuntimeSafetyTests).GetMethod("UpdateActionFollowPlayer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var root=new RootPlayerBotAction{m_bot=bot.Bot};var otherRoot=new RootPlayerBotAction{m_bot=other.Bot};
        Check(!(bool)Hook("Autonomous",root,scan)&&(bool)Hook("Autonomous",otherRoot,scan),"接管期扫描派发不抢占，其他机器人扫描正常");
        service.AllowAction=true;Check(Gate("Request",bot.Bot,travel)&&Gate("Start",bot.Bot,travel),"只放行指令系统确认的原生动作");service.AllowAction=false;
        service.AllowTeleport=true;Check((bool)Hook("Teleport",bot)&&(bool)Hook("AiWarp",new AiWork{Agent=bot},true),"已有指令传送作用域可通过生产补丁");service.AllowTeleport=false;
        runtime.Exit("驻守测试",false);service.Guard=true;
        Check(!Gate("Request",bot.Bot,travel)&&!Gate("Start",bot.Bot,new PlayerBotActionFollow.Descriptor()),"退出驻守交接空档阻止原生跟随旅行");
        Check(!(bool)Hook("Teleport",bot)&&!(bool)Hook("AiWarp",new AiWork{Agent=bot},true),"驻守尚未取得权限前不能自动传回本体");
        Check((bool)Hook("Autonomous",root,scan)&&(bool)Hook("Autonomous",root,revive)&&!(bool)Hook("Autonomous",root,follow),"交接保护允许原有扫描／救援派发，仅拦自主跟随");
        Check((bool)Hook("Inventory")&&(bool)Hook("Look",body.FPSCamera)&&(bool)Hook("Button",InputAction.Weapon,true),"驻守保护不会延续本体输入锁");
        service.Guard=false;Check((bool)Hook("Teleport",bot)&&(bool)Hook("AiWarp",new AiWork{Agent=bot},true)&&Gate("Request",bot.Bot,travel)&&(bool)Hook("Autonomous",root,follow),"驻守保护结束后自动传送及普通跟随补丁恢复原行为");
        service.Detached=true;Check(!Gate("Request",bot.Bot,travel)&&!(bool)Hook("Teleport",bot)&&(bool)Hook("Inventory"),"原子收尾仍管理机器人但本体操作自由");
        service.Detached=false;runtime.Shutdown();
    }
}
