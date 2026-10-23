using System;
using System.Collections.Generic;
using Enemies;
using Player;
using UnityEngine;
namespace PreLuna.MicroMaster
{
    // Samples the native dimension registry, rather than the robot's sight or the body crosshair.
    internal sealed class BodyThreatWatch
    {
        readonly HashSet<IntPtr> seen = new();
        readonly ThreatWindow window = new();
        float next, last = -1;
        internal void Reset() { next = 0; last = -1; seen.Clear(); window.Reset(); }
        internal string Check(PlayerAgent body, float now, bool enabled, float radius, int minimum, float confirmation)
        {
            if (!enabled) { Reset(); return null; }
            if (!float.IsFinite(now) || now < last) { Reset(); return "敌群检测时钟已改变"; }
            last = now;
            if (now < next) return null;
            next = now + .1f;
            var agents = body?.Dimension?.AgentsInDimension;
            var node = body?.CourseNode;
            if (agents == null || node?.m_zone?.Layer == null || agents.Count > 32768)
                return "本体附近敌群状态不可核验";
            radius = float.IsFinite(radius) ? Math.Clamp(radius, 3, 40) : 15;
            minimum = Math.Clamp(minimum, 1, 20);
            int count = 0;
            seen.Clear();
            for (int i = 0; i < agents.Count; i++) {
                var enemy = agents[i]?.TryCast<EnemyAgent>();
                if (enemy?.Alive != true || enemy.AI == null || (int)enemy.AI.Mode != 1
                    || enemy.m_isBeingDestroyed || enemy.DimensionIndex != body.DimensionIndex
                    || enemy.CourseNode?.m_zone?.Layer?.Pointer != node.m_zone.Layer.Pointer
                    || !seen.Add(enemy.Pointer)) continue;
                var delta = enemy.Position - body.Position;
                if (!float.IsFinite(delta.sqrMagnitude) || delta.sqrMagnitude > radius * radius) continue;
                if (++count >= minimum) break;
            }
            return window.Sample(now, count, minimum, confirmation)
                ? "本体附近已激活敌群达到退出阈值（至少" + minimum + "名／" + radius + "米）" : null;
        }
    }
    internal sealed class ThreatWindow
    {
        float since = -1, last = -1;
        internal void Reset() { since = last = -1; }
        internal bool Sample(float now, int count, int minimum, float confirmation)
        {
            if (!float.IsFinite(now) || now < last || last >= 0 && now - last > .5f) Reset();
            if (!float.IsFinite(now)) return false;
            last = now;
            if (count < Math.Max(1, minimum)) { since = -1; return false; }
            if (since < 0) since = now;
            confirmation = float.IsFinite(confirmation) ? Math.Clamp(confirmation, 0, 2) : .25f;
            return now - since >= confirmation;
        }
    }
}
