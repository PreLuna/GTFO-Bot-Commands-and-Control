using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    // Gate the native one-use action; never manufacture health, ammunition, or pack charges.
    internal static class ResourceUseAdapter
    {
        sealed class Grant
        {
            public PlayerAIBot Bot;
            public PlayerBotActionShareResourcePack.Descriptor Desc;
            public IntPtr Owner, ReceiverOwner, Item, Floor;
            public SupplyKind Kind;
            public float Created, NextCheck, CheckedAt;
            public bool Command, Requested, Accepted, Applied, Cancelled, Allowed, Return;
            public long Token;
            public int Slot;
            public string BotName, ReceiverName;
            public readonly object Report = new();
        }
        static readonly Dictionary<IntPtr, (IntPtr Owner, ResourceUseMode Mode)> modes = new();
        static readonly Dictionary<IntPtr, Grant> grants = new();
        static readonly Dictionary<(IntPtr, SupplyKind), float> cooldowns = new();
        static readonly Dictionary<IntPtr, (IntPtr Owner, float At)> stopping = new();
        static readonly List<IntPtr> stale = new();
        static ConfigEntry<ResourceUseMode> defaultMode;
        static ConfigEntry<float> health, ammo, tool, infection, cooldown;
        static FieldInfo recorderBot, recorderRoot;
        sealed class SelectionCache { public IntPtr Bot, Owner, Receiver, ReceiverOwner, Item; public ResourceUseMode Mode; public float At; public bool Allowed; }
        static readonly Dictionary<IntPtr, SelectionCache> selectionCache = new();
        static float warningAt, pruneAt;
        internal static bool HasState => modes.Count > 0 || grants.Count > 0 || cooldowns.Count > 0;
        static float Setting(ConfigEntry<float> entry, float fallback, float min = 0, float max = 1)
            => float.IsFinite(entry?.Value ?? fallback) ? Math.Clamp(entry?.Value ?? fallback, min, max) : fallback;
        public static void Configure(ConfigFile config)
        {
            defaultMode = config.Bind("机器人资源使用", "默认策略", ResourceUseMode.EmergencyOnly,
                "CommandsOnly只按命令；EmergencyOnly安全时保留、达到紧急阈值可用；CombatAutomatic危险时允许原版自动补给；Original恢复原版自动分发。菜单修改仅本局、仅所选机器人；战斗组共用此策略。");
            health = config.Bind("机器人资源使用", "紧急生命比例", .25f, "相对实际最大生命，默认25%；仍须原版NeedHealth判定。不会按固定血量判断。");
            ammo = config.Bind("机器人资源使用", "紧急双枪弹药比例", .08f, "两把枪的备弹加弹夹均低于此比例，默认8%；空弹夹但有备弹不算紧急。");
            tool = config.Bind("机器人资源使用", "紧急工具弹药比例", .05f, "实际工具资源比例，默认5%；还须原版NeedToolAmmo，不对不能补充的工具强行使用。");
            infection = config.Bind("机器人资源使用", "紧急感染比例", .70f, "消毒包默认感染达到70%，还须原版NeedDisinfection。");
            cooldown = config.Bind("机器人资源使用", "同对象同资源使用间隔", 3f, "每次真实消耗后重新评估；同队友同资源至少间隔1–15秒，防止多个机器人同时浪费。明确重复点击不增加次数。");
        }
        public static ResourceUseMode Mode(PlayerAgent agent)
        {
            if (agent?.Owner != null && modes.TryGetValue(agent.Pointer, out var saved) && saved.Owner == agent.Owner.Pointer) return saved.Mode;
            var value = defaultMode?.Value ?? ResourceUseMode.EmergencyOnly;
            return ResourceUsePolicy.Valid(value) ? value : ResourceUseMode.EmergencyOnly;
        }
        public static string ModeText(PlayerAgent agent) => CaptainCommandCatalog.ResourceModes[(int)Mode(agent)];
        public static string MenuPack(PlayerAIBot bot, out long identity)
        {
            identity = 0;
            if (!Held(bot, out var item, out var amount) || Kind(item) == SupplyKind.Unknown) return null;
            identity = item.Pointer.ToInt64();
            return (Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName) + "，余量 " + amount.ToString("0.#");
        }
        public static void SetMode(PlayerAgent agent, int mode)
        {
            if (agent?.Owner?.IsBot != true || !ResourceUsePolicy.Valid((ResourceUseMode)mode)) return;
            if (Mode(agent) == ResourceUseMode.Original && mode != (int)ResourceUseMode.Original) {
                var bot = agent.GetComponent<PlayerAIBot>();
                if (bot != null && !grants.ContainsKey(bot.Pointer) && bot.Actions != null) {
                    foreach (var action in bot.Actions) {
                        var share = action?.TryCast<PlayerBotActionShareResourcePack>();
                        if (share?.m_state != PlayerBotActionShareResourcePack.State.Apply || share.m_desc == null || share.m_desc.IsTerminated()) continue;
                        var grant = MakeGrant(bot, share.m_desc, false);
                        grant.Requested = grant.Accepted = true; grants[bot.Pointer] = grant; break;
                    }
                }
            }
            modes[agent.Pointer] = (agent.Owner.Pointer, (ResourceUseMode)mode);
            selectionCache.Clear();
            foreach (var grant in grants.Values) if (grant.Bot?.Agent?.Pointer == agent.Pointer) grant.NextCheck = 0;
            ItemTaskAdapter.Feedback(agent.PlayerName + "：" + ModeText(agent) + "（仅本局；不立即消耗，已经开始的单次使用先收尾）");
        }
        public static void Install(Harmony patches)
        {
            patches.Patch(AccessTools.Method(typeof(RootPlayerBotAction), "UpdateActionShareResoursePack"),
                prefix: new HarmonyMethod(typeof(ResourceUseAdapter), nameof(BeforeSelection)),
                postfix: new HarmonyMethod(typeof(ResourceUseAdapter), nameof(AfterSelection)));
            patches.Patch(AccessTools.Method(typeof(PlayerAIBot), "RequestAction", new[] { typeof(PlayerBotActionBase.Descriptor) }),
                prefix: new HarmonyMethod(typeof(ResourceUseAdapter), nameof(Request)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(ResourceUseAdapter), nameof(RequestResult)));
            patches.Patch(AccessTools.Method(typeof(PlayerBotActionShareResourcePack), "Update"),
                prefix: new HarmonyMethod(typeof(ResourceUseAdapter), nameof(UpdateShare)));
            patches.Patch(AccessTools.Method(typeof(Gear.ResourcePackFirstPerson), "ApplyPackBot"),
                prefix: new HarmonyMethod(typeof(ResourceUseAdapter), nameof(BeforeApply)),
                postfix: new HarmonyMethod(typeof(ResourceUseAdapter), nameof(AfterApply)));
            var recorder = KnownTypes.Get("PreLuna.CoopBotsCustom", "PreLuna.CoopBotsCustom.Components.BotRecorder");
            recorderBot = recorder == null ? null : AccessTools.Field(recorder, "m_bot");
            recorderRoot = recorder == null ? null : AccessTools.Field(recorder, "m_rootAction");
            var stop = recorder == null ? null : AccessTools.Method(recorder, "UpdateShareResourceAction", Type.EmptyTypes);
            if (recorderBot == null || recorderRoot == null || stop == null) throw new MissingMethodException("更好的AI资源取消接口不存在，不能安全启用战斗补给策略");
            patches.Patch(stop, prefix: new HarmonyMethod(typeof(ResourceUseAdapter), nameof(AllowDangerCancellation)));
        }
        static bool Host => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel;
        static void Warn(Exception error)
        {
            if (Time.time < warningAt) return;
            warningAt = Time.time + 10; Plugin.Logger.LogWarning("[补给策略] 核验失败，保留资源并等待重试：" + error);
        }
        internal static bool Owns(PlayerAIBot bot) => bot != null && grants.TryGetValue(bot.Pointer, out var grant) && Identity(grant);
        internal static string Status(PlayerAIBot bot)
        {
            if (bot == null || !grants.TryGetValue(bot.Pointer, out var grant) || !Identity(grant)) return null;
            if (grant.Cancelled) return "补给收尾";
            if (!grant.Requested) return "补给等待权限";
            return grant.Desc.ActionBase?.TryCast<PlayerBotActionShareResourcePack>()?.m_state == PlayerBotActionShareResourcePack.State.Apply ? "使用资源包" : "接近补给对象";
        }
        static bool Identity(Grant grant)
            => Host && grant.Bot?.Agent?.Owner?.IsBot == true && grant.Bot.Agent.Owner.Pointer == grant.Owner && grant.Bot.Agent.Alive
                && grant.Desc?.Receiver?.Owner?.Pointer == grant.ReceiverOwner && grant.Desc.Receiver.Alive
                && grant.Floor != IntPtr.Zero && LevelGeneration.Builder.CurrentFloor?.Pointer == grant.Floor
                && grant.Desc.Item?.Pointer == grant.Item && grant.Desc.Bot?.Pointer == grant.Bot.Pointer;
        static bool Held(PlayerAIBot bot, out ItemEquippable item, out float amount)
        {
            item = null; amount = 0;
            if (!ReadCarried(bot, InventorySlot.ResourcePack, out var held, out amount)) return false;
            item = held?.TryCast<ItemEquippable>(); return item != null && float.IsFinite(amount) && amount > 0;
        }
        static bool ReadCarried(PlayerAIBot bot, InventorySlot slot, out Item item, out float amount)
        {
            item = null; amount = 0;
            if (bot?.Agent?.Owner == null || !PlayerBackpackManager.TryGetBackpack(bot.Agent.Owner, out var backpack)
                || backpack?.AmmoStorage == null || !backpack.TryGetBackpackItem(slot, out var value) || value?.Instance == null) return false;
            item = value.Instance;
            if (slot == InventorySlot.ResourcePack || slot == InventorySlot.Consumable)
                amount = backpack.AmmoStorage.GetInventorySlotAmmo(slot)?.AmmoInPack ?? 0;
            return true;
        }
        static SupplyKind Kind(ItemEquippable item)
        {
            if (item == null || !PlayerBackpackManager.TryGetItemInLevelFromItemData(item.Get_pItemData(), out var world)) return SupplyKind.Unknown;
            var pickup = world?.TryCast<Gear.ResourcePackPickup>();
            return pickup?.m_packType switch {
                eResourceContainerSpawnType.Health => SupplyKind.Health,
                eResourceContainerSpawnType.AmmoWeapon => SupplyKind.WeaponAmmo,
                eResourceContainerSpawnType.AmmoTool => SupplyKind.ToolAmmo,
                eResourceContainerSpawnType.Disinfection => SupplyKind.Disinfection,
                _ => SupplyKind.Unknown
            };
        }
        static float Total(PlayerAmmoStorage storage, InventorySlot slot)
        {
            var value = storage?.GetInventorySlotAmmo(slot);
            if (value == null || value.AmmoMaxCap <= 0) return 1;
            // Native GetClipAmmoFromSlot returns a bullet count, whereas AmmoInPack/AmmoMaxCap are resource units.
            return ResourceUsePolicy.Fraction((value.AmmoInPack + storage.GetClipAmmoFromSlot(slot) * value.CostOfBullet) / value.AmmoMaxCap);
        }
        static SupplyNeed Need(PlayerAgent agent, SupplyKind kind)
        {
            bool needed = kind switch {
                SupplyKind.Health => agent.NeedHealth(), SupplyKind.WeaponAmmo => agent.NeedWeaponAmmo(),
                SupplyKind.ToolAmmo => agent.NeedToolAmmo(), SupplyKind.Disinfection => agent.NeedDisinfection(), _ => false };
            float hp = agent.Damage?.HealthMax > 0 ? agent.Damage.Health / agent.Damage.HealthMax : 1;
            PlayerAmmoStorage storage = null;
            if (kind == SupplyKind.WeaponAmmo || kind == SupplyKind.ToolAmmo) {
                if (!PlayerBackpackManager.TryGetBackpack(agent.Owner, out var backpack) || backpack?.AmmoStorage == null) return new(false, hp, 1, 1, 1, 0);
                storage = backpack.AmmoStorage;
            }
            return new(needed, hp, kind == SupplyKind.WeaponAmmo ? Total(storage, InventorySlot.GearStandard) : 1,
                kind == SupplyKind.WeaponAmmo ? Total(storage, InventorySlot.GearSpecial) : 1,
                kind == SupplyKind.ToolAmmo ? Total(storage, InventorySlot.GearClass) : 1, agent.Damage?.Infection ?? 0);
        }
        static bool Protected(PlayerAIBot bot)
        {
            if (ItemTaskAdapter.Busy(bot) || SingleKillAdapter.Owns(bot)) return true;
            var actions = bot.Actions;
            if (actions == null) return true;
            for (int i = 0; i < Math.Min(24, actions.Count); i++) {
                var action = actions[i]; if (action?.DescBase == null || action.DescBase.IsTerminated()) continue;
                if (action.TryCast<PlayerBotActionRevive>() != null || action.TryCast<PlayerBotActionDeploySentryGun>() != null
                    || action.TryCast<PlayerBotActionDeployTripMine>() != null || action.TryCast<PlayerBotActionGatherDeployables>() != null
                    || action.TryCast<PlayerBotActionUnlock>() != null) return true;
            }
            return false;
        }
        static bool CanUse(PlayerAIBot bot, PlayerBotActionShareResourcePack.Descriptor desc, bool command)
        {
            if (!Host || bot?.Agent?.Owner?.IsBot != true || !bot.Agent.Alive || desc?.Bot?.Pointer != bot.Pointer
                || desc.Receiver?.Owner == null || !desc.Receiver.Alive || bot.Agent.DimensionIndex != desc.Receiver.DimensionIndex
                || !Held(bot, out var item, out _) || item.Pointer != desc.Item?.Pointer) return false;
            var kind = Kind(item); if (kind == SupplyKind.Unknown || Protected(bot) || bot.Agent.Damage?.GrabbedAtAll == true) return false;
            // Match the native root action's destination danger check, including for manual requests.
            if (!bot.TestDangerZones(desc.Receiver.Position, AIDangerZone.SeverityEnum.Harmful, out _)) return false;
            bool selfHeal = kind == SupplyKind.Health && desc.Receiver.Pointer == bot.Agent.Pointer;
            if (!selfHeal && bot.Agent.Damage?.HealthMax > 0 && bot.Agent.Damage.Health / bot.Agent.Damage.HealthMax <= .2f) return false;
            // Ordinary automatic use does not take a bot away from a scan or explicit task.
            if (!command && !BotTaskBridge.AutoAvailable(bot)) return false;
            var key = (desc.Receiver.Owner.Pointer, kind);
            if (cooldowns.TryGetValue(key, out var until) && Time.time < until) return false;
            foreach (var active in grants.Values) {
                if (active.Desc.Pointer == desc.Pointer && active.Bot.Pointer == bot.Pointer) continue;
                if (Identity(active) && Time.time <= active.Created + 45 && active.ReceiverOwner == key.Item1 && active.Kind == kind) return false;
            }
            return ResourceUsePolicy.Allowed(Mode(bot.Agent), kind, Need(desc.Receiver, kind), command, BotTaskBridge.Unsafe(bot),
                Setting(health, .25f), Setting(ammo, .08f), Setting(tool, .05f), Setting(infection, .70f));
        }
        static bool SelectionAllowed(PlayerAIBot bot, PlayerBotActionShareResourcePack.Descriptor desc)
        {
            if (desc?.Receiver?.Owner == null || bot?.Agent?.Owner == null || desc.Item == null) return false;
            var mode = Mode(bot.Agent);
            if (selectionCache.TryGetValue(bot.Pointer, out var entry) && entry.Bot == bot.Pointer && entry.Owner == bot.Agent.Owner.Pointer
                && entry.Receiver == desc.Receiver.Pointer && entry.ReceiverOwner == desc.Receiver.Owner.Pointer && entry.Item == desc.Item.Pointer
                && entry.Mode == mode && Time.time >= entry.At && Time.time < entry.At + .25f) return entry.Allowed;
            if (selectionCache.Count > 4) selectionCache.Clear();
            var allowed = CanUse(bot, desc, false);
            selectionCache[bot.Pointer] = new() { Bot = bot.Pointer, Owner = bot.Agent.Owner.Pointer, Receiver = desc.Receiver.Pointer,
                ReceiverOwner = desc.Receiver.Owner.Pointer, Item = desc.Item.Pointer, Mode = mode, At = Time.time, Allowed = allowed };
            return allowed;
        }
        static bool BeforeSelection(RootPlayerBotAction __instance, ref PlayerBotActionBase.Descriptor __0, out PlayerBotActionBase.Descriptor __state)
        {
            __state = __0;
            return !Host || __instance?.m_bot?.Agent == null || Mode(__instance.m_bot.Agent) != ResourceUseMode.CommandsOnly;
        }
        static void AfterSelection(RootPlayerBotAction __instance, ref PlayerBotActionBase.Descriptor __0, PlayerBotActionBase.Descriptor __state)
        {
            if (!Host || __instance?.m_bot?.Agent == null || __0 == null || __0.Pointer != __instance.m_shareResourceAction?.Pointer) return;
            try {
                if (Mode(__instance.m_bot.Agent) == ResourceUseMode.Original || SelectionAllowed(__instance.m_bot, __instance.m_shareResourceAction)) return;
                __0 = __state?.Pointer == __instance.m_shareResourceAction?.Pointer ? null : __state;
                // Preserve the competing native action; do not erase scan/combat candidates.
            } catch (Exception error) { __0 = __state?.Pointer == __instance.m_shareResourceAction?.Pointer ? null : __state; Warn(error); }
        }
        static bool Request(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0, ref bool __result)
        {
            if (!Host || __instance?.Agent?.Owner?.IsBot != true) return true;
            var share = __0?.TryCast<PlayerBotActionShareResourcePack.Descriptor>(); if (share == null) return true;
            try {
                if (grants.TryGetValue(__instance.Pointer, out var old) && old.Desc.Pointer == share.Pointer && Identity(old)) {
                    if (old.Accepted && !share.IsTerminated()) { __result = true; return false; }
                    if (CanUse(__instance, share, old.Command)) return true;
                    __result = false; return false;
                }
                if (Mode(__instance.Agent) == ResourceUseMode.Original) return true;
                if (grants.ContainsKey(__instance.Pointer) || !CanUse(__instance, share, false)) { __result = false; return false; }
                var grant = MakeGrant(__instance, share, false); grant.Requested = true;
                grants.Add(__instance.Pointer, grant); return true;
            } catch (Exception error) { Warn(error); __result = false; return false; }
        }
        static Grant MakeGrant(PlayerAIBot bot, PlayerBotActionShareResourcePack.Descriptor desc, bool command)
        {
            return new() { Bot = bot, Desc = desc, Owner = bot.Agent.Owner.Pointer, ReceiverOwner = desc.Receiver.Owner.Pointer,
                Item = desc.Item.Pointer, Kind = Kind(desc.Item), Created = Time.time, Command = command,
                Floor = LevelGeneration.Builder.CurrentFloor?.Pointer ?? IntPtr.Zero,
                Slot = bot.Agent.PlayerSlotIndex, BotName = bot.Agent.PlayerName, ReceiverName = desc.Receiver.PlayerName };
        }
        static void RequestResult(PlayerAIBot __instance, PlayerBotActionBase.Descriptor __0, bool __result)
        {
            if (__instance == null || __0 == null || !grants.TryGetValue(__instance.Pointer, out var grant) || grant.Desc.Pointer != __0.Pointer) return;
            grant.Accepted = __result;
            if (!__result) Finish(grant, false, "原版拒绝补给动作，资源未授权消耗");
        }
        static bool Refresh(Grant grant, bool applying)
        {
            if (!Identity(grant) || Time.time < grant.Created || Time.time > grant.Created + 45) return false;
            if (applying) return grant.Accepted && !grant.Applied; // Complete this single timed use, never extend the grant.
            if (grant.Cancelled || grant.Command && !BotTaskBridge.CanExecute(grant.Bot, grant.Token)) return false;
            if (Time.time < grant.NextCheck && Time.time >= grant.CheckedAt) return grant.Allowed;
            grant.CheckedAt = Time.time; grant.NextCheck = Time.time + .25f;
            return grant.Allowed = CanUse(grant.Bot, grant.Desc, grant.Command);
        }
        static bool UpdateShare(PlayerBotActionShareResourcePack __instance, ref bool __result)
        {
            if (!Host || __instance?.m_bot?.Agent?.Owner?.IsBot != true) return true;
            try {
                var bot = __instance.m_bot;
                if (!grants.TryGetValue(bot.Pointer, out var grant) || grant.Desc.Pointer != __instance.m_desc?.Pointer) {
                    if (Mode(bot.Agent) == ResourceUseMode.Original) return true;
                } else if (Refresh(grant, __instance.m_state == PlayerBotActionShareResourcePack.State.Apply)) return true;
                Stop(bot, __instance.m_desc);
                __result = false; return false;
            } catch (Exception error) { Warn(error); __result = false; return false; }
        }
        static void Stop(PlayerAIBot bot, PlayerBotActionBase.Descriptor desc)
        {
            if (bot?.Agent?.Owner == null || desc == null || desc.IsTerminated()) return;
            if (stopping.TryGetValue(desc.Pointer, out var prior) && prior.Owner == bot.Agent.Owner.Pointer
                && Time.time >= prior.At && Time.time < prior.At + .5f) return;
            if (stopping.Count > 32) stopping.Clear();
            stopping[desc.Pointer] = (bot.Agent.Owner.Pointer, Time.time); bot.StopAction(desc);
        }
        static bool AllowDangerCancellation(object __instance)
        {
            if (!Host) return true;
            try {
                var bot = recorderBot.GetValue(__instance) as PlayerAIBot;
                if (bot == null || !grants.TryGetValue(bot.Pointer, out var grant) || !grant.Accepted) return true;
                // This managed method cancels only the recorder's root descriptor, never a different command action.
                var root = recorderRoot.GetValue(__instance) as RootPlayerBotAction;
                if (root?.m_shareResourceAction?.Pointer != grant.Desc.Pointer) return true;
                var action = grant.Desc.ActionBase?.TryCast<PlayerBotActionShareResourcePack>();
                return !Refresh(grant, action?.m_state == PlayerBotActionShareResourcePack.State.Apply);
            } catch (Exception error) { Warn(error); return true; }
        }
        static bool BeforeApply(PlayerAgent __0, PlayerAgent __1, ItemEquippable __2, out float __state)
        {
            __state = -1;
            if (ToolSupplyAdapter.Authorizes(__0,__1,__2)) return true;
            if (!Host || __0?.Owner?.IsBot != true) return true;
            try {
                var bot = __0.GetComponent<PlayerAIBot>();
                if (bot == null) return false;
                if (!grants.TryGetValue(bot.Pointer, out var grant)) return Mode(__0) == ResourceUseMode.Original;
                var action = grant.Desc.ActionBase?.TryCast<PlayerBotActionShareResourcePack>();
                if (grant.Desc.Receiver?.Pointer != __1?.Pointer || grant.Item != __2?.Pointer || action?.m_state != PlayerBotActionShareResourcePack.State.Apply
                    || !Refresh(grant, true) || !Need(__1, grant.Kind).Needed
                    || !Held(bot, out var item, out var amount) || item.Pointer != grant.Item || grant.Applied) return false;
                __state = amount;
                return true;
            } catch (Exception error) { Warn(error); return false; }
        }
        static void AfterApply(PlayerAgent __0, PlayerAgent __1, ItemEquippable __2, float __state)
        {
            if (__state < 0 || __0 == null) return;
            try {
                var bot = __0.GetComponent<PlayerAIBot>();
                if (bot == null || !grants.TryGetValue(bot.Pointer, out var grant)) return;
                Held(bot, out var item, out var after);
                grant.Applied = item == null || item.Pointer != grant.Item || after < __state - .001f;
                if (grant.Applied) {
                    cooldowns[(grant.ReceiverOwner, grant.Kind)] = Time.time + Setting(cooldown, 3, 1, 15);
                    ItemTaskAdapter.Feedback(__0.PlayerName + " → " + __1.PlayerName + "：已完成一次补给；资源包余量 " + after.ToString("0.#"));
                }
            } catch (Exception error) { Warn(error); }
        }
        public static void GiveOnce(PlayerAIBot bot, PlayerAgent receiver, long expectedItem = 0)
        {
            if (!Host || bot?.Agent?.Owner?.IsBot != true || receiver?.Owner == null) return;
            if (grants.ContainsKey(bot.Pointer)) { ItemTaskAdapter.Feedback("该机器人已有一次补给，重复命令不增加次数"); return; }
            if (!Held(bot, out var item, out _)) { ItemTaskAdapter.Feedback("该机器人没有可用资源包"); return; }
            if (expectedItem != 0 && item.Pointer.ToInt64() != expectedItem) { ItemTaskAdapter.Feedback("机器人携带的资源包已改变，请重开菜单确认"); return; }
            var desc = new PlayerBotActionShareResourcePack.Descriptor(bot) { Receiver = receiver, Item = item, Haste = .5f, Prio = 100000f };
            if (!CanUse(bot, desc, true)) { ItemTaskAdapter.Feedback("暂不能补给：目标不需要此资源、正在交互／救援、自身濒危或同类补给已预约"); return; }
            var grant = MakeGrant(bot, desc, true);
            if (!BotTaskBridge.Acquire(bot, out grant.Token)) { ItemTaskAdapter.Feedback("机器人当前任务尚不能让出权限，请稍后重试"); return; }
            grants.Add(bot.Pointer, grant); Observe(grant, TaskBoardState.Waiting, "等待权限", "一次补给；不重复询问、不传送");
            ItemTaskAdapter.Feedback(bot.Agent.PlayerName + "：已登记给 " + receiver.PlayerName + " 使用资源包一次");
        }
        static void Observe(Grant grant, TaskBoardState state, string stage, string reason)
        {
            if (!grant.Command) return;
            TaskBoard.Journal.Record(grant.Report, grant.Slot, grant.Owner.ToInt64(), grant.BotName,
                "指定补给一次", grant.ReceiverName, state, stage, reason, Time.time);
        }
        static void Finish(Grant grant, bool success, string reason)
        {
            if (!success && grant.Accepted) {
                try { Stop(grant.Bot, grant.Desc); } catch (Exception error) { Warn(error); }
            }
            grants.Remove(grant.Bot.Pointer);
            if (grant.Token != 0) {
                try { BotTaskBridge.Release(grant.Bot, grant.Token, success ? ItemTaskStage.Complete : grant.Cancelled ? ItemTaskStage.Cancelled : ItemTaskStage.Failed); }
                catch (Exception error) { Warn(error); }
            }
            Observe(grant, success ? TaskBoardState.Complete : grant.Cancelled ? TaskBoardState.Cancelled : TaskBoardState.Failed, success ? "已核验消耗" : "已结束", reason);
            if (grant.Command && !success) ItemTaskAdapter.Feedback(grant.BotName + "：" + reason);
            if (grant.Return && Identity(grant)) ItemTaskAdapter.ReturnAfterSupply(grant.Bot);
        }
        public static bool Cancel(PlayerAIBot bot, bool returnToTask = false)
        {
            if (bot == null || !grants.TryGetValue(bot.Pointer, out var grant)) return false;
            grant.Cancelled = true; grant.Return |= returnToTask; grant.NextCheck = 0;
            ItemTaskAdapter.Feedback("补给取消已登记；已开始的单次使用先核验，不撤销已发生的消耗"); return true;
        }
        public static void Tick()
        {
            if (!Host) return;
            stale.Clear();
            foreach (var pair in grants) stale.Add(pair.Key);
            foreach (var key in stale) {
                if (!grants.TryGetValue(key, out var grant)) continue;
                try {
                    if (!Identity(grant) || Time.time < grant.Created || Time.time > grant.Created + 45) {
                        if (grant.Accepted) Stop(grant.Bot, grant.Desc);
                        Finish(grant, false, "补给对象、机器人身份失效或45秒超时"); continue;
                    }
                    if (!grant.Requested) {
                        if (grant.Cancelled || Time.time > grant.Created + 15) { Finish(grant, false, "补给等待已取消或15秒超时"); continue; }
                        if (!BotTaskBridge.CanExecute(grant.Bot, grant.Token)) continue;
                        if (!CanUse(grant.Bot, grant.Desc, true)) { Finish(grant, false, "等待后条件已经改变，未使用资源"); continue; }
                        if (!BotTaskBridge.Report(grant.Bot, grant.Token, ItemTaskStage.Moving)) { Finish(grant, false, "仲裁权限已失效，未提交补给动作"); continue; }
                        grant.Requested = true;
                        if (!grant.Bot.RequestAction(grant.Desc)) continue; // Rejection postfix owns cleanup.
                    }
                    if (grant.Desc.IsTerminated()) { Finish(grant, grant.Applied, grant.Applied ? "一次实际消耗已核验" : "动作结束但未核验资源消耗"); continue; }
                    var action = grant.Desc.ActionBase?.TryCast<PlayerBotActionShareResourcePack>();
                    bool applying = action?.m_state == PlayerBotActionShareResourcePack.State.Apply;
                    if (grant.Command) {
                        if (!BotTaskBridge.CanExecute(grant.Bot, grant.Token) && !applying) { grant.Cancelled = true; grant.NextCheck = 0; }
                        if (!BotTaskBridge.Report(grant.Bot, grant.Token, applying ? ItemTaskStage.Interacting : ItemTaskStage.Moving)) { grant.Cancelled = true; grant.NextCheck = 0; }
                        Observe(grant, TaskBoardState.Executing, applying ? "使用资源包" : "接近队友", "原版补给动作；一次使用后重新评估");
                    }
                    if (!Refresh(grant, applying)) Stop(grant.Bot, grant.Desc);
                } catch (Exception error) { Warn(error); Finish(grant, false, "补给核验异常，停止本次请求"); }
            }
            if (Time.time >= pruneAt) {
                pruneAt = Time.time + 5;
                var expired = new List<(IntPtr, SupplyKind)>();
                foreach (var pair in cooldowns) if (Time.time >= pair.Value || Time.time < pair.Value - 15) expired.Add(pair.Key);
                foreach (var key in expired) cooldowns.Remove(key);
                stale.Clear(); foreach (var pair in stopping) if (Time.time >= pair.Value.At + .5f || Time.time < pair.Value.At) stale.Add(pair.Key);
                foreach (var key in stale) stopping.Remove(key);
                stale.Clear(); foreach (var pair in modes) {
                    bool found = false;
                    for (int slot = 0; slot < 4; slot++) { var agent = PlayerManager.Current?.GetPlayerAgentInSlot(slot); if (agent?.Pointer == pair.Key && agent.Owner?.Pointer == pair.Value.Owner && agent.Owner.IsBot) found = true; }
                    if (!found) stale.Add(pair.Key);
                }
                foreach (var key in stale) modes.Remove(key);
            }
        }
        public static void Report(PlayerAgent agent)
        {
            var bot = agent.GetComponent<PlayerAIBot>(); string result = agent.PlayerName + "；策略：" + ModeText(agent);
            foreach (var slot in new[] { InventorySlot.ResourcePack, InventorySlot.Consumable, InventorySlot.ConsumableHeavy, InventorySlot.InLevelCarry }) {
                if (!ReadCarried(bot, slot, out var item, out var amount)) continue;
                result += "；" + (slot switch {
                    InventorySlot.ResourcePack => "资源包：", InventorySlot.Consumable => "普通消耗品：", InventorySlot.ConsumableHeavy => "重型消耗品：", _ => "任务携带：" });
                result += (Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName)
                    + (slot == InventorySlot.ResourcePack || slot == InventorySlot.Consumable ? "，余量 " + amount.ToString("0.#") : "（单件携带）");
            }
            ItemTaskAdapter.Feedback(result + "（实际槽位；余量不是统一使用次数）");
        }
        public static void Reset()
        {
            foreach (var grant in grants.Values) {
                try { if (grant.Accepted && !grant.Desc.IsTerminated()) grant.Bot.StopAction(grant.Desc); if (grant.Token != 0) BotTaskBridge.Release(grant.Bot, grant.Token, ItemTaskStage.Cancelled); }
                catch (Exception error) { Warn(error); }
            }
            grants.Clear(); modes.Clear(); cooldowns.Clear(); stopping.Clear(); selectionCache.Clear(); stale.Clear(); pruneAt = warningAt = 0;
        }
    }
}

