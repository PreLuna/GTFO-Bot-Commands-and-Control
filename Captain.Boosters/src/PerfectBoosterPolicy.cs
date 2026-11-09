using System;
using System.Collections.Generic;
using System.Linq;
namespace PreLuna.Captain.Boosters
{
    internal sealed class PerfectTemplate
    {
        public uint Id;
        public int Category;
        public EffectValue[][] Effects;
        public uint[][] Conditions;
    }
    internal static class PerfectBoosterPolicy
    {
        // Match full groups before filtering. Preserve current-before-legacy order.
        internal static bool TryResolve(uint template, int category, EffectValue[] source, uint[] conditions,
            IReadOnlyList<PerfectTemplate> templates, out EffectValue[] result, out string reason)
        {
            result = null; reason = "没有匹配的完整模板／效果组";
            if (source == null || source.Length == 0 || conditions == null
                || source.Any(x => x.Id == 0 || !float.IsFinite(x.Value))
                || source.Select(x => x.Id).Distinct().Count() != source.Length)
            { reason = "效果数组无效／包含重复ID"; return false; }
            foreach (var candidate in templates)
            {
                if (candidate.Id != template || candidate.Category != category) continue;
                if (conditions.Length != 0 && !candidate.Conditions.Any(x => x.Length == conditions.Length
                    && x.OrderBy(v => v).SequenceEqual(conditions.OrderBy(v => v)))) continue;
                foreach (var group in candidate.Effects)
                {
                    if (group.Length != source.Length || !group.Select(x => x.Id).OrderBy(x => x)
                        .SequenceEqual(source.Select(x => x.Id).OrderBy(x => x))) continue;
                    if (group.Any(x => x.Id == 0 || !float.IsFinite(x.Value)) || group.Select(x => x.Id).Distinct().Count() != group.Length)
                    { reason = "模板上限无效"; return false; }
                    result = group.Where(x => x.Value > 1f).ToArray();
                    if (result.Length == 0) { reason = "模板没有正面效果"; return false; }
                    reason = "完整模板匹配"; return true;
                }
            }
            return false;
        }
    }
}
