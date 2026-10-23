using System;
using System.Collections.Generic;
using AIGraph;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal sealed record ContainerIdentity(LG_WeakResourceContainer Value, int Serial, IntPtr Sync, IntPtr Floor);
    internal sealed class KnownContainerSnapshot
    {
        public readonly List<CaptainContainerResult> Results = new();
        public readonly Dictionary<int, ContainerIdentity> Targets = new();
        public readonly Dictionary<int, string> Details = new();
    }
    internal static class KnownContainerIndex
    {
        static readonly KnownSupplyLedger<ContainerIdentity> known = new();
        static float nextWarning;
        internal static bool HasState => known.Count != 0;
        static bool Active => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor != null;
        internal static bool Resolve(LG_WeakResourceContainer box, out AIG_CourseNode node)
        {
            node = null;
            try {
                if (!Active || box == null || box.gameObject == null || !box.gameObject.scene.IsValid() || !box.gameObject.activeInHierarchy
                    || box.m_terminalItem == null || box.m_storage == null || box.m_sync == null || box.m_syncComp == null) return false;
                node = box.SpawnNode; return node?.m_zone?.Layer != null;
            } catch (Exception error) { Warn("身份核验", error); return false; }
        }
        internal static List<LG_WeakResourceContainer> Candidates(out bool truncated)
        {
            var result = new List<LG_WeakResourceContainer>(); truncated = false;
            if (!Active) return result;
            var seen = new HashSet<IntPtr>(); int examined = 0;
            // Command-time only; includes empty boxes, and never relies on contained items to find boxes.
            foreach (var component in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<LG_WeakResourceContainer>())) {
                if (++examined > 4096) { truncated = true; break; }
                try {
                    var box = component.TryCast<LG_WeakResourceContainer>();
                    if (Resolve(box, out _) && seen.Add(box.Pointer)) result.Add(box);
                } catch (Exception error) { Warn("候选对象", error); }
            }
            return result;
        }
        internal static string Name(LG_WeakResourceContainer box) => (box.m_isLocker ? "储物柜" : "物资箱") + " #" + box.m_serialNumber;
        internal static bool Matches(LG_WeakResourceContainer box, int filter)
            => filter == 0 || filter == 1 && !box.ISOpen || filter == 2 && !box.ISOpen && box.IsLocked();
        internal static string State(LG_WeakResourceContainer box)
        {
            if (box.ISOpen) return "已开启";
            var weak = box.WeakLockComponent;
            var kind = MarkedOpenTarget.Kind(weak);
            return kind switch { OpenLockKind.Hackable => "未开启·入侵锁", OpenLockKind.Melee => "未开启·物理锁",
                OpenLockKind.Unsupported => "未开启·不支持的锁状态", _ => box.IsLocked() ? "未开启·锁定／解锁中" : "未开启·无锁" };
        }
        internal static string Contents(LG_WeakResourceContainer box)
        {
            try { return ReadContents(box); }
            catch (Exception error) { Warn("内容读取", error); return "内容未确认（读取失败，详见日志）"; }
        }
        static string ReadContents(LG_WeakResourceContainer box)
        {
            var labels = new List<string>(); var seen = new HashSet<IntPtr>(); int count = 0; bool truncated = false;
            foreach (var item in box.GetComponentsInChildren<ItemInLevel>(true)) {
                if (++count > 64) { truncated = true; break; }
                if (item == null || item.ItemDataBlock == null || !ItemRegion.TryResolve(item, out _)
                    || item.container?.m_core?.Pointer != box.Pointer || !seen.Add(item.Pointer)) continue;
                if (labels.Count < 8) labels.Add(Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName ?? "未知物品");
            }
            if (seen.Count > 0) return "已确认 " + seen.Count + " 件：" + string.Join("、", labels)
                + (seen.Count > 8 ? "等" : "") + (truncated ? "（检查达到上限）" : "");
            var storage = box.m_storageComp?.TryCast<LG_ResourceContainer_Storage>();
            return !truncated && box.ISOpen && storage?.m_numberOfItems == 0 ? "已开启·当前为空" : "内容未确认";
        }
        internal static bool Observe(LG_WeakResourceContainer box)
        {
            if (!Resolve(box, out _)) return false;
            long key = box.Pointer.ToInt64();
            foreach (var entry in known.Entries) if (entry.Key == key && (entry.Value.Serial != box.m_serialNumber
                || entry.Value.Sync != box.m_syncComp.Pointer || entry.Value.Floor != Builder.CurrentFloor.Pointer)) {
                known.Forget(key); break;
            }
            return known.Observe(key, new(box, box.m_serialNumber, box.m_syncComp.Pointer, Builder.CurrentFloor.Pointer), SupplySource.RobotSearch) != null;
        }
        internal static bool Current(int id, ContainerIdentity saved) => saved != null && saved.Floor == Builder.CurrentFloor?.Pointer
            && Resolve(saved.Value, out _) && saved.Value.m_serialNumber == saved.Serial && saved.Value.m_syncComp.Pointer == saved.Sync
            && known.Current(saved.Value.Pointer.ToInt64(), id);
        internal static bool Accessible(PlayerAgent source, ContainerIdentity saved, out AIG_CourseNode node)
        {
            node = null;
            return saved != null && Resolve(saved.Value, out node) && SearchTraversal.InScope(node, source?.CourseNode, 1)
                && ItemRegion.SameSpace(source, node)
                && SearchTraversal.Reachable(source, source.CourseNode, source.CourseNode, 1, out _).Contains(node.Pointer.ToInt64());
        }
        internal static MarkedOpenTarget OpenTarget(PlayerAgent source, ContainerIdentity saved)
            => Accessible(source, saved, out var node) ? MarkedOpenTarget.FromContainer(source, saved.Value, node) : null;
        internal static KnownContainerSnapshot Capture(PlayerAgent source)
        {
            var snapshot = new KnownContainerSnapshot();
            if (!Active || source?.CourseNode?.m_zone == null) return snapshot;
            var reachable = SearchTraversal.Reachable(source, source.CourseNode, source.CourseNode, 1, out _);
            var entries = new List<KnownSupplyLedger<ContainerIdentity>.Entry>(known.Entries); entries.Sort((a,b) => a.Id.CompareTo(b.Id));
            foreach (var entry in entries) try {
                var saved = entry.Value;
                if (!Current(entry.Id, saved)) { known.Forget(entry.Key); continue; }
                if (!Resolve(saved.Value, out var node) || !SearchTraversal.InScope(node, source.CourseNode, 1) || !ItemRegion.SameSpace(source, node)) continue;
                if (snapshot.Targets.Count >= 64) { snapshot.Results.Add(new(0, "本次最多展示64个容器，请缩小查看范围", "显示上限提示", Notice:true)); break; }
                var box = saved.Value; var target = MarkedOpenTarget.FromContainer(source, box, node);
                string blocked = box.ISOpen ? "该容器已开启，无需重复操作" : !reachable.Contains(node.Pointer.ToInt64()) ? "容器当前被封锁门阻隔" : target.Restriction;
                string label = Name(box) + "（" + State(box) + "）";
                string detail = label + "；" + node.m_zone.AliasName + " / 房间 " + node.m_area?.m_navInfo?.Number
                    + "；位置 " + box.transform.position + "；" + Contents(box) + "；来源：即时搜寻；" + (blocked ?? "可指定机器人打开，不自动拾取");
                snapshot.Results.Add(new(entry.Id, label, blocked, target.NeedsMelee, target.NeededMelters));
                snapshot.Targets[entry.Id] = saved; snapshot.Details[entry.Id] = detail;
            } catch (Exception error) { Warn("结果列表", error); }
            return snapshot;
        }
        internal static bool Mark(LG_WeakResourceContainer box)
        {
            if (!Resolve(box, out _)) return false;
            bool marked = SearchMarker.Place(box.gameObject, "搜寻：" + Name(box) + "（" + State(box) + "）",
                box.m_isLocker ? NavMarkerOption.ResourceLockerTitleDistance : NavMarkerOption.ResourceBoxTitleDistance);
            try { if (box.m_mapLookatRevealer != null)
                MapDataManager.WantToSetGUIObjVisible(box.m_mapLookatRevealer.MapGUIObjID, eCM_GuiObjectStatus.Normal); }
            catch (Exception error) { Warn("地图图标", error); }
            return marked;
        }
        static void Warn(string stage, Exception error)
        {
            if (Time.time < nextWarning) return;
            nextWarning = Time.time + 10; Plugin.Logger.LogWarning("容器搜寻" + stage + "失败，其他结果仍保留：" + error);
        }
        internal static void Reset() { known.Reset(); nextWarning = 0; }
    }
}
