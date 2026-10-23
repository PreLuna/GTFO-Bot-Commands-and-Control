using System;
using Enemies;
using Gear;
using Player;
using LevelGeneration;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static partial class FocusOrders
    {
        internal static string ControlReason(PlayerAIBot bot, EnemyAgent target)
        {
            if (!Available || !Host || bot?.Agent?.Alive != true || bot.Agent.Owner?.IsBot != true || bot.m_rootAction == null)
                return "集火接口或机器人尚未就绪";
            if (!Attackable(target)) return "集火需要已激怒、存活且可受伤的敌人；沉睡目标使用指定击杀";
            if (Owns(bot)) return "已有集火指令，请等待其结束";
            if (!ItemRegion.SameSpace(bot.Agent, target.CourseNode) || !SearchTraversal.InScope(target.CourseNode, bot.Agent.CourseNode, 1))
                return "集火目标必须位于接管机器人当前Zone同层同维度";
            return null;
        }
        internal static IControlCombatTask CreateControlled(PlayerAIBot bot, EnemyAgent target, IControlCommandPermit permit, out string reason)
        {
            reason = ControlReason(bot, target); if (reason != null) return null;
            var e = new Entry { Bot = bot, Target = target, Floor = Builder.CurrentFloor.Pointer, Agent = bot.Agent.Pointer,
                Owner = bot.Agent.Owner.Pointer, Slot = bot.Agent.PlayerSlotIndex, EnemyID = target.GetInstanceID(),
                Name = bot.Agent.PlayerName, Label = "敌人 #" + target.EnemyDataID, Controlled = true, Origin = bot.Agent.CourseNode,
                Occupied = () => ItemTaskAdapter.PausedWorkBlocks(bot), ControlIdentity = () => permit.IdentityValid };
            var task = new ControlledFocus(e, permit);
            active.Add(bot.Pointer, e);
            return task;
        }
        sealed class ControlledFocus : IControlCombatTask
        {
            readonly Entry entry;
            readonly IControlCommandPermit permit;
            readonly ControlCombatRoot root;
            readonly ControlCombatEquipment equipment = new();
            PlayerBotActionAttack.Descriptor attack;
            PlayerBotActionLook.Descriptor look;
            long token;
            bool finished;
            bool submitted;
            float nextRequest, progressAt, health;
            float zeroHealthAt = -1;
            internal ControlledFocus(Entry entry, IControlCommandPermit permit)
            { this.entry = entry; this.permit = permit; root = new(entry.Bot); progressAt = Time.time; health = entry.Target.Damage.Health; }
            public bool Finished => finished;
            BulletWeaponSynced Gun => attack?.ActionBase?.TryCast<PlayerBotActionAttack>()?.m_currentAttackOption?.ItemToUse?.TryCast<BulletWeaponSynced>();
            public string Phase => entry.Bot.Inventory?.IsEquiping == true ? "原版选枪／装备" : Gun?.IsReloading == true ? "原版换弹"
                : entry.Committed ? "集火指定敌人" : "等待原有探测／射击窗口";
            public bool Authorizes(PlayerBotActionBase.Descriptor desc)
            {
                if (finished) return false;
                if (equipment.Authorizes(entry.Bot, desc, Gun, attack)) return true;
                for (int depth = 0; desc != null && depth < 8; depth++, desc = desc.ParentActionBase?.DescBase)
                    if (desc.Pointer == attack?.Pointer || desc.Pointer == look?.Pointer) return true;
                return false;
            }
            void StopLook()
            {
                var own = look; look = null;
                if (own?.IsTerminated() == false && Identity(entry)) entry.Bot.StopAction(own);
            }
            public void Tick(float now)
            {
                if (finished) return;
                if (!Identity(entry) || !entry.Bot.Agent.Alive || entry.Target?.GetInstanceID() != entry.EnemyID) { Abort("集火会话／敌人身份已改变"); return; }
                if (!entry.Target.Alive) { End(TaskBoardState.Complete, "指定集火目标已死亡，交回微调"); return; }
                if (!Valid(entry) || !active.TryGetValue(entry.Bot.Pointer, out var current) || !ReferenceEquals(current, entry)) { Abort("目标失效、离开Zone或手动集火已解除"); return; }
                if (token == 0 && !permit.Acquire(out token)) { if (now - progressAt > 15) Abort("等待集火执行权限超时"); return; }
                if (!permit.Report(ItemTaskStage.Moving) || !BotTaskBridge.CanExecute(entry.Bot, token)) { Abort("集火让位救援或更高优先级任务"); return; }
                float actual = entry.Target.Damage.Health;
                if (!float.IsFinite(actual) || actual < 0) { Abort("敌人生命状态异常，未确认有效死亡"); return; }
                if (actual == 0) {
                    if (zeroHealthAt < 0) zeroHealthAt = now;
                    entry.Committed = false;
                    StopLook();
                    if (attack?.IsTerminated() == false) entry.Bot.StopAction(attack);
                    if (now - zeroHealthAt >= 5) Abort("生命为零但未确认实际死亡，停止重复攻击");
                    return;
                }
                zeroHealthAt = -1;
                if (actual < health - .01f) progressAt = now;
                health = actual;
                if (now - progressAt >= 60) { Abort("集火60秒无实际伤害进展；检查视线、距离或弹药"); return; }
                if (entry.Occupied?.Invoke() == true || !entry.Bot.HasLineOfFireToObject(entry.Bot.Agent.EyePosition, entry.Target.gameObject)) {
                    entry.Committed = false;
                    StopLook();
                    if (attack?.IsTerminated() == false) entry.Bot.StopAction(attack);
                    return;
                }
                bool request = attack == null || attack.IsTerminated();
                if (request) {
                    if (now < nextRequest) return; nextRequest = now + 1;
                    attack = new PlayerBotActionAttack.Descriptor(entry.Bot) { Prio = 100000f, TargetAgent = entry.Target,
                        Means = PlayerBotActionAttack.AttackMeansEnum.Bullet, MovementAllowed = false, Haste = .5f };
                    submitted = false;
                    root.Bind(attack);
                }
                // Same FocusOrders commit and existing two-gun chooser, visibility cache and reload logic.
                Commit(entry, attack);
                if (!entry.Committed) {
                    // Face the saved target through a native look action so the existing sight loop
                    // can observe it. The independent control camera keeps its saved orientation.
                    var direction = entry.Target.Position - entry.Bot.Agent.EyePosition;
                    if (direction.sqrMagnitude > .001f) {
                        if (look == null || look.IsTerminated()) {
                            look = new PlayerBotActionLook.Descriptor(entry.Bot, true) { Prio = 100000f,
                                TargetType = PlayerBotActionLook.TargetTypeEnum.Direction, TargetDir = direction.normalized, Haste = .5f };
                            if (!entry.Bot.RequestAction(look)) look = null;
                        } else look.TargetDir = direction.normalized;
                    }
                    return;
                }
                StopLook();
                if (!submitted) { submitted = true; if (!entry.Bot.RequestAction(attack)) { Abort("原版拒绝指定目标的集火动作"); return; } }
            }
            public void Cancel() => End(TaskBoardState.Cancelled, "接管退出，停止指定集火");
            public void Abort(string reason) => End(TaskBoardState.Failed, reason);
            void End(TaskBoardState state, string reason)
            {
                if (finished) return; finished = true;
                try { if (permit.IdentityValid) root.Restore(); StopLook(); if (attack?.IsTerminated() == false && Identity(entry)) entry.Bot.StopAction(attack); }
                finally { try { Clear(entry, state, reason); } finally { permit.Hold(); token = 0; } }
            }
        }
    }
}
