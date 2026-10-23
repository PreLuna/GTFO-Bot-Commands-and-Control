using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BoosterImplants;
using CellMenu;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SNetwork;
using PreLuna.Captain.Boosters;

static class BoosterAdapterTests
{
    static int checks;
    static object Call(string name, params object[] args)
    {
        try { return typeof(BotBoosterAdapter).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static SNet_Player Player(int slot, bool bot)
    {
        var value = new SNet_Player { IsBot = bot, PlayerSlot = new SNet_Slot { index = slot }, NickName = "Test " + slot };
        value.PlayerAgent = new Player.PlayerAgent { Owner = value }; SNet.Slots.Players[slot] = value; return value;
    }
    static BoosterImplant Stock(int category, uint id, float value, int effects = 1)
    {
        var result = new BoosterImplant { Category = (BoosterImplantCategory)category, InstanceId = id, TemplateId = 100 + (uint)category,
            Uses = 5, Effects = new Il2CppStructArray<pBoosterEffectData>(effects), Conditions = new Il2CppStructArray<uint>(1) };
        for (int i = 0; i < effects; i++) result.Effects[i] = new pBoosterEffectData { BoosterEffectID = 52 + (uint)i, EffectValue = value };
        result.Conditions[0] = 1;
        PersistentInventoryManager.Stock[category].Add(new BoosterImplantInventoryItem { Implant = result }); return result;
    }
    static CM_PlayerLobbyBar Open(SNet_Player owner, int category)
    {
        var bar = new CM_PlayerLobbyBar { m_player = owner };
        var args = new object[] { bar, (BoosterImplantCategory)category, -1, false };
        Check((bool)Call("OpenIndexPrefix", args), "机器人原版弹窗可以绑定目标");
        bar.m_popupScrollWindow.m_nonContentItems.Add(new CM_Item { OnBtnPressCallback = new Il2CppSystem.Action<int> {
            Target = new CM_PlayerLobbyBar.__c__DisplayClass141_0 { __4__this = bar }
        }});
        Call("RenderPostfix", args[3]); return bar;
    }
    static void Button(CM_PlayerLobbyBar bar, string action, object argument, bool exception = false)
    {
        var closure = bar.m_popupScrollWindow.m_nonContentItems[0].OnBtnPressCallback.Target;
        var args = new object[] { closure, false };
        Check((bool)Call("CallbackPrefix", args) && (bool)args[1], "按钮作用域仅绑定机器人目标");
        if (argument == null) Check(!(bool)Call(action), "机器人按钮拦截原库存全卸下操作");
        else Check(!(bool)Call(action, argument), "机器人按钮拦截原库存操作");
        if (exception) Call("CallbackFinalizer", new Exception("simulated UI fault"), args[1]);
        else Call("CallbackPostfix", args[1]);
    }
    static void Tick()
    {
        UnityEngine.Time.realtimeSinceStartup += 3; UnityEngine.Time.frameCount++;
        Call("ManagerPostfix", BoosterImplantManager.Current);
    }
    static pBoosterImplantData Data(SNet_Player owner, int category = 0) => BoosterImplantManager.Current.States[owner.Pointer].BoosterImplantDatas[category];
    static void Main(string[] args)
    {
        BepInEx.Paths.ConfigPath = args[0]; Directory.CreateDirectory(args[0]);
        string save = Path.Combine(args[0], "PreLuna.Captain.Boosters.json"); if (File.Exists(save)) File.Delete(save);
        var config = new ConfigFile(); SNet.IsMaster = true; GameStateManager.CurrentStateName = eGameStateName.Lobby;
        var host = Player(0, false); var bot = Player(1, true); var bot2 = Player(2, true);
        var main = Stock(0, 10, 1.15f); var high = Stock(1, 11, 1.2f); Stock(2, 12, .8f); Stock(0, 90, 9f, 9);
        BotBoosterAdapter.Configure(config);
        HarmonyLib.Harmony.ThrowOnPatch = 3; BotBoosterAdapter.Install();
        Check(HarmonyLib.Harmony.PatchCount == 0, "注册中途失败撤销整组补丁，不保留半套库存保护");
        var failedOpen = new object[] { new CM_PlayerLobbyBar { m_player = bot }, BoosterImplantCategory.Muted, -1, false };
        Check(!(bool)Call("OpenIndexPrefix", failedOpen), "补丁组未安装成功不能开启机器人编辑");
        HarmonyLib.Harmony.ThrowOnPatch = 0; BotBoosterAdapter.Install();
        BoosterImplantManager.EnableOnSync = true; // Exercise a native sync implementation that also enables state.
        Check(HarmonyLib.Harmony.PatchCount == 37, "全部注册事务保护及安全的整页状态事件，避免半安装");
        Check(!HarmonyLib.Harmony.PatchedTargets.Exists(x => x.DeclaringType == typeof(CM_PageLoadout)
            && x.Name == "OnSetGuiLayerVisible"), "不再注册崩溃转储定位的可见性短方法原生钩子");
        Tick(); var bar = Open(bot, 0);
        Check(typeof(BotBoosterAdapter).GetMethod("GetSlotPrefix", BindingFlags.NonPublic | BindingFlags.Static) == null, "不再注册全局可空原生 out 查询，正常空槽不进入不兼容 trampoline");
        Button(bar, "PrepareInventoryPrefix", (uint)10);
        Check(Data(bot).BoosterEffectDatas[0].EffectValue == 1.15f, "使用主机列表原数值，不重复乘完美加成");
        var slotView = new CM_BoosterImplantSlot { m_category = BoosterImplantCategory.Muted };
        var stableBar = new CM_PlayerLobbyBar { m_player = bot };
        CM_PlayerLobbyBar.instances.Add(stableBar);
        stableBar.m_boosterImplantSlotHolder.m_categorySlots[BoosterImplantCategory.Muted] = slotView;
        slotView.transform.parent = stableBar.m_boosterImplantSlotHolder.transform;
        Call("SlotPostfix", slotView, bot);
        Check(slotView.m_player == bot && slotView.m_boosterInSlot.TemplateId == 100, "原版提前返回时仍绑定正确机器人和所选数据");
        Check(slotView.m_boosterIcon.Active && slotView.m_boosterIcon.Enabled && !slotView.m_emptyIcon.gameObject.activeSelf, "选中槽位显示原版图标并可点击");
        Call("BarPostfix", stableBar); var stableIcon = slotView.m_boosterInSlot;
        Check(stableBar.m_parentPage.m_guiLayer.m_menu == null && BotBoosterView.PageVisible(stableBar)
            && BotBoosterView.SlotVisible(slotView, true), "真实CellUI的m_menu为空也能恢复显示，不拒绝有效页面");
        Call("BarPostfix", stableBar);
        Check(ReferenceEquals(stableIcon, slotView.m_boosterInSlot), "稳定页面不每0.25秒重建强化剂图标对象");
        stableBar.m_boosterImplantSlotHolder.gameObject.SetActive(false);
        Call("BarEventPostfix", stableBar);
        Check(stableBar.m_boosterImplantSlotHolder.gameObject.activeSelf, "原版页面事件隐藏控件后可按事件恢复显示");
        stableBar.m_boosterImplantAlign.gameObject.SetActive(false);
        Check(stableBar.m_boosterImplantSlotHolder.gameObject.activeSelf && !stableBar.m_boosterImplantSlotHolder.gameObject.activeInHierarchy,
            "模拟真实父节点隐藏：容器自身仍开启但实际不可见");
        Call("BarPostfix", stableBar);
        Check(stableBar.m_boosterImplantSlotHolder.gameObject.activeInHierarchy && ReferenceEquals(stableIcon, slotView.m_boosterInSlot),
            "缓存命中仍恢复父节点，不重建强化剂数据");
        slotView.m_boxRoot.SetActive(false); slotView.m_boosterIcon.m_iconForegroundRenderer.Sprite.enabled = false;
        Call("BarPostfix", stableBar);
        Check(BotBoosterView.SlotVisible(slotView, true) && ReferenceEquals(stableIcon, slotView.m_boosterInSlot),
            "子节点和Renderer被隐藏也恢复，数据对象保持缓存");
        UnityEngine.Time.realtimeSinceStartup += 3;
        stableBar.m_parentPage.m_isActive = false; stableBar.m_boosterImplantAlign.gameObject.SetActive(false);
        Call("BarPostfix", stableBar);
        Check(!stableBar.m_boosterImplantAlign.gameObject.activeSelf, "未激活配装页不被后台刷新打开");
        Check(PreLuna.Captain.Plugin.Logger.Infos.Exists(x => x.Contains("reason=页面未激活")), "页面门禁拒绝也记录具体原因");
        stableBar.m_parentPage.m_isActive = true; stableBar.m_parentPage.m_guiLayer.Root.SetActive(false);
        Call("BarPostfix", stableBar);
        Check(!stableBar.m_boosterImplantAlign.gameObject.activeSelf, "Tab菜单关闭时即使页面对象还活着也不恢复");
        stableBar.m_parentPage.m_guiLayer.Root.SetActive(true);
        stableBar.m_parentPage.m_guiLayer.Visible = false;
        Call("PageEventPostfix", stableBar.m_parentPage);
        Check(!stableBar.m_boosterImplantAlign.gameObject.activeSelf, "菜单图层已隐藏但GameObject尚开启时也不刷新");
        stableBar.m_parentPage.m_guiLayer.Visible = true;
        UnityEngine.Time.realtimeSinceStartup += 3;
        int gateLogs = PreLuna.Captain.Plugin.Logger.Infos.Count;
        Call("BarPostfix", stableBar);
        int onceLogs = PreLuna.Captain.Plugin.Logger.Infos.Count;
        for (int i = 0; i < 20; i++) Call("BarPostfix", stableBar);
        Check(onceLogs > gateLogs && PreLuna.Captain.Plugin.Logger.Infos.Count == onceLogs,
            "入口状态不变时不重复刷日志");
        stableBar.m_boosterImplantAlign.gameObject.SetActive(false);
        stableBar.m_parentPage.m_guiLayer.ThrowOnRead = true;
        Check(!(bool)Call("CanView", stableBar), "原生可见性读取异常按不可见处理，不逃出界面补丁");
        Call("BarPostfix", stableBar); Call("PageEventPostfix", stableBar.m_parentPage);
        Check(!stableBar.m_boosterImplantAlign.gameObject.activeSelf, "未就绪页面回调异常不打开槽位");
        GameStateManager.CurrentStateName = eGameStateName.Startup;
        int startupLogs = PreLuna.Captain.Plugin.Logger.Infos.Count;
        Call("BarPostfix", stableBar); Call("PageEventPostfix", stableBar.m_parentPage);
        Check(startupLogs == PreLuna.Captain.Plugin.Logger.Infos.Count,
            "启动初始化阶段不读取原生可见性，不修复界面或记录机器人日志");
        GameStateManager.CurrentStateName = eGameStateName.Lobby;
        stableBar.m_parentPage.m_guiLayer.ThrowOnRead = false;
        SNet.IsMaster = false; UnityEngine.Time.realtimeSinceStartup += 3;
        Call("BarPostfix", stableBar);
        Check(PreLuna.Captain.Plugin.Logger.Infos.Exists(x => x.Contains("reason=非房主"))
            && !stableBar.m_boosterImplantAlign.gameObject.activeSelf, "客户端拒绝原因可见且不修改机器人界面");
        SNet.IsMaster = true;
        GameStateManager.IsReady = true; stableBar.HideLoadoutUI(true, true);
        Call("PageEventPostfix", stableBar.m_parentPage);
        Check(BotBoosterView.SlotVisible(slotView, true) && !slotView.m_slotCollider.enabled,
            "整页准备流程后槽位可见且点击锁定");
        var readyOpen = new object[] { stableBar, BoosterImplantCategory.Muted, -1, false };
        Check(!(bool)Call("OpenIndexPrefix", readyOpen) && !(bool)Call("SlotClickPrefix", slotView), "准备后直接入口与槽位点击均不能编辑");
        var preparedSave = File.ReadAllText(save);
        var oldReadyCallback = new object[] { bar.m_popupScrollWindow.m_nonContentItems[0].OnBtnPressCallback.Target, false };
        Check(!(bool)Call("CallbackPrefix", oldReadyCallback) && File.ReadAllText(save) == preparedSave,
            "准备时旧弹窗按钮被拒绝，不进入库存操作作用域");
        GameStateManager.IsReady = false; Call("PageEventPostfix", stableBar.m_parentPage);
        Check(slotView.m_slotCollider.enabled && ReferenceEquals(stableIcon, slotView.m_boosterInSlot), "取消准备恢复编辑而不重建槽位内容");
        var emptyView = new CM_BoosterImplantSlot { m_category = BoosterImplantCategory.Aggressive };
        stableBar.m_boosterImplantSlotHolder.m_categorySlots[BoosterImplantCategory.Aggressive] = emptyView;
        emptyView.transform.parent = stableBar.m_boosterImplantSlotHolder.transform;
        Call("SlotPostfix", emptyView, bot);
        Check(emptyView.m_boosterInSlot == null && !emptyView.m_boosterIcon.Active && emptyView.m_emptyIcon.gameObject.activeSelf, "空槽保持原版空图标");
        var staleCard = new CM_BoosterImplantSlotItem { BoosterInstanceID = 10 };
        Call("CardRenderPrefix", bar, staleCard, false); Call("CardPostfix", staleCard); Call("RenderPostfix", true);
        Check(PersistentInventoryManager.Stock[0][0].Implant.Uses == 5 && !PersistentInventoryManager.Stock[0][0].Prepared, "不改变主机次数或准备状态");
        Check(PersistentInventoryManager.PrepareCalls == 0 && PersistentInventoryManager.UnprepareCalls == 0 && PersistentInventoryManager.DropCalls == 0, "没有进入账号库存事务");
        var bar2 = Open(bot2, 0); Button(bar2, "PrepareInventoryPrefix", (uint)10);
        Check(Data(bot).PointerEquals(Data(bot2)) == false, "机器人各自拥有原版数据对象");
        Check(Data(bot2).BoosterEffectDatas[0].EffectValue == 1.15f, "同一来源可以供第二个机器人使用");
        bar = Open(bot, 1); Button(bar, "PrepareInventoryPrefix", (uint)11);
        Check(Data(bot, 0).BoosterImplantID == 100 && Data(bot, 1).BoosterImplantID == 101, "不同类别不覆盖其他槽位");
        Button(bar, "UnprepareAllPrefix", null);
        Check(Data(bot, 1).BoosterImplantID == 101 && Data(bot2).BoosterImplantID == 100, "原弹窗全卸下的内部操作不会误卸其他槽或队友");
        Button(bar, "DropInventoryPrefix", (uint)11);
        Check(Data(bot, 1).BoosterImplantID == 0 && PersistentInventoryManager.Stock[1].Count == 1, "丢弃按钮改成机器人卸下，保留主机库存项");
        Check((bool)Call("PrepareInventoryPrefix", (uint)10), "真人操作在机器人按钮作用域外继续走原流程");
        Button(bar, "PrepareInventoryPrefix", (uint)11, true);
        Check((bool)Call("PrepareInventoryPrefix", (uint)10), "按钮抛异常后作用域也恢复，不拦截后续真人操作");
        bar = Open(bot, 0); Button(bar, "PrepareInventoryPrefix", (uint)90);
        Check(Data(bot).BoosterEffectDatas[0].EffectValue == 1.15f, "超网络容量的自定义项不能静默截断装备");
        SNet.IsMaster = false;
        var denied = new object[] { new CM_PlayerLobbyBar { m_player = bot }, BoosterImplantCategory.Muted, -1, false };
        Check(!(bool)Call("OpenIndexPrefix", denied), "客户端不能开启机器人装备编辑");
        SNet.IsMaster = true;
        var humanArgs = new object[] { new CM_PlayerLobbyBar { m_player = host }, BoosterImplantCategory.Muted, -1, false };
        Check((bool)Call("OpenIndexPrefix", humanArgs) && !(bool)humanArgs[3], "真人原版弹窗保持原流程");
        Call("ChangeStatePrefix", eGameStateName.Generating); GameStateManager.CurrentStateName = eGameStateName.Generating;
        main.Effects[0] = new pBoosterEffectData { BoosterEffectID = 52, EffectValue = 9f };
        Call("ResetManagerPrefix"); BoosterImplantManager.Current = new BoosterImplantManager();
        GameStateManager.CurrentStateName = eGameStateName.InLevel; Tick();
        Check(Data(bot).BoosterEffectDatas[0].EffectValue == 1.15f, "生成阶段原版管理器重建不会丢失已冻结快照");
        stableBar.HideLoadoutUI(true, true); Call("PageEventPostfix", stableBar.m_parentPage);
        Check(slotView.m_boosterInSlot.TemplateId == 100 && BotBoosterView.SlotVisible(slotView, true)
            && !slotView.m_slotCollider.enabled, "关卡内Tab使用被冻结的原装备，库存已改变也不显示成空槽");
        int viewSyncs = BoosterImplantManager.SyncCalls; var frozenIcon = slotView.m_boosterInSlot;
        for (int i = 0; i < 8; i++) { stableBar.HideLoadoutUI(true, true); Call("PageEventPostfix", stableBar.m_parentPage); }
        Check(BoosterImplantManager.SyncCalls == viewSyncs && ReferenceEquals(frozenIcon, slotView.m_boosterInSlot),
            "反复打开Tab只恢复可见性，不额外同步或重建数据");
        Check(!(bool)Call("SlotClickPrefix", slotView) && !(bool)Call("OpenIndexPrefix", readyOpen), "对局内两条编辑入口均锁定");
        var beforeSave = File.ReadAllText(save);
        Call("PrepareCardPrefix", staleCard);
        Check(File.ReadAllText(save) == beforeSave && PersistentInventoryManager.PrepareCalls == 0, "关卡内迟到的装备按钮不修改配置或账号库存");
        var state = BoosterImplantManager.Current.States[bot.Pointer];
        Check(state.PlayerAgent == bot.PlayerAgent && state.IsEnabled && state.Updates == 1, "绑定机器人自身代理并调用原版条件更新");
        Check(state.OtherModifier == 77, "未清除其他插件修饰器");
        Check(BoosterViewPolicy.Read(null, 0, true, false).Status == BoosterViewStatus.Pending, "保存选择不冒充已经生效");
        Check(BoosterViewPolicy.Read(state, 0, true, false).Status == BoosterViewStatus.Synced, "只有同步数据而无原版实例显示待激活");
        state.BoosterImplants = new(new[] { new BoosterImplantInstance { BoosterImplantID = Data(bot).BoosterImplantID } });
        Check(BoosterViewPolicy.Read(state, 0, true, false).Status == BoosterViewStatus.Inactive, "实例条件未满足不能显示生效");
        state.BoosterImplants[0].ConditionsMet = true;
        Check(BoosterViewPolicy.Read(state, 0, true, false).Status == BoosterViewStatus.Active, "原版启用与实例条件满足才显示生效");
        state._IsEnabled_k__BackingField = false;
        Check(BoosterViewPolicy.Read(state, 0, true, false).Status == BoosterViewStatus.Inactive, "原版未启用即使满足条件也不冒充生效");
        state._IsEnabled_k__BackingField = true;
        Check(BoosterViewPolicy.Read(state, 0, true, true).Status == BoosterViewStatus.Unavailable, "接口被停用时不查询原版效果");
        Check(BoosterViewPolicy.ResolveY(5, 3, 4, 6, .5f) == 3.5f, "重叠强化剂栏移到服装下方并留标题间隔");
        Check(BoosterViewPolicy.ResolveY(3, 3, 4, 6, .5f) == 3, "已分离布局不额外偏移");
        Check(BoosterViewPolicy.ResolveY(10, 3, 4, 6, .5f) == 10, "上方不重叠的栏不下移");
        int sync = BoosterImplantManager.SyncCalls; Tick();
        Check(BoosterImplantManager.SyncCalls == sync, "稳定装备不反复同步或重建效果");
        var oldBot=bot;var oldState=state;int cleanupCalls=Il2CppInterop.Runtime.IL2CPP.NativeCalls;
        SNet.Slots.Players[1]=null;Tick();
        Check(oldState.BoosterImplantDatas[0].BoosterImplantID==0 && Il2CppInterop.Runtime.IL2CPP.NativeCalls>cleanupCalls,
            "移除机器人调用正确引用数组的原生清理，未进入损坏包装");
        bot=Player(1,true);Tick();state=BoosterImplantManager.Current.States[bot.Pointer];
        Check(Data(bot).BoosterEffectDatas[0].EffectValue==1.15f && Data(bot,1).BoosterImplantID==101,
            "局内重加入按同槽冻结的三类装备恢复，不采纳当前9倍库存");
        Check(Data(bot).Conditions[0]==1 && Data(bot).ConditionCount==1,"重加入保留真实冻结条件，开关由统一运行判定处理");
        Check(oldState.Player==oldBot && !oldState.IsEnabled,"旧拥有者被清理，新拥有者有独立状态");
        int rejoinSync=BoosterImplantManager.SyncCalls;Tick();Tick();
        Check(BoosterImplantManager.SyncCalls==rejoinSync,"重加入恢复后稳定更新不反复同步");
        Check(File.ReadAllText(save)==beforeSave,"重加入不改选择文件或账号库存");
        var human = Player(1, false); Call("MappingPrefix", 1);
        Check(state.BoosterImplantDatas[0].BoosterImplantID == 0 && !state.IsEnabled, "真人接替前移除旧机器人本插件效果");
        Check(!BoosterImplantManager.Current.States.ContainsKey(human.Pointer), "清理旧机器人不会创建或改写新真人强化剂状态");
        Check(Data(bot2).BoosterEffectDatas[0].EffectValue == 1.15f, "一个机器人替换不影响其他机器人");
        var original = BoosterImplantManager.Current.States[bot2.Pointer];
        original.BoosterImplantDatas[0].BoosterEffectDatas[0] = new pBoosterEffectData { BoosterEffectID = 52, EffectValue = 4f };
        Call("CleanupPrefix");
        Check(original.BoosterImplantDatas[0].BoosterEffectDatas[0].EffectValue == 4f, "清理时不覆盖其他来源已经替换的强化剂数据");
        Check((bool)Call("PrepareInventoryPrefix", (uint)10), "关卡清理后没有遗留库存拦截作用域");
        GameStateManager.CurrentStateName = eGameStateName.Lobby; bot = Player(1, true);
        main.Effects[0] = new pBoosterEffectData { BoosterEffectID = 52, EffectValue = 1.15f }; Tick();
        BoosterImplantManager.RejectSync = true; bar = Open(bot, 2); Button(bar, "PrepareInventoryPrefix", (uint)12);
        Check(Data(bot, 2).BoosterImplantID == 0, "拒绝同步时不把 UI 选择伪装成已经应用的原版状态");
        int rejected = BoosterImplantManager.SyncCalls;
        UnityEngine.Time.realtimeSinceStartup += .5f; UnityEngine.Time.frameCount++; Call("ManagerPostfix", BoosterImplantManager.Current);
        Check(BoosterImplantManager.SyncCalls == rejected, "同步失败有重试间隔，不每帧重试");
        BoosterImplantManager.RejectSync = false; UnityEngine.Time.realtimeSinceStartup += 5; Tick();
        Check(Data(bot, 2).BoosterEffectDatas[0].EffectValue == .8f, "原版状态就绪后保留来源负面效果并重试");
        SNet.IsMaster = false; Tick();
        Check(Data(bot).BoosterImplantID == 0 && Data(bot, 2).BoosterImplantID == 0, "失去主机权限清理本机机器人效果与编辑目标");
        Check(PlayerBoosterImplantState.BadWrapperCalls==0,"所有清理和回退都不调用错误的StructArray类包装");
        var stale = new object[] { bar.m_popupScrollWindow.m_nonContentItems[0].OnBtnPressCallback.Target, false };
        Check(!(bool)Call("CallbackPrefix", stale), "失去主机权限后旧机器人按钮不能变成真人库存操作");
        SNet.IsMaster = true; GameStateManager.CurrentStateName = eGameStateName.Lobby;
        Open(bot, 0);
        Check(!(bool)Call("CallbackPrefix", stale), "新弹窗打开后旧按钮不能操作新弹窗目标");
        Check(!(bool)Call("PrepareCardPrefix", staleCard) && !(bool)Call("UnprepareCardPrefix", staleCard), "旧物品卡迟到操作也不能落入真人库存流程");
        Check((bool)Call("PrepareCardPrefix", new CM_BoosterImplantSlotItem { BoosterInstanceID = 10 }), "未属于机器人窗口的真人卡片保持原流程");
        Call("OpenIndexPrefix", humanArgs); Call("CardPostfix", staleCard);
        Check((bool)Call("PrepareCardPrefix", staleCard), "物品卡被原版重新用于真人窗口时释放旧机器人路由");
        BoosterImplantManager.RejectSync = true;
        bar = Open(bot, 2); Button(bar, "PrepareInventoryPrefix", (uint)12);
        for (int i = 0; i < 5; i++) { UnityEngine.Time.realtimeSinceStartup += 70; Tick(); }
        int paused = BoosterImplantManager.SyncCalls;
        for (int i = 0; i < 5; i++) { UnityEngine.Time.realtimeSinceStartup += 70; Tick(); }
        Check(BoosterImplantManager.SyncCalls == paused, "连续拒绝达到上限后停止重试，不产生无限失败循环");
        BoosterImplantManager.RejectSync = false;
        bar = Open(bot, 2); Button(bar, "PrepareInventoryPrefix", (uint)12);
        Check(Data(bot, 2).BoosterImplantID == 102, "明确重新选择恢复已暂停槽位");
        BoosterImplantManager.SyncException = new TypeLoadException("模拟永久接口错误");
        bar = Open(bot, 1); Button(bar, "PrepareInventoryPrefix", (uint)11);
        int stopped = BoosterImplantManager.SyncCalls;
        for (int i = 0; i < 4; i++) { UnityEngine.Time.realtimeSinceStartup += 70; Tick(); }
        Check(BoosterImplantManager.SyncCalls == stopped, "结构性接口错误后停止整个进程的自动更新");
        Check(PersistentInventoryManager.PrepareCalls == 0 && PersistentInventoryManager.DropCalls == 0, "停止更新不会退回账号库存事务");
        Console.WriteLine("PASS: 实际适配代码的库存路由／生命周期模拟 " + checks + " 项检查。原版原生代码及真实联机未在此测试运行。");
    }
}
static class PacketTestExtensions { public static bool PointerEquals(this pBoosterImplantData left, pBoosterImplantData right) => ReferenceEquals(left, right); }
