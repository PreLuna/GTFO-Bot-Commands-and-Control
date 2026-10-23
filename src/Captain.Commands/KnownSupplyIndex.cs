using System;
using System.Collections.Generic;
using AIGraph;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal sealed class KnownSupplySnapshot
    {
        public readonly List<CaptainSupplyScope> Scopes = new();
        public readonly Dictionary<int, ItemInLevel> Targets = new();
        public readonly Dictionary<int, string> Details = new();
        public readonly Dictionary<int, (IntPtr Zone, IntPtr Room)> Regions = new();
    }
    internal static class KnownSupplyIndex
    {
        static readonly KnownSupplyLedger<ItemInLevel> known = new();
        static readonly HashSet<IntPtr> visitedZones = new();
        static float nextWarning;
        internal static bool SuppressQueryMarker;
        public static bool HasState => known.Count != 0 || visitedZones.Count != 0;
        public static bool Current(int id, ItemInLevel item) => item != null && known.Current(item.Pointer.ToInt64(), id);
        static bool Active => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor != null;
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(NavMarkerLayer), "PrepareGenericMarker", new[] { typeof(GameObject) }),
                postfix: new HarmonyMethod(typeof(KnownSupplyIndex), nameof(GenericMarker)));
            harmony.Patch(AccessTools.Method(typeof(PlayerAgent), "TriggerMarkerPing"),
                postfix: new HarmonyMethod(typeof(KnownSupplyIndex), nameof(PlayerMarker)));
            harmony.Patch(AccessTools.Method(typeof(ItemInLevel), "OnDespawn"),
                prefix: new HarmonyMethod(typeof(KnownSupplyIndex), nameof(Despawn)));
        }
        static ItemInLevel From(GameObject selected) => selected == null ? null : selected.GetComponentInParent<ItemInLevel>();
        static void GenericMarker(GameObject __0, NavMarker __result)
        {
            if (__result == null || SuppressQueryMarker) return;
            try { Observe(From(__0), SupplySource.QueryMarker); } catch (Exception error) { Warn(error); }
        }
        static void PlayerMarker(PlayerAgent __instance, GameObject __1)
        {
            if (__instance?.Owner?.IsBot != false) return;
            try { Observe(From(__1), SupplySource.PlayerMark); } catch (Exception error) { Warn(error); }
        }
        static void Despawn(ItemInLevel __instance) { if (__instance != null) known.Forget(__instance.Pointer.ToInt64()); }
        static void Warn(Exception error)
        {
            if (Time.time < nextWarning) return;
            nextWarning = Time.time + 10; Plugin.Logger.LogWarning("已知物资记录失败：" + error);
        }
        public static void Observe(ItemInLevel item, SupplySource source)
        {
            if (Active && item != null && ItemRegion.TryResolve(item, out _)) known.Observe(item.Pointer.ToInt64(), item, source);
        }
        // Only human presence opens the selectable area list. Querying a remote zone is not a visit.
        public static void Tick()
        {
            if (!Active) return;
            for (int i = 0; i < 4; i++) {
                var human = PlayerManager.Current?.GetPlayerAgentInSlot(i);
                if (human?.Owner?.IsBot == false && human.CourseNode?.m_zone != null) visitedZones.Add(human.CourseNode.m_zone.Pointer);
            }
        }
        public static string PickupReason(PlayerAgent source, ItemInLevel item)
        {
            if (item == null || item.gameObject == null || item.ItemDataBlock == null
                || !ItemRegion.TryResolve(item, out var node)) return "物品已被拿走或失效，请重开列表";
            if (!ItemRegion.SameSpace(source, node)) return "物品不在当前维度与关卡分层";
            if (SpecialItemPolicy.Special((int)item.Get_pItemData().slot)) return SpecialItemAccess.Reason(item);
            if (!ItemTaskPolicy.SupportedSlot((int)item.Get_pItemData().slot)) return "该槽位不支持指定拿取";
            if (!AutoPickupAdapter.Open(item)) return "箱柜尚未打开，请先开启后重开列表";
            if (!item.gameObject.activeInHierarchy) return "物品交互尚未启用，请重开列表";
            return null;
        }
        static string Zone(AIG_CourseNode node) => node.m_zone.AliasName ?? ("Zone " + node.m_zone.NavInfo?.Number);
        static string Room(AIG_CourseNode node) => node.m_area?.m_navInfo?.Number.ToString() ?? "未知房间";
        public static KnownSupplySnapshot Capture(PlayerAgent source)
        {
            var snapshot = new KnownSupplySnapshot();
            var room = new CaptainSupplyScope("当前房间"); var zone = new CaptainSupplyScope("当前区域 Zone");
            snapshot.Scopes.Add(room); snapshot.Scopes.Add(zone);
            if (source?.CourseNode?.m_zone == null) return snapshot;
            Tick();
            var zones = new Dictionary<IntPtr, CaptainSupplyScope>();
            var entries = new List<KnownSupplyLedger<ItemInLevel>.Entry>(known.Entries);
            entries.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var entry in entries) {
                try {
                var item = entry.Value;
                if (item == null || item.gameObject == null || !ItemRegion.TryResolve(item, out var node)) { known.Forget(entry.Key); continue; }
                if (!ItemRegion.SameSpace(source, node)) continue;
                bool sameZone = source.CourseNode.m_zone.Pointer == node.m_zone.Pointer;
                if (!sameZone && !visitedZones.Contains(node.m_zone.Pointer)) continue;
                if (snapshot.Targets.Count >= 80) { snapshot.Scopes[0].Notice = snapshot.Scopes[1].Notice = "本次最多展示80件；更多物资将在前项移除后显示"; break; }
                string name = Plugin.Catalog?.TranslateItems(item.PublicName) ?? item.PublicName ?? "未知物品";
                string key = item.GetComponentInChildren<LG_GenericTerminalItem>()?.TerminalItemKey;
                if (string.IsNullOrWhiteSpace(key)) key = "本局物品 #" + entry.Id;
                string amount = SpecialItemAccess.AmountText(item);
                string origin = KnownSupplyLedger<ItemInLevel>.Describe(entry.Sources);
                string blocked = PickupReason(source, item);
                var result = new CaptainSupplyResult(entry.Id, name + " " + key + "（" + amount + "）", blocked,
                    item.ItemDataBlock?.persistentID ?? 0, (int)item.Get_pItemData().slot, name, (entry.Sources & SupplySource.RobotSearch) != 0);
                snapshot.Targets[entry.Id] = item;
                snapshot.Regions[entry.Id] = (node.m_zone.Pointer, node.m_area?.Pointer ?? IntPtr.Zero);
                snapshot.Details[entry.Id] = result.Label + "；" + Zone(node) + " / 房间 " + Room(node)
                    + "；" + origin + "；" + (blocked ?? "可指定拿取（仍需核验机器人背包）");
                if (sameZone) {
                    zone.Results.Add(result);
                    if (source.CourseNode.m_area != null && node.m_area?.Pointer == source.CourseNode.m_area.Pointer) room.Results.Add(result);
                }
                if (!zones.TryGetValue(node.m_zone.Pointer, out var area)) {
                    zones[node.m_zone.Pointer] = area = new("已到访区域：" + Zone(node)); snapshot.Scopes.Add(area);
                }
                area.Results.Add(result);
                } catch (Exception error) { Warn(error); }
            }
            return snapshot;
        }
        public static void Reset() { known.Reset(); visitedZones.Clear(); nextWarning = 0; SuppressQueryMarker = false; }
    }
}
