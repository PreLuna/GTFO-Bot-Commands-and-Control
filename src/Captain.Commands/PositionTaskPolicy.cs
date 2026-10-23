using System;
namespace PreLuna.Captain.Tasks
{
    internal enum PositionTaskStage { Waiting, Moving, Paused, Returning, Complete, Failed, Cancelled }
    internal interface IPositionTaskSession
    {
        bool Valid { get; }
        bool Acquire();
        bool Renew(bool returning);
        bool CanExecute { get; }
        string Blocked { get; }
        bool Arrived(bool returning);
        bool Move(bool returning);
        void Stop();
        void Suspend();
        bool Revalidate(bool returning);
        void End(PositionTaskStage stage, string reason);
    }
    // Run one lease through outbound movement and return. Native action success alone is not arrival.
    internal sealed class PositionTaskRunner
    {
        readonly IPositionTaskSession session;
        readonly float timeout;
        float started, last, elapsed, returnAt, pauseElapsed;
        bool acquired, cancelled, returnOnCancel, manualPause, resumeReturn, revalidate;
        public bool ManualPaused => manualPause;
        public PositionTaskStage Stage { get; private set; } = PositionTaskStage.Waiting;
        public bool Finished => Stage is PositionTaskStage.Complete or PositionTaskStage.Failed or PositionTaskStage.Cancelled;
        public PositionTaskRunner(IPositionTaskSession session, float now, float timeout)
        { this.session = session; started = last = now; this.timeout = timeout; }
        public void Cancel(bool returning) { cancelled = true; returnOnCancel |= returning; }
        public int PauseRevision { get; private set; }
        public void Pause(bool value) { if (Finished) return; PauseRevision++; if (manualPause == value) return; manualPause = value; pauseElapsed = 0; if (!value) revalidate = true; }
        public void Abort(string reason) { if (!Finished) Finish(PositionTaskStage.Failed, reason); }
        public void Tick(float now)
        {
            if (Finished) return;
            if (!float.IsFinite(now) || now < last) { Finish(PositionTaskStage.Failed, "任务时钟已失效"); return; }
            float delta = Math.Max(0, now - last); last = now;
            if (!session.Valid) { Finish(PositionTaskStage.Failed, "机器人／关卡／目标身份已失效"); return; }
            if (cancelled && (!returnOnCancel || !acquired)) { Finish(PositionTaskStage.Cancelled, "已取消前往；没有改变物品或原版交互"); return; }
            if (manualPause && !cancelled) {
                if (Stage != PositionTaskStage.Paused || acquired) { resumeReturn = Stage == PositionTaskStage.Returning; session.Suspend(); acquired = false; Stage = PositionTaskStage.Paused; }
                started += delta; returnAt += delta; pauseElapsed += delta;
                if (pauseElapsed > 600) Finish(PositionTaskStage.Failed, "手动暂停超过10分钟，释放前往记录");
                return;
            }
            if (revalidate) {
                revalidate = false;
                if (!session.Revalidate(resumeReturn)) { Finish(PositionTaskStage.Failed, session.Blocked ?? "继续前位置或路线核验失败"); return; }
            }
            if (!acquired)
            {
                if (!session.Acquire()) {
                    if (now - started > timeout) Finish(PositionTaskStage.Failed, "等待移动权限超时；未开始移动");
                    return;
                }
                acquired = true; Stage = resumeReturn ? PositionTaskStage.Returning : PositionTaskStage.Moving;
                if (resumeReturn) returnAt = now;
            }
            if (cancelled && Stage != PositionTaskStage.Returning) { session.Stop(); Stage = PositionTaskStage.Returning; returnAt = now; }
            bool returning = Stage == PositionTaskStage.Returning;
            if (!session.Renew(returning)) { Finish(PositionTaskStage.Failed, "移动控制权已失效，停止自己的移动动作"); return; }
            if (!session.CanExecute)
            {
                session.Stop();
                if (!returning) Stage = PositionTaskStage.Paused;
                if (now - started > timeout + 120) Finish(PositionTaskStage.Failed, "原版保护动作持续占用，前往任务已结束");
                else if (returning && now - returnAt > 8) Finish(cancelled ? PositionTaskStage.Cancelled : PositionTaskStage.Complete, "原活动恢复交给原版仲裁");
                return;
            }
            if (!returning) Stage = PositionTaskStage.Moving;
            if (session.Arrived(returning))
            {
                session.Stop();
                if (returning) { Finish(cancelled ? PositionTaskStage.Cancelled : PositionTaskStage.Complete, "已恢复扫描／跟随"); return; }
                Stage = PositionTaskStage.Returning; returnAt = now; return;
            }
            if (returning && now - returnAt > 8) { Finish(cancelled ? PositionTaskStage.Cancelled : PositionTaskStage.Complete, "已到达标记位置；后续恢复交给原版仲裁"); return; }
            if (!returning) {
                elapsed += delta;
                if (elapsed > timeout) { Finish(PositionTaskStage.Failed, "前往超时，未确认到达标记位置"); return; }
            }
            if (!session.Move(returning) && session.Blocked != null)
                Finish(returning ? cancelled ? PositionTaskStage.Cancelled : PositionTaskStage.Complete : PositionTaskStage.Failed, session.Blocked);
        }
        void Finish(PositionTaskStage stage, string reason) { Stage = stage; try { session.Stop(); } finally { session.End(stage, reason); } }
    }
    internal static class MovementOrderPolicy
    {
        public static bool Arrived(float dx, float dy, float dz, float radius = 1)
            => float.IsFinite(dx) && float.IsFinite(dy) && float.IsFinite(dz)
                && Math.Abs(dy) <= 1 && dx * dx + dz * dz <= radius * radius;
        public static bool Duplicate(long currentTarget, long nextTarget) => currentTarget != 0 && currentTarget == nextTarget;
        public static bool SamePoint(float distance, bool sameNode) => sameNode && float.IsFinite(distance) && distance <= .25f;
    }
}
