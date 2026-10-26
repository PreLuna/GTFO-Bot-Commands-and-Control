using System;
using Player;
using AIGraph;
using UnityEngine;
using UnityEngine.AI;
namespace PreLuna.MicroMaster
{
    internal sealed class NativeManualWalk : IManualWalk
    {
        static readonly float[] StepLengths = { 1.2f, .6f, .3f, .15f, .08f };
        readonly PlayerAIBot bot;
        readonly long agent, owner;
        PlayerBotActionWalk.Descriptor walk;
        Il2CppSystem.Collections.Generic.LinkedList<Vector3> path;
        internal PlayerAIBot Bot => bot;
        internal void ClearPath() => path?.Clear();
        internal NativeManualWalk(PlayerAIBot bot)
        { this.bot = bot; agent = bot.Agent.Pointer.ToInt64(); owner = bot.Agent.Owner.Pointer.ToInt64(); }
        public bool Valid => bot?.Agent?.Pointer.ToInt64() == agent && bot.Agent.Owner?.Pointer.ToInt64() == owner
            && bot.Agent.Alive && ControlRuntime.Current?.Controls(bot.Agent) == true;
        internal bool IsOwn(PlayerBotActionBase.Descriptor desc) => desc != null && walk != null && desc.Pointer == walk.Pointer
            && desc.Bot?.Pointer == bot.Pointer;
        public bool TryDirection(Direction3 direction)
        {
            if (!Valid) return false;
            var start = bot.Agent.Position;
            var delta = new Vector3(direction.X, 0, direction.Z);
            if (!TryPoint(start, delta, out var point)) return false;
            if (walk == null || walk.IsTerminated()) {
                path = new Il2CppSystem.Collections.Generic.LinkedList<Vector3>();
                walk = new PlayerBotActionWalk.Descriptor(bot) { Prio = 100000, Haste = Plugin.WalkHaste.Value,
                    Posture = PlayerBotActionWalk.Descriptor.PostureEnum.Stand, Path = path };
                path.AddLast(point);
                if (!bot.RequestAction(walk)) { path.Clear(); walk = null; path = null; throw new InvalidOperationException("原版步行动作未被接受，已停止接管"); }
            } else {
                path.Clear(); path.AddLast(point); walk.Haste = Plugin.WalkHaste.Value;
            }
            return true;
        }
        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        internal bool RefreshBeforeNativeStep(Direction3 direction)
        {
            if (!Valid || walk == null || walk.IsTerminated() || path == null) return false;
            if (!TryPoint(bot.Agent.Position, new Vector3(direction.X, 0, direction.Z), out var point)) return false;
            path.Clear(); path.AddLast(point); return true;
        }
        bool TryPoint(Vector3 position, Vector3 direction, out Vector3 point)
        {
            point = default;
            if (!Finite(position) || !Finite(direction) || direction.sqrMagnitude < .001f || bot.Agent.CourseNode?.m_zone?.Layer == null) return false;
            direction.Normalize();
            // Project short segments onto the navmesh first, then cast along the slope.
            // Retry a shorter step before declaring a nearby ramp or stair impassable.
            foreach (float length in StepLengths)
                if (TrySegment(position, direction, length, out point)) return true;
            return false;
        }
        bool TrySegment(Vector3 position, Vector3 direction, float length, out Vector3 point)
        {
            point = default;
            int mask = LayerManager.MASK_WORLD | LayerManager.MASK_PLAYER_INTERACT_BLOCKERS;
            Vector3 desired = position + direction * length;
            const int allAreas = -1; // Same mask used by the existing Captain route adapter.
            if (!NavMesh.SamplePosition(position, out var origin, .5f, allAreas)
                || !NavMesh.SamplePosition(desired, out var destination, .65f, allAreas)) return false;
            var shift = destination.position - desired;
            if (new Vector2(shift.x, shift.z).sqrMagnitude > .15f * .15f || Math.Abs(destination.position.y - position.y) > .45f
                || NavMesh.Raycast(origin.position, destination.position, out _, allAreas)) return false;
            point = destination.position;
            if (!AIG_CourseNode.TryGetCourseNode(bot.Agent.DimensionIndex, point, 1, out var node)
                || node?.m_zone?.Layer?.Pointer != bot.Agent.CourseNode.m_zone.Layer.Pointer || node.m_zone.DimensionIndex != bot.Agent.DimensionIndex) return false;
            var segment = point - origin.position;
            float distance = MathF.Sqrt(segment.sqrMagnitude);
            if (distance < .04f || Physics.CapsuleCast(origin.position + Vector3.up * .55f, origin.position + Vector3.up * 1.45f,
                .25f, segment * (1 / distance), out var hit, distance + .04f, mask, QueryTriggerInteraction.Ignore)
                && hit.distance < distance + .04f) return false;
            return !Physics.CheckCapsule(point + Vector3.up * .55f, point + Vector3.up * 1.45f, .25f, mask, QueryTriggerInteraction.Ignore);
        }
        public void Stop()
        {
            var old = walk;
            // An empty path also causes native completion if StopAction cannot finish this frame.
            path?.Clear();
            if (old != null && !old.IsTerminated()) bot.StopAction(old);
            if (old == null || old.IsTerminated()) { walk = null; path = null; }
        }
    }
}
