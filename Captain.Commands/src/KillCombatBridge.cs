using System;
using System.Reflection;
using Enemies;
using Gear;
using HarmonyLib;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    // Cached, version-checked access to the actual equipped-gun evaluator; no independent ammo writes.
    internal static class KillCombatBridge
    {
        internal sealed record Choice(BulletWeaponSynced Weapon, KillGunEstimate Estimate);
        static MethodInfo state, monitor, guns, facts, plan;
        static FieldInfo main, special, gunWeapon, health, usable, canFinish, readyKill, readyShots, time;
        internal static bool Available { get; private set; }
        internal static void Install(Harmony patches)
        {
            try {
                var t = KnownTypes.Get("PreLuna.CoopBotsCustom", "PreLuna.CoopBotsCustom.Combat.CombatAdapter");
                state = t == null ? null : AccessTools.Method(t, "State", new[] { typeof(PlayerAIBot) });
                var s = state?.ReturnType;
                main = s == null ? null : AccessTools.Field(s, "Main"); special = s == null ? null : AccessTools.Field(s, "Special");
                var g = main?.FieldType;
                gunWeapon = g == null ? null : AccessTools.Field(g, "Weapon");
                monitor = s == null ? null : AccessTools.Method(t, "Monitor", new[] { s });
                guns = s == null ? null : AccessTools.Method(t, "ReadGuns", new[] { s, typeof(bool) });
                facts = s == null ? null : AccessTools.Method(t, "Facts", new[] { s, typeof(EnemyAgent) });
                health = facts == null ? null : AccessTools.Field(facts.ReturnType, "Health");
                plan = facts == null || g == null ? null : AccessTools.Method(t, "Plan", new[] { s, g, facts.ReturnType, typeof(EnemyAgent), typeof(bool) });
                var p = plan?.ReturnType;
                usable = p == null ? null : AccessTools.Field(p, "Usable"); canFinish = p == null ? null : AccessTools.Field(p, "CanFinish");
                readyKill = p == null ? null : AccessTools.Field(p, "ReadyKill"); readyShots = p == null ? null : AccessTools.Field(p, "ReadyShots"); time = p == null ? null : AccessTools.Field(p, "Time");
                if (monitor == null || guns == null || main == null || special?.FieldType != g || gunWeapon?.FieldType != typeof(BulletWeaponSynced)
                    || health?.FieldType != typeof(float) || usable?.FieldType != typeof(bool) || canFinish?.FieldType != typeof(bool)
                    || readyKill?.FieldType != typeof(bool) || readyShots?.FieldType != typeof(int) || time?.FieldType != typeof(float))
                    throw new MissingMethodException("当前两把枪评分接口不匹配");
                patches.Patch(AccessTools.Method(typeof(PlayerBotActionAttack), "ChooseAttackOption"),
                    postfix: new HarmonyMethod(typeof(KillCombatBridge), nameof(Option)) { priority = Priority.Last });
                Available = true;
            } catch (Exception e) { Available = false; Plugin.Logger.LogWarning("[击杀接战接口] " + e.GetBaseException().Message); }
        }
        internal static Choice Evaluate(PlayerAIBot bot, EnemyAgent enemy, BulletWeaponSynced chosen, float held)
        {
            if (!Available) return null;
            var s = state.Invoke(null, new object[] { bot }); if (s == null) return null;
            monitor.Invoke(null, new[] { s }); guns.Invoke(null, new object[] { s, false });
            var f = facts.Invoke(null, new object[] { s, enemy });
            // Facts may be cached; continuation always uses observed remaining HP.
            health.SetValue(f, enemy.Damage.Health);
            Choice Read(FieldInfo slot) {
                var g = slot.GetValue(s); var w = gunWeapon.GetValue(g) as BulletWeaponSynced; if (w == null) return null;
                var wielded = bot.Inventory?.WieldedItem;
                var p = plan.Invoke(null, new object[] { s, g, f, enemy, wielded?.Pointer == w.Pointer });
                return new(w, new((bool)usable.GetValue(p), (bool)canFinish.GetValue(p), (bool)readyKill.GetValue(p),
                    (int)readyShots.GetValue(p), (float)time.GetValue(p)));
            }
            var a = Read(main); var b = Read(special);
            if (a == null) return b; if (b == null) return a;
            var currentID = chosen?.Pointer ?? bot.Inventory?.WieldedItem?.Pointer;
            var current = b.Weapon.Pointer == currentID ? b : a; var other = ReferenceEquals(current, a) ? b : a;
            return KillContinuationPolicy.Switch(current.Estimate, other.Estimate, current.Weapon.IsReloading,
                current.Weapon.GetCurrentClip() <= 0, chosen == null ? 1000 : held) ? other : current;
        }
        static void Option(PlayerBotActionAttack __instance, bool __result)
        {
            // Preserve native target, invulnerability and successful-option gates.
            if (!__result || __instance?.m_currentAttackOption == null || __instance.IsTargetInvulnerable()
                || !SingleKillAdapter.TryGun(__instance, out var target, out var weapon)) return;
            __instance.m_currentAttackOption.TargetAgent = target;
            __instance.m_currentAttackOption.Means = PlayerBotActionAttack.AttackMeansEnum.Bullet;
            __instance.m_currentAttackOption.ItemToUse = weapon;
            if (__instance.m_fireAction?.IsTerminated() == false) __instance.m_fireAction.Weapon = weapon;
        }
    }
}
