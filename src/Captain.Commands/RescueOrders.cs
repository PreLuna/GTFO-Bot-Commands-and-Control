using System;
using System.Collections.Generic;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class RescueOrders
    {
        sealed record Target(int Slot, IntPtr Agent, IntPtr Owner, string Name);
        sealed record Entry(Session Session, RescueRunner Runner);
        static readonly Dictionary<IntPtr, Entry> active = new();
        static readonly List<Entry> snapshot = new(3);
        static readonly List<Target> queued = new(3);
        static readonly Dictionary<IntPtr, object> queueTickets = new();
        static readonly HashSet<(IntPtr, IntPtr)> attempted = new();
        static IntPtr batchFloor;
        static float batchUntil, nextBatch;
        internal static bool HasState => active.Count != 0 || queued.Count != 0;
        static bool Host => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor != null;
        internal static bool Downed(PlayerAgent agent) => agent?.Owner != null && !agent.Alive
            && agent.Locomotion?.m_currentStateEnum == PlayerLocomotion.PLOC_State.Downed && agent.ReviveInteraction != null;
        static bool BotReady(PlayerAIBot bot) => Host && bot?.Agent?.Alive == true && bot.Agent.Owner?.IsBot == true
            && bot.SyncValues != null && bot.Agent.CourseNode != null && bot.Agent.Damage?.GrabbedAtAll != true;
        static PlayerAgent Current(Target target)
        {
            var current = PlayerManager.Current?.GetPlayerAgentInSlot(target.Slot);
            return current?.Pointer == target.Agent && current.Owner?.Pointer == target.Owner ? current : null;
        }
        static Target Capture(PlayerAgent agent) => new(agent.PlayerSlotIndex, agent.Pointer, agent.Owner.Pointer, agent.PlayerName);
        static RoutePlan RescueRoute(PlayerAIBot bot, PlayerAgent target, out string reason)
        {
            if (bot.Agent.CourseNode?.Pointer == target.CourseNode?.Pointer && Vector3.Distance(bot.Agent.Position, target.Position) <= 2) {
                reason = null;
                return new(new[] { bot.Agent.Position, target.Position }, target.CourseNode, null, false, Array.Empty<AIGraph.AIG_CoursePortal>(), new[] {target.CourseNode.Pointer.ToInt64()});
            }
            var route = RouteTraversal.Build(bot.Agent, target.CourseNode, target.Position, null, bot.Agent.CourseNode, out reason);
            if (route?.WaitingForDoor == true) { reason = "救援通路的普通门尚未开启，请先开门"; return null; }
            return route;
        }
        static void QueueReport(Target target, TaskBoardState state, string stage, string reason)
        {
            if (!queueTickets.TryGetValue(target.Agent, out var ticket)) return;
            TaskBoard.Journal.Record(ticket, -1, target.Owner.ToInt64(), "救援调度", "多人救援队列", target.Name, state, stage, reason, Time.time);
        }
        static void EndQueue(TaskBoardState state, string reason)
        {
            foreach (var target in queued) QueueReport(target, state, "队列结束", reason);
            queued.Clear(); queueTickets.Clear(); attempted.Clear();
        }
        internal static bool Owns(PlayerAIBot bot) => bot != null && active.ContainsKey(bot.Pointer);
        internal static string Status(PlayerAIBot bot) => bot != null && active.TryGetValue(bot.Pointer, out var entry)
            ? "救援 " + entry.Session.Target.Name + "·" + Text(entry.Runner.Stage) : null;
        static string Text(RescueStage stage) => stage switch { RescueStage.Waiting => "等待交互收尾", RescueStage.Approaching => "接近队友",
            RescueStage.Reviving => "救起／核验", RescueStage.Complete => "已救起", RescueStage.Cancelled => "取消", _ => "失败" };
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(RootPlayerBotAction), "UpdateActionReviveTeammate"),
                prefix: new HarmonyMethod(typeof(RescueOrders), nameof(BeforeSelection)),
                postfix: new HarmonyMethod(typeof(RescueOrders), nameof(AfterSelection)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction"),
                prefix: new HarmonyMethod(typeof(RescueOrders), nameof(Request)) { priority = Priority.First + 30 });
            harmony.Patch(AccessTools.Method(typeof(PlayerAIBot), "StartAction"),
                prefix: new HarmonyMethod(typeof(RescueOrders), nameof(Start)) { priority = Priority.First + 30 });
        }
        static bool Reserved(PlayerAgent target, PlayerAIBot except = null)
        {
            foreach (var entry in active.Values) if (entry.Session.Target.Agent == target.Pointer && entry.Session.Bot.Pointer != except?.Pointer) return true;
            for (int slot = 0; slot < 4; slot++) {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot); if (agent?.Owner?.IsBot != true) continue;
                var bot = agent.GetComponent<PlayerAIBot>(); if (bot == null || bot.Pointer == except?.Pointer) continue;
                var actions = bot.Actions; if (actions == null) continue;
                for (int i = 0; i < Math.Min(32, actions.Count); i++) {
                    var desc = actions[i]?.DescBase?.TryCast<PlayerBotActionRevive.Descriptor>();
                    if (desc != null && !desc.IsTerminated() && desc.Client?.Pointer == target.Pointer) return true;
                }
            }
            var interactors = target.ReviveInteraction?.m_interactors;
            if (interactors != null) for (int i = 0; i < Math.Min(4, interactors.Count); i++) {
                var agent = interactors[i]?.Agent;
                if (agent != null && agent.Pointer != except?.Agent?.Pointer) return true;
            }
            return false;
        }
        static string TargetReason(PlayerAIBot bot, PlayerAgent target)
        {
            if (!BotReady(bot)) return "机器人尚未就绪、倒地或被抓住";
            if (!Downed(target)) return "目标并非可救援的倒地队友";
            if (target.Pointer == bot.Agent.Pointer) return "机器人不能救援自己";
            var origin = PlayerManager.GetLocalPlayerAgent()?.CourseNode;
            if (!ItemRegion.SameSpace(bot.Agent, target.CourseNode) || !SearchTraversal.InScope(bot.Agent.CourseNode, origin, 1)
                || !SearchTraversal.InScope(target.CourseNode, origin, 1)) return "机器人与倒地队友必须在队长当前Zone的同层同维度";
            if (Reserved(target, bot)) return "队友已由其他机器人／玩家救援，避免重复抢救";
            return null;
        }
        internal static void Issue(PlayerAIBot bot, PlayerAgent target)
        {
            string reason = TargetReason(bot, target);
            if (reason != null) { ItemTaskAdapter.Feedback("救援拒绝：" + reason); return; }
            if (active.TryGetValue(bot.Pointer, out var prior)) {
                ItemTaskAdapter.Feedback(prior.Session.Target.Agent == target.Pointer ? "同一救援已登记，不重复发起" : "该机器人已有救援目标，请先取消"); return;
            }
            StartJob(bot, target, false);
        }
        static void StartJob(PlayerAIBot bot, PlayerAgent target, bool batch)
        {
            var session = new Session(bot, Capture(target), batch);
            var entry = new Entry(session, new RescueRunner(session, Time.time));
            active.Add(bot.Pointer, entry);
            session.Report(entry.Runner);
            ItemTaskAdapter.Feedback(bot.Agent.PlayerName + "：救援 " + target.PlayerName + " 已登记；现有原子交互先收尾");
        }
        internal static void AssignAll()
        {
            if (!Host) { ItemTaskAdapter.Feedback("多人救援仅由关卡内房主分配"); return; }
            if (queued.Count != 0) { ItemTaskAdapter.Feedback("多人救援分配仍在执行，不重复刷新队列；可在同一入口取消"); return; }
            attempted.Clear(); queueTickets.Clear(); batchFloor = Builder.CurrentFloor.Pointer; batchUntil = Time.time + 120; nextBatch = 0;
            var origin = PlayerManager.GetLocalPlayerAgent()?.CourseNode;
            for (int slot = 0; slot < 4; slot++) {
                var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if (Downed(agent) && SearchTraversal.InScope(agent.CourseNode, origin, 1)) {
                    var target = Capture(agent); queued.Add(target); queueTickets[target.Agent] = new object();
                    QueueReport(target, TaskBoardState.Queued, "等待分配", "仅冻结本次倒地队友名单；机器人不足、已有救援或交互尚未收尾时排队");
                }
            }
            if (queued.Count == 0) { ItemTaskAdapter.Feedback("当前Zone没有可分配救援的倒地队友"); return; }
            ItemTaskAdapter.Feedback("已登记 " + queued.Count + " 名倒地队友；分别分配，已有救援不抢占，机器人不足则排队");
            TickBatch();
        }
        static void TickBatch()
        {
            if (queued.Count == 0 || Time.time < nextBatch) return;
            nextBatch = Time.time + .5f;
            if (!Host || Builder.CurrentFloor.Pointer != batchFloor || Time.time >= batchUntil) {
                if (Host) ItemTaskAdapter.Feedback("多人救援等待已结束：未完成目标 " + queued.Count + "；已提交的救援继续有界收尾");
                EndQueue(TaskBoardState.Failed, "120秒等待期限或关卡身份已失效"); return;
            }
            for (int i = queued.Count - 1; i >= 0; i--) if (!Downed(Current(queued[i]))) {
                bool recovered = Current(queued[i])?.Alive == true;
                QueueReport(queued[i], recovered ? TaskBoardState.Complete : TaskBoardState.Failed,
                    recovered ? "已救起" : "目标失效", recovered ? "实际倒地状态已解除" : "离队／替换／非可救援状态");
                queueTickets.Remove(queued[i].Agent); queued.RemoveAt(i);
            }
            var edges = new List<RescueEdge>(12);
            var targets = new Dictionary<int, PlayerAgent>(); var bots = new Dictionary<int, PlayerAIBot>();
            foreach (var target in queued) {
                var agent = Current(target); if (agent == null) continue;
                if (Reserved(agent)) { QueueReport(target, TaskBoardState.Waiting, "已有救援", "已由玩家或机器人救援，不重复分配，等待真实结果"); continue; }
                QueueReport(target, TaskBoardState.Queued, "等待分配", "没有重复占用该目标；等待可用机器人或此前交互收尾");
                targets[target.Slot] = agent;
                for (int slot = 0; slot < 4; slot++) {
                    var source = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                    var bot = source?.Owner?.IsBot == true ? source.GetComponent<PlayerAIBot>() : null;
                    if (!BotReady(bot) || Owns(bot) || attempted.Contains((bot.Agent.Pointer, target.Agent)) || TargetReason(bot, agent) != null
                        || NativePickupAdapter.Owns(bot) || ResourceUseAdapter.Owns(bot) || SingleKillAdapter.Owns(bot) || NativeRevive(bot) != null) continue;
                    // Validate each pair before matching; a nearer bot behind a closed door
                    // must not reserve a target away from another bot with an open route.
                    var route = RescueRoute(bot, agent, out _);
                    if (route == null || !bot.TestDangerZones(agent.Position, AIDangerZone.SeverityEnum.Harmful, out _)) continue;
                    bots[slot] = bot;
                    float cost = ItemTaskAdapter.Busy(bot) ? 30 : 0;
                    for (int i = 1; i < route.Points.Length; i++) cost += Vector3.Distance(route.Points[i - 1], route.Points[i]);
                    edges.Add(new(slot, target.Slot, cost));
                }
            }
            foreach (var edge in RescueAllocation.Match(edges)) {
                var bot = bots[edge.Bot]; var target = targets[edge.Target]; attempted.Add((bot.Agent.Pointer, target.Pointer));
                StartJob(bot, target, true);
                foreach (var pending in queued) if (pending.Agent == target.Pointer) QueueReport(pending, TaskBoardState.Executing, "已分配", "执行人：" + bot.Agent.PlayerName + "；详见该机器人的救援阶段");
            }
        }
        static PlayerBotActionRevive.Descriptor NativeRevive(PlayerAIBot bot)
        {
            var actions = bot?.Actions; if (actions == null) return null;
            for (int i = 0; i < Math.Min(32, actions.Count); i++) {
                var desc = actions[i]?.DescBase?.TryCast<PlayerBotActionRevive.Descriptor>();
                if (desc != null && !desc.IsTerminated()) return desc;
            }
            return null;
        }
        internal static void Cancel(PlayerAIBot bot)
        {
            if (bot == null || !active.TryGetValue(bot.Pointer, out var entry)) return;
            if (queued.Exists(t => t.Agent == entry.Session.Target.Agent)) {
                QueueReport(entry.Session.Target, TaskBoardState.Cancelled, "已取消", "玩家取消该目标救援，不自动重新分配");
                queued.RemoveAll(t => t.Agent == entry.Session.Target.Agent); queueTickets.Remove(entry.Session.Target.Agent);
            }
            entry.Runner.Cancel(); entry.Runner.Tick(Time.time); entry.Session.Report(entry.Runner);
            ItemTaskAdapter.Feedback("救援取消已登记；已开始的救援交互最多12秒收尾");
        }
        internal static void CancelBatch()
        {
            EndQueue(TaskBoardState.Cancelled, "玩家取消多人救援队列"); snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var entry in snapshot) if (entry.Session.Batch) { entry.Runner.Cancel(); entry.Runner.Tick(Time.time); entry.Session.Report(entry.Runner); }
            snapshot.Clear(); ItemTaskAdapter.Feedback("已取消多人救援队列；已开始的救援交互先收尾");
        }
        internal static void Tick()
        {
            snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var entry in snapshot) try { entry.Runner.Tick(Time.time); entry.Session.Report(entry.Runner); }
                catch (Exception error) { Plugin.Logger.LogWarning("[救援诊断] " + error); entry.Runner.Abort("救援检查异常：" + error.GetBaseException().Message); }
            snapshot.Clear();
            try { TickBatch(); }
            catch (Exception error) { Plugin.Logger.LogWarning("[救援诊断] 分配失败：" + error); EndQueue(TaskBoardState.Failed, "分配检查异常：" + error.GetBaseException().Message); }
        }
        internal static void Reset()
        {
            EndQueue(TaskBoardState.Cancelled, "关卡／房主状态重置"); snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var entry in snapshot) try { entry.Runner.Abort("救援任务随关卡／房主状态重置"); }
                catch (Exception error) { Plugin.Logger.LogWarning("[救援诊断] 重置：" + error.Message); }
            active.Clear(); snapshot.Clear();
        }
        static bool BeforeSelection(RootPlayerBotAction __instance, PlayerBotActionBase.Descriptor __0, out PlayerBotActionBase.Descriptor __state)
        { __state = __0; return !Host || !Owns(__instance?.m_bot); }
        static void AfterSelection(RootPlayerBotAction __instance, ref PlayerBotActionBase.Descriptor __0, PlayerBotActionBase.Descriptor __state)
        {
            if (!Host || !HasState || __0 == null || __instance?.m_bot == null) return;
            var revive = __0.TryCast<PlayerBotActionRevive.Descriptor>();
            if (revive?.Client != null && Reserved(revive.Client, __instance.m_bot)) __0 = __state?.Pointer == revive.Pointer ? null : __state;
        }
        static bool Allow(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (!Host || !HasState || bot == null || desc == null) return true;
            var revive = desc.TryCast<PlayerBotActionRevive.Descriptor>();
            if (revive?.Client != null && Reserved(revive.Client, bot)) return false;
            if (!active.TryGetValue(bot.Pointer, out var entry)) return true;
            if (entry.Session.OurAction(desc)) return true;
            if (revive != null) return false; // A named assignment must never silently change targets.
            if (!entry.Session.Running) return true;
            return desc.TryCast<PlayerBotActionUseBioscan.Descriptor>() == null
                && (desc.RequiredLayers & (PlayerBotActionBase.AccessLayers.Legs | PlayerBotActionBase.AccessLayers.RootPosition)) == 0;
        }
        static bool Request(PlayerAIBot __instance, PlayerBotActionBase.Descriptor desc, ref bool __result)
        { if (Allow(__instance, desc)) return true; __result = false; return false; }
        static bool Start(PlayerAIBot __instance, PlayerBotActionBase.Descriptor desc) => Allow(__instance, desc);
        sealed class Session : IRescueSession
        {
            internal readonly PlayerAIBot Bot;
            internal readonly Target Target;
            internal readonly bool Batch;
            readonly IntPtr floor, agent, owner;
            readonly int slot;
            readonly string name;
            readonly object ticket = new();
            readonly List<Action> resume = new(3);
            PlayerBotActionRevive.Descriptor desc;
            float previousPriority;
            bool prepared, priorityChanged;
            RescueStage? reportedStage;
            string reportedReason;
            public Session(PlayerAIBot bot, Target target, bool batch)
            { Bot = bot; Target = target; Batch = batch; floor = Builder.CurrentFloor.Pointer; agent = bot.Agent.Pointer;
                owner = bot.Agent.Owner.Pointer; slot = bot.Agent.PlayerSlotIndex; name = bot.Agent.PlayerName; }
            bool Identity => Host && Builder.CurrentFloor.Pointer == floor
                && PlayerManager.Current?.GetPlayerAgentInSlot(slot)?.Pointer == agent && Bot?.Agent?.Owner?.Pointer == owner
                && PlayerManager.Current.GetPlayerAgentInSlot(slot).GetComponent<PlayerAIBot>()?.Pointer == Bot.Pointer;
            public bool Valid => Identity && BotReady(Bot) && Current(Target) is PlayerAgent target && ItemRegion.SameSpace(Bot.Agent, target.CourseNode);
            public bool Downed => RescueOrders.Downed(Current(Target));
            public bool Recovered => Current(Target)?.Alive == true;
            public bool Running => desc != null && !desc.IsTerminated();
            public bool Interacting => Running && desc.ActionBase?.TryCast<PlayerBotActionRevive>()?.m_state == PlayerBotActionRevive.State.Revive;
            public bool Ready(out string reason)
            {
                reason = TargetReason(Bot, Current(Target)); if (reason != null) return false;
                if (!prepared) {
                    prepared = true; AutoPickupAdapter.CancelPrompt(Bot); MovementOrders.BeforeNativeCommand(Bot);
                    ItemTaskAdapter.SuspendForRescue(Bot, resume);
                }
                var existing = NativeRevive(Bot);
                if (existing != null) { reason = existing.Client?.Pointer == Target.Agent ? null : "机器人正在救援另一名队友，等待原版收尾"; return reason == null; }
                if (!ItemTaskAdapter.RescueYielded(Bot) || NativePickupAdapter.Owns(Bot) || ResourceUseAdapter.Owns(Bot) || SingleKillAdapter.Owns(Bot)) {
                    reason = "等待物品、开锁、补给、特殊搬运或挥击交互收尾"; return false;
                }
                var actions = Bot.Actions;
                if (actions != null) for (int i = 0; i < Math.Min(32, actions.Count); i++) {
                    var action = actions[i]; if (action?.DescBase == null || action.DescBase.IsTerminated()) continue;
                    if (action.TryCast<PlayerBotActionDeploySentryGun>() != null || action.TryCast<PlayerBotActionDeployTripMine>() != null
                        || action.TryCast<PlayerBotActionGatherDeployables>() != null || action.TryCast<PlayerBotActionUnlock>() != null
                        || action.TryCast<PlayerBotActionCollectItem>() != null || action.TryCast<PlayerBotActionShareResourcePack>() != null
                        || action.TryCast<PlayerBotActionCarryExpeditionItem>() != null) { reason = "等待原版工具／物品交互结束"; return false; }
                }
                var target = Current(Target);
                var route = RescueRoute(Bot, target, out reason);
                if (route == null) return false;
                if (!Bot.TestDangerZones(target.Position, AIDangerZone.SeverityEnum.Harmful, out _)) { reason = "原版危险区域检查拒绝救援站位"; return false; }
                var root = Bot.m_rootAction?.ActionBase?.TryCast<RootPlayerBotAction>();
                if (root?.m_reviveAction == null) { reason = "原版救援描述符尚未就绪"; return false; }
                return true;
            }
            public bool Begin()
            {
                var existing = NativeRevive(Bot);
                if (existing != null && existing.Client?.Pointer == Target.Agent) { desc = existing; return true; }
                var root = Bot.m_rootAction?.ActionBase?.TryCast<RootPlayerBotAction>();
                desc = root?.m_reviveAction; if (desc == null || !desc.IsTerminated()) return false;
                desc.Client = Current(Target);
                if (!priorityChanged) { previousPriority = desc.Prio; priorityChanged = true; }
                desc.Prio = 100000f;
                // Native OnQueued creates the object reservation; termination releases it.
                bool accepted = Bot.RequestAction(desc);
                Plugin.Logger.LogInfo("[救援诊断] bot=" + name + " target=" + Target.Name + " accepted=" + accepted);
                return accepted;
            }
            internal bool OurAction(PlayerBotActionBase.Descriptor action)
            {
                for (int i = 0; action != null && i < 8; i++) {
                    if (desc?.Pointer == action.Pointer) return true;
                    action = action.ParentActionBase?.DescBase;
                }
                return false;
            }
            internal void Report(RescueRunner runner)
            {
                TaskBoard.Journal.Record(ticket, slot, owner.ToInt64(), name,
                    Batch ? "多人救援分配" : "指定救援", Target.Name,
                    runner.Stage switch { RescueStage.Waiting => TaskBoardState.Waiting, RescueStage.Complete => TaskBoardState.Complete,
                        RescueStage.Failed => TaskBoardState.Failed, RescueStage.Cancelled => TaskBoardState.Cancelled, _ => TaskBoardState.Executing },
                    Text(runner.Stage), runner.Reason, Time.time);
                if (reportedStage != runner.Stage || reportedReason != runner.Reason) {
                    reportedStage = runner.Stage; reportedReason = runner.Reason;
                    Plugin.Logger.LogInfo("[救援诊断] bot=" + name + " target=" + Target.Name + " stage=" + runner.Stage + " reason=" + runner.Reason);
                }
            }
            public void Finish(RescueStage result, string reason)
            {
                active.Remove(Bot.Pointer);
                try { if (Identity && Running) Bot.StopAction(desc); }
                finally {
                    if (Identity && priorityChanged && desc != null) desc.Prio = previousPriority;
                    if (Identity) foreach (var restore in resume) try { restore(); } catch (Exception error) { Plugin.Logger.LogWarning("[救援诊断] 恢复原任务：" + error.Message); }
                    resume.Clear();
                    TaskBoard.Journal.Record(ticket, slot, owner.ToInt64(), name, Batch ? "多人救援分配" : "指定救援", Target.Name,
                        result == RescueStage.Complete ? TaskBoardState.Complete : result == RescueStage.Cancelled ? TaskBoardState.Cancelled : TaskBoardState.Failed,
                        Text(result), reason, Time.time);
                    if (Host) ItemTaskAdapter.Feedback(name + "：救援 " + Target.Name + " — " + reason);
                }
            }
        }
    }
}

