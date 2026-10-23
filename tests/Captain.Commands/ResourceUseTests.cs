using System;
using System.Reflection;
using Player;
using PreLuna.Captain.Tasks;
static class ResourceUseTests
{
    static int count;
    static void Check(bool value,string reason){count++;if(!value)throw new Exception(reason);}
    public static object Call(string method,params object[] args) => typeof(ResourceUseAdapter).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    static PlayerAIBot Bot(int slot,bool isBot=true){var agent=new PlayerAgent{PlayerSlotIndex=slot,PlayerName="成员"+slot};agent.Owner.IsBot=isBot;var bot=new PlayerAIBot{Agent=agent};agent.Bot=bot;PlayerManager.Current.Agents[slot]=agent;PlayerBackpackManager.All[agent.Owner.Pointer]=bot.Backpack;return bot;}
    static void Reset(){ResourceUseAdapter.Reset();TaskBoard.Journal.Reset();PlayerManager.Current=new();PlayerBackpackManager.All.Clear();UnityEngine.Time.time=100;SNetwork.SNet.IsMaster=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;BotTaskBridge.Available=BotTaskBridge.Execute=BotTaskBridge.AcquireOK=BotTaskBridge.ReportOK=true;BotTaskBridge.Danger=ItemTaskAdapter.IsBusy=false;Gear.ResourcePackFirstPerson.Uses=0;ItemTaskAdapter.Messages.Clear();}
    static PlayerBotActionShareResourcePack.Descriptor Desc(PlayerAIBot bot,PlayerAgent receiver)=>new(bot){Receiver=receiver,Item=bot.Backpack.Held};
    static void Policy(){
        var need=new SupplyNeed(true,.2f,.05f,.05f,.02f,.8f);
        foreach(SupplyKind kind in Enum.GetValues<SupplyKind>())foreach(ResourceUseMode mode in Enum.GetValues<ResourceUseMode>())foreach(bool cmd in new[]{false,true})foreach(bool danger in new[]{false,true}){
            bool expected=kind!=SupplyKind.Unknown&&(cmd||mode!=ResourceUseMode.CommandsOnly);
            Check(ResourceUsePolicy.Allowed(mode,kind,need,cmd,danger,.25f,.08f,.05f,.7f)==expected,"四种模式、资源种类、命令及危险状态矩阵");
        }
        Check(!ResourceUsePolicy.Emergency(SupplyKind.WeaponAmmo,need with{Special=.9f},.25f,.08f,.05f,.7f),"另一把枪还有弹药不紧急");
        Check(!ResourceUsePolicy.Emergency(SupplyKind.WeaponAmmo,need with{Standard=float.NaN},.25f,.08f,.05f,.7f),"未知弹药不当缺弹");
        Check(!ResourceUsePolicy.Emergency(SupplyKind.Disinfection,need with{Infection=float.NaN},.25f,.08f,.05f,.7f),"未知感染不使用");
        Check(!ResourceUsePolicy.Allowed(ResourceUseMode.EmergencyOnly,SupplyKind.Health,need with{Needed=false},true,true,.25f,.08f,.05f,.7f),"命令也不向不需要资源的对象使用");
        Check(!ResourceUsePolicy.Allowed((ResourceUseMode)9,SupplyKind.Health,need,true,true,.25f,.08f,.05f,.7f),"非法模式拒绝");
    }
    static void Automatic(){
        Reset();var bot=Bot(1);var target=Bot(0,false);target.Agent.Damage.Health=60;
        Check(!bot.RequestAction(Desc(bot,target.Agent))&&bot.Backpack.AmmoStorage.Pack.AmmoInPack==60,"默认安全保留，不给普通缺血自动使用");
        target.Agent.Damage.Health=20;var share=Desc(bot,target.Agent);Check(bot.RequestAction(share),"默认允许一次紧急医疗");
        Check(bot.RequestAction(share)&&bot.Actions.Count==1,"同一已接受描述符再次请求直接合并，不重建原版动作");
        var recorder=new Recorder{m_bot=bot,m_rootAction=new(){m_shareResourceAction=share}};
        Check(!(bool)Call("AllowDangerCancellation",recorder),"只为获授权的同一根补给动作取消危险停止规则");
        var other=Bot(2);Check(!other.RequestAction(Desc(other,target.Agent)),"预约同队友同类资源，不重复补给");
        var action=(PlayerBotActionShareResourcePack)share.ActionBase;action.m_state=PlayerBotActionShareResourcePack.State.Apply;
        ResourceUseAdapter.SetMode(bot.Agent,0);Check(action.Update(),"使用中改策略不打断已开始的一次");
        Gear.ResourcePackFirstPerson.ApplyPackBot(bot.Agent,target.Agent,bot.Backpack.Held);share.Terminated=true;ResourceUseAdapter.Tick();
        Check(bot.Backpack.AmmoStorage.Pack.AmmoInPack==40&&Gear.ResourcePackFirstPerson.Uses==1&&!ResourceUseAdapter.Owns(bot),"只通过原版方法消耗一次并结束");
        Check(!other.RequestAction(Desc(other,target.Agent)),"完成后同类对象冷却，防止紧接重复使用");
        Gear.ResourcePackFirstPerson.ApplyPackBot(bot.Agent,target.Agent,bot.Backpack.Held);Check(Gear.ResourcePackFirstPerson.Uses==1,"禁止无授权绕过原版调度直接消耗");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.SetMode(bot.Agent,2);
        Check(!bot.RequestAction(Desc(bot,target.Agent)),"战斗自动模式安全时保留");BotTaskBridge.Danger=true;
        Check(bot.RequestAction(Desc(bot,target.Agent)),"战斗模式危险时允许原版所选缺资源对象");
        Reset();bot=Bot(1);target=Bot(0,false);ResourceUseAdapter.SetMode(bot.Agent,3);
        Check(bot.RequestAction(Desc(bot,target.Agent)),"恢复原版不引入额外需要判定");
        var original=(PlayerBotActionShareResourcePack)bot.Actions[0];original.m_state=PlayerBotActionShareResourcePack.State.Apply;
        ResourceUseAdapter.SetMode(bot.Agent,0);Check(original.Update(),"从原版切到保留模式也先收尾已开始的单次使用");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=20;BotTaskBridge.Available=false;
        Check(!bot.RequestAction(Desc(bot,target.Agent)),"自动补给不抢扫描等明确任务");
        BotTaskBridge.Available=true;bot.SafeDestination=false;
        Check(!bot.RequestAction(Desc(bot,target.Agent)),"保留原版危险落点检查，不为补给走进致命地点");
        bot.SafeDestination=true;bot.Agent.Damage.Health=10;
        Check(!bot.RequestAction(Desc(bot,target.Agent)),"自身濒危不冒险给别人补给");
        bot.Agent.Damage.GrabbedAtAll=true;Check(!bot.RequestAction(Desc(bot,bot.Agent)),"被抓不能强制自疗");
        bot.Agent.Damage.GrabbedAtAll=false;Check(bot.RequestAction(Desc(bot,bot.Agent)),"自身濒危可以必要自疗");
        Reset();bot=Bot(1);target=Bot(0,false);bot.Backpack.Held.World.m_packType=eResourceContainerSpawnType.AmmoWeapon;
        target.Backpack.AmmoStorage.Main.AmmoInPack=target.Backpack.AmmoStorage.Special.AmmoInPack=0;target.Backpack.AmmoStorage.MainClip=15;
        Check(!bot.RequestAction(Desc(bot,target.Agent)),"弹夹内仍有足量弹药，不把备弹为零误当紧急");
        target.Backpack.AmmoStorage.MainClip=0;Check(bot.RequestAction(Desc(bot,target.Agent)),"双枪真实总弹药低时允许弹药补给");
        Reset();bot=Bot(1);target=Bot(0,false);bot.Backpack.Held.World.m_packType=eResourceContainerSpawnType.AmmoWeapon;
        target.Backpack.AmmoStorage.Main.AmmoInPack=target.Backpack.AmmoStorage.Special.AmmoInPack=0;
        target.Backpack.AmmoStorage.Main.CostOfBullet=10;target.Backpack.AmmoStorage.MainClip=1;
        Check(!bot.RequestAction(Desc(bot,target.Agent)),"大口径弹夹按真实弹药成本计算，不能混加子弹数和资源单位");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.HealthMax=400;target.Agent.Damage.Health=100;
        Check(bot.RequestAction(Desc(bot,target.Agent)),"修改后的最大生命按比例核验，不硬编码原版100血");
        Reset();bot=Bot(1);target=Bot(0,false);bot.Backpack.Held.World.m_packType=eResourceContainerSpawnType.AmmoTool;target.Backpack.AmmoStorage.Tool.AmmoInPack=4;
        Check(bot.RequestAction(Desc(bot,target.Agent)),"工具资源达独立阈值时允许一次");
        Reset();bot=Bot(1);target=Bot(0,false);bot.Backpack.Held.World.m_packType=eResourceContainerSpawnType.Disinfection;target.Agent.Damage.Infection=.8f;
        Check(bot.RequestAction(Desc(bot,target.Agent)),"消毒包按真实感染独立判定");
    }
    static void Commands(){
        Reset();var bot=Bot(1);var target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.SetMode(bot.Agent,0);
        ResourceUseAdapter.GiveOnce(bot,target.Agent);ResourceUseAdapter.GiveOnce(bot,target.Agent);
        Check(BotTaskBridge.Acquires>0&&ResourceUseAdapter.Owns(bot)&&TaskBoard.Journal.Count==1&&bot.Requests==0,"一次明确命令登记，重复点击合并、不即刻消耗");
        ResourceUseAdapter.Tick();Check(bot.Requests==1&&bot.Actions.Count==1,"仅请求一次原版动作");
        var desc=(PlayerBotActionShareResourcePack.Descriptor)bot.Actions[0].DescBase;var action=(PlayerBotActionShareResourcePack)desc.ActionBase;
        UnityEngine.Time.time+=.3f;ResourceUseAdapter.Cancel(bot);action.Update();ResourceUseAdapter.Tick();
        Check(bot.Stops==1&&!ResourceUseAdapter.Owns(bot)&&bot.Backpack.AmmoStorage.Pack.AmmoInPack==60,"开始交互前取消，停止真实动作，不消耗");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.GiveOnce(bot,target.Agent);ResourceUseAdapter.Tick();
        desc=(PlayerBotActionShareResourcePack.Descriptor)bot.Actions[0].DescBase;action=(PlayerBotActionShareResourcePack)desc.ActionBase;action.m_state=PlayerBotActionShareResourcePack.State.Apply;ResourceUseAdapter.Cancel(bot);
        Check(action.Update(),"使用中取消只停止后续动作");Gear.ResourcePackFirstPerson.ApplyPackBot(bot.Agent,target.Agent,bot.Backpack.Held);desc.Terminated=true;ResourceUseAdapter.Tick();
        var rows=TaskBoard.Journal.Capture(UnityEngine.Time.time).Rows;Check(rows.Count==1&&rows[0].State==TaskBoardState.Complete&&bot.Backpack.AmmoStorage.Pack.AmmoInPack==40,"实际用完一次核验成功，未谎报撤销消耗");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;bot.Accept=false;ResourceUseAdapter.GiveOnce(bot,target.Agent);ResourceUseAdapter.Tick();
        Check(!ResourceUseAdapter.Owns(bot)&&bot.Requests==1&&TaskBoard.Journal.Capture(100).Rows[0].State==TaskBoardState.Failed,"原版拒绝请求结束为失败");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.GiveOnce(bot,target.Agent);target.Agent.Owner=new();ResourceUseAdapter.Tick();
        Check(!ResourceUseAdapter.Owns(bot)&&bot.Requests==0,"补给接收人被替换拒绝原会话");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.GiveOnce(bot,target.Agent);LevelGeneration.Builder.CurrentFloor=new();ResourceUseAdapter.Tick();
        Check(!ResourceUseAdapter.Owns(bot)&&bot.Requests==0,"关卡对象替换立即使旧补给授权失效");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.SetMode(bot.Agent,0);bot.Agent.Owner=new(){IsBot=true};
        Check(ResourceUseAdapter.Mode(bot.Agent)==ResourceUseMode.EmergencyOnly,"新机器人不继承旧拥有者的本局模式");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;BotTaskBridge.Execute=false;ResourceUseAdapter.GiveOnce(bot,target.Agent);UnityEngine.Time.time+=16;ResourceUseAdapter.Tick();
        Check(!ResourceUseAdapter.Owns(bot)&&bot.Requests==0,"等待权限15秒超时，不无限重试");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.GiveOnce(bot,target.Agent);UnityEngine.Time.time+=46;ResourceUseAdapter.Tick();
        Check(!ResourceUseAdapter.Owns(bot)&&bot.Requests==0,"总时限45秒不能无限续期");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.GiveOnce(bot,target.Agent);ResourceUseAdapter.SetMode(bot.Agent,0);ResourceUseAdapter.Reset();
        Check(!ResourceUseAdapter.HasState&&ResourceUseAdapter.Mode(bot.Agent)==ResourceUseMode.EmergencyOnly,"关卡清理释放全部授权、模式和缓存");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;int returns=ItemTaskAdapter.Returns;
        ResourceUseAdapter.GiveOnce(bot,target.Agent);ResourceUseAdapter.Tick();desc=(PlayerBotActionShareResourcePack.Descriptor)bot.Actions[0].DescBase;
        ResourceUseAdapter.Cancel(bot,true);((PlayerBotActionShareResourcePack)desc.ActionBase).Update();ResourceUseAdapter.Tick();
        Check(ItemTaskAdapter.Returns==returns+1&&!ResourceUseAdapter.Owns(bot),"取消并返回在原版收尾和权限释放后发起一次返回");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.GiveOnce(bot,target.Agent);ResourceUseAdapter.Tick();
        desc=(PlayerBotActionShareResourcePack.Descriptor)bot.Actions[0].DescBase;desc.Terminated=true;ResourceUseAdapter.Tick();
        Check(TaskBoard.Journal.Capture(100).Rows[0].State==TaskBoardState.Failed,"动作完成但没有消费凭据不能记为补给成功");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;long oldItem=bot.Backpack.Held.Pointer.ToInt64();bot.Backpack.Held=new();
        ResourceUseAdapter.GiveOnce(bot,target.Agent,oldItem);Check(!ResourceUseAdapter.Owns(bot)&&bot.Requests==0,"菜单展示的资源包被替换后必须重开确认，不能使用另一种物品");
        Reset();bot=Bot(1);target=Bot(0,false);target.Agent.Damage.Health=60;ResourceUseAdapter.GiveOnce(bot,target.Agent);ResourceUseAdapter.Tick();
        Gear.ResourcePackFirstPerson.ApplyPackBot(bot.Agent,target.Agent,bot.Backpack.Held);Check(Gear.ResourcePackFirstPerson.Uses==0,"接近阶段不能绕过原版计时直接消耗");
        desc=(PlayerBotActionShareResourcePack.Descriptor)bot.Actions[0].DescBase;((PlayerBotActionShareResourcePack)desc.ActionBase).m_state=PlayerBotActionShareResourcePack.State.Apply;
        target.Agent.Damage.Health=100;Gear.ResourcePackFirstPerson.ApplyPackBot(bot.Agent,target.Agent,bot.Backpack.Held);
        Check(Gear.ResourcePackFirstPerson.Uses==0&&bot.Backpack.AmmoStorage.Pack.AmmoInPack==60,"实际使用前再次核查需要，队友已补满则不消耗");
    }
    static void RootSelection(){
        Reset();var bot=Bot(1);var target=Bot(0,false);target.Agent.Damage.Health=60;var root=new RootPlayerBotAction{m_bot=bot,m_shareResourceAction=Desc(bot,target.Agent)};var combat=new PlayerBotActionBase.Descriptor(bot);
        object[] args={root,root.m_shareResourceAction,combat};Call("AfterSelection",args);Check(ReferenceEquals(args[1],combat),"补给被拒绝恢复已有战斗候选，不能清除它");
        ResourceUseAdapter.SetMode(bot.Agent,0);object[] pre={root,combat,null};Check(!(bool)Call("BeforeSelection",pre)&&ReferenceEquals(pre[2],combat),"仅命令模式跳过原版扫描补给对象但保留其它候选");
        ResourceUseAdapter.SetMode(bot.Agent,2);BotTaskBridge.Danger=true;args=new object[]{root,root.m_shareResourceAction,combat};Call("AfterSelection",args);Check(ReferenceEquals(args[1],root.m_shareResourceAction),"模式切换立即清缓存，战斗候选允许补给");
        SNetwork.SNet.IsMaster=false;Check(bot.RequestAction(Desc(bot,target.Agent)),"非房主不干涉原版共享请求");
    }
    static void Main(){ResourceUseAdapter.Configure(new());var harmony=new HarmonyLib.Harmony();ResourceUseAdapter.Install(harmony);Check(harmony.Patched.Count==5,"原版与AI取消接口全部存在，五组注册完成");Policy();Automatic();Commands();RootSelection();Console.WriteLine("PASS: ResourceUse "+count+" assertions，真实适配器配合模拟原版动作，未执行游戏原生函数。");}
}
