using System;
namespace PreLuna.Captain.Tasks
{
    public enum ItemTaskKind { Pickup, PutBack, Return }
    public enum ItemTaskStage { Waiting, Moving, Paused, Interacting, Verifying, Returning, Complete, Failed, Cancelled }
    public readonly record struct PickupCapacity(int Slot, uint ItemId, float Amount, uint HeldId, float HeldAmount, bool StackEnabled, float StackLimit);
    public static class ItemTaskPolicy
    {
        public static bool Quantity(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
        public static bool SupportedSlot(int slot) => slot == 4 || slot == 5;
        public static string CanPickup(PickupCapacity capacity)
        {
            if (!SupportedSlot(capacity.Slot)) return "该物品不属于资源包或普通消耗品；任务搬运使用原版搬运指令";
            if (capacity.ItemId == 0 || !Quantity(capacity.Amount) || capacity.Amount <= 0 || !Quantity(capacity.HeldAmount)) return "物品数量或身份无效";
            if (capacity.HeldId == 0) return null;
            if (capacity.HeldId != capacity.ItemId) return "对应槽位已有其他物品；请先放回，不会自动替换";
            if (!capacity.StackEnabled) return "同槽已有同类物品，但可用堆叠入口未开启";
            if (!Quantity(capacity.StackLimit) || capacity.StackLimit <= capacity.HeldAmount) return "同类物品已达到当前实际堆叠容量";
            return null;
        }
        public static bool Conserved(float before, float after) => Quantity(before) && Quantity(after) && Math.Abs(before - after) <= Math.Max(.01f, before * .0001f);
    }
    public interface IItemTaskSession
    {
        bool Valid { get; }
        bool Acquire();
        bool Renew(ItemTaskStage stage);
        bool CanExecute { get; }
        bool AtDestination(bool returning);
        bool Move(bool returning);
        void StopOwnMovement();
        void Suspend();
        string Validate();
        float InteractionDuration { get; }
        bool Apply(out string error);
        bool Verify();
        void Release(ItemTaskStage outcome);
        void Notify(ItemTaskStage stage, string detail);
    }
    // Shared production scheduler. Native calls and world verification belong to the session adapter.
    public sealed class ItemTaskRunner
    {
        public readonly ItemTaskKind Kind;
        public ItemTaskStage Stage { get; private set; } = ItemTaskStage.Waiting;
        public bool Finished => Stage == ItemTaskStage.Complete || Stage == ItemTaskStage.Failed || Stage == ItemTaskStage.Cancelled;
        public bool OwnsControl => acquired && !Finished && Stage != ItemTaskStage.Paused;
        private readonly IItemTaskSession session;
        private float created;
        private float lastClock, started, verifyDeadline, returnStarted;
        private bool acquired, cancel, returnRequested, operationDone;
        bool manualPause, manuallySuspended;
        float pauseElapsed;
        public bool ManualPaused => manualPause;
        public bool ManuallySuspended => manuallySuspended;
        public int PauseRevision { get; private set; }
        public void Pause(bool value) { if (!Finished) PauseRevision++; if (!Finished && manualPause != value) { manualPause = value; pauseElapsed = 0; } }
        readonly bool returnAfterOperation;
        public ItemTaskRunner(ItemTaskKind kind, IItemTaskSession adapter, float now, bool returnAfterOperation = true)
        { Kind = kind; session = adapter; created = lastClock = now; this.returnAfterOperation = returnAfterOperation; session.Notify(Stage, "等待机器人空闲／扫描结束"); }
        public void Cancel(bool returnToLeader) { cancel = true; returnRequested |= returnToLeader; }
        private void Set(ItemTaskStage stage, string message, float now)
        { Stage = stage; started = now; session.Notify(stage, message); }
        private void End(ItemTaskStage outcome, string message)
        {
            bool owned = acquired; acquired = false; Stage = outcome;
            try { session.StopOwnMovement(); }
            finally { try { if (owned) session.Release(outcome); } finally { session.Notify(outcome, message); } }
        }
        public void Abort(string reason) { if (!Finished) End(ItemTaskStage.Failed, reason); }
        public void Tick(float now)
        {
            if (Finished) return;
            if (!ItemTaskPolicy.Quantity(now) || now < lastClock || !session.Valid) { End(ItemTaskStage.Failed, "机器人、关卡或房主状态已改变"); return; }
            float delta = now - lastClock; lastClock = now;
            bool wasAtomic = Stage == ItemTaskStage.Interacting || Stage == ItemTaskStage.Verifying;
            if (manualPause && !cancel && !wasAtomic) {
                if (!manuallySuspended) { session.Suspend(); acquired = false; manuallySuspended = true; Stage = ItemTaskStage.Paused; session.Notify(Stage, "手动暂停：保存物品／容器目标并交还移动权限"); }
                created += delta; started += delta; returnStarted += delta; pauseElapsed += delta;
                if (pauseElapsed > 600) End(ItemTaskStage.Failed, "手动暂停超过10分钟，释放物品预约并保留实际背包");
                return;
            }
            if (manuallySuspended) { manuallySuspended = false; Stage = ItemTaskStage.Waiting; }
            if (!acquired)
            {
                if (cancel && !returnRequested) { End(ItemTaskStage.Cancelled, "任务已取消，保留现有物品"); return; }
                if (now - created > 120) { End(ItemTaskStage.Failed, "等待控制权限超时"); return; }
                if (!session.Acquire()) return;
                acquired = true;
                Set(Kind == ItemTaskKind.Return || returnRequested || operationDone ? ItemTaskStage.Returning : ItemTaskStage.Moving, "重新核验后开始执行", now);
                returnStarted = now;
            }
            if (!session.Renew(Stage)) { End(ItemTaskStage.Failed, "任务控制令牌失效"); return; }
            bool atomic = Stage == ItemTaskStage.Interacting || Stage == ItemTaskStage.Verifying;
            if (cancel && !atomic)
            {
                if (!returnRequested) { End(ItemTaskStage.Cancelled, "任务已取消，保留现有物品"); return; }
                if (Stage != ItemTaskStage.Returning && Stage != ItemTaskStage.Paused) { session.StopOwnMovement(); Set(ItemTaskStage.Returning, "取消后返回，保留已拿物品", now); returnStarted = now; }
            }
            if (!atomic && !session.CanExecute)
            {
                session.StopOwnMovement();
                if (Stage != ItemTaskStage.Paused) Set(ItemTaskStage.Paused, "让出扫描或原版受保护动作", now);
                session.Renew(ItemTaskStage.Paused); return;
            }
            if (Stage == ItemTaskStage.Paused)
            { Set(Kind == ItemTaskKind.Return || returnRequested || cancel || operationDone ? ItemTaskStage.Returning : ItemTaskStage.Moving, "恢复任务", now); returnStarted = now; }
            if (Stage == ItemTaskStage.Returning)
            {
                if (session.AtDestination(true)) { End(cancel ? ItemTaskStage.Cancelled : ItemTaskStage.Complete, "已返回，保留携带物品"); return; }
                if (now - returnStarted > 30) { End(ItemTaskStage.Failed, "物品操作已结束，但返程超时"); return; }
                session.Renew(ItemTaskStage.Returning); session.Move(true); return;
            }
            if (Stage == ItemTaskStage.Moving)
            {
                if (now - created > 120) { End(ItemTaskStage.Failed, "接近物品或保持交互站位超时"); return; }
                string reason = session.Validate(); if (reason != null) { End(ItemTaskStage.Failed, reason); return; }
                if (!session.AtDestination(false))
                { if (now - created > 120) End(ItemTaskStage.Failed, "接近物品超时"); else session.Move(false); return; }
                session.StopOwnMovement(); session.Renew(ItemTaskStage.Interacting);
                Set(ItemTaskStage.Interacting, "正在交互", now); return;
            }
            if (Stage == ItemTaskStage.Interacting && !session.AtDestination(false))
            {
                if (!session.CanExecute) { End(ItemTaskStage.Failed, "交互被紧急动作打断"); return; }
                session.StopOwnMovement(); session.Renew(ItemTaskStage.Moving);
                Set(ItemTaskStage.Moving, "站位或视线变化，重新接近原物品", now); return;
            }
            if (Stage == ItemTaskStage.Interacting && now - started >= Math.Clamp(session.InteractionDuration, .1f, 5f))
            {
                if (!session.Apply(out var reason)) { End(ItemTaskStage.Failed, reason ?? "无法提交物品交互"); return; }
                session.Renew(ItemTaskStage.Verifying); Set(ItemTaskStage.Verifying, "等待原版物品与数量同步确认", now); verifyDeadline = now + 5; return;
            }
            if (Stage == ItemTaskStage.Verifying)
            {
                if (session.Verify())
                {
                    operationDone = true;
                    if (cancel && !returnRequested) { End(ItemTaskStage.Cancelled, "交互已完成，停止后续任务，保留物品"); return; }
                    if (!returnAfterOperation && !returnRequested) { End(ItemTaskStage.Complete, "拾取已核验，交回收集队列"); return; }
                    Set(ItemTaskStage.Returning, "操作已确认，返回队伍", now); returnStarted = now; return;
                }
                if (now > verifyDeadline) End(ItemTaskStage.Failed, "物品或数量未确认；停止重试，保留实际状态");
            }
        }
    }
}
