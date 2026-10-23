using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace PreLuna.Captain.Tasks
{
    internal enum TaskBoardState { Executing, Waiting, Paused, Returning, Queued, Complete, Failed, Cancelled, Native }
    internal sealed record TaskBoardRow(int Id, int Slot, long Owner, string Bot, string Task, string Target,
        TaskBoardState State, string Stage, string Reason, float Started, float Updated)
    {
        public bool Terminal => State is TaskBoardState.Complete or TaskBoardState.Failed or TaskBoardState.Cancelled;
        public string Label => (Id > 0 ? "#" + Id + " " : "") + Bot + "·" + Task + "·" + Stage;
        public string Detail(float now) => Label + "；目标：" + Target + "；说明：" + Reason
            + (State == TaskBoardState.Native ? "；原版动作起始时间未记录" : "；" + (Terminal ? "持续" : "已持续")
                + " " + Math.Max(0, (Terminal ? Updated : now) - Started).ToString("0.0") + " 秒");
    }
    internal sealed class TaskBoardSnapshot
    {
        public readonly List<TaskBoardRow> Rows = new();
        public float At;
    }
    // No game calls, native references, task control or logging. Tickets have weak keys.
    internal sealed class TaskBoardJournal
    {
        sealed class Ticket { public int Id; public bool Ended; }
        ConditionalWeakTable<object, Ticket> tickets = new();
        readonly Dictionary<int, TaskBoardRow> rows = new();
        readonly Queue<int> history = new();
        int next;
        public int Count => rows.Count;
        public void Record(object identity, int slot, long owner, string bot, string task, string target,
            TaskBoardState state, string stage, string reason, float now)
        {
            if (identity == null || owner == 0 || !float.IsFinite(now)) return;
            var ticket = tickets.GetValue(identity, _ => new Ticket());
            if (ticket.Ended) return;
            if (ticket.Id == 0) {
                if (rows.Count - history.Count >= 32) return;
                ticket.Id = ++next;
            }
            rows.TryGetValue(ticket.Id, out var prior);
            if (prior != null && (prior.Owner != owner || prior.Slot != slot)) return;
            if (prior != null && prior.State == state && prior.Stage == stage && prior.Reason == (reason ?? "无额外说明")) return;
            var row = new TaskBoardRow(ticket.Id, slot, owner, bot, task, target, state, stage, reason ?? "无额外说明", prior?.Started ?? now, now);
            rows[row.Id] = row;
            if (row.Terminal) {
                ticket.Ended = true; history.Enqueue(row.Id);
                while (history.Count > 32) rows.Remove(history.Dequeue());
            }
        }
        public TaskBoardSnapshot Capture(float now)
        {
            var result = new TaskBoardSnapshot { At = float.IsFinite(now) ? now : 0 };
            result.Rows.AddRange(rows.Values);
            result.Rows.Sort((a, b) => a.State == b.State ? (a.Terminal
                ? b.Updated == a.Updated ? b.Id.CompareTo(a.Id) : b.Updated.CompareTo(a.Updated)
                : a.Id.CompareTo(b.Id)) : a.State.CompareTo(b.State));
            return result;
        }
        public void Reset() { rows.Clear(); history.Clear(); tickets = new(); }
    }
    internal static class TaskBoard
    {
        public static readonly TaskBoardJournal Journal = new();
        public static readonly (TaskBoardState State, string Label)[] Groups = {
            (TaskBoardState.Executing,"正在执行"), (TaskBoardState.Waiting,"等待权限"), (TaskBoardState.Queued,"等待命令"),
            (TaskBoardState.Paused,"任务暂停"), (TaskBoardState.Returning,"返回中"),
            (TaskBoardState.Complete,"最近完成"), (TaskBoardState.Failed,"最近失败"), (TaskBoardState.Cancelled,"最近取消"),
            (TaskBoardState.Native,"原版部署／回收当前动作") };
    }
}
