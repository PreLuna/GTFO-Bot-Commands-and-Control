using System;
using System.Reflection;
using HarmonyLib;
using LevelGeneration;
using Localization;
namespace PreLuna.Captain
{
    internal static class SmartPingCompat
    {
        static Harmony patches;
        const string FullCommand = "smartping", ShortCommand = "sm", SmartPingOwner = "com.fridos.smartping";
        public static string Normalize(string text)
        {
            return TerminalAliasPolicy.Normalize(text);
        }
        public static void Install()
        {
            if (KnownTypes.Get("FridosSmartPing", "FridosSmartPing.SmartPing") == null) { Plugin.Logger.LogWarning("Smart Ping 兼容模块未启用：没有检测到本体"); return; }
            try
            {
                patches = new Harmony(Plugin.Guid + ".smartping");
                patches.Patch(AccessTools.Method(typeof(LG_ComputerTerminalCommandInterpreter), "SetupCommands"), postfix: new HarmonyMethod(typeof(SmartPingCompat), nameof(Registered)) { priority = Priority.Last, after = new[] { SmartPingOwner } });
                patches.Patch(AccessTools.Method(typeof(LG_ComputerTerminalCommandInterpreter), "EvaluateInput"), prefix: new HarmonyMethod(typeof(SmartPingCompat), nameof(Input)) { priority = Priority.First });
                patches.Patch(AccessTools.Method(typeof(LG_ComputerTerminalCommandInterpreter), "ReceiveCommand"), prefix: new HarmonyMethod(typeof(SmartPingCompat), nameof(Receive)) { priority = Priority.First, before = new[] { SmartPingOwner } });
                Plugin.Logger.LogInfo("SM／SMARTPING 终端兼容补丁已注册：终端建立后核对命令映射，保留原版命令");
            }
            catch (Exception error) { patches?.UnpatchSelf(); Plugin.Logger.LogError("Smart Ping 兼容模块注册失败：" + error); }
        }
        static void Registered(LG_ComputerTerminalCommandInterpreter __instance)
        {
            try
            {
                if (__instance.m_commandsPerString == null || __instance.m_commandsPerEnum == null) return;
                bool existed = FindCommand(__instance, FullCommand, out var previousKey, out var previous);
                TERM_Command command = previous;
                bool invalid = existed && (previous == TERM_Command.InvalidCommand || previous == TERM_Command.None);
                bool aliasExists = FindCommand(__instance, ShortCommand, out var aliasKey, out var aliasCommand);
                bool ownOldAlias = existed && aliasExists && aliasCommand == previous;
                if (!existed || invalid)
                {
                    if (!__instance.TryGetUniqueCommandSlot(out command)) { Plugin.Logger.LogWarning("终端无空闲自定义命令槽，未覆盖原版命令；SM 无法注册"); return; }
                    // Older SmartPing registers enum 10, which is InvalidCommand in this build.
                    // Migrate only our named mapping; never borrow a reserved native command ID.
                    if (invalid) __instance.m_commandsPerString.Remove(previousKey);
                    bool added;
                    try { added = __instance.AddCommand(command, "SMARTPING", new LocalizedText { UntranslatedText = "标记当前区域物资；SM PACK 或 SMARTPING PACK", Id = 0 }, (TERM_CommandRule)0); }
                    catch { if (invalid) __instance.m_commandsPerString[previousKey] = previous; throw; }
                    if (!added)
                    { if (invalid) __instance.m_commandsPerString[previousKey] = previous; Plugin.Logger.LogWarning("SMARTPING 注册被原版拒绝，终端保留原有命令"); return; }
                    if (invalid && __instance.m_commandsPerEnum.ContainsKey(previous) && string.Equals(__instance.m_commandsPerEnum[previous], FullCommand, StringComparison.OrdinalIgnoreCase)) __instance.m_commandsPerEnum.Remove(previous);
                    if (invalid) Plugin.Logger.LogInfo("SMARTPING 已从无效命令编号 " + (int)previous + " 迁移到自定义槽 " + (int)command);
                }
                __instance.m_commandsPerString[FullCommand] = command;
                // Direct SM works, independent of whether Tab happens to complete the longer name.
                if (aliasExists && aliasCommand != command && !ownOldAlias)
                { Plugin.Logger.LogWarning("SM 已被其他命令占用，仅保留 SMARTPING"); return; }
                // Native AddCommand and EvaluateInput use lowercase dictionary keys.
                // Keep a native-readable alias even if an older addon used uppercase.
                __instance.m_commandsPerString[ShortCommand] = command;
                if (aliasExists && aliasKey != ShortCommand && ownOldAlias) __instance.m_commandsPerString.Remove(aliasKey);
                Plugin.Logger.LogInfo("终端命令映射已确认：SM／SMARTPING，编号 " + (int)command);
            }
            catch (Exception error) { Plugin.Logger.LogWarning("终端命令映射核查失败：" + error.Message); }
        }
        static bool FindCommand(LG_ComputerTerminalCommandInterpreter terminal, string name, out string key, out TERM_Command command)
        {
            key = name; command = TERM_Command.None;
            if (terminal?.m_commandsPerString == null) return false;
            if (terminal.m_commandsPerString.ContainsKey(key)) { command = terminal.m_commandsPerString[key]; return true; }
            key = name.ToUpperInvariant();
            if (!terminal.m_commandsPerString.ContainsKey(key)) return false;
            command = terminal.m_commandsPerString[key]; return true;
        }
        static bool AliasAvailable(LG_ComputerTerminalCommandInterpreter terminal)
            => FindCommand(terminal, FullCommand, out _, out var command)
                && command != TERM_Command.InvalidCommand && command != TERM_Command.None
                && FindCommand(terminal, ShortCommand, out _, out var alias) && alias == command;
        static void Input(LG_ComputerTerminalCommandInterpreter __instance, ref string __0) => __0 = TerminalAliasPolicy.Normalize(__0, AliasAvailable(__instance));
        static void Receive(LG_ComputerTerminalCommandInterpreter __instance, ref string __1) => __1 = TerminalAliasPolicy.Normalize(__1, AliasAvailable(__instance));
    }
}
