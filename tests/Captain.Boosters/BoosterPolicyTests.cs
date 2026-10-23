using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PreLuna.Captain.Boosters;

static class BoosterPolicyTests
{
    static int count;
    static void Check(bool value, string reason) { count++; if (!value) throw new Exception(reason); }
    static void Throws(Action action, string reason)
    {
        try { action(); } catch (ArgumentException) { count++; return; }
        throw new Exception(reason);
    }
    static BoosterChoice Choice(uint id, int category = 0, float value = 1.1f, int uses = 5)
        => new(id, 100, 100, category, uses, new[] { new EffectValue(52, value) }, new uint[] { 1 }, 8, 8);
    public static void Main()
    {
        var book = new BoosterSelection(); var c0 = Choice(10); var c1 = Choice(11, 1); var c2 = Choice(12, 2);
        var stock = new List<BoosterChoice> { c0, c1, c2 };
        Check(!book.Bind(4, 1, true) && !book.Bind(1, 0, true), "非法玩家槽位或身份不能绑定");
        Check(book.Bind(1, 101, true) && book.Bind(2, 202, true), "两个机器人可以分别绑定");
        Check(!book.Choose(1, 999, true, true, true, c0), "旧／伪造拥有者不能装备");
        Check(!book.Choose(1, 101, false, true, true, c0), "客户端不能装备");
        Check(!book.Choose(1, 101, true, false, true, c0), "真人不能走机器人装配");
        Check(!book.Choose(1, 101, true, true, false, c0), "关卡内不能装备");
        foreach (var item in stock) Check(book.Choose(1, 101, true, true, true, item), "三类原版槽位可分别装备");
        Check(book.Choose(2, 202, true, true, true, c0), "两个机器人可以引用同一来源，不消耗库存");
        Check(book.Get(1, 101, stock).All(x => x != null), "第一个机器人持有三类组合");
        Check(book.Get(2, 202, stock)[0] == c0 && book.Get(2, 202, stock)[1] == null, "第二个机器人组合独立");
        Check(book.Unequip(1, 101, true, true, true, 1), "可以卸下一类");
        Check(book.Get(1, 101, stock)[0] == c0 && book.Get(1, 101, stock)[1] == null && book.Get(1, 101, stock)[2] == c2, "卸下一类保留其他两类");
        Check(book.Resolve(1, 0, new List<BoosterChoice>()) == null, "来源消失后不能从保存的效果凭空生成");
        Check(book.Resolve(1, 0, new[] { Choice(10, 0, 2f) }) == null, "来源 ID 复用但数值变化不能静默替换");
        var rebuilt = Choice(99);
        Check(book.Resolve(1, 0, new[] { rebuilt }) == rebuilt, "自定义模式重建 ID 时唯一相同效果可恢复");
        Check(book.Resolve(1, 0, new[] { rebuilt, Choice(98) }) == null, "多个相同备选无法确定来源时留空");
        Check(book.Resolve(1, 0, new[] { Choice(10, 0, 1.1f, 3) }) != null, "剩余次数变化不篡改效果身份");
        var effects = new[] { new EffectValue(52, 1.15f) }; var conditions = new uint[] { 2 };
        var detached = new BoosterChoice(20, 100, 100, 0, 3, effects, conditions, 8, 8);
        effects[0] = new EffectValue(52, 9f); conditions[0] = 8;
        Check(detached.CopyEffects()[0].Value == 1.15f && detached.CopyConditions()[0] == 2, "选择快照不共享原库存数组");
        var copy = detached.CopyEffects(); copy[0] = new EffectValue(52, 7f);
        Check(detached.CopyEffects()[0].Value == 1.15f, "调用者不能通过返回数组改写快照");
        book.Freeze(stock); stock.Clear(); stock.Add(Choice(10, 0, 8f));
        Check(book.Get(1, 101, stock)[0].CopyEffects()[0].Value == 1.1f, "开局后库存改写不改变本局加成");
        Check(!book.Choose(1, 101, true, true, true, stock[0]) && !book.Unequip(1, 101, true, true, true, 0), "冻结期间不能经配置绕过装备限制");
        book.Bind(1, 303, false);
        Check(book.Get(1, 303, stock).All(x => x == null), "真人接替机器人不继承机器人强化剂");
        book.Bind(1, 404, true);
        Check(book.Get(1, 404, stock)[0] == c0 && book.Get(1, 404, stock)[2] == c2, "局内机器人重新加入接续本局该槽冻结效果");
        Check(book.Get(1, 101, stock).All(x => x == null), "旧网络拥有者失去读取冻结槽位权限");
        book.Bind(1, 0, false);Check(book.Get(1, 404, stock).All(x => x == null), "空槽没有旧拥有者权限");
        book.Bind(1, 505, true);Check(book.Get(1, 505, stock)[0] == c0, "空槽期间保留本局快照供机器人重加入");
        book.Freeze(stock);Check(book.Get(1, 505, stock)[0] == c0, "重复冻结不得读取被改写的主机库存");
        Check(book.Get(2, 202, stock)[0] == c0, "更换一个拥有者不影响其他机器人");
        book.Thaw(); book.Bind(1, 101, true);
        stock.Clear(); stock.AddRange(new[] { c0, c1, c2 });
        var json = JsonSerializer.Serialize(book.Export()); var restored = new BoosterSelection();
        restored.Import(JsonSerializer.Deserialize<SelectionFile>(json));
        Check(restored.Get(1, 101, stock).All(x => x == null), "磁盘记录不能自带会话权限");
        restored.Bind(1, 101, true);
        Check(restored.Resolve(1, 0, stock) == c0 && restored.Resolve(1, 1, stock) == null, "保存并重启后独立槽位选择可以恢复");
        var saved = restored.Export(); saved.Selections[0].Fingerprint = new string('A', 64);
        Check(restored.Resolve(1, 0, stock) == c0, "导出配置不共享内部可变记录");
        Throws(() => restored.Import(new SelectionFile { Version = 2 }), "不接受未知配置版本");
        Throws(() => restored.Import(new SelectionFile { Selections = new() { new SavedSelection { Slot = 1, Category = 0, Fingerprint = "坏配置" } } }), "坏指纹不接受");
        var duplicate = restored.Export(); duplicate.Selections.Add(duplicate.Selections[0]);
        Throws(() => restored.Import(duplicate), "重复槽位配置不接受");
        Check(restored.Resolve(1, 0, stock) == c0, "失败的配置导入不能破坏已有记录");
        Throws(() => Choice(1, 3), "不存在第四类槽位");
        Throws(() => Choice(1, 0, float.NaN), "拒绝 NaN 数值");
        Throws(() => Choice(1, 0, float.PositiveInfinity), "拒绝无穷大数值");
        Throws(() => Choice(1, 0, 1.1f, 0), "无剩余次数的库存项不能装备");
        Throws(() => new BoosterChoice(1, 100, 100, 0, 1, new EffectValue[9], new uint[0], 8, 8), "超过同步容量不能截断");
        Throws(() => new BoosterChoice(1, 100, 100, 0, 1, new[] { new EffectValue(52, 1.1f) }, new uint[9], 8, 8), "条件数量同样受网络容量限制");
        Check(Choice(1, 0, .8f).CopyEffects()[0].Value == .8f, "正常负面倍率保持原值，不擅自完美化");
        var legacy = new BoosterChoice(10,100,100,0,5,new[]{new EffectValue(52,1.1f)},Array.Empty<uint>(),8,8);
        var recovery = new BoosterSelection();recovery.Bind(1,111,true);recovery.Choose(1,111,true,true,true,legacy);
        Check(recovery.Resolve(1,0,new[]{c0})==null,"未启用精确元数据修复时不改原身份规则");
        recovery.RestoreLegacyConditionIdentity=true;
        Check(recovery.Resolve(1,0,new[]{c0})==c0,"同一来源与完整效果身份可恢复曾被清空的原条件");
        Check(recovery.Resolve(1,0,new[]{Choice(99)})==null,"条件恢复不猜测另一个来源");
        Check(recovery.Resolve(1,0,new[]{Choice(10,0,2f)})==null,"条件恢复不接纳改变的效果");
        Check(recovery.Resolve(1,0,new[]{c0,c0})==null,"条件恢复拒绝重复来源歧义");
        restored.ForgetBindings();
        Check(restored.Get(1, 101, stock).All(x => x == null) && !restored.IsFrozen, "会话结束清理权限与快照，保留配置记录");
        Console.WriteLine("PASS: 机器人强化剂策略 " + count + " 项检查。");
    }
}
