using System;
using System.Collections.Generic;
namespace PreLuna.Captain.Tasks
{
    internal enum RescueStage { Waiting, Approaching, Reviving, Complete, Failed, Cancelled }
    internal interface IRescueSession
    {
        bool Valid { get; }
        bool Downed { get; }
        bool Recovered { get; }
        bool Interacting { get; }
        bool Running { get; }
        bool Ready(out string reason);
        bool Begin();
        void Finish(RescueStage result, string reason);
    }
    internal sealed class RescueRunner
    {
        readonly IRescueSession session;
        readonly float created, timeout;
        float nextRequest, cancelAt = -1, endedAt = -1;
        int attempts;
        bool started, cancel;
        public RescueStage Stage { get; private set; }
        public string Reason { get; private set; } = "等待救援权限";
        public bool Finished => Stage >= RescueStage.Complete;
        public RescueRunner(IRescueSession session, float now, float timeout = 90)
        { this.session = session; created = now; this.timeout = timeout; }
        public void Cancel() => cancel = true;
        public void Abort(string reason) { if (!Finished) End(RescueStage.Failed, reason); }
        void End(RescueStage stage, string reason) { Stage = stage; Reason = reason; session.Finish(stage, reason); }
        public void Tick(float now)
        {
            if (Finished) return;
            if (!float.IsFinite(now) || now < created) { End(RescueStage.Failed, "救援时钟已失效"); return; }
            if (!session.Valid) { End(RescueStage.Failed, "房主、关卡、机器人或目标身份已改变"); return; }
            if (session.Recovered) { End(RescueStage.Complete, "目标已恢复站立（可能由其他队友救起）"); return; }
            if (!session.Downed) { End(RescueStage.Failed, "目标不再处于可救援倒地状态"); return; }
            if (cancel) {
                if (cancelAt < 0) cancelAt = now;
                if (!session.Interacting || now - cancelAt >= 12) { End(RescueStage.Cancelled, "取消救援，保留实际救援结果"); return; }
                Stage = RescueStage.Reviving; Reason = "取消已登记，已开始的救援交互先收尾"; return;
            }
            if (now - created >= timeout) { End(RescueStage.Failed, "救援超时，已释放目标预约"); return; }
            if (started) {
                if (!session.Running) {
                    if (endedAt < 0) endedAt = now;
                    Stage = RescueStage.Reviving; Reason = "原版动作结束，等待倒地状态同步";
                    if (now - endedAt >= 2) End(RescueStage.Failed, "原版救援动作已结束，但队友尚未被救起；请检查通路／危险区域");
                    return;
                }
                Stage = session.Interacting ? RescueStage.Reviving : RescueStage.Approaching;
                Reason = session.Interacting ? "原版救援交互中" : "原版接近倒地队友"; return;
            }
            Stage = RescueStage.Waiting;
            if (now < nextRequest) return;
            nextRequest = now + 1;
            if (!session.Ready(out var reason)) { Reason = reason; return; }
            if (attempts >= 3) { End(RescueStage.Failed, "三次救援请求未被原版接受，停止重试"); return; }
            attempts++;
            if (session.Begin()) { started = true; Stage = RescueStage.Approaching; Reason = "已提交原版救援"; }
            else Reason = "原版暂未接受救援请求（" + attempts + "/3）";
        }
    }
    internal sealed record RescueEdge(int Bot, int Target, float Cost);
    internal static class RescueAllocation
    {
        // At most three bots / three downed teammates. Maximise coverage before travel cost.
        internal static List<RescueEdge> Match(IReadOnlyList<RescueEdge> edges)
        {
            var ordered = new List<RescueEdge>();
            foreach (var edge in edges) if (edge.Bot != edge.Target && float.IsFinite(edge.Cost) && edge.Cost >= 0) ordered.Add(edge);
            ordered.Sort((a,b) => a.Bot == b.Bot ? a.Target.CompareTo(b.Target) : a.Bot.CompareTo(b.Bot));
            var best = new List<RescueEdge>(); var current = new List<RescueEdge>();
            var bots = new HashSet<int>(); var targets = new HashSet<int>(); float bestCost = float.PositiveInfinity;
            void Visit(int from, float cost) {
                if (current.Count > best.Count || current.Count == best.Count && cost < bestCost) { best = new(current); bestCost = cost; }
                if (current.Count == 3) return;
                for (int i = from; i < ordered.Count; i++) {
                    var edge = ordered[i]; if (bots.Contains(edge.Bot) || targets.Contains(edge.Target)) continue;
                    bots.Add(edge.Bot); targets.Add(edge.Target); current.Add(edge);
                    Visit(i + 1, cost + edge.Cost);
                    current.RemoveAt(current.Count - 1); bots.Remove(edge.Bot); targets.Remove(edge.Target);
                }
            }
            Visit(0, 0); return best;
        }
    }
}
