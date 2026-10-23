using System;
using Agents;
using Enemies;
using Gear;
using HarmonyLib;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal sealed class KillStrikeScope
    {
        internal readonly PlayerAgent Source;
        internal readonly EnemyAgent Enemy;
        internal readonly MeleeWeaponThirdPerson Weapon;
        internal bool Consumed, Dispatching;
        internal bool RequireBehind = true;
        internal KillStrikeScope(PlayerAgent source, EnemyAgent enemy, MeleeWeaponThirdPerson weapon)
        { Source = source; Enemy = enemy; Weapon = weapon; }
    }
    internal static class KillMeleeAdapter
    {
        [ThreadStatic] static KillStrikeScope hitScope;
        internal static void Install(Harmony patches)
        {
            patches.Patch(AccessTools.Method(typeof(MeleeWeaponThirdPerson), "DoAttackDamage"),
                prefix: new HarmonyMethod(typeof(KillMeleeAdapter), nameof(BeginHit)),
                finalizer: new HarmonyMethod(typeof(KillMeleeAdapter), nameof(EndHit)));
            patches.Patch(AccessTools.Method(typeof(Dam_EnemyDamageLimb), "MeleeDamage"),
                prefix: new HarmonyMethod(typeof(KillMeleeAdapter), nameof(Hit)) { priority = Priority.First });
        }
        internal static bool BeginHit(MeleeWeaponThirdPerson __instance, GameObject damageGO, bool isPush, out KillStrikeScope __state)
        {
            __state = hitScope; hitScope = null;
            if (!isPush && SNet.IsMaster && SingleKillAdapter.TryStrikeScope(__instance, out var scope)) {
                var limb = damageGO?.GetComponent<Dam_EnemyDamageLimb>() ?? damageGO?.GetComponentInParent<Dam_EnemyDamageLimb>();
                if (limb?.m_base?.Owner?.Pointer != scope.Enemy.Pointer) return true;
                if (scope.Consumed) return false;
                if (!KillPositionAdapter.CanSwing(scope.Source, scope.Enemy, scope.Weapon, limb, scope.Source.Position,
                    scope.Source.EyePosition - scope.Source.Position, requireBehind: scope.RequireBehind)) {
                    scope.Consumed = true;
                    Plugin.Logger.LogInfo("[击杀命中] 挥击到达时落点／距离／遮挡改变，未派发伤害");
                    return false;
                }
                hitScope = scope;
            }
            return true;
        }
        internal static Exception EndHit(Exception __exception, KillStrikeScope __state)
        { hitScope = __state; return __exception; }
        internal static bool ValidLimb(Dam_EnemyDamageLimb limb, EnemyAgent enemy)
            => limb != null && enemy != null && !limb.IsDestroyed && limb.m_base?.Owner?.Pointer == enemy.Pointer;
        // Same selector for previews, release checks and the actual native collision callback.
        internal static Dam_EnemyDamageLimb Select(EnemyAgent enemy, Dam_EnemyDamageLimb original, float damage, float precision)
        {
            Dam_EnemyDamageLimb selected = null; float best = float.NegativeInfinity;
            var limbs = enemy?.Damage?.DamageLimbs;
            if (limbs != null) for (int i = 0; i < Math.Min(limbs.Count, 128); i++) {
                var limb = limbs[i];
                if (!ValidLimb(limb, enemy) || limb.m_type != eLimbDamageType.Weakspot) continue;
                float rank = limb.ApplyWeakspotAndArmorModifiers(damage, precision);
                if (!float.IsFinite(rank) || rank <= 0) continue;
                if (selected == null || rank > best || rank == best && limb.m_limbID < selected.m_limbID) { selected = limb; best = rank; }
            }
            return selected ?? (ValidLimb(original, enemy) ? original : null);
        }
        internal static float Estimate(Dam_EnemyDamageLimb limb, float damage, float precision, Vector3 position, Vector3 direction,
            float backstab, float sleeper, bool sleeping)
        {
            if (limb == null || !float.IsFinite(damage) || damage <= 0 || !float.IsFinite(precision)
                || !float.IsFinite(backstab) || !float.IsFinite(sleeper)) return float.NaN;
            float value = limb.ApplyWeakspotAndArmorModifiers(damage, precision);
            value = limb.ApplyDamageFromBehindBonus(value, position, direction, backstab);
            if (sleeping) value *= sleeper;
            value = Dam_EnemyDamageBase.RoundDamage(value);
            var enemy = limb.m_base.Owner;
            float maximum = enemy.Damage.DamageMax;
            if (!float.IsFinite(maximum) || maximum <= 0) return float.NaN;
            // Native sender packs damage relative to DamageMax; receiver rounds and applies enemy resistance.
            var packed = new UFloat16(); packed.Set(value, maximum);
            value = Dam_EnemyDamageBase.RoundDamage(packed.Get(maximum));
            return AgentModifierManager.ApplyModifier(enemy, AgentModifier.MeleeResistance, value);
        }
        internal static bool Hit(Dam_EnemyDamageLimb __instance, float dam, Agent sourceAgent, Vector3 position, Vector3 direction,
            float staggerMulti, float precisionMulti, float environmentMulti, float backstabberMulti, float sleeperMulti,
            bool skipLimbDestruction, DamageNoiseLevel damageNoiseLevel, uint gearCategoryId)
        {
            var scope = hitScope;
            if (scope == null || scope.Dispatching || !SNet.IsMaster || sourceAgent?.Pointer != scope.Source.Pointer
                || __instance?.m_base?.Owner?.Pointer != scope.Enemy.Pointer) return true;
            // A successful dispatch and a partially failed dispatch both consume the one native hit.
            if (scope.Consumed) return false;
            if (!scope.Source.Alive || scope.Source.Owner?.IsBot != true || !scope.Enemy.Alive || scope.Enemy.Damage?.IsImortal != false
                || !ValidLimb(__instance, scope.Enemy)) return true;
            Dam_EnemyDamageLimb selected;
            float estimate;
            try {
                selected = Select(scope.Enemy, __instance, dam, precisionMulti);
                estimate = Estimate(selected, dam, precisionMulti, position, direction, backstabberMulti, sleeperMulti,
                    scope.Enemy.Locomotion?.CurrentStateEnum == ES_StateEnum.Hibernate);
            } catch (Exception error) { Plugin.Logger.LogWarning("[击杀命中] 弱点核验失败，保留本次原版命中：" + error.Message); scope.Consumed = true; return true; }
            scope.Consumed = true;
            if (selected == null || selected.Pointer == __instance.Pointer) return true;
            scope.Dispatching = true;
            try {
                // Forward all native inputs unchanged. No direct HP write, packet or second original fallback.
                selected.MeleeDamage(dam, sourceAgent, position, direction, staggerMulti, precisionMulti, environmentMulti,
                    backstabberMulti, sleeperMulti, skipLimbDestruction, damageNoiseLevel, gearCategoryId);
                Plugin.Logger.LogInfo("[击杀命中] bot=" + scope.Source.PlayerName + " target=" + scope.Enemy.Pointer
                    + " limb=" + __instance.m_limbID + "→" + selected.m_limbID + " nativeEstimate=" + estimate.ToString("F2"));
            } catch (Exception error) { Plugin.Logger.LogWarning("[击杀命中] 原版弱点派发异常；不补发第二次伤害：" + error.Message); }
            finally { scope.Dispatching = false; }
            return false;
        }
        // Verified in this game's native UpdateStateStrike / DoAttackDamage: Force lerps 4..20,
        // melee modifier runs in both calls, temporary melee buff is x3, TP uses light precision and
        // passes backstab/sleeper=1. Do not substitute the human player's charged weapon formula.
        internal static float NativeDamage(PlayerAgent source, float force = 1)
        {
            float value = AgentModifierManager.ApplyModifier(source, AgentModifier.MeleeDamage, Mathf.Lerp(4, 20, force));
            if (source.MeleeBuffTimer > Time.time) value *= 3;
            return AgentModifierManager.ApplyModifier(source, AgentModifier.MeleeDamage, value);
        }
        internal static float Preview(PlayerAgent bot, EnemyAgent enemy, MeleeWeaponThirdPerson weapon, Dam_EnemyDamageLimb aim,
            Vector3 stand, Vector3 eyeOffset, bool awake = false, float force = 1)
        {
            float damage = NativeDamage(bot, force), precision = weapon.MeleeArchetypeData.LightPrecisionMulti;
            var selected = Select(enemy, aim, damage, precision);
            return Estimate(selected, damage, precision, aim.transform.position, (aim.transform.position - stand - eyeOffset).normalized,
                1, 1, !awake && enemy.Locomotion?.CurrentStateEnum == ES_StateEnum.Hibernate);
        }
    }
}
