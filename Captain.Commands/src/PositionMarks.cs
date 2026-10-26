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
    internal sealed record MarkedPosition(Vector3 Point, AIG_CourseNode Node, AIG_CourseNode Origin, IntPtr Floor, IntPtr Issuer, float At, Vector3 Direction = default)
    {
        public string Label => "中键位置（" + Point.x.ToString("0.0") + ", " + Point.y.ToString("0.0") + ", " + Point.z.ToString("0.0") + "）";
    }
    internal static class PositionMarks
    {
        static MarkedPosition marked;
        static int capturedFrame = -1;
        static ConfigEntry<float> lifetime;
        public static void Configure(ConfigFile config)
            => lifetime = config.Bind("行动指令", "位置标记有效秒数", 120f, "本地主机中键标记保存时长15–300秒；菜单固定打开时的坐标，提交时重新核验。保留原版Ping。");
        public static void Install(Harmony patches) => patches.Patch(AccessTools.Method(typeof(PlayerAgent), "TriggerMarkerPing"),
            prefix: new HarmonyMethod(typeof(PositionMarks), nameof(Ping)));
        static void Ping(PlayerAgent __instance, Vector3 __2)
        {
            if (BotControlService.HasActive) return;
            if (!SNet.IsMaster || !AutoPickupAdapter.CanAnswer || CaptainMenuInput.OwnsInput || !Input.GetMouseButtonDown(2)
                || __instance?.Pointer != PlayerManager.GetLocalPlayerAgent()?.Pointer) return;
            Capture(__instance, __2);
        }
        static float Lifetime => float.IsFinite(lifetime?.Value ?? 120) ? Math.Clamp(lifetime?.Value ?? 120, 15, 300) : 120;
        public static MarkedPosition Current => Reason(marked) == null ? marked : null;
        public static string Reason(MarkedPosition value)
            => Reason(value, PlayerManager.GetLocalPlayerAgent());
        internal static string Reason(MarkedPosition value, PlayerAgent source)
        {
            if (value == null) return "请先用鼠标中键标记可站立的位置";
            if (!SNet.IsMaster || source?.Owner?.Pointer != value.Issuer || Builder.CurrentFloor?.Pointer != value.Floor
                || value.Floor == IntPtr.Zero || value.Node == null) return "位置标记的玩家／关卡身份已失效";
            if (!OpenTaskPolicy.Fresh(Time.time, value.At, Lifetime)) return "位置标记已过期，请重新中键标记";
            if (!ItemRegion.SameSpace(source, value.Node) || !SearchTraversal.InScope(source.CourseNode, value.Origin, 1)
                || !SearchTraversal.InScope(value.Node, value.Origin, 1)) return "位置不在当前Zone／维度／关卡分层";
            return null;
        }
        internal static MarkedPosition ControlHere(PlayerAgent bot)
        {
            var direction = bot?.GetComponent<PlayerAIBot>()?.SyncValues?.LookDirection ?? bot?.Forward ?? default;
            return ControlAt(bot, bot?.Position ?? default, direction);
        }
        internal static MarkedPosition ControlAt(PlayerAgent bot, Vector3 desired, Vector3 direction)
        {
            if (bot?.Owner?.IsBot != true || !bot.Alive || Builder.CurrentFloor == null || bot.CourseNode == null) return null;
            if (!float.IsFinite(desired.x) || !float.IsFinite(desired.y) || !float.IsFinite(desired.z)) return null;
            if (!PlayerAIBot.SnapPositionToNav(desired, out var point, 1.5f, 2) || Vector3.Distance(point, desired) > .75f
                || !AIG_CourseNode.TryGetCourseNode(bot.DimensionIndex, point, 1, out var node) || !ItemRegion.SameSpace(bot, node)
                || !SearchTraversal.InScope(node, bot.CourseNode, 1)
                || Physics.CheckCapsule(point + Vector3.up * .35f, point + Vector3.up * 1.4f, .25f, LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore)) return null;
            direction.y = 0;
            return new(point, node, bot.CourseNode, Builder.CurrentFloor.Pointer, bot.Owner.Pointer, Time.time,
                direction.sqrMagnitude > .01f ? direction.normalized : Vector3.forward);
        }
        static void Capture(PlayerAgent source, Vector3 desired)
        {
            capturedFrame = Time.frameCount; marked = null;
            if (source?.Owner == null || source.CourseNode == null || Builder.CurrentFloor == null) return;
            if (!float.IsFinite(desired.x) || !float.IsFinite(desired.y) || !float.IsFinite(desired.z)
                || !PlayerAIBot.SnapPositionToNav(desired, out var point, 1.5f, 2)
                || Vector3.Distance(point, desired) > 1.6f || Math.Abs(point.y - desired.y) > 1.5f
                || !AIG_CourseNode.TryGetCourseNode(source.DimensionIndex, point, 1, out var node)
                || !ItemRegion.SameSpace(source, node) || !SearchTraversal.InScope(node, source.CourseNode, 1)
                || Physics.CheckCapsule(point + Vector3.up * .35f, point + Vector3.up * 1.4f, .25f, LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore)) return;
            var direction = source.TargetLookDir; direction.y = 0;
            marked = new(point, node, source.CourseNode, Builder.CurrentFloor.Pointer, source.Owner.Pointer, Time.time, direction.sqrMagnitude > .01f ? direction.normalized : Vector3.forward);
            ItemTaskAdapter.Feedback("已记录 " + marked.Label + "；Q → 行动 → 前往标记位置");
        }
        public static void InputTick()
        {
            if (BotControlService.HasActive) return;
            if (!SNet.IsMaster || !AutoPickupAdapter.CanAnswer || CaptainMenuInput.OwnsInput || !Application.isFocused
                || !InputMapper.PlayerHasFocus || FocusStateManager.CurrentState != eFocusState.FPS
                || !Input.GetMouseButtonDown(2) || capturedFrame == Time.frameCount) return;
            var source = PlayerManager.GetLocalPlayerAgent();
            if (source != null && Physics.Raycast(source.EyePosition, source.TargetLookDir, out var hit, 80,
                LayerManager.MASK_DEFAULT | LayerManager.MASK_PLAYER_INTERACT_BLOCKERS, QueryTriggerInteraction.Ignore)) Capture(source, hit.point);
            else { capturedFrame = Time.frameCount; marked = null; }
        }
        public static void Reset() { marked = null; capturedFrame = -1; }
    }
}
