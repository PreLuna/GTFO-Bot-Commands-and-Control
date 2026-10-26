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
        static bool readingMouse, readingFlashlight;
        internal static bool FlashlightPressed()
        {
            readingFlashlight = true;
            try { return InputMapper.GetButtonDownKeyMouse(InputAction.Flashlight, eFocusState.FPS); }
            finally { readingFlashlight = false; }
        }
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
        static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null, string finalizer = null)
        {
            if (target == null) throw new MissingMethodException("接管必要接口不存在：" + prefix + "/" + postfix);
            Plugin.LoadStep("补丁 " + target.DeclaringType?.Name + "." + target.Name, () => harmony.Patch(target, prefix: prefix == null ? null : new HarmonyMethod(typeof(ControlHooks), prefix) { priority = Priority.First + 100 },
                postfix: postfix == null ? null : new HarmonyMethod(typeof(ControlHooks), postfix) { priority = Priority.Last },
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(ControlHooks), finalizer) { priority = Priority.Last }));
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
            Patch(harmony, AccessTools.Method(typeof(RootPlayerBotAction), "UpdateFlashlightState"), nameof(FlashlightState));
            Patch(harmony, AccessTools.Method(typeof(PlayerSync), "WantsToSetFlashlightEnabled", new[] { typeof(bool), typeof(bool) }), nameof(FlashlightRequest));
            Patch(harmony, AccessTools.Method(typeof(PlayerInventoryBase), "ReceiveSetFlashlightStatus", new[] { typeof(bool), typeof(bool) }), nameof(FlashlightReceive));
            Patch(harmony, AccessTools.Method(typeof(PlayerInventoryBase), "SetFlashlightEnabled", new[] { typeof(bool), typeof(bool), typeof(bool) }), nameof(FlashlightSet));
            Patch(harmony, AccessTools.Method(typeof(PlayerInventoryBase), "UnWieldFlashlight", Type.EmptyTypes), nameof(BeforeLightHandoff), finalizer: nameof(FinishLightHandoff));
            Patch(harmony, AccessTools.Method(typeof(PlayerInventoryBase), "OnItemEquippableFlashlightWielded"), nameof(BeforeWieldLight), nameof(AfterLightSetup));
            Patch(harmony, AccessTools.Method(typeof(PlayerInventoryBase), "PrepareHelmetFlashlight"), nameof(BeforeHelmetLight), nameof(AfterLightSetup));
            Patch(harmony, AccessTools.Method(typeof(CL_SpotLight), "UpdateData"), nameof(LightData));
            Patch(harmony, AccessTools.Method(typeof(CL_ShadowAtlas), "TempDisableLight"), postfix: nameof(AfterLightDisabled));
            Patch(harmony, AccessTools.PropertySetter(typeof(Light), "range"), nameof(LightRange));
            Patch(harmony, AccessTools.PropertySetter(typeof(Light), "spotAngle"), nameof(LightAngle));
            Patch(harmony, AccessTools.PropertySetter(typeof(Light), "intensity"), nameof(LightIntensity));
            Patch(harmony, AccessTools.Method(typeof(CullingSystem.C_Light), "SetOn"), nameof(LightOn));
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
            // Run before the game's own asset/session teardown, independent of MonoBehaviour callback order.
            Patch(harmony, AccessTools.Method(typeof(Globals.Global), "OnApplicationQuit", Type.EmptyTypes), nameof(ApplicationQuitting));
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
        { if (readingFlashlight && __0 == InputAction.Flashlight) return true;
            if (!Blocked || !ControlSafety.BlockAction((int)__0)) return true; __result = false; return false; }
        static bool Axis(InputAction __0, ref float __result)
        {
            if (readingMouse && __0 is InputAction.LookHorizontal or InputAction.LookVertical) return true;
            if (!Blocked || !ControlSafety.BlockAction((int)__0)) return true; __result = 0; return false;
        }
        static bool Inventory() => !Blocked;
        static bool FlashlightRequest(PlayerSync __instance, bool __0) => NativeLightLifetime.Allows(__instance?.m_agent) && ControlRuntime.Current?.AllowNativeLightSwitch(__instance?.m_agent, __0, 0) != false;
        static bool FlashlightReceive(PlayerInventoryBase __instance, bool __0) => NativeLightLifetime.Allows(__instance?.Owner) && ControlRuntime.Current?.AllowNativeLightSwitch(__instance?.Owner, __0, 1) != false;
        static bool FlashlightSet(PlayerInventoryBase __instance, bool __0) => NativeLightLifetime.Allows(__instance?.Owner) && ControlRuntime.Current?.AllowNativeLightSwitch(__instance?.Owner, __0, 2) != false;
        static void BeforeLightHandoff(PlayerInventoryBase __instance, out IDisposable __state) => __state = ControlRuntime.Current?.BeginNativeLightHandoff(__instance?.Owner);
        static Exception FinishLightHandoff(Exception __exception, IDisposable __state) { __state?.Dispose(); return __exception; }
        static bool BeforeWieldLight(PlayerInventoryBase __instance, ItemEquippable __0, Gear.GearPartFlashlight __1)
        {
            if (!NativeLightLifetime.Allows(__instance?.Owner)) return false;
            ControlRuntime.Current?.BeforeNativeLightSetup(__instance.Owner, __1?.m_settingsID ?? 1, __0); return true;
        }
        static bool BeforeHelmetLight(PlayerInventoryBase __instance)
        {
            if (!NativeLightLifetime.Allows(__instance?.Owner)) return false;
            ControlRuntime.Current?.BeforeNativeLightSetup(__instance.Owner, 1, null); return true;
        }
        static void AfterLightSetup(PlayerInventoryBase __instance) => ControlRuntime.Current?.AfterNativeLightSetup(__instance.Owner);
        static void AfterLightDisabled(CL_Light __0) => ControlRuntime.Current?.NativeLightRenderDisabled(__0);
        static bool LightData(CL_SpotLight __instance) => ControlRuntime.Current?.AllowNativeLightData(__instance?.UnityLight) != false;
        static void LightRange(Light __instance, ref float __0) => ControlRuntime.Current?.FilterNativeLightWrite(__instance, ref __0, 0);
        static void LightAngle(Light __instance, ref float __0) => ControlRuntime.Current?.FilterNativeLightWrite(__instance, ref __0, 1);
        static void LightIntensity(Light __instance, ref float __0) => ControlRuntime.Current?.FilterNativeLightWrite(__instance, ref __0, 2);
        static bool LightOn(CullingSystem.C_Light __instance, bool __0) => !__0 || ControlRuntime.Current?.AllowNativeLightData(__instance?.m_unityLight) != false;
        static bool FlashlightState(RootPlayerBotAction __instance) => ControlRuntime.Current?.NativeLightControls(__instance?.m_bot?.Agent) != true;
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
        static void LevelCleanup() => ControlRuntime.Current?.LevelCleanup();
        static void ApplicationQuitting() => ControlRuntime.Current?.OnApplicationQuit();
        static void StateChanging(eGameStateName __0) => ControlRuntime.Current?.StateChanging(__0);
        static void PlayerDeparting(PlayerAgent __instance) => ControlRuntime.Current?.Departing(__instance);
    }
}
