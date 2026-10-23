using System;
using HarmonyLib;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class CaptainMenuInput
    {
        static Harmony patches;
        static PUI_CommunicationMenu menu;
        static bool ownsSession;
        static int consumedFrame = -10, selectedFrame = -10;
        public static bool Active => GameStateManager.CurrentStateName == eGameStateName.InLevel
            && AutoPickupAdapter.CanAnswer
            && ownsSession && menu?.m_active == true;
        public static bool IsCaptainNode(uint id) => CaptainMenuTree.Owned(id);
        public static bool OwnsInput => AutoPickupAdapter.CanAnswer && (Active || Time.frameCount <= consumedFrame);
        public static bool Blocks(InputAction action) => MenuInputPolicy.Block((int)action, OwnsInput);
        public static void Begin(PUI_CommunicationMenu value)
        { menu = value; ownsSession = true; consumedFrame = Time.frameCount + 1; }
        public static void Closing(PUI_CommunicationMenu value)
        {
            if (menu?.Pointer != value.Pointer) return;
            if (ownsSession) consumedFrame = Time.frameCount + 1;
            ownsSession = false;
        }
        public static void Activated(PUI_CommunicationMenu value, bool active)
        { if (!active) Closing(value); }
        public static void Reset() { menu = null; ownsSession = false; consumedFrame = selectedFrame = -10; }
        public static void Uninstall() { patches?.UnpatchSelf(); Reset(); }
        public static void Install()
        {
            patches = new Harmony(Plugin.Guid + ".menuinput");
            try
            {
                foreach (var name in new[] { "GetButtonKeyMouse", "GetButtonDownKeyMouse", "GetButtonUpKeyMouse", "GetButtonKeyMouseGamepad", "GetButtonDownKeyMouseGamepad", "GetButtonUpKeyMouseGamepad" })
                    patches.Patch(AccessTools.Method(typeof(InputMapper), name, new[] { typeof(InputAction), typeof(eFocusState) }), prefix: new HarmonyMethod(typeof(CaptainMenuInput), nameof(Button)) { priority = Priority.First });
                foreach (var name in new[] { "GetAxisKeyMouse", "GetAxisKeyMouseGamepad" })
                    patches.Patch(AccessTools.Method(typeof(InputMapper), name, new[] { typeof(InputAction), typeof(eFocusState) }), prefix: new HarmonyMethod(typeof(CaptainMenuInput), nameof(Axis)) { priority = Priority.First });
                patches.Patch(AccessTools.Method(typeof(PUI_CommunicationMenu), "CheckSelectionInput"), prefix: new HarmonyMethod(typeof(CaptainMenuInput), nameof(Select)) { priority = Priority.First });
                // The native Update body also calls MoveNext directly (its selection helper is inlined).
                patches.Patch(AccessTools.Method(typeof(PUI_CommunicationMenu), "MoveNext", new[] { typeof(int) }), prefix: new HarmonyMethod(typeof(CaptainMenuInput), nameof(ChooseButton)) { priority = Priority.First });
                patches.Patch(AccessTools.Method(typeof(PUI_CommunicationMenu), "CheckHighlightInput"), prefix: new HarmonyMethod(typeof(CaptainMenuInput), nameof(Highlight)));
                patches.Patch(AccessTools.Method(typeof(PlayerInventoryLocal), "CheckInput"), prefix: new HarmonyMethod(typeof(CaptainMenuInput), nameof(InventoryInput)) { priority = Priority.First });
                patches.Patch(AccessTools.Method(typeof(PUI_CommunicationMenu), "SetActive", new[] { typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(CaptainMenuInput), nameof(BeforeActive)) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(typeof(CaptainMenuInput), nameof(ActiveFinalizer)));
                Plugin.Logger.LogInfo("队长菜单输入隔离已注册：1–9 仅选择菜单，屏蔽同时切枪、手电、射击、装填和物品使用；关闭后恢复。");
            }
            catch (Exception ex) { patches.UnpatchSelf(); throw new InvalidOperationException("队长菜单输入隔离安装失败", ex); }
        }
        static bool Button(InputAction __0, ref bool __result)
        { if (!Blocks(__0)) return true; __result = false; return false; }
        static bool Axis(InputAction __0, ref float __result)
        { if (!Blocks(__0)) return true; __result = 0; return false; }
        static bool InventoryInput() => !OwnsInput;
        static bool Highlight() => !Active || !IsCaptainNode(menu.m_menu?.CurrentNode?.TextId ?? 0);
        static bool BeforeActive(PUI_CommunicationMenu __instance, bool __0)
        {
            if (!__0) { Closing(__instance); ItemTaskAdapter.ReleaseMenu(__instance); return true; }
            if (!__instance.m_active) {
                if (ItemTaskAdapter.PrepareMenu(__instance)) Begin(__instance);
                else if (ItemTaskAdapter.ControlMenuRequired) {
                    ItemTaskAdapter.Feedback("接管指令菜单未准备好，请稍后重开Q；不会改用本体准星下达原版指令"); return false;
                }
            }
            return true;
        }
        static Exception ActiveFinalizer(PUI_CommunicationMenu __instance, bool __0, Exception __exception)
        {
            if (__exception == null || !__0 || menu?.Pointer != __instance.Pointer) return __exception;
            Closing(__instance); ItemTaskAdapter.ReleaseMenu(__instance);
            try { __instance.SetActive(false); } catch { __instance.m_active = false; }
            Plugin.Logger.LogWarning("队长菜单打开失败，已关闭并归还控制：" + __exception.Message);
            return __exception;
        }
        static bool Select(PUI_CommunicationMenu __instance)
        {
            if (!Active || menu.Pointer != __instance.Pointer) return true;
            // The key that opened the native tools page must not select one of its new buttons again.
            if (Time.frameCount == selectedFrame) return false;
            if (!IsCaptainNode(menu.m_menu?.CurrentNode?.TextId ?? 0)) return true;
            consumedFrame = Time.frameCount + 1;
            if (Input.GetKeyDown(KeyCode.Escape))
            { selectedFrame = Time.frameCount; __instance.SetActive(false); return false; }
            for (int index = 0; index < 9; index++)
            {
                if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + index)) && !Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + index))) continue;
                __instance.MoveNext(index);
                break;
            }
            return false;
        }
        static bool ChooseButton(PUI_CommunicationMenu __instance, int __0)
        {
            if (!Active || menu.Pointer != __instance.Pointer) return true;
            if (Time.frameCount == selectedFrame) return false;
            if (__0 < 0 || __0 > __instance.m_lastActiveButtonIndex || __0 >= (__instance.m_buttons?.Count ?? 0)) return false;
            var node = __instance.m_buttons[__0]?.Node;
            if (node == null) return false;
            selectedFrame = Time.frameCount; consumedFrame = Time.frameCount + 1;
            // Dynamic native tool buttons need their real ExecuteFunc and PlayerAgent, not child-list indices.
            var previous = __instance.m_menu.CurrentNode;
            if (!IsCaptainNode(previous?.TextId ?? 0) || !IsCaptainNode(node.TextId)) return true;
            if (node.IsLastNode)
            {
                var execute = ItemTaskAdapter.CaptureDispatch(node.TextId);
                __instance.SetActive(false); execute?.Invoke(); return false;
            }
            var target = CaptainMenuTree.Target(node);
            __instance.m_menu.MoveNext(target); __instance.TryUpdateCategoryButtons();
            if (__instance.m_lastActiveButtonIndex >= 0)
            {
                __instance.SetTitleTextFromCategory(target);
                if (!IsCaptainNode(target.TextId)) Plugin.Logger.LogInfo("[队长菜单切换] button=" + __0 + " entry=" + node.TextId + " target=" + target.TextId
                    + " script=" + target.Script + " children=" + target.m_ChildNodes?.Count + " buttons=" + (__instance.m_lastActiveButtonIndex + 1) + " active=" + __instance.m_active);
            }
            else
            {
                __instance.m_menu.MoveNext(previous); __instance.TryUpdateCategoryButtons(); __instance.SetTitleTextFromCategory(previous);
                Plugin.Logger.LogWarning("[队长菜单切换] 空页已退回：entry=" + node.TextId + " target=" + target.TextId + " children=" + target.m_ChildNodes?.Count);
                ItemTaskAdapter.Feedback("当前页面没有可用命令，已返回上一级；部署工具请确认机器人工具、状态及部署目标");
            }
            return false;
        }
    }
    public static class MenuInputPolicy
    {
        public static bool Block(int action, bool ownsInput) => ownsInput &&
            (action >= 23 && action <= 32 || action == 6 || action == 8 || action == 11 || action == 12 || action == 65 || action == 66 || action == 67);
    }
}
