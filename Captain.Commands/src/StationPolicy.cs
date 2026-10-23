using System;
namespace PreLuna.Captain.Tasks
{
    internal enum StationStage { Waiting, Moving, Holding, Yielded, Paused, Cancelled, Failed }
    internal interface IStationSession
    {
        bool Valid { get; }
        bool Available { get; }
        bool HasLease { get; }
        bool Acquire();
        bool Renew();
        bool Revalidate();
        bool Arrived { get; }
        bool Move();
        string Error { get; }
        void Hold();
        void Suspend();
        void End(bool failed, string reason);
    }
    // A persistent duty owns only movement, with explicit yield/reacquire boundaries.
    internal sealed class StationRunner
    {
        readonly IStationSession session;
        readonly float timeout;
        float last, elapsed, readyAt, pauseElapsed;
        bool paused, cancelled, needValidation = true;
        public StationStage Stage { get; private set; } = StationStage.Waiting;
        public bool Finished => Stage is StationStage.Cancelled or StationStage.Failed;
        public bool ManualPaused => paused;
        public StationRunner(IStationSession session, float now, float timeout) { this.session = session; last = now; this.timeout = timeout; }
        public int PauseRevision { get; private set; }
        public void Pause(bool value) { if (Finished) return; PauseRevision++; if (paused == value) return; paused = value; pauseElapsed = 0; needValidation = true; }
        public void Cancel() => cancelled = true;
        public void Abort(string reason) { if (!Finished) End(true, reason); }
        public void Yield(float now) { if (Finished) return; session.Suspend(); needValidation = true; readyAt = now + 2.5f; elapsed = 0; Stage = paused ? StationStage.Paused : StationStage.Yielded; }
        void End(bool failed, string reason) { Stage = failed ? StationStage.Failed : StationStage.Cancelled; try { session.Suspend(); } finally { session.End(failed, reason); } }
        public void Tick(float now)
        {
            if (Finished) return;
            if (!float.IsFinite(now) || now < last || !session.Valid) { End(true, "驻守关卡／机器人身份已失效"); return; }
            float delta = now - last; last = now;
            if (cancelled) { End(false, "驻守已取消，交还原版活动"); return; }
            if (paused) {
                if (Stage != StationStage.Paused) Yield(now);
                Stage = StationStage.Paused; pauseElapsed += delta;
                if (pauseElapsed > 600) End(true, "手动暂停超过10分钟，释放驻守记录");
                return;
            }
            if (!session.Available) { if (Stage != StationStage.Yielded || session.HasLease) Yield(now); readyAt = now + 2.5f; return; }
            if (now < readyAt) return;
            if (!session.HasLease) {
                if (needValidation) { if (!session.Revalidate()) { End(true, session.Error ?? "驻守路线已经失效"); return; } needValidation = false; }
                if (!session.Acquire()) { needValidation = true; readyAt = now + .5f; Stage = StationStage.Waiting; return; }
            }
            if (!session.Renew()) { Yield(now); return; }
            if (session.Arrived) { if (Stage != StationStage.Holding) { session.Hold(); elapsed = 0; } Stage = StationStage.Holding; return; }
            if (Stage == StationStage.Holding) { needValidation = true; Yield(now); return; }
            Stage = StationStage.Moving; elapsed += delta;
            if (elapsed > timeout) { End(true, "驻守接近超时，未确认到达"); return; }
            if (!session.Move() && session.Error != null) End(true, session.Error);
        }
    }
}
