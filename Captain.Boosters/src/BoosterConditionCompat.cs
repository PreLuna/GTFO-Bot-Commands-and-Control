using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BoosterImplants;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PreLuna.Captain.Boosters
{
    // One reversible rule at the native condition evaluation boundary. Inventory
    // sources and frozen bot snapshots retain their actual condition metadata.
    internal static class BoosterConditionCompat
    {
        const string FeatureGuid = "Hikaria.BoosterTweaker.Features.CustomBooster";
        static Harmony patches;
        static PropertyInfo setting;
        static MethodInfo featureQuery;
        static bool installed, bound, bypass;
        static float nextProbe, nextWarning;
        static string lastState;
        internal static bool RawConditionsPreserved => installed && bound;
        internal static void Install()
        {
            if (installed) return;
            patches = new Harmony(Plugin.Guid + ".boosterconditions");
            try
            {
                var check = AccessTools.Method(typeof(PlayerBoosterImplantState), "CheckCondition", new[] { typeof(BoosterCondition) });
                var tick = AccessTools.Method(typeof(BoosterImplantManager), "Update", Type.EmptyTypes);
                if (check?.ReturnType != typeof(bool) || tick == null) throw new MissingMethodException("原版强化剂条件检查／管理器接口变化");
                patches.Patch(check, prefix: new HarmonyMethod(typeof(BoosterConditionCompat), nameof(ConditionPrefix)));
                patches.Patch(tick, postfix: new HarmonyMethod(typeof(BoosterConditionCompat), nameof(TickPostfix)));
                installed = true;
                // Bind managed hooks early, but do not invoke its static settings
                // or construct inventory/settings objects during plugin Load.
                TryBind();
            }
            catch (Exception ex)
            {
                patches.UnpatchSelf(); installed = bound = bypass = false;
                Plugin.Logger.LogError("强化剂条件兼容未完整安装，整组已撤销：" + ex);
            }
        }
        static void TryBind()
        {
            if (bound) return;
            var type = KnownTypes.Get("Hikaria.BoosterTweaker", "Hikaria.BoosterTweaker.Managers.BoosterImplantTemplateManager");
            if (type == null) return; // Archive modules may initialize after BepInEx plugins.
            var manager = KnownTypes.Get("TheArchive.Core", "TheArchive.Core.FeaturesAPI.FeatureManager");
            if (manager == null) return;
            var flag = type.GetProperty("DisableBoosterConditions", BindingFlags.Static | BindingFlags.Public);
            var query = manager.GetMethod("IsFeatureEnabled", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string), typeof(bool) }, null);
            var apply = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(m => m.Name == "ApplyPerfectBoosterFromTemplate" && m.GetParameters().Length == 3
                    && m.GetParameters()[0].ParameterType == typeof(BoosterImplant) && m.GetParameters()[2].ParameterType == typeof(List<uint>));
            if (flag?.PropertyType != typeof(bool) || flag.GetMethod == null || flag.SetMethod == null || query?.ReturnType != typeof(bool) || apply == null)
                throw new MissingMethodException("BoosterTweaker条件开关／模板或框架运行开关接口变化");
            // These two hooks form a transaction: the bypass remains false
            // unless restoring original condition metadata is also installed.
            patches.Patch(apply, postfix: new HarmonyMethod(typeof(BoosterConditionCompat), nameof(TemplatePostfix)));
            patches.Patch(flag.SetMethod, postfix: new HarmonyMethod(typeof(BoosterConditionCompat), nameof(SettingPostfix)));
            setting = flag; featureQuery = query; bound = true;
        }
        static void Refresh(bool force = false)
        {
            if (!installed) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!force && now < nextProbe) return;
            nextProbe = now + (bound ? .25f : 2);
            try
            {
                TryBind();
                bool active = bound && (bool)featureQuery.Invoke(null, new object[] { FeatureGuid, true });
                bool requested = bound && (bool)setting.GetValue(null);
                bypass = active && requested;
                Report("provider=" + bound + " featureActive=" + active + " DisableBoosterConditions=" + requested
                    + " runtimeBypass=" + bypass);
            }
            catch (Exception ex)
            {
                bypass = false;
                // A failed managed hook transaction must not leave a metadata
                // hook while reporting that the original behavior is intact.
                if (!bound) { patches.UnpatchSelf(); installed = false; }
                if (now >= nextWarning)
                {
                    nextWarning = now + 10;
                    Plugin.Logger.LogWarning("[强化剂条件] 状态无法确认，保持原版条件检查：" + ex.GetBaseException().Message);
                }
            }
        }
        static void Report(string state)
        {
            if (state == lastState) return; lastState = state;
            Plugin.Logger.LogInfo("[强化剂条件] " + state
                + "；统一真人与机器人原版条件判定，不删条件、不改效果、不扣库存。");
        }
        static void TickPostfix() => Refresh();
        static void SettingPostfix() => Refresh(true);
        static bool ConditionPrefix(ref bool __result)
        {
            if (!installed || !bound || !bypass) return true;
            __result = true; return false;
        }
        static void TemplatePostfix(BoosterImplant __0, List<uint> __2)
        {
            // The old ordinary-perfect branch permanently erased Conditions
            // while the setting was true; custom branches already preserve them.
            // Restore exactly the supplied template metadata for reversibility.
            if (__0 == null || __2 == null) return;
            try { __0.Conditions = new Il2CppStructArray<uint>(__2.ToArray()); }
            catch (Exception ex)
            {
                bypass = bound = installed = false; patches.UnpatchSelf();
                Plugin.Logger.LogError("[强化剂条件] 原条件元数据无法安全恢复，整组兼容已停用，保留原版流程：" + ex);
            }
        }
    }
}
