using System;
using System.Collections.Generic;
using Gear;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class OpenTaskAdapter
    {
        sealed class Entry { public Session Session; public OpenTaskRunner Runner; }
        static readonly Dictionary<IntPtr, Entry> active = new();
        static readonly List<Entry> snapshot = new(3);
        public static int Count => active.Count;
        internal static bool PausedWorkBlocks(PlayerAIBot bot) => Owns(bot)
            && !(active[bot.Pointer].Runner.ManualPaused && active[bot.Pointer].Runner.Stage == OpenTaskStage.Paused && !active[bot.Pointer].Session.HasControl);
        internal static bool Reserved(IntPtr key) => ControlCommandAdapter.Reserved(key) || System.Linq.Enumerable.Any(active.Values, e => e.Session.Target.Key == key);
        internal static void SuspendForBorrow(PlayerAIBot bot, List<BorrowHold> holds)
        {
            if (!Owns(bot)) return;
            var entry = active[bot.Pointer]; bool changed = !entry.Runner.ManualPaused;
            if (changed) { entry.Runner.Pause(true); entry.Runner.Tick(Time.time); }
            int revision = entry.Runner.PauseRevision;
            bool Current() => active.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, entry) && !entry.Runner.Finished;
            holds.Add(new(() => !Current() || entry.Runner.Stage == OpenTaskStage.Paused && !entry.Session.HasControl,
                () => { if (changed && Current() && entry.Runner.PauseRevision == revision && entry.Runner.ManualPaused) entry.Runner.Pause(false); },
                () => Current() ? entry.Runner.PauseRevision == revision : !Owns(bot)));
        }
        internal static bool RescueYielded(PlayerAIBot bot) => !Owns(bot) || active[bot.Pointer].Runner.Stage == OpenTaskStage.Paused;
        internal static Action SuspendForRescue(PlayerAIBot bot)
        {
            if (!Owns(bot) || active[bot.Pointer].Runner.ManualPaused) return null;
            var entry = active[bot.Pointer]; entry.Runner.Pause(true); entry.Runner.Tick(Time.time);
            return () => { if (active.TryGetValue(bot.Pointer, out var current) && ReferenceEquals(current, entry)) entry.Runner.Pause(false); };
        }
        public static bool Owns(PlayerAIBot bot) => bot != null && active.ContainsKey(bot.Pointer);
        public static string Status(PlayerAIBot bot) => bot != null && active.TryGetValue(bot.Pointer, out var entry)
            ? entry.Session.Target.Label + "·" + Text(entry.Runner.Stage) : null;
        static string Text(OpenTaskStage stage) => stage switch {
            OpenTaskStage.Waiting => "等待权限", OpenTaskStage.Moving => "接近", OpenTaskStage.Unlocking => "开锁／核验",
            OpenTaskStage.Opening => "开启交互", OpenTaskStage.Verifying => "核验开启", OpenTaskStage.Returning => "返回",
            OpenTaskStage.Paused => "手动暂停", OpenTaskStage.Complete => "完成", OpenTaskStage.Cancelled => "取消", _ => "失败" };
        public static bool HasMelee(PlayerAIBot bot) => bot?.Backpack != null && bot.Backpack.TryGetBackpackItem(InventorySlot.GearMelee, out var item)
            && item.Instance?.TryCast<MeleeWeaponThirdPerson>() != null;
        public static int Melters(PlayerAIBot bot)
        {
            if (bot?.Backpack?.AmmoStorage == null || !bot.Backpack.TryGetBackpackItem(InventorySlot.Consumable, out var item) || item.Instance?.ItemDataBlock == null) return 0;
            var data = item.Instance.ItemDataBlock;
            bool matching = item.Instance.TryCast<LockMelterFirstPerson>() != null;
            if (!matching && data.FirstPersonPrefabs != null) foreach (var path in data.FirstPersonPrefabs)
                if (path != null && path.Replace('\\', '/').Contains("/LockMelter/", StringComparison.OrdinalIgnoreCase)) { matching = true; break; }
            if (!matching) return 0;
            var ammo = bot.Backpack.AmmoStorage.GetInventorySlotAmmo(InventorySlot.Consumable);
            return ammo?.HasBulletsLeft == true ? Math.Max(0, ammo.BulletsInPack) : 0;
        }
        public static bool Authorizes(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
            => ControlCommandAdapter.Authorizes(bot, desc) || bot != null && active.TryGetValue(bot.Pointer, out var entry) && entry.Session.OurAction(desc);
        public static void Install(Harmony patches)
        {
            OpenTargetMarks.Install(patches);
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction"), prefix: new HarmonyMethod(typeof(OpenTaskAdapter), nameof(Request)));
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction"), postfix: new HarmonyMethod(typeof(OpenTaskAdapter), nameof(RequestResult)));
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "StartAction"), prefix: new HarmonyMethod(typeof(OpenTaskAdapter), nameof(Start)));
        }
        public static void Cancel(PlayerAIBot bot, bool returning)
        { if (bot != null && active.TryGetValue(bot.Pointer, out var entry)) { entry.Runner.Cancel(returning); entry.Runner.Tick(Time.time); } }
        internal static bool Control(PlayerAIBot bot, bool pause)
        { if (!Owns(bot)) return false; var entry = active[bot.Pointer]; entry.Runner.Pause(pause); entry.Runner.Tick(Time.time); entry.Session.Observe(entry.Runner.Stage); return true; }
        public static void Issue(PlayerAIBot bot, MarkedOpenTarget target)
        {
            void Reject(string reason) { OpenTaskDiagnostics.Write("reject-before-accept", target, "bot=" + bot?.Pointer + " reason=" + reason); ItemTaskAdapter.Feedback(reason); }
            if (!SNet.IsMaster || GameStateManager.CurrentStateName != eGameStateName.InLevel || bot?.Agent?.Alive != true || bot.Agent.Owner?.IsBot != true) {
                Reject("仅关卡内房主可以指挥存活且身份就绪的机器人开门"); return;
            }
            if (active.TryGetValue(bot.Pointer, out var prior)) {
                Reject(prior.Session.Target.Key == target?.Key ? "同一开启任务正在执行，不重复下达" : "该机器人已有开箱／开门任务，请先取消"); return;
            }
            var markReason = OpenTargetMarks.RejectReason(target);
            if (markReason != null) { Reject(markReason); return; }
            if (ItemTaskAdapter.Busy(bot) || SingleKillAdapter.Owns(bot)) { Reject("机器人已有队长任务，请先取消或等待完成"); return; }
            if (Reserved(target.Key)) { Reject("该箱柜／门已有机器人处理"); return; }
            var session = new Session(bot, target);
            string reason = session.Restriction ?? session.CapabilityError;
            if (reason != null) { Reject(reason); return; }
            AutoPickupAdapter.CancelPrompt(bot);
            int reportSlot = bot.Agent.PlayerSlotIndex; long reportOwner = bot.Agent.Owner.Pointer.ToInt64();
            string reportBot = bot.Agent.PlayerName, reportTarget = target.Label;
            session.StatusObserver = (stage, detail) => TaskBoard.Journal.Record(session, reportSlot, reportOwner, reportBot,
                "开箱／开门", reportTarget, stage switch {
                    OpenTaskStage.Waiting => TaskBoardState.Waiting, OpenTaskStage.Paused => TaskBoardState.Paused, OpenTaskStage.Returning => TaskBoardState.Returning,
                    OpenTaskStage.Complete => TaskBoardState.Complete, OpenTaskStage.Failed => TaskBoardState.Failed,
                    OpenTaskStage.Cancelled => TaskBoardState.Cancelled, _ => TaskBoardState.Executing }, Text(stage),
                detail ?? (stage == OpenTaskStage.Waiting ? "等待原有动作或任务权限" : "按原版锁具与门状态核验；不重发已提交的开门请求"), Time.time);
            session.StatusObserver(OpenTaskStage.Waiting, "开启任务已接单，等待原有动作或任务权限");
            active.Add(bot.Pointer, new() { Session = session, Runner = new OpenTaskRunner(session, Time.time) });
            OpenTaskDiagnostics.Write("accepted", target, "bot=" + bot.Pointer);
            ItemTaskAdapter.Feedback(bot.Agent.PlayerName + "：已接单 " + target.Label + "（" + target.LockText + "）");
        }
        public static void Tick()
        {
            if (active.Count == 0) return;
            snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var entry in snapshot) try { entry.Runner.Tick(Time.time); entry.Session.Observe(entry.Runner.Stage); }
                catch (Exception error) { Plugin.Logger.LogError("[开门诊断] task-exception " + error); entry.Runner.Abort("开启任务检查失败：" + error.Message); }
            snapshot.Clear();
        }
        public static void Reset()
        {
            snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var entry in snapshot) try { entry.Runner.Abort("关卡或房主状态重置"); }
                catch (Exception error) { Plugin.Logger.LogWarning("开启任务清理：" + error.Message); }
            snapshot.Clear(); OpenTargetMarks.Reset();
        }
        static bool Allow(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (bot == null || !active.TryGetValue(bot.Pointer, out var entry) || !entry.Session.HasControl || entry.Session.OurAction(desc)) return true;
            if (desc?.TryCast<PlayerBotActionRevive.Descriptor>() != null) {
                // Revive is an emergency. A melter already applied remains in the real lock state.
                entry.Runner.Abort("让位给原版救援"); return true;
            }
            entry.Session.ActionTrace("blocked-action", desc, false); return false;
        }
        static bool Request(PlayerAIBot __instance, PlayerBotActionBase.Descriptor desc, ref bool __result)
        { if (Allow(__instance, desc)) return true; __result = false; return false; }
        static bool Start(PlayerAIBot __instance, PlayerBotActionBase.Descriptor desc) {
            bool allowed = Allow(__instance, desc);
            if (__instance != null && active.TryGetValue(__instance.Pointer, out var entry)) entry.Session.ActionTrace("start-action", desc, allowed);
            return allowed;
        }
        static void RequestResult(PlayerAIBot __instance, PlayerBotActionBase.Descriptor desc, bool __result) {
            if (__instance != null && active.TryGetValue(__instance.Pointer, out var entry)) {
                entry.Session.ActionTrace("request-result", desc, __result);
                if (!__result) entry.Session.NativeRejection(desc);
            }
        }

        internal sealed class Session : IOpenTaskSession
        {
            internal Action<OpenTaskStage, string> StatusObserver;
            readonly PlayerAIBot bot;
            readonly IntPtr owner, identity;
            readonly int slot;
            readonly HashSet<IntPtr> attempted = new();
            long token;
            readonly IControlCommandPermit controlPermit;
            PlayerBotActionTravel.Descriptor travel;
            PlayerBotActionUnlock.Descriptor unlock;
            LG_WeakLock selectedLock;
            Vector3 approach, travelPosition;
            bool hasApproach, outboundWarped, inboundWarped, applied;
            float nextTravel, diagnosticAt, unlockEnded = -1;
            float observeAt;
            string observed;
            OpenTaskStage? observedStage;
            readonly HashSet<string> actionRecords = new();
            public MarkedOpenTarget Target { get; }
            public Session(PlayerAIBot bot, MarkedOpenTarget target, IControlCommandPermit controlPermit = null)
            { this.bot = bot; Target = target; this.controlPermit = controlPermit; owner = bot.Agent.Owner.Pointer; identity = bot.Agent.Pointer; slot = bot.Agent.PlayerSlotIndex; }
            bool ValidBot => controlPermit?.IdentityValid != false && SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor?.Pointer == Target.Floor
                && bot?.Agent?.Alive == true && bot.Agent.Owner?.IsBot == true && bot.Agent.Owner.Pointer == owner
                && PlayerManager.Current?.GetPlayerAgentInSlot(slot)?.Pointer == identity;
            public bool Valid => ValidBot && Target.SameSpace(bot.Agent);
            public string Restriction => Target.Restriction;
            public bool IsOpen => Target.IsOpen;
            public bool IsOpening => Target.Opening;
            public bool HasControl => token != 0;
            public void ActionTrace(string phase, PlayerBotActionBase.Descriptor desc, bool allowed)
            {
                // Cap per task; routine combat requests cannot flood logs while a command owns the bot.
                string key = phase + ":" + desc?.Pointer + ":" + allowed;
                if (actionRecords.Count >= 40 || !actionRecords.Add(key)) return;
                OpenTaskDiagnostics.Write(phase, Target, "bot=" + bot.Pointer + " allowed=" + allowed
                    + " desc=" + OpenTaskDiagnostics.Descriptor(desc));
            }
            public void Observe(OpenTaskStage stage)
            {
                StatusObserver?.Invoke(stage, null);
                bool changed = observedStage != stage;
                if (!changed && Time.time < observeAt) return;
                observedStage = stage; observeAt = Time.time + .5f;
                var action = unlock?.ActionBase?.TryCast<PlayerBotActionUnlock>();
                string state = OpenTaskDiagnostics.Snapshot(Target) + " unlock=" + OpenTaskDiagnostics.Descriptor(unlock)
                    + " chosen=" + OpenTaskDiagnostics.Descriptor(action?.m_chosenMethod)
                    + " nativeMethod=" + action?.m_method + " used=" + unlock?.UsedMethod;
                if (!changed && state == observed) return;
                observed = state;
                Plugin.Logger.LogInfo("[开门诊断] phase=state stage=" + stage + " target=" + Target.Label + " bot=" + bot.Pointer + " " + state);
            }
            public void NativeRejection(PlayerBotActionBase.Descriptor desc)
            {
                if (desc == null || !OurAction(desc) || actionRecords.Count >= 40
                    || !actionRecords.Add("native-rejection:" + desc.Pointer)) return;
                try {
                    OpenTaskDiagnostics.Write("native-rejection", Target, "bot=" + bot.Pointer
                        + " nativeForbidden=" + bot.IsActionForbidden(desc) + " request=" + OpenTaskDiagnostics.Descriptor(desc));
                    int records = 0;
                    if (bot.m_queuedActions != null) foreach (var queued in bot.m_queuedActions) {
                        if (++records > 24) break;
                        if (queued != null && !queued.IsTerminated() && !queued.IsActionAllowed(desc))
                            OpenTaskDiagnostics.Write("native-blocker", Target, "source=queued desc=" + OpenTaskDiagnostics.Descriptor(queued));
                    }
                    if (bot.Actions != null) foreach (var action in bot.Actions) {
                        if (++records > 24) break;
                        var other = action?.DescBase;
                        if (other != null && !other.IsTerminated() && !action.IsActionAllowed(desc))
                            OpenTaskDiagnostics.Write("native-blocker", Target, "source=active desc=" + OpenTaskDiagnostics.Descriptor(other));
                    }
                } catch (Exception error) { Plugin.Logger.LogWarning("[开门诊断] native-rejection-inspect-failed " + error); }
            }
            public bool NeedsUnlock => Target.NextLock != null;
            public string CapabilityError => !Target.SameSpace(bot.Agent) ? "目标不在机器人当前维度与关卡分层"
                : Target.NeedsMelee && !HasMelee(bot) ? "该机器人近战装备尚未就绪"
                : Melters(bot) < Target.NeededMelters ? "熔锁器已换下、耗尽或不足以处理剩余入侵锁" : null;
            bool NativeBusy()
            {
                if (bot.Actions == null) return true;
                foreach (var action in bot.Actions) {
                    var desc = action?.DescBase;
                    if (desc == null || desc.IsTerminated()) continue;
                    if (action.TryCast<PlayerBotActionUnlock>() != null || action.TryCast<PlayerBotActionUseLockMelter>() != null
                        || action.TryCast<PlayerBotActionCollectItem>() != null || action.TryCast<PlayerBotActionShareResourcePack>() != null
                        || action.TryCast<PlayerBotActionGatherDeployables>() != null || action.TryCast<PlayerBotActionDeploySentryGun>() != null
                        || action.TryCast<PlayerBotActionDeployTripMine>() != null || action.TryCast<PlayerBotActionCarryExpeditionItem>() != null) return true;
                }
                return false;
            }
            public bool Acquire()
            { ItemActionAuthorization.Quiesce(bot); return !NativeBusy() && (controlPermit != null ? controlPermit.Acquire(out token) : BotTaskBridge.Acquire(bot, out token)); }
            public void Suspend()
            { try { StopMovement(); FinishUnlock(); } finally { var lease = token; token = 0; if (controlPermit != null) controlPermit.Hold(); else if (lease != 0 && ValidBot) BotTaskBridge.Release(bot, lease, ItemTaskStage.Cancelled); hasApproach = false; } }
            public bool Renew(OpenTaskStage stage)
            {
                var value = stage == OpenTaskStage.Verifying ? ItemTaskStage.Verifying : stage == OpenTaskStage.Returning ? ItemTaskStage.Returning : ItemTaskStage.Moving;
                return controlPermit != null ? controlPermit.Report(value) : BotTaskBridge.Report(bot, token, value);
            }
            // Unlocking deliberately keeps broker stage Moving: native unlock owns child travel/equip/look.
            // Reporting Interacting here would make AI r14 stop that child travel on every heartbeat.
            public bool CanExecute => BotTaskBridge.CanExecute(bot, token) && !BotTaskBridge.Unsafe(bot);
            Vector3 Destination(bool returning)
            {
                if (returning) {
                    if (BotTaskBridge.ResumePosition(bot, token, out var point)) return point;
                    var leader = bot.SyncValues?.Leader;
                    if (leader?.Alive != true || !ItemRegion.SameSpace(bot.Agent, leader.CourseNode)) throw new InvalidOperationException("有效领队或原扫描位置不可达");
                    return leader.Position;
                }
                var next = Target.NextLock;
                if (next != null) {
                    if (MarkedOpenTarget.Kind(next) == OpenLockKind.Melee && next.m_meleeDamage != null) return next.m_meleeDamage.DamageTargetPos;
                    if (next.m_lockMelterAlignHackable != null && MarkedOpenTarget.Kind(next) == OpenLockKind.Hackable) return next.m_lockMelterAlignHackable.position;
                    return next.transform.position;
                }
                return Target.OpenPoint;
            }
            public bool AtDestination(bool returning)
            {
                var point = Destination(returning); var delta = bot.Agent.Position - point;
                return ItemArrivalPolicy.Interactable(delta.x, delta.y, delta.z, returning, ItemTaskAdapter.PickupReach)
                    && (returning || Target.Visible(bot.Agent.EyePosition, point));
            }
            bool Standing(Vector3 point, out Vector3 standing)
            {
                standing = default;
                var toward = bot.Agent.Position - point; toward.y = 0;
                if (toward.sqrMagnitude < .01f) toward = Vector3.forward;
                toward = toward.normalized; var side = Vector3.Cross(Vector3.up, toward);
                for (int ring = 0; ring < 2; ring++) for (int dir = 0; dir < 8; dir++) {
                    var direction = dir == 0 ? toward : dir == 1 ? side : dir == 2 ? -side : dir == 3 ? -toward
                        : dir == 4 ? (toward + side).normalized : dir == 5 ? (toward - side).normalized
                        : dir == 6 ? (-toward + side).normalized : (-toward - side).normalized;
                    var desired = point + direction * (ring == 0 ? 1.4f : 1f);
                    if (!PlayerAIBot.SnapPositionToNav(desired, out var candidate, 2, 8)
                        || Math.Abs(candidate.x - desired.x) > .75f || Math.Abs(candidate.z - desired.z) > .75f || Math.Abs(candidate.y - desired.y) > 2.1f
                        || !ItemArrivalPolicy.Interactable(candidate.x - point.x, candidate.y - point.y, candidate.z - point.z, false, ItemTaskAdapter.PickupReach)
                        || !AIGraph.AIG_CourseNode.TryGetCourseNode(bot.Agent.DimensionIndex, candidate, 1, out var node) || !ItemRegion.SameSpace(bot.Agent, node)
                        || Target.Door != null && node.Pointer != Target.Node.Pointer
                        || Physics.CheckCapsule(candidate + Vector3.up * .35f, candidate + Vector3.up * 1.4f, .25f, LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore)
                        || !Target.Visible(candidate + (bot.Agent.EyePosition - bot.Agent.Position), point)) continue;
                    standing = candidate; return true;
                }
                return false;
            }
            public bool Move(bool returning)
            {
                var point = Destination(returning); var destination = point;
                if (!returning) {
                    if (!hasApproach) {
                        if (!Standing(point, out approach)) {
                            if (Time.time >= diagnosticAt) { diagnosticAt = Time.time + 8; ItemTaskAdapter.Feedback(Target.Label + "：尚未找到同侧可达站位，请检查柜门／门按钮前障碍"); }
                            return false;
                        }
                        hasApproach = true;
                    }
                    destination = approach;
                }
                if (!(returning ? inboundWarped : outboundWarped) && ItemTaskAdapter.TeleportEnabled
                    && Vector3.Distance(bot.Agent.Position, point) > ItemTaskAdapter.TeleportDistance && BotTaskBridge.Teleport(bot, token, destination)) {
                    if (returning) inboundWarped = true; else outboundWarped = true;
                    StopMovement(); if (AtDestination(returning)) return true;
                }
                if (travel != null && !travel.IsTerminated() && Vector3.Distance(travelPosition, destination) < .5f) return true;
                if (Time.time < nextTravel) return false;
                StopMovement(); nextTravel = Time.time + 1; travelPosition = destination;
                travel = new PlayerBotActionTravel.Descriptor(bot) { Prio = 100000f, Haste = .5f,
                    DestinationType = PlayerBotActionTravel.Descriptor.DestinationEnum.Position,
                    DestinationPos = destination, Radius = returning ? 1 : .25f, RadiusHeightTolerance = 1, Persistent = false };
                return bot.RequestAction(travel);
            }
            public void StopMovement()
            {
                var own = travel; travel = null;
                if (ValidBot && own != null && !own.IsTerminated()) bot.StopAction(own);
            }
            public bool BeginUnlock()
            {
                selectedLock = Target.NextLock; unlockEnded = -1;
                if (selectedLock == null) return true;
                var kind = MarkedOpenTarget.Kind(selectedLock);
                if (kind == OpenLockKind.Melting) return true;
                if (attempted.Contains(selectedLock.Pointer) || kind == OpenLockKind.Unsupported || CapabilityError != null) return false;
                attempted.Add(selectedLock.Pointer);
                unlock = new PlayerBotActionUnlock.Descriptor(bot) { Prio = 100000f,
                    TargetType = Target.Box != null ? PlayerBotActionUnlock.Descriptor.TargetTypeEnum.Container : PlayerBotActionUnlock.Descriptor.TargetTypeEnum.Door,
                    Method = kind == OpenLockKind.Hackable ? PlayerBotActionUnlock.Descriptor.MethodEnum.Melt : PlayerBotActionUnlock.Descriptor.MethodEnum.Melee,
                    Lock = selectedLock, TargetGO = Target.Root, TargetPosition = selectedLock.transform.position, CourseNode = Target.Node };
                OpenTaskDiagnostics.Write("unlock-submit", Target, "bot=" + bot.Pointer + " desc=" + unlock.Pointer
                    + " method=" + unlock.Method + " lock=" + selectedLock.Pointer + " targetGO=" + unlock.TargetGO?.Pointer
                    + " position=" + unlock.TargetPosition + " node=" + unlock.CourseNode?.Pointer);
                bool result = bot.RequestAction(unlock);
                OpenTaskDiagnostics.Write("unlock-result", Target, "accepted=" + result + " action=" + OpenTaskDiagnostics.Descriptor(unlock));
                return result;
            }
            public bool LockResolved => selectedLock == null || MarkedOpenTarget.Kind(selectedLock) == OpenLockKind.None;
            public bool UnlockAtomic {
                get {
                    var method = unlock?.ActionBase?.TryCast<PlayerBotActionUnlock>()?.m_chosenMethod;
                    var melter = method?.ActionBase?.TryCast<PlayerBotActionUseLockMelter>();
                    var melee = method?.TryCast<PlayerBotActionMelee.Descriptor>();
                    return method != null && !method.IsTerminated() && (melter?.m_state == PlayerBotActionUseLockMelter.State.ApplyMelter
                        || melee?.State == PlayerBotActionMelee.Descriptor.StateEnum.Strike || melee?.State == PlayerBotActionMelee.Descriptor.StateEnum.Cooldown);
                }
            }
            public bool UnlockFailed {
                get {
                    if (unlock == null || !unlock.IsTerminated() || MarkedOpenTarget.Kind(selectedLock) == OpenLockKind.Melting) return false;
                    if (unlockEnded < 0) unlockEnded = Time.time;
                    return !LockResolved && Time.time - unlockEnded > 1;
                }
            }
            public void FinishUnlock()
            {
                var own = unlock; unlock = null; selectedLock = null; hasApproach = false;
                if (ValidBot && own != null && !own.IsTerminated()) bot.StopAction(own);
            }
            public float OpenDuration => Target.Box != null ? Target.Box.m_intOpen?.InteractDuration ?? float.NaN
                : Target.Button?.OperationDelay ?? float.NaN;
            public bool ApplyOpen()
            {
                string reason = applied ? "已提交，禁止重复" : Target.Opening ? "门已在开启动画中" : !CanExecute ? "权限／战斗状态不允许"
                    : !AtDestination(false) ? "未在可见交互范围" : NeedsUnlock ? "锁尚未解除" : Restriction;
                if (reason != null) { OpenTaskDiagnostics.Write("open-rejected", Target, "reason=" + reason); return false; }
                if (Target.Box != null) {
                    var interact = Target.Box.m_intOpen;
                    if (interact == null || !interact.IsActive || interact.IsBlocked || !interact.PlayerCanInteract(bot.Agent)) return false;
                    applied = true; interact.TriggerInteractionAction(bot.Agent); return true;
                }
                if (Target.Door == null || Target.Button == null || !Target.Button.m_enabled
                    || !Target.Button.gameObject.activeInHierarchy || !Target.Door.InteractionAllowed
                    || Target.Door.InAnimation || Target.Door.m_inButtonOperation || !Target.Button.CanInteract()
                    || Target.IsOpen) {
                    OpenTaskDiagnostics.Write("door-rejected", Target, "原版当前侧按钮／门状态条件拒绝，未调用共用接口"); return false;
                }
                OpenTaskDiagnostics.Write("door-call", Target, "bot=" + bot.Pointer + " method=LG_WeakDoor.AttemptOpenCloseInteraction onlyUnlock=False");
                // This native request can toggle the door. Mark before invoking so an exception
                // or delayed network acknowledgement cannot cause a second request to close it.
                applied = true;
                Target.Door.AttemptOpenCloseInteraction(false);
                OpenTaskDiagnostics.Write("door-return", Target, "submitted=True opened=" + Target.IsOpen + " opening=" + Target.Opening);
                return true; // void request is submission only; runner verifies real door state.
            }
            public bool OurAction(PlayerBotActionBase.Descriptor desc)
            {
                bool live = unlock != null && !unlock.IsTerminated() && selectedLock != null;
                var chosen = live ? unlock.ActionBase?.TryCast<PlayerBotActionUnlock>()?.m_chosenMethod : null;
                for (int i = 0; desc != null && i < 8; i++) {
                    if (desc.Pointer == travel?.Pointer || desc.Pointer == unlock?.Pointer) return true;
                    var melter = desc.TryCast<PlayerBotActionUseLockMelter.Descriptor>();
                    var melee = desc.TryCast<PlayerBotActionMelee.Descriptor>();
                    bool lockTarget = melee?.TargetGameObject != null && selectedLock != null
                        && (melee.TargetGameObject.Pointer == selectedLock.gameObject.Pointer
                            || melee.TargetGameObject.Pointer == selectedLock.m_meleeRoot?.Pointer
                            || melee.TargetGameObject.Pointer == selectedLock.m_meleeDamage?.gameObject?.Pointer
                            || melee.TargetGameObject.GetComponentInParent<LG_WeakLock>()?.Pointer == selectedLock.Pointer);
                    if (OpenTaskPolicy.UnlockChild(live, chosen != null && desc.Pointer == chosen.Pointer,
                        melee != null, lockTarget, melter != null, selectedLock != null && melter?.Lock?.Pointer == selectedLock.Pointer)) return true;
                    desc = desc.ParentActionBase?.DescBase;
                }
                return false;
            }
            public void Finish(OpenTaskStage result, string reason)
            {
                StatusObserver?.Invoke(result, reason);
                OpenTaskDiagnostics.Write("finished", Target, "result=" + result + " reason=" + reason
                    + " unlock=" + OpenTaskDiagnostics.Descriptor(unlock)
                    + " chosen=" + OpenTaskDiagnostics.Descriptor(unlock?.ActionBase?.TryCast<PlayerBotActionUnlock>()?.m_chosenMethod));
                if (controlPermit == null) active.Remove(bot.Pointer);
                try { try { FinishUnlock(); } finally { StopMovement(); } }
                catch (Exception error) { Plugin.Logger.LogWarning("开箱／开门动作清理：" + error.Message); }
                try { if (controlPermit != null) controlPermit.Hold(); else if (token != 0) BotTaskBridge.Release(bot, token,
                    result == OpenTaskStage.Complete ? ItemTaskStage.Complete : result == OpenTaskStage.Cancelled ? ItemTaskStage.Cancelled : ItemTaskStage.Failed); }
                catch (Exception error) { Plugin.Logger.LogWarning("开箱／开门权限清理：" + error.Message); }
                if (SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel)
                    ItemTaskAdapter.Feedback(Target.Label + "：" + reason);
            }
        }
    }
}
