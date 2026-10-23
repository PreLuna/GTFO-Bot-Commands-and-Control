using System;
using System.Collections.Generic;
using AIGraph;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    // Instant host query. No bot, navigation action, broker lease, warp, or return task.
    internal static class SearchTaskAdapter
    {
        static float nextQuery, nextWarning;
        internal static bool HasState => false;
        internal static bool Owns(PlayerAIBot bot) => false;
        internal static string Status(PlayerAIBot bot) => null;
        internal static bool Cancel(PlayerAIBot bot, bool returning) => false;
        internal static void Control(PlayerAIBot bot, bool pause) => ItemTaskAdapter.Feedback("搜寻已立即完成，无需暂停或继续");
        internal static void Tick() { }
        internal static void Reset() { nextQuery = nextWarning = 0; SearchMarker.Reset(); }
        internal static void Issue(PlayerAgent source, AIG_CourseNode origin, int scope, int filter,
            SearchTargetKind kind = SearchTargetKind.Supplies)
        {
            if (!SNet.IsMaster || GameStateManager.CurrentStateName != eGameStateName.InLevel || Builder.CurrentFloor == null
                || source?.Owner?.IsBot != false || scope is < 0 or > 1 || filter < 0 || filter > (kind == SearchTargetKind.Interactions ? 3 : kind == SearchTargetKind.Supplies ? 4 : 2)
                || !Enum.IsDefined(typeof(SearchTargetKind), kind) || !SearchTraversal.InScope(source.CourseNode, origin, scope)
                || !ItemRegion.SameSpace(source, origin)) {
                ItemTaskAdapter.Feedback("搜索范围已失效；请在当前房间／区域重新打开菜单"); return;
            }
            if (Time.time < nextQuery) { ItemTaskAdapter.Feedback("刚刚完成统计，请稍后重试；已有结果仍保留"); return; }
            nextQuery = Time.time + 1;
            var nodes = kind == SearchTargetKind.Interactions ? new Dictionary<long, AIG_CourseNode>() : null;
            var reachable = SearchTraversal.Reachable(source, origin, origin, scope, out bool truncated, nodes);
            int found = 0, packs = 0, consumables = 0, special = 0, marked = 0, failures = 0, attempts = 0;
            var seen = new HashSet<IntPtr>(); var boxes = new HashSet<IntPtr>();
            int examined = 0;
            if (kind == SearchTargetKind.Interactions) {
                var candidates = KnownInteractionIndex.Candidates(nodes, filter, out bool interactionTruncated); truncated |= interactionTruncated;
                foreach (var point in candidates) try {
                    examined++;
                    if (!KnownInteractionIndex.Resolve(point, out var node) || !SearchTraversal.InScope(node, origin, scope)
                        || !ItemRegion.SameSpace(source, node) || !reachable.Contains(node.Pointer.ToInt64())
                        || !KnownInteractionIndex.Matches(point, filter) || !seen.Add(point.Value.Pointer)) continue;
                    if (!KnownInteractionIndex.Observe(point)) { truncated = true; continue; }
                    found++;
                    if (attempts++ < 80 && KnownInteractionIndex.Mark(point)) marked++;
                } catch (Exception error) { failures++; Warn("交互点统计／标记", error); }
            } else if (kind == SearchTargetKind.Containers) {
                var candidates = KnownContainerIndex.Candidates(out bool containerTruncated); truncated |= containerTruncated;
                foreach (var box in candidates) try {
                    examined++;
                    if (!KnownContainerIndex.Resolve(box, out var node) || !SearchTraversal.InScope(node, origin, scope)
                        || !reachable.Contains(node.Pointer.ToInt64()) || !KnownContainerIndex.Matches(box, filter) || !seen.Add(box.Pointer)) continue;
                    if (!KnownContainerIndex.Observe(box)) { truncated = true; continue; }
                    found++;
                    if (attempts++ < 80 && KnownContainerIndex.Mark(box)) marked++;
                } catch (Exception error) { failures++; Warn("容器统计／标记", error); }
            } else if (kind == SearchTargetKind.Terminals) {
                var candidates = KnownTerminalIndex.Candidates(out bool registryTruncated); truncated |= registryTruncated;
                foreach (var terminal in candidates) try {
                    examined++;
                    if (!KnownTerminalIndex.Resolve(terminal, out var node) || !SearchTraversal.InScope(node, origin, scope)
                        || !reachable.Contains(node.Pointer.ToInt64()) || !seen.Add(terminal.Pointer)) continue;
                    if (!KnownTerminalIndex.Observe(terminal)) continue;
                    found++;
                    if (attempts++ < 80 && KnownTerminalIndex.Mark(terminal)) marked++;
                } catch (Exception error) { failures++; Warn("终端统计／标记", error); }
            } else {
                // Snapshot the existing placed-item index once; closed cabinet contents remain queryable.
                var candidates = new List<ItemInLevel>(ItemWorldIndex.Items);
                foreach (var item in candidates) try {
                    if (++examined > 8192) { truncated = true; break; }
                    if (item == null || item.gameObject == null || item.ItemDataBlock == null || !ItemRegion.TryResolve(item, out var node)
                        || !SearchTraversal.InScope(node, origin, scope) || !reachable.Contains(node.Pointer.ToInt64())) continue;
                    var slot = item.Get_pItemData().slot;
                    if (!SpecialItemPolicy.Matches((int)slot, filter) || !seen.Add(item.Pointer)) continue;
                    KnownSupplyIndex.Observe(item, SupplySource.RobotSearch); found++;
                    if (slot == InventorySlot.ResourcePack) packs++; else if (slot == InventorySlot.Consumable) consumables++; else special++;
                    // Bound HUD load, but continue recording and counting every eligible object.
                    if (attempts++ < 80 && SearchMarker.Place(item.gameObject,
                        "搜寻：" + (Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName ?? "未知物品")
                        + (AutoPickupAdapter.Open(item) ? "" : "（柜内·未开启）"), NavMarkerOption.WaypointTitleDistance)) marked++;
                    var box = item.container?.m_core?.TryCast<LG_WeakResourceContainer>();
                    if (box?.m_mapLookatRevealer != null && boxes.Add(item.container.Pointer)) try {
                        MapDataManager.WantToSetGUIObjVisible(box.m_mapLookatRevealer.MapGUIObjID, eCM_GuiObjectStatus.Normal);
                    } catch (Exception error) { failures++; Warn("箱柜地图图标", error); }
                } catch (Exception error) { failures++; Warn("物资统计／标记", error); }
            }
            string task = kind == SearchTargetKind.Terminals ? "搜寻终端" : kind == SearchTargetKind.Containers ? "搜寻容器" : kind == SearchTargetKind.Interactions ? "搜寻交互点" : "搜寻物资";
            string label = scope == 0 ? "当前房间" : "当前区域 Zone（普通门可达）";
            string detail = "共 " + found + " 件" + (kind == SearchTargetKind.Supplies ? "（资源包 " + packs + "、消耗品 " + consumables + "、任务／特殊物品 " + special + "）" : kind == SearchTargetKind.Terminals ? "终端" : kind == SearchTargetKind.Containers ? "容器" : "交互点")
                + "；本次显示标记 " + marked + "；统计已汇入搜索结果，机器人任务不变";
            if (found > marked) detail += "；未显示标记的对象也计入统计（临时标记最多80个）";
            if (found > (kind == SearchTargetKind.Supplies ? 80 : 64)) detail += "；结果列表有显示限额，可缩小到当前房间查看";
            if (failures > 0) detail += "；异常对象／地图操作 " + failures + " 项，详情见日志";
            if (truncated) detail += "；范围／索引达到处理上限，本次统计可能不完整";
            TaskBoard.Journal.Record(new object(), source.PlayerSlotIndex, source.Owner.Pointer.ToInt64(), "即时查询", task, label,
                TaskBoardState.Complete, "统计完成", detail, Time.time);
            Plugin.Logger.LogInfo("[即时搜寻] " + task + " / " + label + ";候选=" + examined + ";截断=" + truncated + ";" + detail);
            ItemTaskAdapter.Feedback(task + " / " + label + "：" + detail);
        }
        static void Warn(string stage, Exception error)
        {
            if (Time.time < nextWarning) return;
            nextWarning = Time.time + 10; Plugin.Logger.LogWarning("即时搜寻" + stage + "失败（其余对象继续处理）：" + error);
        }
    }
}
