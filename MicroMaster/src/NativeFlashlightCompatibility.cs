using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace PreLuna.MicroMaster
{
    // Patch the optional MOD's handlers, never the frozen file or global Light API.
    // Every guard is scoped to the currently controlled bot and disappears on exit.
    internal static class NativeFlashlightCompatibility
    {
        static Harmony bridge;
        internal static void Install()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PreLuna.FlashlightCustom");
            if (assembly == null) return;
            try {
                string[] names = { "Patch_LightIntensity", "Patch_LightRange", "Patch_LightAngle", "Patch_LightInnerAngle", "Patch_LightColor", "Patch_LightType", "Patch_LightCullingMask" };
                var methods = names.Select(name => assembly.GetType("PreLuna.FlashlightCustom." + name)?.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                    .SingleOrDefault(m => m.Name is "Prefix" or "Postfix" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Light))).ToArray();
                var local = assembly.GetType("PreLuna.FlashlightCustom.LightUpdater")?.GetMethod("IsLocalItem", BindingFlags.Static | BindingFlags.NonPublic);
                var sandwich = assembly.GetType("PreLuna.FlashlightCustom.LightSandwich");
                var writers = sandwich?.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(m => m.Name is "ApplySandwich" or "ApplySandwichInternal" or "ApplyHelmetInPlace" or "SuppressRangedSiblings" or "EnsureRangedSettings")
                    .Where(m => m.ReturnType == typeof(void) && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Light)).ToArray();
                var sync = sandwich?.GetMethod("SyncEnabled", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Transform), typeof(bool) }, null);
                var behaviour = assembly.GetType("PreLuna.FlashlightCustom.Patch_BehaviourEnabled")?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
                if (writers == null || writers.Length != 7 || sync == null || methods.Any(m => m == null || m.ReturnType != typeof(void) && m.ReturnType != typeof(bool)) || local?.ReturnType != typeof(bool) || local.GetParameters().Length != 1
                    || local.GetParameters()[0].ParameterType != typeof(ItemEquippable) || behaviour?.GetParameters().FirstOrDefault()?.ParameterType != typeof(Behaviour))
                    throw new MissingMethodException("手电MOD的受控光源接口形状不匹配");
                bridge = new Harmony(Plugin.Guid + ".NativeFlashlight");
                foreach (var method in methods) bridge.Patch(method, prefix: new HarmonyMethod(typeof(NativeFlashlightCompatibility),
                    method.ReturnType == typeof(bool) ? nameof(LightPrefix) : nameof(LightPostfix)));
                bridge.Patch(local, prefix: new HarmonyMethod(typeof(NativeFlashlightCompatibility), nameof(LocalItem)));
                bridge.Patch(behaviour, prefix: new HarmonyMethod(typeof(NativeFlashlightCompatibility), nameof(Enabled)));
                foreach (var writer in writers) bridge.Patch(writer, prefix: new HarmonyMethod(typeof(NativeFlashlightCompatibility), nameof(LightPostfix)));
                bridge.Patch(sync, prefix: new HarmonyMethod(typeof(NativeFlashlightCompatibility), nameof(SyncEnabled)));
                Plugin.Instance?.Log.LogInfo("[原生接管手电] 已接入手电MOD受控目标豁免；不修改本体、其他机器人或冻结DLL");
            } catch (Exception error) {
                Uninstall(); Plugin.Instance?.Log.LogWarning("[原生接管手电] 可选手电兼容未启用：" + error);
            }
        }
        static bool Owned(Light light) => ControlRuntime.Current?.OwnsNativeLight(light) == true;
        static bool LightPostfix(Light __0) => !Owned(__0);
        static bool LightPrefix(Light __0, ref bool __result) { if (!Owned(__0)) return true; __result = true; return false; }
        static bool Enabled(Behaviour __0) => !Owned(__0?.TryCast<Light>());
        static bool SyncEnabled(Transform __0) => !Owned(__0?.GetComponent<Light>());
        static bool LocalItem(ItemEquippable __0, ref bool __result)
        {
            if (ControlRuntime.Current?.OwnsNativeLightItem(__0?.Owner) != true) return true;
            __result = false; return false;
        }
        internal static void Uninstall() { bridge?.UnpatchSelf(); bridge = null; }
    }
}
