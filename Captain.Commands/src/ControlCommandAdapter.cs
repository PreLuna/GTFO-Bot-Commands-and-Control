using System;
using System.Collections.Generic;
using System.Linq;
using LevelGeneration;
using Player;
using UnityEngine;
using PreLuna.Captain.Control;
using HarmonyLib;
using System.Reflection;
using Enemies;
using SNetwork;
namespace PreLuna.Captain.Tasks
{
    internal sealed record ControlMenuSnapshot(long Ticket, BotControlTarget Bot, ItemInLevel Item, MarkedOpenTarget Open, float At,
        EnemyAgent Enemy = null, int EnemyIdentity = 0, MarkedPosition Station = null);
    // Separate child records preserve the original paused item/open/collection/movement task dictionaries.
    internal static class ControlCommandAdapter
    {
        sealed class Job
        {
            internal long Ticket;
            internal IntPtr Floor, Master;
            internal int AgentInstance, BotInstance;
            internal BotControlTarget Target;
            internal PlayerAIBot Bot;
            internal ItemTaskRunner Pickup;
            internal ItemTaskSession ItemSession;
            internal OpenTaskRunner Open;
            internal OpenTaskAdapter.Session OpenSession;
            internal IControlCombatTask Combat;
            internal readonly Dictionary<IntPtr, PlayerBotActionBase.Descriptor> Actions = new();
            internal Action Released;
            internal bool Detached;
            internal float Next, CleanupAt = -1, StopAt, DetachedAt;
            internal string Label;
            internal bool Finished => Combat?.Finished ?? Pickup?.Finished ?? Open?.Finished ?? true;
            internal bool Valid { get { try { return SNet.IsMaster && SNet.Master?.Pointer == Master
                && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor?.Pointer == Floor
                && Bot?.Agent?.Pointer.ToInt64() == Target.Agent && Bot.Agent.Owner?.Pointer.ToInt64() == Target.Owner && Bot.Agent.Owner.IsBot
                && !Bot.Agent.IsBeingDestroyed && !Bot.Agent.IsBeingDespawned && Bot.Agent.Alive
                && Bot.GetInstanceID() == BotInstance && Bot.Agent.GetComponent<PlayerAIBot>()?.Pointer == Bot.Pointer
                && PlayerManager.Current?.GetPlayerAgentInSlot(Target.Slot)?.Pointer == Bot.Agent.Pointer
                && PlayerManager.Current.GetPlayerAgentInSlot(Target.Slot).GetInstanceID() == AgentInstance;
            } catch { return false; } } }
            internal void Tick() { if (Combat != null) Combat.Tick(Time.time); else if (Pickup != null) Pickup.Tick(Time.time); else { Open.Tick(Time.time); OpenSession.Observe(Open.Stage); } }
            internal void Cancel() { if (Combat != null) Combat.Cancel(); else if (Pickup != null) Pickup.Cancel(false); else Open.Cancel(false); }
            internal void Abort(string reason) { if (Combat != null) Combat.Abort(reason); else if (Pickup != null) Pickup.Abort(reason); else Open.Abort(reason); }
            internal bool Authorizes(PlayerBotActionBase.Descriptor desc) => !Finished && (Combat?.Authorizes(desc) == true || ItemSession?.OurAction(desc) == true || OpenSession?.OurAction(desc) == true);
        }
        static readonly Dictionary<IntPtr, Job> jobs = new();
        static ControlMenuSnapshot mark;
        static IntPtr teleportBot;
        internal static void Install(Harmony harmony)
        {
            var guard = new HarmonyMethod(typeof(ControlCommandAdapter), nameof(Request)) { priority = Priority.First + 90 };
            harmony.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction"), prefix: guard);
            harmony.Patch(AccessTools.Method(typeof(PlayerAIBot), "StartAction"), prefix: new HarmonyMethod(typeof(ControlCommandAdapter), nameof(Start)) { priority = Priority.First + 90 });
            foreach (var method in typeof(RootPlayerBotAction).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name.StartsWith("UpdateAction", StringComparison.Ordinal) && m.ReturnType == typeof(void)))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(ControlCommandAdapter), nameof(Autonomous)));
        }
        static bool IsJob(PlayerAIBot bot) => bot?.Agent?.Owner != null && Owns(bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64());
        static bool Request(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0, ref bool __result)
        { if (!IsJob(__instance) || Authorizes(__instance, __0)) return true; __result = false; return false; }
        static bool Start(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0) => !IsJob(__instance) || Authorizes(__instance, __0);
        static bool Autonomous(RootPlayerBotAction __instance) => !IsJob(__instance?.m_bot);
        internal static bool HasState => jobs.Count != 0;
        static bool Match(Job job, long agent, long owner) => job.Target.Agent == agent && job.Target.Owner == owner && job.Valid;
        internal static bool Owns(long agent, long owner) => jobs.Values.Any(j => Match(j, agent, owner));
        internal static bool Busy(long ticket, out string label)
        {
            var job = jobs.Values.FirstOrDefault(j => j.Ticket == ticket);
            label = job == null ? null : job.Label + "·" + (job.Finished ? "等待动作停止" : job.Combat?.Phase ?? StageText(job.Pickup != null ? job.Pickup.Stage.ToString() : job.Open.Stage.ToString()));
            return job != null;
        }
        internal static bool AllowsAction(long agent, long owner, long descriptor)
        {
            if (descriptor == 0) return false;
            var job = jobs.Values.FirstOrDefault(j => Match(j, agent, owner));
            return job != null && Authorizes(job.Bot, new PlayerBotActionBase.Descriptor(new IntPtr(descriptor)));
        }
        internal static bool Authorizes(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (bot == null || desc == null || !jobs.TryGetValue(bot.Pointer, out var job) || !job.Valid || !job.Authorizes(desc)) return false;
            job.Actions[desc.Pointer] = desc; return true;
        }
        internal static bool AllowsTeleport(long agent, long owner)
            => jobs.TryGetValue(teleportBot, out var job) && !job.Detached && !job.Finished && Match(job, agent, owner);
        internal static bool Teleport(PlayerAIBot bot, Func<bool> call)
        {
            var previous = teleportBot;
            teleportBot = bot?.Pointer ?? IntPtr.Zero;
            try { return call(); } finally { teleportBot = previous; }
        }
        internal static bool Mark(long ticket, PlayerAIBot bot, BotControlRay ray, out string reason)
        {
            reason = ControlCommandPolicy.Reject(true, Busy(ticket, out _), true, true, 1);
            if (reason != null) return false;
            // A missed/invalid fresh click clears its previous target; never silently reuse an old body mark.
            mark = null;
            var origin = new Vector3(ray.X, ray.Y, ray.Z); var direction = new Vector3(ray.DX, ray.DY, ray.DZ);
            if (!ray.Finite || Vector3.Distance(origin, bot.Agent.EyePosition) > 2) { reason = "机器人镜头位置已改变，请等待画面刷新后重新标记"; return false; }
            if (!ItemTargetResolver.Ray(bot.Agent, origin, direction, out var hit, LayerManager.MASK_ENEMY_DAMAGABLE | LayerManager.MASK_ENEMY) || hit.collider == null) { reason = "机器人镜头未命中可标记对象"; return false; }
            var item = ItemTargetResolver.Resolve(bot.Agent, hit);
            if (item != null && (!ItemTaskPolicy.SupportedSlot((int)item.Get_pItemData().slot)
                || !SpecialItemAccess.Placed(item) || !ItemInteractionGeometry.Visible(origin, item))) item = null;
            var open = MarkedOpenTarget.Resolve(bot.Agent, hit.collider.gameObject);
            var enemy = SingleKillAdapter.Resolve(bot.Agent, hit);
            var target = new BotControlTarget(bot.Agent.PlayerSlotIndex, bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64());
            mark = new(ticket, target, item, open, Time.time, enemy, enemy?.GetInstanceID() ?? 0);
            if (item != null) KnownSupplyIndex.Observe(item, SupplySource.Aimed);
            reason = item != null ? "已标记机器人镜头物品：" + (Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName)
                : open != null ? "已标记机器人镜头目标：" + open.Label + "（" + open.LockText + "）"
                : enemy != null ? "已标记机器人镜头敌人 #" + enemy.EnemyDataID + "；Q选择指定击杀／集火"
                : "该对象不支持当前接管标记指令";
            ItemTaskAdapter.Feedback(reason); return item != null || open != null || enemy != null;
        }
        internal static ControlMenuSnapshot Snapshot(long ticket, PlayerAIBot bot)
        {
            var target = new BotControlTarget(bot.Agent.PlayerSlotIndex, bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64());
            return mark?.Ticket == ticket && mark.Bot == target ? mark : new(ticket, target, null, null, Time.time);
        }
        internal static string MarkReason(ControlMenuSnapshot snapshot)
            => snapshot == null || !OpenTaskPolicy.Fresh(Time.time, snapshot.At, OpenTargetMarks.MarkLifetime)
                ? "机器人视角标记已过期，请重新中键标记" : null;
        internal static ControlMenuSnapshot MenuSnapshot(long ticket, PlayerAIBot bot)
            => Snapshot(ticket, bot) with { Station = PositionMarks.ControlHere(bot.Agent) };
        internal static string EnemyReason(ControlMenuSnapshot snapshot)
            => MarkReason(snapshot) ?? (snapshot?.Enemy?.Alive != true || snapshot.Enemy.GetInstanceID() != snapshot.EnemyIdentity ? "机器人镜头标记的敌人已死亡／身份失效，请重新中键标记" : null);
        internal static void Issue(ControlMenuSnapshot context, CaptainMenuLeaf entry)
        {
            PlayerAIBot bot = null;
            bool active = context != null && BotControlService.Instance.TryActive(context.Ticket, out bot);
            bool same = bot?.Agent?.Pointer.ToInt64() == context?.Bot.Agent && bot?.Agent?.Owner?.Pointer.ToInt64() == context?.Bot.Owner
                && entry.Slot == context?.Bot.Slot && entry.Owner == context?.Bot.Owner;
            string reason = ControlCommandPolicy.Reject(active, context != null && Busy(context.Ticket, out _), active, same, entry.Handler)
                ?? entry.Blocked ?? (entry.Handler == 55 ? null : MarkReason(context));
            if (reason != null) { ItemTaskAdapter.Feedback(reason); return; }
            if (entry.Handler == 55) {
                if (!BotControlService.Instance.ExitStation(context.Ticket, context.Station, out reason)) ItemTaskAdapter.Feedback(reason);
                else ItemTaskAdapter.Feedback("已退出并驻守此地；驻守由原指令系统管理，不召回本体");
                return;
            }
            var permit = BotControlService.Instance.CommandPermit(context.Ticket, entry.Handler == 43);
            if (permit == null) { ItemTaskAdapter.Feedback("接管权限已失效"); return; }
            var job = new Job { Ticket = context.Ticket, Bot = bot, Target = context.Bot,
                Floor = Builder.CurrentFloor.Pointer, Master = SNet.Master.Pointer, AgentInstance = bot.Agent.GetInstanceID(), BotInstance = bot.GetInstanceID() };
            if (entry.Handler == 1)
            {
                var item = context.Item;
                if (item?.ItemDataBlock == null || !ItemTaskPolicy.SupportedSlot((int)item.Get_pItemData().slot)) { ItemTaskAdapter.Feedback("请从机器人镜头中键标记资源包／普通消耗品"); return; }
                if (!ItemTaskAdapter.ReserveControlItem(item, out var cleanup)) { ItemTaskAdapter.Feedback("目标已被其他任务预约，旧暂停任务的预约也继续有效"); return; }
                job.Label = "拿取 " + (Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName);
                try {
                    job.ItemSession = new ItemTaskSession(bot, ItemTaskKind.Pickup, item.Get_pItemData().slot, item, null, job.Label, cleanup,
                        controlPermit: permit, onStatus: (stage, detail) => Record(job, stage.ToString(), stage >= ItemTaskStage.Complete, detail));
                    reason = job.ItemSession.Validate();
                    if (reason != null) { cleanup(); ItemTaskAdapter.Feedback(reason); return; }
                    job.Pickup = new ItemTaskRunner(ItemTaskKind.Pickup, job.ItemSession, Time.time, false);
                } catch { cleanup(); throw; }
            }
            else if (entry.Handler is 9 or 43)
            {
                reason = EnemyReason(context);
                if (reason != null) { ItemTaskAdapter.Feedback(reason); return; }
                job.Label = (entry.Handler == 9 ? "指定击杀" : "集火") + "敌人 #" + context.Enemy.EnemyDataID;
                job.Combat = entry.Handler == 9 ? SingleKillAdapter.CreateControlled(bot, context.Enemy, permit, out reason)
                    : FocusOrders.CreateControlled(bot, context.Enemy, permit, out reason);
                if (job.Combat == null) { ItemTaskAdapter.Feedback(reason); return; }
            }
            else
            {
                reason = OpenTargetMarks.RejectReason(context.Open, bot.Agent);
                if (reason == null && OpenTaskAdapter.Reserved(context.Open.Key)) reason = "该箱柜／门已有任务处理（包括暂停任务）";
                if (reason != null) { ItemTaskAdapter.Feedback(reason); return; }
                job.Label = context.Open.Label;
                job.OpenSession = new OpenTaskAdapter.Session(bot, context.Open, permit);
                reason = job.OpenSession.Restriction ?? job.OpenSession.CapabilityError;
                if (reason != null) { ItemTaskAdapter.Feedback(reason); return; }
                job.OpenSession.StatusObserver = (stage, detail) => Record(job, stage.ToString(), stage >= OpenTaskStage.Complete, detail);
                job.Open = new OpenTaskRunner(job.OpenSession, Time.time, false);
            }
            jobs.Add(bot.Pointer, job);
            AutoPickupAdapter.CancelPrompt(bot);
            ItemTaskAdapter.Feedback("接管指令已接单：" + job.Label + "；微调与重新标记暂停，旧任务仍保持暂停");
        }
        static void Record(Job job, string stage, bool finished, string detail)
            => TaskBoard.Journal.Record(job, job.Target.Slot, job.Target.Owner, job.Bot.Agent.PlayerName,
                "接管指令", job.Label, finished ? stage == "Complete" ? TaskBoardState.Complete : stage == "Cancelled" ? TaskBoardState.Cancelled : TaskBoardState.Failed
                    : TaskBoardState.Executing, StageText(stage), detail ?? "机器人镜头标记；沿用原有交互与结果核验", Time.time);
        static string StageText(string stage) => stage switch {
            "Waiting" => "等待接管权限", "Moving" => "接近目标", "Interacting" => "拾取交互", "Verifying" => "核验实际结果",
            "Unlocking" => "开锁与核验", "Opening" => "开启交互", "Paused" => "让位等待", "Returning" => "返回",
            "Complete" => "完成", "Failed" => "失败", "Cancelled" => "取消", _ => "执行中" };
        internal static bool Reserved(IntPtr key) => jobs.Values.Any(j => j.OpenSession?.Target.Key == key);
        internal static bool Detach(long ticket, Action release)
        {
            if (mark?.Ticket == ticket) mark = null;
            var job = jobs.Values.FirstOrDefault(j => j.Ticket == ticket);
            if (job == null) return false;
            if (!job.Detached) { job.Detached = true; job.DetachedAt = Time.unscaledTime; job.Released = release; job.Cancel(); }
            // Return body input immediately; the existing runner finishes any atomic native operation.
            return true;
        }
        internal static void Tick()
        {
            foreach (var job in jobs.Values.ToArray())
            {
                try {
                    if (!job.Valid) {
                        try { job.Abort("接管机器人／房主／关卡身份失效，停止旧指令；不召回替代对象"); }
                        finally { jobs.Remove(job.Bot.Pointer); job.Released?.Invoke(); }
                        continue;
                    }
                    if (Time.unscaledTime < job.Next) continue;
                    job.Next = Time.unscaledTime + .1f;
                    // A paused scaled game clock cannot keep a detached atomic child alive forever.
                    if (job.Detached && !job.Finished && Time.unscaledTime - job.DetachedAt >= 35)
                        job.Abort("退出后的交互收尾超过35秒，停止本任务动作并保留实际结果");
                    if (!job.Finished) job.Tick();
                    if (!job.Finished) continue;
                    if (job.CleanupAt < 0) job.CleanupAt = Time.unscaledTime;
                    var remaining = job.Valid ? job.Actions.Values.Where(d => !d.IsTerminated()).ToArray() : Array.Empty<PlayerBotActionBase.Descriptor>();
                    if (remaining.Length != 0) {
                        if (Time.unscaledTime >= job.StopAt) { job.StopAt = Time.unscaledTime + .25f; foreach (var desc in remaining) job.Bot.StopAction(desc); }
                        if (!job.Detached && Time.unscaledTime - job.CleanupAt > 5) BotControlService.Instance.Release(job.Ticket, true);
                        continue;
                    }
                    jobs.Remove(job.Bot.Pointer);
                    job.Released?.Invoke();
                } catch (Exception error) {
                    Plugin.Logger.LogWarning("[接管指令] 执行／收尾异常：" + error);
                    try { job.Abort("接管指令异常，保留已提交的真实状态"); }
                    finally { if (!job.Detached) BotControlService.Instance.Release(job.Ticket, true); }
                }
            }
        }
        internal static void Reset()
        {
            mark = null;
            try {
                foreach (var job in jobs.Values.ToArray()) {
                    try { job.Abort("关卡或房主重置"); }
                    catch (Exception error) { Plugin.Logger.LogWarning("[接管指令] 重置任务：" + error); }
                    finally { try { job.Released?.Invoke(); } catch (Exception error) { Plugin.Logger.LogWarning("[接管指令] 重置租约：" + error); } }
                }
            } finally { jobs.Clear(); teleportBot = IntPtr.Zero; }
        }
    }
}
