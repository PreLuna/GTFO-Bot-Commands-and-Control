using System;
using System.Collections.Generic;
namespace PreLuna.Captain.Tasks
{
    internal sealed record CaptainCommandSpec(string Code, string Title, string Parameters, int Handler = 0);
    internal sealed record CaptainCommandGroup(string Title, CaptainCommandSpec[] Commands);
    // Stable task codes belong to the planning document, never to native bot priority values.
    internal static class CaptainCommandCatalog
    {
        public static readonly string[] ResourceModes = { "只按命令使用", "仅允许紧急使用", "允许战斗自动补给", "恢复原版自动分发" };
        public static IEnumerable<string> PreviewOptions(string code, string fallback) => code switch {
            "W11" or "B07" => ResourceModes,
            "S06" => new[] { "门、房间／区域", "任务交付点、房间／区域", "发电机接口、房间／区域" },
            "A09" => new[] { "暂停当前任务并保留进度", "继续任务并重新核验目标" },
            "A02" => new[] { "中键标记当前Zone可达位置，到达后恢复扫描／跟随" },
            "B02" => new[] { "协同近战准备，等待成员就绪", "执行已准备的协同近战" },
            _ => new[] { fallback }
        };
        public static readonly CaptainCommandGroup[] Groups = {
            new("物资", new[] {
                new CaptainCommandSpec("W01", "指定拿取", "准星物品，按实际槽位走普通拾取／原版搬运", 1),
                new("W03", "放回容器", "准星已打开的容器", 2),
                new("W07", "按类型收集", "已知范围、实际类型、对象数量上限", 17),
                new("W08", "打开标记容器／普通门", "鼠标中键标记；物理锁近战，入侵锁筛选熔锁器", 10),
                new("W09", "给指定队友补给一次", "队友、持有资源包的机器人；原版动作使用一次", 16),
                new("W10", "给指定工具补给", "携带工具／已部署炮台，单次原版补给与双向核验", 42),
                new("W11", "设置资源使用策略", "仅按命令／紧急／战斗／原版", 14),
                new("W12", "报告携带物资", "按实际槽位和数量报告", 15) }),
            new("搜寻", new[] {
                new CaptainCommandSpec("S01", "列出已知物资", "范围、来源，仅查询"),
                new("S03", "搜寻物资", "当前房间／区域、类型，立即统计标记，不移动", 18),
                new("S04", "搜寻终端", "当前房间／Zone，立即统计标记，不移动", 23),
                new("S05", "搜寻容器", "当前房间／Zone、全部／未开启／有锁，立即统计标记", 24),
                new("S06", "搜寻交互点", "门／发电机／任务接口，只读统计标记", 28),
                new("S07", "查看搜索结果", "搜寻统计的物资、终端、容器与交互点", 19) }),
            new("部署工具（原版）", Array.Empty<CaptainCommandSpec>()),
            new("行动", new[] {
                new CaptainCommandSpec("A01", "跟随指定玩家", "指定真人队友；临时扫描／交互结束后恢复", 35),
                new("A02", "前往标记位置", "鼠标中键位置，到达后恢复扫描／跟随", 36),
                new("A03", "驻守标记位置", "中键地面与朝向，活动半径可配置", 37),
                new("A04", "守卫房间", "当前房间固定点、半径，不巡视", 52),
                new("A05", "显示到目标的路线", "当前Zone已知目标；独立步行路线，机器人不移动", 31),
                new("A06", "查看／重算／取消导航", "仅管理自己的路线显示", 32),
                new("A07", "救援指定队友", "倒地队友、一名机器人；原版救援与结果核验", 39),
                new("A08", "多人救援分配", "当前Zone倒地队友分别分配，已有救援不抢占", 40),
                new("A09", "暂停／继续当前任务", "保留目标；原子交互收尾；继续时重新核验", 38),
                new("A10", "取消队长任务", "保留已携带物品", 4),
                new("A11", "取消并返回", "恢复有效扫描／跟随", 5),
                new("A12", "查看队长任务", "按状态查看目标、执行人、阶段与最近结果", 13) }),
            new("战斗", new[] {
                new CaptainCommandSpec("B01", "集火指定敌人", "已激怒目标、指定机器人／全队，沿用现有武器决策", 43),
                new("B02", "协同近战准备／执行", "复用E键同一目标、参与者和就绪状态", 47),
                new("B03", "掩护指定队友", "队友附近固定站位、半径，不持续追随", 53),
                new("B04", "防守指定入口", "中键地面站位与朝向、半径", 54),
                new("B08", "指定击杀", "准星沉睡敌人；仅选择一名机器人，蓄力近战一次", 9),
                new("B06", "恢复自主战斗", "解除指定／全队集火目标，原有任务保留", 45),
                new("B07", "战斗补给策略", "复用物资组的同一策略", 14) })
        };
    }
    internal sealed record CaptainMenuBot(int Slot, long Owner, string Label, bool Alive, bool HasMelee = true, int LockMelters = 0,
        string ResourceMode = null, bool HasSupplyPack = true, string SupplyPack = null, long SupplyItem = 0, bool HasToolPack = false,
        string KillBlocked = null, string KillNote = null);
    internal sealed record CaptainMenuReceiver(int Slot, long Owner, string Label, bool Alive, bool IsBot = false, bool Downed = false);
    internal sealed record CaptainMenuToolTarget(int Id, string Label, string Blocked);
    internal sealed record CaptainMenuCombatTarget(string Label, string FocusBlocked, string MeleeBlocked);
    internal sealed record CaptainMenuMeleeState(string Label, bool Ready, bool Active, string Blocked);
    internal sealed record CaptainMenuGuardPoint(int Id, string Label, string Blocked);
    internal sealed record CaptainMenuGuardCover(int Slot, long Owner, string Label, string Blocked);
    internal sealed record CaptainMenuGuardTargets(IReadOnlyList<CaptainMenuGuardPoint> Rooms, CaptainMenuGuardPoint Entrance, IReadOnlyList<CaptainMenuGuardCover> Covers);
    internal sealed record CaptainMenuPositionTarget(string Label, string Blocked);
    internal sealed record CaptainMenuOpenTarget(string Label, string Blocked, bool NeedsMelee, int NeededMelters);
    internal sealed record CaptainSupplyResult(int Id, string Label, string Blocked, uint TypeId = 0, int ItemSlot = 0, string TypeName = null, bool BotDiscovered = false);
    internal sealed record CaptainTerminalResult(int Id, string Label, bool Notice = false);
    internal sealed record CaptainContainerResult(int Id, string Label, string Blocked = null, bool NeedsMelee = false, int NeededMelters = 0, bool Notice = false);
    internal sealed record CaptainInteractionResult(int Id, string Label, bool Notice = false);
    internal sealed class CaptainSupplyScope
    {
        public readonly string Label;
        public string Notice;
        public readonly List<CaptainSupplyResult> Results = new();
        public CaptainSupplyScope(string label) { Label = label; }
    }
    internal sealed record CaptainMenuLeaf(string Code, string Title, int Slot, long Owner, int Handler, string Blocked, int SupplyId = 0, int RecordId = 0,
        int ResourceMode = -1, int ReceiverSlot = -1, long ReceiverOwner = 0, long SupplyItem = 0,
        int CollectScope = -1, uint CollectType = 0, int CollectSlot = 0, int CollectLimit = 0,
        int SearchScope = -1, int SearchFilter = 0, bool SearchPause = false, int TerminalId = 0, int ContainerId = 0, int InteractionId = 0, int ToolTargetId = 0, int GuardPoint = -1, float GuardRadius = 3);
    internal sealed class CaptainMenuPlanNode
    {
        public uint Id; public string Label; public uint BackTo; public CaptainMenuLeaf Leaf;
        public readonly List<CaptainMenuPlanNode> Children = new();
    }
    // Pure menu plan. It does not search the world, acquire task control, or send bot commands.
    internal static class CaptainMenuPlan
    {
        public static CaptainMenuPlanNode Build(IReadOnlyList<CaptainMenuBot> bots, string item, string container,
            string ground, bool pickupValid, bool containerValid, bool groundValid, string enemy = "未识别敌人", bool enemyValid = false,
            CaptainMenuOpenTarget openTarget = null, IReadOnlyList<CaptainSupplyScope> supplies = null, TaskBoardSnapshot taskBoard = null,
            IReadOnlyList<CaptainMenuReceiver> receivers = null, IReadOnlyList<CaptainTerminalResult> terminals = null,
            IReadOnlyList<CaptainContainerResult> containers = null, IReadOnlyList<CaptainInteractionResult> interactions = null,
            CaptainMenuPositionTarget position = null, IReadOnlyList<CaptainMenuToolTarget> tools = null,
            CaptainMenuCombatTarget combat = null, CaptainMenuMeleeState melee = null, CaptainMenuGuardTargets guards = null, bool controlMode = false)
        {
            uint next = CaptainMenuTree.NodeStart;
            CaptainMenuPlanNode Node(string title) {
                if (next >= CaptainMenuTree.NodeEnd) throw new InvalidOperationException("队长菜单节点数量超出范围");
                return new() { Id = next++, Label = title };
            }
            var root = new CaptainMenuPlanNode { Id = CaptainMenuTree.RootId, Label = "队长指令（先任务，再选人）" };
            void Back(CaptainMenuPlanNode page, CaptainMenuPlanNode parent)
                => page.Children.Add(new() { Id = next++, Label = parent == root ? "返回队长首页" : "返回上一级", BackTo = parent.Id });
            void Task(CaptainMenuPlanNode parent, CaptainCommandSpec spec)
            {
                var task = Node(spec.Handler == 1 ? spec.Title + ": " + item : spec.Handler == 9 ? spec.Title + ": " + enemy
                    : spec.Handler == 10 ? spec.Title + ": " + (openTarget?.Label ?? "请先中键标记")
                    : spec.Handler == 43 ? spec.Title + ": " + (combat?.Label ?? "未识别敌人") : spec.Handler is 36 or 37 ? spec.Title + ": " + (position?.Label ?? "请先中键标记") : spec.Title + (spec.Handler == 0 && spec.Code != "S01" ? "【待实现】" : "")); parent.Children.Add(task);
                void Executors(CaptainMenuPlanNode target, int handler, string blocked, bool automaticChoice = true, CaptainMenuReceiver receiver = null, bool controlPause = false, int guardPoint = -1, float guardRadius = 3)
                {
                    foreach (var bot in bots)
                    {
                        var reason = !bot.Alive && handler != 8 ? "机器人倒地，不能执行" : handler == 53 && bot.Slot == receiver?.Slot ? "不能掩护自己" : handler == 9 ? bot.KillBlocked ?? blocked : blocked;
                        var submit = Node(bot.Label + (reason != null ? handler == 9 ? "【" + reason + "】" : "【不可执行】"
                            : handler == 9 ? " → 提交（" + (bot.KillNote ?? "接单时再次核验") + "）" : handler == 0 ? "【预览】" : " → 提交"));
                        submit.Leaf = new(spec.Code, spec.Title, bot.Slot, bot.Owner, handler, reason,
                            ReceiverSlot:receiver?.Slot ?? -1, ReceiverOwner:receiver?.Owner ?? 0, SearchPause:controlPause, GuardPoint:guardPoint, GuardRadius:guardRadius);
                        target.Children.Add(submit);
                    }
                    if (bots.Count == 0) { var empty = Node("当前没有机器人"); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 0, "请先加入机器人"); target.Children.Add(empty); }

                }
                void Target(string title, int handler, string blocked, CaptainMenuPlanNode parentPage = null)
                {
                    var targetParent = parentPage ?? task;
                    var target = Node(title); targetParent.Children.Add(target);
                    Executors(target, handler, blocked);
                    Back(target, targetParent);
                }
                if (spec.Handler is 52 or 53 or 54) {
                    void FixedPoint(string label, string blocked, int point, CaptainMenuReceiver receiver = null) {
                        var target=Node(label);task.Children.Add(target);
                        foreach(float radius in new[]{2f,3f,5f}){
                            var parameter=Node("活动半径："+radius+"米（固定点，不巡视）");target.Children.Add(parameter);
                            Executors(parameter,spec.Handler,blocked,false,receiver,guardPoint:point,guardRadius:radius);Back(parameter,target);
                        }
                        Back(target,task);
                    }
                    if(spec.Handler==52&&guards?.Rooms!=null)foreach(var point in guards.Rooms)FixedPoint(point.Label,point.Blocked,point.Id);
                    else if(spec.Handler==53&&guards?.Covers!=null)foreach(var receiver in guards.Covers)
                        FixedPoint("掩护队友："+receiver.Label,receiver.Blocked,-1,new(receiver.Slot,receiver.Owner,receiver.Label,true));
                    else if(spec.Handler==54&&guards?.Entrance!=null)FixedPoint(guards.Entrance.Label,guards.Entrance.Blocked,guards.Entrance.Id);
                    if(task.Children.Count==0){var notice=Node("没有有效的固定目标，请重开菜单／中键标记门旁地面");notice.Leaf=new(spec.Code,spec.Title,-1,0,spec.Handler,notice.Label);task.Children.Add(notice);}
                }
                else if (spec.Handler == 35) {
                    int count = 0;
                    foreach (var receiver in receivers ?? Array.Empty<CaptainMenuReceiver>()) {
                        if (receiver.IsBot) continue;
                        var player = Node("目标玩家：" + receiver.Label); task.Children.Add(player); count++;
                        Executors(player, 35, receiver.Alive ? null : "跟随对象已经倒地，请先救援", false, receiver); Back(player, task);
                    }
                    if (count == 0) { var empty = Node("没有可选的真人队友"); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 35, empty.Label); task.Children.Add(empty); }
                }
                else if (spec.Handler == 42) {
                    foreach (var tool in tools ?? Array.Empty<CaptainMenuToolTarget>()) {
                        var page=Node(tool.Label);task.Children.Add(page);int found=0;
                        foreach(var bot in bots) {
                            if(!bot.Alive||!bot.HasToolPack)continue;
                            var submit=Node(bot.Label+"（"+bot.SupplyPack+"） → 补给一次");
                            submit.Leaf=new(spec.Code,spec.Title,bot.Slot,bot.Owner,42,tool.Blocked,SupplyItem:bot.SupplyItem,ToolTargetId:tool.Id);
                            page.Children.Add(submit);found++;
                        }
                        if(found==0){var empty=Node("没有携带工具补给包的存活机器人");empty.Leaf=new(spec.Code,spec.Title,-1,0,42,empty.Label);page.Children.Add(empty);}
                        Back(page,task);
                    }
                    if(tools==null||tools.Count==0){var empty=Node("当前Zone未识别到携带工具或已部署炮台");empty.Leaf=new(spec.Code,spec.Title,-1,0,42,empty.Label);task.Children.Add(empty);}
                }
                else if (spec.Handler is 43 or 45) {
                    Executors(task,spec.Handler,spec.Handler==43?combat?.FocusBlocked??(combat==null?"请对准已激怒敌人":null):null,false);
                    var all=Node(spec.Handler==43?"当前全队机器人集火":"全队恢复自主战斗");
                    all.Leaf=new(spec.Code,spec.Title,-1,0,spec.Handler==43?44:46,spec.Handler==43?combat?.FocusBlocked??(combat==null?"请对准已激怒敌人":null):null);if(!controlMode)task.Children.Add(all);
                }
                else if(spec.Handler==47) {
                    var prepare=Node("准备目标："+(combat?.Label??"未识别沉睡敌人"));task.Children.Add(prepare);
                    Executors(prepare,47,combat?.MeleeBlocked??(combat==null?"请靠近沉睡敌人":null),false);
                    var all=Node("全部所选可用机器人准备（原系统核验跟随／距离）");
                    all.Leaf=new(spec.Code,spec.Title,-1,0,48,combat?.MeleeBlocked??(combat==null?"请靠近沉睡敌人":null));prepare.Children.Add(all);Back(prepare,task);
                    var execute=Node("执行共用协同（"+(melee?.Label??"未准备")+"）");
                    execute.Leaf=new(spec.Code,spec.Title,-1,0,49,melee?.Ready==true?null:melee?.Blocked??"请先准备协同近战");task.Children.Add(execute);
                    var stop=Node("取消共用协同任务");stop.Leaf=new(spec.Code,spec.Title,-1,0,50,melee?.Active==true?null:"没有共用协同任务");task.Children.Add(stop);
                    var status=Node("查看参与者与就绪状态");status.Leaf=new(spec.Code,spec.Title,-1,0,51,null);task.Children.Add(status);
                }
                else if (spec.Handler == 39) {
                    int count = 0;
                    foreach (var receiver in receivers ?? Array.Empty<CaptainMenuReceiver>()) {
                        if (!receiver.Downed) continue;
                        var target = Node("倒地队友：" + receiver.Label); task.Children.Add(target); count++;
                        Executors(target, 39, null, false, receiver); Back(target, task);
                    }
                    if (count == 0) { var empty = Node("没有可救援的倒地队友"); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 39, empty.Label); task.Children.Add(empty); }
                }
                else if (spec.Handler == 40) {
                    var assign = Node("分配当前Zone的倒地队友（每个目标一名机器人）");
                    assign.Leaf = new(spec.Code, spec.Title, -1, 0, 40, null); task.Children.Add(assign);
                    var cancel = Node("取消多人救援队列（已开始的交互先收尾）");
                    cancel.Leaf = new(spec.Code, "取消多人救援分配", -1, 0, 41, null); task.Children.Add(cancel);
                }
                else if (spec.Handler is 36 or 37 or 55) Executors(task, spec.Handler, position?.Blocked ?? (position == null ? "请先用鼠标中键标记可站立位置" : null), false);
                else if (spec.Handler == 56) Executors(task, 56, null, false);
                else if (spec.Handler == 38) {
                    foreach (bool pause in new[] { true, false }) {
                        var page = Node(pause ? "暂停当前任务（原子交互先收尾）" : "继续当前任务（重新核验）"); task.Children.Add(page);
                        Executors(page, 38, null, false, controlPause:pause); Back(page, task);
                    }
                }
                else if (spec.Handler == 31) {
                    void Choice(CaptainMenuPlanNode page, string title, int supply = 0, int terminal = 0, int box = 0, int point = 0) {
                        var route = Node(title + " → 显示路线（当前Zone）");
                        route.Leaf = new(spec.Code, title, -1, 0, 31, null, SupplyId:supply, TerminalId:terminal, ContainerId:box, InteractionId:point);
                        page.Children.Add(route);
                    }
                    var supplyPage = Node("已知物资"); task.Children.Add(supplyPage); var ids = new HashSet<int>();
                    if (supplies != null) for (int i = 0; i < Math.Min(2, supplies.Count); i++) foreach (var result in supplies[i].Results)
                        if (result.Id > 0 && ids.Add(result.Id)) Choice(supplyPage, result.Label, supply:result.Id);
                    var terminalPage = Node("终端"); task.Children.Add(terminalPage);
                    if (terminals != null) foreach (var result in terminals) if (!result.Notice) Choice(terminalPage, result.Label, terminal:result.Id);
                    var boxPage = Node("容器"); task.Children.Add(boxPage);
                    if (containers != null) foreach (var result in containers) if (!result.Notice) Choice(boxPage, result.Label, box:result.Id);
                    var pointPage = Node("门／发电机／任务接口（仅定位）"); task.Children.Add(pointPage);
                    if (interactions != null) foreach (var result in interactions) if (!result.Notice) Choice(pointPage, result.Label, point:result.Id);
                    foreach (var page in new[] { supplyPage, terminalPage, boxPage, pointPage }) {
                        if (page.Children.Count == 0) {
                            var empty = Node("尚无已知目标，请先查询物资／执行对应搜寻");
                            empty.Leaf = new(spec.Code, empty.Label, -1, 0, 31, empty.Label); page.Children.Add(empty);
                        }
                        Back(page, task);
                    }
                }
                else if (spec.Handler == 32) {
                    foreach (var option in new[] { ("查看当前导航", 32), ("重新计算当前路线", 33), ("取消独立导航", 34) }) {
                        var choice = Node(option.Item1); choice.Leaf = new(spec.Code, option.Item1, -1, 0, option.Item2, null); task.Children.Add(choice);
                    }
                }
                else if (spec.Handler is 18 or 23 or 24 or 28) {
                    int options = 2;
                    for (int range = 0; range < options; range++) {
                        var page = Node(range == 0 ? "范围：当前房间" : "范围：当前区域 Zone（普通门可达）"); task.Children.Add(page);
                        int filters = spec.Handler == 28 ? 4 : spec.Handler == 18 ? 5 : spec.Handler == 24 ? 3 : 1;
                        for (int filter = 0; filter < filters; filter++) {
                            var target = spec.Handler == 18 ? Node(new[] { "全部物资（含任务／特殊物品）", "资源包", "普通消耗品", "口袋任务物品", "特殊手持／搬运物品" }[filter])
                                : spec.Handler == 24 ? Node(new[] { "全部容器", "未开启", "有锁" }[filter])
                                : spec.Handler == 28 ? Node(new[] { "全部交互点", "普通门／安全门（当前可达侧）", "发电机接口", "任务交付／交互点（含 HSU）" }[filter]) : page;
                            if (target != page) page.Children.Add(target);
                            var submit = Node("立即统计并标记（机器人不移动）");
                            submit.Leaf = new(spec.Code, spec.Title, -1, 0, spec.Handler, null, SearchScope:range, SearchFilter:filter);
                            target.Children.Add(submit);
                            if (target != page) Back(target, page);
                        }
                        Back(page, task);
                    }
                }
                else if (spec.Handler == 17) {
                    var areas = Node("指定已到访区域");
                    var scopes = supplies ?? new[] { new CaptainSupplyScope("当前房间"), new CaptainSupplyScope("当前区域 Zone") };
                    for (int scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++) {
                        var scope = scopes[scopeIndex]; var parentPage = scopeIndex < 2 ? task : areas;
                        var page = Node("范围：" + scope.Label); parentPage.Children.Add(page);
                        var groups = new Dictionary<(int, uint), List<CaptainSupplyResult>>();
                        foreach (var supply in scope.Results) {
                            if (supply.Blocked != null || supply.TypeId == 0 || (supply.ItemSlot != 4 && supply.ItemSlot != 5)) continue;
                            var key = (supply.ItemSlot, supply.TypeId);
                            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
                            if (!list.Exists(s => s.Id == supply.Id)) list.Add(supply);
                        }
                        foreach (var pair in groups) {
                            var list = pair.Value;
                            var type = Node((list[0].TypeName ?? list[0].Label) + " #" + pair.Key.Item2 + "（已知可选 " + list.Count + " 件）"); page.Children.Add(type);
                            var limits = new HashSet<int>();
                            foreach (int requested in new[] { 1, 3, 5 }) {
                                int limit = Math.Min(requested, list.Count); if (!limits.Add(limit)) continue;
                                var count = Node("最多拾取 " + limit + " 件（按实际容量）"); type.Children.Add(count);
                                foreach (var bot in bots) {
                                    var choice = Node(bot.Label + (bot.Alive ? " → 提交" : "【不可执行】"));
                                    choice.Leaf = new(spec.Code, spec.Title, bot.Slot, bot.Owner, 17, bot.Alive ? null : "机器人倒地，不能执行",
                                        CollectScope:scopeIndex, CollectType:pair.Key.Item2, CollectSlot:pair.Key.Item1, CollectLimit:limit);
                                    count.Children.Add(choice);
                                }
                                if (bots.Count == 0) { var empty = Node("当前没有机器人"); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 17, empty.Label); count.Children.Add(empty); }
                                Back(count, type);
                            }
                            Back(type, page);
                        }
                        if (groups.Count == 0) { var empty = Node("没有可收集的已知物资：先发现物品／SM 查询并打开箱柜"); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 17, empty.Label); page.Children.Add(empty); }
                        if (scope.Notice != null) { var notice = Node(scope.Notice); notice.Leaf = new(spec.Code, spec.Title, -1, 0, 17, scope.Notice); page.Children.Add(notice); }
                        Back(page, parentPage);
                    }
                    if (areas.Children.Count > 0) { task.Children.Add(areas); Back(areas, task); }
                }
                else if (spec.Handler == 14) {
                    for (int mode = 0; mode < CaptainCommandCatalog.ResourceModes.Length; mode++) {
                        var page = Node(CaptainCommandCatalog.ResourceModes[mode]); task.Children.Add(page);
                        foreach (var bot in bots) {
                            var choice = Node(bot.Label + "（当前：" + (bot.ResourceMode ?? "默认策略") + "） → 设置");
                            choice.Leaf = new(spec.Code, spec.Title, bot.Slot, bot.Owner, 14, null, ResourceMode:mode); page.Children.Add(choice);
                        }
                        if (bots.Count > 0) { var all = Node("设置所有当前机器人（仅本局）"); all.Leaf = new(spec.Code, spec.Title, -2, 0, 14, null, ResourceMode:mode); page.Children.Add(all); }
                        else { var empty = Node("当前没有机器人"); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 14, empty.Label); page.Children.Add(empty); }
                        Back(page, task);
                    }
                }
                else if (spec.Handler == 15) {
                    foreach (var bot in bots) {
                        var report = Node(bot.Label + " → 查看物资与策略"); report.Leaf = new(spec.Code, spec.Title, bot.Slot, bot.Owner, 15, null); task.Children.Add(report);
                    }
                    var all = Node(bots.Count > 0 ? "报告所有当前机器人" : "当前没有机器人");
                    all.Leaf = new(spec.Code, spec.Title, -2, 0, 15, bots.Count > 0 ? null : all.Label); task.Children.Add(all);
                }
                else if (spec.Handler == 16) {
                    foreach (var receiver in receivers ?? Array.Empty<CaptainMenuReceiver>()) {
                        var page = Node("补给对象：" + receiver.Label); task.Children.Add(page);
                        foreach (var bot in bots) {
                            var reason = !receiver.Alive ? "倒地队友需要先救援" : !bot.Alive ? "机器人倒地，不能执行" : !bot.HasSupplyPack ? "没有可用资源包" : null;
                            var choice = Node(bot.Label + (bot.SupplyPack == null ? "" : "（" + bot.SupplyPack + "）") + (reason == null ? " → 使用一次" : "【不可执行】"));
                            choice.Leaf = new(spec.Code, spec.Title, bot.Slot, bot.Owner, 16, reason, ReceiverSlot:receiver.Slot, ReceiverOwner:receiver.Owner, SupplyItem:bot.SupplyItem); page.Children.Add(choice);
                        }
                        if (bots.Count == 0) { var empty = Node("当前没有机器人"); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 16, empty.Label); page.Children.Add(empty); }
                        Back(page, task);
                    }
                    if (receivers == null || receivers.Count == 0) { var empty = Node("没有可识别的补给对象"); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 16, empty.Label); task.Children.Add(empty); }
                }
                else if (spec.Handler == 13) {
                    foreach (var group in TaskBoard.Groups) {
                        var page = Node(group.Label); task.Children.Add(page);
                        int count = 0;
                        if (taskBoard != null) foreach (var row in taskBoard.Rows) {
                            if (row.State != group.State) continue;
                            var result = Node(row.Label); result.Leaf = new(spec.Code, row.Task, -1, 0, 13, null, RecordId:row.Id);
                            page.Children.Add(result); count++;
                        }
                        if (count == 0) {
                            var empty = Node("此分组没有记录"); empty.Leaf = new(spec.Code, empty.Label, -1, 0, 13, "此分组没有记录；看板不代表原版机器人空闲"); page.Children.Add(empty);
                        }
                        page.Label += "（" + count + "）"; Back(page, task);
                    }
                }
                else if (spec.Handler == 1) Executors(task, 1, pickupValid ? null : "未识别受支持的物品，请对准后重开菜单");
                else if (spec.Handler == 9) Executors(task, 9, enemyValid ? null : "请对准沉睡敌人后重新打开菜单");
                else if (spec.Handler == 2) {
                    Target("资源包 → " + container, 2, containerValid ? null : "未识别容器，请对准已打开的箱柜后重开菜单");
                    Target("普通消耗品 → " + container, 3, containerValid ? null : "未识别容器，请对准已打开的箱柜后重开菜单");
                }
                else if (spec.Handler == 7) Target("确认炮台位置：" + ground, 7, groundValid ? null : "未识别地面，请对准地面后重开菜单");
                else if (spec.Handler == 6) Target("确认范围：所选机器人的已部署物（原版回收）", 6, null);
                else if (spec.Handler == 10) {
                    string error = openTarget == null ? "请先用鼠标中键标记物资箱、储物柜或普通弱门" : openTarget.Blocked;
                    if (error != null) { var blocked = Node(error); blocked.Leaf = new(spec.Code, spec.Title, -1, 0, 10, error); task.Children.Add(blocked); }
                    else {
                        int count = 0;
                        foreach (var bot in bots) {
                            if (!OpenTaskPolicy.CanUse(bot.Alive, bot.HasMelee, bot.LockMelters, openTarget.NeedsMelee, openTarget.NeededMelters)) continue;
                            var submit = Node(bot.Label + (openTarget.NeededMelters > 0 ? "（熔锁器×" + bot.LockMelters + "）" : "") + " → 提交");
                            submit.Leaf = new(spec.Code, spec.Title, bot.Slot, bot.Owner, 10, null); task.Children.Add(submit); count++;
                        }
                        if (count == 0) {
                            string reason = openTarget.NeededMelters > 0 ? "没有可行动且熔锁器余量足够的机器人" : "没有具备当前开启能力的可行动机器人";
                            var empty = Node(reason); empty.Leaf = new(spec.Code, spec.Title, -1, 0, 10, reason); task.Children.Add(empty);
                        }
                    }
                }
                else if (spec.Code == "S01" || spec.Code == "S07") {
                    var areas = Node("指定已到访区域");
                    IReadOnlyList<CaptainSupplyScope> scopes = supplies ?? new[] { new CaptainSupplyScope("当前房间"), new CaptainSupplyScope("当前区域 Zone") };
                    if (spec.Code == "S07") {
                        var summary = new CaptainSupplyScope("本局队长即时搜寻发现"); var ids = new HashSet<int>();
                        foreach (var scope in scopes) foreach (var result in scope.Results)
                            if (result.BotDiscovered && ids.Add(result.Id)) summary.Results.Add(result);
                        summary.Results.Sort((a, b) => a.Id.CompareTo(b.Id)); scopes = new[] { summary };
                    }
                    for (int i = 0; i < scopes.Count; i++) {
                        var scope = scopes[i]; var parentPage = i < 2 ? task : areas;
                        var scopePage = Node("范围：" + scope.Label); parentPage.Children.Add(scopePage);
                        var shown = scope.Results.FindAll(s => spec.Code != "S07" || s.BotDiscovered);
                        if (shown.Count == 0) {
                            var empty = Node(spec.Code == "S07" ? "尚无队长即时搜寻发现，请先执行搜寻物资" : "没有已知物资：先发现物品或执行 SM 查询");
                            empty.Leaf = new(spec.Code, empty.Label, -1, 0, 11, empty.Label); scopePage.Children.Add(empty);
                        }
                        foreach (var supply in shown) {
                            var result = Node(supply.Label); scopePage.Children.Add(result);
                            var details = Node("查看位置、来源与状态");
                            details.Leaf = new(spec.Code, supply.Label, -1, 0, 11, null, supply.Id); result.Children.Add(details);
                            var pickup = Node("指定拿取：" + supply.Label); result.Children.Add(pickup);
                            foreach (var bot in bots) {
                                string reason = !bot.Alive ? "机器人倒地，不能执行" : supply.Blocked;
                                var submit = Node(bot.Label + (reason == null ? " → 提交" : "【不可执行】"));
                                submit.Leaf = new(spec.Code, supply.Label, bot.Slot, bot.Owner, 12, reason, supply.Id); pickup.Children.Add(submit);
                            }
                            if (bots.Count == 0) {
                                var empty = Node("当前没有机器人"); empty.Leaf = new(spec.Code, supply.Label, -1, 0, 12, "请先加入机器人", supply.Id); pickup.Children.Add(empty);
                            }
                            var route = Node("显示到物资的路线（当前Zone）");
                            route.Leaf = new(spec.Code, supply.Label, -1, 0, 31, null, SupplyId:supply.Id); result.Children.Add(route);
                            Back(pickup, result); Back(result, scopePage);
                        }
                        if (scope.Notice != null) { var notice = Node(scope.Notice); notice.Leaf = new(spec.Code, notice.Label, -1, 0, 11, notice.Label); scopePage.Children.Add(notice); }
                        Back(scopePage, parentPage);
                    }
                    if (spec.Code != "S07") {
                        if (areas.Children.Count == 0) { var empty = Node("没有其他已知物资区域"); empty.Leaf = new(spec.Code, empty.Label, -1, 0, 11, empty.Label); areas.Children.Add(empty); }
                        task.Children.Add(areas); Back(areas, task);
                    }
                    else if (terminals != null) {
                        var terminalPage = Node("发现的终端（当前Zone）"); task.Children.Add(terminalPage);
                        if (terminals.Count == 0) {
                            var empty = Node("尚无当前Zone终端发现，请先执行搜寻终端");
                            empty.Leaf = new(spec.Code, empty.Label, -1, 0, 21, empty.Label); terminalPage.Children.Add(empty);
                        }
                        foreach (var terminal in terminals) {
                            var result = Node(terminal.Label); terminalPage.Children.Add(result);
                            if (terminal.Notice) { result.Leaf = new(spec.Code, terminal.Label, -1, 0, 21, terminal.Label); continue; }
                            var details = Node("查看编号、位置与状态");
                            details.Leaf = new(spec.Code, terminal.Label, -1, 0, 21, null, TerminalId:terminal.Id); result.Children.Add(details);
                            var mark = Node("重新标记终端位置");
                            mark.Leaf = new(spec.Code, terminal.Label, -1, 0, 22, null, TerminalId:terminal.Id); result.Children.Add(mark);
                            var route = Node("显示到终端的路线（当前Zone）");
                            route.Leaf = new(spec.Code, terminal.Label, -1, 0, 31, null, TerminalId:terminal.Id); result.Children.Add(route);
                            Back(result, terminalPage);
                        }
                        Back(terminalPage, task);
                    }
                    if (spec.Code == "S07" && containers != null) {
                        var containerPage = Node("发现的容器（当前Zone）"); task.Children.Add(containerPage);
                        if (containers.Count == 0) {
                            var empty = Node("尚无当前Zone容器发现，请先执行搜寻容器");
                            empty.Leaf = new(spec.Code, empty.Label, -1, 0, 25, empty.Label); containerPage.Children.Add(empty);
                        }
                        foreach (var box in containers) {
                            var result = Node(box.Label); containerPage.Children.Add(result);
                            if (box.Notice) { result.Leaf = new(spec.Code, box.Label, -1, 0, 25, box.Label); continue; }
                            var details = Node("查看编号、位置、锁与内容");
                            details.Leaf = new(spec.Code, box.Label, -1, 0, 25, null, ContainerId:box.Id); result.Children.Add(details);
                            var mark = Node("重新标记容器位置");
                            mark.Leaf = new(spec.Code, box.Label, -1, 0, 26, null, ContainerId:box.Id); result.Children.Add(mark);
                            var open = Node("打开：" + box.Label); result.Children.Add(open);
                            int count = 0;
                            if (box.Blocked == null) foreach (var bot in bots) {
                                if (!OpenTaskPolicy.CanUse(bot.Alive, bot.HasMelee, bot.LockMelters, box.NeedsMelee, box.NeededMelters)) continue;
                                var submit = Node(bot.Label + (box.NeededMelters > 0 ? "（熔锁器×" + bot.LockMelters + "）" : "") + " → 提交");
                                submit.Leaf = new(spec.Code, box.Label, bot.Slot, bot.Owner, 27, null, ContainerId:box.Id); open.Children.Add(submit); count++;
                            }
                            if (count == 0) {
                                string reason = box.Blocked ?? (box.NeededMelters > 0 ? "没有可行动且熔锁器余量足够的机器人" : "没有具备当前开启能力的可行动机器人");
                                var empty = Node(reason); empty.Leaf = new(spec.Code, box.Label, -1, 0, 27, reason, ContainerId:box.Id); open.Children.Add(empty);
                            }
                            var route = Node("显示到容器的路线（当前Zone）");
                            route.Leaf = new(spec.Code, box.Label, -1, 0, 31, null, ContainerId:box.Id); result.Children.Add(route);
                            Back(open, result); Back(result, containerPage);
                        }
                        Back(containerPage, task);
                    }
                    if (spec.Code == "S07" && interactions != null) {
                        var page = Node("发现的交互点（当前Zone）"); task.Children.Add(page);
                        if (interactions.Count == 0) {
                            var empty = Node("尚无当前Zone交互点发现，请先执行搜寻交互点");
                            empty.Leaf = new(spec.Code, empty.Label, -1, 0, 29, empty.Label); page.Children.Add(empty);
                        }
                        foreach (var point in interactions) {
                            var result = Node(point.Label); page.Children.Add(result);
                            if (point.Notice) { result.Leaf = new(spec.Code, point.Label, -1, 0, 29, point.Label); continue; }
                            var details = Node("查看编号、位置、类型与状态（只读）");
                            details.Leaf = new(spec.Code, point.Label, -1, 0, 29, null, InteractionId:point.Id); result.Children.Add(details);
                            var mark = Node("重新标记交互点位置");
                            mark.Leaf = new(spec.Code, point.Label, -1, 0, 30, null, InteractionId:point.Id); result.Children.Add(mark);
                            var route = Node("显示到交互点的路线（只定位当前可达侧）");
                            route.Leaf = new(spec.Code, point.Label, -1, 0, 31, null, InteractionId:point.Id); result.Children.Add(route);
                            Back(result, page);
                        }
                        Back(page, task);
                    }
                }
                else if (spec.Handler == 0) {
                    var notice=Node("尚未接入："+spec.Title);notice.Leaf=new(spec.Code,spec.Title,-1,0,0,"此任务尚未实现");task.Children.Add(notice);
                }
                else Target("确认操作：" + spec.Parameters, spec.Handler, null);
                Back(task, parent);
            }
            if (controlMode) {
                root.Label = "接管机器人指令（中键保存目标）";
                var page = Node("物资／普通门（当前接管机器人）"); root.Children.Add(page);
                foreach (var spec in CaptainCommandCatalog.Groups[0].Commands)
                    if (spec.Code is "W01" or "W08") Task(page, spec);
                Back(page, root);
                var combatPage = Node("战斗（当前接管机器人）"); root.Children.Add(combatPage);
                foreach(var spec in CaptainCommandCatalog.Groups[4].Commands) if(spec.Code is "B08" or "B01") Task(combatPage,spec);
                Back(combatPage,root);
                var actionPage=Node("接管退出");root.Children.Add(actionPage);
                Task(actionPage,new CaptainCommandSpec("A_CONTROL_STATION","结束接管并前往标记点驻守",position?.Label ?? "请先中键标记有效地面位置",55));
                Task(actionPage,new CaptainCommandSpec("A_CONTROL_HERE","结束接管并原地驻守","当前位置；接单成功后归还本体",56));
                Back(actionPage,root);return root;
            }
            foreach (var group in CaptainCommandCatalog.Groups) {
                var page = Node(group.Title); root.Children.Add(page);
                foreach (var command in group.Commands) Task(page, command);
                if (group.Commands.Length > 0) Back(page, root);
            }
            var shortcuts = Node("常用任务"); root.Children.Add(shortcuts);
            foreach (var code in new[] { "A10", "A11", "A12", "A08" })
                foreach (var group in CaptainCommandCatalog.Groups)
                    foreach (var spec in group.Commands) if (spec.Code == code) Task(shortcuts, spec);
            Back(shortcuts, root);
            return root;
        }
    }
}




