using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
namespace PreLuna.Captain.Tasks
{
    internal static class BrokerHeartbeatPatch
    {
        static Harmony patch;
        internal static void Install(Type broker)
        {
            if (patch != null) return;
            var target = AccessTools.Method(broker, "IsOwned");
            if (target == null) throw new MissingMethodException("更好AI租约时钟接口不兼容");
            var next = new Harmony("PreLuna.Captain.Commands.LeaseClock");
            next.Patch(target, transpiler: new HarmonyMethod(typeof(BrokerHeartbeatPatch), nameof(Transpile)));
            patch = next;
            Plugin.Logger.LogInfo("[接管兼容] 已修复租约起始时钟浮点舍入；保留原15秒过期上限与控制身份核验");
        }
        static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> source)
        {
            var code = source.ToList(); int changed = 0;
            for (int i = 1; i + 1 < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldc_R4 && code[i].operand is float value && value == 15f
                    && code[i - 1].opcode == OpCodes.Ldfld && code[i - 1].operand is FieldInfo field && field.Name == "ExternalUntil"
                    && code[i + 1].opcode == OpCodes.Sub) {
                    code[i] = new CodeInstruction(code[i]) { operand = LeaseClockPolicy.LowerWindow }; changed++;
                }
            if (changed != 1) throw new InvalidOperationException("更好AI租约时钟实现已变化，未应用不匹配补丁");
            return code;
        }
        internal static void Uninstall() { patch?.UnpatchSelf(); patch = null; }
    }
}
