using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameData;
using HarmonyLib;
using UnityEngine;
namespace PreLuna.MicroMaster
{
    internal readonly record struct LampParameters(float Range, float Angle, float Intensity, Color Color)
    {
        internal bool Valid => ValidNumbers(Range, Angle, Intensity) && float.IsFinite(Color.r) && float.IsFinite(Color.g)
            && float.IsFinite(Color.b) && float.IsFinite(Color.a) && Color.r >= 0 && Color.g >= 0 && Color.b >= 0
            && Math.Max(Color.r, Math.Max(Color.g, Color.b)) > 0
            && float.IsFinite(Color.r * Intensity) && float.IsFinite(Color.g * Intensity) && float.IsFinite(Color.b * Intensity);
        internal static bool ValidNumbers(float range, float angle, float intensity)
            => float.IsFinite(range) && range > 0 && float.IsFinite(range * range) && float.IsFinite(1f / (range * range))
            && float.IsFinite(angle) && angle >= 1 && angle < 180 && float.IsFinite(intensity) && intensity > 0;
        internal static LampParameters Read(Light light) => new(light.range, light.spotAngle, light.intensity, light.color);
    }
    internal static class NativeLightSettings
    {
        static readonly Dictionary<uint, (long Source, LampParameters Value)> originals = new();
        internal static void Install(Harmony harmony)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PreLuna.FlashlightCustom");
            // Intercept the managed RF handler itself: the snapshot cannot run after
            // its native GearManager prefix, regardless of other prefix priorities.
            MethodInfo target = assembly == null ? AccessTools.Method(typeof(Gear.GearManager), "LoadOfflineGearDatas")
                : assembly.GetType("PreLuna.FlashlightCustom.Patch_GearManager")?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null) throw new MissingMethodException("原生手电数据快照入口不存在");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(NativeLightSettings), nameof(Capture)) { priority = Priority.First + 200 });
        }
        internal static void Capture()
        {
            try {
                var blocks = GameDataBlockBase<FlashlightSettingsDataBlock>.Wrapper?.Blocks;
                if (blocks == null) return;
                var present = new HashSet<uint>();
                foreach (var block in blocks) {
                    if (block == null) continue;
                    uint id = block.persistentID; present.Add(id); long source = block.Pointer.ToInt64();
                    // GearManager can be called again on the same, already-mutated blocks.
                    if (originals.TryGetValue(id, out var old) && old.Source == source) continue;
                    var value = new LampParameters(block.range, block.angle, block.intensity, block.color);
                    if (value.Valid) originals[id] = (source, value);
                    else originals.Remove(id);
                }
                foreach (uint id in originals.Keys.ToArray()) if (!present.Contains(id)) originals.Remove(id);
                Plugin.Instance?.Log.LogInfo("[原生接管手电] 清零前有效参数快照=" + originals.Count + "；未写全局数据块");
            } catch (Exception error) { Plugin.Instance?.Log.LogWarning("[原生接管手电] 参数快照不可用，保留原灯组：" + error.GetBaseException().Message); }
        }
        internal static bool TryGet(uint id, out LampParameters value)
        {
            value = default;
            if (!originals.TryGetValue(id, out var entry) || !entry.Value.Valid) return false;
            value = entry.Value; return true;
        }
        internal static void Clear() => originals.Clear();
    }
}
