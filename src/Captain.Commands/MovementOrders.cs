using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class MovementOrders
    {
        sealed class Follow
        {
            public PlayerAIBot Bot;
            public PlayerAgent Target, Previous;
            public IntPtr Owner, Agent, Floor, Receiver;
            public int Slot, ReceiverSlot;
            public string BotName, TargetName;
            public bool Applied, Restore, HadInterruption, ManualPaused;
            public float Next, RequestedAt;
            public int Attempts;
            public readonly object Report = new();
            public object BorrowMarker;
            public bool ControlPaused;
            public int PauseRevision;
        }
        sealed class Go
        {
            public PositionSession Session;
            public PositionTaskRunner Runner;
            public readonly object Report = new();
            public PositionTaskStage? LastStage;
        }
        sealed class Station
        {
            public StationSession Session;
            public StationRunner Runner;
            public readonly object Report = new();
            public StationStage? LastStage;
        }
        static readonly Dictionary<IntPtr, Follow> follows = new();
        static readonly Dictionary<IntPtr, Go> moving = new();
        static readonly Dictionary<IntPtr, Station> stations = new();
        static readonly List<Station> stationSnapshot = new(3);
        static readonly List<Go> goSnapshot = new(3);
        static readonly List<Follow> followSnapshot = new(3);
        static ConfigEntry<float> timeout, stationRadius;
        internal static bool HasState => follows.Count != 0 || moving.Count != 0 || stations.Count != 0;
        internal static bool BlocksAuto(PlayerAIBot bot) => Owns(bot) || bot != null && stations.TryGetValue(bot.Pointer, out var s) && !s.Runner.Finished;
        internal static bool HasDuty(PlayerAIBot bot) => bot != null && stations.TryGetValue(bot.Pointer, out var s) && !s.Runner.Finished;
        internal static string ControlStationStatus(PlayerAIBot bot) => bot != null && stations.TryGetValue(bot.Pointer, out var duty)
            && duty.Session.FromControl && !duty.Runner.Finished && !duty.Runner.ManualPaused && !ItemTaskAdapter.PausedWorkBlocks(bot)
                ? "驻守·" + StationText(duty.Runner.Stage) : null;
        internal static bool Owns(PlayerAIBot bot) => bot != null && moving.TryGetValue(bot.Pointer, out var job) && !job.Runner.Finished;
        internal static bool PausedWorkBlocks(PlayerAIBot bot) => Owns(bot)
            && !(moving[bot.Pointer].Runner.ManualPaused && !moving[bot.Pointer].Session.HasLease);
        internal static bool ControlStationRecallProtected(long agent, long owner)
        {
            foreach (var duty in stations.Values)
                if (duty.Session.FromControl && duty.Session.Agent.ToInt64() == agent && duty.Session.Owner.ToInt64() == owner
                    && duty.Session.IdentityValid && !duty.Runner.Finished && duty.Runner.Stage == StationStage.Waiting && !duty.Session.HasLease) return true;
            return false;
        }
        internal static string Status(PlayerAIBot bot)
            => bot == null ? null : moving.TryGetValue(bot.Pointer, out var go) ? "前往位置·" + Text(go.Runner.Stage)
                : stations.TryGetValue(bot.Pointer, out var station) ? (station.Session.Guard?.Label ?? "驻守") + "·" + StationText(station.Runner.Stage)
                : follows.TryGetValue(bot.Pointer, out var follow) ? follow.Restore ? "恢复原跟随" : (follow.ManualPaused ? "手动暂停跟随 " : follow.Applied ? "跟随 " : "等待跟随 ") + follow.TargetName : null;
        internal static void Configure(ConfigFile config)
        {
            PositionMarks.Configure(config);
            timeout = config.Bind("行动指令", "前往任务超时秒数", 60f, "等待权限及实际移动超时，范围15–180秒；保护动作期间停止自己的移动，暂停总时长有界；不会每帧重发寻路。");
            stationRadius = config.Bind("行动指令", "驻守活动半径", 3f, "1.5–8米；到达标记后在半径内保持，超出后有限重返。扫描、救援及明确交互优先，收尾后稳定2.5秒再恢复。");
        }
        internal static void Install(Harmony patches)
        {
            PositionMarks.Install(patches);
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction"), prefix: new HarmonyMethod(typeof(MovementOrders), nameof(AllowRequest)) { priority = Priority.First });
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "StartAction"), prefix: new HarmonyMethod(typeof(MovementOrders), nameof(AllowStart)) { priority = Priority.First });
            patches.Patch(AccessTools.Method(typeof(PUI_CommunicationMenu), "ExecuteCmdCall", new[] { typeof(PlayerAgent), typeof(PlayerAgent) }),
                prefix: new HarmonyMethod(typeof(MovementOrders), nameof(NativeCall)) { priority = Priority.First + 20 });
        }
        static bool Host => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor != null;
        static bool Live(PlayerAIBot bot) => Host && bot?.Agent?.Alive == true && bot.Agent.Owner?.IsBot == true && bot.SyncValues != null;
        static bool Identity(Follow job) => Host && Builder.CurrentFloor.Pointer == job.Floor
            && PlayerManager.Current?.GetPlayerAgentInSlot(job.Slot)?.Pointer == job.Agent
            && job.Bot?.Agent?.Owner?.Pointer == job.Owner && job.Bot.Agent.Owner.IsBot;
        static bool Receiver(Follow job) => job.Target != null
            && PlayerManager.Current?.GetPlayerAgentInSlot(job.ReceiverSlot)?.Pointer == job.Target.Pointer
            && job.Target.Owner?.IsBot == false && job.Target.Owner.Pointer == job.Receiver;
        static void Observe(Follow job, TaskBoardState state, string stage, string reason)
            => TaskBoard.Journal.Record(job.Report, job.Slot, job.Owner.ToInt64(), job.BotName,
                job.Restore ? "恢复原跟随" : "跟随指定玩家", job.TargetName, state, stage, reason, Time.time);
        static Follow MakeFollow(PlayerAIBot bot, PlayerAgent target, PlayerAgent previous, bool restore = false)
            => new() { Bot = bot, Target = target, Previous = previous, Restore = restore, Owner = bot.Agent.Owner.Pointer,
                Agent = bot.Agent.Pointer, Floor = Builder.CurrentFloor.Pointer, Slot = bot.Agent.PlayerSlotIndex,
                Receiver = target.Owner.Pointer, ReceiverSlot = target.PlayerSlotIndex, BotName = bot.Agent.PlayerName, TargetName = target.PlayerName };
        static PlayerAgent LivePrevious(PlayerAgent previous)
        {
            if (previous == null) return null;
            for (int slot = 0; slot < 4; slot++) {
                var current = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                if (current?.Pointer == previous.Pointer && current.Owner?.IsBot == false && current.Alive) return current;
            }
            return null;
        }
        internal static void FollowPlayer(PlayerAIBot bot, PlayerAgent target)
        {
            if (!Live(bot) || target?.Alive != true || target.Owner?.IsBot != false || !ItemRegion.SameSpace(bot.Agent, target.CourseNode)) {
                ItemTaskAdapter.Feedback("仅房主可选择同层同维度的存活真人队友作为跟随对象"); return;
            }
            var previous = bot.SyncValues.Leader;
            if (stations.TryGetValue(bot.Pointer, out var duty)) { duty.Runner.Cancel(); duty.Runner.Tick(Time.time); Observe(duty); stations.Remove(bot.Pointer); }
            if (follows.TryGetValue(bot.Pointer, out var existing)) {
                if (MovementOrderPolicy.Duplicate(existing.Receiver.ToInt64(), target.Owner.Pointer.ToInt64()) && !existing.Restore) {
                    ItemTaskAdapter.Feedback("相同跟随指令已登记，不重复召回或传送"); return;
                }
                previous = existing.Previous;
                Observe(existing, TaskBoardState.Cancelled, "已改派", "新的跟随对象替换旧职责；临时交互继续收尾");
            }
            var job = MakeFollow(bot, target, previous); follows[bot.Pointer] = job;
            Observe(job, TaskBoardState.Waiting, "等待原活动", "扫描、救援及原子交互优先；生效后使用原版跟随与传送");
            ItemTaskAdapter.Feedback(bot.Agent.PlayerName + "：已选择跟随 " + target.PlayerName + "；等待当前受保护动作结束后生效");
        }
        static void TickFollow(Follow job)
        {
            if (!Identity(job) || !Receiver(job)) { Observe(job, TaskBoardState.Failed, "目标失效", "玩家、机器人或关卡身份改变"); follows.Remove(job.Bot.Pointer); return; }
            if (job.BorrowMarker != null || job.ControlPaused && job.ManualPaused) return;
            if (Time.time < job.Next) return; job.Next = Time.time + .5f;
            if (!job.Bot.Agent.Alive || !job.Target.Alive || !ItemRegion.SameSpace(job.Bot.Agent, job.Target.CourseNode)) {
                if (!job.HadInterruption) job.Attempts = 0;
                job.HadInterruption = true; Observe(job, TaskBoardState.Paused, "等待恢复", "倒地／不同维度或层级；保留跟随对象，不强行传送"); return;
            }
            var desired = job.ManualPaused ? LivePrevious(job.Previous) ?? PlayerManager.GetLocalPlayerAgent() : job.Target;
            if (desired?.Alive != true || desired.Owner?.IsBot != false) { Observe(job, TaskBoardState.Paused, "等待领队", "恢复原活动的真人领队暂不可用"); return; }
            bool matches = job.Bot.SyncValues?.Leader?.Pointer == desired.Pointer;
            if (job.ManualPaused && matches) { Observe(job, TaskBoardState.Paused, "手动暂停", "保留所选领队，暂交还此前原版活动；继续时重新核验"); return; }
            if (job.Applied && !matches && !job.HadInterruption) {
                Observe(job, TaskBoardState.Cancelled, "原版已改派", "其他原版跟随指令已改变领队，不反复覆盖玩家的指令"); follows.Remove(job.Bot.Pointer); return;
            }
            if (matches) {
                job.Applied = true; job.HadInterruption = false;
                if (job.Restore) { Observe(job, TaskBoardState.Complete, "已恢复", "原跟随对象已确认"); follows.Remove(job.Bot.Pointer); return; }
                bool occupied = ItemTaskAdapter.Busy(job.Bot) || ResourceUseAdapter.Owns(job.Bot) || SingleKillAdapter.Owns(job.Bot)
                    || NativeProtected(job.Bot) != null || !BotTaskBridge.AutoAvailable(job.Bot);
                Observe(job, occupied ? TaskBoardState.Paused : TaskBoardState.Executing, occupied ? "临时任务优先" : "持续跟随",
                    occupied ? "职责保留，扫描／救援／交互完成后由原版恢复跟随" : "原版领队身份已确认；不占用明确任务的移动租约");
                return;
            }
            if (ItemTaskAdapter.Busy(job.Bot) || SingleKillAdapter.Owns(job.Bot) || ResourceUseAdapter.Owns(job.Bot)
                || NativeProtected(job.Bot) != null || !BotTaskBridge.AutoAvailable(job.Bot, out _)) {
                Observe(job, TaskBoardState.Waiting, "等待原活动", "扫描／救援／交互／其他明确任务尚未结束"); return;
            }
            if (job.Attempts >= 3) { Observe(job, TaskBoardState.Failed, "原版未确认", "三次有间隔的跟随请求未更新原版领队，已停止重试"); follows.Remove(job.Bot.Pointer); ItemTaskAdapter.Feedback(job.BotName + "：原版未确认跟随，请查看日志"); return; }
            if (job.Attempts != 0 && Time.time - job.RequestedAt < 2) return;
            job.RequestedAt = Time.time; job.Attempts++;
            var menu = ItemTaskAdapter.NativeMenu;
            bool accepted = menu != null && menu.ExecuteCmdCall(desired, job.Bot.Agent);
            Observe(job, TaskBoardState.Waiting, "核验跟随", "原版请求=" + accepted + "；尝试=" + job.Attempts);
            Plugin.Logger.LogInfo("[行动指令] follow bot=" + job.BotName + " target=" + job.TargetName + " accepted=" + accepted + " attempt=" + job.Attempts);
        }
        internal static void GoTo(PlayerAIBot bot, MarkedPosition mark)
        {
            string reason = PositionMarks.Reason(mark);
            if (!Live(bot) || reason != null) { ItemTaskAdapter.Feedback(reason ?? "机器人尚未就绪或已经倒地"); return; }
            if (!SearchTraversal.InScope(bot.Agent.CourseNode, mark.Origin, 1) || !ItemRegion.SameSpace(bot.Agent, mark.Node)) {
                ItemTaskAdapter.Feedback("机器人与标记位置必须处于当前同一Zone／维度／分层"); return;
            }
            if (moving.TryGetValue(bot.Pointer, out var existing)) {
                if (MovementOrderPolicy.SamePoint(Vector3.Distance(existing.Session.Target.Point, mark.Point), existing.Session.Target.Node.Pointer == mark.Node.Pointer)) {
                    ItemTaskAdapter.Feedback("相同位置任务正在执行，不重启寻路或重复传送"); return;
                }
                ItemTaskAdapter.Feedback("该机器人已有前往任务；请先取消后再指定新位置"); return;
            }
            string busy = ItemTaskAdapter.BusyReason(bot);
            if (busy != null) { ItemTaskAdapter.Feedback("前往位置暂不能接单：" + busy); return; }
            if (bot.Actions != null) foreach (var action in bot.Actions)
                if (action?.TryCast<PlayerBotActionCarryExpeditionItem>() != null && action.DescBase != null && !action.DescBase.IsTerminated()) {
                    ItemTaskAdapter.Feedback("机器人正在原版搬运任务道具；本批前往指令不抢占手持搬运动作，请先完成／解除搬运"); return;
                }
            YieldDuty(bot);
            var session = new PositionSession(bot, mark);
            if (!session.Preflight(out reason)) { ItemTaskAdapter.Feedback("前往位置拒绝：" + reason); return; }
            var limit = float.IsFinite(timeout?.Value ?? 60) ? Math.Clamp(timeout?.Value ?? 60, 15, 180) : 60;
            var job = new Go { Session = session, Runner = new PositionTaskRunner(session, Time.time, limit) };
            moving.Add(bot.Pointer, job); AutoPickupAdapter.CancelPrompt(bot);
            Observe(job, "中键位置已接单；先取得移动权限，再前往并核验坐标");
            ItemTaskAdapter.Feedback(bot.Agent.PlayerName + "：前往 " + mark.Label + "，到达后恢复原活动");
        }
        internal static bool CombatAvailable(PlayerAIBot bot)=>bot!=null&&stations.TryGetValue(bot.Pointer,out var duty)&&duty.Session.HasLease
            ?duty.Session.Available:BotTaskBridge.AutoAvailable(bot);
        internal static GuardDutySpec Guard(PlayerAIBot bot)=>bot!=null&&stations.TryGetValue(bot.Pointer,out var duty)
            &&duty.Session.HasLease&&duty.Runner.Stage==StationStage.Holding&&!duty.Runner.ManualPaused&&duty.Session.Available?duty.Session.Guard:null;
        internal static void StationAt(PlayerAIBot bot,MarkedPosition mark)
            =>StationAtCore(bot,mark,float.IsFinite(stationRadius?.Value??3)?Math.Clamp(stationRadius?.Value??3,1.5f,8):3,null);
        internal static void GuardAt(PlayerAIBot bot,GuardDutySpec spec)=>StationAtCore(bot,spec?.Anchor,spec?.Radius??3,spec);
        internal static bool AcceptControlStation(PlayerAIBot bot, MarkedPosition mark, out string reason)
        {
            reason = PositionMarks.Reason(mark, bot?.Agent);
            if (!Live(bot) || reason != null) { reason ??= "机器人状态尚未就绪"; return false; }
            if (Vector3.Distance(bot.Agent.Position, mark.Point) > .75f || !SearchTraversal.InScope(bot.Agent.CourseNode, mark.Origin, 1)) {
                reason = "机器人已移离开菜单时的位置，请重开Q确认驻守此地"; return false;
            }
            if (ItemTaskAdapter.PausedWorkBlocks(bot)) { reason = "原任务还未完全暂停／已被继续，驻守交接暂不能接单"; return false; }
            var radius = float.IsFinite(stationRadius?.Value ?? 3) ? Math.Clamp(stationRadius?.Value ?? 3, 1.5f, 8) : 3;
            var session = new StationSession(bot, mark, radius, fromControl: true);
            if (!session.Preflight(out reason)) return false;
            CommitStation(bot, mark, radius, null, session, true);
            return stations.TryGetValue(bot.Pointer, out var accepted) && ReferenceEquals(accepted.Session, session) && !accepted.Runner.Finished;
        }
        static void StationAtCore(PlayerAIBot bot,MarkedPosition mark,float radius,GuardDutySpec guard)
        {
            string reason=PositionMarks.Reason(mark);
            if(!Live(bot)||reason!=null||guard?.Valid==false){ItemTaskAdapter.Feedback(reason??"机器人或守卫目标身份尚未就绪");return;}
            if(ItemTaskAdapter.BusyReason(bot) is string busy||ResourceUseAdapter.Owns(bot)||SingleKillAdapter.Owns(bot)){
                ItemTaskAdapter.Feedback("固定职责暂不能接单："+(ItemTaskAdapter.BusyReason(bot)??"补给／指定击杀正在执行"));return;
            }
            if(!SearchTraversal.InScope(bot.Agent.CourseNode,mark.Origin,1)){ItemTaskAdapter.Feedback("机器人必须位于目标的当前Zone，不跨封锁区调遣");return;}
            var session=new StationSession(bot,mark,radius,guard);
            if(!session.Preflight(out reason)){ItemTaskAdapter.Feedback("固定职责拒绝："+reason);return;}
            CommitStation(bot,mark,radius,guard,session,false);
        }
        static void CommitStation(PlayerAIBot bot, MarkedPosition mark, float radius, GuardDutySpec guard, StationSession session, bool fromControl)
        {
            if(stations.TryGetValue(bot.Pointer,out var prior)){
                bool sameKind=guard==null?prior.Session.Guard==null:guard.Same(prior.Session.Guard);
                if(!fromControl&&sameKind&&prior.Session.Radius==radius&&MovementOrderPolicy.SamePoint(Vector3.Distance(prior.Session.Target.Point,mark.Point),prior.Session.Target.Node.Pointer==mark.Node.Pointer)
                    &&Vector3.Dot(prior.Session.Target.Direction,mark.Direction)>.99f){ItemTaskAdapter.Feedback("相同固定职责、站位、半径和朝向已登记，不重复启动");return;}
            }
            if(!fromControl&&follows.TryGetValue(bot.Pointer,out var follow)){Observe(follow,TaskBoardState.Cancelled,"职责改派","已改为固定站位职责");follows.Remove(bot.Pointer);}
            var job=new Station{Session=session,Runner=new StationRunner(session,Time.time,float.IsFinite(timeout?.Value??60)?Math.Clamp(timeout?.Value??60,15,180):60)};
            stations[bot.Pointer]=job;
            // The new record exists before callbacks. Old station cleanup cannot remove it or resume a prior duty.
            try { if(prior!=null){prior.Runner.Cancel();prior.Runner.Tick(Time.time);Observe(prior);} }
            catch(Exception error){Plugin.Logger.LogWarning("[驻守交接] 旧驻守收尾："+error);}
            try { AutoPickupAdapter.CancelPrompt(bot);Observe(job);
                ItemTaskAdapter.Feedback(session.Name+"："+(guard?.Label??"驻守位置")+"，半径 "+radius+"米；受保护任务结束后恢复固定点"); }
            catch(Exception error){Plugin.Logger.LogWarning("[驻守交接] 接单已保存，反馈失败："+error);}
        }
        static string StationText(StationStage stage) => stage switch { StationStage.Waiting => "等待权限", StationStage.Moving => "接近驻守点", StationStage.Holding => "保持驻守", StationStage.Yielded => "临时任务优先", StationStage.Paused => "手动暂停", StationStage.Cancelled => "已取消", _ => "失败" };
        static void Observe(Station job)
        {
            if (job.LastStage != job.Runner.Stage) { job.LastStage = job.Runner.Stage; Plugin.Logger.LogInfo("[驻守指令] bot=" + job.Session.Name + " stage=" + job.Runner.Stage); }
            TaskBoard.Journal.Record(job.Report, job.Session.Slot, job.Session.Owner.ToInt64(), job.Session.Name, job.Session.Guard?.Label ?? "驻守位置", job.Session.Target.Label,
                job.Runner.Stage switch { StationStage.Paused or StationStage.Yielded => TaskBoardState.Paused, StationStage.Waiting => TaskBoardState.Waiting, StationStage.Cancelled => TaskBoardState.Cancelled, StationStage.Failed => TaskBoardState.Failed, _ => TaskBoardState.Executing },
                StationText(job.Runner.Stage), job.Session.Error ?? job.Session.Guard?.Detail ?? "半径 " + job.Session.Radius + "米；扫描／救援／明确任务优先，稳定后重新核验驻守路线", Time.time);
        }
        internal static void YieldDuty(PlayerAIBot bot)
        { if (bot != null && stations.TryGetValue(bot.Pointer, out var job) && !job.Runner.Finished) { job.Runner.Yield(Time.time); Observe(job); } }
        internal static void SuspendForBorrow(PlayerAIBot bot, List<BorrowHold> holds)
        {
            if (bot == null) return;
            if (moving.TryGetValue(bot.Pointer, out var go) && !go.Runner.Finished) {
                bool changed = !go.Runner.ManualPaused;
                if (changed) { go.Runner.Pause(true); go.Runner.Tick(Time.time); }
                int revision = go.Runner.PauseRevision;
                bool Current() => moving.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, go) && !go.Runner.Finished;
                holds.Add(new(() => !Current() || !go.Session.HasLease, () => {
                    if (changed && Current() && go.Runner.PauseRevision == revision && go.Runner.ManualPaused) go.Runner.Pause(false);
                }, () => Current() ? go.Runner.PauseRevision == revision : !Owns(bot)));
            }
            if (stations.TryGetValue(bot.Pointer, out var duty) && !duty.Runner.Finished) {
                bool changed = !duty.Runner.ManualPaused;
                if (changed) { duty.Runner.Pause(true); duty.Runner.Tick(Time.time); }
                int revision = duty.Runner.PauseRevision;
                bool Current() => stations.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, duty) && !duty.Runner.Finished;
                holds.Add(new(() => !Current() || !duty.Session.HasLease, () => {
                    if (changed && Current() && duty.Runner.PauseRevision == revision && duty.Runner.ManualPaused) duty.Runner.Pause(false);
                }, () => Current() ? duty.Runner.PauseRevision == revision : !HasDuty(bot)));
            }
            if (follows.TryGetValue(bot.Pointer, out var follow)) {
                var marker = new object(); follow.BorrowMarker = marker;
                int revision = follow.PauseRevision;
                holds.Add(new(() => true, () => {
                    if (follows.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, follow) && ReferenceEquals(follow.BorrowMarker, marker)) follow.BorrowMarker = null;
                }, () => follows.TryGetValue(bot.Pointer, out var current) ? ReferenceEquals(current, follow) && follow.PauseRevision == revision : true));
            }
        }
        internal static void SuspendFollowForControl(PlayerAIBot bot, List<BorrowHold> holds)
        {
            if (!follows.TryGetValue(bot.Pointer, out var follow)) return;
            bool wasPaused = follow.ManualPaused, wasControl = follow.ControlPaused;
            int revision = follow.PauseRevision;
            follow.ManualPaused = true; follow.ControlPaused = true;
            bool Current() => follows.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, follow) && current.PauseRevision == revision;
            holds.Add(new(() => true, () => {
                if (Current()) { follow.ManualPaused = wasPaused; follow.ControlPaused = wasControl; }
            }, Current));
        }
        internal static void KeepFollowPausedAfterControl(PlayerAIBot bot)
        {
            if (!follows.TryGetValue(bot.Pointer, out var follow)) return;
            follow.BorrowMarker = null; follow.ManualPaused = true; follow.ControlPaused = true;
            follow.Applied = false; follow.Attempts = 0; follow.Next = 0;
            Observe(follow, TaskBoardState.Paused, "接管退出后暂停", "旧跟随对象保留；本体召回由原版跟随执行，Q可继续旧任务");
        }
        internal static bool Control(PlayerAIBot bot, bool pause)
        {
            if (bot == null) return false;
            if (moving.TryGetValue(bot.Pointer, out var go) && !go.Runner.Finished) { go.Runner.Pause(pause); go.Runner.Tick(Time.time); Observe(go, pause ? "手动暂停：保留坐标并释放移动权限" : "继续：重新核验固定目标与路线"); return true; }
            if (stations.TryGetValue(bot.Pointer, out var duty) && !duty.Runner.Finished) { duty.Runner.Pause(pause); duty.Runner.Tick(Time.time); Observe(duty); return true; }
            if (follows.TryGetValue(bot.Pointer, out var follow) && !follow.Restore) {
                if (!pause) follow.ControlPaused = false;
                follow.PauseRevision++;
                if (follow.ManualPaused != pause) { follow.ManualPaused = pause; follow.Applied = false; follow.Attempts = 0; follow.Next = 0; }
                Observe(follow, pause ? TaskBoardState.Paused : TaskBoardState.Waiting, pause ? "手动暂停" : "等待继续", "保留所选领队；暂停时交还此前原版活动，继续时核验真人身份"); return true;
            }
            return false;
        }
        static string Text(PositionTaskStage stage) => stage switch { PositionTaskStage.Waiting => "等待权限", PositionTaskStage.Moving => "前往中",
            PositionTaskStage.Paused => "暂时让位", PositionTaskStage.Returning => "恢复原活动", PositionTaskStage.Complete => "已完成", PositionTaskStage.Cancelled => "已取消", _ => "失败" };
        static void Observe(Go job, string detail = null) {
            if (job.LastStage != job.Runner.Stage) {
                job.LastStage = job.Runner.Stage;
                Plugin.Logger.LogInfo("[行动指令] position bot=" + job.Session.Name + " stage=" + job.Runner.Stage + " target=" + job.Session.Target.Label);
            }
            TaskBoard.Journal.Record(job.Report, job.Session.Slot, job.Session.Owner.ToInt64(), job.Session.Name,
            "前往标记位置", job.Session.Target.Label, job.Runner.Stage switch { PositionTaskStage.Waiting => TaskBoardState.Waiting,
                PositionTaskStage.Paused => TaskBoardState.Paused, PositionTaskStage.Returning => TaskBoardState.Returning,
                PositionTaskStage.Complete => TaskBoardState.Complete, PositionTaskStage.Failed => TaskBoardState.Failed,
                PositionTaskStage.Cancelled => TaskBoardState.Cancelled, _ => TaskBoardState.Executing }, Text(job.Runner.Stage),
            detail ?? job.Session.EndReason ?? (job.Runner.Stage is PositionTaskStage.Waiting or PositionTaskStage.Paused ? job.Session.WaitReason : null)
                ?? "同一租约保护前往／恢复；近处步行，远处仅一次安全传送", Time.time);
        }
        internal static bool Cancel(PlayerAIBot bot, bool returning)
        {
            if (bot == null) return false;
            bool handled = false;
            if (stations.TryGetValue(bot.Pointer, out var duty)) { duty.Runner.Cancel(); duty.Runner.Tick(Time.time); Observe(duty); stations.Remove(bot.Pointer); handled = true; }
            if (moving.TryGetValue(bot.Pointer, out var go)) { go.Runner.Cancel(returning); go.Runner.Tick(Time.time); Observe(go); handled = true; }
            if (follows.TryGetValue(bot.Pointer, out var follow) && !follow.Restore) {
                Observe(follow, TaskBoardState.Cancelled, "已取消", "清除手动跟随职责，受保护动作收尾后恢复原领队");
                follows.Remove(bot.Pointer); handled = true;
                var previous = LivePrevious(follow.Previous) ?? PlayerManager.GetLocalPlayerAgent();
                if (Live(bot) && previous?.Owner?.IsBot == false) follows[bot.Pointer] = MakeFollow(bot, previous, null, true);
            }
            if (handled) ItemTaskAdapter.Feedback(bot.Agent.PlayerName + "：行动指令取消已登记，扫描／救援和其他交互继续受保护");
            return handled;
        }
        internal static void BeforeNativeCommand(PlayerAIBot bot)
        { YieldDuty(bot); if (bot != null && moving.TryGetValue(bot.Pointer, out var job)) { job.Runner.Abort("让位给新的原版部署／物品指令"); Observe(job); } }
        internal static void Tick()
        {
            goSnapshot.Clear(); goSnapshot.AddRange(moving.Values);
            foreach (var job in goSnapshot) {
                try { job.Runner.Tick(Time.time); Observe(job); }
                catch (Exception error) { Plugin.Logger.LogWarning("[行动指令] 前往异常：" + error); job.Runner.Abort("前往检查异常：" + error.GetBaseException().Message); Observe(job); }
                if (job.Runner.Finished && moving.TryGetValue(job.Session.Bot.Pointer, out var current) && ReferenceEquals(current, job)) moving.Remove(job.Session.Bot.Pointer);
            }
            goSnapshot.Clear(); followSnapshot.Clear(); followSnapshot.AddRange(follows.Values);
            foreach (var job in followSnapshot) try { TickFollow(job); }
                catch (Exception error) { Observe(job, TaskBoardState.Failed, "检查异常", error.GetBaseException().Message); follows.Remove(job.Bot.Pointer); Plugin.Logger.LogWarning("[行动指令] 跟随异常：" + error); }
            followSnapshot.Clear();
            stationSnapshot.Clear(); stationSnapshot.AddRange(stations.Values);
            foreach (var duty in stationSnapshot) {
                if (duty.Session.FromControl && duty.Runner.Stage == StationStage.Waiting && !duty.Runner.ManualPaused && !duty.Session.HasLease
                    && PreLuna.Captain.Control.BotControlApi.Controls(duty.Session.Agent.ToInt64(), duty.Session.Owner.ToInt64())) continue;
                try { duty.Runner.Tick(Time.time); Observe(duty); }
                catch (Exception error) { Plugin.Logger.LogWarning("[驻守指令] " + error); duty.Runner.Abort(error.GetBaseException().Message); Observe(duty); }
                if (duty.Runner.Finished && stations.TryGetValue(duty.Session.Bot.Pointer, out var current) && ReferenceEquals(current, duty)) stations.Remove(duty.Session.Bot.Pointer);
            }
            stationSnapshot.Clear();
        }
        internal static void Reset()
        {
            goSnapshot.Clear(); goSnapshot.AddRange(moving.Values);
            foreach (var job in goSnapshot) try { job.Runner.Abort("关卡、房主或机器人状态重置"); } catch (Exception error) { Plugin.Logger.LogWarning("行动清理：" + error.Message); }
            stationSnapshot.Clear(); stationSnapshot.AddRange(stations.Values);
            foreach (var duty in stationSnapshot) try { duty.Runner.Abort("关卡／房主重置"); } catch (Exception error) { Plugin.Logger.LogWarning("驻守清理：" + error.Message); }
            stations.Clear(); stationSnapshot.Clear(); moving.Clear(); follows.Clear(); goSnapshot.Clear(); followSnapshot.Clear(); PositionMarks.Reset();
        }
        static bool AllowRequest(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0, ref bool __result)
        { if (Allow(__instance, __0)) return true; __result = false; return false; }
        static bool AllowStart(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0) => Allow(__instance, __0);
        static void NativeCall(PlayerAgent __1)
        {
            if (BotControlService.IsRecall(__1?.GetComponent<PlayerAIBot>())) return;
            if (__1?.Owner != null && (PreLuna.Captain.Control.BotControlApi.Controls(__1.Pointer.ToInt64(), __1.Owner.Pointer.ToInt64())
                || PreLuna.Captain.Control.BotControlApi.CommandOwns(__1.Pointer.ToInt64(), __1.Owner.Pointer.ToInt64()))) return;
            if (!Host || __1?.Owner?.IsBot != true) return;
            var bot = __1.GetComponent<PlayerAIBot>();
            if (bot != null && stations.TryGetValue(bot.Pointer, out var duty)) { duty.Runner.Cancel(); duty.Runner.Tick(Time.time); Observe(duty); stations.Remove(bot.Pointer); }
            BeforeNativeCommand(bot);
        }
        static string NativeProtected(PlayerAIBot bot, PositionSession owner = null)
        {
            if (bot.Actions == null) return "机器人动作表尚未就绪";
            foreach (var action in bot.Actions) {
                if (action?.DescBase == null || action.DescBase.IsTerminated() || owner?.OurAction(action.DescBase) == true) continue;
                if (action.TryCast<PlayerBotActionRevive>() != null) return "原版救援尚未结束";
                if (action.TryCast<PlayerBotActionShareResourcePack>() != null || action.TryCast<PlayerBotActionCollectItem>() != null
                    || action.TryCast<PlayerBotActionCarryExpeditionItem>() != null || action.TryCast<PlayerBotActionUnlock>() != null
                    || action.TryCast<PlayerBotActionDeploySentryGun>() != null || action.TryCast<PlayerBotActionDeployTripMine>() != null
                    || action.TryCast<PlayerBotActionGatherDeployables>() != null) return "原版物品／工具交互尚未收尾";
            }
            return null;
        }
        static bool Allow(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (bot != null && desc != null && stations.TryGetValue(bot.Pointer, out var duty) && duty.Session.HasLease && !duty.Session.OurAction(desc)) {
                if (desc.TryCast<PlayerBotActionRevive.Descriptor>() != null || desc.TryCast<PlayerBotActionUseBioscan.Descriptor>() != null
                    || desc.TryCast<PlayerBotActionShareResourcePack.Descriptor>() != null || desc.TryCast<PlayerBotActionUnlock.Descriptor>() != null
                    || desc.TryCast<PlayerBotActionDeploySentryGun.Descriptor>() != null || desc.TryCast<PlayerBotActionDeployTripMine.Descriptor>() != null
                    || desc.TryCast<PlayerBotActionGatherDeployables.Descriptor>() != null || desc.TryCast<PlayerBotActionCarryExpeditionItem.Descriptor>() != null) {
                    duty.Runner.Yield(Time.time); Observe(duty); return true;
                }
                if (desc.TryCast<PlayerBotActionUseFirearm.Descriptor>() != null || desc.TryCast<PlayerBotActionMelee.Descriptor>() != null
                    || desc.TryCast<PlayerBotActionReloadWeapon.Descriptor>() != null || desc.TryCast<PlayerBotActionEquipItem.Descriptor>() != null) return true;
                if ((desc.RequiredLayers & (PlayerBotActionBase.AccessLayers.Legs | PlayerBotActionBase.AccessLayers.RootPosition)) != 0) return false;
            }
            if (bot == null || !moving.TryGetValue(bot.Pointer, out var job) || !job.Session.HasLease || desc == null || job.Session.OurAction(desc)) return true;
            if (desc.TryCast<PlayerBotActionRevive.Descriptor>() != null) { job.Runner.Abort("让位给原版救援"); Observe(job); return true; }
            return (desc.RequiredLayers & (PlayerBotActionBase.AccessLayers.Legs | PlayerBotActionBase.AccessLayers.RootPosition)) == 0;
        }
        class PositionSession : IPositionTaskSession
        {
            public readonly PlayerAIBot Bot;
            public readonly MarkedPosition Target;
            public readonly int Slot;
            public readonly IntPtr Owner, Agent;
            public readonly string Name;
            long token;
            RoutePlan route, returnRoute;
            PlayerBotActionTravel.Descriptor travel;
            Vector3 travelPoint;
            bool outboundWarped, inboundWarped, returnChosen, reacquiringReturn;
            Vector3 returnPoint;
            float nextRequest;
            int attempts;
            public string EndReason { get; private set; }
            public string Blocked { get; private set; }
            public string WaitReason { get; private set; }
            public bool HasLease => token != 0;
            public PositionSession(PlayerAIBot bot, MarkedPosition target)
            { Bot = bot; Target = target; Slot = bot.Agent.PlayerSlotIndex; Owner = bot.Agent.Owner.Pointer; Agent = bot.Agent.Pointer; Name = bot.Agent.PlayerName; }
            public bool IdentityValid => Host && Builder.CurrentFloor.Pointer == Target.Floor
                && PlayerManager.Current?.GetPlayerAgentInSlot(Slot)?.Pointer == Agent
                && Bot?.Agent?.Owner?.Pointer == Owner && Bot.Agent.Owner.IsBot;
            public bool Valid => IdentityValid && Bot.Agent.Alive && ItemRegion.SameSpace(Bot.Agent, Target.Node);
            public bool Preflight(out string reason)
            {
                reason = null;
                if (!Valid || !AIGraph.AIG_CourseNode.TryGetCourseNode(Bot.Agent.DimensionIndex, Target.Point, 1, out var resolved)
                    || resolved?.Pointer != Target.Node.Pointer
                    || Physics.CheckCapsule(Target.Point + Vector3.up * .35f, Target.Point + Vector3.up * 1.4f, .25f, LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore)) {
                    reason = "固定位置的身份、导航节点或可站立空间已失效"; return false;
                }
                if (At(Target.Point)) return true;
                route = RouteTraversal.Build(Bot.Agent, Target.Node, Target.Point, null, Target.Origin, out reason);
                if (route?.WaitingForDoor == true) { reason = "途中普通门尚未打开；请先用开门指令打开，再前往（不会穿门传送）"; return false; }
                return route != null;
            }
            public virtual bool Acquire()
            {
                WaitReason = NativeProtected(Bot, this);
                if (WaitReason != null) return false;
                if (!BotTaskBridge.Acquire(Bot, out token, this is not StationSession)) { WaitReason = "原版扫描／救援／任务仲裁尚未移交权限"; return false; }
                if (this is not StationSession && !reacquiringReturn && !Preflight(out var reason)) { Blocked = reason; return true; }
                return true;
            }
            public bool Renew(bool returning) => BotTaskBridge.Report(Bot, token, returning ? ItemTaskStage.Returning : ItemTaskStage.Moving);
            public void Suspend()
            { try { Stop(); } finally { var lease = token; token = 0; if (lease != 0 && IdentityValid) BotTaskBridge.Release(Bot, lease, ItemTaskStage.Cancelled); } }
            public bool Revalidate(bool returning)
            {
                Blocked = null; attempts = 0; nextRequest = 0; route = returnRoute = null;
                reacquiringReturn = returning;
                if (!Valid) { Blocked = "当前驻守／前往点不在机器人同层同维度或身份失效"; return false; }
                if (returning) { return true; }
                if (!Preflight(out var reason)) { Blocked = reason; return false; }
                return true;
            }
            public bool CanExecute { get {
                WaitReason = NativeProtected(Bot, this);
                if (WaitReason != null) return false;
                if (BotTaskBridge.CanExecute(Bot, token)) return true;
                WaitReason = "原版紧急状态或扫描仲裁暂时拒绝执行"; return false;
            } }
            bool At(Vector3 point) {
                var d = Bot.Agent.Position - point;
                return MovementOrderPolicy.Arrived(d.x, d.y, d.z)
                    && AIGraph.AIG_CourseNode.TryGetCourseNode(Bot.Agent.DimensionIndex, point, 1, out var node)
                    && node?.Pointer == Bot.Agent.CourseNode?.Pointer;
            }
            void ChooseReturn()
            {
                if (returnChosen) return; returnChosen = true; attempts = 0; nextRequest = 0;
                if (!BotTaskBridge.ResumePosition(Bot, token, out returnPoint))
                    returnPoint = stations.TryGetValue(Bot.Pointer, out var duty) && !duty.Runner.Finished && !duty.Runner.ManualPaused ? duty.Session.Target.Point
                        : follows.TryGetValue(Bot.Pointer, out var follow) && !follow.ManualPaused && Receiver(follow) && follow.Target?.Alive == true
                        ? follow.Target.Position : Bot.SyncValues?.Leader?.Position ?? Bot.Agent.Position;
            }
            public bool Arrived(bool returning)
            {
                if (returning) ChooseReturn();
                return At(returning ? returnPoint : Target.Point);
            }
            protected virtual bool WarpAllowed => true;
            public bool Move(bool returning)
            {
                if (Blocked != null) return false;
                if (returning) ChooseReturn();
                var point = returning ? returnPoint : Target.Point;
                if (returning && returnRoute == null) {
                    if (!AIGraph.AIG_CourseNode.TryGetCourseNode(Bot.Agent.DimensionIndex, point, 1, out var node)) { Blocked = "恢复位置缺少导航节点，已交还原版"; return false; }
                    // Validate the return too; a moving player cannot pull the task across a sealed/closed door.
                    returnRoute = RouteTraversal.Build(Bot.Agent, node, point, null, Target.Origin, out var reason);
                    if (returnRoute == null || returnRoute.WaitingForDoor) { Blocked = "恢复路径暂不可用：" + (reason ?? "门关闭") + "；交还原版"; return false; }
                }
                var current = returning ? returnRoute : route;
                if (current?.Passages != null) foreach (var portal in current.Passages)
                    if (!RouteTraversal.Open(portal)) { Blocked = "路线中的门状态改变，停止移动并交还原版；请打开门后重新下令"; return false; }
                bool warped = returning ? inboundWarped : outboundWarped;
                if (WarpAllowed && !warped && ItemTaskAdapter.TeleportEnabled && Vector3.Distance(Bot.Agent.Position, point) > ItemTaskAdapter.TeleportDistance) {
                    if (returning) inboundWarped = true; else outboundWarped = true;
                    Vector3 landing = point;
                    bool safe = returning || SearchTraversal.Standing(Bot.Agent, point, Target.Node, out landing);
                    if (safe && BotTaskBridge.Teleport(Bot, token, landing)) {
                        Stop(); if (At(point)) return true;
                    }
                }
                if (travel != null && !travel.IsTerminated() && Vector3.Distance(travelPoint, point) < .25f) return true;
                if (Time.time < nextRequest) return true;
                if (attempts++ >= 3) { Blocked = "原版移动三次尝试后仍未到达，停止重复请求"; return false; }
                Stop(); nextRequest = Time.time + 2; travelPoint = point;
                travel = new PlayerBotActionTravel.Descriptor(Bot) { Prio = 100000f, Haste = .5f,
                    DestinationType = PlayerBotActionTravel.Descriptor.DestinationEnum.Position, DestinationPos = point,
                    Radius = .75f, RadiusHeightTolerance = 1, Persistent = false };
                bool accepted = Bot.RequestAction(travel);
                Plugin.Logger.LogInfo("[行动指令] travel bot=" + Name + " return=" + returning + " point=" + point + " accepted=" + accepted + " attempt=" + attempts);
                if (!accepted) travel = null;
                return true;
            }
            public virtual bool OurAction(PlayerBotActionBase.Descriptor desc)
            {
                for (int i = 0; desc != null && i < 8; i++, desc = desc.ParentActionBase?.DescBase)
                    if (travel != null && desc.Pointer == travel.Pointer) return true;
                return false;
            }
            public virtual void Stop()
            {
                var own = travel; travel = null;
                if (Host && Builder.CurrentFloor.Pointer == Target.Floor && PlayerManager.Current?.GetPlayerAgentInSlot(Slot)?.Pointer == Agent
                    && Bot?.Agent?.Owner?.Pointer == Owner && own != null && !own.IsTerminated()) Bot.StopAction(own);
            }
            public void End(PositionTaskStage stage, string reason)
            {
                EndReason = reason; long lease = token; token = 0;
                if (lease != 0 && Host && Builder.CurrentFloor.Pointer == Target.Floor
                    && PlayerManager.Current?.GetPlayerAgentInSlot(Slot)?.Pointer == Agent && Bot?.Agent?.Owner?.Pointer == Owner)
                    BotTaskBridge.Release(Bot, lease, stage == PositionTaskStage.Complete ? ItemTaskStage.Complete : stage == PositionTaskStage.Cancelled ? ItemTaskStage.Cancelled : ItemTaskStage.Failed);
                ItemTaskAdapter.Feedback(Name + "：" + Text(stage) + "；" + reason);
            }
        }
        sealed class StationSession : PositionSession, IStationSession
        {
            public readonly float Radius;
            public readonly GuardDutySpec Guard;
            public readonly bool FromControl;
            protected override bool WarpAllowed => Guard == null;
            PlayerBotActionLook.Descriptor look;
            bool hasHeld;
            readonly List<PlayerBotActionBase.Descriptor> routine = new(8);
            public StationSession(PlayerAIBot bot, MarkedPosition target, float radius,GuardDutySpec guard=null,bool fromControl=false) : base(bot, target) { Radius = radius;Guard=guard;FromControl=fromControl; }
            bool IStationSession.Valid => IdentityValid && (Guard?.Valid ?? true);
            public bool Available => Valid && !(FromControl ? ItemTaskAdapter.PausedWorkBlocks(Bot) : ItemTaskAdapter.Busy(Bot))
                && (!FromControl || !PreLuna.Captain.Control.BotControlApi.Controls(Agent.ToInt64(), Owner.ToInt64()))
                && !ResourceUseAdapter.Owns(Bot) && !SingleKillAdapter.Owns(Bot)
                && NativeProtected(Bot, this) == null && (HasLease ? CanExecute : BotTaskBridge.AutoAvailable(Bot));
            public string Error => EndReason ?? Blocked;
            public override bool Acquire()
            {
                if (!base.Acquire()) return false;
                if (Blocked != null) return true;
                // Clear already-running locomotion too: request hooks only guard future actions.
                routine.Clear();
                foreach (var action in Bot.Actions) {
                    if (routine.Count == 16) break;
                    if (action?.DescBase == null || action.DescBase.IsTerminated() || OurAction(action.DescBase)) continue;
                    if (action.TryCast<PlayerBotActionTravel>() != null || action.TryCast<PlayerBotActionWalk>() != null) routine.Add(action.DescBase);
                }
                try { foreach (var desc in routine) if (!desc.IsTerminated()) Bot.StopAction(desc); }
                finally { routine.Clear(); }
                return true;
            }
            bool IStationSession.Renew() => Renew(false);
            bool IStationSession.Revalidate() => Revalidate(false);
            bool IStationSession.Arrived { get { var d = Bot.Agent.Position - Target.Point; return Guard==null?MovementOrderPolicy.Arrived(d.x,d.y,d.z,hasHeld?Radius:1)&&Bot.Agent.CourseNode?.Pointer==Target.Node.Pointer
                :GuardPolicy.InBound(SearchTraversal.InScope(Bot.Agent.CourseNode,Target.Node,0),MathF.Sqrt(d.x*d.x+d.z*d.z),d.y,hasHeld?Radius:1); } }
            bool IStationSession.Move() => Move(false);
            public void Hold()
            {
                Stop(); hasHeld = true;
                if (Target.Direction.sqrMagnitude < .01f) return;
                look = new PlayerBotActionLook.Descriptor(Bot, true) { Prio = 1f, TargetType = PlayerBotActionLook.TargetTypeEnum.Direction,
                    TargetDir = Target.Direction, Haste = .5f };
                if (!Bot.RequestAction(look)) look = null;
            }
            public override bool OurAction(PlayerBotActionBase.Descriptor desc)
            {
                for (int i = 0; desc != null && i < 8; i++, desc = desc.ParentActionBase?.DescBase)
                    if (look != null && desc.Pointer == look.Pointer || base.OurAction(desc)) return true;
                return false;
            }
            public override void Stop()
            { try { var own = look; look = null; if (IdentityValid && own != null && !own.IsTerminated()) Bot.StopAction(own); } finally { base.Stop(); } }
            void IStationSession.End(bool failed, string reason) => End(failed ? PositionTaskStage.Failed : PositionTaskStage.Cancelled, reason);
        }
    }
}



