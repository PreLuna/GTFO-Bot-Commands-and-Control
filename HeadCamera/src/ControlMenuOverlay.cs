using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace PreLuna.HeadCamera
{
    // Native command labels use MeshRenderers, so an overlay Canvas sort order
    // cannot lift them above the video. Mirror only their presentation; native
    // selection, disabled choices, hotkeys and command execution remain authoritative.
    internal sealed class ControlMenuOverlay : IDisposable
    {
        GameObject root;
        RectTransform panel;
        TextMeshProUGUI heading;
        readonly List<TextMeshProUGUI> rows = new();
        readonly Dictionary<long, (Renderer renderer, bool hidden)> hidden = new();
        PUI_CommunicationMenu menu;
        bool warned;
        static GameObject Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name); var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
            rt.sizeDelta = size; rt.anchoredPosition = position; return go;
        }
        static TextMeshProUGUI Label(Transform parent, TMP_FontAsset font)
        {
            var value = Rect("NativeChoice", parent, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero).AddComponent<TextMeshProUGUI>();
            value.font = font; value.fontSize = 22; value.richText = true; value.raycastTarget = false;
            value.alignment = TextAlignmentOptions.MidlineLeft; value.overflowMode = TextOverflowModes.Ellipsis;
            return value;
        }
        void Ensure(TMP_FontAsset font)
        {
            if (root != null) return;
            root = new GameObject("PreLuna.HeadCamera.CommandMenu"); root.AddComponent<RectTransform>();
            var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 65;
            var scale = root.AddComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1920, 1080); scale.matchWidthOrHeight = .5f;
            var go = Rect("Choices", root.transform, new Vector2(1, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(-20, 0));
            panel = go.GetComponent<RectTransform>(); var bg = go.AddComponent<Image>();
            bg.color = new Color(.015f, .025f, .035f, .94f); bg.raycastTarget = false;
            heading = Label(panel, font);
        }
        internal void Tick(bool controlled, TMP_FontAsset font)
        {
            if (!controlled) { Hide(); return; }
            try {
                menu ??= UnityEngine.Object.FindObjectOfType<PUI_CommunicationMenu>();
                if (menu?.m_active != true || menu.m_buttons == null || font == null) { Hide(); return; }
                Ensure(font);
                int count = menu.m_buttons.Count;
                var (width, height) = WindowLayoutPolicy.ReferenceSize(Screen.width, Screen.height);
                float w = Math.Min(680, width - 40), line = Math.Min(50, (height - 100) / Math.Max(2, count + 1));
                panel.sizeDelta = new Vector2(w, line * (count + 1) + 24);
                heading.rectTransform.sizeDelta = new Vector2(w - 32, line); heading.rectTransform.anchoredPosition = new Vector2(16, -8);
                heading.text = menu.m_title?.text ?? "指令"; heading.color = Color.white;
                while (rows.Count < count) rows.Add(Label(panel, font));
                for (int i = 0; i < rows.Count; i++) {
                    var row = rows[i]; var source = i < count ? menu.m_buttons[i] : null;
                    bool active = source != null && source.gameObject.activeInHierarchy;
                    row.gameObject.SetActive(active); if (!active) continue;
                    row.rectTransform.sizeDelta = new Vector2(w - 32, line);
                    row.rectTransform.anchoredPosition = new Vector2(16, -8 - line * (i + 1)); row.fontSize = Math.Min(22, line * .5f);
                    row.text = (source.m_numberText?.text ?? "") + "  " + (source.m_lineText?.text ?? "");
                    row.color = source.m_lineText?.color ?? Color.white;
                    row.fontStyle = i == menu.m_highlightedButtonIndex ? FontStyles.Bold : FontStyles.Normal;
                }
                root.SetActive(true);
                // Hide the native meshes only after the mirror is ready; restore
                // their exact prior flags on close, control exit and disposal.
                var nativeRoot = menu.m_root ?? menu.gameObject;
                foreach (var renderer in nativeRoot.GetComponentsInChildren<Renderer>(true)) {
                    if (renderer == null) continue;
                    if (!hidden.ContainsKey(renderer.Pointer.ToInt64())) hidden.Add(renderer.Pointer.ToInt64(), (renderer, renderer.forceRenderingOff));
                    renderer.forceRenderingOff = true;
                }
            } catch (Exception error) {
                Hide(); if (!warned) { warned = true; Plugin.Instance?.Log.LogWarning("[头戴摄像头] 指令显示层未启用：" + error); }
            }
        }
        internal void Hide()
        {
            foreach (var saved in hidden.Values) if (saved.renderer != null) saved.renderer.forceRenderingOff = saved.hidden;
            hidden.Clear(); if (root != null) root.SetActive(false);
        }
        public void Dispose() { Hide(); if (root != null) UnityEngine.Object.Destroy(root); root = null; panel = null; heading = null; rows.Clear(); menu = null; }
    }
}
