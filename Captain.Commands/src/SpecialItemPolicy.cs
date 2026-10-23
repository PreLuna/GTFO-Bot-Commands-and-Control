namespace PreLuna.Captain.Tasks
{
    // Query capability is wider than ordinary stacking and container placement.
    internal static class SpecialItemPolicy
    {
        public static bool Special(int slot) => slot is 6 or 7 or 8;
        public static bool Query(int slot) => slot is 4 or 5 or 6 or 7 or 8;
        public static bool Matches(int slot, int filter) => Query(slot) && (filter == 0
            || filter == 1 && slot == 4 || filter == 2 && slot == 5
            || filter == 3 && slot == 7 || filter == 4 && slot is 6 or 8);
        public static string SlotName(int slot) => slot switch {
            4 => "资源包", 5 => "普通消耗品", 6 => "重型消耗品", 7 => "口袋任务物品", 8 => "特殊手持／任务搬运", _ => "未知槽位" };
    }
}
