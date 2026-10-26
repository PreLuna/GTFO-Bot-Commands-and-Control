using System;
using System.Reflection;
using HarmonyLib;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class BotTaskBridge
    {
        static MethodInfo acquire, report, execute, release, teleport, resume, unsafeState, autoAvailable;
        static Type kind, stage, outcome;
        static bool bound;
        public static bool Bind()
        {
            if (bound) return true;
            var api = KnownTypes.Get("PreLuna.CoopBotsCustom", "PreLuna.CoopBotsCustom.Commands.BotCommandBroker");
            if (api?.GetField("ApiVersion")?.GetRawConstantValue() is not int version || version != 1) return false;
            acquire = api.GetMethod("TryAcquire"); report = api.GetMethod("ReportStage"); execute = api.GetMethod("CanExecute");
            release = api.GetMethod("Release"); teleport = api.GetMethod("TryTeleport");
            resume = api.GetMethod("TryGetResumeDestination"); unsafeState = api.GetMethod("IsUnsafe");
            autoAvailable = api.GetMethod("CanAutoCollect");
            if (acquire == null || report == null || execute == null || release == null || teleport == null || resume == null || unsafeState == null || autoAvailable == null) return false;
            kind = acquire.GetParameters()[2].ParameterType; stage = report.GetParameters()[3].ParameterType; outcome = release.GetParameters()[3].ParameterType;
            if (!kind.IsEnum || !stage.IsEnum || !outcome.IsEnum) return false;
            BrokerHeartbeatPatch.Install(api);
            return bound = true;
        }
        internal static void Uninstall() { bound = false; BrokerHeartbeatPatch.Uninstall(); }
        public static bool Acquire(PlayerAIBot bot, out long token, bool explicitCommand = true)
        { object[] args = { bot, Plugin.Guid, Enum.ToObject(kind, explicitCommand ? 40 : 30), 0L }; bool ok = (bool)acquire.Invoke(null, args); token = (long)args[3]; return ok; }
        public static bool Report(PlayerAIBot bot, long token, ItemTaskStage value)
        {
            string name = value switch { ItemTaskStage.Interacting => "Interacting", ItemTaskStage.Verifying => "Verifying", ItemTaskStage.Paused => "Paused", ItemTaskStage.Waiting => "Queued", ItemTaskStage.Returning when resume != null => "Returning", _ => "Moving" };
            return (bool)report.Invoke(null, new object[] { bot, Plugin.Guid, token, Enum.Parse(stage, name) });
        }
        public static bool CanExecute(PlayerAIBot bot, long token) => (bool)execute.Invoke(null, new object[] { bot, Plugin.Guid, token });
        public static bool Teleport(PlayerAIBot bot, long token, Vector3 position)
            => ControlCommandAdapter.Teleport(bot, () => (bool)teleport.Invoke(null, new object[] { bot, Plugin.Guid, token, position }));
        public static bool ResumePosition(PlayerAIBot bot, long token, out Vector3 position)
        { position = default; if (resume == null) return false; object[] args = { bot, Plugin.Guid, token, position }; bool ok = (bool)resume.Invoke(null, args); position = (Vector3)args[3]; return ok; }
        public static bool Unsafe(PlayerAIBot bot)
        { try { return unsafeState == null || (bool)unsafeState.Invoke(null, new object[] { bot }); } catch { return true; } }
        public static bool AutoAvailable(PlayerAIBot bot)
            => AutoAvailable(bot, out _);
        public static bool AutoAvailable(PlayerAIBot bot, out string reason)
        {
            reason = null;
            if (autoAvailable == null) { reason = "自动拾取仲裁接口尚未绑定"; return false; }
            try
            {
                if ((bool)autoAvailable.Invoke(null, new object[] { bot })) return true;
                reason = bot?.Agent?.IsInBioscan == true ? "正在参与扫描，自动拾取不能抢占"
                    : "自动任务仲裁未放行（扫描预留、救援、原版受保护动作或其他任务；接口未提供具体拒绝项）";
            }
            catch (Exception error) { reason = "自动拾取仲裁查询失败：" + error.GetBaseException().Message; }
            return false;
        }
        public static void Release(PlayerAIBot bot, long token, ItemTaskStage result)
        { release.Invoke(null, new object[] { bot, Plugin.Guid, token, Enum.Parse(outcome, result == ItemTaskStage.Complete ? "Success" : result == ItemTaskStage.Cancelled ? "Cancelled" : "Failed") }); }
    }
}
