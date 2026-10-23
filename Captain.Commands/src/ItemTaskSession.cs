using System;
using AIGraph;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    // One explicit batch owns one broker lease. Child completion keeps it queued;
    // only the batch's final outcome may release it and queue scan resumption.
    internal sealed class ItemTaskLease
    {
        readonly PlayerAIBot bot;
        readonly IntPtr owner, floor;
        long token;
        bool ended;
        internal bool HasLease => token != 0;
        public ItemTaskLease(PlayerAIBot agent) { bot = agent; owner = agent.Agent.Owner.Pointer; floor = LevelGeneration.Builder.CurrentFloor.Pointer; }
        bool Valid => !ended && SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel
            && LevelGeneration.Builder.CurrentFloor?.Pointer == floor && bot?.Agent?.Owner?.Pointer == owner && bot.Agent.Owner.IsBot;
        public bool Acquire(out long current)
        {
            current = 0; if (!Valid) return false;
            if (token == 0 && !BotTaskBridge.Acquire(bot, out token, true)) return false;
            if (!BotTaskBridge.Report(bot, token, ItemTaskStage.Waiting)) return false;
            current = token; return true;
        }
        public bool Heartbeat() => token == 0 || Valid && BotTaskBridge.Report(bot, token, ItemTaskStage.Waiting);
        public void Hold() { if (token != 0 && Valid) BotTaskBridge.Report(bot, token, ItemTaskStage.Waiting); }
        public void Suspend() { if (ended) return; var lease = token; token = 0; if (lease != 0 && Valid) BotTaskBridge.Release(bot, lease, ItemTaskStage.Cancelled); }
        public void Release(ItemTaskStage outcome)
        {
            if (ended) return;
            bool valid = Valid; long owned = token; token = 0; ended = true;
            if (owned != 0 && valid) BotTaskBridge.Release(bot, owned, outcome);
        }
    }
    internal sealed class ItemTaskSession : IItemTaskSession
    {
        readonly PlayerAIBot bot;
        readonly ItemTaskKind kind;
        readonly InventorySlot slot;
        readonly ItemInLevel target;
        readonly ContainerBridge container;
        readonly IntPtr floor, owner;
        readonly Action cleanup;
        readonly string label;
        readonly bool explicitCommand;
        readonly Action<bool> result;
        readonly Action<ItemTaskStage, string> statusObserver;
        readonly ItemTaskLease sharedLease;
        readonly IControlCommandPermit controlPermit;
        bool resultReported, outboundWarped, inboundWarped;
        long token;
        PlayerBotActionTravel.Descriptor travel;
        Vector3 travelPosition;
        Vector3 approachPosition;
        bool hasApproach;
        float diagnosticAt;
        string lastDiagnostic;
        float nextTravel;
        PickupCapacity capacity;
        ItemInLevel oldWorld;
        Item carried;
        uint id;
        float beforeAmount, targetAmount;
        bool applied;
        readonly StackCompatibility stacking;
        public ItemTaskSession(PlayerAIBot agent, ItemTaskKind taskKind, InventorySlot inventorySlot, ItemInLevel pickup, ContainerBridge destination, string name, Action releaseReservations, bool explicitCommand = true, Action<bool> onResult = null, Action<ItemTaskStage, string> onStatus = null, ItemTaskLease sharedLease = null, StackCompatibility stacking = null, IControlCommandPermit controlPermit = null)
        {
            bot = agent; kind = taskKind; slot = inventorySlot; target = pickup; container = destination;
            floor = LevelGeneration.Builder.CurrentFloor.Pointer; owner = bot.Agent.Owner.Pointer;
            label = name; cleanup = releaseReservations;
            this.explicitCommand = explicitCommand;
            result = onResult;
            statusObserver = onStatus;
            this.sharedLease = sharedLease;
            this.controlPermit = controlPermit;
            this.stacking = stacking ?? StackCompatibility.Shared;
        }
        public bool Valid => controlPermit?.IdentityValid != false && SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel
            && LevelGeneration.Builder.CurrentFloor?.Pointer == floor && bot != null && bot.Agent != null && bot.Agent.Owner != null
            && bot.Agent.Owner.Pointer == owner && bot.Agent.Owner.IsBot && bot.Agent.Alive && bot.Agent.BackpackAvailable();
        string NativeInventoryBlock()
        {
            var actions = bot.Actions;
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i]; if (action?.DescBase == null || action.DescBase.IsTerminated()) continue;
                if (action.TryCast<PlayerBotActionCollectItem>() != null) return "原版拾取动作尚未结束";
                if (action.TryCast<PlayerBotActionShareResourcePack>() != null) return "原版分发资源包动作尚未结束";
                if (action.TryCast<PlayerBotActionGatherDeployables>() != null) return "原版回收工具动作尚未结束";
                if (action.TryCast<PlayerBotActionDeploySentryGun>() != null) return "原版放置炮台动作尚未结束";
                if (action.TryCast<PlayerBotActionDeployTripMine>() != null) return "原版放置绊雷动作尚未结束";
                if (action.TryCast<PlayerBotActionCarryExpeditionItem>() != null) return "原版手持任务道具搬运动作尚未结束";
            }
            return null;
        }
        void Diagnose(string phase, string reason, ItemTaskStage? stage = null)
        {
            string key = phase + ":" + reason;
            if (lastDiagnostic == key && Time.unscaledTime < diagnosticAt) return;
            lastDiagnostic = key; diagnosticAt = Time.unscaledTime + 5;
            if (stage.HasValue) statusObserver?.Invoke(stage.Value, phase + "：" + reason);
            ItemTaskAdapter.Diagnose(bot, target, slot, label, phase, reason);
        }
        public bool Acquire()
        {
            ItemActionAuthorization.Quiesce(bot);
            string blocked = NativeInventoryBlock();
            bool acquired = blocked == null && (controlPermit != null ? controlPermit.Acquire(out token)
                : sharedLease != null ? sharedLease.Acquire(out token) : BotTaskBridge.Acquire(bot, out token, explicitCommand));
            if (!acquired) Diagnose("等待控制权限", blocked ?? "任务仲裁未放行；未取得租约，尚未开始移动／交互（具体保护来源需结合机器人仲裁日志）", ItemTaskStage.Waiting);
            return acquired;
        }
        public bool Renew(ItemTaskStage stage) => controlPermit != null ? controlPermit.Report(stage) : BotTaskBridge.Report(bot, token, stage);
        public void Suspend()
        {
            try { StopOwnMovement(); }
            finally { var lease = token; token = 0; if (controlPermit != null) controlPermit.Hold(); else if (sharedLease != null) sharedLease.Suspend(); else if (lease != 0 && Valid) BotTaskBridge.Release(bot, lease, ItemTaskStage.Cancelled); hasApproach = false; }
        }
        public bool CanExecute
        {
            get
            {
                string blocked = NativeInventoryBlock();
                bool allowed = blocked == null && BotTaskBridge.CanExecute(bot, token);
                if (!allowed) Diagnose("执行权限受阻", blocked ?? "已持有租约，但机器人仲裁暂时拒绝执行；未提交拾取同步", ItemTaskStage.Paused);
                return allowed;
            }
        }
        static bool Placed(ItemInLevel item) => item != null && item.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>()?.GetCurrentState().status == ePickupItemStatus.PlacedInLevel;
        static float WorldAmount(ItemInLevel item) => Placed(item) ? item.GetCustomData().ammo : 0;
        static bool SameSpace(PlayerAgent agent, AIG_CourseNode node) => ItemRegion.SameSpace(agent, node);
        bool Held(out Item item, out float amount)
        {
            item = null; amount = 0;
            if (!PlayerBackpackManager.TryGetBackpack(bot.Agent.Owner, out var backpack) || backpack.AmmoStorage == null) return false;
            if (backpack.TryGetBackpackItem(slot, out var held) && held?.Instance != null)
            {
                item = held.Instance; var ammo = backpack.AmmoStorage.GetInventorySlotAmmo(slot);
                if (ammo == null) return false; amount = ammo.AmmoInPack;
            }
            return true;
        }
        string PickupCapacityReason(uint heldId, float amount)
        {
            // An empty slot does not require a stacking provider.
            var rule = heldId == id && heldId != 0 ? stacking.Read(slot, target.ItemDataBlock.ConsumableAmmoMax) : default;
            capacity = new PickupCapacity((int)slot, id, targetAmount, heldId, amount, rule.Enabled, rule.Limit);
            string reason = ItemTaskPolicy.CanPickup(capacity);
            return heldId == id && heldId != 0 && !rule.Enabled && ItemTaskPolicy.SupportedSlot((int)slot)
                && ItemTaskPolicy.Quantity(targetAmount) && targetAmount > 0 && ItemTaskPolicy.Quantity(amount) ? rule.Error : reason;
        }
        public string Validate()
        {
            if (!Valid) return "机器人状态已失效";
            if (kind == ItemTaskKind.Return) return null;
            if (!Held(out var held, out var amount)) return "无法读取指定机器人的背包数量";
            if (kind == ItemTaskKind.PutBack)
            {
                if (held == null || !ItemTaskPolicy.SupportedSlot((int)slot) || !ItemTaskPolicy.Quantity(amount) || amount <= 0) return "该槽位没有可以放回的物品";
                if (container == null || !container.Available) return "选定容器位置已经占用或未打开";
                if (!SameSpace(bot.Agent, container.Storage.m_core?.SpawnNode)) return "容器不在机器人当前维度与关卡分层";
                carried = held; id = held.Get_pItemData().itemID_gearCRC; beforeAmount = amount;
                if (!PlayerBackpackManager.TryGetItemInLevelFromItemData(held.Get_pItemData(), out var world)) return "无法找到物品原有的世界同步对象";
                oldWorld = world?.TryCast<ItemInLevel>();
                return oldWorld?.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>() == null ? "该物品不支持原有拾取同步流程" : null;
            }
            if (!Placed(target) || target.ItemDataBlock == null || !target.gameObject.activeInHierarchy) return "目标物品已被拿走或失效";
            if (target.container != null && (target.container.m_core == null || target.container.m_core.Status != eResourceContainerStatus.Open)) return "目标物品所在容器未打开，请先由玩家打开";
            if (!ItemRegion.TryResolve(target, out var targetNode)) return "目标物品区域信息尚未就绪，请稍后重试";
            if (!SameSpace(bot.Agent, targetNode)) return "目标不在机器人当前维度与关卡分层（" + ItemRegion.Describe(bot.Agent, target) + "）";
            id = target.Get_pItemData().itemID_gearCRC; beforeAmount = amount; targetAmount = target.GetCustomData().ammo;
            uint heldId = held == null ? 0 : held.Get_pItemData().itemID_gearCRC;
            carried = held; oldWorld = null;
            if (held != null && PlayerBackpackManager.TryGetItemInLevelFromItemData(held.Get_pItemData(), out var prior)) oldWorld = prior?.TryCast<ItemInLevel>();
            return PickupCapacityReason(heldId, amount);
        }
        Vector3 Destination(bool returning)
        {
            if (returning)
            {
                if (explicitCommand && BotTaskBridge.ResumePosition(bot, token, out var scanPosition)) return scanPosition;
                var leader = bot.SyncValues?.Leader;
                if (leader == null || !leader.Alive || !SameSpace(bot.Agent, leader.CourseNode)) throw new InvalidOperationException("当前跟随领队不可达");
                return leader.Position;
            }
            return kind == ItemTaskKind.Pickup ? ItemInteractionGeometry.Point(target) : container.Placement(slot).position;
        }
        public bool AtDestination(bool returning)
        {
            var destination = Destination(returning);
            return ItemArrivalPolicy.Interactable(bot.Agent.Position.x - destination.x, bot.Agent.Position.y - destination.y,
                bot.Agent.Position.z - destination.z, returning, ItemTaskAdapter.PickupReach)
                && (returning || kind != ItemTaskKind.Pickup || ItemInteractionGeometry.Visible(bot.Agent.EyePosition, target));
        }
        public bool Move(bool returning)
        {
            var position = Destination(returning);
            if (!returning)
            {
                if (hasApproach && (!ItemArrivalPolicy.Interactable(approachPosition.x - position.x, approachPosition.y - position.y,
                    approachPosition.z - position.z, false, ItemTaskAdapter.PickupReach)
                    || kind == ItemTaskKind.Pickup && !ItemInteractionGeometry.Visible(approachPosition + (bot.Agent.EyePosition - bot.Agent.Position), target))) hasApproach = false;
                if (!hasApproach)
                {
                    if (!ItemInteractionGeometry.Standing(bot.Agent, position, kind == ItemTaskKind.Pickup ? target : null, out approachPosition, out var reason))
                    {
                        Diagnose("选择交互站位", reason, ItemTaskStage.Moving);
                        return false;
                    }
                    hasApproach = true;
                }
                position = approachPosition;
            }
            bool warped = returning ? inboundWarped : outboundWarped;
            if (!warped && explicitCommand && ItemTaskAdapter.TeleportEnabled
                && (returning || Vector3.Distance(bot.Agent.Position, Destination(false)) > ItemTaskAdapter.TeleportDistance)
                && BotTaskBridge.Teleport(bot, token, position))
            {
                if (returning) inboundWarped = true; else outboundWarped = true;
                StopOwnMovement();
                if (AtDestination(returning)) return true;
                // A successful warp can land outside interaction range; finish by walking.
            }
            if (!PlayerAIBot.SnapPositionToNav(position, out var standing, 2f, 8f) || Vector3.Distance(standing, position) > 2.1f)
            { Diagnose("移动站位", "目标接近点无法贴合导航面，或贴合偏移超过2.1米"); return false; }
            position = standing;
            if (Time.time < nextTravel) return false;
            if (travel != null && !travel.IsTerminated() && Vector3.Distance(travelPosition, position) < 1) return true;
            StopOwnMovement(); nextTravel = Time.time + 1; travelPosition = position;
            var request = new PlayerBotActionTravel.Descriptor(bot) { Prio = RootPlayerBotAction.m_prioSettings.CollectItem,
                DestinationType = PlayerBotActionTravel.Descriptor.DestinationEnum.Position, DestinationPos = position, Radius = returning ? 1 : .25f, Haste = .5f, Persistent = false };
            travel = request;
            bool accepted = bot.RequestAction(request);
            if (!accepted) Diagnose("原版移动请求", "原版 RequestAction 拒绝移动；目标未到达，不会假定拾取成功");
            return accepted;
        }
        public void StopOwnMovement()
        { var owned = travel; travel = null; if (controlPermit?.IdentityValid != false && SNet.IsMaster && owned != null && !owned.IsTerminated() && bot != null && bot.Agent?.Owner?.Pointer == owner && bot.Agent.Owner.IsBot) bot.StopAction(owned); }
        internal bool OurAction(PlayerBotActionBase.Descriptor desc)
        {
            for (int i = 0; desc != null && i < 8; i++, desc = desc.ParentActionBase?.DescBase)
                if (travel != null && !travel.IsTerminated() && desc.Pointer == travel.Pointer) return true;
            return false;
        }
        public float InteractionDuration => PlayerBotActionCollectItem.s_transferDuration;
        public bool Apply(out string error)
        {
            error = null;
            if (applied) { error = "此交互已经提交，不会重复提交"; return false; }
            if (!CanExecute) { error = "交互被紧急动作打断或控制权已改变"; return false; }
            if (!AtDestination(false)) { hasApproach = false; error = "交互站位已变化或被遮挡，需重新接近"; return false; }
            if (!Held(out var held, out var amount)) { error = "交互时无法读取机器人背包"; return false; }
            if (!ItemTaskPolicy.Conserved(beforeAmount, amount)) { error = "交互时背包数量变化：" + beforeAmount + " → " + amount; return false; }
            if ((held == null ? 0 : held.Get_pItemData().itemID_gearCRC) != (carried == null ? 0 : carried.Get_pItemData().itemID_gearCRC))
            { error = "交互时携带物品身份已变化"; return false; }
            if (kind == ItemTaskKind.Pickup)
            {
                if (target.ItemDataBlock == null || target.Get_pItemData().itemID_gearCRC != id || target.Get_pItemData().slot != slot)
                { error = "拾取物品身份或容量数据已变化，停止此次交互"; return false; }
                if (!ItemInteractionGeometry.Visible(bot.Agent.EyePosition, target))
                { error = "拾取目标被遮挡，停止此次交互"; return false; }
                if (!Placed(target) || target.container != null && (target.container.m_core == null || target.container.m_core.Status != eResourceContainerStatus.Open)
                    || !ItemTaskPolicy.Conserved(targetAmount, target.GetCustomData().ammo)) { error = "目标已变化，停止此次拾取"; return false; }
                var sync = target.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>(); if (sync == null) { error = "无法读取目标同步对象"; return false; }
                if (!ItemRegion.TryResolve(target, out var currentNode) || !SameSpace(bot.Agent, currentNode))
                { error = "提交拾取时当前区域节点失效或已跨层，停止此次交互"; return false; }
                error = PickupCapacityReason(held == null ? 0 : held.Get_pItemData().itemID_gearCRC, amount);
                if (error != null) return false;
                applied = true;
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, bot.Agent.Owner, target.GetCustomData(), target.transform.position, target.transform.rotation, currentNode, false, false);
            }
            else
            {
                if (container == null || !container.Available || oldWorld == null) { error = "放回位置或物品同步对象失效"; return false; }
                var sync = oldWorld.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>(); if (sync == null) { error = "无法读取原有物品同步对象"; return false; }
                var pose = container.Placement(slot); if (pose == null) { error = "容器缺少放回位置"; return false; }
                var custom = sync.GetCustomData(); custom.ammo = amount; applied = true;
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, bot.Agent.Owner, custom, pose.position, pose.rotation, container.Storage.m_core.SpawnNode, false, false);
            }
            return true;
        }
        public bool Verify()
        {
            if (!applied || !Held(out var held, out var amount)) return false;
            if (kind == ItemTaskKind.PutBack)
                return Confirm(held == null && Placed(oldWorld) && ItemTaskPolicy.Conserved(beforeAmount, oldWorld.GetCustomData().ammo)
                    && Vector3.Distance(oldWorld.transform.position, container.Placement(slot).position) < .75f);
            if (held == null || held.Get_pItemData().itemID_gearCRC != id || amount <= beforeAmount) return false;
            float world = WorldAmount(target);
            if (oldWorld != null && oldWorld.Pointer != target.Pointer) world += WorldAmount(oldWorld);
            return Confirm(ItemTaskPolicy.Conserved(beforeAmount + targetAmount, amount + world));
        }
        bool Confirm(bool success)
        { if (success && !resultReported) { resultReported = true; result?.Invoke(true); } return success; }
        void EndResult()
        { if (!resultReported) { resultReported = true; result?.Invoke(false); } }
        public void Release(ItemTaskStage outcome) { try { if (controlPermit != null) controlPermit.Hold(); else if (sharedLease != null) sharedLease.Hold(); else BotTaskBridge.Release(bot, token, outcome); } finally { try { EndResult(); } finally { cleanup(); } } }
        public void Notify(ItemTaskStage stage, string detail)
        {
            if (controlPermit != null && stage == ItemTaskStage.Complete) detail = "拾取已核验，交回接管微调";
            if (stage == ItemTaskStage.Failed) ItemTaskAdapter.Diagnose(bot, target, slot, label, "任务失败", detail, false);
            statusObserver?.Invoke(stage, detail);
            if (stage == ItemTaskStage.Returning) cleanup();
            ItemTaskAdapter.Feedback(bot?.Agent?.PlayerName + "：" + label + " — " + detail);
            if (stage == ItemTaskStage.Complete || stage == ItemTaskStage.Failed || stage == ItemTaskStage.Cancelled) { try { EndResult(); } finally { cleanup(); } }
        }
    }
    internal static class TaskAgentExtensions
    {
        public static bool BackpackAvailable(this PlayerAgent agent) => agent.Owner != null && PlayerBackpackManager.TryGetBackpack(agent.Owner, out var backpack) && backpack?.AmmoStorage != null;
    }
}
