using System;
using BoosterImplants;
namespace PreLuna.Captain.Boosters
{
    internal enum BoosterViewStatus { Empty, Pending, Synced, Inactive, Active, Unavailable }
    internal readonly record struct BoosterSlotView(uint Id, BoosterViewStatus Status);
    internal static class BoosterViewPolicy
    {
        public static BoosterSlotView Read(PlayerBoosterImplantState state, int category, bool selected, bool unavailable)
        {
            if (unavailable) return new(0, selected ? BoosterViewStatus.Unavailable : BoosterViewStatus.Empty);
            var packets = state?.BoosterImplantDatas;
            uint id = packets != null && category < packets.Length ? packets[category]?.BoosterImplantID ?? 0 : 0;
            if (id == 0) return new(0, selected ? BoosterViewStatus.Pending : BoosterViewStatus.Empty);
            var instances = state.BoosterImplants;
            if (instances != null) foreach (var instance in instances)
                if (instance != null && instance.BoosterImplantID == id)
                    return new(id, state.IsEnabled && state.PlayerAgent != null && instance.ConditionsMet ? BoosterViewStatus.Active : BoosterViewStatus.Inactive);
            return new(id, BoosterViewStatus.Synced);
        }
        public static string Label(BoosterViewStatus status) => status switch {
            BoosterViewStatus.Empty => "空槽", BoosterViewStatus.Pending => "待同步", BoosterViewStatus.Synced => "待激活",
            BoosterViewStatus.Inactive => "未生效", BoosterViewStatus.Active => "生效", _ => "接口异常" };
        // Holder starts at its top anchor. Reserve space under the clothes card including the header.
        public static float ResolveY(float original, float holderHeight, float clothesBottom, float clothesTop, float gap)
            => original <= clothesBottom - gap || original - holderHeight >= clothesTop + gap ? original : clothesBottom - gap;
    }
}
