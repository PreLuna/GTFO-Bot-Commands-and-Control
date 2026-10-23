using LevelGeneration;
using Player;
namespace PreLuna.Captain.Tasks
{
    internal static class SpecialItemAccess
    {
        public static CarryItemPickup_Core Carry(ItemInLevel item) => item?.TryCast<CarryItemPickup_Core>();
        public static bool Placed(ItemInLevel item)
        {
            var sync = item?.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>();
            if (sync != null) return sync.GetCurrentState().status == ePickupItemStatus.PlacedInLevel;
            return Carry(item)?.PickupItemStatus == ePickupItemStatus.PlacedInLevel;
        }
        public static string Reason(ItemInLevel item)
        {
            if (item?.ItemDataBlock == null || !Placed(item)) return "物品已被拿走或失效";
            var slot = item.Get_pItemData().slot;
            if (!SpecialItemPolicy.Special((int)slot)) return "不是任务／特殊槽位物品";
            var carry = Carry(item);
            if (carry != null) {
                if (carry.ObjectiveItemSolved) return "任务物品已交付／处理完成，不能再次搬运";
                if (carry.IsLinkedToMachine) return "物品已连接任务设备，本指令不会拔出或触发任务";
                if (!carry.IsInteractable) return "原版搬运交互尚未开放";
            } else if (slot == InventorySlot.InLevelCarry) return "特殊手持槽没有原版搬运组件，拒绝模拟普通拾取";
            if (carry == null && item.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>() == null) return "缺少原版拾取同步组件";
            if (!AutoPickupAdapter.Open(item)) return "箱柜尚未打开，请先开启";
            if (item.gameObject?.activeInHierarchy != true) return "物品交互尚未启用";
            return null;
        }
        public static string AmountText(ItemInLevel item) => SpecialItemPolicy.Special((int)item.Get_pItemData().slot)
            ? SpecialItemPolicy.SlotName((int)item.Get_pItemData().slot) + "·单件"
            : "余量 " + item.GetCustomData().ammo.ToString("0.##");
    }
}
