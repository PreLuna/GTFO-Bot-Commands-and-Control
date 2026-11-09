using System;
using System.Linq;
using PreLuna.Captain.Boosters;
static class PerfectBoosterPolicyTests
{
    static int checks;
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static EffectValue E(uint id, float value) => new(id, value);
    static void Main()
    {
        var current = new PerfectTemplate { Id = 42, Category = 2, Effects = new[] { new[] { E(10,1.3f), E(11,1.3f), E(34,.95f) } }, Conditions = new[] { new uint[] {5}, new uint[] {30} } };
        var legacy = new PerfectTemplate { Id = 42, Category = 2, Effects = new[] { new[] { E(10,1.4f), E(11,1.4f) } }, Conditions = new[] { new uint[] {1}, new uint[] {7} } };
        var variants = new[] { current, legacy };
        bool Run(uint id, int category, EffectValue[] e, uint[] c, out EffectValue[] result)
            => PerfectBoosterPolicy.TryResolve(id, category, e, c, variants, out result, out _);
        var source = new[] { E(34,.91f), E(11,1.2f), E(10,1.25f) };
        Check(Run(42,2,source,new uint[]{5},out var result), "原始完整效果与条件匹配");
        Check(result.Length==2 && result.All(x=>x.Value==1.3f), "去负面且拉满对应当前模板，不取历史最高");
        Check(source.Length==3 && source[0].Value==.91f, "源数组不被原地修改");
        Check(Run(42,2,source,Array.Empty<uint>(),out result), "上游清空条件后仍能按完整效果组识别");
        Check(Run(42,2,new[]{E(10,1.2f),E(11,1.2f)},new uint[]{1},out result) && result[0].Value==1.4f, "旧模板只在对应条件与完整效果组匹配时使用");
        Check(!Run(42,2,new[]{E(10,1.2f),E(11,1.2f)},new uint[]{5},out _), "不把去负面后的残缺组误认成旧高上限模板");
        Check(!Run(42,1,source,new uint[]{5},out _), "类别必须匹配");
        Check(!Run(99,2,source,new uint[]{5},out _), "未知模板拒绝");
        Check(!Run(42,2,source,new uint[]{99},out _), "未知条件组拒绝");
        Check(!Run(42,2,new[]{E(10,float.NaN)},Array.Empty<uint>(),out _), "非有限数值拒绝");
        Check(!Run(42,2,new[]{E(10,1),E(10,1)},Array.Empty<uint>(),out _), "重复效果ID拒绝");
        current.Effects[0][0]=E(10,float.PositiveInfinity);
        Check(!Run(42,2,source,new uint[]{5},out _), "非法模板上限拒绝");
        current.Effects[0]=new[]{E(10,1),E(11,.8f),E(34,.95f)};
        Check(!Run(42,2,source,new uint[]{5},out _), "无正面效果不能伪造装备");
        Console.WriteLine("PASS "+checks+" ordinary perfect template policy assertions.");
    }
}
