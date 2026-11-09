using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BoosterImplants;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PreLuna.Captain.Boosters
{
    // CheckCondition is inlined in GTFO. Normalize inventory before preparation
    // and native packet conversion instead of trying to bypass that function.
    internal static class BoosterConditionCompat
    {
        const string PerfectFeature = "Hikaria.BoosterTweaker.Features.PerfectBooster";
        static Harmony patches;
        static bool enabled = true, farmer = true, installed, bound, ready, normalizing;
        static float nextProbe, nextWarning;
        static PropertyInfo providerTemplates, perfectSettings;
        static MethodInfo featureQuery;
        static readonly Dictionary<MethodBase, bool> settingsRules = new();
        static readonly List<PerfectTemplate> templates = new();
        sealed class Snapshot { public EffectValue[] Effects; public uint[] Conditions; public string After; public int Revision; }
        static readonly Dictionary<(IntPtr, uint), Snapshot> normalized = new();
        static int revision;
        static string lastState;
        // Effective conditions are removed. Do not guess identities from the old
        // condition-preserving route when restoring a saved bot choice.
        internal static bool RawConditionsPreserved => false;

        internal static void Configure(ConfigFile config)
        {
            enabled = config.Bind("完美强化剂", "简化普通模式", true,
                "保留普通完美与不消耗，关闭两种自定义库存；实际库存去条件、去负面并按对应模板拉满。修改后重启。").Value;
            farmer = config.Bind("完美强化剂", "沿用快速刷取", true,
                "使用BoosterTweaker已有结算奖励100000机制；大量随机补充，不保证每种各有一枚。修改后重启。").Value;
        }
        internal static void Install()
        {
            if (!enabled || installed) return;
            patches = new Harmony(Plugin.Guid + ".ordinaryperfect");
            try
            {
                Patch(AccessTools.Method(typeof(BoosterImplantManager), "Update", Type.EmptyTypes), after: nameof(TickPostfix));
                Patch(AccessTools.Method(typeof(PersistentInventoryManager), "PrepareBoosterImplantForInjection", new[] { typeof(uint) }), before: nameof(PreparePrefix));
                installed = true;
                TryBind(); // Metadata only: provider settings may not exist yet.
            }
            catch (Exception ex) { Stop(ex); }
        }
        static void Patch(MethodInfo target, string before = null, string after = null)
        {
            if (target == null) throw new MissingMethodException("普通完美兼容目标不存在");
            patches.Patch(target, prefix: before == null ? null : new HarmonyMethod(typeof(BoosterConditionCompat), before),
                postfix: after == null ? null : new HarmonyMethod(typeof(BoosterConditionCompat), after));
        }
        static PropertyInfo Property(Type type, string name) => type?.GetProperty(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public);
        static void TryBind()
        {
            if (bound) return;
            var manager = KnownTypes.Get("Hikaria.BoosterTweaker", "Hikaria.BoosterTweaker.Managers.BoosterImplantTemplateManager");
            var perfect = KnownTypes.Get("Hikaria.BoosterTweaker", "Hikaria.BoosterTweaker.Features.PerfectBooster");
            var features = KnownTypes.Get("TheArchive.Core", "TheArchive.Core.FeaturesAPI.FeatureManager");
            if (manager == null || perfect == null || features == null) return;
            var rules = new Dictionary<MethodBase, bool>();
            void Rule(Type type, string name, bool value)
            {
                var p = Property(type, name);
                if (p?.PropertyType != typeof(bool) || p.GetMethod == null || p.SetMethod == null) throw new MissingMemberException(type.FullName, name);
                rules.Add(p.SetMethod, value);
            }
            var ps = Property(perfect, "Settings");
            var pt = Property(manager, "BoosterImplantTemplates");
            var query = features.GetMethod("IsFeatureEnabled", new[] { typeof(string), typeof(bool) });
            var apply = AccessTools.Method(perfect, "ApplyPerfectBoosterImplants", Type.EmptyTypes);
            var load = AccessTools.Method(manager, "LoadTemplateData", Type.EmptyTypes);
            if (ps?.GetMethod == null || pt?.GetMethod == null || query?.ReturnType != typeof(bool) || apply == null || load == null)
                throw new MissingMethodException("BoosterTweaker普通完美／模板接口变化");
            Rule(manager, "EnableCustomPerfectBooster", false); Rule(manager, "EnableCustomBooster", false);
            Rule(manager, "DisableBoosterConditions", true); Rule(manager, "DisableBoosterNegativeEffects", true);
            Rule(ps.PropertyType, "EnablePerfectBooster", true); Rule(ps.PropertyType, "DisableBoosterConsume", true);
            Rule(ps.PropertyType, "EnableBoosterFarmer", farmer);
            foreach (var rule in rules) Patch((MethodInfo)rule.Key, before: nameof(SettingPrefix));
            Patch(apply, before: nameof(PerfectPrefix));
            Patch(load, after: nameof(TemplatesPostfix));
            foreach (var rule in rules) settingsRules.Add(rule.Key, rule.Value);
            providerTemplates = pt; perfectSettings = ps; featureQuery = query; bound = true;
        }
        static void SettingPrefix(MethodBase __originalMethod, ref bool __0)
        {
            if (enabled && installed && bound && settingsRules.TryGetValue(__originalMethod, out var value)) __0 = value;
        }
        static void LoadTemplates()
        {
            var loaded = new List<PerfectTemplate>();
            if (providerTemplates.GetValue(null) is not IEnumerable all) return;
            foreach (var row in all)
            {
                object Get(object o, string n) => Property(o.GetType(), n)?.GetValue(o) ?? throw new MissingMemberException(o.GetType().FullName, n);
                var groups = new List<EffectValue[]>();
                foreach (IEnumerable group in (IEnumerable)Get(row, "EffectGroups"))
                {
                    var values = new List<EffectValue>();
                    foreach (var value in group) values.Add(new EffectValue((uint)Get(value, "BoosterImplantEffect"), (float)Get(value, "EffectMaxValue")));
                    groups.Add(values.ToArray());
                }
                var conditions = new List<uint[]>();
                foreach (IEnumerable group in (IEnumerable)Get(row, "ConditionGroups")) conditions.Add(group.Cast<uint>().ToArray());
                loaded.Add(new PerfectTemplate { Id = (uint)Get(row, "BoosterImplantID"), Category = Convert.ToInt32(Get(row, "ImplantCategory")),
                    Effects = groups.ToArray(), Conditions = conditions.ToArray() });
            }
            templates.Clear(); templates.AddRange(loaded); revision++;
        }
        static void Refresh()
        {
            if (!installed || !enabled) return;
            try { TryBind(); } catch (Exception ex) { Stop(ex); throw; }
            if (!bound) { Report("等待BoosterTweaker模块"); return; }
            var settings = perfectSettings.GetValue(null);
            if (settings == null) { Report("等待普通完美设置初始化"); return; }
            foreach (var rule in settingsRules)
            {
                var setter = (MethodInfo)rule.Key;
                var getter = setter.DeclaringType.GetMethod("get_" + setter.Name.Substring(4));
                object owner = setter.IsStatic ? null : settings;
                if ((bool)getter.Invoke(owner, null) != rule.Value) setter.Invoke(owner, new object[] { rule.Value });
            }
            bool active = (bool)featureQuery.Invoke(null, new object[] { PerfectFeature, true });
            if (templates.Count == 0) LoadTemplates();
            ready = active && templates.Count > 0;
            Report("provider=True featureActive=" + active + " templates=" + templates.Count
                + " custom=False customPerfect=False perfect=True noConsume=True noConditions=True noNegative=True farmer=" + farmer);
        }
        static void TickPostfix()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextProbe) return; nextProbe = now + 2;
            try { Refresh(); if (ready && GameStateManager.CurrentStateName != eGameStateName.InLevel && !GameStateManager.IsReady) NormalizeInventory(); }
            catch (Exception ex) { ready = false; Warn("初始化延后：" + ex.GetBaseException().Message); }
        }
        static void TemplatesPostfix()
        {
            templates.Clear(); ready = false;
            try { Refresh(); } catch (Exception ex) { Warn("模板更新延后：" + ex.GetBaseException().Message); }
        }
        static bool PerfectPrefix()
        {
            if (!installed || !enabled || !bound) return true;
            try { Refresh(); if (!ready) return true; NormalizeInventory(); return false; }
            catch (Exception ex) { ready = false; Warn("库存处理失败：" + ex.GetBaseException().Message); return true; }
        }
        static string Fingerprint(BoosterImplant implant)
        {
            var effects = implant.Effects; var conditions = implant.Conditions;
            if (effects == null || conditions == null) return "invalid";
            return implant.TemplateId + ":" + (int)implant.Category + ":"
                + string.Join("/", effects.Select(x => x.Id + "=" + x.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
                + ":" + string.Join(",", conditions);
        }
        internal static bool NormalizeItem(BoosterImplant implant)
        {
            if (!enabled || !installed || !bound) return true;
            if (!ready || implant == null) return false;
            var key = (implant.Pointer, implant.InstanceId);
            string before = Fingerprint(implant);
            bool ours = normalized.TryGetValue(key, out var old) && old.After == before;
            if (ours && old.Revision == revision) return true;
            var source = ours ? old.Effects : implant.Effects?.Select(x => new EffectValue(x.Id, x.Value)).ToArray();
            var conditions = ours ? old.Conditions : implant.Conditions?.ToArray();
            if (!PerfectBoosterPolicy.TryResolve(implant.TemplateId, (int)implant.Category,
                source, conditions,
                templates, out var result, out var reason))
            { Warn("无法完美化，禁止准备：instance=" + implant.InstanceId + " template=" + implant.TemplateId + " category=" + implant.Category + " " + reason); return false; }
            var values = new Il2CppStructArray<BoosterImplant.Effect>(result.Select(x => new BoosterImplant.Effect { Id = x.Id, Value = x.Value }).ToArray());
            var none = new Il2CppStructArray<uint>(Array.Empty<uint>());
            implant.Effects = values; implant.Conditions = none;
            normalized[key] = new Snapshot { Effects = source, Conditions = conditions, After = Fingerprint(implant), Revision = revision };
            return true;
        }
        static void NormalizeInventory()
        {
            if (normalizing || PersistentInventoryManager.Current == null) return;
            normalizing = true;
            try
            {
                var live = new HashSet<(IntPtr, uint)>(); bool complete = true;
                for (int c = 0; c < 3; c++)
                {
                    var stock = PersistentInventoryManager.GetBoosterImplantInventory((BoosterImplantCategory)c);
                    if (stock == null) { complete = false; continue; }
                    foreach (var item in stock)
                    {
                        try { if (item?.Implant != null) { live.Add((item.Implant.Pointer, item.Implant.InstanceId)); NormalizeItem(item.Implant); } }
                        catch (Exception ex) { complete = false; Warn("单项库存读取延后：" + ex.GetBaseException().Message); }
                    }
                }
                if (complete) foreach (var key in normalized.Keys.Where(x => !live.Contains(x)).ToArray()) normalized.Remove(key);
            }
            finally { normalizing = false; }
        }
        static bool PreparePrefix(uint __0)
        {
            if (!enabled || !installed || !bound) return true;
            try
            {
                Refresh();
                for (int c = 0; c < 3; c++)
                {
                    var stock = PersistentInventoryManager.GetBoosterImplantInventory((BoosterImplantCategory)c);
                    if (stock != null) foreach (var item in stock)
                        if (item?.Implant?.InstanceId == __0) return NormalizeItem(item.Implant);
                }
            }
            catch (Exception ex) { Warn("准备被拒绝：" + ex.GetBaseException().Message); }
            return false;
        }
        static void Report(string state)
        { if (lastState == state) return; lastState = state; Plugin.Logger.LogInfo("[普通完美强化剂] " + state + "；先处理有效库存，再走原版三槽／人机快照，不拦CheckCondition。"); }
        static void Warn(string message)
        { float now = UnityEngine.Time.realtimeSinceStartup; if (now < nextWarning) return; nextWarning = now + 10; Plugin.Logger.LogWarning("[普通完美强化剂] " + message); }
        static void Stop(Exception ex)
        { patches?.UnpatchSelf(); installed = bound = ready = false; settingsRules.Clear(); Plugin.Logger.LogError("普通完美兼容未完整安装，整组已撤销：" + ex); }
    }
}
