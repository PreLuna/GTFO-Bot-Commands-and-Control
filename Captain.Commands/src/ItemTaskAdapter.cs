using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using LevelGeneration;
using Localization;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class ItemTaskAdapter
    {
        static readonly Dictionary<IntPtr, ItemTaskRunner> tasks = new();
        static readonly HashSet<IntPtr> automatic = new();
        sealed class PendingCommand { public int Command; public IntPtr Owner; public RaycastHit? Hit; public Pose? Placement; public ItemInLevel Item; public float CreatedAt; }
        static readonly Dictionary<int, PendingCommand> pending = new();
        static readonly HashSet<IntPtr> items = new(), containers = new();
        static readonly Dictionary<uint, string> labels = new();
        sealed class MenuContext { public ControlMenuSnapshot Control; public AIGraph.AIG_CourseNode SearchOrigin; public ItemInLevel Item; public KnownSupplySnapshot Supplies; public KnownTerminalSnapshot Terminals; public KnownContainerSnapshot Containers; public KnownInteractionSnapshot Interactions; public TaskBoardSnapshot Board; public List<CaptainMenuBot> Bots; public Enemies.EnemyAgent Enemy, CombatEnemy; public List<ToolSupplyTarget> Tools; public SharedMeleeView Melee; public MarkedOpenTarget OpenTarget; public MarkedPosition Position; public GuardTargetSnapshot Guards; public RaycastHit? Hit; public Pose? Placement; public IntPtr Floor, Issuer; }
        static MenuContext menuContext;
        static readonly Dictionary<uint, CaptainMenuLeaf> menuCommands = new();
        static ConfigEntry<bool> enabled, teleport;
        static ConfigEntry<KeyCode> shortcut;
        static ConfigEntry<float> pickupReach, teleportDistance;
        internal static ItemInLevel MenuTarget { get; private set; }
        internal static float PickupReach => Setting(pickupReach, 2.2f, 1.5f, 3f);
        internal static float TeleportDistance => Setting(teleportDistance, 8, 3, 30);
        static float Setting(ConfigEntry<float> entry, float fallback, float min, float max)
            => float.IsFinite(entry?.Value ?? fallback) ? Math.Clamp(entry?.Value ?? fallback, min, max) : fallback;
        static PUI_CommunicationMenu cachedMenu;
        static PUI_CommunicationMenu viewMenu;
        static CommunicationNode savedRoot;
        static CommunicationNode viewRoot;
        static Harmony patches;
        static bool installed;
        internal static bool Installed => installed;
        internal static bool HasItemTask(PlayerAIBot bot) => bot != null && tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished;
        // A control-created station ignores only records proven manually paused and fully yielded.
        // Continuing any of those tasks makes this true again, so the original station runner yields.
        internal static bool PausedWorkBlocks(PlayerAIBot bot)
            => bot == null || RescueOrders.Owns(bot) || ToolSupplyAdapter.Owns(bot) || ResourceUseAdapter.Owns(bot)
                || SharedMeleeBridge.Owns(bot) || NativePickupAdapter.Owns(bot) || SearchTaskAdapter.Owns(bot) || SingleKillAdapter.Owns(bot)
                || OpenTaskAdapter.PausedWorkBlocks(bot) || CollectTaskAdapter.PausedWorkBlocks(bot) || MovementOrders.PausedWorkBlocks(bot)
                || tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished && !(task.ManualPaused && task.ManuallySuspended);
        internal static void Uninstall()
        {
            installed = false;
            try { ResetTasks(); }
            finally { patches?.UnpatchSelf(); CaptainMenuInput.Uninstall(); }
        }
        static IntPtr floor, textService;
        static float nextTick, lastTick, nextWarning, nextPickupTick, nextPickupWarning;
        public static bool TeleportEnabled => teleport?.Value == true;
        internal static bool CommunicationOpen => cachedMenu?.m_active == true;
        internal static bool ControlMenuRequired => BotControlService.HasActive;
        public static void Configure(ConfigFile config)
        {
            enabled = config.Bind("机器人任务", "启用", true, "五组任务优先菜单；普通拾取、任务／特殊槽原版搬运、放回容器、部署／回收、取消／返回。需要机器人仲裁 API v1，仅房主操作；待实现入口仅显示说明。");
            teleport = config.Bind("机器人任务", "任务传送", true, "队长普通物品及前往位置任务经统一仲裁传送，可暂停扫描并在完成后返回；自动拾取不传送。即时搜寻只查询、不移动。任务／特殊槽搬运走原版命令，传送与返程由更好的AI原版任务配置控制，先验证普通门可达范围。");
            shortcut = config.Bind("机器人任务", "快捷入口按键", KeyCode.None, "通讯键（默认 Q）首次打开队长首页，原版通讯／角色语音保留独立入口；额外快捷键默认 None，避免占用第三人称 F6。数字选择期间不切枪、开关手电或使用物品。Esc 或再次按自定义入口键关闭；终端及聊天输入时不响应。");
            AutoPickupAdapter.Configure(config);
            SingleKillAdapter.Configure(config);
            OpenTargetMarks.Configure(config);
            ResourceUseAdapter.Configure(config);
            MovementOrders.Configure(config);
            pickupReach = config.Bind("机器人任务", "拾取交互距离", 2.2f, "拾取／放回水平距离，范围1.5–3米，默认2.2米；仍检查容器开启、真实遮挡和物品同步。");
            teleportDistance = config.Bind("机器人任务", "任务传送最小距离", 8f, "明确物品／前往位置任务超过此距离才尝试传送到安全接近点，范围3–30米；近处行走，自动任务不传送。");
        }
        public static void Install()
        {
            if (enabled?.Value != true) return;
            try
            {
                if (!BotTaskBridge.Bind()) { Plugin.Logger.LogWarning("机器人任务未启用：需要更好的AI r9／1.0.5 的扫描恢复与自动拾取接口；中文与强化剂模块继续独立运行。"); return; }
                var press = AccessTools.Method(typeof(PUI_CommunicationMenu), "OnButtonPressedTalk", new[] { typeof(PUI_CommunicationButton), typeof(PlayerAgent) });
                var update = AccessTools.Method(typeof(GameStateManager), "Update");
                var reset = AccessTools.Method(typeof(GameStateManager), "DoChangeState", new[] { typeof(eGameStateName) });
                var collect = AccessTools.Method(typeof(RootPlayerBotAction), "UpdateActionCollectItem");
                var share = AccessTools.Method(typeof(RootPlayerBotAction), "UpdateActionShareResoursePack");
                if (press == null || update == null || reset == null || collect == null || share == null) throw new MissingMethodException("缺少原版菜单或库存调度接口");
                patches = new Harmony(Plugin.Guid + ".items");
                ItemWorldIndex.Install(patches);
                KnownSupplyIndex.Install(patches);
                ItemActionAuthorization.Install(patches);
                NativePickupAdapter.Install(patches);
                SingleKillAdapter.Install(patches);
                OpenTaskAdapter.Install(patches);
                ControlCommandAdapter.Install(patches);
                ResourceUseAdapter.Install(patches);
                MovementOrders.Install(patches);
                RescueOrders.Install(patches);
                ToolSupplyAdapter.Install(patches); SharedMeleeBridge.Install(patches); FocusOrders.Install(patches); GuardCombat.Install(patches);
                CaptainMenuInput.Install();
                patches.Patch(press, prefix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(Press)));
                patches.Patch(update, postfix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(Update)));
                patches.Patch(reset, prefix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(BeforeState)));
                patches.Patch(collect, prefix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(AllowAutoInventory)));
                patches.Patch(share, prefix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(AllowAutoInventory)));
                patches.Patch(collect, prefix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(AllowOriginalCollection)));
                patches.Patch(AccessTools.Method(typeof(RootPlayerBotAction), "UpdateActionHighlight"), postfix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(FilterHighlight)));
                patches.Patch(AccessTools.Method(typeof(RootPlayerBotAction), "UpdateActionUnlock"), postfix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(FilterUnlock)));
                patches.Patch(AccessTools.Method(typeof(PUI_CommunicationMenu), "RequestBotAction"), prefix: new HarmonyMethod(typeof(ItemTaskAdapter), nameof(BeforeNativeCommand)) { priority = Priority.First });
                installed = true; Plugin.Logger.LogInfo("队长菜单 0.5.33 已接入；指定击杀采用可挥击背后站位及本次命中限定的弱点规则；低优先级职责可让位扫描／救援，明确交互先完成核验；原版工具父页、特殊搬运与输入边界保留。");
            }
            catch (Exception error) { patches?.UnpatchSelf(); CaptainMenuInput.Uninstall(); installed = false; Plugin.Logger.LogError("机器人任务注册失败，撤销本组补丁：" + error); }
        }
        public static void Feedback(string message)
        {
            Plugin.Logger.LogInfo("[队长任务] " + message);
            // Local event log only. Do not broadcast new commands or task reports to unmodified peers.
            GuiManager.PlayerLayer?.m_gameEventLog?.AddLogItem("[队长] " + message, eGameEventChatLogType.GameEvent);
        }
        static bool AllowAutoInventory(RootPlayerBotAction __instance)
            => !SNet.IsMaster || __instance?.m_bot == null || (!RescueOrders.Owns(__instance.m_bot) && !SingleKillAdapter.Owns(__instance.m_bot) && !OpenTaskAdapter.Owns(__instance.m_bot)
                && !MovementOrders.BlocksAuto(__instance.m_bot) && !ToolSupplyAdapter.Owns(__instance.m_bot) && !SharedMeleeBridge.Owns(__instance.m_bot) && !ResourceUseAdapter.Owns(__instance.m_bot) && !NativePickupAdapter.Owns(__instance.m_bot) && !CollectTaskAdapter.Owns(__instance.m_bot) && !SearchTaskAdapter.Owns(__instance.m_bot) && (!tasks.TryGetValue(__instance.m_bot.Pointer, out var task) || !task.OwnsControl && (!task.ManualPaused || task.Finished)));
        static bool AllowOriginalCollection() => !SNet.IsMaster || !AutoPickupAdapter.Restrict;
        static void FilterHighlight(RootPlayerBotAction __instance, ref PlayerBotActionBase.Descriptor __0)
        { if (SNet.IsMaster && __0 != null && __instance?.m_highlightAction != null && (AutoPickupAdapter.Restrict || SearchTaskAdapter.Owns(__instance.m_bot)) && __0.Pointer == __instance.m_highlightAction.Pointer && __instance.m_highlightAction.TargetType == PlayerBotActionHighlight.Descriptor.TargetTypeEnum.Container) __0 = null; }
        static void FilterUnlock(RootPlayerBotAction __instance, ref PlayerBotActionBase.Descriptor __0)
        { if (SNet.IsMaster && __0 != null && __instance?.m_unlockAction != null && (AutoPickupAdapter.Restrict || SearchTaskAdapter.Owns(__instance.m_bot)) && __0.Pointer == __instance.m_unlockAction.Pointer && __instance.m_unlockAction.TargetType == PlayerBotActionUnlock.Descriptor.TargetTypeEnum.Container) __0 = null; }
        static CommunicationNode Node(uint id, string text, bool leaf = false)
        {
            labels[id] = text;
            return new CommunicationNode { TextId = id, DialogID = 0, Script = CommunicationNode.ScriptType.None,
                IsLastNode = leaf, m_ChildNodes = new Il2CppSystem.Collections.Generic.List<CommunicationNode>() };
        }
        internal static bool PrepareMenu(PUI_CommunicationMenu menu)
        {
            if (!installed || !AutoPickupAdapter.CanAnswer || menu?.m_menu?.m_controller == null) return false;
            try
            {
                ReleaseMenu();
                if (BotControlService.Instance.TryCurrent(out var controlTicket, out var controlBot)) return PrepareControlMenu(menu, controlTicket, controlBot);
                MenuTarget = ItemTargetResolver.Target(PlayerManager.GetLocalPlayerAgent());
                var root = menu?.m_menu?.m_controller?.m_rootNode;
                if (root?.m_ChildNodes == null || menu.m_buttons == null) return false;
                int capacity = Math.Min(9, menu.m_buttons.Count);
                var source = PlayerManager.GetLocalPlayerAgent();
                KnownSupplyIndex.Observe(MenuTarget, SupplySource.Aimed);
                RaycastHit? aim = Ray(source, out var capturedHit) ? capturedHit : null;
                Pose? placement = Ground(out var capturedPose, false) ? capturedPose : null;
                menuContext = new() { SearchOrigin = source?.CourseNode, Item = MenuTarget, Supplies = KnownSupplyIndex.Capture(source), Terminals = KnownTerminalIndex.Capture(source), Containers = KnownContainerIndex.Capture(source), Interactions = KnownInteractionIndex.Capture(source), Board = CaptureBoard(), Enemy = SingleKillAdapter.Target(source), OpenTarget = OpenTargetMarks.Current, Position = PositionMarks.Current, CombatEnemy = FocusOrders.Target(source), Tools = ToolSupplyAdapter.Capture(), Melee = SharedMeleeBridge.Capture(), Hit = aim, Placement = placement,
                    Floor = Builder.CurrentFloor?.Pointer ?? IntPtr.Zero, Issuer = source?.Owner?.Pointer ?? IntPtr.Zero };
                labels.Clear(); menuCommands.Clear();
                var choices = new List<CaptainMenuBot>();
                var receivers = new List<CaptainMenuReceiver>();
                for (int slot = 0; slot < 4; slot++)
                {
                    var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                    if (agent?.Owner != null) receivers.Add(new(slot, agent.Owner.Pointer.ToInt64(), agent.PlayerName, agent.Alive, agent.Owner.IsBot, RescueOrders.Downed(agent)));
                    if (agent?.Owner?.IsBot != true) continue;
                    var bot = agent.GetComponent<PlayerAIBot>();
                    var pack = ResourceUseAdapter.MenuPack(bot, out var packIdentity);
                    string killReason = SingleKillAdapter.Diagnose(bot, menuContext.Enemy, out var killNote);
                    string state = !agent.Alive ? "倒地" : ToolSupplyAdapter.Status(bot) ?? FocusOrders.Status(bot) ?? RescueOrders.Status(bot) ?? NativePickupAdapter.Status(bot) ?? SearchTaskAdapter.Status(bot) ?? MovementOrders.ControlStationStatus(bot) ?? CollectTaskAdapter.Status(bot) ?? (bot != null && tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished ? StageText(task.Stage) : SingleKillAdapter.Status(bot) ?? OpenTaskAdapter.Status(bot) ?? ResourceUseAdapter.Status(bot) ?? MovementOrders.Status(bot));
                    choices.Add(new(slot, agent.Owner.Pointer.ToInt64(), agent.PlayerName + (state == null ? "" : "（" + state + "）"), agent.Alive && bot != null,
                        OpenTaskAdapter.HasMelee(bot), OpenTaskAdapter.Melters(bot), ResourceUseAdapter.ModeText(agent), pack != null, pack, packIdentity, ToolSupplyAdapter.ToolPack(bot), killReason, killNote));
                }
                menuContext.Bots = choices;
                string itemName = MenuTarget == null ? "未识别" : (Plugin.Catalog?.TranslateItems(MenuTarget.PublicName) ?? MenuTarget.PublicName) + " #" + MenuTarget.ItemDataBlock?.persistentID;
                bool supported = MenuTarget?.ItemDataBlock != null && (ItemTaskPolicy.SupportedSlot((int)MenuTarget.Get_pItemData().slot)
                    || SpecialItemPolicy.Special((int)MenuTarget.Get_pItemData().slot) && SpecialItemAccess.Reason(MenuTarget) == null);
                var openReason = OpenTargetMarks.RejectReason(menuContext.OpenTarget);
                OpenTaskDiagnostics.Write("menu-preview", menuContext.OpenTarget, "reject=" + (openReason ?? "none"));
                string containerName = "未识别容器";
                bool validContainer = false;
                if (aim.HasValue) {
                    try { validContainer = ContainerBridge.Find(aim.Value, InventorySlot.ResourcePack, out _)
                        || ContainerBridge.Find(aim.Value, InventorySlot.Consumable, out _); }
                    catch (Exception error) { Plugin.Logger.LogWarning("菜单容器预览暂不可用：" + error.Message); }
                }
                if (validContainer) containerName = "准星容器 @ " + PositionText(aim.Value.point);
                menuContext.Guards = GuardTargets.Capture(source, menuContext.Position);
                var plan = CaptainMenuPlan.Build(choices, itemName, containerName,
                    placement.HasValue ? PositionText(placement.Value.position) + "（沿开菜单时朝向）" : "未识别地面",
                    supported, validContainer, placement.HasValue,
                    menuContext.Enemy == null ? "未识别敌人" : "敌人 #" + menuContext.Enemy.EnemyDataID,
                    SingleKillAdapter.Sleeping(menuContext.Enemy), new CaptainMenuOpenTarget(
                        menuContext.OpenTarget?.Label + (openReason == null ? "（" + menuContext.OpenTarget.LockText + "）" : "（不可接单）"),
                        openReason, openReason == null && menuContext.OpenTarget.NeedsMelee, openReason == null ? menuContext.OpenTarget.NeededMelters : 0), menuContext.Supplies.Scopes, menuContext.Board, receivers, menuContext.Terminals.Results, menuContext.Containers.Results, menuContext.Interactions.Results,
                    new(menuContext.Position?.Label ?? "未标记位置", PositionMarks.Reason(menuContext.Position)),
                    menuContext.Tools.ConvertAll(t=>new CaptainMenuToolTarget(t.Id,t.Label,t.Blocked)),
                    new(menuContext.CombatEnemy==null?"未识别敌人":"敌人 #"+menuContext.CombatEnemy.EnemyDataID,
                        !FocusOrders.Available?"集火接口未就绪":FocusOrders.Attackable(menuContext.CombatEnemy)?null:"请对准已激怒敌人；沉睡敌人使用近战命令",
                        !SharedMeleeBridge.Available?"协同接口未就绪":SingleKillAdapter.Sleeping(menuContext.CombatEnemy)?null:"请对准沉睡目标"),
                    new(menuContext.Melee.Label,menuContext.Melee.Ready,menuContext.Melee.Session!=null,menuContext.Melee.Blocked), menuContext.Guards.Menu);
                var nodes = new Dictionary<uint, CommunicationNode>();
                var links = new List<CaptainMenuPlanNode>();
                CommunicationNode Materialize(CaptainMenuPlanNode value) {
                    var node = Node(value.Id, value.Label, value.Leaf != null); nodes.Add(value.Id, node);
                    if (value.Leaf != null) menuCommands.Add(value.Id, value.Leaf);
                    if (value.BackTo != 0) links.Add(value);
                    foreach (var child in value.Children) node.m_ChildNodes.Add(Materialize(child));
                    return node;
                }
                var branch = Materialize(plan);
                foreach (var link in links) CaptainMenuTree.Link(link.Id, nodes[link.BackTo]);
                var nativeTools = CaptainMenuTree.FindNativeTools(root);
                if (nativeTools != null) CaptainMenuTree.Link(plan.Children[2].Id, nativeTools);
                else {
                    var unavailable = Node(CaptainMenuTree.NodeEnd - 1, "原版部署入口未找到，请从原版通讯进入", true);
                    nodes[plan.Children[2].Id].m_ChildNodes.Add(unavailable);
                    menuCommands[unavailable.TextId] = new("D_NATIVE", "部署工具", -1, 0, 0, "未找到包含原版UseTool模板的工具页面，请从原版通讯进入");
                }
                var original = Node(CaptainMenuTree.OriginalId, "原版通讯／角色语音");
                original.m_ChildNodes = root.m_ChildNodes;
                branch.m_ChildNodes.Add(original);
                uint pageId = CaptainMenuTree.PageStart;
                CaptainMenuTree.Paginate(branch, capacity, (id, text) => Node(id, text), ref pageId);
                if (!RegisterTexts()) { ReleaseMenu(); return false; }
                savedRoot = root; viewRoot = branch; viewMenu = menu; cachedMenu = menu;
                menu.m_menu.m_controller.m_rootNode = branch;
                Plugin.Logger.LogInfo("[队长菜单] 五组任务框架建立，按钮容量=" + capacity + "，原版工具页=" + nativeTools?.TextId
                    + "，工具页子节点=" + nativeTools?.m_ChildNodes?.Count + "；原版与 Chatter 节点顺序保持原样。");
                return true;
            }
            catch (Exception error) { ReleaseMenu(); Plugin.Logger.LogWarning("队长菜单暂未建立，保留原版：" + error.Message); return false; }
        }
        internal static void ReleaseMenu(PUI_CommunicationMenu expected = null)
        {
            if (expected != null && viewMenu?.Pointer != expected.Pointer) return;
            var menu = viewMenu; var root = savedRoot; var owned = viewRoot;
            viewMenu = null; savedRoot = null; viewRoot = null; MenuTarget = null; menuContext = null; menuCommands.Clear(); CaptainMenuTree.Reset();
            if (menu?.m_menu?.m_controller != null && root != null
                && menu.m_menu.m_controller.m_rootNode?.Pointer == owned?.Pointer) menu.m_menu.m_controller.m_rootNode = root;
        }
        static bool PrepareControlMenu(PUI_CommunicationMenu menu, long ticket, PlayerAIBot bot)
        {
            var root = menu.m_menu.m_controller.m_rootNode;
            if (root?.m_ChildNodes == null || menu.m_buttons == null) return false;
            var context = ControlCommandAdapter.MenuSnapshot(ticket, bot);
            MenuTarget = context.Item;
            menuContext = new() { Control = context };
            labels.Clear(); menuCommands.Clear();
            string fresh = ControlCommandAdapter.MarkReason(context);
            string openReason = fresh ?? OpenTargetMarks.RejectReason(context.Open, bot.Agent);
            string name = context.Item == null ? "请先从机器人镜头中键标记" : Plugin.Catalog?.TranslateItems(context.Item.PublicName) ?? context.Item.PublicName;
            string enemyReason = ControlCommandAdapter.EnemyReason(context), killNote = null;
            string killReason = enemyReason ?? SingleKillAdapter.ControlReason(bot, context.Enemy, out killNote);
            string focusReason = enemyReason ?? FocusOrders.ControlReason(bot, context.Enemy);
            string stationReason = context.Station == null ? "机器人当前位置没有可用驻守站位，请先微调后重开Q" : PositionMarks.Reason(context.Station, bot.Agent);
            var choices = new List<CaptainMenuBot> { new(bot.Agent.PlayerSlotIndex, bot.Agent.Owner.Pointer.ToInt64(),
                "当前接管：" + bot.Agent.PlayerName, bot.Agent.Alive, OpenTaskAdapter.HasMelee(bot), OpenTaskAdapter.Melters(bot), KillBlocked: killReason, KillNote: killNote) };
            var plan = CaptainMenuPlan.Build(choices, name, "", "", fresh == null && context.Item != null, false, false,
                enemy: context.Enemy == null ? "未标记敌人" : "敌人 #" + context.Enemy.EnemyDataID, enemyValid: SingleKillAdapter.Sleeping(context.Enemy),
                openTarget: new(context.Open?.Label ?? "未标记", openReason, openReason == null && context.Open.NeedsMelee,
                    openReason == null ? context.Open.NeededMelters : 0), position: new("当前机器人所在位置", stationReason),
                combat: new("机器人镜头标记的敌人", focusReason, "接管协同近战尚未接入"), controlMode: true);
            string busy = ControlCommandAdapter.Busy(ticket, out var running) ? "正在执行／核验：" + running : null;
            var nodes = new Dictionary<uint, CommunicationNode>(); var links = new List<CaptainMenuPlanNode>();
            CommunicationNode Materialize(CaptainMenuPlanNode value)
            {
                var node = Node(value.Id, value.Label, value.Leaf != null); nodes.Add(value.Id, node);
                if (value.Leaf != null) menuCommands.Add(value.Id, busy == null ? value.Leaf : value.Leaf with { Blocked = busy });
                if (value.BackTo != 0) links.Add(value);
                foreach (var child in value.Children) node.m_ChildNodes.Add(Materialize(child));
                return node;
            }
            var branch = Materialize(plan);
            foreach (var link in links) CaptainMenuTree.Link(link.Id, nodes[link.BackTo]);
            uint pageId = CaptainMenuTree.PageStart;
            CaptainMenuTree.Paginate(branch, Math.Min(9, menu.m_buttons.Count), (id, text) => Node(id, text), ref pageId);
            if (!RegisterTexts()) { ReleaseMenu(); return false; }
            savedRoot = root; viewRoot = branch; viewMenu = cachedMenu = menu;
            menu.m_menu.m_controller.m_rootNode = branch; return true;
        }
        internal static Action CaptureDispatch(uint id)
        {
            if (!menuCommands.TryGetValue(id, out var entry)) return null;
            var context = menuContext;
            return () => {
                try {
                    if (context?.Control != null) { ControlCommandAdapter.Issue(context.Control, entry); return; }
                    if (BotControlService.HasActive) { Feedback("接管期间暂不执行菜单指令，请先退出接管"); return; }
                    if (!SNet.IsMaster || GameStateManager.CurrentStateName != eGameStateName.InLevel
                        || context == null || context.Floor == IntPtr.Zero || Builder.CurrentFloor?.Pointer != context.Floor
                        || PlayerManager.GetLocalPlayerAgent()?.Owner?.Pointer != context.Issuer) { Feedback("菜单会话已失效，请重新打开"); return; }
                    if (entry.Blocked != null) {
                        OpenTaskDiagnostics.Write("menu-rejected", context.OpenTarget, "command=" + entry.Title + " reason=" + entry.Blocked);
                        Feedback(entry.Blocked); return;
                    }
                    if (ControlCommandAdapter.HasState && entry.Handler is not (8 or 13 or 21 or 25 or 29)) {
                        var commandAgent = entry.Slot >= 0 ? PlayerManager.Current?.GetPlayerAgentInSlot(entry.Slot) : null;
                        if (entry.Slot < 0 || commandAgent?.Owner != null && ControlCommandAdapter.Owns(commandAgent.Pointer.ToInt64(), commandAgent.Owner.Pointer.ToInt64())) {
                            Feedback("接管指令仍在收尾，暂不能改派该机器人／提交全队任务；其他机器人可单独下达指令"); return;
                        }
                    }
                    if (entry.Handler == 0) { Feedback(entry.Title + "：菜单框架已建立，功能尚未接入；未下达机器人任务"); return; }
                    if (entry.Handler == 31) {
                        RouteNavigation.Start(RouteTargets.Capture(entry, context.Supplies, context.Terminals, context.Containers, context.Interactions)); return;
                    }
                    if (entry.Handler is 32 or 33 or 34) { RouteNavigation.Control(entry.Handler); return; }
                    if (entry.Handler == 13) {
                        foreach (var row in context.Board.Rows) if (row.Id == entry.RecordId) {
                            Feedback(row.Detail(context.Board.At) + "（开菜单时快照，重开刷新）"); return;
                        }
                        Feedback("任务记录不存在，请重开看板"); return;
                    }
                    if (entry.Handler == 11) {
                        if (context.Supplies?.Details.TryGetValue(entry.SupplyId, out var detail) == true) {
                            if (!context.Supplies.Targets.TryGetValue(entry.SupplyId, out var supply) || !KnownSupplyIndex.Current(entry.SupplyId, supply) || !ItemRegion.TryResolve(supply, out _)) Feedback("该物资已被拿走或失效，请重开列表");
                            else Feedback(detail + "（开菜单时快照）");
                        } else Feedback("物资结果已经失效，请重开列表");
                        return;
                    }
                    if (entry.Handler is 21 or 22) {
                        if (context.Terminals?.Targets.TryGetValue(entry.TerminalId, out var saved) != true || !KnownTerminalIndex.Current(entry.TerminalId, saved)) {
                            Feedback("终端已失效或被替换，请重开结果列表"); return;
                        }
                        if (entry.Handler == 21) { Feedback(context.Terminals.Details[entry.TerminalId] + "（开菜单时快照，重开刷新）"); return; }
                        var source = PlayerManager.GetLocalPlayerAgent();
                        if (!KnownTerminalIndex.Resolve(saved.Value, out var node) || !SearchTraversal.InScope(node, source?.CourseNode, 1)
                            || !ItemRegion.SameSpace(source, node) || !SearchTraversal.Reachable(source, source.CourseNode, source.CourseNode, 1, out _).Contains(node.Pointer.ToInt64())) {
                            Feedback("终端已不在当前Zone的普通门可达范围，不能重新标记"); return;
                        }
                        Feedback(KnownTerminalIndex.Mark(saved.Value) ? "已重新标记 " + KnownTerminalIndex.Name(saved.Value) : "结果仍保留，临时标记暂不可用"); return;
                    }
                    if (entry.Handler is 25 or 26 or 27) {
                        if (context.Containers?.Targets.TryGetValue(entry.ContainerId, out var saved) != true || !KnownContainerIndex.Current(entry.ContainerId, saved)) {
                            Feedback("容器已失效或被替换，请重开结果列表"); return;
                        }
                        if (entry.Handler == 25) { Feedback(context.Containers.Details[entry.ContainerId] + "（开菜单时快照，重开刷新）"); return; }
                        var source = PlayerManager.GetLocalPlayerAgent();
                        var target = KnownContainerIndex.OpenTarget(source, saved);
                        if (target == null) { Feedback("容器当前不在本Zone的普通门可达范围，不能标记或下达开启指令"); return; }
                        if (entry.Handler == 26) { Feedback(KnownContainerIndex.Mark(saved.Value) ? "已重新标记 " + KnownContainerIndex.Name(saved.Value) : "结果仍保留，临时标记暂不可用"); return; }
                        if (target.IsOpen) { Feedback("该容器已经开启，无需重复操作"); return; }
                        if (target.Restriction != null) { Feedback(target.Restriction); return; }
                        var opener = PlayerManager.Current?.GetPlayerAgentInSlot(entry.Slot);
                        if (opener?.Owner?.IsBot != true || opener.Owner.Pointer.ToInt64() != entry.Owner) { Feedback("机器人已离队或被替换，请重新选择"); return; }
                        var bot = opener.GetComponent<PlayerAIBot>();
                        // Keep all live capability checks and target reservations in the existing opener.
                        PreemptAutomatic(bot);
                        OpenTaskAdapter.Issue(bot, target); return;
                    }
                    if (entry.Slot == -2 && (entry.Handler == 14 || entry.Handler == 15)) {
                        foreach (var saved in context.Bots) {
                            var current = PlayerManager.Current?.GetPlayerAgentInSlot(saved.Slot);
                            if (current?.Owner?.IsBot != true || current.Owner.Pointer.ToInt64() != saved.Owner) continue;
                            if (entry.Handler == 14) ResourceUseAdapter.SetMode(current, entry.ResourceMode); else ResourceUseAdapter.Report(current);
                        }
                        return;
                    }
                    if (entry.Handler is 29 or 30) {
                        if (context.Interactions?.Targets.TryGetValue(entry.InteractionId, out var saved) != true || !KnownInteractionIndex.Current(entry.InteractionId, saved)) {
                            Feedback("交互点已失效或被替换，请重开结果列表"); return;
                        }
                        if (entry.Handler == 29) { Feedback(context.Interactions.Details[entry.InteractionId] + "（开菜单时快照，重开刷新）"); return; }
                        if (!KnownInteractionIndex.Accessible(PlayerManager.GetLocalPlayerAgent(), saved)) {
                            Feedback("交互点当前不在本Zone的普通门可达范围，不能重新标记"); return;
                        }
                        Feedback(KnownInteractionIndex.Mark(saved.Candidate) ? "已重新标记交互点，仅定位，不执行任务交互" : "结果仍保留，临时标记暂不可用"); return;
                    }
                    if (entry.Handler is 18 or 23 or 24 or 28) {
                        SearchTaskAdapter.Issue(PlayerManager.GetLocalPlayerAgent(), context.SearchOrigin, entry.SearchScope,
                            entry.Handler is 18 or 24 or 28 ? entry.SearchFilter : 0, entry.Handler == 23 ? SearchTargetKind.Terminals : entry.Handler == 24 ? SearchTargetKind.Containers : entry.Handler == 28 ? SearchTargetKind.Interactions : SearchTargetKind.Supplies);
                        return;
                    }
                    if (entry.Handler == 46) { FocusOrders.RestoreAll(); return; }
                    if (entry.Handler == 49) { SharedMeleeBridge.Execute(context.Melee.Session); return; }
                    if (entry.Handler == 50) { SharedMeleeBridge.Cancel(context.Melee.Session); return; }
                    if (entry.Handler == 51) { SharedMeleeBridge.Report(); return; }
                    if (entry.Handler is 44 or 48) {
                        if(entry.Handler==48) SharedMeleeBridge.Prepare(context.CombatEnemy,context.Bots);
                        else foreach(var chosen in context.Bots) {
                            var member=PlayerManager.Current?.GetPlayerAgentInSlot(chosen.Slot);
                            if(member?.Owner?.IsBot==true&&member.Alive&&member.Owner.Pointer.ToInt64()==chosen.Owner)FocusOrders.Issue(member.GetComponent<PlayerAIBot>(),context.CombatEnemy);
                        }
                        return;
                    }
                    if (entry.Handler == 40) { RescueOrders.AssignAll(); return; }
                    if (entry.Handler == 41) { RescueOrders.CancelBatch(); return; }
                    var agent = PlayerManager.Current?.GetPlayerAgentInSlot(entry.Slot);
                    if (agent?.Owner?.IsBot != true || agent.Owner.Pointer.ToInt64() != entry.Owner) { Feedback("机器人已离队或被替换，请重新选择"); return; }
                    if(entry.Handler is >= 52 and <= 54){GuardTargets.Issue(agent.GetComponent<PlayerAIBot>(),context.Guards,entry.Handler,entry.GuardPoint,entry.GuardRadius,entry.ReceiverSlot,entry.ReceiverOwner);return;}
                    if(entry.Handler==42){var tool=context.Tools.Find(t=>t.Id==entry.ToolTargetId);ToolSupplyAdapter.Issue(agent.GetComponent<PlayerAIBot>(),tool,entry.SupplyItem);return;}
                    if(entry.Handler==43){FocusOrders.Issue(agent.GetComponent<PlayerAIBot>(),context.CombatEnemy);return;}
                    if(entry.Handler==45){FocusOrders.Restore(agent.GetComponent<PlayerAIBot>());return;}
                    if(entry.Handler==47){SharedMeleeBridge.Prepare(context.CombatEnemy,new[]{new CaptainMenuBot(entry.Slot,entry.Owner,agent.PlayerName,agent.Alive)});return;}
                    if (entry.Handler == 35) {
                        var receiver = PlayerManager.Current?.GetPlayerAgentInSlot(entry.ReceiverSlot);
                        if (receiver?.Owner?.Pointer.ToInt64() != entry.ReceiverOwner || receiver.Owner.IsBot) { Feedback("跟随玩家已离队或被替换，请重新选择"); return; }
                        MovementOrders.FollowPlayer(agent.GetComponent<PlayerAIBot>(), receiver); return;
                    }
                    if (entry.Handler == 36) { MovementOrders.GoTo(agent.GetComponent<PlayerAIBot>(), context.Position); return; }
                    if (entry.Handler == 37) { MovementOrders.StationAt(agent.GetComponent<PlayerAIBot>(), context.Position); return; }
                    if (entry.Handler == 38) { ControlTask(agent.GetComponent<PlayerAIBot>(), entry.SearchPause); return; }
                    if (entry.Handler == 39) {
                        var receiver = PlayerManager.Current?.GetPlayerAgentInSlot(entry.ReceiverSlot);
                        if (receiver?.Owner?.Pointer.ToInt64() != entry.ReceiverOwner) { Feedback("救援队友已离队或被替换，请重新选择"); return; }
                        RescueOrders.Issue(agent.GetComponent<PlayerAIBot>(), receiver); return;
                    }
                    if (entry.Handler == 8) { ReportTask(agent); return; }
                    if (entry.Handler == 14) { ResourceUseAdapter.SetMode(agent, entry.ResourceMode); return; }
                    if (entry.Handler == 15) { ResourceUseAdapter.Report(agent); return; }
                    if (entry.Handler == 20) { SearchTaskAdapter.Control(agent.GetComponent<PlayerAIBot>(), entry.SearchPause); return; }
                    if (SearchTaskAdapter.Owns(agent.GetComponent<PlayerAIBot>())) {
                        if (entry.Handler == 4 || entry.Handler == 5) SearchTaskAdapter.Cancel(agent.GetComponent<PlayerAIBot>(), entry.Handler == 5);
                        else Feedback("该机器人正在搜索，请先暂停查看或取消后下达其他任务");
                        return;
                    }
                    if (entry.Handler == 16) {
                        MovementOrders.YieldDuty(agent.GetComponent<PlayerAIBot>());
                        var receiver = PlayerManager.Current?.GetPlayerAgentInSlot(entry.ReceiverSlot);
                        if (receiver?.Owner?.Pointer.ToInt64() != entry.ReceiverOwner) { Feedback("补给对象已被替换，请重开菜单"); return; }
                        ResourceUseAdapter.GiveOnce(agent.GetComponent<PlayerAIBot>(), receiver, entry.SupplyItem); return;
                    }
                    if (ResourceUseAdapter.Owns(agent.GetComponent<PlayerAIBot>())) {
                        if (entry.Handler == 4 || entry.Handler == 5) ResourceUseAdapter.Cancel(agent.GetComponent<PlayerAIBot>(), entry.Handler == 5);
                        else Feedback("该机器人正在补给，请先取消或等待完成");
                        return;
                    }
                    if (entry.Handler == 12) {
                        if (context.Supplies?.Targets.TryGetValue(entry.SupplyId, out var supply) != true || !KnownSupplyIndex.Current(entry.SupplyId, supply)) { Feedback("物资结果已经失效，请重开列表"); return; }
                        string reason = KnownSupplyIndex.PickupReason(PlayerManager.GetLocalPlayerAgent(), supply);
                        if (reason != null) { Feedback(reason); return; }
                        Issue(entry.Slot, 1, null, null, supply, true); return;
                    }
                    if (entry.Handler == 17) { var bot = agent.GetComponent<PlayerAIBot>(); PreemptAutomatic(bot); CollectTaskAdapter.Issue(bot, context.Supplies, entry); return; }
                    if (entry.Handler == 9) {
                        if (!SingleKillAdapter.Sleeping(context.Enemy)) { Feedback("原菜单目标已惊醒或失效，请重新选择"); return; }
                        var bot = agent.GetComponent<PlayerAIBot>(); PreemptAutomatic(bot);
                        SingleKillAdapter.Issue(bot, context.Enemy); return;
                    }
                    if (entry.Handler == 10) {
                        string reason = OpenTargetMarks.RejectReason(context.OpenTarget);
                        if (reason != null) { OpenTaskDiagnostics.Write("reject-before-accept", context.OpenTarget, "reason=" + reason); Feedback(reason); return; }
                        var bot = agent.GetComponent<PlayerAIBot>(); PreemptAutomatic(bot);
                        OpenTaskAdapter.Issue(bot, context.OpenTarget); return;
                    }
                    Issue(entry.Slot, entry.Handler, context.Hit, context.Placement, context.Item, true);
                } catch (Exception error) {
                    if (entry.Handler is 10 or 27) Plugin.Logger.LogError("[开门诊断] command-exception " + error);
                    if (entry.Handler is 24 or 25 or 26) Plugin.Logger.LogWarning("容器搜寻／结果指令异常：" + error);
                    if (entry.Handler is 28 or 29 or 30) Plugin.Logger.LogWarning("交互点搜寻／只读结果指令异常：" + error);
                    if (entry.Handler is >= 52 and <= 54) Plugin.Logger.LogWarning("[固定守卫] 指令异常：" + error);
                    if (entry.Handler is >= 42 and <= 51) Plugin.Logger.LogWarning("[第四批补给／战斗] 指令异常：" + error);
                    if (entry.Handler is >= 31 and <= 34) Plugin.Logger.LogWarning("独立导航指令异常：" + error);
                    if (entry.Handler is >= 35 and <= 38) Plugin.Logger.LogWarning("[行动指令] 接单异常：" + error);
                    Feedback("指令失败：" + error.Message);
                }
            };
        }
        static string PositionText(Vector3 position) => $"({position.x:F1}, {position.y:F1}, {position.z:F1})";
        static void ControlTask(PlayerAIBot bot, bool pause)
        {
            if (SharedMeleeBridge.Owns(bot)) { Feedback("协同近战不支持暂停；请使用协同菜单取消"); return; }
            if (ToolSupplyAdapter.Owns(bot)) { Feedback("工具补给不支持暂停；请等待收尾或取消"); return; }
            if (RescueOrders.Owns(bot)) { Feedback("救援是有界紧急任务，不支持暂停；可取消接近，已经开始的救援交互先收尾"); return; }
            bool done = CollectTaskAdapter.Control(bot, pause) || OpenTaskAdapter.Control(bot, pause);
            if (!done && tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished) { task.Pause(pause); task.Tick(Time.time); done = true; }
            if (!done && (NativePickupAdapter.Owns(bot) || ResourceUseAdapter.Owns(bot) || SingleKillAdapter.Owns(bot))) {
                Feedback("当前为原版特殊搬运／单次补给／指定击杀，无法安全保存并复用原生动作；请等待收尾或使用取消，不会伪报已暂停"); return;
            }
            if (!done) done = MovementOrders.Control(bot, pause);
            Feedback(done ? (pause ? "暂停已登记：原子交互先核验，保留目标，交还移动权限" : "继续已登记：重新核验身份、目标、路径和控制权限") : "没有可以暂停／继续的持续或前台任务；即时查询和原版部署不属于可恢复任务");
        }
        static string StageText(ItemTaskStage stage) => stage switch {
            ItemTaskStage.Waiting => "等待权限", ItemTaskStage.Moving => "接近目标", ItemTaskStage.Paused => "任务暂停",
            ItemTaskStage.Interacting => "交互中", ItemTaskStage.Verifying => "核验物品", ItemTaskStage.Returning => "返回中",
            ItemTaskStage.Complete => "已完成", ItemTaskStage.Cancelled => "已取消", _ => "失败" };
        static void ReportTask(PlayerAgent agent)
        {
            var bot = agent.GetComponent<PlayerAIBot>();
            string report = ToolSupplyAdapter.Status(bot) ?? FocusOrders.Status(bot) ?? RescueOrders.Status(bot) ?? NativePickupAdapter.Status(bot) ?? SearchTaskAdapter.Status(bot) ?? MovementOrders.ControlStationStatus(bot) ?? CollectTaskAdapter.Status(bot) ?? SingleKillAdapter.Status(bot) ?? OpenTaskAdapter.Status(bot) ?? (bot != null && tasks.TryGetValue(bot.Pointer, out var task)
                ? (task.Kind == ItemTaskKind.Pickup ? "拿取" : task.Kind == ItemTaskKind.PutBack ? "放回" : "返回") + "：" + StageText(task.Stage)
                : "无队长任务（不代表原版机器人空闲）");
            if (pending.ContainsKey(agent.PlayerSlotIndex)) report += "；有一条明确命令等待物品同步";
            Feedback(agent.PlayerName + "：" + report);
        }
        static TaskBoardSnapshot CaptureBoard()
        {
            var board = TaskBoard.Journal.Capture(Time.time);
            foreach (var pair in pending) {
                try {
                var request = pair.Value;
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(pair.Key);
                if (agent?.Owner?.Pointer != request.Owner || !agent.Owner.IsBot) continue;
                string title = request.Command == 1 ? "指定拿取" : request.Command == 2 || request.Command == 3 ? "放回容器" : "部署命令";
                string target = request.Item != null ? Plugin.Catalog?.TranslateItems(request.Item.PublicName) ?? request.Item.PublicName
                    : request.Placement.HasValue ? PositionText(request.Placement.Value.position) : request.Hit.HasValue ? PositionText(request.Hit.Value.point) : "原命令目标";
                board.Rows.Add(new(-1 - pair.Key, pair.Key, request.Owner.ToInt64(), agent.PlayerName, title, target,
                    TaskBoardState.Queued, "等待移交", "等待当前自动物品任务完成同步；仍只有现有的一条等待命令", request.CreatedAt, board.At));
                } catch (Exception error) { Plugin.Logger.LogWarning("任务看板读取等待命令失败，已跳过该条：" + error.Message); }
            }
            // Original deployment actions are inspected only when opening this read-only view.
            for (int slot = 0; slot < 4; slot++) {
                try {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if (agent?.Owner?.IsBot != true) continue;
                var bot = agent.GetComponent<PlayerAIBot>(); if (bot?.Actions == null) continue;
                int index = 0;
                foreach (var action in bot.Actions) {
                    if (++index > 24) break;
                    var desc = action?.DescBase; if (desc == null || desc.IsTerminated()) continue;
                    string title = action.TryCast<PlayerBotActionDeploySentryGun>() != null ? "放置炮台"
                        : action.TryCast<PlayerBotActionDeployTripMine>() != null ? "放置地雷"
                        : action.TryCast<PlayerBotActionGatherDeployables>() != null ? "回收部署物" : null;
                    if (title == null) continue;
                    board.Rows.Add(new(-100 - slot * 24 - index, slot, agent.Owner.Pointer.ToInt64(), agent.PlayerName, title,
                        "由原版工具动作管理", TaskBoardState.Native, desc.Status == PlayerBotActionBase.Descriptor.StatusType.Active ? "执行中" : "已请求",
                        "原版当前动作（状态 " + desc.Status + "）；不把请求接受当作部署成功，不记录本批无法核验的历史结果", board.At, board.At));
                }
                } catch (Exception error) { Plugin.Logger.LogWarning("任务看板读取原版工具动作失败，已跳过该机器人：" + error.Message); }
            }
            return board;
        }
        static TaskBoardState BoardState(ItemTaskStage stage) => stage switch {
            ItemTaskStage.Waiting => TaskBoardState.Waiting, ItemTaskStage.Paused => TaskBoardState.Paused,
            ItemTaskStage.Returning => TaskBoardState.Returning, ItemTaskStage.Complete => TaskBoardState.Complete,
            ItemTaskStage.Failed => TaskBoardState.Failed, ItemTaskStage.Cancelled => TaskBoardState.Cancelled, _ => TaskBoardState.Executing };
        static bool RegisterTexts()
        {
            var service = Text.TextLocalizationService?.TryCast<GameDataTextLocalizationService>();
            if (service?.m_texts == null) return false;
            if (textService != service.Pointer)
            {
                foreach (var pair in labels)
                    if (service.m_texts.ContainsKey(pair.Key) && service.m_texts[pair.Key] != pair.Value)
                    { Plugin.Logger.LogError("队长菜单文本 ID 与其他内容冲突，停止添加入口：" + pair.Key); return false; }
                textService = service.Pointer;
            }
            foreach (var pair in labels) service.m_texts[pair.Key] = pair.Value;
            return true;
        }
        static bool Press(PUI_CommunicationButton __0, ref bool __result)
        {
            if (__0 != null && menuCommands.ContainsKey(__0.TextID)) {
                __result = true; var execute = CaptureDispatch(__0.TextID); Menu()?.SetActive(false); execute?.Invoke(); return false;
            }
            return true;
        }
        static bool Ray(PlayerAgent source, out RaycastHit hit)
        { return ItemTargetResolver.Ray(source, out hit); }
        static void Issue(int playerSlot, int command, RaycastHit? savedHit = null, Pose? savedPlacement = null, ItemInLevel savedItem = null, bool targetCaptured = false)
        {
            if (GameStateManager.CurrentStateName != eGameStateName.InLevel) { Feedback("当前不在关卡中"); return; }
            var agent = PlayerManager.Current?.GetPlayerAgentInSlot(playerSlot);
            var bot = agent?.Owner?.IsBot == true ? agent.GetComponent<PlayerAIBot>() : null;
            if (bot == null || !agent.Alive) { Feedback("此槽位没有可用机器人"); return; }
            if (ToolSupplyAdapter.Owns(bot)) {
                if(command==4||command==5){FocusOrders.RestoreQuiet(bot);ToolSupplyAdapter.Cancel(bot);Feedback("工具补给取消已登记；已经应用的补给先核验，不重复消耗");}else Feedback("机器人正在给指定工具补给");return;
            }
            if (SharedMeleeBridge.Owns(bot)) { Feedback("该机器人正在共用协同近战，请在协同菜单取消后下达其他任务"); return; }
            if (RescueOrders.Owns(bot)) {
                if (command == 4 || command == 5) RescueOrders.Cancel(bot);
                else Feedback("该机器人已有救援任务，请等待或取消救援");
                return;
            }
            bool hadManualFocus = FocusOrders.Status(bot) != null;
            if (command == 4 || command == 5) {
                FocusOrders.RestoreQuiet(bot);
                bool ownedMovement = MovementOrders.Owns(bot);
                bool hadDuty = MovementOrders.HasDuty(bot);
                bool handled = MovementOrders.Cancel(bot, command == 5);
                if (ownedMovement || handled && !(hadDuty && command == 5) && !Busy(bot) && !SingleKillAdapter.Owns(bot) && !ResourceUseAdapter.Owns(bot)) return;
            }
            if (NativePickupAdapter.Owns(bot)) {
                if (command == 4 || command == 5) {
                FocusOrders.RestoreQuiet(bot); pending.Remove(playerSlot); NativePickupAdapter.Cancel(bot, command == 5); }
                else Feedback(agent.PlayerName + "：已有任务／特殊搬运，请先取消或等待实际拾取完成");
                return;
            }
            if (SearchTaskAdapter.Owns(bot)) {
                if (command == 4 || command == 5) {
                FocusOrders.RestoreQuiet(bot); pending.Remove(playerSlot); SearchTaskAdapter.Cancel(bot, command == 5); }
                else Feedback("该机器人正在搜索，请先取消搜索再下达任务");
                return;
            }
            if (CollectTaskAdapter.Owns(bot)) {
                if (command == 4 || command == 5) {
                FocusOrders.RestoreQuiet(bot); pending.Remove(playerSlot); CollectTaskAdapter.Cancel(bot, command == 5); Feedback(agent.PlayerName + "：收集取消已登记，本次已开始的交互先核验；不会继续拿下一件"); }
                else Feedback(agent.PlayerName + "：正在按类型收集，请先取消或等待完成");
                return;
            }
            if (OpenTaskAdapter.Owns(bot)) {
                if (command == 4 || command == 5) {
                FocusOrders.RestoreQuiet(bot); OpenTaskAdapter.Cancel(bot, command == 5); Feedback(agent.PlayerName + "：取消已登记；保护已开始的熔锁／挥击和开启核验"); }
                else Feedback(agent.PlayerName + "：已有开箱／开门任务，请先取消或等待完成");
                return;
            }
            if (SingleKillAdapter.Owns(bot)) {
                if (command == 4 || command == 5) {
                FocusOrders.RestoreQuiet(bot);
                    SingleKillAdapter.Cancel(bot);
                    Feedback(agent.PlayerName + "：取消指定击杀后恢复扫描／跟随；已挥出的攻击先完成本次核验");
                } else Feedback(agent.PlayerName + "：已有指定击杀任务，请先取消或等待完成");
                return;
            }
            AutoPickupAdapter.CancelPrompt(bot);
            PreemptAutomatic(bot);
            if (tasks.TryGetValue(bot.Pointer, out var active) && !active.Finished)
            {
                if (automatic.Contains(bot.Pointer) && command != 4 && command != 5)
                {
                    var request = new PendingCommand { Command = command, Owner = agent.Owner.Pointer, CreatedAt = Time.time };
                    if (command == 7) { if (savedPlacement.HasValue) request.Placement = savedPlacement; else { if (!Ground(out var pose)) return; request.Placement = pose; } }
                    else if (command == 1) { request.Item = targetCaptured ? savedItem : ItemTargetResolver.Target(PlayerManager.GetLocalPlayerAgent()); if (request.Item == null) { Feedback("未识别拾取物品，请对准物品后重新打开队长菜单"); return; } }
                    else if (command <= 3) { if (savedHit.HasValue) request.Hit = savedHit; else { if (!Ray(PlayerManager.GetLocalPlayerAgent(), out var aim)) { Feedback("准星没有指向目标"); return; } request.Hit = aim; } }
                    pending[playerSlot] = request; Feedback(agent.PlayerName + "：已登记，完成当前物品同步后执行明确命令"); return;
                }
                if (command == 4 || command == 5) {
                FocusOrders.RestoreQuiet(bot); active.Cancel(command == 5); Feedback(agent.PlayerName + "：已登记取消，正在交互时会先完成本次交互"); }
                else Feedback(agent.PlayerName + "：已有任务，请先取消或等待完成");
                return;
            }
            if (command == 4) { Feedback(agent.PlayerName + (hadManualFocus ? "：已解除手动集火，恢复自主战斗" : "：没有队长任务需要取消")); return; }
            if (command == 5) { Start(bot, ItemTaskKind.Return, InventorySlot.None, null, null, "返回跟随"); return; }
            if (command == 6) { var menu = Menu(); Feedback(menu != null && menu.ExecuteCmdPackDeployables(agent) ? agent.PlayerName + "：回收命令已提交" : "原版拒绝回收命令，请确认有此机器人部署的物品"); return; }
            if (command == 7)
            {
                var menu = Menu(); Pose pose;
                if (savedPlacement.HasValue) pose = savedPlacement.Value;
                else if (!Ground(out pose)) return;
                bool accepted = menu != null && menu.ExecuteCmdDeploySentryGun(agent, pose);
                Feedback(accepted ? agent.PlayerName + "：放置命令已提交，仍由原版检查装备与位置" : "原版拒绝放置，请检查炮台装备和位置"); return;
            }
            if (command == 1)
            {
                var item = targetCaptured ? savedItem : ItemTargetResolver.Target(PlayerManager.GetLocalPlayerAgent());
                if (item == null || item.ItemDataBlock == null) { Diagnose(bot, item, InventorySlot.None, "指定拿取", "目标识别", "未识别有效物品，请对准物品后重新打开队长菜单；近处可先确认原版拾取提示"); return; }
                var itemSlot = item.Get_pItemData().slot;
                if (SpecialItemPolicy.Special((int)itemSlot)) { NativePickupAdapter.Issue(bot, item, Menu()); return; }
                if (!ItemTaskPolicy.SupportedSlot((int)itemSlot)) { Diagnose(bot, item, itemSlot, "指定拿取", "槽位能力", "当前指定拿取只支持资源包和普通消耗品；任务／特殊手持道具请使用原版搬运指令"); return; }
                Start(bot, ItemTaskKind.Pickup, itemSlot, item, null, "拿取 " + (Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName)); return;
            }
            RaycastHit hit;
            if (savedHit.HasValue) hit = savedHit.Value;
            else if (!Ray(PlayerManager.GetLocalPlayerAgent(), out hit)) { Feedback("请保持准星指向可见的物品或补给柜，再选择指令"); return; }
            if (hit.collider == null) { Feedback("原命令目标已经失效"); return; }
            {
                var inventorySlot = command == 2 ? InventorySlot.ResourcePack : InventorySlot.Consumable;
                if (!ContainerBridge.Find(hit, inventorySlot, out var container)) { Feedback("请指向已打开、拥有空放回位置的补给柜或补给箱；需要 DropItemFixed"); return; }
                Start(bot, ItemTaskKind.PutBack, inventorySlot, null, container, command == 2 ? "放回资源包" : "放回消耗品");
            }
        }
        static bool ChildBusy(PlayerAIBot bot) => bot != null && (NativePickupAdapter.Owns(bot) || OpenTaskAdapter.Owns(bot) || tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished);
        internal static bool Busy(PlayerAIBot bot) => ChildBusy(bot) || CollectTaskAdapter.Owns(bot) || SearchTaskAdapter.Owns(bot) || MovementOrders.Owns(bot) || RescueOrders.Owns(bot) || ToolSupplyAdapter.Owns(bot) || SharedMeleeBridge.Owns(bot);
        internal static void SuspendForBorrow(PlayerAIBot bot, List<BorrowHold> holds)
        {
            MovementOrders.SuspendForBorrow(bot, holds);
            CollectTaskAdapter.SuspendForBorrow(bot, holds);
            OpenTaskAdapter.SuspendForBorrow(bot, holds);
            // A collection child is paused and resumed by its original parent.
            if (!CollectTaskAdapter.Owns(bot) && tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished) {
                bool changed = !task.ManualPaused;
                if (changed) { task.Pause(true); task.Tick(Time.time); }
                int revision = task.PauseRevision;
                bool Current() => tasks.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, task) && !task.Finished;
                holds.Add(new(() => !Current() || task.ManuallySuspended, () => {
                    if (changed && Current() && task.PauseRevision == revision && task.ManualPaused) task.Pause(false);
                }, () => Current() ? task.PauseRevision == revision : !tasks.TryGetValue(bot.Pointer, out var current) || current.Finished));
            }
        }
        internal static void SuspendForRescue(PlayerAIBot bot, List<Action> resume)
        {
            if (ToolSupplyAdapter.Owns(bot)) ToolSupplyAdapter.Cancel(bot);
            var collect = CollectTaskAdapter.SuspendForRescue(bot); if (collect != null) resume.Add(collect);
            var open = OpenTaskAdapter.SuspendForRescue(bot); if (open != null) resume.Add(open);
            if (tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished && !task.ManualPaused) {
                task.Pause(true); task.Tick(Time.time);
                resume.Add(() => { if (tasks.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, task)) task.Pause(false); });
            }
        }
        internal static bool RescueYielded(PlayerAIBot bot) => !ToolSupplyAdapter.Owns(bot) && OpenTaskAdapter.RescueYielded(bot)
            && (!tasks.TryGetValue(bot.Pointer, out var task) || task.Finished || task.ManuallySuspended);
        internal static string BusyReason(PlayerAIBot bot, bool collectionChild = false)
        {
            if (bot?.Agent?.Owner == null) return "机器人或网络身份尚未就绪";
            if (PreLuna.Captain.Control.BotControlApi.Controls(bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64())
                || ControlCommandAdapter.Owns(bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64())) return "机器人处于接管／接管指令收尾，旧任务不能被覆盖";
            if (RescueOrders.Owns(bot)) return "已有指定救援／多人救援任务";
            if (ToolSupplyAdapter.Owns(bot)) return "正在给指定工具补给";
            if (SharedMeleeBridge.Owns(bot)) return "正在共用协同近战，请在协同菜单取消";
            if (MovementOrders.Owns(bot)) return "已有前往标记位置任务";
            if (NativePickupAdapter.Owns(bot)) return "已有任务／特殊搬运任务";
            if (OpenTaskAdapter.Owns(bot)) return "已有开箱／开门任务";
            if (tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished) return "已有物品任务（" + task.Kind + "／" + task.Stage + "）";
            if (!collectionChild && CollectTaskAdapter.Owns(bot)) return "已有按类型收集任务";
            if (!collectionChild && SearchTaskAdapter.Owns(bot)) return "已有搜寻任务";
            if (ResourceUseAdapter.Owns(bot)) return "正在执行资源补给或收尾";
            if (SingleKillAdapter.Owns(bot)) return "正在执行指定击杀";
            return null;
        }
        internal static void Diagnose(PlayerAIBot bot, ItemInLevel target, InventorySlot slot, string label, string phase, string reason, bool feedback = true)
        {
            string name = bot?.Agent?.PlayerName ?? "未指定机器人";
            string item = target == null ? "无拾取目标" : Plugin.Catalog?.TranslateItems(target.PublicName) ?? target.PublicName;
            Plugin.Logger.LogInfo("[拾取诊断] phase=" + phase + " bot=" + name + " botPtr=" + bot?.Pointer
                + " owner=" + bot?.Agent?.Owner?.Pointer + " item=" + target?.Pointer + " itemName=" + item + " slot=" + (int)slot
                + " targetPos=" + target?.transform?.position + " botPos=" + bot?.Agent?.Position + " task=" + label + " reason=" + reason);
            if (feedback) Feedback(name + "：" + label + " — [" + phase + "] " + reason);
        }
        internal static bool Reserved(ItemInLevel item) => item != null && (items.Contains(item.Pointer) || NativePickupAdapter.Reserved(item));
        internal static ItemTaskRunner CurrentTask(PlayerAIBot bot) => bot != null && tasks.TryGetValue(bot.Pointer, out var task) ? task : null;
        internal static void ReturnAfterSupply(PlayerAIBot bot)
            => Start(bot, ItemTaskKind.Return, InventorySlot.None, null, null, "补给收尾后返回／恢复");
        static bool Ground(out Pose pose, bool feedback = true)
        {
            pose = default; var source = PlayerManager.GetLocalPlayerAgent();
            if (source == null || !Physics.Raycast(source.EyePosition, source.TargetLookDir, out var ground, 80, LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore) || ground.normal.y < .7f)
            { if (feedback) Feedback("请把准星指向同层可站立的地面；墙面不作为炮台放置点"); return false; }
            var forward = Vector3.ProjectOnPlane(source.TargetLookDir, Vector3.up); if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            pose = new Pose(ground.point, Quaternion.LookRotation(forward, Vector3.up)); return true;
        }
        static bool BeforeNativeCommand(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (BotControlService.IsRecall(bot)) return true;
            if (bot?.Agent?.Owner != null && (PreLuna.Captain.Control.BotControlApi.Controls(bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64())
                || ControlCommandAdapter.Owns(bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64()))) {
                Feedback("接管机器人只接受Q接管指令；退出后的交互收尾也不能被原版命令覆盖"); return false;
            }
            if (SharedMeleeBridge.Owns(bot)) { Feedback("协同近战期间不能覆盖工具命令，请先取消协同"); return false; }
            if (ToolSupplyAdapter.Owns(bot)) { Feedback("工具补给期间不能覆盖工具部署，请等待／取消"); return false; }
            if (RescueOrders.Owns(bot)) { Feedback("救援期间不能覆盖为工具命令；请等待或取消救援"); return false; }
            try {
                if (!NativePickupAdapter.BeforeCommand(bot, desc)) return false;
                MovementOrders.BeforeNativeCommand(bot);
                SearchTaskAdapter.Cancel(bot, false);
                if (CollectTaskAdapter.Owns(bot)) {
                    CollectTaskAdapter.Cancel(bot, false);
                    if (CollectTaskAdapter.Owns(bot)) { Feedback("收集正在交互／核验，请收尾后重新下达工具命令"); return false; }
                }
                if (ResourceUseAdapter.Owns(bot)) { ResourceUseAdapter.Cancel(bot); Feedback("该机器人正在补给收尾，请结束后重新下达工具命令"); return false; }
                OpenTaskAdapter.Cancel(bot, false);
                if (OpenTaskAdapter.Owns(bot)) { Feedback("开启任务正在受保护的交互／核验阶段，请结束后重新下达工具命令"); return false; }
                SingleKillAdapter.Cancel(bot);
                if (SingleKillAdapter.Owns(bot)) { Feedback("指定击杀已在挥击，待本次核验结束后重新下达工具命令"); return false; }
                ItemActionAuthorization.Command(bot, desc); AutoPickupAdapter.CancelPrompt(bot); PreemptAutomatic(bot);
            } catch (Exception error) { Plugin.Logger.LogWarning("让出自动任务失败：" + error.Message); }
            return true;
        }
        static void PreemptAutomatic(PlayerAIBot bot)
        {
            MovementOrders.YieldDuty(bot);
            if (bot != null && automatic.Contains(bot.Pointer) && tasks.TryGetValue(bot.Pointer, out var task) && !task.Finished)
            { task.Cancel(false); task.Tick(Time.time); }
        }
        internal static bool Start(PlayerAIBot bot, ItemTaskKind kind, InventorySlot slot, ItemInLevel target, ContainerBridge destination, string label, bool explicitCommand = true, Action<bool> onResult = null, bool collectionChild = false, bool returnAfterOperation = true, ItemTaskLease sharedLease = null)
        {
            var itemKey = target?.Pointer ?? IntPtr.Zero; var containerKey = destination?.Key ?? IntPtr.Zero;
            string rejection = BusyReason(bot, collectionChild);
            if (rejection == null && itemKey != IntPtr.Zero && items.Contains(itemKey)) rejection = "目标物品已被另一个任务预约";
            if (rejection == null && containerKey != IntPtr.Zero && containers.Contains(containerKey)) rejection = "目标容器放回位置已被另一个任务预约";
            if (rejection != null) { Diagnose(bot, target, slot, label, "接单拒绝", rejection, explicitCommand); return false; }
            if (itemKey != IntPtr.Zero) items.Add(itemKey); if (containerKey != IntPtr.Zero) containers.Add(containerKey);
            bool released = false;
            void Cleanup() { if (released) return; released = true; if (itemKey != IntPtr.Zero) items.Remove(itemKey); if (containerKey != IntPtr.Zero) containers.Remove(containerKey); }
            try
            {
                var reportKey = new object(); string reportTarget = label;
                int reportSlot = bot.Agent.PlayerSlotIndex; long reportOwner = bot.Agent.Owner.Pointer.ToInt64(); string reportBot = bot.Agent.PlayerName;
                string reportTitle = kind == ItemTaskKind.Return ? "返回／恢复" : kind == ItemTaskKind.PutBack ? "放回容器" : explicitCommand ? "指定拿取" : "授权自动拾取";
                var session = new ItemTaskSession(bot, kind, slot, target, destination, label, Cleanup, explicitCommand, onResult,
                    (stage, detail) => TaskBoard.Journal.Record(reportKey, reportSlot, reportOwner, reportBot, reportTitle, reportTarget,
                        BoardState(stage), StageText(stage), detail, Time.time), sharedLease);
                string reason = session.Validate(); if (reason != null) { Cleanup(); Diagnose(bot, target, slot, label, "背包／目标检查", reason, explicitCommand); return false; }
                if (explicitCommand) AutoPickupAdapter.CancelPrompt(bot, target);
                try { reportTarget = kind == ItemTaskKind.Return ? "当前有效扫描／跟随领队" : kind == ItemTaskKind.PutBack ? "放回容器 @ " + PositionText(destination.Placement(slot).position)
                    : label + " @ " + PositionText(target.transform.position); }
                catch (Exception error) { Plugin.Logger.LogWarning("任务目标文字暂不可读，保留原任务名称：" + error.Message); }
                tasks[bot.Pointer] = new ItemTaskRunner(kind, session, Time.time, returnAfterOperation);
                if (explicitCommand) automatic.Remove(bot.Pointer); else automatic.Add(bot.Pointer);
                return true;
            }
            catch (Exception error) { Cleanup(); Diagnose(bot, target, slot, label, "接单异常", error.GetBaseException().Message, explicitCommand); throw; }
        }
        internal static bool ReserveControlItem(ItemInLevel item, out Action cleanup)
        {
            cleanup = null; if (item == null || !items.Add(item.Pointer)) return false;
            var key = item.Pointer; bool released = false;
            cleanup = () => { if (released) return; released = true; items.Remove(key); }; return true;
        }
        static void Update()
        {
            if (!installed) return;
            var current = LevelGeneration.Builder.CurrentFloor?.Pointer ?? IntPtr.Zero;
            if (!SNet.IsMaster || GameStateManager.CurrentStateName != eGameStateName.InLevel || current != floor || Time.time < lastTick)
            {
                if (current != floor || Time.time < lastTick || BotControlService.HasState || ControlCommandAdapter.HasState || tasks.Count != 0 || NativePickupAdapter.Count != 0 || SingleKillAdapter.Count != 0 || OpenTaskAdapter.Count != 0 || ResourceUseAdapter.HasState || FocusOrders.HasState || ToolSupplyAdapter.HasState || RescueOrders.HasState || MovementOrders.HasState || CollectTaskAdapter.HasState || SearchTaskAdapter.HasState || KnownSupplyIndex.HasState || KnownTerminalIndex.HasState || KnownContainerIndex.HasState || KnownInteractionIndex.HasState || RouteNavigation.HasState || TaskBoard.Journal.Count != 0) ResetTasks(); floor = current;
            }
            lastTick = Time.time;
            if (!SNet.IsMaster || GameStateManager.CurrentStateName != eGameStateName.InLevel) return;
            BotControlService.Tick();
            OpenTargetMarks.InputTick(); PositionMarks.InputTick(); AutoPickupAdapter.InputTick(); Shortcut();
            // The question deadline must not depend on the scaled task scheduler.
            if (Time.time < nextTick) { TickAutoPickup(); return; }
            nextTick = Time.time + .1f;
            RouteNavigation.Tick();
            NativePickupAdapter.Tick();
            SingleKillAdapter.Tick();
            OpenTaskAdapter.Tick();
            ResourceUseAdapter.Tick();
            RescueOrders.Tick(); ToolSupplyAdapter.Tick(); FocusOrders.Tick();
            MovementOrders.Tick(); GuardCombat.Tick();
            try { ItemActionAuthorization.Tick(); KnownSupplyIndex.Tick(); }
            catch (Exception error) { if (Time.time >= nextWarning) { nextWarning = Time.time + 10; Plugin.Logger.LogWarning("访问后拾取检查暂时失败：" + error.Message); } }
            TickAutoPickup();
            foreach (var task in tasks.Values)
                try { task.Tick(Time.time); } catch (Exception error) { task.Abort("执行失败：" + error.Message); }
            // Start the next child only after enumeration: never mutate the task dictionary while ticking it.
            CollectTaskAdapter.Tick(); SearchTaskAdapter.Tick();
            if (pending.Count > 0)
            {
                var ready = new List<int>();
                foreach (var entry in pending)
                { var agent = PlayerManager.Current?.GetPlayerAgentInSlot(entry.Key); var bot = agent?.Owner?.IsBot == true ? agent.GetComponent<PlayerAIBot>() : null; if (bot == null || !Busy(bot)) ready.Add(entry.Key); }
                foreach (int slot in ready)
                {
                    var request = pending[slot]; pending.Remove(slot);
                    if (PlayerManager.Current?.GetPlayerAgentInSlot(slot)?.Owner?.Pointer == request.Owner) Issue(slot, request.Command, request.Hit, request.Placement, request.Item, request.Command == 1);
                }
            }
        }
        static void TickAutoPickup()
        {
            if (Time.unscaledTime < nextPickupTick) return;
            nextPickupTick = Time.unscaledTime + .1f;
            try { AutoPickupAdapter.Tick(); }
            catch (Exception error)
            {
                if (Time.unscaledTime < nextPickupWarning) return;
                nextPickupWarning = Time.unscaledTime + 10;
                Plugin.Logger.LogWarning("[拾取询问诊断] 自动拾取更新失败：" + error.GetBaseException().Message);
            }
        }
        static PUI_CommunicationMenu Menu()
        {
            if (cachedMenu == null) cachedMenu = UnityEngine.Object.FindObjectOfType<PUI_CommunicationMenu>();
            return cachedMenu;
        }
        internal static PUI_CommunicationMenu NativeMenu => Menu();
        static void Shortcut()
        {
            if (BotControlService.HasActive) return;
            if (shortcut == null || shortcut.Value == KeyCode.None || !Input.GetKeyDown(shortcut.Value) || !AutoPickupAdapter.CanAnswer) return;
            var menu = Menu(); if (menu?.m_menu?.m_controller == null) { Feedback("原版通讯界面尚未建立"); return; }
            if (CaptainMenuInput.Active) { menu.SetActive(false); return; }
            if (menu.m_active) menu.SetActive(false);
            menu.SetActive(true);
            if (!CaptainMenuInput.Active) Feedback("队长菜单未能建立，请检查当前机器人与日志");
        }
        static void BeforeState(eGameStateName __0) { if (__0 != eGameStateName.InLevel) ResetTasks(); }
        static void ResetTasks()
        {
            BotControlService.Instance.Reset();
            ReleaseMenu();
            CaptainMenuInput.Reset();
            RouteNavigation.Reset();
            RescueOrders.Reset(); ToolSupplyAdapter.Reset(); FocusOrders.Reset(); GuardCombat.Reset();
            MovementOrders.Reset();
            NativePickupAdapter.Reset();
            SearchTaskAdapter.Reset(); CollectTaskAdapter.Reset();
            SingleKillAdapter.Reset();
            OpenTaskAdapter.Reset();
            ResourceUseAdapter.Reset();
            foreach (var task in tasks.Values) try { task.Abort("关卡、房主或机器人任务状态重置"); } catch (Exception error) { Plugin.Logger.LogWarning(error.Message); }
            tasks.Clear(); automatic.Clear(); pending.Clear(); items.Clear(); containers.Clear(); MenuTarget = null; nextTick = nextPickupTick = nextPickupWarning = 0; cachedMenu = null; AutoPickupAdapter.Reset(); ItemWorldIndex.Reset(); ItemActionAuthorization.Reset(); KnownSupplyIndex.Reset(); KnownTerminalIndex.Reset(); KnownContainerIndex.Reset(); KnownInteractionIndex.Reset(); TaskBoard.Journal.Reset();
        }
    }
}













