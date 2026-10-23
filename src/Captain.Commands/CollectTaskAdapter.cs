using System;
using System.Collections.Generic;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class CollectTaskAdapter
    {
        sealed record Entry(Session Session, CollectTaskRunner Runner);
        static readonly Dictionary<IntPtr, Entry> active = new();
        internal static bool HasState => active.Count != 0;
        internal static bool PausedWorkBlocks(PlayerAIBot bot) => Owns(bot)
            && !(active[bot.Pointer].Runner.ManualPaused && active[bot.Pointer].Session.BorrowYielded);
        internal static void SuspendForBorrow(PlayerAIBot bot, List<BorrowHold> holds)
        {
            if (!Owns(bot)) return;
            var entry = active[bot.Pointer]; bool changed = !entry.Runner.ManualPaused;
            if (changed) { entry.Runner.Pause(true); entry.Runner.Tick(Time.time); }
            int revision = entry.Runner.PauseRevision;
            bool Current() => active.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, entry) && !entry.Runner.Finished;
            holds.Add(new(() => !Current() || entry.Session.BorrowYielded,
                () => { if (changed && Current() && entry.Runner.PauseRevision == revision && entry.Runner.ManualPaused) entry.Runner.Pause(false); },
                () => Current() ? entry.Runner.PauseRevision == revision : !Owns(bot)));
        }
        internal static Action SuspendForRescue(PlayerAIBot bot)
        {
            if (!Owns(bot) || active[bot.Pointer].Runner.ManualPaused) return null;
            var entry = active[bot.Pointer]; entry.Runner.Pause(true); entry.Runner.Tick(Time.time);
            return () => { if (active.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, entry)) entry.Runner.Pause(false); };
        }
        internal static bool Owns(PlayerAIBot bot) => bot != null && active.TryGetValue(bot.Pointer, out var entry) && !entry.Runner.Finished;
        internal static string Status(PlayerAIBot bot) => Owns(bot) ? "按类型收集（已核验 " + active[bot.Pointer].Runner.Completed + " 件）" : null;
        internal static void Issue(PlayerAIBot bot, KnownSupplySnapshot snapshot, CaptainMenuLeaf command)
        {
            if (bot?.Agent?.Alive != true || snapshot == null || command.CollectScope < 0 || command.CollectScope >= snapshot.Scopes.Count
                || command.CollectType == 0 || !ItemTaskPolicy.SupportedSlot(command.CollectSlot) || command.CollectLimit < 1 || command.CollectLimit > 5) { ItemTaskAdapter.Feedback("收集上下文失效，请重新选择"); return; }
            if (ItemTaskAdapter.Busy(bot) || ResourceUseAdapter.Owns(bot) || SingleKillAdapter.Owns(bot)) { ItemTaskAdapter.Feedback("该机器人已有任务，请先取消或等待完成"); return; }
            var scope = snapshot.Scopes[command.CollectScope]; var ids = new List<int>(); string typeName = null;
            foreach (var result in scope.Results) if (result.Blocked == null && result.TypeId == command.CollectType && result.ItemSlot == command.CollectSlot) { ids.Add(result.Id); typeName ??= result.TypeName; }
            if (ids.Count == 0) { ItemTaskAdapter.Feedback("没有匹配的已知物资，请重开菜单"); return; }
            // Freeze membership and references. A later SM query cannot silently expand this task.
            var targets = new Dictionary<int, ItemInLevel>();
            foreach (int id in ids) if (snapshot.Targets.TryGetValue(id, out var item)) targets[id] = item;
            var regions = new Dictionary<int, (IntPtr Zone, IntPtr Room)>();
            foreach (int id in ids) if (snapshot.Regions.TryGetValue(id, out var region)) regions[id] = region;
            ids.Sort((a, b) => Distance(a).CompareTo(Distance(b)));
            float Distance(int id) => targets.TryGetValue(id, out var item) && item != null ? Vector3.Distance(bot.Agent.Position, item.transform.position) : float.MaxValue;
            var session = new Session(bot, targets, regions, command.CollectScope == 0, command.CollectType, command.CollectSlot, scope.Label + " / " + (typeName ?? "物品") + " #" + command.CollectType);
            active[bot.Pointer] = new(session, new CollectTaskRunner(session, ids, command.CollectLimit, Time.time));
            AutoPickupAdapter.CancelPrompt(bot);
        }
        internal static bool Cancel(PlayerAIBot bot, bool returning)
        {
            if (!Owns(bot)) return false;
            var entry = active[bot.Pointer]; entry.Runner.Cancel(returning); entry.Runner.Tick(Time.time); return true;
        }
        internal static bool Control(PlayerAIBot bot, bool pause)
        { if (!Owns(bot)) return false; var entry = active[bot.Pointer]; entry.Runner.Pause(pause); entry.Runner.Tick(Time.time); return true; }
        internal static void Tick()
        {
            if (active.Count == 0) return;
            var remove = new List<IntPtr>();
            foreach (var pair in active) {
                try { pair.Value.Runner.Tick(Time.time); }
                catch (Exception error) {
                    string reason = "收集执行异常：" + error;
                    ItemTaskAdapter.Feedback(reason);
                    try { pair.Value.Session.Dispose(reason); }
                    catch (Exception cleanupError) { ItemTaskAdapter.Feedback("收集清理失败：" + cleanupError); }
                    try { pair.Value.Runner.Abort(reason); }
                    catch (Exception reportError) { ItemTaskAdapter.Feedback("收集失败记录无法写入：" + reportError); }
                }
                if (pair.Value.Runner.Finished) remove.Add(pair.Key);
            }
            foreach (var key in remove) active.Remove(key);
        }
        internal static void Reset()
        {
            foreach (var entry in active.Values)
                try { entry.Session.Dispose("关卡／房主重置，停止收集"); }
                catch (Exception error) { ItemTaskAdapter.Feedback("收集重置清理失败：" + error); }
            active.Clear();
        }
        sealed class Session : ICollectTaskSession
        {
            readonly PlayerAIBot bot;
            readonly IntPtr owner, floor;
            readonly Dictionary<int, ItemInLevel> targets;
            readonly Dictionary<int, (IntPtr Zone, IntPtr Room)> regions;
            readonly bool roomOnly;
            readonly uint type;
            readonly InventorySlot slot;
            readonly string label, name;
            readonly int playerSlot;
            readonly ItemTaskLease lease;
            ItemTaskRunner current;
            bool confirmed, returning;
            string lastReport;
            public Session(PlayerAIBot bot, Dictionary<int, ItemInLevel> targets, Dictionary<int, (IntPtr Zone, IntPtr Room)> regions, bool roomOnly, uint type, int slot, string label)
            { this.bot = bot; this.targets = targets; this.regions = regions; this.roomOnly = roomOnly; this.type = type; this.slot = (InventorySlot)slot; this.label = label; owner = bot.Agent.Owner.Pointer; floor = Builder.CurrentFloor.Pointer; name = bot.Agent.PlayerName; playerSlot = bot.Agent.PlayerSlotIndex; lease = new(bot); }
            public bool Valid => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor?.Pointer == floor
                && bot?.Agent?.Owner?.Pointer == owner && bot.Agent.Owner.IsBot && bot.Agent.Alive;
            public bool ChildActive => current != null && !current.Finished;
            internal bool BorrowYielded => !lease.HasLease && (!ChildActive || current.ManuallySuspended);
            public bool ChildConfirmed => returning ? current?.Stage == ItemTaskStage.Complete : confirmed;
            public TaskBoardState ChildState => current?.Stage switch {
                ItemTaskStage.Waiting => TaskBoardState.Waiting, ItemTaskStage.Paused => TaskBoardState.Paused, _ => TaskBoardState.Executing };
            public string ChildStage => current?.Stage switch {
                ItemTaskStage.Waiting => "等待权限", ItemTaskStage.Moving => "接近物品", ItemTaskStage.Paused => "让出紧急任务", ItemTaskStage.Interacting => "交互中", ItemTaskStage.Verifying => "核验数量", ItemTaskStage.Returning => "返回中", _ => "等待子任务结束" };
            public CollectAttempt Begin(int id, out string reason)
            {
                confirmed = returning = false; current = null;
                if (!targets.TryGetValue(id, out var item) || !KnownSupplyIndex.Current(id, item)) { reason = "候选物品已失效／被拿走"; return CollectAttempt.Skip; }
                reason = KnownSupplyIndex.PickupReason(bot.Agent, item);
                if (reason != null) return CollectAttempt.Skip;
                if (!regions.TryGetValue(id, out var region) || !ItemRegion.TryResolve(item, out var node) || node.m_zone.Pointer != region.Zone
                    || roomOnly && node.m_area?.Pointer != region.Room) { reason = "物资已离开选定范围"; return CollectAttempt.Skip; }
                if (item.ItemDataBlock?.persistentID != type || item.Get_pItemData().slot != slot) { reason = "物品类型已经变化"; return CollectAttempt.Skip; }
                if (ItemTaskAdapter.Reserved(item)) { reason = "物品已被其他机器人预留"; return CollectAttempt.Skip; }
                var preflight = new ItemTaskSession(bot, ItemTaskKind.Pickup, slot, item, null, label, () => { });
                reason = preflight.Validate(); if (reason != null) return CollectAttempt.Stop;
                if (!lease.Heartbeat()) { reason = "整批控制令牌失效，停止队列"; return CollectAttempt.Stop; }
                if (!ItemTaskAdapter.Start(bot, ItemTaskKind.Pickup, slot, item, null, "按类型收集：" + label, true, success => confirmed = success, true, false, lease)) { reason = "无法登记本次拾取，保留背包并停止"; return CollectAttempt.Stop; }
                current = ItemTaskAdapter.CurrentTask(bot); return CollectAttempt.Started;
            }
            public bool BeginReturn(out string reason)
            {
                confirmed = false; returning = true;
                bool started = lease.Heartbeat() && ItemTaskAdapter.Start(bot, ItemTaskKind.Return, InventorySlot.None, null, null, "收集结束返回／恢复", true, null, true, true, lease);
                current = started ? ItemTaskAdapter.CurrentTask(bot) : null; reason = started ? null : "当前受保护动作阻止返程"; return started;
            }
            public void CancelChild() { if (ChildActive) { current.Cancel(false); current.Tick(Time.time); } }
            public void PauseChild(bool pause)
            { if (ChildActive) { current.Pause(pause); } else if (pause) lease.Suspend(); }
            public void Abort(string reason) { if (ChildActive) current.Abort(reason); }
            public void Dispose(string reason) { try { Abort(reason); } finally { lease.Release(ItemTaskStage.Failed); } }
            public void Notify(TaskBoardState state, string stage, string detail)
            {
                if (state is TaskBoardState.Complete or TaskBoardState.Failed or TaskBoardState.Cancelled)
                    lease.Release(state == TaskBoardState.Complete ? ItemTaskStage.Complete : state == TaskBoardState.Cancelled ? ItemTaskStage.Cancelled : ItemTaskStage.Failed);
                TaskBoard.Journal.Record(this, playerSlot, owner.ToInt64(), name, "按类型收集", label, state, stage, detail, Time.time);
                string report = state + "|" + stage + "|" + detail;
                if (report == lastReport) return; lastReport = report;
                ItemTaskAdapter.Feedback(name + "：" + label + " — " + stage + "；" + detail);
            }
        }
    }
}
