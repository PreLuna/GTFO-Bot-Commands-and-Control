using System;
using System.IO;
using PreLuna.Captain;

static class TranslationTests
{
    static int checks;
    static void Equal(string expected, string actual)
    {
        checks++;
        if (expected != actual) throw new Exception($"翻译结果不符：期望 [{expected}]，实际 [{actual}]");
    }
    static void Main(string[] args)
    {
        var catalog = TranslationCatalog.LoadDefault();
        Equal(null, catalog.TranslateItems(null));
        Equal("", catalog.TranslateItems(""));
        Equal("未收录物品", catalog.TranslateItems("未收录物品"));
        Equal("NEW ITEM", catalog.TranslateItems("NEW ITEM"));
        Equal("AMMOPACK_123 KEYCARD_45 TOOL_REFILL_6 SMARTPING ZONE_40", catalog.TranslateItems("AMMOPACK_123 KEYCARD_45 TOOL_REFILL_6 SMARTPING ZONE_40"));
        Equal("弹药包 3/5", catalog.TranslateItems("Ammo Pack 3/5"));
        Equal("工具补充包 / 荧光棒 ×8 / 熔锁器", catalog.TranslateItems("Tool Refill Pack / Glowsticks ×8 / Lock Melter"));
        Equal("医疗包 2", catalog.TranslateItems("MEDIPACK 2"));
        Equal("弹药包 <color=#CCCCCCFF><size=140%>医疗包 3/5</size></color>", catalog.TranslateItems("Ammo Pack <color=#CCCCCCFF><size=140%>Medipack 3/5</size></color>"));
        Equal("<sprite name=\"Ammo Pack\">弹药包", catalog.TranslateItems("<sprite name=\"Ammo Pack\">Ammo Pack"));
        Equal("AmmoPackFan myGlowstick", catalog.TranslateItems("AmmoPackFan myGlowstick"));
        Equal("<broken Ammo Pack", catalog.TranslateItems("<broken Ammo Pack"));
        string rich = catalog.TranslateItems("<b>Tool Refill Pack</b> ×3");
        Equal(rich, catalog.TranslateItems(rich));
        Equal("unknown menu", catalog.TranslateMenu("unknown menu"));
        int covered = 0;
        foreach (string line in File.ReadLines(args[0]))
        {
            if (line.StartsWith("System.Void") && covered > 0) break;
            if (!line.StartsWith("TEXT: ")) continue;
            string original = line.Substring(6);
            string translated = catalog.TranslateMenu(original);
            checks++;
            if (translated == original) throw new Exception("菜单漏译：" + original);
            Equal(translated, catalog.TranslateMenu(translated));
            covered++;
        }
        if (covered != 69) throw new Exception("Chatter 菜单接口发生变化，需重新审计：" + covered);
        // Exercise the bounded cache under changing status counters.
        for (int i = 0; i < 600; i++) Equal("医疗包 " + i, catalog.TranslateItems("Medipack " + i));
        Console.WriteLine($"PASS: {checks} 项检查；69 条通讯菜单全部覆盖；标签、编号和未知内容保持原样。");
    }
}
