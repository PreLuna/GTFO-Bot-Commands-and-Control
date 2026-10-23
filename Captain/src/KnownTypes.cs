using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace PreLuna.Captain
{
    // Query only a named, already loaded assembly. Never enumerate its types or load a DLL.
    internal sealed class KnownTypeResolver
    {
        sealed class Entry { public Type Type; public long Generation; }
        readonly Func<Assembly[]> inventory;
        readonly Func<long> generation;
        readonly Action<string> diagnostic;
        readonly Dictionary<(string, string), Entry> cache = new();
        readonly HashSet<string> reported = new(StringComparer.Ordinal);
        readonly object gate = new();
        public KnownTypeResolver(Func<Assembly[]> inventory, Func<long> generation, Action<string> diagnostic)
        { this.inventory = inventory; this.generation = generation; this.diagnostic = diagnostic; }
        void Report(string assembly, string name, string reason)
        {
            string message = "定向类型查找失败：" + assembly + " / " + name + "；" + reason;
            if (reported.Add(message)) diagnostic?.Invoke(message);
        }
        public Type Get(string assembly, string name)
        {
            if (string.IsNullOrWhiteSpace(assembly) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("必须指定程序集简单名称与完整类型名");
            lock (gate) {
                var key = (assembly, name);
                long epoch = generation();
                if (cache.TryGetValue(key, out var previous) && (previous.Type != null || previous.Generation == epoch)) return previous.Type;
                Assembly selected = null;
                int matches = 0;
                try {
                    foreach (var candidate in inventory()) {
                        if (candidate == null || !string.Equals(candidate.GetName().Name, assembly, StringComparison.Ordinal)) continue;
                        selected = candidate; matches++;
                    }
                    if (matches == 0) Report(assembly, name, "程序集尚未加载；新程序集加载后允许重新查找");
                    else if (matches > 1) Report(assembly, name, "存在多个同名程序集，拒绝任意选择");
                    else {
                        var type = selected.GetType(name, throwOnError: false, ignoreCase: false);
                        if (type != null) { cache[key] = new() { Type = type, Generation = epoch }; return type; }
                        Report(assembly, name, "程序集已加载，但接口类型不存在");
                    }
                } catch (Exception error) { Report(assembly, name, error.GetType().Name + ": " + error.Message); }
                cache[key] = new() { Generation = epoch };
                return null;
            }
        }
    }
    internal static class KnownTypes
    {
        static long generation;
        internal static Action<string> Warning;
        static readonly KnownTypeResolver resolver = new(
            () => AppDomain.CurrentDomain.GetAssemblies(), () => Interlocked.Read(ref generation), message => Warning?.Invoke(message));
        static KnownTypes() { AppDomain.CurrentDomain.AssemblyLoad += (_, _) => Interlocked.Increment(ref generation); }
        internal static Type Get(string assembly, string name) => resolver.Get(assembly, name);
    }
}
