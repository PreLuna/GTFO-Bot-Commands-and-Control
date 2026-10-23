using System;
using System.Collections.Generic;
using AIGraph;
using LevelGeneration;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    // Never infer reachability from straight-line distance or Zone membership alone.
    internal static class SearchTraversal
    {
        internal static bool InScope(AIG_CourseNode node, AIG_CourseNode origin, int scope)
            => node?.m_zone?.Layer != null && origin?.m_zone?.Layer != null
                && node.m_zone.DimensionIndex == origin.m_zone.DimensionIndex && node.m_zone.Layer.Pointer == origin.m_zone.Layer.Pointer
                && SearchAccessPolicy.InScope(node.m_zone.Pointer.ToInt64(), node.m_area?.Pointer.ToInt64() ?? 0,
                    origin.m_zone.Pointer.ToInt64(), origin.m_area?.Pointer.ToInt64() ?? 0, scope == 0);
        internal static bool Passable(AIG_CoursePortal portal)
        {
            if (portal == null) return false;
            // Native portal getters dereference the associated door/gate. Do not call
            // them for an uninitialized portal or a gate whose wall was removed.
            var core = portal.m_door;
            if (core == null) return portal.m_gate?.HasGateWallRemover == true && portal.m_gate.IsTraversable;
            if (portal.Gate == null) return false;
            var state = core.LastStatus switch {
                eDoorStatus.Closed => SearchDoorState.Closed, eDoorStatus.Unlocked => SearchDoorState.Unlocked,
                eDoorStatus.Open => SearchDoorState.Open, eDoorStatus.Opening => SearchDoorState.Opening,
                eDoorStatus.Destroyed => SearchDoorState.Destroyed,
                eDoorStatus.Closed_LockedWithNoKey => SearchDoorState.WeakLocked, _ => SearchDoorState.Blocked };
            var weak = core.TryCast<LG_WeakDoor>(); bool known = false, unsupported = false;
            if (weak != null && weak.WeakLocks != null) foreach (var item in weak.WeakLocks) {
                if (item == null || !item.IsLocked()) continue;
                if (item.m_lockType is eWeakLockType.Melee or eWeakLockType.Hackable) known = true;
                else unsupported = true;
            }
            return SearchAccessPolicy.Door(portal.IsTraversable, portal.IsProgressionLocked, true,
                weak != null && core.DoorType == eLG_DoorType.Weak, weak?.RequiresKey() == true,
                weak?.IsCheckpointDoor == true, state, known, unsupported);
        }
        internal static HashSet<long> Reachable(PlayerAgent agent, AIG_CourseNode start, AIG_CourseNode scopeOrigin,
            int scope, out bool truncated, Dictionary<long, AIG_CourseNode> nodes = null)
        {
            var queue = new Queue<AIG_CourseNode>(); var seen = new HashSet<long>();
            var pending = new HashSet<long>(); truncated = false;
            nodes?.Clear();
            if (start == null || !ItemRegion.SameSpace(agent, start) || scopeOrigin != null && !InScope(start, scopeOrigin, scope)) return seen;
            queue.Enqueue(start); pending.Add(start.Pointer.ToInt64());
            while (queue.Count != 0) {
                if (seen.Count >= 256) { truncated = true; break; }
                var node = queue.Dequeue(); seen.Add(node.Pointer.ToInt64());
                if (nodes != null) nodes[node.Pointer.ToInt64()] = node;
                if (node.m_portals == null) continue;
                foreach (var portal in node.m_portals) {
                    if (portal == null) continue;
                    var next = portal.m_nodeA?.Pointer == node.Pointer ? portal.m_nodeB
                        : portal.m_nodeB?.Pointer == node.Pointer ? portal.m_nodeA : null;
                    if (next == null || !ItemRegion.SameSpace(agent, next) || scopeOrigin != null && !InScope(next, scopeOrigin, scope)
                        || pending.Contains(next.Pointer.ToInt64()) || !Passable(portal)) continue;
                    pending.Add(next.Pointer.ToInt64()); queue.Enqueue(next);
                }
            }
            return seen;
        }
        internal static bool Standing(PlayerAgent agent, Vector3 point, AIG_CourseNode node, out Vector3 standing)
        {
            standing = default;
            if (node == null || !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return false;
            var toward = agent.Position - point; toward.y = 0;
            if (toward.sqrMagnitude < .01f) toward = Vector3.forward;
            toward = toward.normalized; var side = Vector3.Cross(Vector3.up, toward);
            for (int ring = 0; ring < 3; ring++) for (int i = 0; i < 8; i++) {
                var dir = i == 0 ? toward : i == 1 ? side : i == 2 ? -side : i == 3 ? -toward
                    : i == 4 ? (toward + side).normalized : i == 5 ? (toward - side).normalized
                    : i == 6 ? (-toward + side).normalized : (-toward - side).normalized;
                var desired = point + dir * (1.5f + ring * .5f);
                if (!PlayerAIBot.SnapPositionToNav(desired, out var candidate, 2, 8)
                    || Math.Abs(candidate.x - desired.x) > .75f || Math.Abs(candidate.z - desired.z) > .75f
                    || Math.Abs(candidate.y - desired.y) > 2.1f
                    || !AIG_CourseNode.TryGetCourseNode(agent.DimensionIndex, candidate, 1, out var resolved)
                    || resolved?.Pointer != node?.Pointer
                    || agent.CourseNode?.Pointer != node?.Pointer && Vector3.Distance(agent.Position, candidate) < 2.1f
                    || Physics.CheckCapsule(candidate + Vector3.up * .35f, candidate + Vector3.up * 1.4f, .25f,
                        LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore)) continue;
                standing = candidate; return true;
            }
            return false;
        }
    }
}
