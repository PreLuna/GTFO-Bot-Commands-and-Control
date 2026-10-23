using System;
namespace PreLuna.Captain
{
    internal static class TerminalAliasPolicy
    {
        public static string Normalize(string text, bool aliasAvailable = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var trimmed = text.TrimStart();
            int end = 0; while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end])) end++;
            string token = trimmed.Substring(0, end);
            if (!token.Equals("SMARTPING", StringComparison.OrdinalIgnoreCase)
                && !(aliasAvailable && token.Equals("SM", StringComparison.OrdinalIgnoreCase))) return text;
            return "SMARTPING" + trimmed.Substring(end);
        }
    }
}
