using System;
using System.Collections.Generic;
using AIGraph;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal enum InteractionPointKind { OrdinaryDoor, SecurityDoor, Generator, HSU, WorldEvent }
    internal sealed record InteractionCandidate(Component Value, InteractionPointKind Kind, AIG_CourseNode Node, AIG_CoursePortal Portal = null);
    internal sealed record InteractionIdentity(InteractionCandidate Candidate, IntPtr Sync, string Serial, IntPtr Floor);
    internal sealed class KnownInteractionSnapshot
    {
        public readonly List<CaptainInteractionResult> Results = new();
        public readonly Dictionary<int, InteractionIdentity> Targets = new();
        public readonly Dictionary<int, string> Details = new();
    }
    // No native interaction, activation, Setup, movement, or task callbacks are invoked here.
    internal static class KnownInteractionIndex
    {
        sealed record Data(AIG_CourseNode Node, IntPtr Sync, string Serial, string Label, string State);
        static readonly KnownSupplyLedger<InteractionIdentity> known = new();
        static float nextWarning;
        internal static bool HasState => known.Count != 0;
        static bool Active => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor != null;
        static bool Root(Component value) => value != null && value.gameObject != null && value.gameObject.scene.IsValid()
            && value.gameObject.activeInHierarchy && float.IsFinite(value.transform.position.x)
            && float.IsFinite(value.transform.position.y) && float.IsFinite(value.transform.position.z);
        static string DoorState(eDoorStatus status) => status switch {
            eDoorStatus.Open => "已开启", eDoorStatus.Opening => "开启中", eDoorStatus.Destroyed => "已破坏",
            eDoorStatus.Closed => "关闭", eDoorStatus.Unlocked => "已解锁·关闭",
            eDoorStatus.Closed_LockedWithNoKey => "锁定", _ => "封锁或特殊状态（" + status + "）" };
        static string InsertName(eCarryItemInsertTargetType type) => type switch {
            eCarryItemInsertTargetType.PowerCell => "电池", eCarryItemInsertTargetType.SmallHSU => "小型 HSU",
            eCarryItemInsertTargetType.Crate => "任务箱", _ => "未确认用途" };
        static Data Read(InteractionCandidate target)
        {
            if (!Active || target == null || !Root(target.Value)) return null;
            AIG_CourseNode node = target.Node; IntPtr sync; string serial, label, state;
            switch (target.Kind) {
                case InteractionPointKind.OrdinaryDoor: {
                    var door = target.Value.TryCast<LG_WeakDoor>();
                    if (door == null || door.m_syncComp == null || door.m_sync == null || door._Gate_k__BackingField == null
                        || door._Gate_k__BackingField._SpawnedDoor_k__BackingField?.Pointer != door.Pointer || target.Portal?.m_door?.Pointer != door.Pointer
                        || target.Portal.m_nodeA?.Pointer != node?.Pointer && target.Portal.m_nodeB?.Pointer != node?.Pointer) return null;
                    sync = door.m_syncComp.Pointer; serial = door.m_serialNumber.ToString();
                    label = "普通门 #" + serial; state = DoorState(door._LastStatus_k__BackingField); break;
                }
                case InteractionPointKind.SecurityDoor: {
                    var door = target.Value.TryCast<LG_SecurityDoor>();
                    if (door == null || door.m_syncComp == null || door.m_sync == null || door._Gate_k__BackingField == null
                        || door._Gate_k__BackingField._SpawnedDoor_k__BackingField?.Pointer != door.Pointer || target.Portal?.m_door?.Pointer != door.Pointer
                        || target.Portal.m_nodeA?.Pointer != node?.Pointer && target.Portal.m_nodeB?.Pointer != node?.Pointer) return null;
                    sync = door.m_syncComp.Pointer; serial = door.m_serialNumber.ToString();
                    label = "安全门 #" + serial; state = DoorState(door._LastStatus_k__BackingField) + "；仅标记当前可达侧，不启动扫描"; break;
                }
                case InteractionPointKind.Generator: {
                    var generator = target.Value.TryCast<LG_PowerGenerator_Core>();
                    if (generator?.m_stateReplicator == null || generator.m_terminalItem == null) return null;
                    node = generator._SpawnNode_k__BackingField;
                    sync = generator.m_stateReplicator.Pointer; serial = generator.m_serialNumber.ToString(); label = "发电机接口 #" + serial;
                    state = generator.m_stateReplicator.m_currentState.status switch {
                        ePowerGeneratorStatus.Powered => "已供电", ePowerGeneratorStatus.UnPowered => "未供电", _ => "供电状态未确认" }; break;
                }
                case InteractionPointKind.HSU: {
                    var hsu = target.Value.TryCast<LG_HSUActivator_Core>();
                    if (hsu?.m_stateReplicator == null || hsu.m_terminalItem == null) return null;
                    node = hsu._SpawnNode_k__BackingField; sync = hsu.m_stateReplicator.Pointer; serial = hsu.m_serialNumber.ToString();
                    label = "HSU 任务接口 #" + serial;
                    state = hsu._ObjectiveItemSolved_k__BackingField ? "任务物件已完成" : "任务物件未完成（不代表当前可操作）"; break;
                }
                case InteractionPointKind.WorldEvent: {
                    var interact = target.Value.TryCast<LG_InteractWorldEventTrigger>();
                    if (interact?.m_worldEventTriggerSync?.m_stateReplicator == null
                        || interact.GetComponentInParent<LG_PowerGenerator_Core>() != null || interact.GetComponentInParent<LG_HSUActivator_Core>() != null) return null;
                    // Never guess a node using world coordinates shared by multiple dimensions.
                    var owner = interact.GetComponentInParent<LG_WorldEventObject>();
                    node = owner != null ? owner._ParentArea_k__BackingField?.m_courseNode : interact.GetComponentInParent<LG_Area>()?.m_courseNode;
                    sync = interact.m_worldEventTriggerSync.m_stateReplicator.Pointer;
                    serial = ((int)interact.m_insertType).ToString() + ":" + interact.m_interactionText;
                    label = interact.m_insertType is eCarryItemInsertTargetType.PowerCell or eCarryItemInsertTargetType.SmallHSU or eCarryItemInsertTargetType.Crate
                        ? "任务交付点（" + InsertName(interact.m_insertType) + "）" : "任务交互点（用途未确认）";
                    state = interact.m_worldEventTriggerSync.m_stateReplicator.m_currentState.HasBeenTriggered ? "当前同步状态：已触发" : "当前同步状态：未触发";
                    if (interact.m_isToggle) state += "；可切换事件，已触发不代表任务完成"; break;
                }
                default: return null;
            }
            return node?.m_zone?.Layer == null || sync == IntPtr.Zero ? null : new(node, sync, serial, label, state);
        }
        internal static bool Resolve(InteractionCandidate target, out AIG_CourseNode node)
        {
            node = null;
            try { var data = Read(target); node = data?.Node; return data != null; }
            catch (Exception error) { Warn("对象身份／状态", error); return false; }
        }
        internal static bool Matches(InteractionCandidate target, int filter) => filter == 0
            || filter == 1 && target.Kind is InteractionPointKind.OrdinaryDoor or InteractionPointKind.SecurityDoor
            || filter == 2 && target.Kind == InteractionPointKind.Generator
            || filter == 3 && target.Kind is InteractionPointKind.HSU or InteractionPointKind.WorldEvent;
        internal static List<InteractionCandidate> Candidates(IReadOnlyDictionary<long, AIG_CourseNode> nodes, int filter, out bool truncated)
        {
            var result = new List<InteractionCandidate>(); var seen = new HashSet<IntPtr>(); int examined = 0; bool limited = false;
            bool Add(InteractionCandidate target) {
                if (++examined > 4096) { limited = true; return false; }
                if (Resolve(target, out _) && seen.Add(target.Value.Pointer)) result.Add(target);
                return true;
            }
            if (Active && nodes != null && filter is 0 or 1) foreach (var node in nodes.Values) {
                if (node?.m_portals == null) continue;
                foreach (var portal in node.m_portals) try {
                    var core = portal?.m_door;
                    var weak = core?.TryCast<LG_WeakDoor>(); var security = core?.TryCast<LG_SecurityDoor>();
                    if (weak != null && !Add(new(weak, InteractionPointKind.OrdinaryDoor, node, portal))
                        || security != null && !Add(new(security, InteractionPointKind.SecurityDoor, node, portal))) break;
                } catch (Exception error) { Warn("门候选", error); }
                if (limited) break;
            }
            void Collect<T>(InteractionPointKind kind) where T : Component {
                if (!Active || limited) return;
                try { foreach (var value in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<T>())) {
                    try { var component = value.TryCast<T>(); if (!Add(new(component, kind, null))) break; }
                    catch (Exception error) { Warn("场景候选", error); }
                } } catch (Exception error) { Warn("场景枚举", error); }
            }
            if (filter is 0 or 2) Collect<LG_PowerGenerator_Core>(InteractionPointKind.Generator);
            if (filter is 0 or 3) {
                Collect<LG_HSUActivator_Core>(InteractionPointKind.HSU);
                Collect<LG_InteractWorldEventTrigger>(InteractionPointKind.WorldEvent);
            }
            truncated = limited; return result;
        }
        internal static bool Observe(InteractionCandidate target)
        {
            var data = Read(target); if (data == null) return false;
            long key = target.Value.Pointer.ToInt64();
            foreach (var entry in known.Entries) if (entry.Key == key && (entry.Value.Sync != data.Sync || entry.Value.Serial != data.Serial
                || entry.Value.Candidate.Kind != target.Kind || entry.Value.Floor != Builder.CurrentFloor.Pointer)) { known.Forget(key); break; }
            // A later query may update the same door's accessible side without creating a duplicate result.
            return known.Observe(key, new(target, data.Sync, data.Serial, Builder.CurrentFloor.Pointer), SupplySource.RobotSearch) != null;
        }
        internal static bool Current(int id, InteractionIdentity saved)
        {
            if (saved == null || saved.Floor != Builder.CurrentFloor?.Pointer) return false;
            try { var data = Read(saved.Candidate); return data != null && data.Sync == saved.Sync && data.Serial == saved.Serial
                && known.Current(saved.Candidate.Value.Pointer.ToInt64(), id); }
            catch (Exception error) { Warn("旧结果核验", error); return false; }
        }
        internal static bool Accessible(PlayerAgent source, InteractionIdentity saved)
            => saved != null && Resolve(saved.Candidate, out var node) && SearchTraversal.InScope(node, source?.CourseNode, 1)
                && ItemRegion.SameSpace(source, node)
                && SearchTraversal.Reachable(source, source.CourseNode, source.CourseNode, 1, out _).Contains(node.Pointer.ToInt64());
        internal static bool Mark(InteractionCandidate target)
        {
            var data = Read(target); if (data == null) return false;
            return SearchMarker.Place(target.Value.gameObject, "搜寻：" + data.Label + "（" + data.State + "）", NavMarkerOption.WaypointTitleDistance);
        }
        internal static KnownInteractionSnapshot Capture(PlayerAgent source)
        {
            var snapshot = new KnownInteractionSnapshot();
            if (!Active || source?.CourseNode?.m_zone == null) return snapshot;
            var reachable = SearchTraversal.Reachable(source, source.CourseNode, source.CourseNode, 1, out _);
            var entries = new List<KnownSupplyLedger<InteractionIdentity>.Entry>(known.Entries); entries.Sort((a,b) => a.Id.CompareTo(b.Id));
            foreach (var entry in entries) try {
                var saved = entry.Value;
                if (!Current(entry.Id, saved)) { known.Forget(entry.Key); continue; }
                var data = Read(saved.Candidate);
                if (data == null || !SearchTraversal.InScope(data.Node, source.CourseNode, 1) || !ItemRegion.SameSpace(source, data.Node)) continue;
                if (snapshot.Targets.Count >= 64) { snapshot.Results.Add(new(0, "本次最多展示64个交互点", true)); break; }
                string label = data.Label + (saved.Candidate.Kind == InteractionPointKind.WorldEvent ? " 本局 #" + entry.Id : "") + "（" + data.State + "）";
                string details = label + "；" + data.Node.m_zone.AliasName + " / 房间 " + data.Node.m_area?.m_navInfo?.Number
                    + "；位置 " + saved.Candidate.Value.transform.position + "；来源：队长即时搜寻；"
                    + (reachable.Contains(data.Node.Pointer.ToInt64()) ? "当前普通门可达" : "当前通路已封锁，不能重新标记")
                    + "；只读定位，不执行交付／启动／开门；状态不等于当前交互权限";
                snapshot.Results.Add(new(entry.Id, label)); snapshot.Targets[entry.Id] = saved; snapshot.Details[entry.Id] = details;
            } catch (Exception error) { Warn("结果列表", error); }
            return snapshot;
        }
        static void Warn(string stage, Exception error)
        { if (Time.time < nextWarning) return; nextWarning = Time.time + 10; Plugin.Logger.LogWarning("交互点搜寻" + stage + "失败，其余对象继续处理：" + error); }
        internal static void Reset() { known.Reset(); nextWarning = 0; }
    }
}
