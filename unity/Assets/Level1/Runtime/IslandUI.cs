using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace EcoBotBuilders.Level1.View
{
    /// <summary>Fixed design coordinates, scaled by CanvasScaler; every control uses uGUI events.</summary>
    public static class IslandUI
    {
        public static readonly Color Ink = Hex("163F4A"), Cream = Hex("FFF3D3"), Ocean = Hex("102E3B");
        public static Font Regular, Bold;
        public static Color Hex(string hex) { Color c; ColorUtility.TryParseHtmlString("#" + hex, out c); return c; }
        public static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return r;
        }
        public static Image Panel(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var r = Rect(name, parent, x, y, w, h); var image = r.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false; return image;
        }
        public static Text Label(string text, Transform parent, float x, float y, float w, float h, int size, Color color, bool bold = false)
        {
            var r = Rect("Text", parent, x, y, w, h); var label = r.gameObject.AddComponent<Text>();
            label.font = bold ? Bold : Regular; label.text = text; label.fontSize = size;
            label.color = color; label.raycastTarget = false; label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
        public static Button Button(string text, Transform parent, float x, float y, float w, float h, Color color, UnityAction click, int size = 18)
        {
            var image = Panel(text, parent, x, y, w, h, color); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1,1,1,0.86f);
            colors.pressedColor = new Color(0.7f,0.85f,0.84f,1);
            button.colors = colors; button.onClick.AddListener(click);
            var label = Label(text, image.transform, 5, 0, w - 10, h, size, Ink, true);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        public static RawImage Art(string resource, Transform parent, float x, float y, float w, float h)
        {
            var r = Rect(resource, parent, x, y, w, h); var image = r.gameObject.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>("Level1/" + resource);
            if (!image.texture) Debug.LogError("Missing shipped Level 1 artwork: " + resource);
            image.raycastTarget = false; return image;
        }
        public static Color BlockColor(Command kind)
        {
            switch (kind)
            {
                case Command.Move: return Hex("8DD9C2");
                case Command.Left: case Command.Right: return Hex("A2CDEB");
                case Command.Clean: return Hex("F5CB7D");
                default: return Hex("C8B8ED");
            }
        }
        public static string Name(Command kind)
        {
            switch (kind)
            {
                case Command.Move: return "Move";
                case Command.Left: return "Turn left";
                case Command.Right: return "Turn right";
                case Command.Clean: return "Clean";
                default: return "Repeat";
            }
        }
    }
}
