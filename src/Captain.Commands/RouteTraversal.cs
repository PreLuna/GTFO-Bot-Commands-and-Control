using System;
using System.Collections.Generic;
using AIGraph;
using LevelGeneration;
using Player;
using UnityEngine;
using UnityEngine.AI;
namespace PreLuna.Captain.Tasks
{
    internal sealed record RoutePlan(Vector3[] Points, AIG_CourseNode EndNode, AIG_CoursePortal Door, bool WaitingForDoor, AIG_CoursePortal[] Passages, long[] Nodes);
    internal static class RouteTraversal
    {
        static bool Finite(Vector3 point) => float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        internal static float Deviation(Vector3 position, Vector3[] points)
        {
            if (!Finite(position) || points == null || points.Length < 2) return float.PositiveInfinity;
            float nearest = float.PositiveInfinity;
            for (int i = 1; i < points.Length; i++) {
                var delta = points[i] - points[i - 1]; float length = Vector3.Dot(delta, delta);
                float t = length < .001f ? 0 : Math.Clamp(Vector3.Dot(position - points[i - 1], delta) / length, 0, 1);
                nearest = Math.Min(nearest, Vector3.Distance(position, points[i - 1] + delta * t));
            }
            return nearest;
        }
        internal static bool Open(AIG_CoursePortal portal) => portal != null && portal.m_gate != null
            && !portal.IsProgressionLocked && portal.IsTraversable;
        static bool Sample(PlayerAgent player, Vector3 desired, AIG_CourseNode required, out Vector3 point, bool objectEndpoint = false)
        {
            point = default;
            if (!Finite(desired) || !NavMesh.SamplePosition(desired, out var hit, objectEndpoint ? 2.5f : 1.5f, -1) || !Finite(hit.position)
                || Math.Abs(hit.position.y - desired.y) > (objectEndpoint ? 2.1f : 1.25f)
                || !AIG_CourseNode.TryGetCourseNode(player.DimensionIndex, hit.position, .5f, out var node)
                || node?.Pointer != required?.Pointer) return false;
            point = hit.position; return true;
        }
        static bool Endpoint(PlayerAgent player, Vector3 desired, AIG_CourseNode required, bool door, out Vector3 point)
        {
            point = default;
            if (!door && Sample(player, desired, required, out point, true)) return true;
            // Search near the object, never teleport or move a bot. A door's centre
            // may be a wall, so only accept a point proven to be on the near side.
            for (int ring = 0; ring < 2; ring++) for (int i = 0; i < 8; i++) {
                float angle = i * Mathf.PI / 4;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (1.8f + ring * .7f);
                if (Sample(player, desired + offset, required, out point, true)) return true;
            }
            return false;
        }
        internal static RoutePlan Build(PlayerAgent player, AIG_CourseNode target, Vector3 position, AIG_CoursePortal targetPortal,
            AIG_CourseNode origin, out string reason)
        {
            reason = null; var start = player?.CourseNode;
            if (start == null || !ItemRegion.SameSpace(player, target) || !SearchTraversal.InScope(start, origin, 1)
                || !SearchTraversal.InScope(target, origin, 1) || !Finite(position)) { reason = "目标不在本次导航的当前Zone／维度／层级"; return null; }
            var nodes = new Dictionary<long, AIG_CourseNode>();
            var reachable = SearchTraversal.Reachable(player, start, origin, 1, out bool truncated, nodes);
            if (truncated) { reason = "区域超过256个节点的导航预算，请选择更近目标"; return null; }
            if (!reachable.Contains(target.Pointer.ToInt64())) { reason = "目标通路有封锁门、钥匙门或不支持的锁，不能导航"; return null; }
            var portals = new Dictionary<long, AIG_CoursePortal>();
            IEnumerable<RouteEdge> Edges(long id) {
                if (!nodes.TryGetValue(id, out var node) || node.m_portals == null) yield break;
                foreach (var portal in node.m_portals) {
                    if (portal == null || !SearchTraversal.Passable(portal)) continue;
                    var next = portal.m_nodeA?.Pointer == node.Pointer ? portal.m_nodeB : portal.m_nodeB?.Pointer == node.Pointer ? portal.m_nodeA : null;
                    if (next == null || !reachable.Contains(next.Pointer.ToInt64())) continue;
                    portals[portal.Pointer.ToInt64()] = portal;
                    yield return new(next.Pointer.ToInt64(), portal.Pointer.ToInt64(), !Open(portal));
                }
            }
            var corridor = RoutePolicy.Find(start.Pointer.ToInt64(), target.Pointer.ToInt64(), Edges, false)
                ?? RoutePolicy.Find(start.Pointer.ToInt64(), target.Pointer.ToInt64(), Edges, true);
            if (corridor == null) { reason = "未找到预算内的可达路线"; return null; }
            var allowed = reachable; var passages = new List<AIG_CoursePortal>(); var end = target; AIG_CoursePortal waiting = null;
            long previous = start.Pointer.ToInt64();
            foreach (var edge in corridor) {
                if (edge.Closed) { waiting = portals[edge.Portal]; end = nodes[previous]; position = waiting.Position; break; }
                passages.Add(portals[edge.Portal]); previous = edge.To;
            }
            if (!Sample(player, player.Position, start, out var from)
                || !Endpoint(player, position, end, waiting != null || targetPortal != null, out var to)) {
                reason = "目标附近没有处于正确房间的步行导航网格"; return null;
            }
            // Own a single temporary native path and release its native buffer explicitly.
            var native = new NavMeshPath();
            try {
                if (!NavMesh.CalculatePath(from, to, -1, native) || native.status != NavMeshPathStatus.PathComplete) {
                    reason = "原版步行导航网格未给出完整路径（梯子／断开网格暂不绘制）"; return null;
                }
                var corners = native.corners;
                if (corners == null || corners.Length < 2 || corners.Length > 128) { reason = "路径拐点无效或超出128点预算"; return null; }
                var points = new Vector3[corners.Length]; var samples = new List<long>();
                for (int i = 0; i < corners.Length; i++) {
                    points[i] = corners[i];
                    if (!Finite(points[i])) { reason = "路径包含无效坐标"; return null; }
                    float length = i == 0 ? 0 : Vector3.Distance(points[i - 1], points[i]);
                    if (!float.IsFinite(length) || length > 1024) { reason = "路径长度无效或超出验证预算"; return null; }
                    int steps = (int)Math.Ceiling(length);
                    if (samples.Count + steps + 1 > 1024) { reason = "步行路线超出逐段验证预算"; return null; }
                    for (int step = i == 0 ? 0 : 1; step <= steps; step++) {
                        var point = i == 0 ? points[0] : Vector3.Lerp(points[i - 1], points[i], step / (float)Math.Max(1, steps));
                        if (!AIG_CourseNode.TryGetCourseNode(player.DimensionIndex, point, .5f, out var node)
                            || node == null || !SearchTraversal.InScope(node, origin, 1) || !ItemRegion.SameSpace(player, node)) {
                            reason = "路径经过未知房间或超出本Zone，已拒绝绘制"; return null;
                        }
                        samples.Add(node.Pointer.ToInt64());
                    }
                }
                bool Link(long a, long b) {
                    foreach (var edge in Edges(a)) if (edge.To == b && !edge.Closed) {
                        var portal = portals[edge.Portal];
                        if (!passages.Exists(p => p.Pointer == portal.Pointer)) passages.Add(portal);
                        return true;
                    }
                    return false;
                }
                if (!RoutePolicy.ValidateSamples(samples, start.Pointer.ToInt64(), end.Pointer.ToInt64(), allowed, Link)) {
                    reason = "完整路线没有通过房间／开门状态验证，已拒绝绘制"; return null;
                }
                return new(points, end, waiting ?? targetPortal, waiting != null, passages.ToArray(), new List<long>(new HashSet<long>(samples)).ToArray());
            } finally {
                if (native.m_Ptr != IntPtr.Zero) { NavMeshPath.DestroyNavMeshPath(native.m_Ptr); native.m_Ptr = IntPtr.Zero; }
            }
        }
    }
}
