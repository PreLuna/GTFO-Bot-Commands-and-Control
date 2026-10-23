using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace PreLuna.Captain.Tasks
{
    internal static class PickupPromptHud
    {
        static GameObject root;
        static TextMeshProUGUI text;
        static RectTransform fill;
        static float retryAt;
        static bool warned;
        static string previous;
        static GameObject Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 size, Vector2 position)
        {
            var obj = new GameObject(name);
            var rect = obj.AddComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = new Vector2(1, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = position; return obj;
        }
        static bool Create()
        {
            if (root != null) return true;
            if (Time.unscaledTime < retryAt || GuiManager.PlayerLayer == null) return false;
            retryAt = Time.unscaledTime + 5;
            try
            {
                // Inherit GTFO's font and fallback glyphs instead of shipping another font.
                var template = GuiManager.PlayerLayer.m_gameEventLog?.m_txtInput;
                if (template == null || template.font == null) return false;
                root = new GameObject("PreLuna.Captain.PickupPromptHUD");
                root.AddComponent<RectTransform>();
                // Standalone overlay canvas: nesting under GTFO's world-space HUD would
                // make Unity inherit its render mode and invalidate screen-sized placement.
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
                var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
                // No GraphicRaycaster, Selectable, focus actor or cursor changes.
                var card = Rect("Card", root.transform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(350, 100), new Vector2(-24, -135));
                var background = card.AddComponent<Image>(); background.color = new Color(.025f, .035f, .04f, .84f); background.raycastTarget = false;
                var label = Rect("Text", card.transform, Vector2.zero, Vector2.one, new Vector2(-22, -18), new Vector2(-11, 4));
                text = label.AddComponent<TextMeshProUGUI>(); text.font = template.font; text.fontSize = 18;
                text.color = Color.white; text.richText = false; text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.MidlineLeft; text.overflowMode = TextOverflowModes.Truncate;
                var track = Rect("CountdownTrack", card.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(-22, 4), new Vector2(-11, 8));
                var trackImage = track.AddComponent<Image>(); trackImage.color = new Color(.3f, .35f, .4f, .5f); trackImage.raycastTarget = false;
                var progress = Rect("Countdown", track.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                fill = progress.GetComponent<RectTransform>(); fill.pivot = new Vector2(0, .5f);
                var image = progress.AddComponent<Image>(); image.color = new Color(.45f, .8f, .7f, .95f); image.raycastTarget = false;
                root.SetActive(false); previous = null; return true;
            }
            catch (Exception error)
            {
                Reset(); retryAt = Time.unscaledTime + 10;
                if (!warned) { warned = true; Plugin.Logger.LogWarning("拾取询问 HUD 暂不可用，倒计时继续；从未显示的询问超时后延后处理：" + error.Message); }
                return false;
            }
        }
        public static bool Show(string bot, string item, float remaining, float duration, int waiting, bool inputAllowed)
        {
            if (!Create()) return false;
            root.SetActive(true);
            string content = bot + "：可以拿起“" + item + "”吗？\n" + (inputAllowed ? "Y 同意 / N 拒绝" : "当前不接收按键答复") + " · " + Math.Ceiling(remaining) + " 秒后同意"
                + (waiting > 0 ? "\n另有 " + waiting + " 项等待询问" : "");
            if (previous != content) { text.text = content; previous = content; }
            fill.anchorMax = new Vector2(Math.Clamp(remaining / Math.Max(1, duration), 0, 1), 1);
            return true;
        }
        public static void Hide() { if (root != null && root.activeSelf) root.SetActive(false); previous = null; }
        public static void Reset()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null; text = null; fill = null; previous = null; retryAt = 0;
        }
    }
}
