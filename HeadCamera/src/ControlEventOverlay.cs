using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace PreLuna.HeadCamera
{
    // A passive copy of native chat/events; native input, log ownership and networking stay unchanged.
    internal sealed class ControlEventOverlay : IDisposable
    {
        GameObject root;
        TextMeshProUGUI text;
        RectTransform panel;
        readonly Dictionary<long, (Renderer renderer, bool hidden)> hidden = new();
        readonly Dictionary<long, string> observed = new();
        readonly List<(string text, float time)> recent = new();
        bool warned;
        internal void Tick(bool controlled, TMP_FontAsset font)
        {
            if (!controlled) { Hide(); return; }
            try {
                var log = GuiManager.PlayerLayer?.m_gameEventLog;
                if (font == null || log?.m_logItems == null) { Hide(); return; }
                if (root == null) {
                    root = new GameObject("PreLuna.HeadCamera.Events"); root.AddComponent<RectTransform>();
                    var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 60;
                    var scale = root.AddComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scale.referenceResolution = new Vector2(1920, 1080); scale.matchWidthOrHeight = .5f;
                    var go = new GameObject("NativeEvents"); var rt = go.AddComponent<RectTransform>(); rt.SetParent(root.transform, false);
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0); rt.anchoredPosition = new Vector2(24, 74);
                    panel = rt;
                    var bg = go.AddComponent<Image>(); bg.color = new Color(.01f, .02f, .025f, .86f); bg.raycastTarget = false;
                    var label = new GameObject("Text"); var tr = label.AddComponent<RectTransform>(); tr.SetParent(rt, false);
                    tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0, 0); tr.anchoredPosition = new Vector2(12, 8);
                    text = label.AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = 20;
                    text.richText = true; text.raycastTarget = false; text.color = Color.white;
                    text.alignment = TextAlignmentOptions.BottomLeft; text.overflowMode = TextOverflowModes.Ellipsis;
                }
                var current = new HashSet<long>();
                // Observe the full native pool: versions may prepend or append new entries.
                for (int i = 0; i < log.m_logItems.Count; i++) {
                    var item = log.m_logItems[i]; if (item?.m_text == null) continue;
                    long id = item.Pointer.ToInt64(); string value = item.m_text.text; current.Add(id);
                    if (!string.IsNullOrWhiteSpace(value) && (!observed.TryGetValue(id, out var old) || old != value)) {
                        recent.Add((value, Time.unscaledTime)); observed[id] = value;
                    }
                }
                foreach (long id in new List<long>(observed.Keys)) if (!current.Contains(id)) observed.Remove(id);
                recent.RemoveAll(v => Time.unscaledTime < v.time || Time.unscaledTime - v.time > 7);
                while (recent.Count > 4) recent.RemoveAt(0);
                var lines = new List<string>(); foreach (var entry in recent) lines.Add(entry.text);
                if (PlayerChatManager.InChatMode) lines.Add(log.m_txtInput?.text ?? "> " );
                text.text = string.Join("\n", lines);
                float width = Math.Min(800, WindowLayoutPolicy.ReferenceSize(Screen.width, Screen.height).Width - 48);
                text.rectTransform.sizeDelta = new Vector2(width - 24, 150);
                panel.sizeDelta = new Vector2(width, 166);
                root.SetActive(lines.Count > 0);
                if (lines.Count == 0) { Restore(); return; }
                foreach (var source in new[] { log.m_chatActiveRoot, log.m_chatInActiveRoot }) {
                    if (source == null) continue;
                    foreach (var renderer in source.GetComponentsInChildren<Renderer>(true)) {
                        if (renderer == null) continue;
                        if (!hidden.ContainsKey(renderer.Pointer.ToInt64())) hidden.Add(renderer.Pointer.ToInt64(), (renderer, renderer.forceRenderingOff));
                        renderer.forceRenderingOff = true;
                    }
                }
            } catch (Exception error) {
                Hide(); if (!warned) { warned = true; Plugin.Instance?.Log.LogWarning("[头戴摄像头] 事件显示层未启用：" + error); }
            }
        }
        void Restore() { foreach (var saved in hidden.Values) if (saved.renderer != null) saved.renderer.forceRenderingOff = saved.hidden; hidden.Clear(); }
        internal void Hide() { Restore(); if (root != null) root.SetActive(false); recent.Clear(); observed.Clear(); }
        public void Dispose() { Hide(); if (root != null) UnityEngine.Object.Destroy(root); root = null; text = null; panel = null; }
    }
}
