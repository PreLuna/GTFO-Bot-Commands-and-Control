using System;
using Enemies;
using Gear;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class KillPositionAdapter
    {
        static readonly float[] angles = { 0, -25, 25, -45, 45 };
        static readonly float[] radii = { 1.1f, 1.45f, .85f };
        internal static bool ClearStand(Vector3 point)
            => !Physics.CheckCapsule(point + Vector3.up * .35f, point + Vector3.up * 1.4f, .25f,
                LayerManager.MASK_DEFAULT | LayerManager.MASK_PLAYER_INTERACT_BLOCKERS, QueryTriggerInteraction.Ignore);
        internal static bool CanSwing(PlayerAgent bot, EnemyAgent enemy, MeleeWeaponThirdPerson weapon, Dam_EnemyDamageLimb aim,
            Vector3 stand, Vector3 eyeOffset, bool standChecked = false, bool requireBehind = true)
        {
            if (!KillMeleeAdapter.ValidLimb(aim, enemy) || weapon?.MeleeArchetypeData == null) return false;
            var relative = stand - enemy.Position; relative.y = 0;
            var forward = enemy.transform.forward; forward.y = 0;
            if (requireBehind && (forward.sqrMagnitude < .1f || Vector3.Dot(relative, forward.normalized) >= -.4f)
                || relative.sqrMagnitude >= PlayerBotActionMelee.s_strikeMaxDistanceSQ || !standChecked && !ClearStand(stand)) return false;
            float reach = weapon.MeleeArchetypeData.CameraDamageRayLength + weapon.MeleeArchetypeData.AttackSphereRadius;
            var eye = stand + eyeOffset;
            if (!float.IsFinite(reach) || reach <= 0 || Vector3.Distance(eye, aim.transform.position) > reach) return false;
            return !Physics.Linecast(eye, aim.transform.position,
                LayerManager.MASK_DEFAULT | LayerManager.MASK_PLAYER_INTERACT_BLOCKERS, QueryTriggerInteraction.Ignore);
        }
        internal static bool Find(PlayerAIBot bot, EnemyAgent enemy, MeleeWeaponThirdPerson weapon,
            out Transform aim, out Vector3 position, out float estimate)
        {
            aim = null; position = default; estimate = float.NaN;
            var limbs = enemy.Damage.DamageLimbs;
            if (limbs == null) return false;
            var forward = enemy.transform.forward; forward.y = 0;
            if (forward.sqrMagnitude < .1f) return false; forward = forward.normalized;
            float raw = KillMeleeAdapter.NativeDamage(bot.Agent), precision = weapon.MeleeArchetypeData.LightPrecisionMulti;
            var redirect = KillMeleeAdapter.Select(enemy, null, raw, precision);
            float best = float.NegativeInfinity, bestWalk = float.PositiveInfinity;
            bool bestWeak = false;
            // Preview is recalculated at the actual crouched eye position before release.
            var eyeOffset = Vector3.up * Math.Clamp(bot.Agent.EyePosition.y - bot.Agent.Position.y, .8f, 1.4f);
            foreach (var radius in radii) foreach (var angle in angles) {
                var desired = enemy.Position - Quaternion.Euler(0, angle, 0) * forward * radius;
                if (!PlayerAIBot.SnapPositionToNav(desired, out var point, .4f, 4f)
                    || Vector3.Distance(point, desired) > .4f || Math.Abs(point.y - enemy.Position.y) > .6f || !ClearStand(point)) continue;
                for (int i = 0; i < Math.Min(limbs.Count, 128); i++) {
                    var limb = limbs[i];
                    if (!CanSwing(bot.Agent, enemy, weapon, limb, point, eyeOffset, true)) continue;
                    float damage = KillMeleeAdapter.Estimate(redirect ?? limb, raw, precision, limb.transform.position,
                        (limb.transform.position - point - eyeOffset).normalized, 1, 1,
                        enemy.Locomotion?.CurrentStateEnum == ES_StateEnum.Hibernate);
                    if (!float.IsFinite(damage) || damage <= 0) continue;
                    // Prefer a physically reachable weakpoint, then native projected damage and shortest move.
                    bool weak = limb.m_type == eLimbDamageType.Weakspot;
                    float walk = Vector3.Distance(bot.Agent.Position, point);
                    if (aim == null || weak && !bestWeak || weak == bestWeak && (damage > best || damage == best && walk < bestWalk)) {
                        aim = limb.transform; position = point; estimate = damage; best = damage; bestWalk = walk; bestWeak = weak;
                    }
                }
            }
            return aim != null;
        }
    }
}
