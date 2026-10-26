using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PreLuna.HeadCamera
{
    internal sealed class CameraHud : IDisposable
    {
        GameObject root;
        RectTransform card;
        RawImage image;
        Texture2D testPattern;
        TextMeshProUGUI title, status;
        TextMeshProUGUI aim;
        readonly ControlMenuOverlay commands = new();
        readonly ControlEventOverlay events = new();
        Image marker;
        TextMeshProUGUI markerLabel;
        string lastTitle, lastStatus;
        float retryAt;
        int previousWidth;
        int previousScreenWidth, previousScreenHeight;
        WindowCorner previousCorner;
        float previousX, previousY;
        bool previousEnlarged;
        internal bool IsVisible => root != null && root.activeInHierarchy;
        static GameObject Rect(string name, Transform parent, Vector2 anchors, Vector2 pivot, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = anchors; rt.pivot = pivot;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            return go;
        }
        public bool Ensure()
        {
            if (root != null) return true;
            if (Time.unscaledTime < retryAt) return false;
            retryAt = Time.unscaledTime + 3;
            var template = GuiManager.PlayerLayer?.m_gameEventLog?.m_txtInput;
            if (template?.font == null) return false;
            try
            {
                root = new GameObject("PreLuna.HeadCamera.HUD"); root.AddComponent<RectTransform>();
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 45;
                var scale = root.AddComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scale.referenceResolution = new Vector2(1920, 1080); scale.matchWidthOrHeight = .5f;
                // A passive overlay: no raycaster, selectable, event system, focus change or cursor unlock.
                var panel = Rect("Panel", root.transform, Vector2.one, Vector2.one, new Vector2(360, 255), Vector2.zero);
                card = panel.GetComponent<RectTransform>();
                var background = panel.AddComponent<Image>(); background.color = new Color(.01f, .02f, .025f, .94f); background.raycastTarget = false;
                var picture = Rect("Feed", card, new Vector2(0, 1), new Vector2(0, 1), new Vector2(348, 196), new Vector2(6, -30));
                image = picture.AddComponent<RawImage>(); image.color = Color.white; image.raycastTarget = false;
                var reticle = Rect("ControlAim", image.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(24, 24), Vector2.zero);
                aim = reticle.AddComponent<TextMeshProUGUI>(); aim.font = template.font; aim.fontSize = 22;
                aim.text = "+"; aim.alignment = TextAlignmentOptions.Center; aim.color = Color.white; aim.raycastTarget = false;
                var markerRoot = Rect("NativePing", image.rectTransform, Vector2.zero, new Vector2(.5f, .5f), new Vector2(28, 28), Vector2.zero);
                marker = markerRoot.AddComponent<Image>(); marker.raycastTarget = false; marker.color = Color.cyan;
                var nativePing = GuiManager.NavMarkerLayer?.m_markerPrefabGeneric;
                var nativeIcon = nativePing?.GetComponentInChildren<SpriteRenderer>(true);
                marker.sprite = nativeIcon?.sprite; marker.preserveAspect = true;
                markerLabel = Label("PingTarget", markerRoot.transform, template.font, 17, new Vector2(20, 4));
                markerLabel.rectTransform.sizeDelta = new Vector2(330, 28); markerLabel.color = Color.cyan;
                markerRoot.SetActive(false);
                title = Label("Title", card, template.font, 19, new Vector2(8, -3));
                status = Label("Status", card, template.font, 16, new Vector2(8, -229));
                previousWidth = -1; ApplyLayout(); root.SetActive(false);
                return true;
            }
            catch { Dispose(); retryAt = Time.unscaledTime + 5; throw; }
        }
        static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font, float size, Vector2 position)
        {
            var go = Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(344, 26), position);
            var value = go.AddComponent<TextMeshProUGUI>(); value.font = font; value.fontSize = size;
            value.richText = false; value.raycastTarget = false; value.color = Color.white;
            value.alignment = TextAlignmentOptions.MidlineLeft; value.overflowMode = TextOverflowModes.Truncate;
            return value;
        }
        void ApplyLayout()
        {
            bool enlarged = HeadCameraApi.Enlarged;
            int width = (int)(enlarged ? WindowLayoutPolicy.SpectatorWidth(Screen.width, Screen.height)
                : WindowLayoutPolicy.Width(Math.Clamp(Plugin.WindowWidth.Value, 200, 960), Screen.width, Screen.height));
            var corner = Plugin.Corner.Value; float x = Plugin.MarginX.Value, y = Plugin.MarginY.Value;
            if (enlarged == previousEnlarged && width == previousWidth && corner == previousCorner && x == previousX && y == previousY
                && Screen.width == previousScreenWidth && Screen.height == previousScreenHeight) return;
            previousEnlarged = enlarged; previousWidth = width; previousCorner = corner; previousX = x; previousY = y;
            previousScreenWidth = Screen.width; previousScreenHeight = Screen.height;
            float height = (width - 12) * 9f / 16;
            bool right = corner == WindowCorner.TopRight || corner == WindowCorner.BottomRight;
            bool top = corner == WindowCorner.TopRight || corner == WindowCorner.TopLeft;
            var anchor = new Vector2(right ? 1 : 0, top ? 1 : 0);
            card.anchorMin = card.anchorMax = card.pivot = anchor; card.sizeDelta = new Vector2(width, height + 58);
            // Clamp margins to leave the entire window on ultrawide/narrow screens too.
            float referenceWidth = 1080f * Screen.width / Math.Max(1, Screen.height);
            x = Math.Clamp(x, 0, Math.Max(0, referenceWidth - width)); y = Math.Clamp(y, 0, 1080 - height - 58);
            card.anchoredPosition = new Vector2(right ? -x : x, top ? -y : y);
            if (enlarged) {
                card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
                card.anchoredPosition = Vector2.zero;
            }
            image.rectTransform.sizeDelta = new Vector2(width - 12, height);
            title.rectTransform.sizeDelta = new Vector2(width - 16, 26);
            status.rectTransform.sizeDelta = new Vector2(width - 16, 24); status.rectTransform.anchoredPosition = new Vector2(8, -height - 32);
        }
        public void ShowTestPattern(string message = null)
        {
            testPattern ??= CapturePresentation.CreatePattern();
            Show(testPattern, "头戴摄像头 · 显示测试", message ?? "彩条仅验证窗口显示，不代表队友画面正常");
            aim.enabled = false;
        }
        public void Show(Texture texture, string name, string message)
        {
            if (texture == null) HeadCameraApi.RecordControlAim(null);
            if (!Ensure()) return;
            ApplyLayout(); root.SetActive(true);
            commands.Tick(HeadCameraApi.Busy, title.font);
            events.Tick(HeadCameraApi.Busy, title.font);
            ShowMarker(texture != null);
            image.texture = texture; image.enabled = texture != null;
            aim.enabled = texture != null && HeadCameraApi.Busy;
            if (name != lastTitle) { title.text = name; lastTitle = name; }
            if (message != lastStatus) { status.text = message; lastStatus = message; }
        }
        void ShowMarker(bool hasPicture)
        {
            ControlMarker target = default;
            bool visible = hasPicture && HeadCameraApi.MarkerProjected && HeadCameraApi.TryMarker(out target);
            var point = HeadCameraApi.MarkerViewport;
            visible = visible && point.z > 0 && point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1;
            marker.gameObject.SetActive(visible); if (!visible) return;
            HeadCameraApi.TryMarker(out target);
            marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = new Vector2(point.x, point.y);
            markerLabel.text = (marker.sprite == null ? "◇ " : "") + target.Label;
            // Use the native Ping sprite when ready; the glyph remains visible if the template has not loaded.
            marker.enabled = marker.sprite != null;
        }
        public void Hide() { HeadCameraApi.RecordControlAim(null); commands.Hide(); events.Hide(); if (root != null) root.SetActive(false); }
        public void Dispose()
        {
            commands.Dispose(); events.Dispose(); marker = null; markerLabel = null;
            if (image != null) image.texture = null;
            if (root != null) UnityEngine.Object.Destroy(root);
            if (testPattern != null) UnityEngine.Object.Destroy(testPattern);
            testPattern = null;
            root = null; card = null; image = null; title = status = null; lastTitle = lastStatus = null; retryAt = 0;
            aim = null;
        }
    }
}
