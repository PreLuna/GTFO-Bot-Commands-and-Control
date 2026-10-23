using System;
using System.Collections.Generic;
using Gear;
using Player;
namespace PreLuna.Captain.Tasks
{
    // Some existing Better AI equip/reload requests use the root as parent. Permit only the
    // selected weapon during this attack, then its exact descendants; idle weapon work stays blocked.
    internal sealed class ControlCombatEquipment
    {
        readonly HashSet<IntPtr> granted = new();
        bool GrantedChain(PlayerBotActionBase.Descriptor desc)
        {
            for (int depth = 0; desc != null && depth < 8; depth++, desc = desc.ParentActionBase?.DescBase)
                if (granted.Contains(desc.Pointer)) return true;
            return false;
        }
        internal bool AuthorizesMelee(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc, MeleeWeaponThirdPerson weapon, PlayerBotActionMelee.Descriptor melee)
        {
            if (desc == null || melee == null || melee.IsTerminated() || weapon?.Owner?.Pointer != bot?.Agent?.Pointer) return false;
            if (GrantedChain(desc)) return true;
            bool selected = desc.ParentActionBase != null && desc.ParentActionBase.Pointer == bot.m_rootAction?.ActionBase?.Pointer
                && desc.TryCast<PlayerBotActionEquipItem.Descriptor>()?.Item?.Pointer == weapon.Pointer;
            if (selected) granted.Add(desc.Pointer);
            return selected;
        }
        internal bool Authorizes(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc, BulletWeaponSynced gun, PlayerBotActionAttack.Descriptor attack)
        {
            if (desc == null || gun == null || attack == null || attack.IsTerminated() || gun.Owner?.Pointer != bot?.Agent?.Pointer) return false;
            if (GrantedChain(desc)) return true;
            if (desc.ParentActionBase?.Pointer != bot.m_rootAction?.ActionBase?.Pointer || desc.ParentActionBase == null) return false;
            bool selected = desc.TryCast<PlayerBotActionEquipItem.Descriptor>()?.Item?.Pointer == gun.Pointer
                || desc.TryCast<PlayerBotActionReloadWeapon.Descriptor>() != null && bot.Inventory?.WieldedItem?.Pointer == gun.Pointer;
            if (selected) granted.Add(desc.Pointer);
            return selected;
        }
    }
    internal sealed class ControlCombatRoot
    {
        readonly PlayerAIBot bot;
        readonly IntPtr agent, owner, floor;
        readonly int slot;
        PlayerBotActionAttack.Descriptor saved, current;
        bool captured;
        internal ControlCombatRoot(PlayerAIBot bot) { this.bot = bot; agent = bot.Agent.Pointer; owner = bot.Agent.Owner.Pointer;
            slot = bot.Agent.PlayerSlotIndex; floor = LevelGeneration.Builder.CurrentFloor.Pointer; }
        bool Identity => LevelGeneration.Builder.CurrentFloor?.Pointer == floor
            && PlayerManager.Current?.GetPlayerAgentInSlot(slot)?.Pointer == agent && bot.Agent?.Owner?.Pointer == owner;
        internal void Bind(PlayerBotActionAttack.Descriptor attack)
        {
            if (!Identity) return;
            var root = bot.m_rootAction?.ActionBase?.TryCast<RootPlayerBotAction>();
            if (root == null) return;
            if (!captured) { saved = root.m_attackAction; captured = true; }
            current = attack; root.m_attackAction = attack;
        }
        internal void Restore()
        {
            var root = Identity ? bot.m_rootAction?.ActionBase?.TryCast<RootPlayerBotAction>() : null;
            if (captured && root?.m_attackAction?.Pointer == current?.Pointer) root.m_attackAction = saved;
            current = saved = null; captured = false;
        }
    }
}
