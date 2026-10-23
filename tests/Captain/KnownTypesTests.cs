using System;
using System.Collections.Generic;
using System.Reflection;
using PreLuna.Captain;

static class KnownTypesTests
{
    sealed class ProbeAssembly : Assembly
    {
        readonly string name;
        public int Names, Queries, Enumerations;
        public bool Fail;
        public ProbeAssembly(string name) { this.name = name; }
        public override AssemblyName GetName() { Names++; return new AssemblyName(name); }
        public override Type GetType(string name, bool throwOnError, bool ignoreCase)
        {
            Queries++;
            if (Fail) throw new TypeLoadException("测试接口损坏");
            return name == "Probe.Valid" ? typeof(KnownTypesTests) : null;
        }
        public override Type[] GetTypes() { Enumerations++; throw new ReflectionTypeLoadException(Array.Empty<Type>(), new Exception[] { new TypeLoadException("OptionValue") }); }
    }
    static int checks;
    static void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
    static void Main()
    {
        long generation = 0; int inventories = 0;
        var target = new ProbeAssembly("Wanted"); var poison = new ProbeAssembly("SteamWorks_ASM");
        Assembly[] loaded = { poison, target }; var messages = new List<string>();
        var resolver = new KnownTypeResolver(() => { inventories++; return loaded; }, () => generation, messages.Add);
        Check(resolver.Get("Wanted", "Probe.Valid") == typeof(KnownTypesTests), "读取命名程序集的单个类型");
        Check(poison.Queries == 0 && poison.Enumerations == 0 && target.Enumerations == 0, "不枚举损坏的SteamWorks类型");
        int before = inventories;
        for (int i = 0; i < 1000; i++) Check(resolver.Get("Wanted", "Probe.Valid") == typeof(KnownTypesTests), "缓存保持类型身份");
        Check(inventories == before && target.Queries == 1, "1000次重复请求不扫描程序集或重新GetType");
        Check(resolver.Get("Late", "Probe.Valid") == null && messages.Count == 1, "缺少程序集有诊断");
        Check(messages[0].Contains("Late") && messages[0].Contains("Probe.Valid"), "诊断指出程序集和完整接口");
        before = inventories;
        for (int i = 0; i < 1000; i++) resolver.Get("Late", "Probe.Valid");
        Check(inventories == before && messages.Count == 1, "未加载程序集的失败缓存不重复扫描或刷屏");
        loaded = new Assembly[] { poison, target, new ProbeAssembly("Late") }; generation++;
        Check(resolver.Get("Late", "Probe.Valid") == typeof(KnownTypesTests), "加载迟到的前置后重新解析");
        Check(resolver.Get("Wanted", "Probe.Missing") == null && messages.Count == 2, "接口缺失仍有诊断");
        before = target.Queries;
        resolver.Get("Wanted", "Probe.Missing");
        Check(target.Queries == before && messages.Count == 2, "缓存缺失类型");
        var broken = new ProbeAssembly("Broken") { Fail = true }; loaded = new Assembly[] { broken }; generation++;
        Check(resolver.Get("Broken", "Probe.Valid") == null && messages.Count == 3, "真正类型加载异常可定位");
        Check(messages[2].Contains("TypeLoadException") && messages[2].Contains("测试接口损坏"), "保留异常类型和原因");
        loaded = new Assembly[] { new ProbeAssembly("Duplicate"), new ProbeAssembly("Duplicate") }; generation++;
        Check(resolver.Get("Duplicate", "Probe.Valid") == null && messages.Count == 4, "重复程序集不任意选取");
        bool invalid = false; try { resolver.Get("", "Probe.Valid"); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "配置错误不静默");
        Console.WriteLine("PASS: 定向类型缓存 " + checks + " 项；含1000次成功查找和1000次失败缓存检查。");
    }
}
