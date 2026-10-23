using System;
using System.Collections.Generic;
namespace PreLuna.Captain.Tasks
{
    // Bounded FIFO; deadlines start on display, never while waiting in the queue.
    internal sealed class PickupPromptQueue<T> where T : class
    {
        readonly Queue<T> entries = new();
        readonly HashSet<T> unique = new();
        public const int Capacity = 32;
        public int Count => entries.Count;
        public bool Enqueue(T entry)
        {
            if (entry == null || entries.Count >= Capacity || !unique.Add(entry)) return false;
            entries.Enqueue(entry); return true;
        }
        public T Dequeue()
        {
            if (entries.Count == 0) return null;
            var entry = entries.Dequeue(); unique.Remove(entry); return entry;
        }
        public void Clear() { entries.Clear(); unique.Clear(); }
    }
    internal sealed class PickupPromptClock
    {
        public float Duration { get; private set; }
        public float Remaining { get; private set; }
        public bool Expired => Remaining <= 0;
        public void Begin(float duration) { Duration = Remaining = Math.Clamp(duration, 1, 60); }
        public void Advance(float elapsed)
        { if (elapsed > 0 && !float.IsNaN(elapsed) && !float.IsInfinity(elapsed)) Remaining = Math.Max(0, Remaining - elapsed); }
    }
}
