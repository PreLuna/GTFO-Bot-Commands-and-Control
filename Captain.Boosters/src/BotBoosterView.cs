using System;
using System.Collections.Generic;
using System.Text;
using CellMenu;
using UnityEngine;

namespace PreLuna.Captain.Boosters
{
    // This class only repairs the visible loadout's existing UI. It never enables a page or a player bar.
    internal static class BotBoosterView
    {
        public static bool PageVisible(CM_PlayerLobbyBar bar)
        {
            var page = bar?.m_parentPage;
            // m_menu is unused/null in the installed game's CellUI. The page's real
            // ancestry and GuiLayer visibility already track whether Tab is open.
            return page != null && page.m_isActive && page.gameObject.activeInHierarchy
                && bar.gameObject.activeInHierarchy
                && page.m_guiLayer != null && page.m_guiLayer.IsVisible();
        }

        public static bool Restore(GameObject target, Transform boundary)
        {
            if (target == null || boundary == null || !boundary.gameObject.activeInHierarchy) return false;
            // Validate ancestry before writing anything: never open the surrounding menu/page to show one item.
            var node = target.transform;
            int count = 0;
            while (node != null && node.Pointer != boundary.Pointer && count++ < 32) node = node.parent;
            if (node == null || node.Pointer != boundary.Pointer) return false;
            node = target.transform;
            while (node.Pointer != boundary.Pointer)
            {
                if (!node.gameObject.activeSelf) node.gameObject.SetActive(true);
                node = node.parent;
            }
            return target.activeInHierarchy;
        }

        public static bool SlotVisible(CM_BoosterImplantSlot slot, bool selected)
        {
            if (slot == null || !slot.gameObject.activeInHierarchy
                || slot.m_boxRoot != null && !slot.m_boxRoot.activeInHierarchy) return false;
            if (!selected) return slot.m_emptyIcon != null && slot.m_emptyIcon.gameObject.activeInHierarchy && slot.m_emptyIcon.enabled;
            var icon = slot.m_boosterIcon;
            return icon != null && icon.IsBoosterIconEnabled && icon.IsBoosterIconActive
                && SpriteVisible(icon.m_iconBackgroundRenderer?.Sprite)
                && SpriteVisible(icon.m_iconForegroundRenderer?.Sprite);
        }
        static bool SpriteVisible(SpriteRenderer sprite) => sprite != null && sprite.gameObject.activeInHierarchy
            && sprite.enabled && sprite.color.a > .001f;

        public static void RestoreSlot(CM_BoosterImplantSlot slot, Transform boundary, bool selected)
        {
            Restore(slot.gameObject, boundary);
            if (slot.m_boxRoot != null) Restore(slot.m_boxRoot, boundary);
            if (selected)
            {
                var icon = slot.m_boosterIcon;
                if (icon != null)
                {
                    Restore(icon.gameObject, boundary);
                    if (icon.m_iconBackgroundRenderer != null) Restore(icon.m_iconBackgroundRenderer.gameObject, boundary);
                    if (icon.m_iconForegroundRenderer != null) Restore(icon.m_iconForegroundRenderer.gameObject, boundary);
                }
            }
            else if (slot.m_emptyIcon != null)
            { Restore(slot.m_emptyIcon.gameObject, boundary); slot.m_emptyIcon.enabled = true; }
        }

