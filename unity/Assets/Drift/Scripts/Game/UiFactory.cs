using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Drift.Game
{
    /// <summary>Builds the uGUI hierarchy in code, so the project needs no prefabs or art.</summary>
    public static class UiFactory
    {
        public static readonly Color Panel = new Color(0.05f, 0.06f, 0.09f, 0.82f);
        public static readonly Color Accent = new Color(1.00f, 0.55f, 0.12f);
        public static readonly Color Nitro = new Color(0.25f, 0.75f, 1.00f);
        public static readonly Color TextColor = new Color(0.96f, 0.96f, 0.98f);
        public static readonly Color Muted = new Color(0.70f, 0.73f, 0.80f);
        public static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.16f);

        private static Font _font;

        public static Font UiFont
        {
            get
            {
                if (_font == null) _font = Compat.DefaultFont();
                return _font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Stretch to fill the parent.</summary>
        public static RectTransform Fill(Transform parent, string name)
        {
            RectTransform rt = Rect(parent, name);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Anchor and pivot at one of the nine positions, keeping size.</summary>
        public static void Anchor(RectTransform rt, TextAnchor anchor)
        {
            Vector2 p;
            switch (anchor)
            {
                case TextAnchor.UpperLeft: p = new Vector2(0f, 1f); break;
                case TextAnchor.UpperCenter: p = new Vector2(0.5f, 1f); break;
                case TextAnchor.UpperRight: p = new Vector2(1f, 1f); break;
                case TextAnchor.MiddleLeft: p = new Vector2(0f, 0.5f); break;
                case TextAnchor.MiddleRight: p = new Vector2(1f, 0.5f); break;
                case TextAnchor.LowerLeft: p = new Vector2(0f, 0f); break;
                case TextAnchor.LowerCenter: p = new Vector2(0.5f, 0f); break;
                case TextAnchor.LowerRight: p = new Vector2(1f, 0f); break;
                default: p = new Vector2(0.5f, 0.5f); break;
            }
            rt.anchorMin = p;
            rt.anchorMax = p;
            rt.pivot = p;
        }

        public static RectTransform Place(RectTransform rt, TextAnchor anchor, Vector2 position, Vector2 size)
        {
            Anchor(rt, anchor);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            RectTransform rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align)
        {
            RectTransform rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor, Vector2 position, Vector2 boxSize, TextAnchor align)
        {
            Text t = Label(parent, text, size, color, align);
            Place(t.rectTransform, anchor, position, boxSize);
            return t;
        }

        public static Button CreateButton(Transform parent, string text, int fontSize, Vector2 size, Action onClick)
        {
            Image img = CreateImage(parent, text + "Button", Accent);
            img.rectTransform.sizeDelta = size;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            Text label = Label(img.rectTransform, text, fontSize, new Color(0.08f, 0.08f, 0.1f), TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>A touch control: a translucent pad with a label and a HoldButton.</summary>
        public static HoldButton TouchPad(Transform parent, string text, int fontSize, TextAnchor anchor, Vector2 position, Vector2 size, Color tint)
        {
            Image img = CreateImage(parent, text + "Pad", tint);
            Place(img.rectTransform, anchor, position, size);
            img.raycastTarget = true;
            Text label = Label(img.rectTransform, text, fontSize, TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return img.gameObject.AddComponent<HoldButton>();
        }

        public static InputField CreateInputField(Transform parent, string placeholder, string value, int fontSize, Vector2 size)
        {
            Image bg = CreateImage(parent, "Input", new Color(1f, 1f, 1f, 0.12f));
            bg.rectTransform.sizeDelta = size;
            var field = bg.gameObject.AddComponent<InputField>();

            Text text = Label(bg.rectTransform, string.Empty, fontSize, TextColor, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            Stretch(text.rectTransform, 18f);
            Text ph = Label(bg.rectTransform, placeholder, fontSize, Muted, TextAnchor.MiddleLeft);
            ph.fontStyle = FontStyle.Italic;
            Stretch(ph.rectTransform, 18f);

            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = bg;
            field.text = value;
            return field;
        }

        public static void Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, 0f);
            rt.offsetMax = new Vector2(-padding, 0f);
        }

        /// <summary>A vertical stack: children are laid out top to bottom with a gap.</summary>
        public static RectTransform Column(Transform parent, string name, float spacing)
        {
            RectTransform rt = Rect(parent, name);
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return rt;
        }
    }
}
