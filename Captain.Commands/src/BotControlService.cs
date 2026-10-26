using System;
using System.Collections.Generic;
using System.Linq;
using Player;
using SNetwork;
using LevelGeneration;
using UnityEngine;
using PreLuna.Captain.Control;
namespace PreLuna.Captain.Tasks
{
    internal sealed class BotControlService : IBotControlService, IBotControlCommandService, IBotControlStationService
    {
        internal static readonly BotControlService Instance = new();
        sealed class Entry
        {
            internal Session Session;
            internal ControlTransaction Transaction;
            internal long Ticket;
            internal string Outcome;
        }
        sealed class RecallEntry
        {
            internal Session Session;
            internal float Deadline, Next;
            internal int Attempts;
        }
        readonly Dictionary<long, Entry> entries = new();
        readonly List<RecallEntry> recalling = new();
        long serial;
        internal static bool RecallScope { get; private set; }
        static IntPtr recallBot;
        internal static bool IsRecall(PlayerAIBot bot) => RecallScope && bot?.Pointer == recallBot;
        internal static bool HasState => Instance.entries.Count != 0 || Instance.recalling.Count != 0;
        internal static bool HasActive => Instance.entries.Values.Any(e => e.Transaction.State == BotControlState.Active);
        internal static void Install() { if (!BotControlApi.Register(Instance)) throw new InvalidOperationException("队长接管服务重复注册"); }
        internal static void Shutdown() { Instance.Reset(); BotControlApi.Unregister(Instance); }
        public bool Begin(BotControlTarget target, string source, out long ticket, out string reason)
        {
            ticket = 0; reason = null;
            if (!SNet.IsMaster || SNet.Master == null || GameStateManager.CurrentStateName != eGameStateName.InLevel || Builder.CurrentFloor == null) reason = "仅房主可在关卡内接管";
            var local = PlayerManager.GetLocalPlayerAgent();
            var agent = target.Slot >= 0 && target.Slot < 4 ? PlayerManager.Current?.GetPlayerAgentInSlot(target.Slot) : null;
            var bot = agent?.GetComponent<PlayerAIBot>();
            if (reason == null && (local?.Alive != true || local.Damage?.GrabbedAtAll == true || local.Locomotion == null
                || (int)local.Locomotion.m_currentStateEnum is not (>= 0 and <= 5 or 17))) reason = "本体状态不允许接管";
            if (reason == null && (agent?.Alive != true || agent.Pointer.ToInt64() != target.Agent || agent.Owner?.Pointer.ToInt64() != target.Owner
                || agent.Owner.IsBot != true || bot?.SyncValues == null || agent.Pointer == local.Pointer || agent.IsBeingDestroyed || agent.IsBeingDespawned)) reason = "目标不是当前存活机器人／身份已改变";
            if (reason == null && !ItemRegion.SameSpace(agent, local.CourseNode)) reason = "首版只接管同层同维度机器人";
            if (reason == null && (string.IsNullOrWhiteSpace(source) || source.Length > 64)) reason = "控制来源无效";
            if (reason == null && entries.Values.Any(e => e.Transaction.State is BotControlState.Waiting or BotControlState.Active)) reason = "已有接管会话";
            if (reason == null && recalling.Any(e => e.Session.Target == target)) reason = "该机器人退出召回尚未完成";
            if (reason == null && ControlCommandAdapter.Owns(target.Agent, target.Owner)) reason = "该机器人上一条接管指令仍在交互收尾";
            if (reason == null && !BotTaskBridge.Bind()) reason = "更好 AI 仲裁接口不可用";
            if (reason == null && NativePickupAdapter.Owns(bot)) reason = "特殊搬运任务暂不支持保留接管";
            if (reason != null) return false;
            // Keep only recent terminal tickets; identities and monotonic tickets prevent slot reuse.
            foreach (var old in entries.Where(p => p.Value.Transaction.State is BotControlState.Released or BotControlState.Failed).Select(p => p.Key).ToArray()) entries.Remove(old);
            var session = new Session(bot, local, target);
            try {
                session.Prepare();
                var entry = new Entry { Session = session, Ticket = ++serial, Transaction = new ControlTransaction(session, Time.unscaledTime) };
                entries.Add(entry.Ticket, entry); ticket = entry.Ticket;
                Plugin.Logger.LogInfo("[接管服务] 申请 ticket=" + ticket + " bot=" + agent.PlayerName + " 暂停记录=" + session.Holds.Count);
                return true;
            } catch (Exception error) {
                session.RestoreBeforeActivation(); reason = "暂停任务失败：" + error.GetBaseException().Message;
                Plugin.Logger.LogWarning("[接管服务] " + error); return false;
            }
        }
        static string NativeBlock(PlayerAIBot bot, bool allowOwnedInteraction, bool manual = false)
        {
            if (bot.Actions == null || bot.Actions.Count > 64) return "机器人动作表不可安全核验";
            foreach (var action in bot.Actions) {
                if (action?.DescBase == null || action.DescBase.IsTerminated()) continue;
                if (action.TryCast<RootPlayerBotAction>() != null || action.TryCast<PlayerBotActionTravel>() != null
                    || action.TryCast<PlayerBotActionWalk>() != null || action.TryCast<PlayerBotActionLook>() != null
                    || action.TryCast<PlayerBotActionFollow>() != null || action.TryCast<PlayerBotActionIdle>() != null || action.TryCast<PlayerBotActionTurn>() != null) continue;
                if (manual && (action.TryCast<PlayerBotActionAttack>() != null || action.TryCast<PlayerBotActionUseFirearm>() != null
                    || action.TryCast<PlayerBotActionReloadWeapon>() != null || action.TryCast<PlayerBotActionEquipItem>() != null
                    || action.TryCast<PlayerBotActionEvadeProjectile>() != null || action.TryCast<PlayerBotActionUseBioscan>() != null
                    || action.TryCast<PlayerBotActionBioscanProximity>() != null || action.TryCast<PlayerBotActionUseEnemyScanner>() != null)) continue;
                if (manual && action.TryCast<PlayerBotActionMelee>() != null) {
                    if (action.DescBase.TryCast<PlayerBotActionMelee.Descriptor>()?.Strike != false) return "等待已经释放的近战挥击收尾";
                    continue;
                }
                if (allowOwnedInteraction && (OpenTaskAdapter.Owns(bot) || CollectTaskAdapter.Owns(bot) || ItemTaskAdapter.HasItemTask(bot))
                    && (action.TryCast<PlayerBotActionCollectItem>() != null || action.TryCast<PlayerBotActionUnlock>() != null)) continue;
                return manual ? "等待原版交互或特殊动作收尾，尚未强制中断" : "当前原版动作暂不支持安全暂停，请完成后接管";
            }
            return null;
        }
        public BotControlState Poll(long ticket, out string reason)
        {
            reason = "控制票据已失效";
            if (!entries.TryGetValue(ticket, out var e)) return BotControlState.Failed;
            e.Transaction.Tick(Time.unscaledTime);
            reason = e.Outcome ?? e.Transaction.Reason;
            if (e.Transaction.State == BotControlState.Waiting) reason = e.Session.WaitReason;
            else if (e.Transaction.State == BotControlState.Failed && reason == "等待任务暂停／更好 AI 控制权限超时")
                reason += "：" + e.Session.WaitReason;
            return e.Transaction.State;
        }
        public bool Renew(long ticket) => entries.TryGetValue(ticket, out var e) && e.Transaction.Renew(Time.unscaledTime);
        public void Release(long ticket, bool recall)
        { if (entries.TryGetValue(ticket, out var e)) e.Transaction.Exit(recall); }
        public bool Controls(long agent, long owner) => entries.Values.Any(e => e.Transaction.ActiveAt(Time.unscaledTime)
            && e.Session.Target.Agent == agent && e.Session.Target.Owner == owner && e.Session.Valid);
        internal bool TryActive(long ticket, out PlayerAIBot bot)
        {
            bot = null;
            if (!entries.TryGetValue(ticket, out var e) || !e.Transaction.ActiveAt(Time.unscaledTime) || !e.Session.Valid) return false;
            bot = e.Session.Bot; return true;
        }
        internal bool TryCurrent(out long ticket, out PlayerAIBot bot)
        {
            ticket = 0; bot = null;
            foreach (var e in entries.Values) if (TryActive(e.Ticket, out bot)) { ticket = e.Ticket; return true; }
            return false;
        }
        sealed class CombatPermit : IControlCommandPermit
        {
            readonly Session session;
            internal CombatPermit(Session session) { this.session = session; }
            public bool IdentityValid => session.Valid;
            public bool Acquire(out long token) => session.AcquireCommand(out token, false);
            public bool Report(ItemTaskStage stage) => ((IControlCommandPermit)session).Report(stage);
            public void Hold() => ((IControlCommandPermit)session).Hold();
        }
        internal IControlCommandPermit CommandPermit(long ticket, bool combat = false)
            => !TryActive(ticket, out _) ? null : combat ? new CombatPermit(entries[ticket].Session) : entries[ticket].Session;
        public bool RecallProtected(long agent, long owner) => MovementOrders.ControlStationRecallProtected(agent, owner);
        internal bool ExitStation(long ticket, MarkedPosition mark, out string reason)
        {
            reason = "接管驻守会话已失效";
            if (!TryActive(ticket, out var bot) || ControlCommandAdapter.Busy(ticket, out _)) return false;
            var e = entries[ticket];
            if (!e.Session.Holds.All(h => h.Retained && h.Yielded)) { reason = "旧任务暂停状态已改变，不能提交驻守交接"; return false; }
            // Stop and confirm the previous manual motion before the native station accepts ownership.
            try { e.Session.Quiesce(); }
            catch (Exception error) { reason = "微调动作尚未确认停止：" + error.GetBaseException().Message; return false; }
            if (!MovementOrders.AcceptControlStation(bot, mark, out reason)) return false;
            e.Outcome = "已退出并驻守此地；原指令系统接管驻守管理";
            // Accepted first. No ordinary recall is queued, even if the client polls/repeats Release afterward.
            try { e.Transaction.Exit(false); }
            catch (Exception error) { Plugin.Logger.LogWarning("[驻守交接] 已接单并归还本体；原权限释放回调异常，驻守按原规则等待：" + error); }
            return true;
        }
        public bool Mark(long ticket, BotControlRay ray, out string reason)
        {
            reason = "接管会话已失效";
            if (CaptainMenuInput.OwnsInput || ItemTaskAdapter.CommunicationOpen) { reason = "Q菜单打开或刚关闭，暂不接受重新标记"; return false; }
            return TryActive(ticket, out var bot) && ControlCommandAdapter.Mark(ticket, bot, ray, out reason);
        }
        public bool CommandBusy(long ticket, out string label) => ControlCommandAdapter.Busy(ticket, out label);
        public bool CommandOwns(long agent, long owner) => ControlCommandAdapter.Owns(agent, owner);
        public bool AllowsAction(long agent, long owner, long descriptor) => ControlCommandAdapter.AllowsAction(agent, owner, descriptor);
        public bool AllowsTeleport(long agent, long owner) => ControlCommandAdapter.AllowsTeleport(agent, owner);
        internal static void Tick()
        {
            foreach (var e in Instance.entries.Values.ToArray()) try { e.Transaction.Tick(Time.unscaledTime); }
                catch (Exception error) { Plugin.Logger.LogWarning("[接管服务] 事务失败：" + error); }
            ControlCommandAdapter.Tick();
            foreach (var e in Instance.recalling.ToArray()) Instance.TickRecall(e);
        }
        void TickRecall(RecallEntry e)
        {
            try {
                var s = e.Session;
                if (!s.Valid) { recalling.Remove(e); Plugin.Logger.LogWarning("[接管召回] 身份失效，停止旧对象召回；旧任务保持暂停"); return; }
                if (ControlCommandAdapter.Owns(s.Target.Agent, s.Target.Owner)) return;
                // Start the original recall wait only after atomic child work and native stop acknowledgement.
                if (e.Deadline < 0) e.Deadline = Time.unscaledTime + 30;
                if (Time.unscaledTime >= e.Deadline) { recalling.Remove(e); Plugin.Logger.LogWarning("[接管召回] 等待超时，停止召回；旧任务保持暂停"); return; }
                if (s.Bot.SyncValues.Leader?.Pointer == s.Controller.Pointer) { recalling.Remove(e); Plugin.Logger.LogInfo("[接管召回] 已确认跟随本体；远距离返回沿用原版与更好 AI 传送规则"); return; }
                if (Time.unscaledTime < e.Next || !s.Controller.Alive || s.Controller.Damage?.GrabbedAtAll == true
                    || !ItemRegion.SameSpace(s.Bot.Agent, s.Controller.CourseNode) || NativeBlock(s.Bot, false) != null || !BotTaskBridge.AutoAvailable(s.Bot)) return;
                if (e.Attempts >= 3) { recalling.Remove(e); Plugin.Logger.LogWarning("[接管召回] 三次原版跟随请求未确认，停止重试"); return; }
                var menu = ItemTaskAdapter.NativeMenu;
                if (menu == null) return;
                e.Attempts++; e.Next = Time.unscaledTime + 2;
                RecallScope = true; recallBot = s.Bot.Pointer;
                try { menu.ExecuteCmdCall(s.Controller, s.Bot.Agent); }
                finally { RecallScope = false; recallBot = IntPtr.Zero; }
            } catch (Exception error) { recalling.Remove(e); RecallScope = false; recallBot = IntPtr.Zero; Plugin.Logger.LogWarning("[接管召回] " + error); }
        }
        internal void Reset()
        {
            foreach (var e in entries.Values.ToArray()) try { e.Transaction.Exit(false); } catch (Exception error) { Plugin.Logger.LogWarning("[接管服务] 清理：" + error); }
            ControlCommandAdapter.Reset();
            entries.Clear(); recalling.Clear(); RecallScope = false; recallBot = IntPtr.Zero;
        }
        sealed class Session : IControlTransaction, IControlCommandPermit
        {
            internal readonly PlayerAIBot Bot;
            internal readonly PlayerAgent Controller;
            internal readonly BotControlTarget Target;
            readonly IntPtr floor, controllerOwner, master;
            readonly int controllerSlot, controllerInstance, agentInstance, botInstance;
            long lease;
            bool prepared;
            internal string WaitReason { get; private set; } = "等待机器人任务交接";
            ItemTaskStage commandStage = ItemTaskStage.Moving;
            internal readonly List<BorrowHold> Holds = new();
            internal Session(PlayerAIBot bot, PlayerAgent local, BotControlTarget target)
            { Bot = bot; Controller = local; Target = target; floor = Builder.CurrentFloor.Pointer; controllerOwner = local.Owner.Pointer; controllerSlot = local.PlayerSlotIndex;
                master = SNet.Master.Pointer; controllerInstance = local.GetInstanceID(); agentInstance = bot.Agent.GetInstanceID(); botInstance = bot.GetInstanceID(); }
            public bool Valid { get { try { return SNet.IsMaster && SNet.Master?.Pointer == master && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor?.Pointer == floor
                && PlayerManager.Current?.GetPlayerAgentInSlot(Target.Slot)?.Pointer.ToInt64() == Target.Agent
                && PlayerManager.Current.GetPlayerAgentInSlot(Target.Slot).GetInstanceID() == agentInstance
                && Bot.Agent?.Owner?.Pointer.ToInt64() == Target.Owner && Bot.Agent.Owner.IsBot && Bot.Agent.Alive && !Bot.Agent.IsBeingDestroyed && !Bot.Agent.IsBeingDespawned
                && Bot.Agent.GetComponent<PlayerAIBot>()?.Pointer == Bot.Pointer && Bot.GetInstanceID() == botInstance
                && PlayerManager.Current.GetPlayerAgentInSlot(controllerSlot)?.Pointer == Controller.Pointer && Controller.Owner?.Pointer == controllerOwner
                && PlayerManager.GetLocalPlayerAgent()?.Pointer == Controller.Pointer && Controller.GetInstanceID() == controllerInstance
                && PlayerManager.Current.GetPlayerAgentInSlot(controllerSlot).GetInstanceID() == controllerInstance
                && !Controller.IsBeingDestroyed && !Controller.IsBeingDespawned;
            } catch { return false; } } }
            internal bool Prepare()
            {
                if (RescueOrders.Owns(Bot)) { WaitReason = "等待救援交互收尾"; return false; }
                if (ToolSupplyAdapter.Owns(Bot) || ResourceUseAdapter.Owns(Bot)) { WaitReason = "等待补给应用／核验收尾"; return false; }
                if (SharedMeleeBridge.Owns(Bot)) { WaitReason = "等待协同近战收尾，请先在协同菜单取消或等待完成"; return false; }
                if (!SingleKillAdapter.YieldForControl(Bot)) { WaitReason = "已取消指定击杀，等待已释放的挥击核验"; return false; }
                FocusOrders.RestoreQuiet(Bot);
                if (!prepared) {
                    AutoPickupAdapter.CancelPrompt(Bot);
                    ItemTaskAdapter.SuspendForBorrow(Bot, Holds);
                    MovementOrders.SuspendFollowForControl(Bot, Holds);
                    prepared = true;
                }
                return true;
            }
            public bool Paused { get {
                if (!Prepare()) return false;
                if (!Holds.All(h => h.Retained && h.Yielded)) { WaitReason = "等待旧任务完成本次交互并暂停"; return false; }
                var native = NativeBlock(Bot, false, true);
                if (native != null) { WaitReason = native; return false; }
                WaitReason = "等待更好 AI 交还控制权（扫描预留、动作状态或其他租约）";
                return true;
            } }
            public bool IdentityValid => Valid;
            public bool Acquire() => BotTaskBridge.Acquire(Bot, out lease);
            public bool Renew() => Valid && BotTaskBridge.Report(Bot, lease, commandStage)
                && (commandStage is ItemTaskStage.Interacting or ItemTaskStage.Verifying || BotTaskBridge.CanExecute(Bot, lease));
            internal bool AcquireCommand(out long token, bool requireSafe)
            { token = lease; return token != 0 && Valid && (!requireSafe || !BotTaskBridge.Unsafe(Bot)) && BotTaskBridge.CanExecute(Bot, token); }
            bool IControlCommandPermit.Acquire(out long token) => AcquireCommand(out token, true);
            bool IControlCommandPermit.Report(ItemTaskStage stage)
            { if (!Valid || lease == 0 || !BotTaskBridge.Report(Bot, lease, stage)) return false; commandStage = stage; return true; }
            void IControlCommandPermit.Hold() { commandStage = ItemTaskStage.Moving; }
            public void Quiesce()
            {
                var blocked = NativeBlock(Bot, false, true);
                if (blocked != null) throw new InvalidOperationException(blocked);
                var actions = Bot.Actions;
                var stop = new List<PlayerBotActionBase.Descriptor>();
                foreach (var action in actions) if (action?.DescBase != null && !action.DescBase.IsTerminated() && action.TryCast<RootPlayerBotAction>() == null) stop.Add(action.DescBase);
                foreach (var desc in stop) Bot.StopAction(desc);
                if (Bot.m_queuedActions != null) {
                    stop.Clear(); foreach (var desc in Bot.m_queuedActions) if (desc != null && !desc.IsTerminated() && desc.TryCast<RootPlayerBotAction.Descriptor>() == null) stop.Add(desc);
                    foreach (var desc in stop) Bot.StopAction(desc);
                }
                foreach (var action in Bot.Actions)
                    if (action?.DescBase != null && !action.DescBase.IsTerminated() && action.TryCast<RootPlayerBotAction>() == null)
                        throw new InvalidOperationException("机器人活动动作尚未确认停止");
                if (Bot.m_queuedActions != null) foreach (var desc in Bot.m_queuedActions)
                    if (desc != null && !desc.IsTerminated() && desc.TryCast<RootPlayerBotAction.Descriptor>() == null)
                        throw new InvalidOperationException("机器人排队动作尚未确认停止");
            }
            public void ReleaseLease()
            {
                // The transaction has already surrendered body input. A child may still need its atomic lease.
                var entry = Instance.entries.Values.FirstOrDefault(e => ReferenceEquals(e.Session, this));
                if (entry != null && ControlCommandAdapter.Detach(entry.Ticket, ReleaseActualLease)) return;
                ReleaseActualLease();
            }
            void ReleaseActualLease() { long old = lease; lease = 0; if (old != 0 && Valid) BotTaskBridge.Release(Bot, old, ItemTaskStage.Cancelled); }
            public void RestoreBeforeActivation()
            {
                foreach (var hold in Holds) try { if (Valid) hold.Restore(); else hold.Discard(); } catch (Exception error) { Plugin.Logger.LogWarning("[接管回滚] " + error); }
                Holds.Clear();
            }
            public void KeepPaused()
            {
                if (Valid) MovementOrders.KeepFollowPausedAfterControl(Bot);
                foreach (var hold in Holds) hold.Discard(); Holds.Clear();
                Plugin.Logger.LogInfo("[接管服务] 旧任务保留手动暂停，沿用原期限；可从 Q 继续／取消");
            }
            public void Recall()
            { if (Valid) Instance.recalling.Add(new RecallEntry { Session = this, Deadline = -1 }); }
        }
    }
}
