using System;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using LevelGeneration;
using Localization;

[assembly: AssemblyVersion("0.5.1.0")]
[assembly: AssemblyFileVersion("0.5.1.0")]

namespace PreLuna.Captain.Fixes
{
    [BepInPlugin("PreLuna.Captain.Fixes", "你是队长·部分文件修复", "0.5.1")]
    // BepInEx 6 treats a bare version as an exact range; compatible core updates must remain loadable.
    [BepInDependency(Plugin.Guid, ">=0.5.1 <0.6.0")]
    [BepInDependency("com.fridos.smartping", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ModuleEntry : BasePlugin
    {
        public override void Load()
        {
            SmartPingCompat.Install();
            if (Plugin.ChineseMenus.Value)
            {
                var target = AccessTools.Method(typeof(LG_ComputerTerminalCommandInterpreter), "AddCommand", new[] {
                    typeof(TERM_Command), typeof(string), typeof(LocalizedText), typeof(TERM_CommandRule)
                });
                if (target == null) Log.LogWarning("终端帮助接口不存在，跳过中文帮助；SM 兼容模块按自身日志判定。");
                else
                {
                    try
                    {
                        new Harmony(Plugin.Guid + ".terminalhelp").Patch(target,
                            prefix: new HarmonyMethod(typeof(ModuleEntry), nameof(HelpPrefix)));
                    }
                    catch (Exception error) { Log.LogError("终端帮助中文补丁注册失败：" + error.Message); }
                }
            }
            Plugin.SharedConfig.Save();
            Log.LogInfo("终端修复独立模块加载完成；包含 SM／SMARTPING 命令映射、别名和帮助翻译，本体仍为 FridosSmartPing。");
        }
        static void HelpPrefix(string __1, LocalizedText __2)
            => TerminalHelpTranslation.Apply(__1, __2, message => Plugin.Logger.LogWarning(message));
    }
}
