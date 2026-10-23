using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class AutoPickupAdapter
    {
        sealed class Visit
        {
            public ItemInLevel Item;
            public bool Near, Seen, Handled, Reserved, Approved;
            public float LeftAt, RetryAt, DiagnosticAt;
            public string LastReason;
            public int Failures;
        }
        static ConfigEntry<bool> restrict, ask;
        static ConfigEntry<float> timeout, visitRadius, searchRadius;
        static readonly Dictionary<IntPtr, Visit> visits = new();
        static readonly PickupPromptQueue<Visit> queue = new();
        static readonly PickupPromptClock clock = new();
        static readonly PlayerAgent[] humans = new PlayerAgent[4];
        static int cursor, answer, shownFrame;
        static float lastPromptTick;
        static bool hudReady, hudShown;
        static Visit prompt;
        static PlayerAIBot assigned;
        public static bool Restrict => restrict?.Value == true;
        public static bool CanAnswer
        {
            get
            {
                var local = PlayerManager.GetLocalPlayerAgent();
                return SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel
                    && local != null && local.Alive && local.Locomotion != null
                    && (int)local.Locomotion.m_currentStateEnum >= 0 && (int)local.Locomotion.m_currentStateEnum <= 5 && !PlayerChatManager.InChatMode;
            }
        }
        static bool ReplyInput => CanAnswer && Application.isFocused && InputMapper.PlayerHasFocus
            && FocusStateManager.CurrentState == eFocusState.FPS && !CaptainMenuInput.OwnsInput && !ItemTaskAdapter.CommunicationOpen;
        public static void Configure(ConfigFile file)
        {
            restrict = file.Bind("自动拾取", "仅收集玩家已访问物资", true, "不主动开物资箱。玩家到过可见物品旁且离开后才允许自动收集；明确指定拾取不受访问限制。关闭恢复原版自动行为。");
            ask = file.Bind("自动拾取", "安全时询问", true, "HUD 单项排队询问 Y/N，显示剩余秒数与进度条；危险时收集已访问物资。倒计时持续运行；聊天、终端、菜单或失去焦点时不读取答复。不占用鼠标或禁止正常游戏操作。");
            timeout = file.Bind("自动拾取", "询问超时秒数", 8f, "询问倒计时不暂停，超时默认同意，范围 1–60 秒；没有执行者时延后并处理下一项，不抢占扫描。HUD 从未显示则延后而非暗中同意。N 拒绝后本次访问不再询问。");
            visitRadius = file.Bind("自动拾取", "玩家访问距离", 2.5f, "真人到达已打开、可见物品旁才登记访问，范围 1–5 米。");
            searchRadius = file.Bind("自动拾取", "机器人收集距离", 15f, "只选择同区域、同层、同维度且空闲的机器人，范围 3–30 米；自动任务行走，不传送抢占扫描。");
        }
        static float Value(ConfigEntry<float> entry, float fallback, float min, float max)
        { var x = entry?.Value ?? fallback; return float.IsNaN(x) || float.IsInfinity(x) ? fallback : Math.Clamp(x, min, max); }
        internal static bool Open(ItemInLevel item)
            => item.container == null || item.container.m_core != null && item.container.m_core.Status == eResourceContainerStatus.Open;
        static bool Valid(ItemInLevel item)
            => item != null && item.gameObject != null && item.gameObject.activeInHierarchy && item.ItemDataBlock != null
                && ItemTaskPolicy.SupportedSlot((int)item.Get_pItemData().slot) && Open(item)
                && item.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>()?.GetCurrentState().status == ePickupItemStatus.PlacedInLevel;
        static bool SameZone(PlayerAgent agent, ItemInLevel item) => ItemRegion.SameZone(agent, item);
        static bool HumanNear(ItemInLevel item, float radius)
        {
            foreach (var human in humans)
            {
                if (human == null || !SameZone(human, item) || Vector3.Distance(human.Position, ItemInteractionGeometry.Point(item)) > radius) continue;
                if (ItemInteractionGeometry.Visible(human.EyePosition, item)) return true;
            }
            return false;
        }
        static PlayerAIBot Choose(ItemInLevel item, out string reason)
        {
            PlayerAIBot selected = null; float nearest = Value(searchRadius, 15, 3, 30);
            var rejected = new List<string>(4);
            for (int i = 0; i < 4; i++)
            {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(i);
                if (agent?.Owner?.IsBot != true) continue;
                string blocked = null;
                try
                {
                    var bot = agent.GetComponent<PlayerAIBot>();
                    float distance = Vector3.Distance(agent.Position, item.transform.position);
                    if (!agent.Alive) blocked = "机器人已倒地或死亡";
                    else if (!SameZone(agent, item)) blocked = "不在物品当前区域／分层／维度";
                    else if (bot == null) blocked = "机器人组件尚未就绪";
                    else if (distance > Value(searchRadius, 15, 3, 30)) blocked = "超出自动收集距离（" + distance.ToString("F1") + "米）";
                    else if ((blocked = ItemTaskAdapter.BusyReason(bot)) != null) { }
                    else if (!BotTaskBridge.AutoAvailable(bot, out blocked)) { }
                    else
                    {
                        var session = new ItemTaskSession(bot, ItemTaskKind.Pickup, item.Get_pItemData().slot, item, null, "自动拾取", () => { }, false);
                        blocked = session.Validate();
                        if (blocked == null && distance <= nearest) { selected = bot; nearest = distance; }
                    }
                }
                catch (Exception error) { blocked = "候选检查异常：" + error.GetBaseException().Message; }
                if (blocked != null) rejected.Add(agent.PlayerName + "：" + blocked);
            }
            reason = selected != null ? null : rejected.Count == 0 ? "当前没有机器人" : string.Join("；", rejected);
            return selected;
        }
        static void Defer(Visit visit, string reason, bool approved = false)
        {
            visit.Approved |= approved; visit.RetryAt = Time.time + 15;
            if (visit.LastReason != reason || Time.unscaledTime >= visit.DiagnosticAt)
            {
                visit.LastReason = reason; visit.DiagnosticAt = Time.unscaledTime + 30;
                Plugin.Logger.LogInfo("[拾取询问延后] item=" + visit.Item.Pointer + " name=" + Name(visit.Item)
                    + " approved=" + visit.Approved + " retrySeconds=15 reason=" + reason);
            }
        }
        public static void InputTick()
        {
            if (prompt == null || !hudReady || !ReplyInput || Time.frameCount <= shownFrame) return;
            // Observe only Y/N; do not alter game input or take keyboard/mouse focus.
            if (Input.GetKeyDown(KeyCode.N)) answer = -1;
            else if (Input.GetKeyDown(KeyCode.Y)) answer = 1;
        }
        public static void Tick()
        {
            if (!Restrict) { Reset(); return; }
            for (int i = 0; i < 4; i++)
            {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(i);
                humans[i] = agent?.Owner?.IsBot == false && agent.Alive ? agent : null;
            }
            float now = Time.time, radius = Value(visitRadius, 2.5f, 1, 5);
            var snapshot = ItemWorldIndex.Items;
            int count = Math.Min(64, snapshot.Count);
            for (int i = 0; i < count; i++)
            {
                var item = snapshot[cursor++ % snapshot.Count]; if (!Valid(item)) { if (item != null) visits.Remove(item.Pointer); continue; }
                bool near = HumanNear(item, radius);
                if (!visits.TryGetValue(item.Pointer, out var visit))
                { if (!near) continue; visits[item.Pointer] = visit = new Visit { Item = item }; Plugin.Logger.LogInfo("[物资访问] item=" + item.Pointer + " name=" + Name(item)); }
                if (near)
                {
                    KnownSupplyIndex.Observe(item, SupplySource.PlayerVisit);
                    if (!visit.Near && !visit.Reserved) { visit.Handled = visit.Approved = false; visit.Failures = 0; visit.RetryAt = 0; }
                    visit.Near = visit.Seen = true; visit.LeftAt = now; continue;
                }
                if (HumanNear(item, radius + 1)) continue;
                visit.Near = false;
                if (!Eligible(visit, now) || ReferenceEquals(visit, prompt)) continue;
                queue.Enqueue(visit);
            }
            if (prompt != null)
            {
                if (!Eligible(prompt, now) || !Valid(prompt.Item)) ClearPrompt();
                else
                {
                    bool near = HumanNear(prompt.Item, radius + 1);
                    bool available = assigned != null && assigned.Agent?.Owner?.IsBot == true && assigned.Agent.Alive
                        && SameZone(assigned.Agent, prompt.Item) && !ItemTaskAdapter.Busy(assigned) && BotTaskBridge.AutoAvailable(assigned);
                    string blocked = near ? "玩家已回到物品旁，不争抢物品" : "执行者暂时不可用";
                    if (!available && !near) { assigned = Choose(prompt.Item, out blocked); available = assigned != null; }
                    bool unsafeNow = available && BotTaskBridge.Unsafe(assigned);
                    bool allowed = ReplyInput && hudReady;
                    clock.Advance(Time.unscaledTime - lastPromptTick); lastPromptTick = Time.unscaledTime;
                    var decision = AutoPickupPolicy.Decide(true, near, available, unsafeNow, ask.Value, true, allowed, answer, clock.Expired, hudShown);
                    if (decision == PickupDecision.Take) { Take(prompt, assigned); ClearPrompt(); }
                    else if (decision == PickupDecision.Decline)
                    { prompt.Handled = true; ItemTaskAdapter.Feedback("已拒绝此次自动拾取；重新走近并离开后才会再次询问"); ClearPrompt(); }
                    else if (decision == PickupDecision.Defer)
                    {
                        Defer(prompt, !hudShown ? "询问HUD未显示，不默认授权拾取" : blocked, hudShown && (clock.Expired || allowed && answer > 0));
                        ClearPrompt();
                    }
                    else
                    {
                        hudReady = PickupPromptHud.Show(available ? assigned.Agent.PlayerName : "执行者暂时不可用", Name(prompt.Item), clock.Remaining, clock.Duration, queue.Count, allowed);
                        hudShown |= hudReady;
                        if (!allowed) answer = 0;
                    }
                }
            }
            if (prompt == null)
            {
                int budget = queue.Count;
                while (budget-- > 0)
                {
                    var visit = queue.Dequeue();
                    if (!Eligible(visit, now) || !Valid(visit.Item)) continue;
                    if (HumanNear(visit.Item, radius + 1)) { queue.Enqueue(visit); continue; }
                    var bot = Choose(visit.Item, out var blocked);
                    if (bot == null) { Defer(visit, blocked); continue; }
                    if (visit.Approved || BotTaskBridge.Unsafe(bot) || ask?.Value != true) { Take(visit, bot); continue; }
                    prompt = visit; assigned = bot; answer = 0; shownFrame = Time.frameCount; hudShown = false;
                    clock.Begin(Value(timeout, 8, 1, 60)); lastPromptTick = Time.unscaledTime;
                    hudReady = PickupPromptHud.Show(bot.Agent.PlayerName, Name(visit.Item), clock.Remaining, clock.Duration, queue.Count, ReplyInput);
                    hudShown |= hudReady;
                    Plugin.Logger.LogInfo("[拾取询问] bot=" + bot?.Pointer + " item=" + visit.Item.Pointer + " name=" + Name(visit.Item) + " queued=" + queue.Count);
                    break;
                }
            }
        }
        static bool Eligible(Visit visit, float now) => visit != null && visit.Seen && !visit.Handled && !visit.Reserved
            && now >= visit.RetryAt && now - visit.LeftAt >= 1.5f && visits.TryGetValue(visit.Item.Pointer, out var live) && ReferenceEquals(live, visit);
        static string Name(ItemInLevel item)
        { var text = Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName; return string.IsNullOrWhiteSpace(text) ? "指定物品" : text; }
        static void Take(Visit visit, PlayerAIBot bot)
        {
            visit.Reserved = true;
            bool resolved = false;
            void Result(bool success)
            {
                if (resolved) return; resolved = true;
                visit.Reserved = false;
                if (success) visit.Handled = true;
                else { visit.Failures++; visit.RetryAt = Time.time + 15 * visit.Failures; visit.Handled = visit.Failures >= 3; }
            }
            try
            {
                if (!ItemTaskAdapter.Start(bot, ItemTaskKind.Pickup, visit.Item.Get_pItemData().slot, visit.Item, null, "收集已访问物资", false, Result)) Result(false);
            }
            catch { Result(false); throw; }
        }
        static void ClearPrompt() { prompt = null; assigned = null; answer = 0; hudReady = hudShown = false; PickupPromptHud.Hide(); }
        public static void CancelPrompt(PlayerAIBot bot, ItemInLevel target = null)
        {
            if (prompt != null && (bot != null && assigned?.Pointer == bot.Pointer || target != null && prompt.Item.Pointer == target.Pointer))
            { prompt.RetryAt = Time.time + 5; ClearPrompt(); }
        }
        internal static void Forget(IntPtr key) { if (prompt?.Item?.Pointer == key) ClearPrompt(); visits.Remove(key); }
        public static void Reset()
        { visits.Clear(); queue.Clear(); cursor = 0; ClearPrompt(); PickupPromptHud.Reset(); }
    }
}
