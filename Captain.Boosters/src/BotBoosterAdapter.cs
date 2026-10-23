using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BoosterImplants;
using CellMenu;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SNetwork;
using UnityEngine;

namespace PreLuna.Captain.Boosters
{
    internal static class BotBoosterAdapter
    {
        private sealed class Applied
        {
            public SNet_Player Owner;
            public PlayerBoosterImplantState State;
            public IntPtr Manager, Agent;
            public pBoosterImplantData[] Before, Ours;
            public string Key;
            public bool EnabledBefore;
            public int NativeUpdatedFrame = -1;
        }
        private sealed class Popup
        {
            public CM_PlayerLobbyBar Bar;
            public SNet_Player Owner;
            public int Slot, Category;
            public IntPtr Window;
            public readonly HashSet<IntPtr> Cards = new();
            public bool Closed;
        }

        private static readonly BoosterSelection selections = new();
        private static readonly Applied[] applied = new Applied[4];
        private static readonly List<BoosterChoice> catalog = new();
        private static readonly Dictionary<IntPtr, long> lastBotWindows = new();
        private static readonly Dictionary<IntPtr, Popup> callbackOwners = new();
        private static readonly Dictionary<IntPtr, Popup> cardOwners = new();
        private static readonly float[] retryAt = new float[4];
        private static readonly string[] retryKey = new string[4];
        private static readonly int[] failures = new int[4];
        private static readonly bool[] suspended = new bool[4];
        private static readonly IntPtr[] failureOwners = new IntPtr[4];
        private static readonly Dictionary<IntPtr, string> renderedBars = new();
        private sealed class SlotRender { public long Owner; public string Fingerprint; public IntPtr Data; }
        private static readonly Dictionary<IntPtr, SlotRender> renderedSlots = new();
        private static bool runtimeUnavailable;
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<bool> showHud;
        private static ConfigEntry<bool> viewDiagnostics;
        private static readonly string[] observed = new string[4];
        private static float hudRetryAt;
        private static Harmony patches;
        private static Popup popup;
        private static int actionDepth, renderDepth;
        private static float nextTick, nextWarning, nextCatalog;
        private static bool installed, wasMaster;
        private static string savePath;

        public static void Configure(ConfigFile config)
        {
            enabled = config.Bind("机器人强化剂", "启用", true,
                "开发中的机器人虚拟装配：复用主机当前强化剂列表和原版三槽，不消耗账号库存。仅房主在大厅操作，修改后重启。");
            showHud = config.Bind("机器人强化剂", "显示对局状态", true, "房主对局 HUD 显示机器人原版同步及条件状态；保存的选择不代表已生效。");
            viewDiagnostics = config.Bind("机器人强化剂", "界面状态诊断", true, "只在界面状态变化／恢复隐藏控件时记录日志，最多每条机器人栏每2秒一条；验证完成可关闭。");
            savePath = Path.Combine(Paths.ConfigPath, "PreLuna.Captain.Boosters.json");
            if (!File.Exists(savePath)) return;
            try { selections.Import(JsonSerializer.Deserialize<SelectionFile>(File.ReadAllText(savePath))); }
            catch (Exception ex) { Warn("机器人装备记录无法读取，保持空槽：" + ex.Message); }
        }

