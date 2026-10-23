using System;
namespace PreLuna.Captain.Tasks
{
    internal enum SingleKillStage { Approaching, Charging, Striking, Continuing, Complete, Cancelled, Failed }
    internal interface ISingleKillSession
    {
        bool Valid { get; }
        bool TargetAlive { get; }
        bool SleepingStable { get; }
        bool Unsafe { get; }
        bool CanExecute { get; }
        bool Arrived { get; }
        bool TravelFailed { get; }
        bool Ready { get; }
        string StrikeFailure { get; }
        bool MeleeTerminated { get; }
        bool ContinuationAtomic { get; }
        bool Renew(SingleKillStage stage);
        bool BeginMelee();
        void Strike();
        string ContinueAttack(float now);
        void Finish(SingleKillStage result, string reason);
    }
    // The native strike has an atomic window; surviving targets retain the same borrowed session.
    internal sealed class SingleKillRunner
    {
        readonly ISingleKillSession session;
        readonly float deadline;
        float strikeDeadline;
        bool cancel;
        public SingleKillStage Stage { get; private set; } = SingleKillStage.Approaching;
        public bool Finished => Stage >= SingleKillStage.Complete;
        public SingleKillRunner(ISingleKillSession session, float now, float timeout)
        { this.session = session; deadline = now + Math.Clamp(timeout, 5, 60); }
        void End(SingleKillStage result, string reason)
        { if (Finished) return; Stage = result; session.Finish(result, reason); }
        public void Abort(string reason) => End(SingleKillStage.Failed, reason);
        public void Cancel()
        { if (Stage == SingleKillStage.Striking || Stage == SingleKillStage.Continuing && session.ContinuationAtomic) cancel = true;
            else End(SingleKillStage.Cancelled, "已取消指定击杀"); }
        public void Tick(float now)
        {
            if (Finished) return;
            if (!session.Valid) { Abort("机器人或关卡会话已改变"); return; }
            if (!session.TargetAlive) { End(SingleKillStage.Complete, "目标已死亡"); return; }
            if (!session.Renew(Stage)) { Abort("任务控制权已失效"); return; }
            if (Stage == SingleKillStage.Continuing) {
                if (cancel) { if (!session.ContinuationAtomic) End(SingleKillStage.Cancelled, "本次补击结束，已取消"); return; }
                if (!session.CanExecute) { Abort("持续接战被救援或更高优先级任务打断"); return; }
                var failure = session.ContinueAttack(now);
                if (failure != null) Abort(failure);
                return;
            }
            if (Stage == SingleKillStage.Striking)
            {
                if (session.MeleeTerminated || now >= strikeDeadline) {
                    if (cancel) End(SingleKillStage.Cancelled, "本次挥击结束，已取消");
                    else {
                        Stage = SingleKillStage.Continuing;
                        if (!session.Renew(Stage) || !session.CanExecute) { Abort("持续接战控制权未被接受"); return; }
                        var failure = session.ContinueAttack(now); if (failure != null) Abort(failure);
                    }
                }
                return;
            }
            if (cancel) { End(SingleKillStage.Cancelled, "已取消指定击杀"); return; }
            if (!session.SleepingStable) { Abort("目标已惊醒或移动，未执行挥击"); return; }
            if (session.Unsafe || !session.CanExecute) { Abort("出现战斗、救援或更高优先级任务"); return; }
            if (now >= deadline) { Abort("接近或蓄力超时"); return; }
            if (Stage == SingleKillStage.Approaching)
            {
                if (session.Arrived) {
                    Stage = SingleKillStage.Charging;
                    if (!session.Renew(Stage) || !session.BeginMelee()) Abort("近战准备未被接受");
                }
                else if (session.TravelFailed) Abort("背后站位无法到达");
                return;
            }
            if (session.MeleeTerminated) { Abort("近战准备被打断"); return; }
            if (session.Ready) {
                Stage = SingleKillStage.Striking; strikeDeadline = now + 5;
                if (!session.Renew(Stage)) { Abort("挥击前任务控制权失效"); return; }
                session.Strike();
            }
            else if (session.StrikeFailure != null) Abort(session.StrikeFailure);
        }
        // Adapter supplies the final native pipeline estimate; optional multiplier retained for older callers/tests.
        public static bool DamageEnough(float health, float limbDamage, float sleeperMultiplier)
            => float.IsFinite(health) && health > 0 && float.IsFinite(limbDamage) && limbDamage > 0
                && float.IsFinite(sleeperMultiplier) && sleeperMultiplier > 0
                && (double)limbDamage * sleeperMultiplier >= health;
    }
}
