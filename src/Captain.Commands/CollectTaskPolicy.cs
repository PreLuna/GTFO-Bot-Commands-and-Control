using System;
using System.Collections.Generic;
namespace PreLuna.Captain.Tasks
{
    internal enum CollectAttempt { Started, Skip, Stop }
    internal interface ICollectTaskSession
    {
        bool Valid { get; }
        bool ChildActive { get; }
        bool ChildConfirmed { get; }
        string ChildStage { get; }
        TaskBoardState ChildState { get; }
        CollectAttempt Begin(int id, out string reason);
        bool BeginReturn(out string reason);
        void CancelChild();
        void PauseChild(bool pause);
        void Notify(TaskBoardState state, string stage, string detail);
    }
    // Counts confirmed world-object pickups, not pack uses or remaining charges.
    internal sealed class CollectTaskRunner
    {
        readonly ICollectTaskSession session;
        readonly List<int> candidates = new();
        readonly int limit;
        float created, last, pauseElapsed;
        bool paused;
        public bool ManualPaused => paused;
        public int PauseRevision { get; private set; }
        public void Pause(bool value) { if (!Finished) PauseRevision++; if (!Finished && paused != value) { paused = value; pauseElapsed = 0; } }
        int index;
        bool child, returning, stopping, returnRequested = true;
        TaskBoardState outcome = TaskBoardState.Complete;
        string ending = "已知候选已处理";
        public int Completed { get; private set; }
        public int Skipped { get; private set; }
        public bool Finished { get; private set; }
        public CollectTaskRunner(ICollectTaskSession adapter, IEnumerable<int> ids, int maximum, float now)
        {
            if (adapter == null || ids == null || maximum < 1 || maximum > 5 || !float.IsFinite(now)) throw new ArgumentException("收集任务参数无效");
            session = adapter; limit = maximum; created = last = now;
            var unique = new HashSet<int>();
            foreach (int id in ids) if (id > 0 && unique.Add(id)) { candidates.Add(id); if (candidates.Count == 80) break; }
            Report(TaskBoardState.Waiting, "收集已登记", "仅处理下单时的已知候选；不自动发现或换掉携带物品");
        }
        void Report(TaskBoardState state, string stage, string reason)
            => session.Notify(state, stage, "成功 " + Completed + "/" + limit + "，跳过 " + Skipped + "；" + reason);
        void End(TaskBoardState state, string reason) { Finished = true; Report(state, state == TaskBoardState.Complete ? "收集结束" : state == TaskBoardState.Cancelled ? "收集已取消" : "收集停止", reason); }
        public void Abort(string reason) { if (!Finished) End(TaskBoardState.Failed, reason); }
        public void Cancel(bool returnToLeader)
        {
            if (Finished) return;
            stopping = true; returnRequested = returnToLeader; outcome = TaskBoardState.Cancelled; ending = "已取消，保留实际携带物品";
            if (!returning || !returnToLeader) session.CancelChild();
        }
        public void Tick(float now)
        {
            if (Finished) return;
            if (!float.IsFinite(now) || now < created || !session.Valid) { session.CancelChild(); End(TaskBoardState.Failed, "关卡、房主或机器人身份已失效"); return; }
            float delta = Math.Max(0, now - last); last = now;
            if (paused && !stopping) { pauseElapsed += delta; if (pauseElapsed > 600) { stopping = true; returnRequested = false; outcome = TaskBoardState.Failed; ending = "手动暂停超过10分钟，释放收集队列"; session.CancelChild(); } }
            session.PauseChild(paused && !stopping);
            if (paused && !stopping) { created += delta; Report(TaskBoardState.Paused, "手动暂停收集", "当前原子交互先核验，不提交下一件；保留候选顺序与成功数量"); return; }
            if (now - created > 660) {
                session.CancelChild();
                if (session.ChildActive) return; // Already submitted interactions get their bounded verification window.
                End(TaskBoardState.Failed, "收集及返程总时限到达，停止剩余动作并保留实际物品"); return;
            }
            if (!stopping && now - created > 600) { stopping = true; outcome = TaskBoardState.Failed; ending = "收集总时限到达"; session.CancelChild(); }
            if (returning) {
                if (stopping && outcome == TaskBoardState.Cancelled && !returnRequested && !session.ChildActive) { End(outcome, ending); return; }
                if (session.ChildActive) { Report(session.ChildState == TaskBoardState.Paused ? TaskBoardState.Paused : TaskBoardState.Returning, session.ChildStage, ending); return; }
                End(session.ChildConfirmed ? outcome : TaskBoardState.Failed, ending + (session.ChildConfirmed ? "；已返回／恢复" : "；返程未确认")); return;
            }
            if (child) {
                if (session.ChildActive) { Report(session.ChildState, session.ChildStage, "本次交互结束前不会提交下一件"); return; }
                child = false;
                if (session.ChildConfirmed) Completed++;
                else if (!stopping) { stopping = true; outcome = TaskBoardState.Failed; ending = "当前拾取未确认，停止剩余队列，请查看子任务失败原因"; }
            }
            if (!stopping && Completed < limit && index < candidates.Count) {
                var attempt = session.Begin(candidates[index++], out var reason);
                if (attempt == CollectAttempt.Started) { child = true; Report(TaskBoardState.Executing, "接近下一件", "等待原版拾取和数量同步"); return; }
                if (attempt == CollectAttempt.Skip) { Skipped++; Report(TaskBoardState.Executing, "跳过候选", reason); return; }
                stopping = true; outcome = TaskBoardState.Failed; ending = reason;
            }
            if (!stopping && Completed < limit && index < candidates.Count) return;
            if (!stopping && Completed >= limit) ending = "已达到对象数量上限";
            if (!returnRequested) { End(outcome, ending); return; }
            if (!session.BeginReturn(out var failure)) { End(TaskBoardState.Failed, ending + "；无法接单返程：" + failure); return; }
            returning = true; Report(TaskBoardState.Returning, "统一返回／恢复", ending);
        }
    }
}
