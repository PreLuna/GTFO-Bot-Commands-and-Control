using System;
namespace PreLuna.Captain.Tasks
{
    // A saved continuation belongs to one task instance and one pause revision.
    internal sealed class BorrowHold
    {
        readonly Func<bool> yielded;
        readonly Func<bool> retained;
        Action restore;
        internal BorrowHold(Func<bool> yielded, Action restore, Func<bool> retained = null) { this.yielded = yielded; this.restore = restore; this.retained = retained ?? (() => true); }
        internal bool Yielded => yielded();
        internal bool Retained => retained();
        internal void Restore() { var action = restore; restore = null; action?.Invoke(); }
        internal void Discard() => restore = null;
    }
    internal interface IBorrowReturnSession
    {
        bool Valid { get; }
        bool Retained { get; }
        bool Safe { get; }
        bool Arrived { get; }
        bool Moving { get; }
        bool Acquire();
        bool Renew();
        void Release();
        bool Move();
        void Finish(bool restore, string reason);
    }
    // Waiting for safety never owns movement or prevents native combat / rescue.
    internal sealed class BorrowReturnRunner
    {
        readonly IBorrowReturnSession session;
        readonly float deadline;
        float quietAt = -1, nextMove;
        int attempts;
        bool owned;
        internal bool Finished { get; private set; }
        internal BorrowReturnRunner(IBorrowReturnSession session, float now) { this.session = session; deadline = now + 120; }
        void End(bool restore, string reason)
        { if (Finished) return; Finished = true; try { Release(); } finally { session.Finish(restore, reason); } }
        void Release() { if (!owned) return; owned = false; session.Release(); }
        internal void Abort(bool restore, string reason) => End(restore, reason);
        internal void Tick(float now)
        {
            if (Finished) return;
            if (!float.IsFinite(now) || !session.Valid) { End(false, "关卡／机器人身份改变，丢弃旧任务恢复记录"); return; }
            if (!session.Retained) { End(true, "玩家更改了原任务，取消旧返程并保留新指令"); return; }
            if (now >= deadline) { End(true, "安全返程等待超时，交还原任务自行核验；未强制传送"); return; }
            if (!session.Safe) { quietAt = -1; Release(); return; }
            if (quietAt < 0) quietAt = now;
            if (now - quietAt < 1) return;
            if (!owned) { if (!session.Acquire()) return; owned = true; }
            if (!session.Renew()) { End(true, "返程控制权失效，停止传送并交还原任务"); return; }
            if (session.Arrived) { End(true, "已返回借调前的位置，继续原任务"); return; }
            if (session.Moving) return;
            if (now < nextMove) return;
            if (attempts >= 3) { End(true, "返程位置或路线未确认，停止重试并交还原任务"); return; }
            nextMove = now + 2; attempts++;
            if (!session.Move()) End(true, "返程路线／落点失效，未穿门传送；交还原任务重新核验");
        }
    }
}
