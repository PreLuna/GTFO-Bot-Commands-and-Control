using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HarmonyLib;
using LevelGeneration;
using Player;

namespace PreLuna.Captain.Tasks
{
    internal readonly record struct StackObservation(int ActiveCount, bool Verified, string Provider, string Error,
        float ResourceLimit = 100, float ConsumableMultiplier = 1, bool Uncapped = false);
    internal readonly record struct StackRule(bool Enabled, float Limit, string Provider, string Error);

    // This adapter never installs a stacking patch or changes a framework setting.
    // Read at task validation/commit, not in a per-frame update. Tests inject the
    // patch observation; the production path reads Harmony's actual patch list.
    internal sealed class StackCompatibility
    {
        internal static StackCompatibility Shared { get; set; } = new();
        readonly Func<StackObservation> observe;
        string lastDiagnostic;
        internal StackCompatibility(Func<StackObservation> observation = null) { observe = observation ?? StackRuntime.Observe; }
        internal StackRule Read(InventorySlot slot, float consumableMax)
        {
            StackObservation state;
            try { state = observe(); }
            catch (Exception ex) { state = new(0, false, null, "读取实际堆叠补丁失败：" + ex.GetBaseException().Message); }
            string error = state.Error;
            if (state.ActiveCount > 1) error = "多个堆叠补丁同时生效，停止合并；请只保留一个实现";
            else if (state.ActiveCount == 1 && !state.Verified) error ??= "实际堆叠补丁代码尚未核验，无法确定容量规则";
            else if (state.ActiveCount == 0) error ??= "未发现生效的堆叠补丁；仅有 DLL 文件或关闭的 Core 功能不算启用";
            if (!float.IsFinite(state.ResourceLimit) || state.ResourceLimit <= 0
                || !float.IsFinite(state.ConsumableMultiplier) || state.ConsumableMultiplier <= 0)
                error = "堆叠容量规则非法；停止合并";
            bool enabled = state.ActiveCount == 1 && state.Verified && error == null;
            float limit = state.Uncapped ? float.MaxValue : slot == InventorySlot.ResourcePack ? state.ResourceLimit : consumableMax * state.ConsumableMultiplier;
            string diagnostic = enabled ? state.Provider + (state.Uncapped ? "；资源和普通消耗品全量累计，无玩法容量上限；保留数量守恒核验"
                : $"；资源包容量{state.ResourceLimit}，消耗品实际ConsumableAmmoMax×{state.ConsumableMultiplier}；保留数量守恒核验") : error;
            if (lastDiagnostic != diagnostic)
            {
                lastDiagnostic = diagnostic;
                if (enabled) Plugin.Logger.LogInfo("队长堆叠兼容：" + diagnostic);
                else Plugin.Logger.LogWarning("队长堆叠兼容：" + diagnostic);
            }
            return new(enabled, limit, state.Provider, error);
        }
    }

