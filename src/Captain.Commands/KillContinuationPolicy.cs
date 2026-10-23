using System;
namespace PreLuna.Captain.Tasks
{
    internal readonly record struct KillGunEstimate(bool Usable, bool CanFinish, bool ReadyKill, int ReadyShots, float Time);
    internal static class KillContinuationPolicy
    {
        // A short equip animation alone cannot justify an inadequate alternative magazine.
        internal static bool Switch(KillGunEstimate current, KillGunEstimate other, bool reloading, bool empty, float held)
        {
            if (!other.Usable || !other.CanFinish || !float.IsFinite(other.Time)) return false;
            if (!current.Usable || !current.CanFinish) return true;
            if (reloading || empty) return other.ReadyKill && other.Time + .2f < current.Time;
            if (held < 3) return false;
            return other.ReadyKill && other.Time < current.Time * .7f;
        }
        internal static bool Melee(float health, float damage, float time, KillGunEstimate gun, bool reachable)
        {
            if (!reachable || !float.IsFinite(health) || health <= 0 || !float.IsFinite(damage) || damage <= 0) return false;
            if (health <= damage && time <= 3) return true;
            if (!gun.Usable || !gun.CanFinish || !float.IsFinite(gun.Time)) return true;
            return Math.Ceiling(health / damage) * time + .2f < gun.Time;
        }
    }
}
