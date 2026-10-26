using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Enemies;
using Gear;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class SingleKillAdapter
    {
        sealed class Entry { public Session Session; public SingleKillRunner Runner; public float Deadline, NextProbe; public string LastWait; public bool Controlled; }
        static readonly Dictionary<IntPtr, Entry> active = new();
        static readonly List<Entry> snapshot = new(3);
        sealed record ReturnEntry(ReturnSession Session, BorrowReturnRunner Runner);
        static readonly Dictionary<IntPtr, ReturnEntry> returning = new();
        static readonly List<ReturnEntry> returnSnapshot = new(3);
        sealed record Candidate(MeleeWeaponThirdPerson Weapon, Transform Aim, Vector3 Position, float Health, float Damage);
        static ConfigEntry<float> maxDistance, timeout;
        public static int Count => active.Count + returning.Count;
        public static void Configure(ConfigFile config)
        {
            maxDistance = config.Bind("指定击杀", "最大距离", 30f, "玩家识别目标的最大距离，范围3–60米；借调机器人须位于当前Zone同层同维度且通路已打开。传送沿用物品任务的开关和距离。");
            timeout = config.Bind("指定击杀", "准备超时", 30f, "分别限制借调收尾／权限等待及首轮接近蓄力，范围5–60秒。首轮未击杀则按实际血量持续补击／枪械接战；连续60秒无伤害进展时诊断失败。原任务保留，战斗或救援期间不强制返程。");
        }
        static float Limit(ConfigEntry<float> value, float fallback, float min, float max)
            => float.IsFinite(value?.Value ?? fallback) ? Math.Clamp(value?.Value ?? fallback, min, max) : fallback;
        public static void Install(Harmony patches)
        {
            KillMeleeAdapter.Install(patches);
            KillCombatBridge.Install(patches);
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction"), prefix: new HarmonyMethod(typeof(SingleKillAdapter), nameof(Request)));
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "StartAction"), prefix: new HarmonyMethod(typeof(SingleKillAdapter), nameof(Start)));
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "WantsCrouch"), postfix: new HarmonyMethod(typeof(SingleKillAdapter), nameof(Crouch)) { priority = Priority.Last });
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "IsFlashlightAllowed"), postfix: new HarmonyMethod(typeof(SingleKillAdapter), nameof(Flashlight)));
        }
        public static bool Sleeping(EnemyAgent enemy) => enemy != null && enemy.Alive && enemy.AI != null
            && (int)enemy.AI.Mode == 4 && enemy.CourseNode != null && enemy.Damage != null && !enemy.Damage.IsImortal;
        public static EnemyAgent Target(PlayerAgent source)
        {
            if (source == null || !Physics.Raycast(source.EyePosition, source.TargetLookDir, out var hit,
                Limit(maxDistance, 30, 3, 60), LayerManager.MASK_ENEMY_DAMAGABLE | LayerManager.MASK_ENEMY | LayerManager.MASK_DEFAULT
                    | LayerManager.MASK_PLAYER_INTERACT_BLOCKERS, QueryTriggerInteraction.Collide) || hit.collider == null) return null;
            var enemy = hit.collider.GetComponentInParent<EnemyAgent>() ?? hit.collider.GetComponentInParent<Dam_EnemyDamageLimb>()?.m_base?.Owner;
            return enemy?.Alive == true && enemy.DimensionIndex == source.DimensionIndex ? enemy : null;
        }
        internal static EnemyAgent Resolve(PlayerAgent source, RaycastHit hit)
        {
            var enemy = hit.collider?.GetComponentInParent<EnemyAgent>() ?? hit.collider?.GetComponentInParent<Dam_EnemyDamageLimb>()?.m_base?.Owner;
            return enemy?.Alive == true && enemy.DimensionIndex == source.DimensionIndex ? enemy : null;
        }
        internal static string ControlReason(PlayerAIBot bot, EnemyAgent enemy, out string note)
        {
            note = null;
            try { var reason = Check(bot, enemy, out var plan, sourceOverride: bot?.Agent);
                if (reason == null) note = "首击预估 " + plan.Damage.ToString("F1") + "；存活则沿用补击／两把枪评分";
                return reason;
            } catch (Exception error) { return "击杀资格读取失败：" + error.GetBaseException().Message; }
        }
        public static bool Owns(PlayerAIBot bot) => bot != null && (active.ContainsKey(bot.Pointer)
            || returning.TryGetValue(bot.Pointer, out var back) && back.Session.HasControl);
        public static string Status(PlayerAIBot bot) => bot != null && active.TryGetValue(bot.Pointer, out var entry)
            ? "指定击杀·" + (entry.Runner == null ? "借调收尾／等待权限" : Text(entry.Runner.Stage))
            : bot != null && returning.ContainsKey(bot.Pointer) ? "击杀借调·等待安全返程／恢复" : null;
        static string Text(SingleKillStage stage) => stage switch {
            SingleKillStage.Approaching => "接近背后", SingleKillStage.Charging => "蓄力", SingleKillStage.Striking => "挥击核验",
            SingleKillStage.Continuing => "持续接战／补击",
            SingleKillStage.Complete => "完成", SingleKillStage.Cancelled => "取消", _ => "失败" };
        public static void Cancel(PlayerAIBot bot)
        {
            if (bot != null && active.TryGetValue(bot.Pointer, out var entry)) {
                if (entry.Runner != null) entry.Runner.Cancel(); else entry.Session.Finish(SingleKillStage.Cancelled, "借调准备已取消");
            }
            else if (bot != null && returning.TryGetValue(bot.Pointer, out var back)) back.Runner.Abort(true, "玩家取消返程，保留实际位置并交还原任务");
        }
        // A released strike must finish. Once it has yielded, cancel its return and
        // restore the old holds before the manual session takes its own pause snapshot.
        internal static bool YieldForControl(PlayerAIBot bot)
        {
            if (bot == null) return false;
            Cancel(bot);
            if (active.ContainsKey(bot.Pointer)) return false;
            if (returning.TryGetValue(bot.Pointer, out var back))
                back.Runner.Abort(true, "手动接管取消击杀返程，原任务交由接管会话暂停");
            return !returning.ContainsKey(bot.Pointer);
        }
        internal static string Diagnose(PlayerAIBot bot, EnemyAgent enemy, out string note)
        {
            note = null;
            try {
                var reason = Check(bot, enemy, out var candidate);
                if (reason == null) note = "预估 " + candidate.Damage.ToString("F1") + "／生命 " + candidate.Health.ToString("F1")
                    + (!SingleKillRunner.DamageEnough(candidate.Health, candidate.Damage, 1) ? "；首击不足，后续补击／枪械接战" : "")
                    + (ItemTaskAdapter.Busy(bot) || MovementOrders.Status(bot) != null ? "；临时借调，保留原任务" : "；可参与")
                    + (NativeBusy(bot) is string busy ? "；先等待" + busy : "");
                return reason;
            } catch (Exception error) { return "参与资格检查失败：" + error.GetBaseException().Message; }
        }
        static string Check(PlayerAIBot bot, EnemyAgent enemy, out Candidate candidate, Entry expected = null, PlayerAgent sourceOverride = null)
        {
            candidate = null;
            if (!SNet.IsMaster || GameStateManager.CurrentStateName != eGameStateName.InLevel || Builder.CurrentFloor == null) return "指定击杀需要房主在关卡内下达";
            if (bot?.Agent?.Owner?.IsBot != true || !bot.Agent.Alive || bot.Actions == null || bot.m_rootAction == null) return "机器人倒地或动作组件尚未就绪";
            if (!BotTaskBridge.Bind()) return "更好的人机任务仲裁接口未就绪";
            if (active.TryGetValue(bot.Pointer, out var own) && !ReferenceEquals(own, expected)) return "已有指定击杀／借调任务，请先等待或取消";
            if (returning.ContainsKey(bot.Pointer)) return "上一任务正在恢复；可先取消返程再重新派遣";
            if (!Sleeping(enemy)) return enemy == null ? "准星未识别存活敌人（尸体或场景碰撞可能遮挡），请重新瞄准后打开菜单"
                : !enemy.Alive ? "原菜单目标已死亡，请重新瞄准" : "目标已惊醒、无有效导航数据或不可受伤";
            foreach (var entry in active.Values) if (!ReferenceEquals(entry, expected) && entry.Session.Enemy.Pointer == enemy.Pointer) return "目标已分配给另一名机器人";
            if (RescueOrders.Owns(bot)) return "正在执行救援，不能借调";
            if (SharedMeleeBridge.Owns(bot)) return "正在执行E键协同近战，不能重复占用";
            if (NativePickupAdapter.Owns(bot)) return "任务／特殊手持搬运暂不支持保留后借调";
            if (ResourceUseAdapter.Owns(bot) || ToolSupplyAdapter.Owns(bot)) return "补给正在应用或核验，请收尾后再派遣";
            if (SearchTaskAdapter.Owns(bot)) return "当前搜寻查询尚未收尾，请稍后派遣";
            var source = sourceOverride ?? PlayerManager.GetLocalPlayerAgent();
            if (source?.Alive != true || source.CourseNode == null) return "指挥玩家倒地或区域数据未就绪";
            if (!ItemRegion.SameSpace(bot.Agent, enemy.CourseNode) || !ItemRegion.SameSpace(source, enemy.CourseNode)) return "机器人、玩家与目标不在同层同维度";
            if (!SearchTraversal.InScope(bot.Agent.CourseNode, source.CourseNode, 1) || !SearchTraversal.InScope(enemy.CourseNode, source.CourseNode, 1)) return "机器人或目标不在指挥玩家当前Zone";
            if (Vector3.Distance(source.Position, enemy.Position) > Limit(maxDistance, 30, 3, 60)) return "目标超出玩家指定击杀距离";
            if (BotTaskBridge.Unsafe(bot)) return "存在战斗、警报或紧急救援威胁，不适合潜行近战借调";
            if (NativeReviving(bot)) return "正在原版救援，不能借调";
            if (!bot.Backpack.TryGetBackpackItem(InventorySlot.GearMelee, out var item)
                || item.Instance?.TryCast<MeleeWeaponThirdPerson>() is not MeleeWeaponThirdPerson weapon || weapon.MeleeArchetypeData == null) return "机器人近战装备尚未就绪";
            if (expected != null && expected.Session.StrikeScope.Weapon.Pointer != weapon.Pointer) return "借调期间近战装备已更换，请重新下达击杀指令";
            Transform aim; Vector3 point; float damage;
            if (!(expected?.Session.TryPlan(out aim, out point, out damage) ?? false)
                && !KillPositionAdapter.Find(bot, enemy, weapon, out aim, out point, out damage))
                return "目标背后没有同时满足导航、站立、挥击距离和无遮挡的落点";
            if (!float.IsFinite(enemy.Damage.Health) || enemy.Damage.Health <= 0 || !float.IsFinite(damage) || damage <= 0)
                return "目标生命或原生近战伤害预估无效，不能开始指定击杀";
            var route = RouteTraversal.Build(bot.Agent, enemy.CourseNode, point, null, source.CourseNode, out var routeReason);
            if (route == null) return "接近路线不可用：" + routeReason;
            if (route.WaitingForDoor || !PassagesOpen(route)) return "途中普通门尚未打开；先开门再借调，不穿门传送";
            candidate = new(weapon, aim, point, enemy.Damage.Health, damage); return null;
        }
        static bool PassagesOpen(RoutePlan route)
        { if (route.Passages != null) foreach (var passage in route.Passages) if (!RouteTraversal.Open(passage)) return false; return true; }
        static bool NativeReviving(PlayerAIBot bot)
        { var actions = bot.Actions; for (int i = 0; i < Math.Min(actions.Count, 64); i++) if (actions[i]?.DescBase?.IsTerminated() == false && actions[i].TryCast<PlayerBotActionRevive>() != null) return true; return false; }
        static string NativeBusy(PlayerAIBot bot)
        {
            var actions = bot.Actions;
            for (int i = 0; i < Math.Min(actions.Count, 64); i++) {
                var action = actions[i]; if (action?.DescBase == null || action.DescBase.IsTerminated()) continue;
                if (action.TryCast<PlayerBotActionCollectItem>() != null) return "原版拾取核验";
                if (action.TryCast<PlayerBotActionUnlock>() != null) return "原版开锁交互";
                if (action.TryCast<PlayerBotActionGatherDeployables>() != null) return "原版回收工具";
                if (action.TryCast<PlayerBotActionDeploySentryGun>() != null || action.TryCast<PlayerBotActionDeployTripMine>() != null) return "原版部署工具";
                if (action.TryCast<PlayerBotActionShareResourcePack>() != null) return "原版补给交互";
                if (action.TryCast<PlayerBotActionCarryExpeditionItem>() != null) return "原版特殊手持搬运";
            }
            return null;
        }
        public static void Issue(PlayerAIBot bot, EnemyAgent enemy)
        {
            var reason = Check(bot, enemy, out var candidate);
            if (reason != null) { Plugin.Logger.LogInfo("[击杀资格] bot=" + bot?.Agent?.PlayerName + " target=" + enemy?.Pointer + " reject=" + reason); ItemTaskAdapter.Feedback(reason); return; }
            var session = new Session(bot, enemy, candidate.Weapon, candidate.Aim, candidate.Position, 0);
            int reportSlot = bot.Agent.PlayerSlotIndex; long reportOwner = bot.Agent.Owner.Pointer.ToInt64(); string reportBot = bot.Agent.PlayerName;
            string reportTarget = "敌人类型 #" + enemy.EnemyDataID + " @ " + enemy.Position;
            session.StatusObserver = (stage, detail) => TaskBoard.Journal.Record(session, reportSlot, reportOwner, reportBot, "指定击杀", reportTarget,
                stage switch { SingleKillStage.Complete => TaskBoardState.Complete, SingleKillStage.Cancelled => TaskBoardState.Cancelled,
                    SingleKillStage.Failed => TaskBoardState.Failed, _ => TaskBoardState.Executing }, Text(stage),
                detail ?? "接近背后并核验真实结果；目标存活则持续接战，原任务保持暂停", Time.time);
            session.StatusObserver(SingleKillStage.Approaching, "已接单，借调保留原任务，等待已提交的交互核验");
            var pending = new Entry { Session = session, Deadline = Time.time + Limit(timeout, 30, 5, 60) };
            active.Add(bot.Pointer, pending);
            try {
                AutoPickupAdapter.CancelPrompt(bot); ItemTaskAdapter.SuspendForBorrow(bot, session.Holds);
                Plugin.Logger.LogInfo("[击杀借调] bot=" + bot.Agent.PlayerName + " target=" + enemy.Pointer + " retained=" + session.Holds.Count);
                ItemTaskAdapter.Feedback(bot.Agent.PlayerName + "：击杀借调已登记，先完成当前交互核验；原任务保留");
            } catch { session.Finish(SingleKillStage.Failed, "启动借调失败，交还原任务"); throw; }
        }
        internal static IControlCombatTask CreateControlled(PlayerAIBot bot, EnemyAgent enemy, IControlCommandPermit permit, out string reason)
        {
            reason = Check(bot, enemy, out var plan, sourceOverride: bot.Agent);
            if (reason != null) return null;
            var entry = new Entry { Controlled = true, Session = new Session(bot, enemy, plan.Weapon, plan.Aim, plan.Position, 0, permit),
                Deadline = Time.time + Limit(timeout, 30, 5, 60) };
            active.Add(bot.Pointer, entry);
            entry.Session.StatusObserver = (stage, detail) => TaskBoard.Journal.Record(entry, bot.Agent.PlayerSlotIndex,
                bot.Agent.Owner.Pointer.ToInt64(), bot.Agent.PlayerName, "接管指定击杀", "敌人 #" + enemy.EnemyDataID,
                stage == SingleKillStage.Complete ? TaskBoardState.Complete : stage == SingleKillStage.Cancelled ? TaskBoardState.Cancelled
                    : stage == SingleKillStage.Failed ? TaskBoardState.Failed : TaskBoardState.Executing, Text(stage), detail, Time.time);
            return new ControlledKill(entry);
        }
        sealed class ControlledKill : IControlCombatTask
        {
            readonly Entry entry;
            internal ControlledKill(Entry entry) { this.entry = entry; }
            public bool Finished => entry.Session.Finished;
            public string Phase => entry.Runner == null ? "等待接管权限" : Text(entry.Runner.Stage);
            public bool Authorizes(PlayerBotActionBase.Descriptor desc) => !Finished && entry.Session.OurAction(desc);
            public void Tick(float now)
            {
                if (Finished) return;
                if (entry.Runner != null) { entry.Runner.Tick(now); return; }
                var session = entry.Session;
                if (now >= entry.Deadline || !session.Valid || !session.SleepingStable) { Abort("接管击杀准备超时／目标状态改变，未挥击"); return; }
                if (!session.Acquire()) return;
                entry.Runner = new SingleKillRunner(session, now, Limit(timeout, 30, 5, 60));
                if (!session.Renew(SingleKillStage.Approaching) || !session.BeginTravel()) entry.Runner.Abort("接管击杀接近动作未被接受");
            }
            public void Cancel() { if (entry.Runner != null) entry.Runner.Cancel(); else entry.Session.Finish(SingleKillStage.Cancelled, "接管退出，尚未开始击杀"); }
            public void Abort(string reason) { if (entry.Runner != null) entry.Runner.Abort(reason); else entry.Session.Finish(SingleKillStage.Failed, reason); }
        }
        internal static bool TryStrikeScope(MeleeWeaponThirdPerson weapon, out KillStrikeScope scope)
        {
            scope = null;
            if (weapon?.Owner == null) return false;
            foreach (var entry in active.Values) if (entry.Session.Bot.Agent.Pointer == weapon.Owner.Pointer
                && (entry.Runner?.Stage == SingleKillStage.Striking || entry.Runner?.Stage == SingleKillStage.Continuing) && entry.Session.CanHit(weapon)) {
                scope = entry.Session.StrikeScope; return true;
            }
            return false;
        }
        internal static bool TryGun(PlayerBotActionAttack action, out EnemyAgent enemy, out BulletWeaponSynced weapon)
        {
            enemy = null; weapon = null;
            if (action?.m_bot == null || !active.TryGetValue(action.m_bot.Pointer, out var entry)
                || entry.Runner?.Stage != SingleKillStage.Continuing || !entry.Session.GunMatches(action)) return false;
            enemy = entry.Session.Enemy; weapon = entry.Session.Gun;
            return enemy?.Alive == true && weapon != null && action.m_bot.HasLineOfFireToObject(action.m_bot.Agent.EyePosition, enemy.gameObject);
        }
        public static void Tick()
        {
            snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var entry in snapshot) try {
                if (entry.Controlled) continue; // The control child scheduler alone advances this entry.
                if (entry.Runner == null) {
                    if (!entry.Session.Retained) { entry.Session.Finish(SingleKillStage.Cancelled, "玩家更改了被借调任务，停止旧借调并保留新指令"); continue; }
                    if (!entry.Session.Valid || !entry.Session.SleepingStable) { entry.Session.Finish(SingleKillStage.Failed, "借调期间目标或机器人身份改变，未挥击"); continue; }
                    if (Time.time >= entry.Deadline) { entry.Session.Finish(SingleKillStage.Failed, "借调收尾／原版任务仲裁等待超时，未挥击"); continue; }
                    if (Time.time < entry.NextProbe) continue; entry.NextProbe = Time.time + 1;
                    var reason = Check(entry.Session.Bot, entry.Session.Enemy, out var refreshed, entry);
                    if (reason != null) { entry.Session.Finish(SingleKillStage.Failed, reason); continue; }
                    entry.Session.Replan(refreshed);
                    string wait = !entry.Session.Yielded ? "原任务尚未完成交互核验和暂停" : NativeBusy(entry.Session.Bot);
                    if (wait == null && !entry.Session.Acquire()) wait = "更好的人机仍保留原版任务／回收部署收尾／扫描预留或外部租约，未移交控制权限";
                    if (wait != null) {
                        if (wait != entry.LastWait) { entry.LastWait = wait; Plugin.Logger.LogInfo("[击杀借调] bot=" + entry.Session.Bot.Agent.PlayerName + " wait=" + wait); }
                        continue;
                    }
                    entry.Runner = new SingleKillRunner(entry.Session, Time.time, Limit(timeout, 30, 5, 60));
                    if (!entry.Session.Renew(SingleKillStage.Approaching) || !entry.Session.BeginTravel()) entry.Runner.Abort("借调后的接近动作未被接受");
                    continue;
                }
                if (!entry.Session.Retained) entry.Runner.Cancel();
                entry.Runner.Tick(Time.time);
            } catch (Exception error) {
                if (entry.Runner != null) entry.Runner.Abort("击杀检查失败：" + error.Message);
                else entry.Session.Finish(SingleKillStage.Failed, "借调检查失败：" + error.Message);
            }
            snapshot.Clear();
            returnSnapshot.Clear(); returnSnapshot.AddRange(returning.Values);
            foreach (var back in returnSnapshot) try { back.Runner.Tick(Time.time); }
                catch (Exception error) { back.Runner.Abort(back.Session.Valid, "借调恢复失败：" + error.Message); }
            returnSnapshot.Clear();
        }
        public static void Reset()
        {
            snapshot.Clear(); snapshot.AddRange(active.Values);
            foreach (var entry in snapshot) try {
                if (entry.Runner != null) entry.Runner.Abort("关卡或房主状态重置");
                else entry.Session.Finish(SingleKillStage.Failed, "关卡或房主状态重置");
            }
                catch (Exception error) { Plugin.Logger.LogWarning("指定击杀重置：" + error.Message); }
            snapshot.Clear();
            returnSnapshot.Clear(); returnSnapshot.AddRange(returning.Values);
            foreach (var back in returnSnapshot) try { back.Runner.Abort(false, "关卡或房主状态重置，丢弃恢复记录"); }
                catch (Exception error) { Plugin.Logger.LogWarning("借调恢复重置：" + error.Message); }
            returnSnapshot.Clear(); active.Clear(); returning.Clear();
        }
        static bool Allow(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (bot == null) return true;
            if (!active.TryGetValue(bot.Pointer, out var entry)) {
                if (!returning.TryGetValue(bot.Pointer, out var back) || !back.Session.HasControl || back.Session.OurAction(desc)) return true;
                if (desc?.TryCast<PlayerBotActionRevive.Descriptor>() != null || desc?.TryCast<PlayerBotActionUseBioscan.Descriptor>() != null) { back.Runner.Abort(true, "让位扫描／救援，交还原任务重新核验"); return true; }
                return (desc.RequiredLayers & (PlayerBotActionBase.AccessLayers.Legs | PlayerBotActionBase.AccessLayers.RootPosition)) == 0;
            }
            if (entry.Session.OurAction(desc)) return true;
            if (!entry.Session.HasControl) return true; // Existing atomic work must be allowed to finish.
            if (entry.Runner?.Stage == SingleKillStage.Continuing && desc?.TryCast<PlayerBotActionUseBioscan.Descriptor>() != null)
                return false; // Finish this explicit target before resuming the suspended scan.
            if (desc?.TryCast<PlayerBotActionRevive.Descriptor>() != null || desc?.TryCast<PlayerBotActionUseBioscan.Descriptor>() != null) {
                entry.Runner.Cancel(); return !active.ContainsKey(bot.Pointer); // Finish an already released strike before yielding.
            }
            return false;
        }
        static bool Request(PlayerAIBot __instance, PlayerBotActionBase.Descriptor desc, ref bool __result)
        { if (Allow(__instance, desc)) return true; __result = false; return false; }
        static bool Start(PlayerAIBot __instance, PlayerBotActionBase.Descriptor desc) => Allow(__instance, desc);
        static void Crouch(PlayerAIBot __instance, ref bool __result) { if (Owns(__instance)
            && (!active.TryGetValue(__instance.Pointer, out var entry) || entry.Runner?.Stage != SingleKillStage.Continuing)) __result = true; }
        static void Flashlight(PlayerAIBot __instance, ref bool __result) { if (Owns(__instance)) __result = false; }

        sealed class Session : ISingleKillSession
        {
            internal Action<SingleKillStage, string> StatusObserver;
            readonly PlayerAIBot bot; readonly IntPtr floor, owner, agentIdentity; readonly int playerSlot;
            readonly MeleeWeaponThirdPerson weapon; Transform aim; Vector3 position; readonly Vector3 targetPosition;
            internal KillStrikeScope StrikeScope;
            long token;
            bool finished, warped, strikeReleased;
            readonly IControlCommandPermit controlPermit;
            readonly ControlCombatEquipment equipment = new();
            readonly ControlCombatRoot combatRoot;
            internal bool Finished => finished;
            float nextStrikeProbe;
            bool geometryReady;
            public string StrikeFailure { get; private set; }
            internal readonly List<BorrowHold> Holds = new(4);
            internal PlayerAIBot Bot => bot;
            internal bool HasControl => token != 0;
            internal bool Yielded { get { foreach (var hold in Holds) if (!hold.Yielded) return false; return true; } }
            internal bool Retained { get { foreach (var hold in Holds) if (!hold.Retained) return false; return true; } }
            internal readonly Vector3 OriginalPosition;
            internal readonly AIGraph.AIG_CourseNode OriginalNode, Origin;
            readonly int enemyIdentity;
            PlayerBotActionTravel.Descriptor travel;
            PlayerBotActionMelee.Descriptor melee;
            PlayerBotActionAttack.Descriptor attack;
            internal BulletWeaponSynced Gun { get; private set; }
            bool continuing;
            float continueDue, progressAt, lastHealth, chosenAt, meleeDeadline, requestAt, meleeForce = 1;
            string lastContinuationNote;
            void ContinuationNote(string text) { if (text == lastContinuationNote) return; lastContinuationNote = text;
                StatusObserver?.Invoke(SingleKillStage.Continuing, text); }
            internal bool GunMatches(PlayerBotActionAttack action) => Valid && HasControl && attack != null && !attack.IsTerminated()
                && action.m_desc?.Pointer == attack.Pointer && action.m_desc.TargetAgent?.Pointer == Enemy.Pointer;
            public EnemyAgent Enemy { get; }
            public Session(PlayerAIBot bot, EnemyAgent enemy, MeleeWeaponThirdPerson weapon, Transform aim, Vector3 position, long token, IControlCommandPermit controlPermit = null)
            { this.bot = bot; Enemy = enemy; this.weapon = weapon; this.aim = aim; this.position = position; this.token = token;
                floor = Builder.CurrentFloor?.Pointer ?? IntPtr.Zero; owner = bot.Agent.Owner.Pointer; targetPosition = enemy.Position;
                agentIdentity = bot.Agent.Pointer; playerSlot = bot.Agent.PlayerSlotIndex; enemyIdentity = enemy.GetInstanceID();
                this.controlPermit = controlPermit;
                if (controlPermit != null) combatRoot = new(bot);
                OriginalPosition = bot.Agent.Position; OriginalNode = bot.Agent.CourseNode; Origin = controlPermit != null ? bot.Agent.CourseNode : PlayerManager.GetLocalPlayerAgent()?.CourseNode;
                StrikeScope = new(bot.Agent, enemy, weapon); }
            internal void Replan(Candidate plan) { aim = plan.Aim; position = plan.Position; }
            internal bool TryPlan(out Transform target, out Vector3 point, out float damage)
            {
                target = aim; point = position; damage = float.NaN;
                var limb = aim?.GetComponent<Dam_EnemyDamageLimb>();
                var eye = Vector3.up * Math.Clamp(bot.Agent.EyePosition.y - bot.Agent.Position.y, .8f, 1.4f);
                if (!KillPositionAdapter.CanSwing(bot.Agent, Enemy, weapon, limb, point, eye)) return false;
                damage = KillMeleeAdapter.Preview(bot.Agent, Enemy, weapon, limb, point, eye); return float.IsFinite(damage);
            }
            internal bool CanHit(MeleeWeaponThirdPerson actual) => !finished && Valid && token != 0 && actual.Pointer == weapon.Pointer
                && actual.Owner?.Pointer == bot.Agent.Pointer && melee != null && strikeReleased
                && !melee.IsTerminated() && melee.TargetAgent?.Pointer == Enemy.Pointer;
            bool IdentityMatches => controlPermit?.IdentityValid != false && SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && floor != IntPtr.Zero
                && Builder.CurrentFloor?.Pointer == floor && bot?.Agent != null && bot.Agent.Owner?.IsBot == true
                && bot.Agent.Owner.Pointer == owner && PlayerManager.Current?.GetPlayerAgentInSlot(playerSlot)?.Pointer == agentIdentity;
            bool ValidBot => IdentityMatches && bot.Agent.Alive;
            public bool Valid => ValidBot && Enemy != null && Enemy.GetInstanceID() == enemyIdentity && Enemy.DimensionIndex == bot.Agent.DimensionIndex;
            internal bool IdentityValid => IdentityMatches;
            internal bool Acquire() => token != 0 || (controlPermit != null ? controlPermit.Acquire(out token) : BotTaskBridge.Acquire(bot, out token));
            public bool TargetAlive => Enemy.Alive;
            public bool SleepingStable => Sleeping(Enemy) && Vector3.Distance(Enemy.Position, targetPosition) <= .75f;
            public bool Unsafe => BotTaskBridge.Unsafe(bot);
            public bool CanExecute => BotTaskBridge.CanExecute(bot, token);
            public bool Arrived => travel?.Status == PlayerBotActionBase.Descriptor.StatusType.Successful && Vector3.Distance(bot.Agent.Position, position) <= .8f;
            public bool TravelFailed => travel == null || travel.IsTerminated();
            public bool MeleeTerminated => melee == null || melee.IsTerminated();
            public bool ContinuationAtomic => continuing && strikeReleased && melee?.IsTerminated() == false && Time.time < meleeDeadline;
            public bool Ready => aim != null && weapon != null && melee?.Status == PlayerBotActionBase.Descriptor.StatusType.Active
                && melee.State == PlayerBotActionMelee.Descriptor.StateEnum.Charge && melee.IsCharged
                && melee.ActionBase?.TryCast<PlayerBotActionMelee>()?.CheckStrikeDistance() == true
                && ReadyGeometry();
            bool ReadyGeometry()
            {
                if (Time.time < nextStrikeProbe) return geometryReady;
                nextStrikeProbe = Time.time + .15f;
                return geometryReady = ReleaseGeometry();
            }
            bool ReleaseGeometry()
            {
                var limb = aim?.GetComponent<Dam_EnemyDamageLimb>();
                var eye = bot.Agent.EyePosition - bot.Agent.Position;
                StrikeFailure = null;
                if (!KillPositionAdapter.CanSwing(bot.Agent, Enemy, weapon, limb, bot.Agent.Position, eye, requireBehind: !continuing))
                    StrikeFailure = "挥击前背后站位、武器距离、弱点或遮挡核验失败，未释放攻击";
                else {
                    float damage = KillMeleeAdapter.Preview(bot.Agent, Enemy, weapon, limb, bot.Agent.Position, eye, continuing, meleeForce);
                    if (!float.IsFinite(damage) || damage <= 0)
                        StrikeFailure = "挥击前原生伤害预估无效，未释放攻击";
                }
                return StrikeFailure == null;
            }
            public bool Renew(SingleKillStage stage)
            {
                var value = stage == SingleKillStage.Approaching || stage == SingleKillStage.Continuing && !ContinuationAtomic ? ItemTaskStage.Moving
                    : stage == SingleKillStage.Charging ? ItemTaskStage.Interacting : ItemTaskStage.Verifying;
                return controlPermit != null ? controlPermit.Report(value) : BotTaskBridge.Report(bot, token, value);
            }
            public bool BeginTravel()
            {
                if (ItemTaskAdapter.TeleportEnabled && Vector3.Distance(bot.Agent.Position, position) > ItemTaskAdapter.TeleportDistance) {
                    var before = bot.Agent.Position;
                    if (!BotTaskBridge.Teleport(bot, token, position)) return false;
                    warped = Vector3.Distance(before, bot.Agent.Position) > 1;
                }
                travel = new PlayerBotActionTravel.Descriptor(bot) { Prio = 100000f, Haste = .5f,
                    WalkPosture = PlayerBotActionWalk.Descriptor.PostureEnum.Crouch,
                    DestinationType = PlayerBotActionTravel.Descriptor.DestinationEnum.Position, DestinationPos = position,
                    Radius = .25f, RadiusHeightTolerance = .6f, Persistent = false };
                return bot.RequestAction(travel);
            }
            public bool BeginMelee()
            {
                if (aim == null || weapon == null) return false;
                strikeReleased = false; geometryReady = false; nextStrikeProbe = 0; StrikeFailure = null;
                StrikeScope = new(bot.Agent, Enemy, weapon) { RequireBehind = !continuing };
                melee = new PlayerBotActionMelee.Descriptor(bot) { Prio = 100000f, Haste = .5f, Force = meleeForce, Weapon = weapon,
                    TargetAgent = Enemy, TargetGameObject = aim.gameObject, Strike = false, Loop = false, Travel = false, Push = false };
                return bot.RequestAction(melee);
            }
            public void Strike()
            {
                if (!ReleaseGeometry()) { Finish(SingleKillStage.Failed, StrikeFailure); return; }
                strikeReleased = true; melee.Strike = true;
            }
            public string ContinueAttack(float now)
            {
                if (!continuing) {
                    continuing = true; progressAt = now; lastHealth = Enemy.Damage.Health;
                    if (melee?.IsTerminated() == false) bot.StopAction(melee);
                    melee = null; strikeReleased = false;
                    if (travel?.IsTerminated() == false) bot.StopAction(travel);
                    travel = null;
                    StatusObserver?.Invoke(SingleKillStage.Continuing, "首轮未击杀；按实际剩余生命持续接战，尚不返程");
                }
                if (now < continueDue) return null; continueDue = now + .25f;
                float hp = Enemy.Damage.Health;
                if (!float.IsFinite(hp) || hp <= 0 || Enemy.Damage.IsImortal) return "目标生命数据异常或已不可受伤";
                if (hp < lastHealth - .01f) progressAt = now;
                lastHealth = hp;
                if (now - progressAt >= 60) return "持续接战60秒无伤害进展：目标遮挡、距离、弹药或原版攻击动作不可用；停止本指令，保留失败诊断";
                if (!SearchTraversal.InScope(Enemy.CourseNode, Origin, 1)) return "目标已离开借调时的Zone，停止追击";
                if (melee != null && !melee.IsTerminated()) {
                    if (now >= meleeDeadline) { bot.StopAction(melee); melee = null; strikeReleased = false; }
                    else {
                        if (!strikeReleased && Ready) Strike();
                        else if (!strikeReleased && StrikeFailure != null) { bot.StopAction(melee); melee = null; }
                        return null;
                    }
                }
                melee = null; strikeReleased = false;
                if (travel?.IsTerminated() == false) return null;
                travel = null;
                if (bot.Inventory?.IsEquiping == true) return null;
                var choice = KillCombatBridge.Evaluate(bot, Enemy, Gun, now - chosenAt);
                var estimate = choice?.Estimate ?? default;
                var eye = bot.Agent.EyePosition - bot.Agent.Position;
                Transform best = null; float damage = 0, quick = 0;
                var limbs = Enemy.Damage.DamageLimbs;
                for (int i = 0; limbs != null && i < Math.Min(limbs.Count, 128); i++) {
                    var limb = limbs[i];
                    if (!KillPositionAdapter.CanSwing(bot.Agent, Enemy, weapon, limb, bot.Agent.Position, eye, requireBehind: false)) continue;
                    float value = KillMeleeAdapter.Preview(bot.Agent, Enemy, weapon, limb, bot.Agent.Position, eye, true);
                    if (float.IsFinite(value) && value > damage) { damage = value; best = limb.transform;
                        quick = KillMeleeAdapter.Preview(bot.Agent, Enemy, weapon, limb, bot.Agent.Position, eye, true, 0); }
                }
                meleeForce = SingleKillRunner.DamageEnough(hp, quick, 1) ? 0 : 1;
                float hitDamage = meleeForce == 0 ? quick : damage;
                var times = PlayerBotActionMelee.s_chargeTimes;
                float charge = times == null || times.Length < 2 ? 2 : Math.Max(.1f, times[meleeForce == 0 ? 0 : times.Length - 1]);
                float meleeTime = charge + Math.Max(.1f, PlayerBotActionMelee.s_strikeDuration)
                    + (bot.Inventory?.WieldedItem?.Pointer == weapon.Pointer ? 0 : Math.Max(.1f, weapon.EquipTransitionTime));
                if (KillContinuationPolicy.Melee(hp, hitDamage, meleeTime, estimate, best != null)
                    && (Gun?.IsReloading != true || !estimate.Usable || !estimate.CanFinish || hp <= hitDamage && meleeTime + .2f < estimate.Time)
                    && (attack == null || attack.IsTerminated() || now - chosenAt >= 3
                        || Gun?.GetCurrentClip() <= 0 && hp <= hitDamage && meleeTime + .2f < estimate.Time)) {
                    if (attack?.IsTerminated() == false) bot.StopAction(attack);
                    combatRoot?.Restore();
                    attack = null; Gun = null; aim = best;
                    meleeDeadline = now + Math.Max(5, meleeTime + 2);
                    if (!BeginMelee()) { requestAt = now + 1; return "补击的原版近战动作未被接受"; }
                    ContinuationNote("剩余生命 " + hp.ToString("F1") + "；苏醒后补击预估 " + hitDamage.ToString("F1"));
                    Plugin.Logger.LogInfo("[击杀接战] bot=" + bot.Agent.PlayerName + " hp=" + hp.ToString("F2")
                        + " awakeMelee=" + hitDamage.ToString("F2") + " force=" + meleeForce + " meleeTime=" + meleeTime.ToString("F2"));
                    return null;
                }
                if (choice == null || !estimate.Usable || !estimate.CanFinish
                    || !bot.HasLineOfFireToObject(bot.Agent.EyePosition, Enemy.gameObject)) {
                    if (attack?.IsTerminated() == false) bot.StopAction(attack);
                    combatRoot?.Restore();
                    attack = null; Gun = null;
                    ContinuationNote("枪械弹药／射程／视线暂不可用，评估近战接近；本次目标尚未完成");
                    if (now >= requestAt) {
                        requestAt = now + 1;
                        if (KillPositionAdapter.Find(bot, Enemy, weapon, out var approachAim, out var approachPoint, out _)) {
                            var approachRoute = RouteTraversal.Build(bot.Agent, Enemy.CourseNode, approachPoint, null, Origin, out _);
                            if (approachRoute != null && !approachRoute.WaitingForDoor && PassagesOpen(approachRoute)) {
                                aim = approachAim; position = approachPoint;
                                travel = new PlayerBotActionTravel.Descriptor(bot) { Prio = 100000f, Haste = .5f,
                                    DestinationType = PlayerBotActionTravel.Descriptor.DestinationEnum.Position, DestinationPos = position,
                                    Radius = .25f, RadiusHeightTolerance = .6f, Persistent = false };
                                if (!bot.RequestAction(travel)) travel = null;
                            }
                        }
                    }
                    return null;
                }
                bool changed = Gun?.Pointer != choice.Weapon.Pointer;
                if (changed) {
                    Gun = choice.Weapon; chosenAt = now;
                    Plugin.Logger.LogInfo("[击杀接战] bot=" + bot.Agent.PlayerName + " hp=" + hp.ToString("F2") + " gun=" + Gun.PublicName
                        + " clip=" + Gun.GetCurrentClip() + " readyKill=" + estimate.ReadyKill + " ttk=" + estimate.Time.ToString("F2"));
                }
                ContinuationNote("枪械持续接战：" + Gun.PublicName + (Gun.IsReloading ? "（等待原版换弹）" : ""));
                if (attack?.IsTerminated() == false) return null;
                if (now < requestAt) return null; requestAt = now + 1;
                // Avoid chasing through a new door or sealed passage. No follow-up teleport.
                var route = RouteTraversal.Build(bot.Agent, Enemy.CourseNode, Enemy.Position, null, Origin, out var routeReason);
                if (route == null || route.WaitingForDoor || !PassagesOpen(route)) return "接战通路失效：" + routeReason;
                attack = new PlayerBotActionAttack.Descriptor(bot) { Prio = 100000f, TargetAgent = Enemy,
                    Means = PlayerBotActionAttack.AttackMeansEnum.Bullet, MovementAllowed = false, Haste = .5f };
                combatRoot?.Bind(attack);
                if (!bot.RequestAction(attack)) { attack = null; return "指定目标的原版枪械接战动作未被接受"; }
                return null;
            }
            public bool OurAction(PlayerBotActionBase.Descriptor desc)
            {
                if (controlPermit != null && (equipment.AuthorizesMelee(bot, desc, weapon, melee) || equipment.Authorizes(bot, desc, Gun, attack))) return true;
                for (int i = 0; desc != null && i < 8; i++) {
                    if (desc.Pointer == travel?.Pointer || desc.Pointer == melee?.Pointer || desc.Pointer == attack?.Pointer) return true;
                    desc = desc.ParentActionBase?.DescBase;
                }
                return false;
            }
            public void Finish(SingleKillStage result, string reason)
            {
                if (finished) return; finished = true;
                try { StatusObserver?.Invoke(result, reason); }
                catch (Exception error) { Plugin.Logger.LogWarning("指定击杀结果记录：" + error.Message); }
                active.Remove(bot.Pointer); // Stop callbacks must already see ownership released.
                if (controlPermit?.IdentityValid != false) combatRoot?.Restore();
                try {
                    if (ValidBot) {
                        if (melee != null && !melee.IsTerminated()) bot.StopAction(melee);
                        if (travel != null && !travel.IsTerminated()) bot.StopAction(travel);
                        if (attack != null && !attack.IsTerminated()) bot.StopAction(attack);
                    }
                } catch (Exception error) { Plugin.Logger.LogWarning("指定击杀动作清理：" + error.Message); }
                if (controlPermit != null) {
                    token = 0; controlPermit.Hold();
                    ItemTaskAdapter.Feedback("接管指定击杀：" + reason + "；交回微调，旧任务保持暂停"); return;
                }
                var returnPoint = OriginalPosition;
                try {
                    if (Holds.Count == 0 && token != 0 && warped && ValidBot && BotTaskBridge.ResumePosition(bot, token, out var nativeResume)) returnPoint = nativeResume;
                    if (token != 0 && IdentityMatches) BotTaskBridge.Release(bot, token, result == SingleKillStage.Complete ? ItemTaskStage.Complete
                        : result == SingleKillStage.Cancelled ? ItemTaskStage.Cancelled : ItemTaskStage.Failed);
                }
                catch (Exception error) { Plugin.Logger.LogWarning("指定击杀权限清理：" + error.Message); }
                finally { token = 0; }
                if (IdentityMatches && (Holds.Count != 0 || warped)) {
                    var restore = new ReturnSession(this, returnPoint, Holds.Count != 0 || warped);
                    returning[bot.Pointer] = new(restore, new BorrowReturnRunner(restore, Time.time));
                } else foreach (var hold in Holds) hold.Discard();
                if (SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel)
                    ItemTaskAdapter.Feedback("指定击杀：" + reason);
            }
        }
        sealed class ReturnSession : IBorrowReturnSession
        {
            readonly Session saved;
            readonly Vector3 destination;
            readonly bool move;
            readonly object report = new();
            long token;
            PlayerBotActionTravel.Descriptor travel;
            public ReturnSession(Session saved, Vector3 destination, bool move) { this.saved = saved; this.destination = destination; this.move = move; }
            public bool Valid => saved.IdentityValid && saved.Bot.Agent.CourseNode != null;
            public bool Retained => saved.Retained;
            public bool Safe => saved.Bot.Agent.Alive && !BotTaskBridge.Unsafe(saved.Bot) && !NativeReviving(saved.Bot) && NativeBusy(saved.Bot) == null;
            internal bool HasControl => token != 0 && Valid && Safe;
            public bool Arrived => !move || Vector3.Distance(saved.Bot.Agent.Position, destination) <= .8f;
            public bool Moving => travel != null && !travel.IsTerminated();
            public bool Acquire() => BotTaskBridge.Acquire(saved.Bot, out token);
            public bool Renew() => BotTaskBridge.Report(saved.Bot, token, ItemTaskStage.Returning) && BotTaskBridge.CanExecute(saved.Bot, token);
            public void Release()
            {
                var lease = token; token = 0;
                try { if (Valid && travel != null && !travel.IsTerminated()) saved.Bot.StopAction(travel); }
                finally { if (lease != 0 && Valid) BotTaskBridge.Release(saved.Bot, lease, ItemTaskStage.Cancelled); }
                travel = null;
            }
            internal bool OurAction(PlayerBotActionBase.Descriptor desc)
            { for (int i = 0; desc != null && i < 8; i++, desc = desc.ParentActionBase?.DescBase) if (desc.Pointer == travel?.Pointer) return true; return false; }
            public bool Move()
            {
                if (!saved.Retained || !saved.Yielded || !AIGraph.AIG_CourseNode.TryGetCourseNode(saved.Bot.Agent.DimensionIndex, destination, 1, out var node)
                    || !SearchTraversal.InScope(PlayerManager.GetLocalPlayerAgent()?.CourseNode, saved.Origin, 1)
                    || Physics.CheckCapsule(destination + Vector3.up * .35f, destination + Vector3.up * 1.4f, .25f, LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore)) return false;
                var route = RouteTraversal.Build(saved.Bot.Agent, node, destination, null, saved.Origin, out var reason);
                if (route == null || route.WaitingForDoor || !PassagesOpen(route)) { Plugin.Logger.LogInfo("[击杀借调] 返程路线拒绝：" + reason); return false; }
                if (travel != null && !travel.IsTerminated()) return true;
                if (ItemTaskAdapter.TeleportEnabled && Vector3.Distance(saved.Bot.Agent.Position, destination) > ItemTaskAdapter.TeleportDistance
                    && !BotTaskBridge.Teleport(saved.Bot, token, destination)) return false;
                if (Arrived) return true;
                travel = new PlayerBotActionTravel.Descriptor(saved.Bot) { Prio = 100000f, Haste = .5f,
                    WalkPosture = PlayerBotActionWalk.Descriptor.PostureEnum.Crouch,
                    DestinationType = PlayerBotActionTravel.Descriptor.DestinationEnum.Position, DestinationPos = destination,
                    Radius = .4f, RadiusHeightTolerance = .6f, Persistent = false };
                return saved.Bot.RequestAction(travel);
            }
            public void Finish(bool restore, string reason)
            {
                returning.Remove(saved.Bot.Pointer);
                foreach (var hold in saved.Holds) try { if (restore && Valid) hold.Restore(); else hold.Discard(); }
                    catch (Exception error) { Plugin.Logger.LogWarning("[击杀借调] 原任务恢复失败：" + error.Message); }
                saved.Holds.Clear();
                if (Valid) {
                    TaskBoard.Journal.Record(report, saved.Bot.Agent.PlayerSlotIndex, saved.Bot.Agent.Owner.Pointer.ToInt64(), saved.Bot.Agent.PlayerName,
                        "击杀借调恢复", "借调前位置", restore ? TaskBoardState.Complete : TaskBoardState.Cancelled, "恢复收尾", reason, Time.time);
                    ItemTaskAdapter.Feedback(saved.Bot.Agent.PlayerName + "：" + reason);
                }
                Plugin.Logger.LogInfo("[击杀借调] bot=" + saved.Bot.Agent?.PlayerName + " restore=" + restore + " reason=" + reason);
            }
        }
    }
}
