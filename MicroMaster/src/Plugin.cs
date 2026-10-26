using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using PreLuna.HeadCamera;
using PreLuna.Captain.Control;
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("0.5.16.0")]
namespace PreLuna.MicroMaster
{
    [BepInDependency("PreLuna.FlashlightCustom", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(Guid, "微操大师", "0.5.16")]
    [BepInDependency("PreLuna.HeadCamera", ">=0.3.14 <0.4.0")]
    [BepInDependency("PreLuna.Captain", "0.5.4")]
    [BepInDependency("PreLuna.Captain.Commands", ">=0.5.42 <0.6.0")]
    [BepInDependency("PreLuna.CoopBotsCustom", "1.0.12")]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "PreLuna.MicroMaster";
        internal static Plugin Instance;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> DoubleClickWindow;
        internal static ConfigEntry<KeyCode> EmergencyKey;
        internal static ConfigEntry<KeyCode> ToggleViewKey, ResetViewKey;
        internal static ConfigEntry<float> WalkHaste, MouseSensitivity;
        internal static ConfigEntry<bool> InvertMouse;
        internal static ConfigEntry<bool> NativeLight;
        internal static ConfigEntry<BotFlashlightMode> LightMode;
        static void OnFeedback(string message) => ControlRuntime.Current?.CommandFeedback(message);
        Harmony harmony;
        ControlRuntime runtime;
        public override void Load()
        {
            Instance = this;
            Log.LogInfo("[启动阶段] MicroMaster 0.5.16 BEGIN pid=" + Environment.ProcessId);
            LoadStep("读取配置", () => {
            Enabled = Config.Bind("开关", "启用", true, "房主接管、稳定观察、WASD微调；中键标记机器人镜头对象，Q下达开启、拾取、击杀、集火和退出驻守指令。");
            DoubleClickWindow = Config.Bind("按键", "双击间隔", .3f, new ConfigDescription("摄像头统一区分T单击和双击，单位秒。", new AcceptableValueRange<float>(.18f, .6f)));
            EmergencyKey = Config.Bind("按键", "紧急退出", KeyCode.F8, "等待接管或已接管时有效；Esc也会退出，T为普通退出。");
            ToggleViewKey = Config.Bind("按键", "切换画面大小", KeyCode.F7, "接管时切换接近全屏的观战画面与原小窗。");
            ResetViewKey = Config.Bind("按键", "重置视角", KeyCode.Home, "接管时回正到机器人正前方，恢复默认放大布局和摄像头配置FOV。");
            WalkHaste = Config.Bind("微调", "步行速度档位", .25f, new ConfigDescription("使用原版步行Haste，不是固定米每秒；本批只支持普通地面步行。", new AcceptableValueRange<float>(.05f, .5f)));
            MouseSensitivity = Config.Bind("观察", "鼠标灵敏度", 1f, new ConfigDescription("每单位原版鼠标观察轴输入对应的角度。", new AcceptableValueRange<float>(.05f, 10f)));
            InvertMouse = Config.Bind("观察", "反转纵轴", false, "仅影响接管观察。");
            NativeLight = Config.Bind("接管手电", "原生手电常亮", true, "原生常亮总开关；false交还AI。开启后的适用范围由[人机手电]模式决定。");
            LightMode = Config.Bind("人机手电", "模式", BotFlashlightMode.AllBots, "AllBots：房主在关卡内让所有仍在关卡内的人机保持原生灯开启，退出接管仍保持；ControlledOnly：仅接管期间常亮；AI：交还原版AI。F不关闭强制常亮的灯。仅维护现有灯，不生成补光灯，不修改曝光。");
            });
            harmony = new Harmony(Guid);
            try {
                if (BotControlApi.ApiVersion != 1 || BotControlApi.CommandApiVersion != 1 || BotControlApi.StationApiVersion != 1 || HeadCameraApi.ControlInputVersion != 1
                    || HeadCameraApi.ControlViewVersion != 1 || HeadCameraApi.ControlAimVersion != 1) throw new InvalidOperationException("接管协议版本不兼容");
                LoadStep("清零前手电参数快照", () => NativeLightSettings.Install(harmony));
                LoadStep("安装接管补丁", () => ControlHooks.Install(harmony));
                LoadStep("可选原生手电兼容", NativeFlashlightCompatibility.Install);
                PreLuna.Captain.Tasks.ControlPresentation.Feedback += OnFeedback;
                LoadStep("创建接管运行器", () => runtime = AddComponent<ControlRuntime>());
                LoadStep("保存配置", Config.Save); Log.LogInfo("微操大师0.5.16已加载：双击T接管，WASD微调，鼠标观察，中键标记物体／位置，Q下达指令／退出驻守，原生手电默认全关卡所有人机常亮（可配置为仅接管／AI自动）；敌群数量、标记与AI危险预判不再限制接管；本体受伤、倒地、被抓与生命周期变化仍自动退出；T退出，F8／Esc紧急退出。");
            } catch (Exception error) {
                PreLuna.Captain.Tasks.ControlPresentation.Feedback -= OnFeedback;
                NativeFlashlightCompatibility.Uninstall(); NativeLightSettings.Clear(); harmony.UnpatchSelf(); Instance = null;
                Log.LogError("接管入口未启用，已撤销输入补丁：" + error); throw;
            }
        }
        internal static void LoadStep(string name, Action action)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Instance.Log.LogInfo("[启动阶段] MicroMaster BEGIN " + name);
            try { action(); Instance.Log.LogInfo("[启动阶段] MicroMaster END " + name + " ms=" + watch.ElapsedMilliseconds); }
            catch (Exception error) { Instance.Log.LogError("[启动阶段] MicroMaster FAIL " + name + " ms=" + watch.ElapsedMilliseconds + "\n" + error); throw; }
        }
        public override bool Unload()
        {
            PreLuna.Captain.Tasks.ControlPresentation.Feedback -= OnFeedback;
            try { (ControlRuntime.Current ?? runtime)?.Shutdown(); }
            finally { NativeFlashlightCompatibility.Uninstall(); NativeLightSettings.Clear(); harmony?.UnpatchSelf(); Instance = null; if (runtime != null) UnityEngine.Object.Destroy(runtime); }
            return true;
        }
    }
}



