using System;
namespace PreLuna.Captain.Tasks
{
    internal enum ResourceUseMode { CommandsOnly, EmergencyOnly, CombatAutomatic, Original }
    internal enum SupplyKind { Unknown, Health, WeaponAmmo, ToolAmmo, Disinfection }
    internal readonly record struct SupplyNeed(bool Needed, float Health, float Standard, float Special, float Tool, float Infection);
    internal static class ResourceUsePolicy
    {
        public static bool Valid(ResourceUseMode mode) => (int)mode >= 0 && (int)mode <= 3;
        public static float Fraction(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
        // Weapon fractions include loaded ammunition. An empty magazine alone is never an emergency.
        public static bool Emergency(SupplyKind kind, SupplyNeed need, float health, float ammo, float tool, float infection)
            => need.Needed && kind switch {
                SupplyKind.Health => Fraction(need.Health) <= health,
                SupplyKind.WeaponAmmo => Fraction(need.Standard) <= ammo && Fraction(need.Special) <= ammo,
                SupplyKind.ToolAmmo => Fraction(need.Tool) <= tool,
                SupplyKind.Disinfection => float.IsFinite(need.Infection) && Fraction(need.Infection) >= infection,
                _ => false
            };
        public static bool Allowed(ResourceUseMode mode, SupplyKind kind, SupplyNeed need, bool command, bool danger,
            float health, float ammo, float tool, float infection)
        {
            if (!Valid(mode) || kind == SupplyKind.Unknown || !need.Needed) return false;
            if (command || mode == ResourceUseMode.Original) return true;
            return mode != ResourceUseMode.CommandsOnly && (Emergency(kind, need, health, ammo, tool, infection)
                || mode == ResourceUseMode.CombatAutomatic && danger);
        }
    }
}
