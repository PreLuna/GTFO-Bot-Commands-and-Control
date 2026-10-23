using System;
namespace PreLuna.Captain.Tasks
{
    internal static class ItemArrivalPolicy
    {
        public static bool Interactable(float dx, float dy, float dz, bool returning, float reach = 2.2f)
        {
            if (!float.IsFinite(dx) || !float.IsFinite(dy) || !float.IsFinite(dz)) return false;
            if (returning) return dx * dx + dy * dy + dz * dz <= 4;
            // Item models can sit on a shelf above their reachable standing point.
            reach = float.IsFinite(reach) ? Math.Clamp(reach, 1.5f, 3f) : 2.2f;
            return dx * dx + dz * dz <= reach * reach && Math.Abs(dy) <= 2;
        }
    }
}
