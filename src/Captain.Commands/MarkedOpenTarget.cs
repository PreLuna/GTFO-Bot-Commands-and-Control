using System;
using AIGraph;
using BepInEx.Configuration;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal sealed class MarkedOpenTarget
    {
        public readonly LG_WeakResourceContainer Box;
        public readonly LG_WeakDoor Door;
        public readonly LG_DoorButton Button;
        public readonly AIG_CourseNode Node;
        public readonly IntPtr Floor, Issuer, Key;
        public readonly float MarkedAt;
        public readonly string Label;
        MarkedOpenTarget(PlayerAgent source, LG_WeakResourceContainer box, LG_WeakDoor door, LG_DoorButton button, AIG_CourseNode node)
        {
            Box = box; Door = door; Button = button; Node = node;
            Floor = Builder.CurrentFloor?.Pointer ?? IntPtr.Zero; Issuer = source.Owner.Pointer; MarkedAt = Time.time;
            Key = box != null ? box.Pointer : door.Pointer;
            Label = box != null ? (box.m_isLocker ? "储物柜" : "物资箱") + " #" + box.m_serialNumber : "普通门 #" + door.m_serialNumber;
        }
        public GameObject Root => Box != null ? Box.gameObject : Door?.gameObject;
        public bool IsOpen => Box != null ? Box.ISOpen : Door != null && (Door.LastStatus == eDoorStatus.Open || Door.LastStatus == eDoorStatus.Destroyed);
        public bool Opening => Door != null && (Door.InAnimation || Door.LastStatus == eDoorStatus.Opening);
        public Vector3 OpenPoint => Box != null ? Point(Box.m_intOpen) : Button != null ? Button.transform.position : Door.transform.position;
        static Vector3 Point(Interact_Base interact) => interact?.m_colliderToOwn != null ? interact.m_colliderToOwn.bounds.center
            : interact?.Root != null ? interact.Root.position : interact.transform.position;
        public bool Valid => Floor != IntPtr.Zero && Builder.CurrentFloor?.Pointer == Floor && Root != null && Root.activeInHierarchy && Node?.m_zone?.Layer != null;
        public string Restriction
        {
            get {
                if (!Valid) return "标记对象或区域信息已失效";
                if (Box != null) return Box.m_intOpen == null ? "容器缺少原版开启交互" : UnknownLock;
                if (Door.DoorType != eLG_DoorType.Weak || Door.IsCheckpointDoor || Door.RequiresKey()) return "只支持普通弱门；任务、钥匙与检查点门不适用";
                switch (Door.LastStatus) {
                    case eDoorStatus.Closed: case eDoorStatus.Unlocked: case eDoorStatus.Open: case eDoorStatus.Opening: case eDoorStatus.Destroyed: break;
                    case eDoorStatus.Closed_LockedWithNoKey:
                        bool knownWeakLock = false;
                        foreach (var item in OperationLocks()) if (item.m_lockType == eWeakLockType.Melee || item.m_lockType == eWeakLockType.Hackable) { knownWeakLock = true; break; }
                        if (!knownWeakLock) return "此门缺少可处理的普通弱锁";
                        break;
                    default: return "门的当前状态不允许普通开启：" + Door.LastStatus;
                }
                return Button == null && !IsOpen ? "未找到当前这一侧的原版门按钮" : UnknownLock;
            }
        }
        string UnknownLock => OpenTargetLocks.Error(Box, Door, Button);
        public static OpenLockKind Kind(LG_WeakLock item) => item == null ? OpenLockKind.None
            : OpenTaskPolicy.Lock(item.IsLocked(), (int)item.Status, (int)item.m_lockType);
        internal System.Collections.Generic.IEnumerable<LG_WeakLock> Locks() => OpenTargetLocks.All(Box, Door);
        internal System.Collections.Generic.IEnumerable<LG_WeakLock> OperationLocks() => OpenTargetLocks.Selected(Box, Door, Button);
        public LG_WeakLock NextLock { get { foreach (var item in OperationLocks()) if (Kind(item) != OpenLockKind.None) return item; return null; } }
        public int NeededMelters { get { int count = 0; foreach (var item in OperationLocks()) if (Kind(item) == OpenLockKind.Hackable) count++; return count; } }
        public bool NeedsMelee { get { foreach (var item in OperationLocks()) if (Kind(item) == OpenLockKind.Melee) return true; return false; } }
        public string LockText => NeededMelters > 0 ? "入侵锁·需熔锁器×" + NeededMelters : NeedsMelee ? "物理弱锁·近战破锁"
            : NextLock != null ? "等待熔锁完成" : IsOpen ? "已开启" : "无锁";
        public bool SameSpace(PlayerAgent agent) => Valid && ItemRegion.SameSpace(agent, Node);
        public bool Visible(Vector3 eye, Vector3 point)
        {
            var delta = point - eye;
            return delta.sqrMagnitude < .01f || !Physics.Raycast(eye, delta.normalized, out var hit, Math.Max(0, delta.magnitude - .05f),
                LayerManager.MASK_PLAYER_INTERACT_BLOCKERS | LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore)
                || hit.collider != null && (hit.collider.transform.IsChildOf(Root.transform) || hit.collider.GetComponentInParent<LG_WeakResourceContainer>()?.Pointer == Key
                    || hit.collider.GetComponentInParent<LG_WeakDoor>()?.Pointer == Key);
        }
        public static MarkedOpenTarget Resolve(PlayerAgent source, GameObject selected)
        {
            if (source?.Owner == null || selected == null) return null;
            var box = selected.GetComponentInParent<LG_WeakResourceContainer>()
                ?? selected.GetComponentInParent<LG_ResourceContainer_Storage>()?.m_core?.TryCast<LG_WeakResourceContainer>();
            var door = selected.GetComponentInParent<LG_WeakDoor>() ?? selected.GetComponentInParent<LG_DoorButton>()?.m_door?.TryCast<LG_WeakDoor>();
            var weakLock = selected.GetComponentInParent<LG_WeakLock>() ?? selected.GetComponentInParent<LG_LockMelterTarget>()?.m_weakLock;
            if (weakLock != null && box == null && door == null) {
                box = weakLock.m_holder?.TryCast<LG_WeakResourceContainer>();
                door = weakLock.m_holder?.TryCast<LG_DoorButton>()?.m_door?.TryCast<LG_WeakDoor>();
            }
            if (box == null && door == null) return null;
            LG_DoorButton button = selected.GetComponentInParent<LG_DoorButton>();
            if (button == null) button = weakLock?.m_holder?.TryCast<LG_DoorButton>();
            if (button?.m_door?.Pointer != door?.Pointer) button = null;
            if (button == null && door?.m_buttons != null) {
                float nearest = float.PositiveInfinity;
                foreach (var candidate in door.m_buttons) if (candidate != null) {
                    float distance = Vector3.Distance(source.Position, candidate.transform.position);
                    if (distance < nearest) { nearest = distance; button = candidate; }
                }
            }
            var point = box != null ? box.transform.position : button != null ? button.transform.position : door.transform.position;
            AIG_CourseNode node = box?.SpawnNode;
            if (node == null) AIG_CourseNode.TryGetCourseNode(source.DimensionIndex, point, 1, out node);
            // Wall buttons can be above the graph query height. Project a point on the marked player's side.
            if (node == null && door != null) {
                var toward = source.Position - point; toward.y = 0;
                var desired = point + (toward.sqrMagnitude > .01f ? toward.normalized : Vector3.forward);
                desired.y = source.Position.y;
                if (PlayerAIBot.SnapPositionToNav(desired, out var standing, 2, 8)
                    && Vector3.Distance(standing, desired) < 2)
                    AIG_CourseNode.TryGetCourseNode(source.DimensionIndex, standing, 1, out node);
            }
            if (door?.Gate != null && door.Gate.DimensionIndex != source.DimensionIndex) return null;
            return new(source, box, door, button, node);
        }
        // A result selection creates a fresh command target, without overwriting the player's middle-click mark.
        internal static MarkedOpenTarget FromContainer(PlayerAgent source, LG_WeakResourceContainer box, AIG_CourseNode node)
            => source?.Owner == null || box == null || node == null ? null : new(source, box, null, null, node);
    }
    internal static class OpenTargetMarks
    {
        static MarkedOpenTarget marked;
        static int capturedFrame = -1;
        static ConfigEntry<float> lifetime;
        public static void Configure(ConfigFile config)
            => lifetime = config.Bind("标记开箱开门", "标记有效秒数", 60f, "本地主机中键标记的目标保留时长，范围15–300秒。菜单保存打开时的具体对象；其他玩家标记不会覆盖它。");
        static float Lifetime => float.IsFinite(lifetime?.Value ?? 60) ? Math.Clamp(lifetime?.Value ?? 60, 15, 300) : 60;
        internal static float MarkLifetime => Lifetime;
        public static MarkedOpenTarget Current {
            get {
                var source = PlayerManager.GetLocalPlayerAgent();
                return marked != null && source?.Owner?.Pointer == marked.Issuer
                    && OpenTaskPolicy.Fresh(Time.time, marked.MarkedAt, Lifetime) ? marked : null;
            }
        }
        public static bool Fresh(MarkedOpenTarget value) => value?.Valid == true
            && PlayerManager.GetLocalPlayerAgent()?.Owner?.Pointer == value.Issuer
            && OpenTaskPolicy.Fresh(Time.time, value.MarkedAt, Lifetime);
        public static string RejectReason(MarkedOpenTarget value)
            => RejectReason(value, PlayerManager.GetLocalPlayerAgent());
        internal static string RejectReason(MarkedOpenTarget value, PlayerAgent source)
        {
            if (value == null) return marked == null ? "未记录开门目标，请先中键标记普通门／门按钮" : "原标记已过期，请重新中键标记";
            if (!value.Valid) return "标记对象或导航区域失效，请重新标记";
            if (source?.Owner?.Pointer != value.Issuer) return "目标不是当前本地主机的标记";
            if (!OpenTaskPolicy.Fresh(Time.time, value.MarkedAt, Lifetime)) return "标记已过期，请重新标记";
            if (!value.SameSpace(source)) return "标记不在玩家当前维度与关卡分层";
            return value.Restriction;
        }
        public static void Install(Harmony patches) => patches.Patch(AccessTools.Method(typeof(PlayerAgent), "TriggerMarkerPing"),
            prefix: new HarmonyMethod(typeof(OpenTargetMarks), nameof(Ping)));
        static void Ping(PlayerAgent __instance, GameObject __1)
        {
            if (BotControlService.HasActive) return;
            if (!SNet.IsMaster || !AutoPickupAdapter.CanAnswer || PlayerManager.GetLocalPlayerAgent()?.Pointer != __instance?.Pointer || CaptainMenuInput.Active) return;
            Capture(__instance, __1);
        }
        static void Capture(PlayerAgent source, GameObject selected)
        {
            try {
                var value = MarkedOpenTarget.Resolve(source, selected);
                bool duplicate = capturedFrame == Time.frameCount && marked?.Key == value?.Key;
                marked = value; capturedFrame = Time.frameCount;
                if (!duplicate && value != null) {
                    var reason = RejectReason(value);
                    OpenTaskDiagnostics.Write("mark", value, "reject=" + (reason ?? "none"));
                    ItemTaskAdapter.Feedback(reason != null ? value.Label + "：" + reason
                        : "已记录 " + value.Label + "（" + value.LockText + "）；Q → 物资 → 打开标记容器／普通门");
                }
            } catch (Exception error) { marked = null; capturedFrame = Time.frameCount; Plugin.Logger.LogWarning("开箱／开门标记识别：" + error); }
        }
        public static void InputTick()
        {
            if (BotControlService.HasActive) return;
            // Observe the default middle button as a fallback for ping mods; never consume it.
            if (!SNet.IsMaster || !AutoPickupAdapter.CanAnswer || CaptainMenuInput.OwnsInput || !Application.isFocused
                || !InputMapper.PlayerHasFocus || FocusStateManager.CurrentState != eFocusState.FPS
                || !Input.GetMouseButtonDown(2) || capturedFrame == Time.frameCount) return;
            var source = PlayerManager.GetLocalPlayerAgent();
            Capture(source, ItemTargetResolver.Ray(source, out var hit) ? hit.collider?.gameObject : null);
        }
        public static void Reset() { marked = null; capturedFrame = -1; }
    }
}
