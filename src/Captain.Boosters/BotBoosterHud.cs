using System;
using CellMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace PreLuna.Captain.Boosters
{
    internal static class BotBoosterHud
    {
        static GameObject root;
        static readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[4];
        static readonly Image[,] icons = new Image[4, 3], backgrounds = new Image[4, 3];
        static readonly string[] last = new string[4];
        static float retryAt;
        static bool warned;
        static GameObject Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var obj = new GameObject(name); var rect = obj.AddComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(1, 0);
            rect.sizeDelta = size; rect.anchoredPosition = position; return obj;
        }
        static bool Create()
        {
            if (root != null) return true;
            if (Time.unscaledTime < retryAt) return false;
            retryAt = Time.unscaledTime + 5;
            try
            {
                var font = GuiManager.PlayerLayer?.m_gameEventLog?.m_txtInput?.font;
                if (font == null) return false;
                root = new GameObject("PreLuna.Captain.BotBoosterHUD"); root.AddComponent<RectTransform>();
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 49;
                var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
                for (int slot = 0; slot < 4; slot++)
                {
                    var row = Rect("Bot" + slot, root.transform, new Vector2(340, 45), new Vector2(-24, 85 + slot * 48));
                    var panel = row.AddComponent<Image>(); panel.color = new Color(.025f, .035f, .04f, .78f); panel.raycastTarget = false;
                    var label = Rect("State", row.transform, new Vector2(238, 42), new Vector2(-98, 1));
                    labels[slot] = label.AddComponent<TextMeshProUGUI>(); labels[slot].font = font; labels[slot].fontSize = 14;
                    labels[slot].richText = false; labels[slot].raycastTarget = false; labels[slot].color = Color.white;
                    labels[slot].overflowMode = TextOverflowModes.Truncate;
                    for (int category = 0; category < 3; category++)
                    {
                        var icon = Rect("Category" + category, row.transform, new Vector2(28, 28), new Vector2(-8 - (2 - category) * 30, 9));
                        backgrounds[slot, category] = icon.AddComponent<Image>(); backgrounds[slot, category].raycastTarget = false;
                        var fg = Rect("Effect", icon.transform, new Vector2(28, 28), Vector2.zero);
                        icons[slot, category] = fg.AddComponent<Image>(); icons[slot, category].raycastTarget = false; icons[slot, category].preserveAspect = true;
                    }
                    row.SetActive(false);
                }
                return true;
            }
            catch (Exception error) { Reset(); retryAt = Time.unscaledTime + 10; if (!warned) { warned = true; Plugin.Logger.LogWarning("机器人强化剂 HUD 创建延后：" + error.Message); } return false; }
        }
        public static void Show(int slot, string name, BoosterSlotView[] states)
        {
            if (!Create()) return;
            labels[slot].transform.parent.gameObject.SetActive(true);
            string key = name + string.Join("/", states);
            if (last[slot] == key) return;
            var template = GuiManager.PlayerLayer?.m_playerStatus?.m_boosterIconActiveDisplay?.m_boosterIcons;
            var sample = template != null && template.Length > 0 ? template[0] : null;
            labels[slot].text = name + " · 强化剂\n低：" + BoosterViewPolicy.Label(states[0].Status)
                + "  高：" + BoosterViewPolicy.Label(states[1].Status) + "  特：" + BoosterViewPolicy.Label(states[2].Status);
            for (int category = 0; category < 3; category++)
            {
                var state = states[category]; var background = backgrounds[slot, category]; var image = icons[slot, category];
                background.sprite = category == 0 ? sample?.m_basicBackground : category == 1 ? sample?.m_advancedBackground : sample?.m_specializedBackground;
                background.color = state.Status == BoosterViewStatus.Active ? new Color(.5f, .9f, .65f, .95f) : new Color(.55f, .6f, .65f, .85f);
                image.sprite = null;
                var effect = state.Id == 0 ? null : GameData.BoosterImplantTemplateDataBlock.GetBlock(state.Id);
                if (sample?.m_spriteEffectPairs != null && effect != null) foreach (var pair in sample.m_spriteEffectPairs)
                    if (pair != null && pair.BoosterEffectCategory == effect.MainEffectType)
                    { image.sprite = category == 0 ? pair.BasicSprite : category == 1 ? pair.AdvancedSprite : pair.SpecializedSprite; break; }
                image.enabled = image.sprite != null; image.color = state.Status == BoosterViewStatus.Active ? Color.white : new Color(.6f, .6f, .6f, .8f);
            }
            // Retry missing native sprites later, without recreating the canvas.
            last[slot] = sample == null ? null : key;
        }
        public static void Hide(int slot) { if (labels[slot] != null) labels[slot].transform.parent.gameObject.SetActive(false); last[slot] = null; }
        public static void Reset()
        {
            if (root != null) UnityEngine.Object.Destroy(root); root = null; retryAt = 0;
            Array.Clear(labels, 0, 4); Array.Clear(icons, 0, 12); Array.Clear(backgrounds, 0, 12); Array.Clear(last, 0, 4);
        }
    }
}