        // Install the whole transaction guard together. Never expose a bot popup with only half its guards.
        public static void Install()
        {
            if (!enabled.Value) return;
            patches = new Harmony(Plugin.Guid + ".boosters");
            var plans = new List<(MethodInfo target, string before, string after, string finalizer)>();
            void Add(Type type, string name, Type[] args, string before = null, string after = null, string finalizer = null)
            {
                var method = AccessTools.Method(type, name, args);
                if (method == null) throw new MissingMethodException(type.FullName, name);
                plans.Add((method, before, after, finalizer));
            }
            try
            {
                Add(typeof(CM_PlayerLobbyBar), "UpdateBoosterImplantInventory", Type.EmptyTypes, after: nameof(BarEventPostfix));
                Add(typeof(CM_PlayerLobbyBar), "UpdatePlayer", new[] { typeof(SNet_Player) }, after: nameof(BarEventPostfix));
                Add(typeof(CM_PlayerLobbyBar), "SetHasPlayer", new[] { typeof(bool), typeof(bool) }, nameof(LayoutEventPrefix), nameof(BarEventPostfix));
                Add(typeof(CM_PlayerLobbyBar), "HideLoadoutUI", new[] { typeof(bool), typeof(bool) }, nameof(LayoutEventPrefix), nameof(BarEventPostfix));
                Add(typeof(CM_PlayerLobbyBar), "OnEnable", Type.EmptyTypes, after: nameof(BarEventPostfix));
                Add(typeof(CM_PageLoadout), "UpdateReadyState", Type.EmptyTypes, after: nameof(PageEventPostfix));
                Add(typeof(CM_PageLoadout), "SetPageActive", new[] { typeof(bool) }, after: nameof(PageEventPostfix));
                // Do not detour OnSetGuiLayerVisible: its false branch hit native
                // padding in the installed runtime (0.4.8 dump, RVA 0x1251916).
                // SetPageActive, bar events and the .25s refresh cover display restoration.
                Add(typeof(CM_BoosterImplantSlot), "OnBtnPress", new[] { typeof(iCellMenuInputHandler) }, nameof(SlotClickPrefix));
                Add(typeof(CM_PlayerLobbyBar), "ShowBoosterImplantSelect", new[] { typeof(BoosterImplantCategory), typeof(SNet_Player) }, nameof(OpenPlayerPrefix));
                Add(typeof(CM_PlayerLobbyBar), "ShowBoosterImplantTeamView", new[] { typeof(BoosterImplantCategory), typeof(SNet_Player) }, nameof(OpenPlayerPrefix));
                Add(typeof(CM_PlayerLobbyBar), "ShowBoosterImplantSelect", new[] { typeof(BoosterImplantCategory), typeof(int) }, nameof(OpenIndexPrefix), nameof(RenderPostfix), nameof(RenderFinalizer));
                Add(typeof(CM_PlayerLobbyBar), "UpdateBoosterSelectionPoup", new[] { typeof(BoosterImplantCategory), typeof(int) }, nameof(RenderPrefix), nameof(RenderPostfix), nameof(RenderFinalizer));
                Add(typeof(CM_PlayerLobbyBar), "UpdateBoosterImplantContentItem", new[] { typeof(CM_BoosterImplantSlotItem) }, nameof(CardRenderPrefix), nameof(RenderPostfix), nameof(RenderFinalizer));
                Add(typeof(CM_BoosterImplantSlot), "UpdateSlot", new[] { typeof(SNet_Player) }, after: nameof(SlotPostfix));
                Add(typeof(CM_BoosterImplantSlotItem), "LoadData", new[] { typeof(BoosterImplantInventoryItem) }, after: nameof(CardPostfix));
                Add(typeof(CM_BoosterImplantSlotItem), "LoadData", new[] { typeof(BoosterImplant), typeof(bool) }, after: nameof(CardPostfix));
                Add(typeof(CM_BoosterImplantSlotItem), "PrepareBoosterImplant", Type.EmptyTypes, nameof(PrepareCardPrefix));
                Add(typeof(CM_BoosterImplantSlotItem), "UnprepareBoosterImplant", Type.EmptyTypes, nameof(UnprepareCardPrefix));
                Add(typeof(CM_BoosterImplantSlotItem), "DropBoosterImplant", Type.EmptyTypes, nameof(UnprepareCardPrefix));
                Add(typeof(CM_ScrollWindow), "SetVisible", new[] { typeof(bool) }, after: nameof(WindowPostfix));
                Add(typeof(PersistentInventoryManager), "PrepareBoosterImplantForInjection", new[] { typeof(uint) }, nameof(PrepareInventoryPrefix));
                Add(typeof(PersistentInventoryManager), "UnPrepareBoosterImplant", new[] { typeof(BoosterImplantCategory) }, nameof(UnprepareInventoryPrefix));
                Add(typeof(PersistentInventoryManager), "UnPrepareAllBoosterImplants", Type.EmptyTypes, nameof(UnprepareAllPrefix));
                Add(typeof(PersistentInventoryManager), "UnprepareAllBoosters", Type.EmptyTypes, nameof(UnprepareAllPrefix));
                Add(typeof(PersistentInventoryManager), "DropBoosterImplant", new[] { typeof(uint) }, nameof(DropInventoryPrefix));
                Add(typeof(BoosterImplantManager), "Update", Type.EmptyTypes, after: nameof(ManagerPostfix));
                Add(typeof(BoosterImplantManager), "SetupBoosterManager", Type.EmptyTypes, nameof(ResetManagerPrefix));
                Add(typeof(BoosterImplantManager), "OnLevelCleanup", Type.EmptyTypes, nameof(CleanupPrefix));
                Add(typeof(BoosterImplantManager), "UpdateBoosterPlayerSlotMapping", new[] { typeof(int) }, nameof(MappingPrefix));
                Add(typeof(PlayerBoosterImplantState), "Update", Type.EmptyTypes, after: nameof(StatePostfix));
                Add(typeof(GameStateManager), "ChangeState", new[] { typeof(eGameStateName) }, nameof(ChangeStatePrefix));
                Add(typeof(SNet_PlayerSlotManager), "OnResetSession", Type.EmptyTypes, nameof(SessionPrefix));
                // Stock popup buttons call PersistentInventoryManager directly from these callbacks.
                // The scope is local to those buttons, not every inventory operation while a popup is open.
                var closure = typeof(CM_PlayerLobbyBar).GetNestedType("__c__DisplayClass141_0", BindingFlags.Public | BindingFlags.NonPublic);
                if (closure == null || AccessTools.Property(closure, "__4__this") == null)
                    throw new MissingMemberException("原版强化剂弹窗的按钮拥有者接口已变化。");
                for (int i = 0; i < 5; i++)
                    Add(closure, "_ShowBoosterImplantSelect_b__" + i, new[] { typeof(int) }, nameof(CallbackPrefix), nameof(CallbackPostfix), nameof(CallbackFinalizer));
                foreach (var plan in plans)
                    patches.Patch(plan.target,
                        prefix: plan.before == null ? null : new HarmonyMethod(typeof(BotBoosterAdapter), plan.before),
                        postfix: plan.after == null ? null : new HarmonyMethod(typeof(BotBoosterAdapter), plan.after) {
                            priority = plan.after == nameof(BarEventPostfix) || plan.after == nameof(PageEventPostfix) || plan.after == nameof(SlotPostfix)
                                ? Priority.Last : Priority.Normal },
                        finalizer: plan.finalizer == null ? null : new HarmonyMethod(typeof(BotBoosterAdapter), plan.finalizer));
                installed = true;
                Plugin.Logger.LogInfo("机器人强化剂开发补丁已注册：" + plans.Count + " 项；使用原版同步与条件流程，不扣库存。");
            }
            catch (Exception ex)
            {
                patches.UnpatchSelf();
                installed = false;
                Plugin.Logger.LogError("机器人强化剂补丁未完整注册，整组已撤销：" + ex.Message);
            }
        }

