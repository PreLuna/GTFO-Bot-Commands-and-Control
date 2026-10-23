using System;
namespace PreLuna.Captain.Control
{
    public readonly record struct BotControlTarget(int Slot, long Agent, long Owner);
    public enum BotControlState { Waiting, Active, Released, Failed }
    public interface IBotControlService
    {
        bool Begin(BotControlTarget target, string source, out long ticket, out string reason);
        BotControlState Poll(long ticket, out string reason);
        bool Renew(long ticket);
        void Release(long ticket, bool recall);
        bool Controls(long agent, long owner);
    }
    // Native wrappers and private task objects never cross this contract.
    // Registration and all calls belong to the Unity main thread.
    public static partial class BotControlApi
    {
        public const int ApiVersion = 1;
        static IBotControlService service;
        static int thread;
        public static bool Available => service != null && Environment.CurrentManagedThreadId == thread;
        public static bool Register(IBotControlService value)
        {
            if (value == null || service != null) return false;
            thread = Environment.CurrentManagedThreadId; service = value; return true;
        }
        public static void Unregister(IBotControlService value)
        { if (Available && ReferenceEquals(service, value)) service = null; }
        public static bool Begin(BotControlTarget target, string source, out long ticket, out string reason)
        {
            ticket = 0; reason = "队长接管服务尚未就绪／调用线程不正确";
            return Available && service.Begin(target, source, out ticket, out reason);
        }
        public static BotControlState Poll(long ticket, out string reason)
        { reason = "队长接管服务不可用"; return Available ? service.Poll(ticket, out reason) : BotControlState.Failed; }
        public static bool Renew(long ticket) => Available && service.Renew(ticket);
        public static void Release(long ticket, bool recall) { if (Available) service.Release(ticket, recall); }
        public static bool Controls(long agent, long owner) => Available && service.Controls(agent, owner);
    }
    public interface IControlTransaction
    {
        bool Valid { get; }
        bool Paused { get; }
        bool Acquire();
        bool Renew();
        void Quiesce();
        void ReleaseLease();
        void RestoreBeforeActivation();
        void KeepPaused();
        void Recall();
    }
    // Shared by production and tests: input ownership is possible only after Active.
    public sealed class ControlTransaction
    {
        readonly IControlTransaction session;
        readonly float deadline;
        float heartbeat, last;
        bool acquired, everActive;
        public BotControlState State { get; private set; } = BotControlState.Waiting;
        public string Reason { get; private set; }
        public bool ActiveAt(float now) => State == BotControlState.Active && float.IsFinite(now) && now >= last && now <= heartbeat;
        public ControlTransaction(IControlTransaction session, float now)
        { this.session = session; last = now; deadline = now + 8; heartbeat = now + 3; }
        public bool Renew(float now)
        {
            if (!float.IsFinite(now) || now < last || now > heartbeat || State is BotControlState.Released or BotControlState.Failed) return false;
            heartbeat = now + 3; return true;
        }
        public void Tick(float now)
        {
            if (State is BotControlState.Released or BotControlState.Failed) return;
            if (!float.IsFinite(now) || now < last || !session.Valid) { End(false, "关卡／房主／角色身份已失效", false); return; }
            last = now;
            if (now > heartbeat) { End(false, "接管心跳失效", everActive); return; }
            try {
                if (State == BotControlState.Active) {
                    if (!session.Renew()) End(false, "机器人控制租约失效", true);
                    return;
                }
                if (now >= deadline) { End(false, "等待任务暂停／更好 AI 控制权限超时", false); return; }
                if (!session.Paused || !session.Acquire()) return;
                acquired = true;
                session.Quiesce();
                if (!session.Valid || !session.Renew()) { End(false, "接管提交前校验失败", false); return; }
                everActive = true; State = BotControlState.Active;
            } catch {
                End(false, "接管事务异常", everActive); throw;
            }
        }
        public void Exit(bool recall) => End(true, "接管已退出", recall && everActive);
        void End(bool success, string reason, bool recall)
        {
            if (State is BotControlState.Released or BotControlState.Failed) return;
            State = success ? BotControlState.Released : BotControlState.Failed; Reason = reason;
            try { if (acquired) session.ReleaseLease(); }
            finally {
                acquired = false;
                try { if (everActive) session.KeepPaused(); else session.RestoreBeforeActivation(); }
                finally { if (recall && session.Valid) session.Recall(); }
            }
        }
    }
}
