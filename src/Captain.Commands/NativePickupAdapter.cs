using System;
using System.Collections.Generic;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    // The native command path already owns scan interruption, travel, teleport,
    // collecting/equipping and returning. Do not acquire a second external lease.
    internal static class NativePickupAdapter
    {
        sealed class Job
        {
            public PlayerAIBot Bot;
            public ItemInLevel Item;
            public PlayerBotActionBase.Descriptor Desc;
            public IntPtr Owner, Floor;
            public InventorySlot Slot;
            public uint Id;
            public int ReportSlot;
            public string BotName, ItemName;
            public float Created, EndedAt, StopAt;
            public bool Cancelled, Returning, Stopping;
            public string Stage;
            public readonly object Report = new();
        }
        static readonly Dictionary<IntPtr, Job> active = new();
        // A successful persistent carry action must retain permission for its
        // own Collect child even after the Captain one-shot task has completed.
        static readonly Dictionary<IntPtr, Job> carrying = new();
        static readonly Dictionary<IntPtr, (PlayerBotActionBase.Descriptor Desc, IntPtr Owner, float Until)> cancelled = new();
        static readonly List<Job> snapshot = new(4);
        static float warningAt;
        internal static int Count => active.Count;
        static bool Identity(Job job) => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel
            && Builder.CurrentFloor?.Pointer == job.Floor && job.Bot?.Agent?.Owner?.Pointer == job.Owner && job.Bot.Agent.Owner.IsBot;
        internal static bool Owns(PlayerAIBot bot) => bot != null && active.ContainsKey(bot.Pointer);
        internal static bool Reserved(ItemInLevel item)
        {
            if (item == null) return false;
            foreach (var job in active.Values) if (job.Item.Pointer == item.Pointer) return true;
            return false;
        }
        internal static string Status(PlayerAIBot bot) => bot != null && active.TryGetValue(bot.Pointer, out var job)
            ? "原版搬运／特殊拾取：" + (job.Stage ?? "等待原版动作") : null;
        static bool SameTarget(Job job, PlayerBotActionBase.Descriptor desc)
        {
            var carry = desc?.TryCast<PlayerBotActionCarryExpeditionItem.Descriptor>();
            var collect = desc?.TryCast<PlayerBotActionCollectItem.Descriptor>();
            return carry?.TargetItem?.Pointer == job.Item.Pointer || collect?.TargetItem?.Pointer == job.Item.Pointer;
        }
        internal static bool Authorizes(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (bot?.Agent?.Owner == null || desc == null) return false;
            if (!active.TryGetValue(bot.Pointer, out var job)) carrying.TryGetValue(bot.Pointer, out job);
            if (job == null || !Identity(job) || !SameTarget(job, desc)) return false;
            if (desc.Pointer == job.Desc?.Pointer) return !job.Stopping;
            var parent = desc.ParentActionBase;
            for (int depth = 0; parent != null && depth < 8; depth++) {
                var carry = parent.TryCast<PlayerBotActionCarryExpeditionItem>();
                if (carry?.m_desc?.TargetItem?.Pointer == job.Item.Pointer && parent.m_bot?.Pointer == bot.Pointer
                    && !carry.m_desc.IsTerminated()) return true;
                parent = parent.DescBase?.ParentActionBase;
            }
            return false; // An unrelated Collect of the same item is not authorized.
        }
        internal static void Install(Harmony patches)
        {
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction", new[] { typeof(PlayerBotActionBase.Descriptor) }),
                prefix: new HarmonyMethod(typeof(NativePickupAdapter), nameof(Request)) { priority = Priority.First + 10 });
        }
        static bool Request(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0, ref bool __result)
        {
            if (!SNet.IsMaster || __0 == null) return true;
            if (cancelled.TryGetValue(__0.Pointer, out var stop) && __instance?.Agent?.Owner?.Pointer == stop.Owner
                && Time.time <= stop.Until) { __result = false; return false; }
            if (__instance != null && active.TryGetValue(__instance.Pointer, out var job) && Identity(job)
                && !job.Stopping && SameTarget(job, __0) && __0.ParentActionBase == null
                && __0.Pointer != job.Desc.Pointer && job.Desc.IsTerminated()) {
                // AI can replace a descriptor terminated by native warp cleanup.
                // Track the actual replacement, never reset old completion status.
                job.Desc = __0;
            }
            return true;
        }
        internal static string Capability(PlayerAIBot bot, ItemInLevel item)
        {
            var reason = SpecialItemAccess.Reason(item); if (reason != null) return reason;
            if (bot?.Agent?.Alive != true || bot.Agent.Owner?.IsBot != true) return "机器人倒地或网络身份未就绪";
            if (!PlayerBackpackManager.TryGetBackpack(bot.Agent.Owner, out var bag) || bag == null) return "机器人背包尚未加载";
            var slot = item.Get_pItemData().slot;
            if (slot == InventorySlot.InPocket) {
                if (bag.HasPocketItem(item.Get_pItemData().itemID_gearCRC)) return "机器人口袋已有此类任务物品，不重复请求";
            } else if (bag.TryGetBackpackItem(slot, out var held) && held?.Instance != null) return "对应特殊槽位已经占用，不自动丢弃、替换或堆叠";
            return null;
        }
        internal static bool Issue(PlayerAIBot bot, ItemInLevel item, PUI_CommunicationMenu menu)
        {
            void Reject(string reason) => ItemTaskAdapter.Diagnose(bot, item, item?.Get_pItemData().slot ?? InventorySlot.None,
                "任务／特殊拿取", "原版搬运接单", reason);
            if (!SNet.IsMaster || GameStateManager.CurrentStateName != eGameStateName.InLevel || Builder.CurrentFloor == null
                || bot?.Agent?.Owner?.IsBot != true || menu == null) { Reject("仅关卡内房主可以下达原版搬运；通讯界面与机器人必须已就绪"); return false; }
            string reason = ItemTaskAdapter.BusyReason(bot);
            if (reason != null || ItemTaskAdapter.Reserved(item)) { Reject(reason ?? "物品已被另一任务预约"); return false; }
            reason = Capability(bot, item); if (reason != null) { Reject(reason); return false; }
            var source = PlayerManager.GetLocalPlayerAgent();
            if (!ItemRegion.TryResolve(item, out var node) || !ItemRegion.SameSpace(source, node) || !ItemRegion.SameSpace(bot.Agent, node)
                || !SearchTraversal.InScope(node, source?.CourseNode, 1)
                || !SearchTraversal.Reachable(source, source.CourseNode, source.CourseNode, 1, out _).Contains(node.Pointer.ToInt64())
                || !SearchTraversal.Reachable(bot.Agent, bot.Agent.CourseNode, null, 1, out _).Contains(node.Pointer.ToInt64())) {
                Reject("物品不在当前Zone普通门可达范围，或机器人无法从当前侧到达；不跨扫描／钥匙／任务封锁门"); return false;
            }
            var carry = SpecialItemAccess.Carry(item);
            PlayerBotActionBase.Descriptor descriptor;
            if (carry != null) {
                float hold = menu.GetItemHoldPrio(carry, out bool idleDrop);
                if (!float.IsFinite(hold)) { Reject("原版物品保持优先级无效"); return false; }
                if (!PlayerBotActionCarryExpeditionItem.Descriptor.Evaluate(bot, carry, hold, idleDrop)) { Reject("原版搬运 Evaluate 拒绝：目标、预约或携带能力不满足"); return false; }
                var desc = new PlayerBotActionCarryExpeditionItem.Descriptor(bot) { TargetItem = carry, HoldPrio = hold, AllowIdleDrop = idleDrop };
                descriptor = desc;
            } else descriptor = new PlayerBotActionCollectItem.Descriptor(bot) {
                TargetItem = item, TargetContainer = null, TargetPosition = item.transform.position, Haste = .5f };
            descriptor.Prio = Math.Max(RootPlayerBotAction.m_prioSettings.CollectItem, RootPlayerBotAction.m_prioSettings.UseBioscan + PlayerBotActionBase.Descriptor.s_minAbortPrioDiff + .1f);
            var job = new Job { Bot = bot, Item = item, Desc = descriptor, Owner = bot.Agent.Owner.Pointer,
                Floor = Builder.CurrentFloor.Pointer, Slot = item.Get_pItemData().slot, Id = item.Get_pItemData().itemID_gearCRC,
                Created = Time.time, ReportSlot = bot.Agent.PlayerSlotIndex, BotName = bot.Agent.PlayerName,
                ItemName = Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName };
            active.Add(bot.Pointer, job);
            AutoPickupAdapter.CancelPrompt(bot, item);
            Observe(job, TaskBoardState.Waiting, "等待原版动作", "已固定物品身份；提交一次原版搬运／拾取命令，接受不代表成功");
            try {
                if (!menu.RequestBotAction(bot, descriptor)) { Stop(job); Finish(job, false, "原版 RequestBotAction 拒绝，本次未拾取"); return false; }
                return true;
            } catch { Stop(job); Finish(job, false, "原版搬运请求异常，已撤销本次任务"); throw; }
        }
        internal static bool BeforeCommand(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (bot == null || !active.TryGetValue(bot.Pointer, out var job)) return true;
            if (Identity(job) && !job.Stopping && desc?.Pointer == job.Desc.Pointer) return true;
            if (Identity(job) && !job.Stopping && job.Desc.IsTerminated() && SameTarget(job, desc)) { job.Desc = desc; return true; }
            Cancel(bot, false);
            if (Owns(bot)) ItemTaskAdapter.Feedback("原版搬运／拾取仍在交互或取消核验，请结束后重新下达命令");
            return !Owns(bot);
        }
        static bool Atomic(Job job)
        {
            var action = job.Desc?.ActionBase;
            var collect = action?.TryCast<PlayerBotActionCollectItem>()
                ?? action?.TryCast<PlayerBotActionCarryExpeditionItem>()?.m_collectAction?.ActionBase?.TryCast<PlayerBotActionCollectItem>();
            return collect?.m_state == PlayerBotActionCollectItem.State.Transfer;
        }
        static void Stop(Job job)
        {
            if (!Identity(job) || job.Stopping || Atomic(job)) return;
            job.Stopping = true;
            job.StopAt = Time.time;
            if (cancelled.Count >= 64) cancelled.Clear();
            cancelled[job.Desc.Pointer] = (job.Desc, job.Owner, Time.time + 180);
            if (!job.Desc.IsTerminated()) {
                job.Bot.StopAction(job.Desc);
                // A command deferred before action creation still needs its
                // original abort event, so AI clears its own deferred state.
                if (!job.Desc.IsTerminated() && job.Desc.ActionBase == null) job.Desc.OnAborted();
            }
        }
        internal static void Cancel(PlayerAIBot bot, bool returning)
        {
            if (bot == null || !active.TryGetValue(bot.Pointer, out var job)) return;
            job.Cancelled = true; job.Returning |= returning; Step(job);
        }
        static bool Verified(Job job)
        {
            var carry = SpecialItemAccess.Carry(job.Item);
            if (carry != null) return carry.PickupItemStatus == ePickupItemStatus.PickedUp
                && carry.PickedUpByPlayer?.Pointer == job.Bot.Agent.Pointer;
            var sync = job.Item.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>();
            if (sync == null) return false;
            var state = sync.GetCurrentState();
            if (state.status != ePickupItemStatus.PickedUp || !state.pPlayer.TryGetPlayer(out var picker) || picker?.Pointer != job.Owner) return false;
            if (!PlayerBackpackManager.TryGetBackpack(job.Bot.Agent.Owner, out var bag)) return false;
            if (job.Slot == InventorySlot.InPocket) return bag.HasPocketItem(job.Id);
            return bag.TryGetBackpackItem(job.Slot, out var held) && held?.Instance != null
                && held.Instance.Get_pItemData().itemID_gearCRC == job.Id
                && PlayerBackpackManager.TryGetItemInLevelFromItemData(held.Instance.Get_pItemData(), out var world)
                && world?.Pointer == job.Item.Pointer;
        }
        static void Observe(Job job, TaskBoardState state, string stage, string reason)
        {
            job.Stage = stage;
            TaskBoard.Journal.Record(job.Report, job.ReportSlot, job.Owner.ToInt64(), job.BotName,
                "任务／特殊拿取", job.ItemName, state, stage, reason, Time.time);
        }
        static void Finish(Job job, bool success, string reason)
        {
            job.EndedAt = Time.time; active.Remove(job.Bot.Pointer);
            if (success && SpecialItemAccess.Carry(job.Item) != null && !job.Desc.IsTerminated()) carrying[job.Bot.Pointer] = job;
            Observe(job, job.Cancelled ? TaskBoardState.Cancelled : success ? TaskBoardState.Complete : TaskBoardState.Failed,
                job.Cancelled ? "已取消" : success ? "携带已核验" : "失败", reason);
            ItemTaskAdapter.Feedback(job.BotName + "：" + reason);
            if (job.Returning && !success && Identity(job)) ItemTaskAdapter.ReturnAfterSupply(job.Bot);
        }
        static void Step(Job job)
        {
            if (!Identity(job) || job.Bot.Agent.Alive != true) { Finish(job, false, "机器人身份、房主或关卡已变化，释放本次预约"); return; }
            if (Verified(job)) { Finish(job, true, "指定物品已由该机器人实际携带；后续保持／返程由原版与AI仲裁管理"); return; }
            if (job.Cancelled || Time.time - job.Created > 120) {
                if (Atomic(job)) { Observe(job, TaskBoardState.Executing, "交互收尾", "取消／超时已登记，等待原版转移结束后核验"); return; }
                Stop(job);
                if (job.Desc.IsTerminated()) Finish(job, false, job.Cancelled ? "搬运已取消，未强制放下物品" : "原版搬运超时，已停止本次请求");
                else if (job.Stopping && Time.time - job.StopAt > 2) Finish(job, false, "已提交停止但原版未确认结束；释放队长预约，未伪造拾取／动作完成");
                return;
            }
            if (!SpecialItemAccess.Placed(job.Item)) { Stop(job); Finish(job, false, "物品已被他人拿走或交付，本次不算成功"); return; }
            // AI may recreate a terminated command after warp; allow one second
            // for the existing broker replacement, never resubmit here.
            if (job.Desc.IsTerminated()) {
                if (job.EndedAt == 0) job.EndedAt = Time.time;
                if (Time.time - job.EndedAt > 1) Finish(job, false, "原版搬运动作已结束，但未确认指定物品由该机器人持有");
                return;
            }
            job.EndedAt = 0;
            Observe(job, job.Desc.ActionBase == null ? TaskBoardState.Waiting : TaskBoardState.Executing,
                Atomic(job) ? "原版转移物品" : job.Desc.ActionBase == null ? "等待原版调度" : "原版接近／搬运", "由原版动作执行；不写库存、不重复提交、不触发交付接口");
        }
        internal static void Tick()
        {
            snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var job in snapshot) try { Step(job); }
                catch (Exception error) {
                    try { Stop(job); } catch (Exception stopError) { Plugin.Logger.LogWarning("特殊搬运异常清理：" + stopError.Message); }
                    Finish(job, false, "原版搬运核验异常，停止本次跟踪");
                    if (Time.time >= warningAt) { warningAt = Time.time + 10; Plugin.Logger.LogWarning("[特殊搬运] " + error); }
                }
            snapshot.Clear(); snapshot.AddRange(carrying.Values);
            foreach (var job in snapshot) try { if (!Identity(job) || job.Desc.IsTerminated()) carrying.Remove(job.Bot.Pointer); }
                catch { carrying.Remove(job.Bot.Pointer); }
            snapshot.Clear();
        }
        internal static void Reset()
        {
            snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var job in snapshot) try { if (Identity(job)) Stop(job); } catch (Exception error) { Plugin.Logger.LogWarning("特殊搬运清理：" + error.Message); }
            active.Clear(); carrying.Clear(); cancelled.Clear(); snapshot.Clear(); warningAt = 0;
        }
    }
}