        private static bool Host => installed && enabled.Value && SNet.IsMaster;
        private static bool Lobby => GameStateManager.CurrentStateName == eGameStateName.Lobby;
        private static bool ViewState => Lobby || GameStateManager.CurrentStateName == eGameStateName.InLevel;
        private static bool CanEdit(SNet_Player player) => Host && Lobby && !GameStateManager.IsReady
            && !selections.IsFrozen && CurrentBot(player);
        private static bool CanView(CM_PlayerLobbyBar bar)
        {
            try { return Host && ViewState && CurrentBot(bar?.m_player) && BotBoosterView.PageVisible(bar); }
            catch (Exception error) { Warn("机器人强化剂页面读取延后：" + error.Message); return false; }
        }
        private static BoosterChoice DisplayChoice(SNet_Player player, int category)
            => category >= 0 && category < 3 ? selections.Get(Slot(player), Owner(player), catalog)[category] : null;
        private static bool Bot(SNet_Player player) => player != null && player.IsBot && player.PlayerSlot != null;
        private static int Slot(SNet_Player player) => player?.PlayerSlot?.index ?? -1;
        private static long Owner(SNet_Player player) => player == null ? 0 : player.Pointer.ToInt64();
        private static bool CurrentBot(SNet_Player player)
        {
            int slot = Slot(player);
            return Bot(player) && slot >= 0 && slot < 4 && SNet.Slots.GetPlayerInSlot(slot)?.Pointer == player.Pointer;
        }
        private static void BindBots()
        {
            for (int slot = 0; slot < 4; slot++)
            {
                var player = SNet.Slots.GetPlayerInSlot(slot);
                if (player == null) { selections.Bind(slot, 0, false); continue; }
                bool changedOwner = failureOwners[slot] != player.Pointer;
                if (changedOwner) { ResetRetry(slot); failureOwners[slot] = player.Pointer; }
                selections.Bind(slot, Owner(player), Bot(player));
                if (changedOwner && selections.IsFrozen && Bot(player)) Plugin.Logger.LogInfo("[强化剂生命周期] slot=" + slot
                    + " newOwner=" + Owner(player) + " 本局冻结装备接续="
                    + string.Join("/", Array.ConvertAll(selections.Get(slot, Owner(player), catalog), x => x?.PacketId.ToString() ?? "empty")));
            }
        }
        private static void Warn(string message)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextWarning) return;
            nextWarning = now + 5;
            Plugin.Logger.LogWarning(message);
        }

        private static void RefreshCatalog()
        {
            nextCatalog = UnityEngine.Time.realtimeSinceStartup + 2;
            selections.RestoreLegacyConditionIdentity = BoosterConditionCompat.RawConditionsPreserved;
            catalog.Clear();
            var inventory = PersistentInventoryManager.Current;
            if (inventory == null) return;
            for (int category = 0; category < 3; category++)
            {
                var items = PersistentInventoryManager.GetBoosterImplantInventory((BoosterImplantCategory)category);
                if (items == null) continue;
                foreach (var item in items)
                {
                    try
                    {
                        var implant = item?.Implant;
                        if (implant == null || implant.Uses <= 0 || (int)implant.Category != category) continue;
                        if (implant.Effects == null || implant.Conditions == null
                            || implant.Effects.Length > pBoosterImplantData.IMPLANT_EFFECT_SYNC_COUNT
                            || implant.Conditions.Length > pBoosterImplantData.IMPLANT_CONDITION_SYNC_COUNT)
                            throw new ArgumentException("自定义强化剂超过原版网络容量，不能截断后给机器人装备。");
                        var packet = BoosterImplantManager.ActiveBoosterImplantToData(implant);
                        if (packet.BoosterEffectCount != implant.Effects.Length || packet.ConditionCount != implant.Conditions.Length)
                            throw new ArgumentException("原版转换未保留全部效果／条件。");
                        var effects = new EffectValue[packet.BoosterEffectCount];
                        var conditions = new uint[packet.ConditionCount];
                        for (int i = 0; i < effects.Length; i++)
                        {
                            var effect = packet.BoosterEffectDatas[i];
                            if (GameData.BoosterImplantEffectDataBlock.GetBlock(effect.BoosterEffectID) == null)
                                throw new ArgumentException("强化剂效果定义不存在。");
                            effects[i] = new EffectValue(effect.BoosterEffectID, effect.EffectValue);
                        }
                        for (int i = 0; i < conditions.Length; i++)
                        {
                            conditions[i] = packet.Conditions[i];
                            if (GameData.BoosterImplantConditionDataBlock.GetBlock(conditions[i]) == null)
                                throw new ArgumentException("强化剂条件定义不存在。");
                        }
                        catalog.Add(new BoosterChoice(implant.InstanceId, implant.TemplateId, packet.BoosterImplantID,
                            category, implant.Uses, effects, conditions,
                            pBoosterImplantData.IMPLANT_EFFECT_SYNC_COUNT, pBoosterImplantData.IMPLANT_CONDITION_SYNC_COUNT));
                    }
                    catch (Exception ex) { Warn("已跳过不能安全同步的强化剂：" + ex.Message); }
                }
            }
        }

        private static pBoosterImplantData ToData(BoosterChoice choice)
        {
            var result = new pBoosterImplantData {
                BoosterImplantID = choice?.PacketId ?? 0,
                BoosterEffectDatas = new Il2CppStructArray<pBoosterEffectData>(pBoosterImplantData.IMPLANT_EFFECT_SYNC_COUNT),
                Conditions = new Il2CppStructArray<uint>(pBoosterImplantData.IMPLANT_CONDITION_SYNC_COUNT),
                UseCount = choice?.Uses ?? 0
            };
            if (choice == null) return result;
            var effects = choice.CopyEffects(); var conditions = choice.CopyConditions();
            result.BoosterEffectCount = effects.Length; result.ConditionCount = conditions.Length;
            for (int i = 0; i < effects.Length; i++)
                result.BoosterEffectDatas[i] = new pBoosterEffectData { BoosterEffectID = effects[i].Id, EffectValue = effects[i].Value };
            for (int i = 0; i < conditions.Length; i++) result.Conditions[i] = conditions[i];
            return result;
        }
        private static pBoosterImplantData CopyData(pBoosterImplantData source)
        {
            if (source == null) return ToData(null);
            var copy = ToData(null);
            if (source.BoosterEffectCount < 0 || source.BoosterEffectCount > copy.BoosterEffectDatas.Length
                || source.ConditionCount < 0 || source.ConditionCount > copy.Conditions.Length) throw new ArgumentException("同步数据数量异常。");
            copy.BoosterImplantID = source.BoosterImplantID; copy.UseCount = source.UseCount;
            copy.BoosterEffectCount = source.BoosterEffectCount; copy.ConditionCount = source.ConditionCount;
            for (int i = 0; i < copy.BoosterEffectCount; i++) copy.BoosterEffectDatas[i] = source.BoosterEffectDatas[i];
            for (int i = 0; i < copy.ConditionCount; i++) copy.Conditions[i] = source.Conditions[i];
            return copy;
        }
        private static pBoosterImplantData[] CopyDatas(Il2CppReferenceArray<pBoosterImplantData> values)
        {
            var result = new pBoosterImplantData[3];
            for (int i = 0; i < 3; i++) result[i] = CopyData(values != null && i < values.Length ? values[i] : null);
            return result;
        }
        private static bool Same(pBoosterImplantData left, pBoosterImplantData right)
        {
            if (left == null || right == null) return left == null && right == null;
            if (left.BoosterImplantID != right.BoosterImplantID || left.UseCount != right.UseCount
                || left.BoosterEffectCount != right.BoosterEffectCount || left.ConditionCount != right.ConditionCount) return false;
            for (int i = 0; i < left.BoosterEffectCount; i++)
                if (left.BoosterEffectDatas[i].BoosterEffectID != right.BoosterEffectDatas[i].BoosterEffectID
                    || left.BoosterEffectDatas[i].EffectValue != right.BoosterEffectDatas[i].EffectValue) return false;
            for (int i = 0; i < left.ConditionCount; i++) if (left.Conditions[i] != right.Conditions[i]) return false;
            return true;
        }
        private static bool StillOurs(Applied record)
        {
            var values = record.State?.BoosterImplantDatas;
            if (values == null || values.Length != 3) return false;
            for (int i = 0; i < 3; i++) if (!Same(values[i], record.Ours[i])) return false;
            return true;
        }

        private static void Save()
        {
            try
            {
                string temporary = savePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(selections.Export(), new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, savePath, true);
            }
            catch (Exception ex) { Warn("机器人强化剂装备记录保存失败：" + ex.Message); }
        }
        private static void SetChoice(uint id)
        {
            if (!ValidPopup()) { Warn("机器人强化剂操作已失效，主机库存未改动。"); return; }
            RefreshCatalog();
            var choice = catalog.Find(x => x.SourceId == id && x.Category == popup.Category);
            if (choice == null) { Warn("选择的强化剂已失效或超过同步容量，请重新选择。"); return; }
            if (selections.Choose(popup.Slot, Owner(popup.Owner), Host, true, Lobby, choice))
            {
                ResetRetry(popup.Slot);
                Save(); Apply(popup.Slot, popup.Owner, BoosterImplantManager.Current);
                RefreshBar(popup.Bar);
                Plugin.Logger.LogInfo("机器人 " + popup.Owner.NickName + " 已选择槽位 " + popup.Category + " 的强化剂，来源 ID=" + id + "，不扣库存。");
            }
        }
        private static void ClearChoice(int category)
        {
            if (!ValidPopup()) return;
            if (selections.Unequip(popup.Slot, Owner(popup.Owner), Host, true, Lobby, category))
            {
                ResetRetry(popup.Slot);
                Save(); Apply(popup.Slot, popup.Owner, BoosterImplantManager.Current); RefreshBar(popup.Bar);
            }
        }
        private static void Apply(int slot, SNet_Player owner, BoosterImplantManager manager)
        {
            if (runtimeUnavailable || suspended[slot]) return;
            try { ApplyCore(slot, owner, manager); }
            catch (Exception error) { Failed(slot, "更新", error); }
        }
        private static void ResetRetry(int slot)
        { retryKey[slot] = null; retryAt[slot] = 0; failures[slot] = 0; suspended[slot] = false; }
        private static bool Structural(Exception error)
        {
            for (; error != null; error = error.InnerException)
                if (error is TypeLoadException || error is MissingMethodException || error is InvalidProgramException
                    || error is BadImageFormatException || error is EntryPointNotFoundException) return true;
            return false;
        }
        private static void Failed(int slot, string operation, Exception error)
        {
            if (Structural(error))
            {
                if (runtimeUnavailable) return;
                runtimeUnavailable = true;
                Plugin.Logger.LogError("机器人强化剂原生接口不可用，停止本次进程的自动更新，保留选择且不改变真人库存；更新插件后重启。" + error);
                return;
            }
            failures[slot]++;
            suspended[slot] = failures[slot] >= 5;
            retryAt[slot] = UnityEngine.Time.realtimeSinceStartup + Math.Min(60, 2 * (1 << Math.Min(failures[slot], 5)));
            if (failures[slot] == 1 || suspended[slot])
                Plugin.Logger.LogWarning("机器人槽位 " + (slot + 1) + " 强化剂" + operation + "失败：" + error.Message
                    + (suspended[slot] ? "；已暂停该槽，重新选择装备后可重试。" : "；延长重试间隔。"));
        }
        private static void ApplyCore(int slot, SNet_Player owner, BoosterImplantManager manager)
        {
            if (!Host || !CurrentBot(owner) || manager == null) return;
            var choices = selections.Get(slot, Owner(owner), catalog);
            string key = string.Join("/", Array.ConvertAll(choices, x => x?.Fingerprint ?? "empty"));
            string attempt = Owner(owner) + ":" + key;
            if (retryKey[slot] == attempt && UnityEngine.Time.realtimeSinceStartup < retryAt[slot]) return;
            var previous = applied[slot];
            if (previous != null && (previous.Owner.Pointer != owner.Pointer || previous.Manager != manager.Pointer))
            { Restore(slot); previous = null; }
            bool any = Array.Exists(choices, x => x != null);
            if (previous == null && !any) return;
            if (previous != null && previous.Key == key && StillOurs(previous)) return;
            manager.UpdateBoosterPlayerSlotMapping(slot);
            retryKey[slot] = attempt; retryAt[slot] = UnityEngine.Time.realtimeSinceStartup + 2;
            if (!BoosterImplantManager.TryGetPlayerBoosterImplantState(owner, out var state) || state == null)
            { Failed(slot, "等待原版状态", new InvalidOperationException("原版未提供机器人强化剂状态，保留装备选择。")); return; }
            if (previous != null && previous.State.Pointer != state.Pointer) { Restore(slot); previous = null; }
            if (previous != null && previous.Key == key && StillOurs(previous)) return;
            var values = Array.ConvertAll(choices, ToData);
            // The native manager may keep the objects passed to SyncSet. Keep an independent comparison snapshot.
            var expectedValues = CopyDatas(new Il2CppReferenceArray<pBoosterImplantData>(values));
            // This generated TryGetImplantDatas wrapper contains an invalid generic
            // constructor on the supported installation. Read the same native state's
            // data through its validated getter; never detour or regenerate interop here.
            var original = previous?.Before ?? CopyDatas(state.BoosterImplantDatas);
            bool enabledBefore = previous?.EnabledBefore ?? state.IsEnabled;
            if (!BoosterImplantManager.SyncSetBoosterImplants(owner, new Il2CppReferenceArray<pBoosterImplantData>(values)))
            { Failed(slot, "同步", new InvalidOperationException("原版拒绝同步，未标记为成功应用。")); return; }
            // Native synchronization may queue a state change. Do not invent a second modifier path.
            if (!BoosterImplantManager.TryGetPlayerBoosterImplantState(owner, out state) || state == null) return;
            applied[slot] = new Applied { Owner = owner, Manager = manager.Pointer, State = state,
                Before = original, Ours = expectedValues, Key = key, Agent = state.PlayerAgent?.Pointer ?? IntPtr.Zero,
                EnabledBefore = enabledBefore };
            retryAt[slot] = 0;
            failures[slot] = 0;
        }
        private static void Restore(int slot)
        {
            var record = applied[slot]; applied[slot] = null;
            if (record == null) return;
            try
            {
                // A mapping may reuse the state for a human. Matching packets
                // alone do not grant permission to erase a new owner's state.
                if (record.State.Player != null && record.State.Player.Pointer != record.Owner.Pointer) return;
                if (!StillOurs(record)) return; // Another source already replaced this family of effects.
                if (CurrentBot(record.Owner))
                {
                    // Broadcast restoration only while still the host, and only for the same bot/manager.
                    if (!(SNet.IsMaster && BoosterImplantManager.Current?.Pointer == record.Manager
                        && BoosterImplantManager.SyncSetBoosterImplants(record.Owner, new Il2CppReferenceArray<pBoosterImplantData>(record.Before))))
                        NativeBoosterState.SetDatas(record.State, new Il2CppReferenceArray<pBoosterImplantData>(record.Before));
                }
                else
                    NativeBoosterState.SetDatas(record.State, new Il2CppReferenceArray<pBoosterImplantData>(new[] { ToData(null), ToData(null), ToData(null) }));
                record.State._IsEnabled_k__BackingField = record.EnabledBefore;
            }
            catch (Exception ex) { Warn("旧机器人强化剂状态清理失败，原版关卡清理仍会执行：" + ex.Message); }
        }
        private static void Cleanup(bool forgetBindings, bool thaw = true)
        {
            if (popup?.Bar?.m_popupScrollWindow != null)
            {
                try { popup.Bar.m_popupScrollWindow.SetVisible(false); }
                catch (Exception ex) { Warn("旧机器人装备窗口关闭失败：" + ex.Message); }
            }
            // Keep bounded callback tombstones: late clicks from a closed bot popup must never prepare host boosters.
            popup = null;
            for (int slot = 0; slot < 4; slot++) Restore(slot);
            if (forgetBindings) selections.ForgetBindings(); else if (thaw) selections.Thaw();
            catalog.Clear(); nextTick = 0;
            renderedBars.Clear();
            renderedSlots.Clear(); BotBoosterView.Reset();
            Array.Clear(retryKey, 0, retryKey.Length); Array.Clear(retryAt, 0, retryAt.Length);
            Array.Clear(failures, 0, failures.Length); Array.Clear(suspended, 0, suspended.Length);
            Array.Clear(failureOwners, 0, failureOwners.Length);
            Array.Clear(observed, 0, observed.Length); hudRetryAt = 0;
            BotBoosterHud.Reset(); BotBoosterLayout.Reset();
        }

        private static void DisplayStates()
        {
            if (UnityEngine.Time.realtimeSinceStartup < hudRetryAt) return;
            for (int slot = 0; slot < 4; slot++)
            {
                var owner = SNet.Slots.GetPlayerInSlot(slot);
                if (!Host || !CurrentBot(owner) || GameStateManager.CurrentStateName != eGameStateName.InLevel)
                { BotBoosterHud.Hide(slot); continue; }
                try
                {
                    var choices = selections.Get(slot, Owner(owner), catalog);
                    var record = applied[slot];
                    var values = new BoosterSlotView[3];
                    for (int category = 0; category < 3; category++)
                        values[category] = BoosterViewPolicy.Read(record?.State, category, choices[category] != null, runtimeUnavailable);
                    string stamp = Owner(owner) + ":" + record?.State?.Pointer + ":" + string.Join("/", values);
                    if (observed[slot] != stamp)
                    {
                        observed[slot] = stamp;
                        Plugin.Logger.LogInfo("[机器人强化剂状态] slot=" + slot + " bot=" + owner.NickName + " state=" + record?.State?.Pointer
                            + " frozen=" + selections.IsFrozen + " slots=" + string.Join("/", values));
                    }
                    if (showHud.Value) BotBoosterHud.Show(slot, owner.NickName, values); else BotBoosterHud.Hide(slot);
                }
                catch (Exception error) { hudRetryAt = UnityEngine.Time.realtimeSinceStartup + 10; Warn("机器人强化剂状态显示延后：" + error.Message); break; }
            }
        }

        private static void ManagerPostfix(BoosterImplantManager __instance)
        {
            try
            {
                if (!Host)
                {
                    if (wasMaster) Cleanup(true);
                    wasMaster = false; return;
                }
                wasMaster = true;
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now < nextTick) return;
                nextTick = now + .25f;
                if (runtimeUnavailable) { RefreshVisibleBars(); DisplayStates(); return; }
                BindBots();
                if (Lobby)
                {
                    if (selections.IsFrozen) { Cleanup(false); BindBots(); }
                    if (now >= nextCatalog) RefreshCatalog();
                }
                RefreshVisibleBars();
                for (int slot = 0; slot < 4; slot++)
                {
                    var owner = SNet.Slots.GetPlayerInSlot(slot);
                    if (!CurrentBot(owner)) { Restore(slot); continue; }
                    if (failures[slot] > 0 && now < retryAt[slot]) continue;
                    Apply(slot, owner, __instance);
                    if (runtimeUnavailable) break;
                    try
                    {
                        var record = applied[slot];
                        if (suspended[slot] || record == null || !StillOurs(record) || GameStateManager.CurrentStateName != eGameStateName.InLevel) continue;
                        var agent = owner.PlayerAgent?.TryCast<Player.PlayerAgent>();
                        if (agent == null || agent.Owner?.Pointer != owner.Pointer) continue;
                        record.State.m_playerAgent = agent;
                        record.State._IsEnabled_k__BackingField = true;
                        record.Agent = agent.Pointer;
                        if (record.NativeUpdatedFrame != UnityEngine.Time.frameCount) record.State.Update();
                        failures[slot] = 0;
                    }
                    catch (Exception error) { Failed(slot, "状态更新", error); }
                }
                DisplayStates();
                if (popup != null && !ValidPopup())
                {
                    var window = popup.Bar?.m_popupScrollWindow;
                    if (window != null) window.SetVisible(false);
                    if (actionDepth == 0) popup = null;
                }
            }
            catch (Exception ex)
            {
                if (Structural(ex)) Failed(0, "管理器更新", ex);
                else { nextTick = UnityEngine.Time.realtimeSinceStartup + 5; Warn("机器人强化剂检查失败，延后检查：" + ex.Message); }
            }
        }
        private static void StatePostfix(PlayerBoosterImplantState __instance)
        {
            foreach (var record in applied)
                if (record != null && record.State.Pointer == __instance.Pointer) record.NativeUpdatedFrame = UnityEngine.Time.frameCount;
        }
        private static void ChangeStatePrefix(eGameStateName __0)
        {
            if (!Host || __0 != eGameStateName.Generating || selections.IsFrozen) return;
            try
            {
                BindBots(); RefreshCatalog(); selections.Freeze(catalog); popup = null;
                for (int slot = 0; slot < 4; slot++)
                {
                    var owner = SNet.Slots.GetPlayerInSlot(slot);
                    if (CurrentBot(owner)) Plugin.Logger.LogInfo("[机器人强化剂快照] slot=" + slot + " slots="
                        + string.Join("/", Array.ConvertAll(selections.Get(slot, Owner(owner), catalog), x => x?.Fingerprint ?? "empty")));
                }
            }
            catch (Exception ex) { Warn("本局机器人强化剂快照创建失败：" + ex.Message); }
        }
        private static void MappingPrefix(int __0)
        {
            if (__0 < 0 || __0 >= 4 || applied[__0] == null) return;
            var current = SNet.Slots.GetPlayerInSlot(__0);
            if (current?.Pointer != applied[__0].Owner.Pointer || !Bot(current)) Restore(__0);
        }
        private static void ResetManagerPrefix() { Cleanup(false, false); }
        private static void CleanupPrefix() { Cleanup(false); }
        private static void SessionPrefix() { Cleanup(true); wasMaster = false; }

        private static bool ValidPopup() => popup != null && CanEdit(popup.Owner)
            && CurrentBot(popup.Owner) && popup.Bar != null && popup.Bar.m_player?.Pointer == popup.Owner.Pointer
            && BotBoosterView.PageVisible(popup.Bar)
            && selections.Owns(popup.Slot, Owner(popup.Owner)) && (!popup.Closed || actionDepth > 0);

        private static bool OpenPlayerPrefix(CM_PlayerLobbyBar __instance, BoosterImplantCategory __0, SNet_Player __1)
        {
            if (!Bot(__1)) { popup = null; lastBotWindows.Remove(__instance.Pointer); return true; }
            if (!Host || !CurrentBot(__1)) return true; // Preserve the stock client's read-only team view.
            if (!CanEdit(__1) || !BotBoosterView.PageVisible(__instance)) return false;
            try
            {
                BindBots(); RefreshCatalog();
                var choice = selections.Resolve(Slot(__1), (int)__0, catalog);
                __instance.ShowBoosterImplantSelect(__0, choice == null ? -1 : unchecked((int)choice.SourceId));
            }
            catch (Exception ex) { Warn("机器人强化剂选择窗口打开失败：" + ex.Message); }
            return false;
        }
        private static bool OpenIndexPrefix(CM_PlayerLobbyBar __instance, BoosterImplantCategory __0, ref int __1, out bool __state)
        {
            __state = false;
            if (!Bot(__instance.m_player)) { popup = null; lastBotWindows.Remove(__instance.Pointer); return true; }
            if (!CanEdit(__instance.m_player) || !BotBoosterView.PageVisible(__instance)) return false;
            try
            {
                BindBots(); RefreshCatalog();
                popup = new Popup { Bar = __instance, Owner = __instance.m_player, Slot = Slot(__instance.m_player), Category = (int)__0 };
                lastBotWindows[__instance.Pointer] = Owner(popup.Owner);
                if (lastBotWindows.Count > 16)
                {
                    lastBotWindows.Clear(); lastBotWindows[__instance.Pointer] = Owner(popup.Owner);
                }
                var choice = selections.Resolve(popup.Slot, popup.Category, catalog);
                __1 = choice == null ? -1 : unchecked((int)choice.SourceId);
                renderDepth++; __state = true; return true;
            }
            catch (Exception ex) { Warn("机器人强化剂窗口目标绑定失败：" + ex.Message); return false; }
        }
        private static void RenderPrefix(CM_PlayerLobbyBar __instance, out bool __state)
        {
            __state = ValidPopup() && popup.Bar.Pointer == __instance.Pointer;
            if (__state) renderDepth++;
        }
        private static void CardRenderPrefix(CM_PlayerLobbyBar __instance, CM_BoosterImplantSlotItem __0, out bool __state)
        {
            RenderPrefix(__instance, out __state);
            if (__state && __0 != null) popup.Cards.Add(__0.Pointer);
        }
        private static void RenderPostfix(bool __state)
        {
            if (!__state) return;
            renderDepth = Math.Max(0, renderDepth - 1);
            if (popup?.Bar?.m_popupScrollWindow != null)
            {
                var window = popup.Bar.m_popupScrollWindow;
                popup.Window = window.Pointer;
                if (renderDepth == 0 && window.m_nonContentItems != null)
                {
                    if (callbackOwners.Count > 128) callbackOwners.Clear();
                    foreach (var button in window.m_nonContentItems)
                    {
                        var target = button?.OnBtnPressCallback?.Target;
                        if (target != null) callbackOwners[target.Pointer] = popup;
                        var texts = button?.GetTexts();
                        if (texts == null) continue;
                        foreach (var text in texts)
                        {
                            string label = text?.text?.Trim();
                            if (label == "丢弃" || label == "删除" || label == "销毁" || label == "舍弃"
                                || string.Equals(label, "Discard", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(label, "Drop", StringComparison.OrdinalIgnoreCase))
                                button.SetText("卸下本槽");
                        }
                    }
                }
            }
        }
        private static Exception RenderFinalizer(Exception __exception, bool __state)
        {
            if (__exception != null) RenderPostfix(__state);
            return __exception;
        }
        private static void CardPostfix(CM_BoosterImplantSlotItem __instance)
        {
            if (!ValidPopup() || (renderDepth == 0 && !popup.Cards.Contains(__instance.Pointer)))
            { cardOwners.Remove(__instance.Pointer); return; } // Stock reloading a reused card releases its old bot routing.
            popup.Cards.Add(__instance.Pointer);
            if (cardOwners.Count > 512) cardOwners.Clear();
            cardOwners[__instance.Pointer] = popup;
            var selected = selections.Resolve(popup.Slot, popup.Category, catalog);
            __instance._IsPreparedSlot_k__BackingField = selected != null && selected.SourceId == __instance.BoosterInstanceID;
            // These are view flags on the popup card, not flags in the host inventory model.
            __instance._IsInjectedSlot_k__BackingField = false;
        }
        private static bool PrepareCardPrefix(CM_BoosterImplantSlotItem __instance)
        {
            if (!cardOwners.TryGetValue(__instance.Pointer, out var captured)) return true;
            if (!ReferenceEquals(captured, popup) || !ValidPopup()) return false;
            try { SetChoice(__instance.BoosterInstanceID); }
            catch (Exception ex) { Warn("机器人装备操作失败，未操作主机库存：" + ex.Message); }
            return false;
        }
        private static bool UnprepareCardPrefix(CM_BoosterImplantSlotItem __instance)
        {
            if (!cardOwners.TryGetValue(__instance.Pointer, out var captured)) return true;
            if (!ReferenceEquals(captured, popup) || !ValidPopup()) return false;
            try { ClearChoice(popup.Category); }
            catch (Exception ex) { Warn("机器人卸下操作失败：" + ex.Message); }
            return false;
        }
        private static bool CallbackPrefix(object __instance, out bool __state)
        {
            __state = false;
            var native = __instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            if (native != null && callbackOwners.TryGetValue(native.Pointer, out var captured))
            {
                if (!ReferenceEquals(captured, popup) || !ValidPopup()) return false;
                actionDepth++; __state = true; return true;
            }
            var bar = AccessTools.Property(__instance.GetType(), "__4__this")?.GetValue(__instance) as CM_PlayerLobbyBar;
            if (bar == null || !lastBotWindows.ContainsKey(bar.Pointer)) return true;
            // Unknown/stale bot button: fail closed. A human's newly opened stock popup is not in this table.
            return false;
        }
        private static void CallbackPostfix(bool __state)
        {
            if (!__state) return;
            actionDepth = Math.Max(0, actionDepth - 1);
            if (actionDepth == 0 && popup?.Closed == true) popup = null;
        }
        private static Exception CallbackFinalizer(Exception __exception, bool __state)
        {
            if (__exception != null) CallbackPostfix(__state);
            return __exception;
        }
        private static bool PrepareInventoryPrefix(uint __0)
        {
            if (actionDepth == 0) return true;
            try { SetChoice(__0); } catch (Exception ex) { Warn("机器人选择失败：" + ex.Message); }
            return false;
        }
        private static bool UnprepareInventoryPrefix(BoosterImplantCategory __0)
        {
            if (actionDepth == 0) return true;
            try { ClearChoice((int)__0); } catch (Exception ex) { Warn("机器人卸下失败：" + ex.Message); }
            return false;
        }
        private static bool UnprepareAllPrefix() => actionDepth == 0; // Stock housekeeping must not unequip the host or other bots.
        private static bool DropInventoryPrefix(uint __0)
        {
            if (actionDepth == 0) return true;
            try { if (popup != null) ClearChoice(popup.Category); } catch (Exception ex) { Warn("机器人卸下失败：" + ex.Message); }
            return false;
        }
        private static void WindowPostfix(CM_ScrollWindow __instance, bool __0)
        {
            if (__0 || popup == null || popup.Window != __instance.Pointer) return;
            popup.Closed = true;
            if (actionDepth == 0) popup = null;
        }

        // Do not detour TryGetBoosterImplant's nullable native out parameter. Old interop
        // trampolines dereference a null wrapper when an original empty-slot lookup returns false.
        // UI choices are rendered directly; applied data still uses the stock manager sync path.
        private static bool SlotClickPrefix(CM_BoosterImplantSlot __instance)
        {
            if (!Host || !CurrentBot(__instance.m_player)) return true;
            if (!CanEdit(__instance.m_player)) return false;
            try
            {
                foreach (var bar in CM_PlayerLobbyBar.instances)
                    if (bar?.m_player?.Pointer == __instance.m_player.Pointer
                        && bar.m_boosterImplantSlotHolder?.m_categorySlots != null
                        && bar.m_boosterImplantSlotHolder.m_categorySlots.TryGetValue(__instance.m_category, out var control)
                        && control?.Pointer == __instance.Pointer)
                    { OpenPlayerPrefix(bar, __instance.m_category, __instance.m_player); return false; }
                Warn("机器人强化剂配装栏尚未关联，请重新打开配装页面。");
            }
            catch (Exception ex) { Warn("机器人强化剂入口失败：" + ex.Message); }
            return false;
        }
        private static void SlotPostfix(CM_BoosterImplantSlot __instance, SNet_Player __0)
        {
            if (!Host || !ViewState || !CurrentBot(__0)) return;
            try
            {
                CM_PlayerLobbyBar ownerBar = null;
                foreach (var bar in CM_PlayerLobbyBar.instances)
                    if (bar?.m_player?.Pointer == __0.Pointer && CanView(bar)
                        && bar.m_boosterImplantSlotHolder?.m_categorySlots != null
                        && bar.m_boosterImplantSlotHolder.m_categorySlots.TryGetValue(__instance.m_category, out var control)
                        && control?.Pointer == __instance.Pointer) { ownerBar = bar; break; }
                if (ownerBar == null) return;
                RenderSlot(__instance, __0, ownerBar, false);
            }
            catch (Exception ex) { Warn("机器人强化剂槽位显示失败：" + ex.Message); }
        }
        private static void RenderSlot(CM_BoosterImplantSlot control, SNet_Player owner, CM_PlayerLobbyBar bar, bool refreshStatus)
        {
            // Stock UpdateSlot may return early for bots. Bind the stock click target explicitly.
            control.m_player = owner;
            var choice = DisplayChoice(owner, (int)control.m_category);
            string fingerprint = choice?.Fingerprint ?? "empty";
            IntPtr data = control.m_boosterInSlot?.Pointer ?? IntPtr.Zero;
            bool changed = !renderedSlots.TryGetValue(control.Pointer, out var render) || render.Owner != Owner(owner)
                || render.Fingerprint != fingerprint || render.Data != data;
            bool visible = BotBoosterView.SlotVisible(control, choice != null);
            BotBoosterView.RestoreSlot(control, bar.transform, choice != null);
            if (changed)
            {
                control.m_boosterInSlot = choice == null ? null : new BoosterImplant(ToData(choice));
                if (renderedSlots.Count > 64) renderedSlots.Clear();
                renderedSlots[control.Pointer] = new SlotRender { Owner = Owner(owner), Fingerprint = fingerprint,
                    Data = control.m_boosterInSlot?.Pointer ?? IntPtr.Zero };
            }
            if (changed || !visible)
            {
                control.SetBackgroundActive(true);
                var icon = control.m_boosterIcon;
                if (icon != null)
                {
                    if (control.m_boosterInSlot?.Template != null)
                        icon.SetIcon(control.m_category, control.m_boosterInSlot.Template.MainEffectType);
                    else icon.SetIconEmpty(control.m_category);
                    control.UpdateInjectIcon();
                    icon.SetIconEnabled(true); icon.SetIconActive(choice != null);
                }
                if (control.m_emptyIcon != null) control.m_emptyIcon.gameObject.SetActive(choice == null);
            }
            bool editable = CanEdit(owner);
            if (control.m_slotCollider != null && control.m_slotCollider.enabled != editable) control.m_slotCollider.enabled = editable;
            if (!changed && !refreshStatus) return;
            control.SetCategoryText(CategoryName((int)control.m_category));
            var record = applied[Slot(owner)];
            control.SetStatusText(!editable ? (choice == null ? "空槽 · 只读" : selections.IsFrozen ? "本局装备 · 只读" : "已准备 · 只读")
                : choice == null ? "未装备" : runtimeUnavailable ? "接口不可用 · 同步已停用"
                : suspended[Slot(owner)] ? "同步已暂停 · 重新选择重试"
                : record != null && StillOurs(record) ? "虚拟装配 · 不消耗" : "已选择 · 待同步");
        }
        private static string CategoryName(int category) => category == 0 ? "低效" : category == 1 ? "高效" : "特效";
        private static void RefreshBar(CM_PlayerLobbyBar bar)
        {
            if (bar?.m_boosterImplantSlotHolder == null || !CurrentBot(bar.m_player)) return;
            renderedBars.Remove(bar.Pointer); BarPostfix(bar);
        }
        private static void BarEventPostfix(CM_PlayerLobbyBar __instance)
        { BarPostfix(__instance); }
        private static void RefreshVisibleBars()
        {
            if (!Host || !ViewState) return;
            foreach (var bar in CM_PlayerLobbyBar.instances) BarPostfix(bar);
        }
        private static void PageEventPostfix(CM_PageLoadout __instance)
        {
            try
            {
                if (!installed || !enabled.Value || !ViewState || __instance == null) return;
                BotBoosterView.ObservePage(__instance, viewDiagnostics.Value);
                foreach (var bar in CM_PlayerLobbyBar.instances)
                    if (bar?.m_parentPage?.Pointer == __instance.Pointer) BarPostfix(bar);
            }
            catch (Exception error) { Warn("机器人强化剂整页回调延后：" + error.Message); }
        }
        private static void LayoutEventPrefix(CM_PlayerLobbyBar __instance)
        {
            if (!CanView(__instance)) return;
            try { BotBoosterLayout.Restore(__instance); } catch (Exception error) { Warn("机器人强化剂布局恢复延后：" + error.Message); }
        }
        private static void BarPostfix(CM_PlayerLobbyBar __instance)
        {
            try
            {
                // Record the rejection before returning. Previously the diagnostic itself
                // was behind the same gate as the display repair and could never explain a failure.
                if (!installed || !enabled.Value || !ViewState || !Bot(__instance?.m_player)) return;
                BotBoosterView.ObserveGate(__instance, SNet.IsMaster, CurrentBot(__instance.m_player), viewDiagnostics.Value);
                if (!CanView(__instance)) return;
                var holder = __instance.m_boosterImplantSlotHolder;
                BotBoosterLayout.Apply(__instance);
                int slot = Slot(__instance.m_player);
                string renderKey = Owner(__instance.m_player) + ":" + string.Join("/", Array.ConvertAll(selections.Get(slot, Owner(__instance.m_player), catalog), x => x?.Fingerprint ?? "empty"))
                    + ":" + applied[slot]?.Key + ":" + runtimeUnavailable + ":" + suspended[slot] + ":" + CanEdit(__instance.m_player) + ":" + selections.IsFrozen;
                bool refreshStatus = !renderedBars.TryGetValue(__instance.Pointer, out var last) || last != renderKey;
                bool repaired = holder != null && !holder.gameObject.activeInHierarchy;
                BotBoosterView.Observe(__instance, CanEdit(__instance.m_player), repaired, viewDiagnostics.Value);
                if (holder == null || !holder.m_isSetup)
                {
                    // Reuse the stock prefab and a live stock layout, rather than guessing a UI scale.
                    CM_BoosterImplantSlotHolder sample = null;
                    foreach (var bar in CM_PlayerLobbyBar.instances)
                        if (!Bot(bar.m_player) && bar.m_boosterImplantSlotHolder != null && bar.m_boosterImplantSlotHolder.m_isSetup)
                        { sample = bar.m_boosterImplantSlotHolder; break; }
                    if (sample == null || __instance.m_boosterImplantSlotHolderPrefab == null) { Warn("机器人原版强化剂槽位布局尚未就绪。"); return; }
                    if (holder == null)
                    {
                        var go = UnityEngine.Object.Instantiate(__instance.m_boosterImplantSlotHolderPrefab, __instance.m_boosterImplantAlign, false);
                        holder = go.GetComponent<CM_BoosterImplantSlotHolder>();
                        if (holder == null) { UnityEngine.Object.Destroy(go); Warn("原版强化剂槽位预制体缺少控件。"); return; }
                    }
                    holder.Setup(sample.m_size.x, sample.m_layout);
                    holder.add_OnBoosterImplantSlotClicked(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<BoosterImplantCategory, SNet_Player>>(
                        (Action<BoosterImplantCategory, SNet_Player>)((category, player) => __instance.ShowBoosterImplantSelect(category, player))));
                    __instance.m_boosterImplantSlotHolder = holder;
                }
                BotBoosterView.Restore(holder.gameObject, __instance.transform);
                BotBoosterLayout.Apply(__instance);
                if (__instance.m_boosterImplantAlign != null) BotBoosterView.Restore(__instance.m_boosterImplantAlign.gameObject, __instance.transform);
                if (__instance.m_boosterImplantsHeaderText != null)
                {
                    BotBoosterView.Restore(__instance.m_boosterImplantsHeaderText.gameObject, __instance.transform);
                    if (refreshStatus) __instance.m_boosterImplantsHeaderText.text = CanEdit(__instance.m_player)
                        ? "机器人强化剂（主机库存 · 不消耗）" : "机器人强化剂（" + (selections.IsFrozen ? "本局装备" : "已准备") + " · 只读）";
                }
                // The stock inventory update can early-return for bots. Update the three
                // existing category controls directly instead of relying on that path.
                if (holder.m_categorySlots != null)
                    foreach (var entry in holder.m_categorySlots.Values) RenderSlot(entry, __instance.m_player, __instance, refreshStatus);
                if (renderedBars.Count > 64) renderedBars.Clear();
                renderedBars[__instance.Pointer] = renderKey;
            }
            catch (Exception ex) { Warn("机器人原版槽位显示适配失败：" + ex.Message); }
        }
    }
}
