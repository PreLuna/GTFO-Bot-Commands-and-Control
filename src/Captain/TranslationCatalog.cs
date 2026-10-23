using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PreLuna.Captain
{
    // Only presentation strings enter this class. Item identifiers and player names are not inputs.
    public sealed class TranslationCatalog
    {
        private readonly Dictionary<string, string> menus;
        private readonly Dictionary<string, string> items;
        private readonly Regex itemPattern;
        private readonly Dictionary<string, string> cache = new(StringComparer.Ordinal);
        private readonly object cacheLock = new();

        public TranslationCatalog(string json)
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json)
                ?? throw new InvalidDataException("翻译文件为空。");
            menus = new(data["通讯菜单"], StringComparer.Ordinal);
            items = new(data["物品名称"], StringComparer.OrdinalIgnoreCase);
            if (items.Count == 0 || items.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value == null)
                || menus.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value == null))
                throw new InvalidDataException("翻译键或值无效。");
            itemPattern = new Regex(@"(?<![\p{L}\p{N}_])(?:"
                + string.Join("|", items.Keys.OrderByDescending(x => x.Length).Select(Regex.Escape))
                + @")(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static TranslationCatalog LoadDefault()
        {
            using var stream = typeof(TranslationCatalog).Assembly.GetManifestResourceStream("PreLuna.Captain.zh-CN.json")
                ?? throw new InvalidDataException("内置中文词典缺失。");
            using var reader = new StreamReader(stream);
            return new TranslationCatalog(reader.ReadToEnd());
        }

        public string TranslateMenu(string value)
            => value != null && menus.TryGetValue(value, out var translated) ? translated : value;

        public string TranslateItems(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            lock (cacheLock)
            {
                if (cache.TryGetValue(value, out var previous)) return previous;
                // TMP tags must stay byte-for-byte intact, including attributes and sprite names.
                var result = new StringBuilder(value.Length);
                int cursor = 0;
                while (cursor < value.Length)
                {
                    int open = value.IndexOf('<', cursor);
                    if (open < 0) { AppendTranslated(result, value.Substring(cursor)); break; }
                    AppendTranslated(result, value.Substring(cursor, open - cursor));
                    int close = value.IndexOf('>', open + 1);
                    if (close < 0) { result.Append(value.Substring(open)); break; }
                    result.Append(value, open, close - open + 1);
                    cursor = close + 1;
                }
                var translated = result.ToString();
                // Health/ammunition counters change often; bound the presentation cache.
                if (cache.Count >= 256) cache.Clear();
                cache[value] = translated;
                return translated;
            }
        }

        private void AppendTranslated(StringBuilder result, string text)
            => result.Append(itemPattern.Replace(text, match => items[match.Value]));
    }
}
