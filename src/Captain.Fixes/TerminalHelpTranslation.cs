using System;
using Localization;
namespace PreLuna.Captain
{
    internal static class TerminalHelpTranslation
    {
        static bool warned;
        public static void Apply(string command, LocalizedText help, Action<string> warning)
        {
            // LocalizedText != null invokes its native value comparison with a null
            // right operand. A managed null pattern must be used instead.
            if (!string.Equals(command, "SMARTPING", StringComparison.OrdinalIgnoreCase) || help is null) return;
            try { help.UntranslatedText = "标记当前区域内的物资；可追加物品筛选词"; }
            catch (Exception error)
            {
                // A cosmetic translation must not abort native command registration.
                if (warned) return;
                warned = true;
                warning?.Invoke("SMARTPING 帮助翻译失败，保留原帮助并继续注册：" + error.Message);
            }
        }
    }
}
