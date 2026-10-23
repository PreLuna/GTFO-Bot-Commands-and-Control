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
[assembly: AssemblyFileVersion("0.5.0.0")]
namespace PreLuna.MicroMaster
{
    [BepInPlugin(Guid, "微操大师", "0.5.0")]
    [BepInDependency("PreLuna.HeadCamera", "0.3.0")]
    [BepInDependency("PreLuna.Captain", "0.5.4")]
    [BepInDependency("PreLuna.Captain.Commands", "0.5.39")]
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
        internal static ConfigEntry<bool> ThreatExit;
        internal static ConfigEntry<float> ThreatRadius, ThreatConfirmation;
        internal static ConfigEntry<int> ThreatCount;
        Harmony harmony;
        ControlRuntime runtime;
        public override void Load()
        {
            Instance = this;
            Enabled = Config.Bind("开关", "启用", true, "房主接管、稳定观察、WASD微调；中键标记机器人镜头对象，Q下达开启、拾取、击杀、集火和退出驻守指令。");
            DoubleClickWindow = Config.Bind("按键", "双击间隔", .3f, new ConfigDescription("摄像头统一区分T单击和双击，单位秒。", new AcceptableValueRange<float>(.18f, .6f)));
            EmergencyKey = Config.Bind("按键", "紧急退出", KeyCode.F8, "等待接管或已接管时有效；Esc也会退出，T为普通退出。");
            ToggleViewKey = Config.Bind("按键", "切换画面大小", KeyCode.F7, "接管时切换居中放大画面与原小窗。");
            ResetViewKey = Config.Bind("按键", "重置视角", KeyCode.Home, "接管时回正到机器人正前方，恢复默认放大布局和摄像头配置FOV。");
            WalkHaste = Config.Bind("微调", "步行速度档位", .25f, new ConfigDescription("使用原版步行Haste，不是固定米每秒；本批只支持普通地面步行。", new AcceptableValueRange<float>(.05f, .5f)));
            MouseSensitivity = Config.Bind("观察", "鼠标灵敏度", 1f, new ConfigDescription("每单位原版鼠标观察轴输入对应的角度。", new AcceptableValueRange<float>(.05f, 10f)));
            InvertMouse = Config.Bind("观察", "反转纵轴", false, "仅影响接管观察。");
            ThreatExit = Config.Bind("紧急退出", "本体附近敌群退出", true, "统计本体同维度同层附近存活且已激怒的敌人，不使用机器人视野；达到数量并持续确认时间后退出。受伤／倒地等保护始终有效。");
            ThreatRadius = Config.Bind("紧急退出", "敌群检测半径", 15f, new ConfigDescription("以本体位置为中心的三维距离，单位米；不要求视线，墙后同层近敌也计入。", new AcceptableValueRange<float>(3, 40)));
            ThreatCount = Config.Bind("紧急退出", "已激活敌人数量", 3, new ConfigDescription("达到此数量触发敌群保护；沉睡、巡逻、侦察和死亡敌人不计入。", new AcceptableValueRange<int>(1, 20)));
            ThreatConfirmation = Config.Bind("紧急退出", "敌群持续确认秒数", .25f, new ConfigDescription("每0.1秒采样；连续达到阈值才退出。申请接管时若已达阈值直接拒绝。", new AcceptableValueRange<float>(0, 2)));
            harmony = new Harmony(Guid);
            try {
                if (BotControlApi.ApiVersion != 1 || BotControlApi.CommandApiVersion != 1 || BotControlApi.StationApiVersion != 1 || HeadCameraApi.ControlInputVersion != 1
                    || HeadCameraApi.ControlViewVersion != 1 || HeadCameraApi.ControlAimVersion != 1) throw new InvalidOperationException("接管协议版本不兼容");
                ControlHooks.Install(harmony);
                runtime = AddComponent<ControlRuntime>();
                Config.Save(); Log.LogInfo("微操大师0.5.0已加载：双击T接管，WASD微调，鼠标观察，中键标记，Q下达指令／退出驻守；指令期间暂停微调；T退出，F8／Esc紧急退出；本体附近敌群、伤害与生命周期变化自动退出。");
            } catch (Exception error) {
                harmony.UnpatchSelf(); Instance = null;
                Log.LogError("接管入口未启用，已撤销输入补丁：" + error); throw;
            }
        }
        public override bool Unload()
        {
            try { (ControlRuntime.Current ?? runtime)?.Shutdown(); }
            finally { harmony?.UnpatchSelf(); Instance = null; if (runtime != null) UnityEngine.Object.Destroy(runtime); }
            return true;
        }
    }
}



