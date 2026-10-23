using System;
using System.Collections.Generic;
using LevelGeneration;
namespace PreLuna.Captain.Tasks
{
    internal static class OpenTargetLocks
    {
        public static IEnumerable<LG_WeakLock> All(LG_WeakResourceContainer box, LG_WeakDoor door)
        {
            if (box != null) { if (box.WeakLockComponent != null) yield return box.WeakLockComponent; yield break; }
            if (door?.WeakLocks != null) foreach (var item in door.WeakLocks) if (item != null) yield return item;
        }
        public static bool SameButton(LG_WeakLock item, LG_DoorButton button)
            => button != null && item?.m_holder?.TryCast<LG_DoorButton>()?.Pointer == button.Pointer;
        public static IEnumerable<LG_WeakLock> Selected(LG_WeakResourceContainer box, LG_WeakDoor door, LG_DoorButton button)
        {
            // Native OnWeakLockUnlocked enables its holder button only. The opposite side
            // is a separate interaction: never send our bot through the closed door to break it.
            foreach (var item in All(box, door)) if (box != null || SameButton(item, button)) yield return item;
        }
        public static string Error(LG_WeakResourceContainer box, LG_WeakDoor door, LG_DoorButton button)
        {
            if (box == null) foreach (var item in All(null, door)) {
                if (MarkedOpenTarget.Kind(item) == OpenLockKind.None) continue;
                var holder = item.m_holder?.TryCast<LG_DoorButton>();
                if (holder == null || holder.m_door?.Pointer != door.Pointer)
                    return "锁具所属门按钮无法确认，未猜测或绕过锁具";
            }
            foreach (var item in Selected(box, door, button))
                if (MarkedOpenTarget.Kind(item) == OpenLockKind.Unsupported) return "当前交互侧存在未知或不支持的锁状态";
            return null;
        }
    }
}
