using System;
using System.Collections.Generic;
namespace PreLuna.Captain.Tasks
{
    internal sealed record RouteEdge(long To, long Portal, bool Closed);
    internal static class RoutePolicy
    {
        // Prefer an entirely open walking corridor. Closed ordinary doors are only
        // considered by the second pass, whose rendered destination is the first door.
        internal static List<RouteEdge> Find(long start, long target, Func<long, IEnumerable<RouteEdge>> edges, bool allowClosed)
        {
            var queue = new Queue<long>(); var parents = new Dictionary<long, (long From, RouteEdge Edge)>();
            if (start == 0 || target == 0) return null;
            queue.Enqueue(start); parents[start] = default; int examined = 0;
            while (queue.Count > 0) {
                long node = queue.Dequeue();
                if (node == target) {
                    var path = new List<RouteEdge>();
                    while (node != start) { var parent = parents[node]; path.Add(parent.Edge); node = parent.From; }
                    path.Reverse(); return path;
                }
                foreach (var edge in edges(node)) {
                    if (++examined > 8192) return null;
                    if (edge.To == 0 || edge.Portal == 0 || edge.Closed && !allowClosed || parents.ContainsKey(edge.To)) continue;
                    if (parents.Count >= 256) return null;
                    parents[edge.To] = (node, edge); queue.Enqueue(edge.To);
                }
            }
            return null;
        }
        internal static bool ValidateSamples(IReadOnlyList<long> samples, long start, long end, HashSet<long> allowed,
            Func<long, long, bool> openLink)
        {
            if (samples == null || samples.Count < 1 || samples.Count > 1024 || samples[0] != start || samples[^1] != end) return false;
            long previous = start;
            foreach (long node in samples) {
                if (node == 0 || !allowed.Contains(node) || node != previous && !openLink(previous, node)) return false;
                previous = node;
            }
            return true;
        }
        internal static bool Replan(float now, float lastPlan, bool changedNode, bool changedDoor, float moved, float targetMoved)
            => now - lastPlan >= 2 && (changedDoor || targetMoved >= 1 || changedNode || moved >= 3);
    }
}
