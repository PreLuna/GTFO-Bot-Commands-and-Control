using System;
namespace PreLuna.Captain.Tasks
{
    internal enum OpenTaskStage { Waiting, Moving, Unlocking, Opening, Verifying, Returning, Paused, Complete, Cancelled, Failed }
    internal enum OpenLockKind { None, Melee, Hackable, Melting, Unsupported }
    internal static class OpenTaskPolicy
    {
        // Native ChooseMethod may request the selected descriptor before attaching ParentActionBase.
        // A melee fallback must identify the actual selected lock, never merely the door root.
        public static bool UnlockChild(bool live, bool exactChosen, bool melee, bool lockTarget, bool melter, bool sameLock)
            => live && (exactChosen || melee && lockTarget || melter && sameLock);
        public static OpenLockKind Lock(bool locked, int status, int type) => status == 2 ? OpenLockKind.Melting : !locked ? OpenLockKind.None
            : status == 0 && type == 1 ? OpenLockKind.Melee
            : status == 1 && type == 2 ? OpenLockKind.Hackable : OpenLockKind.Unsupported;
        public static bool CanUse(bool alive, bool melee, int melters, bool needsMelee, int neededMelters)
            => alive && (!needsMelee || melee) && neededMelters >= 0 && melters >= neededMelters;
        public static bool Fresh(float now, float marked, float lifetime)
            => float.IsFinite(now) && float.IsFinite(marked) && float.IsFinite(lifetime)
                && lifetime > 0 && now >= marked && now - marked <= lifetime;
    }
    internal interface IOpenTaskSession
    {
        bool Valid { get; }
        string Restriction { get; }
        bool IsOpen { get; }
        bool IsOpening { get; }
        bool Acquire();
        bool Renew(OpenTaskStage stage);
        bool CanExecute { get; }
        bool AtDestination(bool returning);
        bool Move(bool returning);
        void StopMovement();
        void Suspend();
        bool NeedsUnlock { get; }
        string CapabilityError { get; }
        bool BeginUnlock();
        bool LockResolved { get; }
        bool UnlockAtomic { get; }
        bool UnlockFailed { get; }
        void FinishUnlock();
        float OpenDuration { get; }
        bool ApplyOpen();
        void Finish(OpenTaskStage result, string reason);
    }
    // Finite, one-shot native operations. Unlock success is verified from lock state, not descriptor status.
    internal sealed class OpenTaskRunner
    {
        readonly IOpenTaskSession session;
        float created;
        float clock, started, pauseElapsed, acquireAt;
        bool acquired, cancel, returnRequested;
        bool manualPause, manuallySuspended, resumeReturning;
        readonly bool returnAfterOperation;
        public bool ManualPaused => manualPause;
        public int PauseRevision { get; private set; }
        public void Pause(bool value) { if (!Finished) PauseRevision++; if (!Finished && manualPause != value) { manualPause = value; pauseElapsed = 0; } }
        int locks;
        public OpenTaskStage Stage { get; private set; } = OpenTaskStage.Waiting;
        public bool Finished => Stage >= OpenTaskStage.Complete;
        public OpenTaskRunner(IOpenTaskSession session, float now, bool returnAfterOperation = true)
        { this.session = session; created = clock = started = acquireAt = now; this.returnAfterOperation = returnAfterOperation; }
        void Set(OpenTaskStage stage, float now) { Stage = stage; started = now; }
        void End(OpenTaskStage result, string reason)
        { if (Finished) return; Stage = result; session.Finish(result, reason); }
        public void Abort(string reason) => End(OpenTaskStage.Failed, reason);
        public void Cancel(bool returning)
        {
            cancel = true; returnRequested |= returning;
            if (Stage == OpenTaskStage.Waiting && !returning) End(OpenTaskStage.Cancelled, "已取消开箱／开门");
        }
        public void Tick(float now)
        {
            if (Finished) return;
            if (!float.IsFinite(now) || now < clock || !session.Valid) { Abort("关卡、房主、目标或机器人身份已改变"); return; }
            float delta = now - clock; clock = now;
            var restriction = session.Restriction;
            if (restriction != null) { Abort(restriction); return; }
            if (manualPause && !cancel && Stage != OpenTaskStage.Unlocking && Stage != OpenTaskStage.Verifying && !session.IsOpening) {
                if (!manuallySuspended) { resumeReturning = Stage == OpenTaskStage.Returning; session.Suspend(); acquired = false; manuallySuspended = true; Stage = OpenTaskStage.Paused; }
                created += delta; started += delta; pauseElapsed += delta; acquireAt = now;
                if (pauseElapsed > 600) Abort("手动暂停超过10分钟，释放开门任务");
                return;
            }
            if (manuallySuspended) { manuallySuspended = false; Stage = OpenTaskStage.Waiting; }
            if (!acquired) {
                if (now - acquireAt >= 15) { Abort("等待现有动作与任务权限超时"); return; }
                if (!session.Acquire()) return;
                acquired = true; Set(resumeReturning ? OpenTaskStage.Returning : OpenTaskStage.Moving, now);
            }
            if (!session.Renew(Stage)) { Abort("任务控制令牌失效"); return; }
            bool atomic = Stage == OpenTaskStage.Verifying || Stage == OpenTaskStage.Unlocking && session.UnlockAtomic;
            if (cancel && !atomic) {
                session.StopMovement();
                if (!returnRequested) { End(OpenTaskStage.Cancelled, "已取消，保留锁具与容器的实际状态"); return; }
                if (Stage != OpenTaskStage.Returning) { session.FinishUnlock(); Set(OpenTaskStage.Returning, now); }
            }
            if (!atomic && !session.CanExecute) { Abort("已让位给战斗、救援或更高优先级任务"); return; }
            if (Stage == OpenTaskStage.Returning) {
                if (session.AtDestination(true)) { End(cancel ? OpenTaskStage.Cancelled : OpenTaskStage.Complete, "已返回原扫描／跟随任务"); return; }
                if (now - started >= 30) { Abort("操作已结束，但返程超时"); return; }
                session.Move(true); return;
            }
            if (Stage != OpenTaskStage.Verifying && now - created >= 120) { Abort("开箱／开门任务总超时"); return; }
            if (session.IsOpen) {
                session.StopMovement(); session.FinishUnlock();
                if (cancel && !returnRequested) { End(OpenTaskStage.Cancelled, "目标已开启，已取消后续步骤"); return; }
                if (!returnAfterOperation && !returnRequested) { End(OpenTaskStage.Complete, "目标开启已核验，交回接管微调"); return; }
                Set(OpenTaskStage.Returning, now); return;
            }
            if (session.IsOpening && Stage != OpenTaskStage.Verifying) {
                session.StopMovement(); session.FinishUnlock(); Set(OpenTaskStage.Verifying, now); return;
            }
            if (Stage == OpenTaskStage.Moving) {
                var capability = session.CapabilityError;
                if (capability != null) { Abort(capability); return; }
                if (!session.AtDestination(false)) { session.Move(false); return; }
                session.StopMovement();
                if (session.NeedsUnlock) {
                    if (++locks > 8) { Abort("锁具状态反复变化，停止重复开锁"); return; }
                    Set(OpenTaskStage.Unlocking, now);
                    if (!session.BeginUnlock()) Abort("原版拒绝开锁动作");
                } else Set(OpenTaskStage.Opening, now);
                return;
            }
            if (Stage == OpenTaskStage.Unlocking) {
                if (session.LockResolved) { session.FinishUnlock(); Set(OpenTaskStage.Moving, now); return; }
                if (session.UnlockFailed || now - started >= 30) { Abort("锁具未确认解锁；停止重试，保留实际消耗"); return; }
                return;
            }
            if (Stage == OpenTaskStage.Opening) {
                if (session.NeedsUnlock || !session.AtDestination(false)) { Set(OpenTaskStage.Moving, now); return; }
                float duration = session.OpenDuration;
                if (!float.IsFinite(duration) || duration < 0 || duration > 15) { Abort("开启交互时长无效"); return; }
                if (now - started < Math.Max(.1f, duration)) return;
                if (!session.ApplyOpen()) { Abort("原版拒绝开启交互，未重发"); return; }
                Set(OpenTaskStage.Verifying, now); return;
            }
            if (Stage == OpenTaskStage.Verifying && now - started >= 10)
                Abort("已提交开启，但未确认目标打开；不会再次按按钮造成关门");
        }
    }
}
