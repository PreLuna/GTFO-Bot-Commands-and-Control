using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]
namespace PreLuna.HeadCamera
{
    [BepInPlugin(Guid, "头戴摄像头", Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "PreLuna.HeadCamera", Version = "0.3.0";
        internal static Plugin Instance;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> ToggleKey, NextKey, PreviousKey;
        internal static ConfigEntry<int> Width, FramesPerSecond, WindowWidth;
        internal static ConfigEntry<float> FieldOfView, ViewDistance, MarginX, MarginY, EyeOffset;
        internal static ConfigEntry<WindowCorner> Corner;
        internal static ConfigEntry<bool> Diagnostics;
        private CameraRuntime runtime;
        private Harmony harmony;
        public override void Load()
        {
            Instance = this;
            ClassInjector.RegisterTypeInIl2Cpp<CameraRenderCallbacks>();
            Enabled = Config.Bind("开关", "启用", true, "仅提供本机队友视角；不控制人机，不更改任务、强化剂、位置或扫描人数。");
            ToggleKey = Config.Bind("按键", "打开关闭", KeyCode.T, "仅在关卡内正常操作人物时响应。");
            NextKey = Config.Bind("按键", "下一位队友", KeyCode.RightArrow, "小窗打开时按 → 切换到下一位队友；终端中不响应。");
            PreviousKey = Config.Bind("按键", "上一位队友", KeyCode.LeftArrow, "小窗打开时按 ← 切换到上一位队友；终端中不响应。");
            Width = Config.Bind("画面", "渲染宽度", 1280, new ConfigDescription("16:9；默认 1280×720。下次打开小窗应用，降低此值可减轻部分 GPU 开销。", new AcceptableValueRange<int>(160, 1920)));
            FramesPerSecond = Config.Bind("画面", "刷新帧率", 30, new ConfigDescription("小窗目标刷新频率；实际受主画面帧率和渲染开销限制。", new AcceptableValueRange<int>(5, 60)));
            FieldOfView = Config.Bind("画面", "视野角度", 80f, new ConfigDescription("副相机的垂直视野，不修改玩家 FOV。", new AcceptableValueRange<float>(45f, 110f)));
            ViewDistance = Config.Bind("画面", "最大绘制距离", 80f, new ConfigDescription("米；同时受原版渲染范围限制。", new AcceptableValueRange<float>(15f, 120f)));
            EyeOffset = Config.Bind("画面", "视点前移", .16f, new ConfigDescription("从头部沿观看方向前移，减少头部模型遮挡；有墙时自动缩短。", new AcceptableValueRange<float>(0f, .4f)));
            WindowWidth = Config.Bind("小窗", "显示宽度", 480, new ConfigDescription("以 1920×1080 为参考分辨率。", new AcceptableValueRange<int>(200, 960)));
            Corner = Config.Bind("小窗", "位置", WindowCorner.TopRight, "TopRight 右上；TopLeft 左上；BottomRight 右下；BottomLeft 左下。");
            MarginX = Config.Bind("小窗", "水平边距", 24f, new ConfigDescription("参考分辨率像素。", new AcceptableValueRange<float>(0f, 600f)));
            MarginY = Config.Bind("小窗", "垂直边距", 150f, new ConfigDescription("默认避开屏幕顶部提示。", new AcceptableValueRange<float>(0f, 600f)));
            Diagnostics = Config.Bind("诊断", "记录渲染耗时", false, "开启后每 10 秒汇总副相机 CPU 提交耗时；不是 GPU 帧时间。没有逐帧日志。");
            UpgradeDefaults();
            harmony = new Harmony(Guid);
            // Native clustered cleanup assumes a complete FPS prefab. Our lazy/partial
            // initialization uses explicit ownership-based GPU cleanup instead.
            harmony.Patch(AccessTools.Method(typeof(ClusteredRendering), "OnDestroy"), prefix: new HarmonyMethod(typeof(Plugin), nameof(ClusterCleanup)));
            // Cleanup before native agents/cull nodes are destroyed, never alter the native operation.
            harmony.Patch(AccessTools.Method(typeof(Player.PlayerManager), "OnLevelCleanup"), prefix: new HarmonyMethod(typeof(Plugin), nameof(LevelCleanup)));
            harmony.Patch(AccessTools.Method(typeof(FocusStateManager), "ChangeState", new[] { typeof(eFocusState), typeof(bool) }), postfix: new HarmonyMethod(typeof(Plugin), nameof(FocusChanged)));
            runtime = AddComponent<CameraRuntime>();
            Config.Save();
            Log.LogInfo("头戴摄像头 " + Version + " 已加载：" + KeyLabel(ToggleKey.Value) + " 开关，" + SwitchHint + "；渲染宽度=" + Width.Value + " 目标帧率=" + FramesPerSecond.Value + "；进入终端关闭。");
        }
        void UpgradeDefaults()
        {
            var version = Config.Bind("内部", "配置版本", 0, "用于一次性升级旧默认值；升级完成后保留用户自定义设置。");
            int oldVersion = version.Value;
            if (oldVersion >= DefaultUpgrade.CurrentVersion) return;
            int changes = 0;
            if (DefaultUpgrade.Replace(oldVersion, Width.Value, 480)) { Width.Value = 1280; changes++; }
            if (DefaultUpgrade.Replace(oldVersion, FramesPerSecond.Value, 12)) { FramesPerSecond.Value = 30; changes++; }
            if (DefaultUpgrade.Replace(oldVersion, WindowWidth.Value, 360)) { WindowWidth.Value = 480; changes++; }
            if (DefaultUpgrade.Replace(oldVersion, (int)NextKey.Value, (int)KeyCode.RightBracket)) { NextKey.Value = KeyCode.RightArrow; changes++; }
            if (DefaultUpgrade.Replace(oldVersion, (int)PreviousKey.Value, (int)KeyCode.LeftBracket)) { PreviousKey.Value = KeyCode.LeftArrow; changes++; }
            version.Value = DefaultUpgrade.CurrentVersion;
            if (changes > 0) Log.LogInfo("[头戴摄像头] 已一次性升级 " + changes + " 项旧默认配置；其他自定义值保留。");
        }
        internal static string KeyLabel(KeyCode key) => key == KeyCode.LeftArrow ? "←" : key == KeyCode.RightArrow ? "→" : key.ToString();
        internal static string SwitchHint => KeyLabel(PreviousKey.Value) + " / " + KeyLabel(NextKey.Value) + " 切换";
        static void FocusChanged()
        {
            try { CameraRuntime.Current?.ObserveFocus(); }
            catch (Exception error) { Instance?.Log.LogWarning("[头戴摄像头] 焦点切换时关闭失败：" + error); }
        }
        static void LevelCleanup() => CameraRuntime.Current?.ResetLevel();
        static bool ClusterCleanup(ClusteredRendering __instance) => !NativeCapture.OwnsCluster(__instance);
        public override bool Unload()
        {
            runtime?.Shutdown();
            if (runtime != null) UnityEngine.Object.Destroy(runtime);
            harmony?.UnpatchSelf();
            Instance = null;
            return true;
        }
    }
    public enum WindowCorner { TopRight, TopLeft, BottomRight, BottomLeft }
}