        sealed class Trace { public string Stamp; public float NextLog; }
        static readonly Dictionary<IntPtr, Trace> traces = new();
        static readonly Dictionary<IntPtr, Trace> gates = new();
        static readonly Dictionary<IntPtr, Trace> pages = new();
        public static void ObservePage(CM_PageLoadout page, bool diagnostics)
        {
            if (!diagnostics || page == null) return;
            int nativeBars = page.m_playerLobbyBars?.Length ?? -1;
            int registeredBars = CM_PlayerLobbyBar.instances?.Count ?? -1;
            string stamp = page.m_isActive + ":" + page.gameObject.activeInHierarchy
                + ":" + page.m_guiLayer?.IsVisible() + ":" + nativeBars + ":" + registeredBars
                + ":" + GameStateManager.IsReady;
            if (!Changed(pages, page.Pointer, stamp)) return;
            Plugin.Logger.LogInfo("[机器人强化剂页面] state=" + GameStateManager.CurrentStateName
                + " ready=" + GameStateManager.IsReady + " pageActive=" + page.m_isActive
                + " pageHierarchy=" + page.gameObject.activeInHierarchy + " layerVisible=" + page.m_guiLayer?.IsVisible()
                + " nativeBars=" + nativeBars + " registeredBars=" + registeredBars);
        }
        public static void ObserveGate(CM_PlayerLobbyBar bar, bool master, bool current, bool diagnostics)
        {
            if (!diagnostics || bar == null) return;
            var page = bar.m_parentPage; var layer = page?.m_guiLayer;
            string reason = !master ? "非房主" : !current ? "机器人槽位未绑定"
                : page == null ? "无父页面" : !page.m_isActive ? "页面未激活"
                : !page.gameObject.activeInHierarchy ? "页面层级隐藏"
                : !bar.gameObject.activeInHierarchy ? "人物栏层级隐藏"
                : layer == null ? "无菜单图层" : !layer.IsVisible() ? "菜单图层隐藏" : "允许显示";
            string stamp = reason + ":" + GameStateManager.CurrentStateName + ":" + GameStateManager.IsReady;
            if (!Changed(gates, bar.Pointer, stamp)) return;
            Plugin.Logger.LogInfo("[机器人强化剂入口] bot=" + bar.m_player?.NickName + " reason=" + reason
                + " state=" + GameStateManager.CurrentStateName + " ready=" + GameStateManager.IsReady
                + " master=" + master + " current=" + current + " pageActive=" + page?.m_isActive
                + " pageHierarchy=" + page?.gameObject.activeInHierarchy + " layerVisible=" + layer?.IsVisible()
                + " bar=" + Path(bar.transform, page?.transform)
                + " holder=" + Path(bar.m_boosterImplantSlotHolder?.transform, page?.transform));
        }
        static bool Changed(Dictionary<IntPtr, Trace> collection, IntPtr pointer, string stamp)
        {
            if (!collection.TryGetValue(pointer, out var trace))
            {
                if (collection.Count > 32) collection.Clear();
                collection[pointer] = trace = new Trace();
            }
            if (trace.Stamp == stamp || Time.realtimeSinceStartup < trace.NextLog) return false;
            trace.Stamp = stamp; trace.NextLog = Time.realtimeSinceStartup + 2;
            return true;
        }
        public static void Observe(CM_PlayerLobbyBar bar, bool editable, bool repaired, bool diagnostics)
        {
            if (!diagnostics || bar == null) return;
            var holder = bar.m_boosterImplantSlotHolder;
            int visibleSlots = 0;
            if (holder?.m_categorySlots != null)
                foreach (var slot in holder.m_categorySlots.Values)
                    if (slot != null && SlotVisible(slot, slot.m_boosterInSlot != null)) visibleSlots |= 1 << (int)slot.m_category;
            string stamp = GameStateManager.CurrentStateName + ":" + GameStateManager.IsReady + ":" + editable
                + ":" + PageVisible(bar) + ":" + (holder?.gameObject.activeInHierarchy == true) + ":" + repaired
                + ":" + visibleSlots + ":" + (bar.m_boosterImplantsHeaderText?.gameObject.activeInHierarchy == true);
            if (!traces.TryGetValue(bar.Pointer, out var trace))
            {
                if (traces.Count > 32) traces.Clear();
                traces[bar.Pointer] = trace = new Trace();
            }
            if (trace.Stamp == stamp || Time.realtimeSinceStartup < trace.NextLog) return;
            trace.Stamp = stamp; trace.NextLog = Time.realtimeSinceStartup + 2;
            Plugin.Logger.LogInfo("[机器人强化剂界面] bot=" + bar.m_player?.NickName + " state=" + GameStateManager.CurrentStateName
                + " ready=" + GameStateManager.IsReady + " mode=" + (editable ? "编辑" : "只读") + " repaired=" + repaired
                + " beforeSlotMask=" + visibleSlots + " align=" + Path(bar.m_boosterImplantAlign, bar.transform)
                + " holder=" + Path(holder?.transform, bar.transform) + " header=" + Path(bar.m_boosterImplantsHeaderText?.transform, bar.transform));
        }
        static string Path(Transform node, Transform boundary)
        {
            var text = new StringBuilder(); int count = 0;
            while (node != null && count++ < 32)
            {
                if (text.Length > 0) text.Append(" <- ");
                text.Append(node.gameObject.name).Append('[').Append(node.gameObject.activeSelf ? "self+" : "self-")
                    .Append(node.gameObject.activeInHierarchy ? ",visible]" : ",hidden]");
                if (boundary != null && node.Pointer == boundary.Pointer) break;
                node = node.parent;
            }
            return text.ToString();
        }
        public static void Reset() { traces.Clear(); gates.Clear(); pages.Clear(); }
    }
}
