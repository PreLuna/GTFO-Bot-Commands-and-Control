using System;
using BepInEx.Configuration;
using LevelGeneration;
using Player;
using UnityEngine;
using PreLuna.Captain.Tasks;
static class AutoPickupTests
{
    static int checks;
    static ConfigFile config;
    static PlayerAgent human;
    static PlayerAIBot bot;
    static ItemInLevel first, second;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    static bool Allowed(PlayerAIBot owner,PlayerBotActionBase.Descriptor desc)
        => (bool)typeof(ItemActionAuthorization).GetMethod("Allowed",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{owner,desc});
    static void Step(float seconds=.1f, KeyCode? key=null)
    {
        Time.time+=seconds;Time.unscaledTime+=seconds;Time.frameCount++;
        Input.Down.Clear();if(key.HasValue)Input.Down.Add(key.Value);
        AutoPickupAdapter.InputTick();AutoPickupAdapter.Tick();Input.Down.Clear();
    }
    static void RealTimeOnly(float seconds)
    {
        Time.unscaledTime+=seconds;Time.frameCount++;Input.Down.Clear();
        AutoPickupAdapter.InputTick();AutoPickupAdapter.Tick();
    }
    static void Reset()
    {
        AutoPickupAdapter.Reset();ItemWorldIndex.Items.Clear();ItemTaskAdapter.Requests.Clear();ItemTaskAdapter.BusyBots.Clear();ItemTaskAdapter.Accept=true;ItemTaskAdapter.CommunicationOpen=false;
        ItemActionAuthorization.Reset();
        OpenTaskAdapter.Owner=null;OpenTaskAdapter.Action=null;
        Time.time=Time.unscaledTime=0;Time.frameCount=0;Physics.Blocked=false;Application.isFocused=true;InputMapper.PlayerHasFocus=true;PlayerChatManager.InChatMode=false;
        CaptainMenuInput.OwnsInput=false;FocusStateManager.CurrentState=eFocusState.FPS;SNetwork.SNet.IsMaster=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;
        PickupPromptHud.Ready=true;PlayerManager.Current=new();AIGraph.Zone.Shared=new();
        ItemTaskSession.InvalidItems.Clear();PreLuna.Captain.Plugin.Logger.Messages.Clear();
        human=new(){Position=new Vector3(100),PlayerName="房主"};PlayerManager.Current.Slots[0]=human;
        var agent=new PlayerAgent{Position=new Vector3(10),PlayerName="Bishop"};agent.Owner.IsBot=true;bot=new(){Agent=agent};agent.Bot=bot;PlayerManager.Current.Slots[1]=agent;
        first=new();first.transform.position=new Vector3(10);second=new(){PublicName="Medipack"};second.transform.position=new Vector3(11);
        ItemWorldIndex.Items.Add(first);ItemWorldIndex.Items.Add(second);config=new();AutoPickupAdapter.Configure(config);
    }
    static void Leave(){human.Position=new Vector3(10);Step();human.Position=new Vector3(100);Step(1.6f);}
    static void Main()
    {
        var fifo=new PickupPromptQueue<object>();var a=new object();var b=new object();Check(fifo.Enqueue(a)&&fifo.Enqueue(b)&&!fifo.Enqueue(a),"排队去重");
        Check(ReferenceEquals(fifo.Dequeue(),a)&&ReferenceEquals(fifo.Dequeue(),b)&&fifo.Dequeue()==null,"FIFO 一项一项弹出");
        for(int i=0;i<32;i++)Check(fifo.Enqueue(new object()),"队列容量内入队");Check(!fifo.Enqueue(new object()),"队列有界，余项稍后重试");fifo.Clear();Check(fifo.Count==0,"清理队列");
        var timer=new PickupPromptClock();timer.Begin(8);timer.Advance(3);Check(timer.Remaining==5,"当前问题持续倒计时，不依赖输入或机器人");
        timer.Advance(float.NaN);timer.Advance(float.PositiveInfinity);timer.Advance(-1);Check(timer.Remaining==5,"非法时间不污染");timer.Advance(10);Check(timer.Expired&&timer.Remaining==0,"超时不为负");timer.Begin(8);Check(!timer.Expired&&timer.Remaining==8,"下一项独立8秒");
        Reset();bot.Unsafe=true;Step(10);Check(!PickupPromptHud.Visible&&ItemTaskAdapter.Requests.Count==0,"危险也不能拾取未访问物资");
        Reset();human.Position=new Vector3(10);Step(10);Check(!PickupPromptHud.Visible&&ItemTaskAdapter.Requests.Count==0,"玩家在旁边不询问或争抢");
        Reset();Leave();Check(PickupPromptHud.Visible&&PickupPromptHud.Item=="荧光棒"&&PickupPromptHud.Waiting==1,"只显示首项及队列数量、中文名");
        Step(2);Check(PickupPromptHud.Remaining==6,"HUD 显示倒计时");Step(.1f,KeyCode.N);Check(ItemTaskAdapter.Requests.Count==0&&PickupPromptHud.Item=="Medipack"&&PickupPromptHud.Remaining==8,"N 拒绝后下一项重新计时");
        Step(.1f,KeyCode.Y);Check(ItemTaskAdapter.Requests.Count==1&&ReferenceEquals(ItemTaskAdapter.Requests[0].Item,second),"Y 仅同意当前项");
        ItemTaskAdapter.Complete(0,true);Step(10);Check(ItemTaskAdapter.Requests.Count==1&&!PickupPromptHud.Visible,"完成／拒绝本次不重复询问");
        Reset();Leave();float scaled=Time.time;RealTimeOnly(3);
        Check(Time.time==scaled&&PickupPromptHud.Remaining==5,"游戏时间不走时真实时间仍扣倒计时");
        RealTimeOnly(5.1f);Check(ItemTaskAdapter.Requests.Count==1,"游戏时间停住也正常到期处理");
        Check(AutoPickupPolicy.Decide(true,false,false,false,true,true,true,-1,true)==PickupDecision.Decline,"截止时N优先于默认同意，且不要求执行者");
        foreach(var focus in new (string Name,Action Apply)[]{
            ("聊天",()=>PlayerChatManager.InChatMode=true),("终端",()=>FocusStateManager.CurrentState=eFocusState.ComputerTerminal),
            ("地图",()=>FocusStateManager.CurrentState=eFocusState.Map),("队长菜单",()=>CaptainMenuInput.OwnsInput=true),
            ("切出游戏",()=>Application.isFocused=false),("失去玩家焦点",()=>InputMapper.PlayerHasFocus=false),
            ("原版通讯菜单",()=>ItemTaskAdapter.CommunicationOpen=true)})
        {
            Reset();Leave();focus.Apply();Step(2,KeyCode.N);
            Check(ItemTaskAdapter.Requests.Count==0&&PickupPromptHud.Remaining==6&&!PickupPromptHud.InputAllowed,focus.Name+"不接收N但持续计时");
            Step(6.1f,KeyCode.Y);Check(ItemTaskAdapter.Requests.Count==1&&ReferenceEquals(ItemTaskAdapter.Requests[0].Item,first),focus.Name+"期间超时照常默认同意");
        }
        Reset();Leave();first.Sync.Status=ePickupItemStatus.PickedUp;Step();Check(PickupPromptHud.Item=="Medipack"&&PickupPromptHud.Remaining==8,"真人拿走当前物品立即撤销并显示下一项");
        Reset();Leave();AutoPickupAdapter.CancelPrompt(bot);bot.AutoAvailable=false;Step(1);Check(!PickupPromptHud.Visible&&ItemTaskAdapter.Requests.Count==0,"明确命令让出机器人，没有执行者的下一项直接延后");
        Reset();Leave();bot.Unsafe=true;Step();Check(ItemTaskAdapter.Requests.Count==1,"安全转危险不再等待询问");
        Reset();((ConfigEntry<bool>)config.Entries["自动拾取/安全时询问"]).Value=false;Leave();Check(ItemTaskAdapter.Requests.Count==1&&!PickupPromptHud.Visible,"关闭询问直接任务，无HUD");
        Reset();first.container=new();first.container.m_core.Status=eResourceContainerStatus.Closed;second.container=new();second.container.m_core.Status=eResourceContainerStatus.Closed;Leave();Check(ItemTaskAdapter.Requests.Count==0&&!PickupPromptHud.Visible,"封闭箱不登记访问、不拾取");
        Reset();Leave();Step(.1f,KeyCode.Y);Check(ItemTaskAdapter.Requests.Count==1,"开始任务");ItemTaskAdapter.Complete(0,false);Step(1);Check(ItemTaskAdapter.Requests.Count==1,"失败冷却，不误标已完成或连续重试");
        second.Sync.Status=ePickupItemStatus.PickedUp;Step(16);Check(PickupPromptHud.Visible&&PickupPromptHud.Item=="荧光棒","失败后释放预约，再次有机会询问");
        Reset();PickupPromptHud.Ready=false;Leave();Step(30);Check(ItemTaskAdapter.Requests.Count==0,"HUD从未建立的项到期延后，不偷偷同意");PickupPromptHud.Ready=true;Step(16);Step(.1f,KeyCode.Y);Check(ItemTaskAdapter.Requests.Count==1,"HUD恢复并过重试间隔后可正常答复");
        Reset();var other=new PlayerAgent{Position=new Vector3(11),PlayerName="Dauda"};other.Owner.IsBot=true;other.Bot=new(){Agent=other};PlayerManager.Current.Slots[2]=other;Leave();
        Step(.1f,KeyCode.Y);Check(ItemTaskAdapter.Requests.Count==1&&PickupPromptHud.Visible,"多机器人也一次只答复当前项");
        Input.Down.Add(KeyCode.Y);AutoPickupAdapter.InputTick();AutoPickupAdapter.Tick();Input.Down.Clear();
        Check(ItemTaskAdapter.Requests.Count==1,"同一帧的Y不会连带同意新显示的下一项");
        Reset();Leave();AutoPickupAdapter.Reset();Check(!PickupPromptHud.Visible,"关卡清理撤销HUD和队列");
        Reset();var collection=new PlayerBotActionCollectItem.Descriptor{TargetItem=first};
        Check(!Allowed(bot,collection),"无命令普通收集请求拒绝");ItemActionAuthorization.Command(bot,collection);Check(Allowed(bot,collection),"明确原版收集命令绑定目标授权");
        collection.TargetItem=second;Check(!Allowed(bot,collection),"授权不能转移到另一物品");collection.TargetItem=first;
        var oldOwner=bot.Agent.Owner;bot.Agent.Owner=new(){IsBot=true};Check(!Allowed(bot,collection),"机器人替换后旧授权无效");bot.Agent.Owner=oldOwner;
        first.container=new();first.container.m_core.Status=eResourceContainerStatus.Closed;Check(!Allowed(bot,collection),"明确拾取也不授权打开封闭容器");first.container.m_core.Status=eResourceContainerStatus.Open;
        var unlock=new PlayerBotActionUnlock.Descriptor{TargetType=PlayerBotActionUnlock.Descriptor.TargetTypeEnum.Container};
        var highlight=new PlayerBotActionHighlight.Descriptor{TargetType=PlayerBotActionHighlight.Descriptor.TargetTypeEnum.Container};
        Check(!Allowed(bot,unlock)&&!Allowed(bot,highlight),"容器开箱与高亮入口均限制");
        OpenTaskAdapter.Owner=bot;OpenTaskAdapter.Action=unlock;
        Check(Allowed(bot,unlock)&&!Allowed(bot,highlight),"明确开启只放行对应任务动作，不解禁自主开箱");
        Check(!Allowed(new PlayerAIBot{Agent=new PlayerAgent()},unlock),"开启任务授权不会转给其他机器人");
        bot.Actions.Add(new(){DescBase=unlock});ItemActionAuthorization.Quiesce(bot);
        Check(!unlock.IsTerminated(),"物资限制不会停止明确授权的开锁动作");bot.Actions.Clear();
        OpenTaskAdapter.Owner=null;OpenTaskAdapter.Action=null;
        Check(!Allowed(bot,unlock),"任务释放后恢复容器自主动作限制");
        unlock.TargetType=PlayerBotActionUnlock.Descriptor.TargetTypeEnum.Door;highlight.TargetType=PlayerBotActionHighlight.Descriptor.TargetTypeEnum.Door;
        Check(Allowed(bot,unlock)&&Allowed(bot,highlight)&&Allowed(bot,new PlayerBotActionBase.Descriptor()),"门与其他动作不受物资限制");
        var rogue=new PlayerBotActionCollectItem.Descriptor{TargetItem=second};bot.Actions.Add(new(){DescBase=rogue});bot.Actions.Add(new(){DescBase=unlock});
        ItemActionAuthorization.Tick();Check(rogue.IsTerminated()&&bot.Stops==1&&!unlock.IsTerminated(),"仅停止已经执行的未授权物资动作");
        Time.time=181;Check(!Allowed(bot,collection),"授权过期失效");
        ((ConfigEntry<bool>)config.Entries["自动拾取/仅收集玩家已访问物资"]).Value=false;Check(Allowed(bot,rogue),"关闭限制恢复原行为");
        Reset();SNetwork.SNet.IsMaster=false;Check(Allowed(bot,new PlayerBotActionCollectItem.Descriptor{TargetItem=first}),"客户端不修改原版动作授权");
        Reset();first.CourseNode=null;first.Sync.placement.node.Node=human.CourseNode;second.Sync.Status=ePickupItemStatus.PickedUp;
        bot.AutoAvailable=false;Leave();Check(!PickupPromptHud.Visible,"无执行者不创建虚假的Y/N询问");
        Step(30);Check(ItemTaskAdapter.Requests.Count==0&&!PickupPromptHud.Visible,"无人可执行时延后而非占住询问窗");
        bot.AutoAvailable=true;Step(16);Step(.1f,KeyCode.Y);Check(ItemTaskAdapter.Requests.Count==1,"执行者恢复后重新核验并正常询问");
        Reset();Leave();bot.AutoAvailable=false;Step(2);Check(PickupPromptHud.Remaining==6,"询问中执行者忙碌也不暂停");
        Step(.1f,KeyCode.N);Check(!PickupPromptHud.Visible&&ItemTaskAdapter.Requests.Count==0,"失去执行者时N仍可拒绝并让出队列");
        bot.AutoAvailable=true;Step(16);Check(PickupPromptHud.Item=="Medipack","拒绝项不重现，后续项恢复询问");
        Reset();Leave();bot.AutoAvailable=false;Step(8.1f);Check(!PickupPromptHud.Visible&&ItemTaskAdapter.Requests.Count==0,"倒计时到期无执行者，授权延后且关闭当前窗口");
        bot.AutoAvailable=true;Step(16);Check(ItemTaskAdapter.Requests.Count==1&&ReferenceEquals(ItemTaskAdapter.Requests[0].Item,first),"超时同意保留，空闲后执行但不重新阻塞询问");
        Reset();Leave();bot.AutoAvailable=false;Step(.1f,KeyCode.Y);Check(!PickupPromptHud.Visible&&ItemTaskAdapter.Requests.Count==0,"Y同意但无执行者时直接延后");
        bot.AutoAvailable=true;Step(16);Check(ItemTaskAdapter.Requests.Count==1,"Y授权保留直到机器人可用");
        Reset();ItemTaskSession.InvalidItems[first.Pointer]="同类物品已达到当前实际堆叠容量";Leave();
        Check(PickupPromptHud.Visible&&PickupPromptHud.Item=="Medipack","首项背包检查失败不妨碍下一项询问");
        Check(PreLuna.Captain.Plugin.Logger.Messages.Exists(x=>x.Contains("拾取询问延后")&&x.Contains("堆叠容量")),"延后日志记录候选容量拒绝原因");
        Reset();Leave();bot.AutoAvailable=false;Step();AutoPickupAdapter.CancelPrompt(null,first);Check(!PickupPromptHud.Visible,"明确指定同一物品可清除没有执行者的旧询问");
        Reset();first.CourseNode=null;first.container=new();first.container.m_core.SpawnNode=human.CourseNode;second.Sync.Status=ePickupItemStatus.PickedUp;
        Leave();Check(PickupPromptHud.Visible,"容器节点补充支持玩家访问与询问");
        Console.WriteLine("PASS: "+checks+" 项生产自动拾取／队列／计时策略模拟断言；Unity HUD 与原生同步仍需游戏测试。");
    }
}
