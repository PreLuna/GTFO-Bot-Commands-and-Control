using System;
namespace PreLuna.Captain.Tasks
{
    internal static class LeaseClockPolicy
    {
        // (time + 15f) - 15f can round above time and invalidate a fresh lease.
        internal const float LowerWindow = 15.01f;
        internal static bool Valid(float now, float until) => float.IsFinite(now) && float.IsFinite(until)
            && now <= until && now >= until - LowerWindow;
    }
}
