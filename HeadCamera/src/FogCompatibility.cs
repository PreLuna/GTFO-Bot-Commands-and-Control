using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
namespace PreLuna.HeadCamera
{
    internal static class FogCompatibility
    {
        static FieldInfo ready, master;
        static Action<ClusteredRendering> restore;
        internal static void Install(Harmony harmony)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PreLuna.FlashlightCustom");
            var type = assembly?.GetType("PreLuna.FlashlightCustom.LocalVisualFog");
            if (type == null) return;
            try {
                ready = AccessTools.Field(type, "ready"); master = AccessTools.Field(type, "masterEnabled");
                var after = AccessTools.Method(type, "AfterCamera", new[] { typeof(ClusteredRendering) });
                var local = AccessTools.Method(type, "IsLocal", new[] { typeof(PreLitVolume) });
                if (ready?.FieldType != typeof(bool) || master == null || local == null || after == null) throw new MissingMethodException("本地减雾接口不匹配");
                restore = (Action<ClusteredRendering>)after.CreateDelegate(typeof(Action<ClusteredRendering>));
                harmony.Patch(local, postfix: new HarmonyMethod(typeof(FogCompatibility), nameof(Local)));
                Plugin.Instance?.Log.LogInfo("[减雾兼容] 已接入登记的观测相机，沿用本地视觉倍率；不修改任务雾与感染");
            } catch (Exception error) { restore = null; Plugin.Instance?.Log.LogWarning("[减雾兼容] 未启用：" + error); }
        }
        static void Local(PreLitVolume __0, ref bool __result)
        {
            if (__result || restore == null || !NativeCapture.OwnsFog(__0) || ready?.GetValue(null) is not true) return;
            var config = master.GetValue(null);
            if (config != null && config.GetType().GetProperty("Value")?.GetValue(config) is false) return;
            __result = true;
        }
        internal static void Restore(ClusteredRendering renderer) { restore?.Invoke(renderer); }
    }
    // Copy the live shader values, not the unreduced FogSettings data block.
    // Per-volume compute shaders are already owned/cloned by NativeCapture;
    // LocalVisualFog restores its own shared fog-sphere buffer scope above.
    internal sealed class FogShaderState
    {
        // Native UpdateShaderParameters binds volume textures globally and only
        // refreshes them on setup/settings changes. Releasing a feed without
        // restoring these leaves the main view sampling destroyed textures.
        static readonly string[] textureNames = { "_LitVolume", "_FogVolume", "_DensityNoise", "_IndirectVolume", "_ReflectionVolume", "_LitBufferCopy" };
        static readonly string[] floatNames = { "_NearPeak", "_NearInvRange", "_LightFogTonemap", "_ReflectionIntensity" };
        static readonly string[] vectorNames = { "_LitVolumeSize", "_LitVolumeSizeRcp", "_IndirectVolumeSize", "_IndirectVolumeSizeRcp" };
        readonly Texture[] textures = new Texture[textureNames.Length];
        readonly float[] floats = new float[floatNames.Length];
        readonly Vector4[] vectors = new Vector4[vectorNames.Length];
        readonly int density = Shader.PropertyToID("_FogDensity"), cameraDensity = Shader.PropertyToID("_CameraFogDensity"),
            height = Shader.PropertyToID("_DensityHeightParams"), absorption = Shader.PropertyToID("_FogAmbientColorAbsorbtion"),
            spheres = Shader.PropertyToID("_FogSphereColorDensity");
        readonly float d, c;
        readonly Vector4 h, a;
        readonly Il2CppStructArray<Vector4> s;
        internal FogShaderState()
        {
            d = Shader.GetGlobalFloat(density); c = Shader.GetGlobalFloat(cameraDensity); h = Shader.GetGlobalVector(height); a = Shader.GetGlobalVector(absorption); s = Shader.GetGlobalVectorArray(spheres);
            for (int i = 0; i < textures.Length; i++) textures[i] = Shader.GetGlobalTexture(Shader.PropertyToID(textureNames[i]));
            for (int i = 0; i < floats.Length; i++) floats[i] = Shader.GetGlobalFloat(Shader.PropertyToID(floatNames[i]));
            for (int i = 0; i < vectors.Length; i++) vectors[i] = Shader.GetGlobalVector(Shader.PropertyToID(vectorNames[i]));
        }
        internal void Restore()
        {
            for (int i = 0; i < textures.Length; i++) Shader.SetGlobalTexture(Shader.PropertyToID(textureNames[i]), textures[i]);
            for (int i = 0; i < floats.Length; i++) Shader.SetGlobalFloat(Shader.PropertyToID(floatNames[i]), floats[i]);
            for (int i = 0; i < vectors.Length; i++) Shader.SetGlobalVector(Shader.PropertyToID(vectorNames[i]), vectors[i]);
            Shader.SetGlobalFloat(density, d); Shader.SetGlobalFloat(cameraDensity, c);
            Shader.SetGlobalVector(height, h); Shader.SetGlobalVector(absorption, a);
            if (s != null && s.Length > 0) Shader.SetGlobalVectorArray(spheres, s);
        }
    }
}
