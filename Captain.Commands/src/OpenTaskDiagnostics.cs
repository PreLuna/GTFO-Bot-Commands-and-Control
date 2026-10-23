using System;
using System.Text;
using Player;
namespace PreLuna.Captain.Tasks
{
    internal static class OpenTaskDiagnostics
    {
        public static void Write(string phase, MarkedOpenTarget target, string detail)
        {
            try { Plugin.Logger.LogInfo("[开门诊断] phase=" + phase + " target=" + (target?.Label ?? "未记录")
                + " " + detail + " " + Snapshot(target)); }
            catch (Exception error) { Plugin.Logger.LogWarning("[开门诊断] snapshot-failed phase=" + phase + " " + error); }
        }
        public static string Descriptor(PlayerBotActionBase.Descriptor desc)
        {
            if (desc == null) return "null";
            string kind = desc.TryCast<PlayerBotActionUnlock.Descriptor>() != null ? "Unlock"
                : desc.TryCast<PlayerBotActionMelee.Descriptor>() != null ? "Melee"
                : desc.TryCast<PlayerBotActionUseLockMelter.Descriptor>() != null ? "UseLockMelter"
                : desc.TryCast<PlayerBotActionTravel.Descriptor>() != null ? "Travel" : desc.GetType().Name;
            var melee = desc.TryCast<PlayerBotActionMelee.Descriptor>();
            return kind + "@" + desc.Pointer + " terminated=" + desc.IsTerminated() + " parent=" + desc.ParentActionBase?.DescBase?.Pointer
                + " priority=" + desc.Prio + " requiredLayers=" + desc.RequiredLayers
                + (melee == null ? "" : " state=" + melee.State + " go=" + melee.TargetGameObject?.Pointer + " agent=" + melee.TargetAgent?.Pointer);
        }
        public static string Snapshot(MarkedOpenTarget target)
        {
            if (target == null) return "";
            var text = new StringBuilder("valid=" + target.Valid + " node=" + target.Node?.Pointer);
            if (!target.Valid) return text.ToString();
            if (target.Door != null) {
                var button = target.Button;
                text.Append(" doorStatus=" + target.Door.LastStatus + " animation=" + target.Door.InAnimation
                    + " interactionAllowed=" + target.Door.InteractionAllowed + " buttonOperating=" + target.Door.m_inButtonOperation + " button=" + button?.Pointer);
                if (button != null) text.Append(" enabled=" + button.m_enabled + " active=" + button.gameObject.activeInHierarchy
                    + " canInteract=" + button.CanInteract() + " delay=" + button.OperationDelay + " callback=" + (button.OnOperate != null));
            }
            foreach (var item in target.Locks()) text.Append(" lock=" + item.Pointer + " type=" + item.m_lockType
                + " status=" + item.Status + " locked=" + item.IsLocked() + " kind=" + MarkedOpenTarget.Kind(item)
                + " holder=" + item.m_holder?.Pointer + " selectedSide=" + (target.Box != null || OpenTargetLocks.SameButton(item, target.Button)));
            return text.ToString();
        }
    }
}
