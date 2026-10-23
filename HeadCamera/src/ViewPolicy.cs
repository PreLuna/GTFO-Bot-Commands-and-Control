using System;
using System.Collections.Generic;

namespace PreLuna.HeadCamera
{
    // These rules are independent of Unity and Captain; identity is not just the player slot.
    public readonly struct CameraTarget
    {
        public readonly int Slot;
        public readonly long AgentIdentity, OwnerIdentity;
        public readonly string Name;
        public readonly bool IsBot;
        public CameraTarget(int slot, long agent, long owner, string name, bool bot)
        { Slot = slot; AgentIdentity = agent; OwnerIdentity = owner; Name = name; IsBot = bot; }
        internal bool Same(CameraTarget other) => Slot == other.Slot && AgentIdentity == other.AgentIdentity && OwnerIdentity == other.OwnerIdentity;
    }
    internal static class ViewPolicy
    {
        internal static int Select(IReadOnlyList<CameraTarget> candidates, CameraTarget? current, int step)
        {
            if (candidates.Count == 0) return -1;
            int index = -1;
            if (current.HasValue)
                for (int i = 0; i < candidates.Count; i++) if (candidates[i].Same(current.Value)) { index = i; break; }
            if (index < 0) return step < 0 ? candidates.Count - 1 : 0;
            if (step == 0) return index;
            return (index + (step < 0 ? -1 : 1) + candidates.Count) % candidates.Count;
        }
    }
    internal sealed class RenderClock
    {
        double next;
        int rate;
        public bool Due(double now, int fps)
        {
            fps = Math.Clamp(fps, 5, 60);
            double interval = 1d / fps;
            if (rate != fps) { rate = fps; next = now; }
            // Preserve the cadence instead of adding a full interval to a late frame.
            // Unity's float clock can round the second 60-Hz frame just below a 30-Hz deadline.
            if (now + Math.Min(.001, interval * .02) < next) return false;
            next = now - next >= interval ? now + interval : next + interval;
            return true;
        }
        public void Reset() { next = 0; rate = 0; }
    }
    internal static class DefaultUpgrade
    {
        internal const int CurrentVersion = 1;
        internal static bool Replace(int version, int current, int oldDefault) => version < CurrentVersion && current == oldDefault;
    }
    internal sealed class ViewSession
    {
        public bool Opened { get; private set; }
        public void SetVisible(bool value) => Opened = value;
        // Other menus temporarily hide the picture; a terminal permanently closes this session.
        public bool ObserveTerminal(bool terminal)
        {
            if (!terminal || !Opened) return false;
            Opened = false;
            return true;
        }
    }
    internal sealed class FrameRateMeter
    {
        double started;
        int count;
        bool active;
        public double Actual { get; private set; }
        public bool Sample(double now)
        {
            if (!active) { active = true; started = now; count = 0; return false; }
            count++;
            double elapsed = now - started;
            if (elapsed < 1) return false;
            Actual = count / elapsed;
            started = now; count = 0;
            return true;
        }
        public void Reset() { active = false; count = 0; Actual = 0; }
    }
    internal sealed class FailureCircuit
    {
        int consecutive;
        public double RetryAt { get; private set; }
        public bool Tripped { get; private set; }
        public bool Ready(double now) => !Tripped && now >= RetryAt;
        public void Success() { consecutive = 0; RetryAt = 0; }
        public void Fail(double now) { consecutive++; RetryAt = now + 5; Tripped = consecutive >= 3; }
        public void Reset() { consecutive = 0; RetryAt = 0; Tripped = false; }
        public void Trip() { Tripped = true; RetryAt = double.PositiveInfinity; }
    }
}
