using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using GameData;
using HarmonyLib;

[assembly: AssemblyVersion("0.5.1.0")]
[assembly: AssemblyFileVersion("0.5.4.0")]
[assembly: InternalsVisibleTo("PreLuna.Captain.Boosters")]
[assembly: InternalsVisibleTo("PreLuna.Captain.Commands")]
[assembly: InternalsVisibleTo("PreLuna.Captain.Fixes")]

namespace PreLuna.Captain
{
    [BepInPlugin(Guid, "你是队长·公共核心与中文显示", "0.5.4")]
    [BepInDependency("CHTR", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("PreLuna.CoopBotsCustom", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.fridos.smartping", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "PreLuna.Captain";
        internal static TranslationCatalog Catalog;
        internal static ConfigEntry<bool> ChineseMenus, ChineseItems;
        internal static ManualLogSource Logger;
        // Keep the established config file and saved selections across the split.
        internal static ConfigFile SharedConfig;
        private Harmony harmony;
        private static bool warned;
        private sealed class RefreshStamp { }
        private static readonly ConditionalWeakTable<object, RefreshStamp> refreshed = new();
        private struct DisplayState { public string Original, Translated; }

        public override void Load()
        {
            Logger = Log;
            KnownTypes.Warning = message => Log.LogWarning(message);
            SharedConfig = Config;
            ChineseMenus = Config.Bind("中文显示", "通讯菜单", true, "翻译 ChatterReborn 额外通讯菜单和 SMARTPING 命令说明；更改后重启游戏。语音仍使用原版录音。");
            ChineseItems = Config.Bind("中文显示", "携带物品", true, "翻译头顶和队友状态栏中的已知物品名称；更改后重启游戏。");
            Catalog = TranslationCatalog.LoadDefault();
            var overrideFile = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "中文词典.json");
            if (File.Exists(overrideFile))
            {
                try { Catalog = new TranslationCatalog(File.ReadAllText(overrideFile)); }
                catch (Exception ex) { Log.LogWarning("中文词典读取失败，使用内置词典：" + ex.Message); }
            }
            harmony = new Harmony(Guid);
            if (ChineseMenus.Value)
            {
                var manager = KnownTypes.Get("ChatterReborn", "ChatterReborn.Managers.ExtraTextDataBlockManager");
                Patch(AccessTools.Method(manager, "AddNewBlock"), nameof(MenuPrefix));
                Patch(AccessTools.Method(manager, "AddExtraTextBlocks"), nameof(ResetRefreshPrefix));
                Patch(AccessTools.Method(manager, "UpdateTexts"), nameof(RefreshTextsPrefix));
            }
            if (ChineseItems.Value)
            {
                Patch(AccessTools.Method(typeof(PlaceNavMarkerOnGO), "UpdateName", new[] { typeof(string), typeof(string) }), nameof(OverheadPrefix), nameof(OverheadPostfix));
                Patch(AccessTools.Method(typeof(PUI_TeammateStatusItem), "SetClassItem", new[] { typeof(string) }), nameof(ItemPrefix));
            }
            Config.Save();
            Log.LogInfo("你是队长 0.5.4 公共核心已加载：中文显示、共享配置、接管公开协议v1；兼容接口使用定向类型缓存。");
        }

        private void Patch(MethodInfo target, string prefix, string postfix = null)
        {
            if (target == null) { Log.LogWarning("未找到接口，跳过：" + prefix); return; }
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Plugin), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
                Log.LogInfo("中文补丁已注册：" + target.DeclaringType.FullName + "." + target.Name);
            }
            catch (Exception ex) { Log.LogError("补丁注册失败：" + prefix + " / " + ex.Message); }
        }

        private static void MenuPrefix(ref string __0) => __0 = Catalog.TranslateMenu(__0);
        // Only extraInfo is translated. The first argument is the player's nickname.
        private static void OverheadPrefix(ref string __1, out DisplayState __state)
        {
            __state = new DisplayState { Original = __1, Translated = Catalog.TranslateItems(__1) };
            __1 = __state.Translated;
        }
        private static void OverheadPostfix(PlaceNavMarkerOnGO __instance, DisplayState __state)
        {
            // If the game's display method also stores the translated argument, restore the raw cache.
            // PacksHelper compares this cache with its own original strings.
            if (__state.Translated != __state.Original && __instance.m_extraInfo == __state.Translated)
                __instance.m_extraInfo = __state.Original;
        }
        private static void ItemPrefix(ref string __0) => __0 = Catalog.TranslateItems(__0);
        private static void ResetRefreshPrefix(object __instance) => refreshed.Remove(__instance);

        private static void RefreshTextsPrefix(object __instance)
        {
            // Chatter calls UpdateTexts from an update loop, including after its own early-return guard.
            if (refreshed.TryGetValue(__instance, out _)) return;
            try
            {
                // Handles Chatter creating its blocks before this addon loaded, and language changes.
                var field = AccessTools.Field(__instance.GetType(), "m_customTextBlocks");
                if (field?.GetValue(__instance) is not IDictionary blocks) return;
                var service = Localization.Text.TextLocalizationService?.TryCast<Localization.GameDataTextLocalizationService>();
                foreach (DictionaryEntry entry in blocks)
                {
                    if (entry.Value is not TextDataBlock block) continue;
                    string original = block.English;
                    string translated = Catalog.TranslateMenu(original);
                    if (translated == original) continue;
                    block.English = translated;
                    // Update only this plugin's existing IDs, never the game's entire text catalogue.
                    if (service != null && service.m_texts.ContainsKey(block.persistentID))
                        service.m_texts[block.persistentID] = translated;
                }
                refreshed.Add(__instance, new RefreshStamp());
            }
            catch (Exception ex)
            {
                if (warned) return;
                warned = true;
                Logger.LogWarning("已有通讯文本刷新失败，保留原逻辑：" + ex.Message);
            }
        }
    }
}




