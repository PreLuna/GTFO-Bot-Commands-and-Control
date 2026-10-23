using System;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class SearchMarker
    {
        static float nextWarning;
        internal static bool Place(GameObject target, string title, NavMarkerOption options)
        {
            var layer = GuiManager.NavMarkerLayer;
            if (target == null || layer == null) return false;
            bool previous = KnownSupplyIndex.SuppressQueryMarker;
            NavMarker marker = null;
            try {
                KnownSupplyIndex.SuppressQueryMarker = true;
                marker = layer.PrepareGenericMarker(target);
                if (marker == null) return false;
                // PrepareGenericMarker does NOT initialize m_stateOptions. Native SetVisible
                // and the camera's SetState dereference it. Match PlaceCustomMarker's order.
                marker.SetVisualStates(options, options, NavMarkerOption.Empty, NavMarkerOption.Empty);
                marker.SetTitle(title);
                marker.SetVisible(true);
                if (marker.m_fadeRoutine != null) CoroutineManager.StopCoroutine(marker.m_fadeRoutine);
                marker.m_fadeRoutine = CoroutineManager.StartCoroutine(layer.FadeMarkerOverTime(marker, marker.name, .1f, 20, false), null);
                return true;
            } catch (Exception error) {
                // Only remove this failed marker. Never touch other mods' markers or map data.
                Exception failure = error;
                if (marker != null) try { layer.RemoveMarker(marker); }
                    catch (Exception cleanup) { failure = new AggregateException("标记创建与失败清理均出错", error, cleanup); }
                Warn("创建失败", failure); return false;
            } finally { KnownSupplyIndex.SuppressQueryMarker = previous; }
        }
        static void Warn(string stage, Exception error)
        {
            if (Time.time < nextWarning) return;
            nextWarning = Time.time + 10; Plugin.Logger.LogWarning("搜寻临时标记" + stage + "，结果仍保留：" + error);
        }
        internal static void Reset() { nextWarning = 0; }
    }
}
