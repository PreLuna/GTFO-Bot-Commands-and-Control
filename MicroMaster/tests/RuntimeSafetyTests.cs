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
        public bool Valid{get;set;}=true;public bool Paused=>service.Ready;
        public bool Acquire()=>true;public bool Renew()=>service.Lease;
        public void Quiesce(){}public void ReleaseLease(){}public void RestoreBeforeActivation(){}public void KeepPaused(){}public void Recall(){}
    }
    sealed class Service:IBotControlService,IBotControlCommandService,IBotControlStationService {
        internal ControlTransaction Transaction;
        internal Session Session;
        internal bool Lease=true,Ready=true,ThrowRelease,ReleasedWithBodyFree,LastRecall;
        internal int Releases;
        internal BotControlTarget Target;
        internal bool Guard,Detached,AllowAction,AllowTeleport,BusyCommand;
        bool Same(long a,long o)=>a==Target.Agent&&o==Target.Owner;
        public bool Begin(BotControlTarget target,string source,out long ticket,out string reason){Target=target;Session=new();Transaction=new(Session,Time.unscaledTime);ticket=1;reason=null;return true;}
        public BotControlState Poll(long ticket,out string reason){Transaction.Tick(Time.unscaledTime);reason=Transaction.State==BotControlState.Waiting?"等待补给应用／核验收尾":Transaction.Reason;return Transaction.State;}
        public bool Renew(long ticket)=>Transaction.Renew(Time.unscaledTime);
        public void Release(long ticket,bool recall){Releases++;ReleasedWithBodyFree=!runtime.BlocksBody;LastRecall=recall;Transaction.Exit(recall);if(ThrowRelease)throw new Exception("service release");}
        public bool Controls(long a,long o)=>Same(a,o)&&Lease&&Session?.Valid==true&&Transaction?.ActiveAt(Time.unscaledTime)==true;
        public bool Mark(long t,BotControlRay r,out string reason){reason=null;return true;}
        public bool CommandBusy(long t,out string label){label=null;return BusyCommand;}
        public bool CommandOwns(long a,long o)=>Detached&&Same(a,o);
        public bool AllowsAction(long a,long o,long d)=>AllowAction&&Same(a,o);
        public bool AllowsTeleport(long a,long o)=>AllowTeleport&&Same(a,o);
        public bool RecallProtected(long a,long o)=>Guard&&Same(a,o);
    }
    static PlayerAgent Agent(int slot,bool robot){var a=new PlayerAgent{PlayerSlotIndex=slot,Owner=new(){IsBot=robot}};a.Damage=new(){Owner=a};a.Sync.Agent=a;if(robot){a.Bot=new(){Agent=a};a.Bot.Inventory.Owner=a;}return a;}
    static void Reset(bool begin=true,bool snapshot=true){
        HeadCameraApi.ThrowClear=false;NativeManualWalk.ThrowStop=false;service.ThrowRelease=false;runtime?.Shutdown();
        CullingSystem.C_CullingManager.VisibleSpotLights.Clear();Time.unscaledTime=0;Time.frameCount=0;InputMapper.FlashlightDown=false;NativeManualWalk.Blocked=false;NativeManualWalk.Stops=NativeManualWalk.Clears=0;NativeManualWalk.Own=null;Input.Held.Clear();Input.Down.Clear();Application.isFocused=true;FocusStateManager.CurrentState=eFocusState.FPS;
        Plugin.NativeLight.Value=true;Plugin.LightMode.Value=BotFlashlightMode.ControlledOnly;
        NativeLightSettings.Clear();
        var blocks=GameData.GameDataBlockBase<GameData.FlashlightSettingsDataBlock>.Wrapper.Blocks;blocks.Clear();
        blocks.Add(new(){persistentID=1,range=11,angle=40,intensity=.35f});blocks.Add(new(){persistentID=2,range=20,angle=50,intensity=.55f});
        if(snapshot)NativeLightSettings.Capture();
        // Reproduce RF data mutation, rather than assuming native setup returns positive values.
        foreach(var block in blocks){block.range=block.angle=block.intensity=0;}

        Plugin.Enabled.Value=Plugin.ThreatExit.Value=true;Plugin.ThreatCount.Value=3;Plugin.ThreatRadius.Value=15;Plugin.ThreatConfirmation.Value=.25f;
        PlayerChatManager.InChatMode=false;HeadCameraApi.IsVisible=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;
        SNetwork.SNet.IsMaster=true;SNetwork.SNet.Master=new();LevelGeneration.Builder.CurrentFloor=new();PlayerManager.Current=new();
        body=Agent(0,false);bot=Agent(1,true);PlayerManager.Current.Slots[0]=body;PlayerManager.Current.Slots[1]=bot;
        HeadCameraApi.Selected=new(1,bot.Pointer.ToInt64(),bot.Owner.Pointer.ToInt64(),"bot",true);
        service.Lease=service.Ready=true;service.Releases=0;service.Transaction=null;service.Session=null;service.Guard=service.Detached=service.AllowAction=service.AllowTeleport=service.BusyCommand=false;
        runtime=new((IntPtr)10000);runtime.Awake();runtime.OnEnable();runtime.Update();
        if(begin){Check(HeadCameraApi.Request(HeadCameraApi.Selected),"生产接管入口接受安全场景");runtime.Update();Check(runtime.BlocksBody,"生产运行器在激活后隔离本体");}
    }
    static void Tick(float now){Time.unscaledTime=now;Time.frameCount++;foreach(var agent in PlayerManager.Current.Slots)agent?.Bot?.Inventory.Render();runtime.Update();runtime.LateUpdate();}
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
        NativeLights();
        GlobalLights();
        NativeSwitchWrites();
        RenderReadiness();
        Reset();runtime.CommandFeedback("已标记位置");Check(HeadCameraApi.LastNotice=="已标记位置","指令成功反馈进入接管画面");
        service.Transaction.Exit(false);Tick(.1f);Check(!runtime.Busy&&!HeadCameraApi.IsVisible&&!service.LastRecall,"驻守释放后关闭观战窗并返回本体，无错误召回");
        Reset();service.Transaction.Exit(false);runtime.LateUpdate();Check(!runtime.Busy&&!HeadCameraApi.IsVisible&&!service.LastRecall,"Q在Update后完成驻守交接也关闭窗口且不召回");
        Reset();Input.Held.Add(KeyCode.W);Tick(.1f);NativeManualWalk.Own=new();NativeManualWalk.Blocked=true;
        runtime.PrepareNativeWalk(new PlayerBotActionWalk { DescBase=NativeManualWalk.Own });
        Check(NativeManualWalk.Stops==0&&NativeManualWalk.Clears==1,"原生步行Update内只清路径，不移除正在更新的动作");
        runtime.LateUpdate();Check(NativeManualWalk.Stops==1&&runtime.BlocksBody,"LateUpdate安全停止，碰撞不会强制结束接管");
        Reset();body.Damage.Health=99;Tick(.1f);Exited("本体受伤");
        Reset();body.Alive=false;Tick(.1f);Exited("本体倒地");
        Reset(false);Enemy();Enemy();Enemy();Check(HeadCameraApi.Request(HeadCameraApi.Selected),"本体附近已激怒敌群不再拒绝申请");
        runtime.Update();Tick(.1f);Tick(.5f);Check(runtime.BlocksBody,"敌群持续存在不会自动退出");
        body.Damage.Health=98;Tick(.6f);Exited("取消敌群限制仍保留受伤保护");
        Reset(false);body.Dimension.AgentsInDimension=null;Check(HeadCameraApi.Request(HeadCameraApi.Selected),"复现游戏启动时敌群登记表不可核验，仍接受接管");
        runtime.Update();Tick(.1f);Check(runtime.BlocksBody,"敌群登记表缺失不会退出");
        Reset();body.Locomotion.m_currentStateEnum=17;Tick(.1f);Check(runtime.BlocksBody,"StandStill本体状态不误触发退出");
        Reset();body.Locomotion.m_currentStateEnum=8;Tick(.1f);Exited("爬梯仍归还本体");
        Reset();body.Damage.GrabbedAtAll=true;Tick(.1f);Exited("被抓仍归还本体");
        Reset(false);service.Ready=false;Check(HeadCameraApi.Request(HeadCameraApi.Selected),"受保护交互先接受为等待申请");
        Tick(.1f);Check(runtime.Busy&&!runtime.BlocksBody&&HeadCameraApi.LastNotice.Contains("等待补给"),"等待具体原因进入摄像头提示，期间本体可操作");
        service.Ready=true;Tick(.2f);Check(runtime.BlocksBody,"交互完成后同一运行器自动激活接管");
        Reset(false);SNetwork.SNet.IsMaster=false;Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("房主"),"非房主请求明确显示原因");
        Reset(false);Plugin.Enabled.Value=false;Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("关闭"),"关闭插件有拒绝提示");
        Reset(false);body.Damage=null;Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("伤害状态"),"缺少本体伤害状态有拒绝提示");
        Reset(false);Application.isFocused=false;Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("焦点"),"失焦请求有拒绝提示");
        Reset(false);PlayerChatManager.InChatMode=true;Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("聊天"),"聊天中拒绝有原因");
        Reset(false);FocusStateManager.CurrentState=eFocusState.Menu;Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("菜单"),"菜单请求有拒绝提示");
        Reset(false);GameStateManager.CurrentStateName=eGameStateName.Lobby;Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("关卡"),"非关卡请求有拒绝提示");
        Reset(false);LevelGeneration.Builder.CurrentFloor=null;Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("尚未就绪"),"世界未准备好有拒绝提示");
        Reset(false);PlayerManager.Current.Slots[1]=Agent(1,true);Check(!HeadCameraApi.Request(HeadCameraApi.Selected)&&HeadCameraApi.LastNotice.Contains("身份已改变"),"过期观看对象有拒绝提示");
        Reset();Application.isFocused=false;Check(!runtime.BlocksBody,"失焦回调前也不能锁本体");Tick(.1f);Exited("轮询失焦");
        Reset();runtime.Shutdown();Check(!runtime.Busy&&!runtime.BlocksBody&&HeadCameraApi.Request==null,"卸载注销入口并归还输入");
        Hooks();
        BotControlApi.Unregister(service);
    }
    static void NativeLights(){
        Reset();var inv=bot.Bot.Inventory;
        Check(inv.m_flashlight.range==11&&inv.m_flashlight.intensity==.35f&&float.IsFinite(inv.m_flashlightCLight.m_clusterLight.InvRangeSqr),"原生初始化读到零值时，从清零前快照恢复并只提交有限数据");
        Check(inv.m_flashlightCLight.m_clusterLight.NonFiniteWrites==0,"原生初始化内部的数据更新同样被保护，不产生Infinity");

        Check(inv.FlashlightEnabled&&inv.WantsFlashlightEnabled&&inv.m_flashlightCLight.m_clusterLight.m_isOn,"接管通过原生同步开启机器人实际cluster");
        Check(bot.Sync.Requests==1&&!bot.Sync.BroadcastOnly&&body.Sync.Requests==0,"原生同步使用broadcastOnly=false，仅作用于目标");
        Check(!inv.m_flashlight.enabled&&!HeadCameraApi.View(HeadCameraApi.Selected).Value.Illuminate,"不启用Unity灯或请求独立观测灯");
        InputMapper.FlashlightDown=true;Tick(.1f);Check(HeadCameraApi.LastNotice.Contains("原生手电")&&HeadCameraApi.LastNotice.Contains("保持开启"),"F明确提示原生常亮，未改成关灯");
        Tick(.6f);Tick(1.2f);Check(bot.Sync.Requests==1,"稳定常亮不会逐帧或每次轮询重复发网络开灯");
        inv.m_flashlightCLight.m_clusterLight.m_isOn=false;int requests=bot.Sync.Requests;Tick(1.8f);
        Check(inv.m_flashlightCLight.m_clusterLight.m_isOn&&bot.Sync.Requests==requests,"状态已开但cluster关闭时本地原生重应用，不刷网络");
        FocusStateManager.CurrentState=eFocusState.Menu;inv.FlashlightEnabled=inv.WantsFlashlightEnabled=false;Tick(2.4f);
        Check(inv.FlashlightEnabled&&runtime.BlocksBody,"Q菜单占用输入期间仍维持原生灯，不关闭接管");FocusStateManager.CurrentState=eFocusState.FPS;
        inv.WieldedItem=new(){Owner=bot};Tick(3f);Check(inv.WeaponSetups==1&&inv.FlashlightEnabled,"切枪重新按原生装备初始化");
        inv.WieldedItem=new(){Owner=bot,HasFlashlight=false};Tick(3.6f);Check(inv.HelmetSetups>=2&&inv.FlashlightEnabled,"近战或工具沿用原生头盔灯");
        var other=Agent(2,true);var root=new RootPlayerBotAction{m_bot=bot.Bot};var otherRoot=new RootPlayerBotAction{m_bot=other.Bot};
        Check(!(bool)Hook("FlashlightState",root)&&(bool)Hook("FlashlightState",otherRoot),"只代管目标原生开关决策，不让AI关灯，其他机器人保持规则");
        runtime.Exit("手电交还AI",true);Check(bot.Sync.Requests==3&&!bot.Sync.LastValue,"退出通过原生同步归还先前开关，避免远端保留强制常亮");Check((bool)Hook("FlashlightState",root),"退出即恢复AI原生决策");
        Reset(false);service.Ready=false;HeadCameraApi.Request(HeadCameraApi.Selected);Tick(.1f);
        Check(bot.Sync.Requests==0&&(bool)Hook("FlashlightState",new RootPlayerBotAction{m_bot=bot.Bot}),"等待交权时不抢原生手电");
        Reset();Plugin.NativeLight.Value=false;Tick(.6f);
        Check((bool)Hook("FlashlightState",new RootPlayerBotAction{m_bot=bot.Bot}),"配置关闭常亮时立即交还AI");
        Reset(false);Plugin.NativeLight.Value=false;HeadCameraApi.Request(HeadCameraApi.Selected);Tick(.1f);
        Check(bot.Sync.Requests==0,"配置AI自动时不强制开灯");
        Reset();bot.Sync.ThrowRequest=true;inv=bot.Bot.Inventory;inv.WantsFlashlightEnabled=inv.FlashlightEnabled=false;Tick(.6f);
        Check(runtime.BlocksBody&&runtime.Busy,"开灯失败不关闭接管");int failed=bot.Sync.Requests;Tick(.7f);Check(bot.Sync.Requests==failed,"原生报错限频重试");
        bot.Sync.ThrowRequest=false;Tick(1.2f);Check(inv.FlashlightEnabled,"原生接口恢复后能重新开灯");
        Reset(false);var lamp=bot.Bot.Inventory.m_flashlight;lamp.cullingMask=0;lamp.spotAngle=0;lamp.range=lamp.intensity=0;
        var layer=new Transform();lamp.transform.Children["RF_L1"]=layer;HeadCameraApi.Request(HeadCameraApi.Selected);Tick(.1f);
        Check(lamp.cullingMask==0&&lamp.spotAngle>0&&layer.gameObject.activeSelf,"原生参数有效但未经历可见渲染阶段时保留替代层");
        Tick(.2f);Check(!layer.gameObject.activeSelf,"实际模拟可见灯登记及有效数据后才隐藏替代层");
        Check(runtime.OwnsNativeLight(lamp)&&!runtime.OwnsNativeLight(other.Bot.Inventory.m_flashlight),"RF豁免只识别目标原生灯");
        var rfGuard=typeof(NativeFlashlightCompatibility).GetMethod("LightPostfix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Check(!(bool)rfGuard.Invoke(null,new object[]{lamp})&&(bool)rfGuard.Invoke(null,new object[]{other.Bot.Inventory.m_flashlight}),"实际RF直接写入处理器只跳过受控灯，其他机器人保持原逻辑");
        runtime.Exit("灯恢复",true);Check(lamp.cullingMask==0&&lamp.spotAngle>0&&layer.gameObject.activeSelf,"退出只归还RF显隐，不把原生灯重新写成零参数");
        Check(!runtime.OwnsNativeLight(lamp),"退出后灯不再享有豁免");
        ParameterFailures();
        var snapshots=GameData.GameDataBlockBase<GameData.FlashlightSettingsDataBlock>.Wrapper.Blocks;
        snapshots.Clear();snapshots.Add(new(){persistentID=1,range=0,angle=0,intensity=0});NativeLightSettings.Capture();
        Check(!NativeLightSettings.TryGet(1,out _)&&!NativeLightSettings.TryGet(2,out _),"重新加载无效或已移除的数据ID不复用旧快照");
        Reset();int beforeLoss=bot.Sync.Requests;SNetwork.SNet.IsMaster=false;Tick(.6f);
        Check(!runtime.Busy&&bot.Sync.Requests==beforeLoss,"失去房主后只清理本机灯，不向远端发送归还指令");
        Reset();HeadCameraApi.ExitReason("T键退出接管");Check(HeadCameraApi.LastNotice.Contains("T键退出接管"),"退出来源进入微操反馈");
    }
    static void ParameterFailures(){
        Reset(false,false);var inv=bot.Bot.Inventory;var child=new Transform();inv.m_flashlight.transform.Children["RF_L1"]=child;
        HeadCameraApi.Request(HeadCameraApi.Selected);Tick(.1f);
        Check(runtime.BlocksBody&&bot.Sync.Requests==0&&child.gameObject.activeSelf,"无快照不强行开原生灯，不隐藏原RF层，不退出接管");
        InputMapper.FlashlightDown=true;Tick(.2f);Check(HeadCameraApi.LastNotice.Contains("未就绪"),"缺参数提示真实状态，不声称常亮成功");
        inv.m_flashlight.range=0;inv.m_flashlightCLight.m_clusterLight.UpdateData();Check(inv.m_flashlightCLight.m_clusterLight.NonFiniteWrites==0,"缺快照仍拒绝零范围原生数据更新");
        Check(!(bool)Hook("LightOn",inv.m_flashlightCLight,true),"缺参数时原生开灯入口也拒绝启用");
        Reset();inv=bot.Bot.Inventory;inv.m_flashlight.range=0;inv.m_flashlight.intensity=float.NaN;inv.m_flashlightCLight.m_clusterLight.UpdateData();
        Check(inv.m_flashlight.range>0&&float.IsFinite(inv.m_flashlight.intensity)&&inv.m_flashlightCLight.m_clusterLight.NonFiniteWrites==0,"轮询间隙异常值在原生数据入口恢复，未写Infinity");
        Check(!LampParameters.ValidNumbers(0,40,1)&&!LampParameters.ValidNumbers(float.PositiveInfinity,40,1)&&!LampParameters.ValidNumbers(10,180,1)&&!LampParameters.ValidNumbers(10,40,float.NaN),"拒绝零、非有限及非法锥角");
        NativeLightSettings.Capture();Check(NativeLightSettings.TryGet(1,out var original)&&original.Range==11,"同批已被清零的数据再次捕获不覆盖原快照");
        Reset(false);inv=bot.Bot.Inventory;var t=new Transform();var rf=new Light{transform=t,enabled=true,intensity=.7f,range=8,spotAngle=70};t.Light=rf;inv.m_flashlight.transform.Children["RF_L1"]=t;
        HeadCameraApi.Request(HeadCameraApi.Selected);Tick(.1f);int dataBeforeExit=inv.m_flashlightCLight.m_clusterLight.DataUpdates;
        bool guardedAfterInputRelease=false;
        var oldLog=Plugin.Instance.Log.Messages.Count;
        // Observe scope at the actual native status callback during End, after ticket is cleared.
        Player.FlashlightInventory.OnReceive=()=>{if(!runtime.BlocksBody)guardedAfterInputRelease=runtime.OwnsNativeLight(inv.m_flashlight);};
        runtime.Exit("恢复保护核验",true);Player.FlashlightInventory.OnReceive=null;
        Check(guardedAfterInputRelease,"本体输入已归还时，灯光恢复保护仍在");
        Check(rf.enabled&&rf.intensity==.7f&&rf.range==8&&t.gameObject.activeSelf,"退出不改子灯参数和enabled，只恢复显隐");
        Check(inv.m_flashlightCLight.m_clusterLight.DataUpdates==dataBeforeExit,"退出没有主动提交GPU灯数据");
        Check(!runtime.OwnsNativeLight(inv.m_flashlight)&&inv.m_flashlightCLight.m_clusterLight.NonFiniteWrites==0,"恢复结束移除豁免且不提交恢复中的零范围");
    }
    static void RenderReadiness(){
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;
        var inv=bot.Bot.Inventory;var rf=new Transform();inv.m_flashlight.transform.Children["RF_L1"]=rf;runtime.Update();
        Check(inv.FlashlightEnabled&&inv.WantsFlashlightEnabled&&inv.m_flashlightCLight.m_clusterLight.DataUpdates==0,"初始化与接收开灯不伪造立即渲染／数据刷新");
        Check(rf.gameObject.activeSelf&&!NativeBotLamp.Inspect(bot).RenderReady,"仅CPU参数有效时保留RF层");
        Tick(.1f);Check(NativeBotLamp.Inspect(bot).RenderReady&&!rf.gameObject.activeSelf,"可见灯、有效数据和阴影登记就绪后才能停用RF层");
        int setups=inv.HelmetSetups,requests=bot.Sync.Requests;Tick(.7f);Tick(1.3f);
        Check(inv.HelmetSetups==setups&&bot.Sync.Requests==requests,"原生Receive的嵌套头盔初始化不触发持续重复初始化");
        inv.FailShadow=true;Tick(1.4f);
        Check(inv.WantsFlashlightEnabled&&inv.FlashlightEnabled&&!inv.m_flashlightCLight.m_clusterLight.m_isOn,"原生临时禁灯与玩家开灯意图分离");
        Check(rf.gameObject.activeSelf&&!NativeBotLamp.Inspect(bot).RenderReady,"已亮过但渲染失效时下一帧恢复RF层，不保留假就绪");
        Check(runtime.NativeLightControls(bot),"渲染临时禁灯不会交回AI再关闭开灯意图");
        inv.FailShadow=false;Tick(1.9f);Tick(2f);Check(NativeBotLamp.Inspect(bot).RenderReady&&!rf.gameObject.activeSelf,"原生资源恢复后再次进入渲染就绪");
        var cluster=inv.m_flashlightCLight.m_clusterLight;cluster.Data.InvRangeSqr=float.NaN;runtime.Update();
        Check(!NativeBotLamp.Inspect(bot).RenderReady&&rf.gameObject.activeSelf,"无效原生数据撤销渲染就绪并保留替代层");
        inv.Render();var original=cluster.UnityLight;cluster.UnityLight=new Light();runtime.Update();
        Check(NativeBotLamp.Inspect(bot).Reason.Contains("不一致")&&rf.gameObject.activeSelf,"源灯身份不一致会明确诊断且不隐藏RF层");cluster.UnityLight=original;
        int notices=Plugin.Instance.Log.Messages.Count;for(int n=0;n<30;n++)runtime.Update();Check(Plugin.Instance.Log.Messages.Count==notices,"稳定或重复异常渲染状态不逐帧刷日志");
    }
    static void GlobalLights(){
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;
        var second=Agent(2,true);var third=Agent(3,true);PlayerManager.Current.Slots[2]=second;PlayerManager.Current.Slots[3]=third;
        Tick(.1f);
        foreach(var a in new[]{bot,second,third}) {
            Check(a.Bot.Inventory.FlashlightEnabled&&a.Bot.Inventory.WantsFlashlightEnabled&&a.Bot.Inventory.m_flashlightCLight.m_clusterLight.m_isOn,"未打开摄像头也开启所有人机原生灯");
            Check(runtime.NativeLightControls(a)&&!(bool)Hook("FlashlightState",new RootPlayerBotAction{m_bot=a.Bot}),"所有已就绪常亮人机阻止AI关灯");
        }
        Check(!runtime.Busy&&!runtime.NativeLightControls(body)&&body.Sync.Requests==0,"全局灯不取得接管租约、不改本体灯");
        int requests=bot.Sync.Requests,setup=bot.Bot.Inventory.HelmetSetups;Tick(.8f);Tick(1.4f);
        Check(bot.Sync.Requests==requests&&bot.Bot.Inventory.HelmetSetups==setup,"稳定常亮不重复同步／初始化");
        Check(HeadCameraApi.Request(HeadCameraApi.Selected),"全局常亮仍可接管");Tick(1.5f);runtime.Exit("T退出",true);
        Check(bot.Bot.Inventory.FlashlightEnabled&&runtime.NativeLightControls(bot),"T退出继续常亮，不交回AI");
        HeadCameraApi.Request(HeadCameraApi.Selected);Tick(1.6f);service.Transaction.Exit(false);Tick(1.7f);
        Check(!runtime.Busy&&!HeadCameraApi.IsVisible&&bot.Bot.Inventory.FlashlightEnabled&&runtime.NativeLightControls(second),"Q驻守关闭画面但所有灯持续常亮");
        second.Bot.Inventory.FlashlightEnabled=second.Bot.Inventory.WantsFlashlightEnabled=false;Tick(2.3f);
        Check(second.Bot.Inventory.FlashlightEnabled,"外部关灯在有界维护周期重新打开");
        second.Bot.Inventory.WieldedItem=new(){Owner=second};Tick(2.9f);
        Check(second.Bot.Inventory.WeaponSetups==1&&second.Bot.Inventory.m_flashlight.intensity==.55f,"未接管人机换武器使用原生装备初始化和快照");
        second.Alive=false;Tick(3f);Check(runtime.NativeLightControls(second),"倒地但对象尚在时仍保持灯策略，不放宽接管安全条件");
        var replacement=Agent(3,true);replacement.Pointer=third.Pointer;replacement.Owner=third.Owner;PlayerManager.Current.Slots[3]=replacement;Tick(3.1f);
        Check(replacement.Sync.Requests==1&&runtime.NativeLightControls(replacement),"同槽同地址不同实例重新建立灯光身份");
        Plugin.LightMode.Value=BotFlashlightMode.AI;Tick(3.2f);
        Check(!runtime.NativeLightControls(bot)&&!bot.Bot.Inventory.FlashlightEnabled,"配置AI模式归还原开关状态");
        Plugin.LightMode.Value=BotFlashlightMode.AllBots;Tick(3.3f);SNetwork.SNet.IsMaster=false;int before=bot.Sync.Requests;Tick(3.4f);
        Check(bot.Sync.Requests==before&&!runtime.NativeLightControls(bot),"丢失房主权不继续广播开灯／归还请求");
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;Tick(.1f);runtime.StateChanging(eGameStateName.Lobby);
        Check(!runtime.OwnsNativeLight(bot.Bot.Inventory.m_flashlight)&&!bot.Bot.Inventory.FlashlightEnabled,"切关前立即移除全局灯保护并归还开关");
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;Tick(.1f);runtime.LevelCleanup();
        Check(!runtime.OwnsNativeLight(bot.Bot.Inventory.m_flashlight),"关卡清理不依赖接管会话存在");
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;Tick(.1f);Plugin.Enabled.Value=false;Tick(.2f);
        Check(!runtime.NativeLightControls(bot)&&!bot.Bot.Inventory.FlashlightEnabled,"关闭插件配置归还所有人机灯");
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;Tick(.1f);runtime.Shutdown();
        Check(!bot.Bot.Inventory.FlashlightEnabled,"卸载即使没有接管也归还灯");
    }
    static int Main(){try{Cases();Console.WriteLine("PASS: "+checks+" 生产ControlRuntime／放宽接管／生命周期回调与异常清理检查（Unity场景与服务替身）");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static object Hook(string name,params object[] args)=>typeof(ControlHooks).GetMethod(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,args);
    static void NativeSwitchWrites(){
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;Tick(.1f);
        var inv=bot.Bot.Inventory;int requests=bot.Sync.Requests,receives=inv.Receives,calls=inv.Calls;
        float offTime=0;int offAttempts=0,notices=Plugin.Instance.Log.Messages.Count;
        // Reproduce Root.Update's inline decision and its independent cooldown write.
        // Deliberately never call the patched UpdateFlashlightState helper.
        for(int frame=1;frame<=600;frame++){
            float now=.1f+frame/60f;bool allowed=frame>300;bool desired=now-offTime>1.5f&&allowed;
            if(inv.FlashlightEnabled!=desired){bot.Sync.WantsToSetFlashlightEnabled(desired,false);if(!desired){offTime=now;offAttempts++;}}
            Tick(now);
        }
        Check(offAttempts>500&&inv.WantsFlashlightEnabled&&inv.FlashlightEnabled,"真实下游Sync入口挡住内联AI及1.5秒冷却的每帧关灯请求");
        Check(bot.Sync.Requests==requests&&inv.Receives==receives&&inv.Calls==calls,"拒绝false不改发true，十秒AI循环无网络／本地开灯风暴");
        Check(Plugin.Instance.Log.Messages.Count-notices<=10,"关灯诊断按目标限频，持续请求不刷逐帧日志");
        inv.ReceiveSetFlashlightStatus(false,false);inv.SetFlashlightEnabled(false,true,false);bot.Sync.WantsToSetFlashlightEnabled(false,true);
        Check(inv.WantsFlashlightEnabled&&inv.FlashlightEnabled&&bot.Sync.Requests==requests,"旧网络状态、直接本地关灯和仅广播关灯均被拦截");
        inv.UnWieldFlashlight();Check(!inv.FlashlightEnabled&&inv.WantsFlashlightEnabled,"装备解绑仅临时关闭旧灯，保留开灯意图");
        inv.WieldedItem=new(){Owner=bot};inv.OnItemEquippableFlashlightWielded(inv.WieldedItem,inv.WieldedItem.AttachedFlashlight);Tick(10.12f);
        Check(inv.FlashlightEnabled&&inv.WantsFlashlightEnabled,"装备绑定完成下一帧恢复，无嵌套Receive递归");
        inv.ThrowDetach=true;try{inv.UnWieldFlashlight();}catch(Exception){}inv.ThrowDetach=false;Tick(10.14f);
        inv.SetFlashlightEnabled(false,false,false);Check(inv.FlashlightEnabled,"解绑异常finalizer释放许可，后续直接关灯仍被拦截");
        bot.Alive=false;inv.ReceiveSetFlashlightStatus(false,false);Check(inv.FlashlightEnabled,"倒地仍保持灯，AI状态不影响常亮策略");
        HeadCameraApi.Request(HeadCameraApi.Selected);runtime.Update();Check(!runtime.Busy&&!runtime.BlocksBody,"灯常亮没有放宽倒地接管限制，运行器仍取消无效目标");bot.Alive=true;
        inv.m_flashlight.range=0;bot.Sync.WantsToSetFlashlightEnabled(false,false);Check(inv.WantsFlashlightEnabled,"参数／渲染未就绪也不允许清除常亮意图");
        Check((bool)Hook("FlashlightRequest",body.Sync,false)&&(bool)Hook("FlashlightReceive",new PlayerInventoryBase{Owner=body},false),"真人开关和网络状态不受管理");
        runtime.StateChanging(eGameStateName.Lobby);Check(!inv.WantsFlashlightEnabled&&!inv.FlashlightEnabled,"关卡退出显式恢复false不被自身补丁拒绝");
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;Tick(.1f);Plugin.NativeLight.Value=false;
        inv=bot.Bot.Inventory;inv.ReceiveSetFlashlightStatus(false,false);Check(!inv.WantsFlashlightEnabled&&!inv.FlashlightEnabled,"配置关闭立即放行原版关灯，无需等待轮询");
        Reset(false);Plugin.LightMode.Value=BotFlashlightMode.AllBots;Tick(.1f);bot.IsBeingDespawned=true;
        bot.Bot.Inventory.ReceiveSetFlashlightStatus(false,false);Check(!bot.Bot.Inventory.WantsFlashlightEnabled,"销毁／离队对象不继续拦截原生清理");
    }
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
