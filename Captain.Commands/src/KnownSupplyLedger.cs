using System;
using System.Collections.Generic;
namespace PreLuna.Captain.Tasks
{
    [Flags] internal enum SupplySource { None = 0, PlayerVisit = 1, PlayerMark = 2, QueryMarker = 4, Aimed = 8, RobotSearch = 16 }
    // Discovery is an explicit event, never inferred from membership in the world index.
    internal sealed class KnownSupplyLedger<T>
    {
        internal sealed class Entry { public int Id; public long Key; public T Value; public SupplySource Sources; }
        readonly Dictionary<long, Entry> values = new();
        int next;
        public int Count => values.Count;
        public IEnumerable<Entry> Entries => values.Values;
        public Entry Observe(long key, T value, SupplySource source)
        {
            if (key == 0 || value == null || source == SupplySource.None) return null;
            if (!values.TryGetValue(key, out var entry)) {
                if (values.Count >= 2048) return null;
                values[key] = entry = new() { Id = ++next, Key = key };
            }
            entry.Value = value; entry.Sources |= source; return entry;
        }
        public void Forget(long key) => values.Remove(key);
        public bool Current(long key, int id) => values.TryGetValue(key, out var entry) && entry.Id == id;
        public void Reset() { values.Clear(); /* Never reuse an id in an old captured menu. */ }
        public static string Describe(SupplySource source)
        {
            var parts = new List<string>();
            if ((source & SupplySource.PlayerVisit) != 0) parts.Add("玩家访问");
            if ((source & SupplySource.PlayerMark) != 0) parts.Add("玩家标记");
            if ((source & SupplySource.QueryMarker) != 0) parts.Add("查询／模组标记");
            if ((source & SupplySource.Aimed) != 0) parts.Add("准星发现");
            if ((source & SupplySource.RobotSearch) != 0) parts.Add("队长即时搜寻");
            return string.Join("、", parts);
        }
    }
}