    internal static class StackRuntime
    {
        internal const string StandalonePrefixHash = "209F84E7DD8B868D58C79A0C202DFDAFD1C9FAAB44ADE1EFE95D4712700BD49C";
        internal const string StandaloneRuleHash = "89587F31D9CA4AFB160F99BCE80CB00F9A7D0957B95A6F54C7409A21441E9ECE";
        internal const string CorePrefixHash = "51D5C2413DCE4291EC031DCCABFE0371CD891B087D54EEE1EA3D979193506464";
        internal const string CoreRuleHash = "EBE9A442A74AA2FEB02380A72336E1D47AFCF3FAFE788E37F3EFAAC791AF3D56";
        internal const string LocalProvider = "本地 MaxConsumables 1.0.4（唯一实现／全量累计）";
        internal const string LocalPrefixHash = "43C4496C3C72AAECAB9A3E4A2A5378F3834FB92335DF24803473FB3FEB407BB0";
        internal const string LocalAssemblyHash = "F93D3163E773008F86A0F37C02318CF710CEE1850D85E49AC752A63C55AB18A2";
        static readonly Dictionary<MethodInfo, string> fingerprints = new();
        static readonly Dictionary<Assembly, string> assemblyFingerprints = new();
        static MethodInfo target, coreQuery, localBlocked;
        static Type coreQueryType;
        internal static StackObservation Observe()
        {
            target ??= AccessTools.Method(typeof(LG_PickupItem_Sync), "AttemptInteract", new[] { typeof(pPickupItemInteraction) });
            if (target == null) return new(0, false, null, "找不到原版拾取补丁入口 AttemptInteract");
            var state = Probe(Harmony.GetPatchInfo(target)?.Prefixes, CoreEnabled, Fingerprint);
            if (state.Verified && state.ActiveCount == 1 && state.Provider == LocalProvider)
            {
                var type = KnownTypes.Get("PreLuna.MaxConsumables", "PreLuna.MaxConsumables.Patch");
                if (!Registered(typeof(PlayerAmmoStorage), "UpdateAmmoInPack", type, "UpdateAmmoInPack_Override_Prefix", false)
                    || !Registered(typeof(PlayerAmmoStorage), "UpdateBulletsInPack", type, "UpdateBulletsInPack_Override_Prefix", false)
                    || !Registered(typeof(PlayerBackpackManager), "OnUpdateCustomData", type, "InventoryCustomData_Postfix", true))
                    return state with { Verified = false, Error = "本地直接合并的容量或库存同步补丁未完整注册；停止合并" };
            }
            return state;
        }
        static bool Registered(Type native, string original, Type type, string handler, bool postfix)
        {
            var method = AccessTools.Method(native, original);
            var expected = type?.GetMethod(handler, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var info = method == null ? null : Harmony.GetPatchInfo(method);
            var patches = postfix ? info?.Postfixes : info?.Prefixes;
            return expected != null && patches != null
                && patches.Count(p => p.owner == "PreLuna.MaxConsumables" && p.PatchMethod == expected) == 1;
        }
        static bool? CoreEnabled()
        {
            var feature = KnownTypes.Get("Hikaria.Core", "Hikaria.Core.Features.Accessibility.ResourceStack");
            var manager = KnownTypes.Get("TheArchive.Core", "TheArchive.Core.FeaturesAPI.FeatureManager");
            if (feature == null || manager == null) return null;
            if (coreQuery == null || coreQueryType != feature)
            {
                foreach (var method in manager.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (method.Name == "IsFeatureEnabled" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0)
                    { coreQuery = method.MakeGenericMethod(feature); coreQueryType = feature; break; }
            }
            return coreQuery == null ? null : (bool)coreQuery.Invoke(null, null);
        }
        static string Fingerprint(MethodInfo method)
        {
            if (method == null) return null;
            if (fingerprints.TryGetValue(method, out var hash)) return hash;
            var body = method.GetMethodBody()?.GetILAsByteArray();
            hash = body == null ? null : Convert.ToHexString(SHA256.HashData(body));
            fingerprints[method] = hash;
            return hash;
        }
        static string AssemblyFingerprint(Assembly assembly)
        {
            if (assemblyFingerprints.TryGetValue(assembly, out var cached)) return cached;
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            foreach (var type in assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                writer.Write(type.FullName);
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(f => f.IsLiteral).OrderBy(f => f.Name, StringComparer.Ordinal))
                { writer.Write(field.Name); writer.Write(Convert.ToString(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture) ?? ""); }
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)).OrderBy(m => m.MetadataToken))
                {
                    writer.Write(method.MetadataToken); var body = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
                    writer.Write(body.Length); writer.Write(body);
                }
            }
            writer.Flush(); cached = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
            assemblyFingerprints[assembly] = cached; return cached;
        }
        // Capacity rules are accepted only for the audited implementations.
        // Hash managed methods in memory, so a replacement on disk cannot describe
        // the old DLL still running in the process. Hashes are cached per method.
        internal static StackObservation Probe(IEnumerable<Patch> prefixes, Func<bool?> coreEnabled, Func<MethodInfo, string> fingerprint,
            Func<bool> localBlockedOverride = null)
        {
            int count = 0; bool verified = true; string provider = null, error = null;
            float resourceLimit = 100, consumableMultiplier = 1;
            bool uncapped = false;
            if (prefixes == null) return new(0, false, null, null);
            var standalone = KnownTypes.Get("Hikaria.ResourceStack", "Hikaria.ResourceStack.Patches.Patch_LG_PickupItem_Sync");
            var core = KnownTypes.Get("Hikaria.Core", "Hikaria.Core.Features.Accessibility.ResourceStack");
            var corePatch = core?.GetNestedType("LG_PickupItem_Sync__AttemptInteract__Patch", BindingFlags.Public | BindingFlags.NonPublic);
            var local = KnownTypes.Get("PreLuna.MaxConsumables", "PreLuna.MaxConsumables.Patch");
            foreach (var patch in prefixes)
            {
                var method = patch.PatchMethod; var type = method?.DeclaringType;
                if (type != null && type == local)
                {
                    count++;
                    bool localMatches = patch.owner == "PreLuna.MaxConsumables" && method.Name == "AttemptInteract_Prefix"
                        && fingerprint(method) == LocalPrefixHash && AssemblyFingerprint(type.Assembly) == LocalAssemblyHash;
                    if (localMatches)
                    {
                        localBlocked ??= type.Assembly.GetType("PreLuna.MaxConsumables.CoreOverlap")?.GetMethod("Blocked", BindingFlags.Static | BindingFlags.NonPublic);
                        bool paused = localBlockedOverride != null ? localBlockedOverride()
                            : localBlocked == null || (bool)localBlocked.Invoke(null, null);
                        if (paused)
                        { localMatches = false; error = "本地堆叠已注册但运行时仍暂停；请查看 [堆叠状态] 的开关／补丁／接管诊断"; }
                    }
                    if (core != null && coreEnabled() != false)
                    { localMatches = false; error = "本地直接合并要求 Core 堆叠关闭，当前开关未确认关闭"; }
                    verified &= localMatches; provider = LocalProvider; resourceLimit = float.MaxValue; consumableMultiplier = 1; uncapped = true;
                    if (!localMatches) error ??= "本地 MaxConsumables 代码与已核验版本不同；停止合并";
                    continue;
                }
                bool independent = type != null && type == standalone;
                bool framework = type != null && type == corePatch;
                if (framework)
                {
                    bool? enabled = coreEnabled();
                    if (enabled == false) continue;
                    if (enabled == null) { error = "Core 堆叠补丁存在，但无法确认功能开关；停止合并"; verified = false; }
                }
                if (!independent && !framework)
                {
                    if (patch.owner is "Hikaria.ResourceStack" or "INF.MaxConsumables" or "PreLuna.MaxConsumables"
                        || type?.FullName?.IndexOf("Stack", StringComparison.OrdinalIgnoreCase) >= 0)
                    { count++; verified = false; error = "发现未适配的拾取堆叠补丁：" + patch.owner + "/" + type?.FullName; }
                    continue;
                }
                count++;
                var ruleType = independent ? standalone : core;
                var rule = ruleType.GetMethod("TryStackItem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Item), typeof(SNetwork.SNet_Player) }, null);
                bool matches = method.Name == (independent ? "LG_PickupItem_Sync__AttemptInteract__Prefix" : "Prefix")
                    && (!independent || patch.owner == "Hikaria.ResourceStack")
                    && fingerprint(method) == (independent ? StandalonePrefixHash : CorePrefixHash)
                    && rule != null && fingerprint(rule) == (independent ? StandaloneRuleHash : CoreRuleHash);
                verified &= matches;
                provider = independent ? "独立 Hikaria.ResourceStack 2.1.1（实际补丁及代码已核验）" : "Core ResourceStack（实际补丁、开关及代码已核验）";
                if (!matches) error = "堆叠代码与已核验规则不同：" + type.FullName + "；请核验新版容量后适配";
            }
            return new(count, verified, provider, error, resourceLimit, consumableMultiplier, uncapped);
        }
    }
}
