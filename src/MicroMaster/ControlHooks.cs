using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Player;
using UnityEngine;
using PreLuna.Captain.Control;
namespace PreLuna.MicroMaster
{
    internal static class ControlHooks
    {
        static FieldInfo workAgent;
        static bool readingMouse;
        internal static void ReadMouse(out float x, out float y)
        {
            readingMouse = true;
            try {
                x = InputMapper.GetAxisKeyMouse(InputAction.LookHorizontal, eFocusState.FPS);
                y = InputMapper.GetAxisKeyMouse(InputAction.LookVertical, eFocusState.FPS);
            } finally { readingMouse = false; }
        }
        static bool Blocked => ControlRuntime.Current?.BlocksBody == true;
        static bool Owned(PlayerAgent agent) => ControlRuntime.Current?.OwnsBot(agent) == true;
        static bool CommandAction(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc) => bot?.Agent?.Owner != null && desc != null
            && BotControlApi.AllowsAction(bot.Agent.Pointer.ToInt64(), bot.Agent.Owner.Pointer.ToInt64(), desc.Pointer.ToInt64());
        static bool CommandTeleport(PlayerAgent agent) => agent?.Owner != null && BotControlApi.AllowsTeleport(agent.Pointer.ToInt64(), agent.Owner.Pointer.ToInt64());
        static bool RecallBlocked(PlayerAgent agent) => Owned(agent) || agent?.Owner != null
            && BotControlApi.RecallProtected(agent.Pointer.ToInt64(), agent.Owner.Pointer.ToInt64());
        static bool Handoff(PlayerAgent agent) => agent?.Owner != null && BotControlApi.RecallProtected(agent.Pointer.ToInt64(), agent.Owner.Pointer.ToInt64());
        static bool HandoffMovement(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc) => Handoff(bot?.Agent)
            && (desc?.TryCast<PlayerBotActionTravel.Descriptor>() != null || desc?.TryCast<PlayerBotActionFollow.Descriptor>() != null
                || desc?.TryCast<PlayerBotActionWalk.Descriptor>() != null);
        static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null)
        {
            if (target == null) throw new MissingMethodException("接管必要接口不存在：" + prefix + "/" + postfix);
            harmony.Patch(target, prefix: prefix == null ? null : new HarmonyMethod(typeof(ControlHooks), prefix) { priority = Priority.First + 100 },
                postfix: postfix == null ? null : new HarmonyMethod(typeof(ControlHooks), postfix) { priority = Priority.Last });
        }
        internal static void Install(Harmony harmony)
        {
            foreach (var name in new[] { "GetButtonKeyMouse", "GetButtonDownKeyMouse", "GetButtonUpKeyMouse", "GetButtonKeyMouseGamepad", "GetButtonDownKeyMouseGamepad", "GetButtonUpKeyMouseGamepad" })
                Patch(harmony, AccessTools.Method(typeof(InputMapper), name, new[] { typeof(InputAction), typeof(eFocusState) }), nameof(Button));
            foreach (var name in new[] { "GetAxisKeyMouse", "GetAxisKeyMouseGamepad" })
                Patch(harmony, AccessTools.Method(typeof(InputMapper), name, new[] { typeof(InputAction), typeof(eFocusState) }), nameof(Axis));
            Patch(harmony, AccessTools.Method(typeof(PlayerInventoryLocal), "CheckInput"), nameof(Inventory));
            Patch(harmony, AccessTools.Method(typeof(PlayerLocomotion), "GetInputMoveVector"), nameof(MoveInput));
            Patch(harmony, AccessTools.Method(typeof(FPSCamera), "RotationUpdate"), nameof(Look));
            Patch(harmony, AccessTools.Method(typeof(FPSCamera), "ManualMouseLookUpdate"), nameof(Look));
            Patch(harmony, AccessTools.Method(typeof(PlayerAIBot), "RequestAction"), nameof(Request));
            Patch(harmony, AccessTools.Method(typeof(PlayerAIBot), "StartAction"), nameof(Start));
            Patch(harmony, AccessTools.Method(typeof(PlayerBotActionWalk), "Update"), nameof(BeforeWalkUpdate));
            Patch(harmony, AccessTools.Method(typeof(PlayerBotActionWalk), "UpdateMovement"), nameof(WalkMovement));
            Patch(harmony, AccessTools.Method(typeof(PlayerBotActionWalk), "UpdateLookAction"), nameof(WalkLook));
            Patch(harmony, AccessTools.Method(typeof(PUI_CommunicationMenu), "ExecuteCmdCall", new[] { typeof(PlayerAgent), typeof(PlayerAgent) }), nameof(NativeCall));
            // Keep AI Update and action execution running; suppress only autonomous dispatch for this bot.
            foreach (var method in typeof(RootPlayerBotAction).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name.StartsWith("UpdateAction", StringComparison.Ordinal) && m.ReturnType == typeof(void))) Patch(harmony, method, nameof(Autonomous));
            Patch(harmony, AccessTools.Method(typeof(PlayerAgent), "TeleportTo"), nameof(Teleport));
            foreach (var method in typeof(PlayerAgent).GetMethods().Where(m => m.Name == "TryWarpTo" && m.ReturnType == typeof(bool))) Patch(harmony, method, nameof(Warp));
            foreach (var method in typeof(Dam_PlayerDamageLocal).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("Receive", StringComparison.Ordinal) && (m.Name.EndsWith("Damage", StringComparison.Ordinal) || m.Name == "ReceiveSetHealth" || m.Name == "ReceiveSetDead")))
                Patch(harmony, method, nameof(BeforeDamage), nameof(AfterDamage));
            Patch(harmony, AccessTools.Method(typeof(PlayerManager), "OnLevelCleanup"), nameof(LevelCleanup));
            Patch(harmony, AccessTools.Method(typeof(GameStateManager), "ChangeState", new[] { typeof(eGameStateName) }), nameof(StateChanging));
            foreach (var name in new[] { "OnDespawn", "OnDestroy" })
                Patch(harmony, AccessTools.Method(typeof(PlayerAgent), name), nameof(PlayerDeparting));
            var ai = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PreLuna.CoopBotsCustom");
            var behavior = ai?.GetType("PreLuna.CoopBotsCustom.Reliability.Behavior");
            var warp = behavior?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault(m => m.Name == "Warp" && m.GetParameters().Length == 3);
            workAgent = warp == null ? null : AccessTools.Field(warp.GetParameters()[0].ParameterType, "Agent");
            if (workAgent == null || workAgent.FieldType != typeof(PlayerAgent)) throw new MissingMethodException("更好 AI 传送入口与当前接管适配不兼容");
            Patch(harmony, warp, nameof(AiWarp));
        }
        static bool Button(InputAction __0, ref bool __result)
        { if (!Blocked || !ControlSafety.BlockAction((int)__0)) return true; __result = false; return false; }
        static bool Axis(InputAction __0, ref float __result)
        {
            if (readingMouse && __0 is InputAction.LookHorizontal or InputAction.LookVertical) return true;
            if (!Blocked || !ControlSafety.BlockAction((int)__0)) return true; __result = 0; return false;
        }
        static bool Inventory() => !Blocked;
        static bool MoveInput(PlayerLocomotion __instance, ref Vector3 __result)
        { if (!Blocked || __instance.m_owner?.Pointer.ToInt64() != ControlRuntime.Current.Body) return true; __result = Vector3.zero; return false; }
        static bool Look(FPSCamera __instance)
        { if (!Blocked) return true; return PlayerManager.GetLocalPlayerAgent()?.FPSCamera?.Pointer != __instance.Pointer; }
        static bool Request(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0, ref bool __result)
        { if (!HandoffMovement(__instance, __0) && (!Owned(__instance?.Agent) || CommandAction(__instance, __0) || ControlRuntime.Current?.CanWalk(__instance, __0) == true)) return true; __result = false; return false; }
        static bool Start(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0)
            => !HandoffMovement(__instance, __0) && (!Owned(__instance?.Agent) || CommandAction(__instance, __0) || ControlRuntime.Current?.CanWalk(__instance, __0) == true);
        static void BeforeWalkUpdate(PlayerBotActionWalk __instance)
            => ControlRuntime.Current?.PrepareNativeWalk(__instance);
        static bool WalkMovement(PlayerBotActionWalk __instance) => ControlRuntime.Current?.IsOwnWalk(__instance?.DescBase) != true
            || ControlRuntime.Current.CanUpdateWalk(__instance);
        static bool WalkLook(PlayerBotActionWalk __instance) => ControlRuntime.Current?.IsOwnWalk(__instance?.DescBase) != true;
        static bool NativeCall(PlayerAgent __1, ref bool __result)
        { if (!Owned(__1)) return true; __result = false; return false; }
        static bool Autonomous(RootPlayerBotAction __instance, MethodBase __originalMethod) => !Owned(__instance?.m_bot?.Agent)
            && (!Handoff(__instance?.m_bot?.Agent) || __originalMethod.Name is not ("UpdateActionFollowPlayer" or "UpdateActionIdle"));
        static bool Teleport(PlayerAgent __instance) => !RecallBlocked(__instance) || CommandTeleport(__instance);
        static bool Warp(PlayerAgent __instance, ref bool __result)
        { if (!RecallBlocked(__instance) || CommandTeleport(__instance)) return true; __result = false; return false; }
        static bool AiWarp(object __0, ref bool __result)
        { var agent = workAgent.GetValue(__0) as PlayerAgent; if (!RecallBlocked(agent) || CommandTeleport(agent)) return true; __result = false; return false; }
        static void BeforeDamage(Dam_PlayerDamageLocal __instance, out float __state) => __state = __instance.Health;
        static void AfterDamage(Dam_PlayerDamageLocal __instance, float __state) => ControlRuntime.Current?.Damage(__instance, __state);
        static void LevelCleanup() => ControlRuntime.Current?.Exit("关卡清理", false);
        static void StateChanging(eGameStateName __0) => ControlRuntime.Current?.StateChanging(__0);
        static void PlayerDeparting(PlayerAgent __instance) => ControlRuntime.Current?.Departing(__instance);
    }
}
