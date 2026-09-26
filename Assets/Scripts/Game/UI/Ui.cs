using System;
using GrandStrategy.Game.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>Colours and small builders for the code-built UI Toolkit interface.</summary>
    public static class Ui
    {
        public static readonly Color PanelColor = new Color(0.055f, 0.07f, 0.10f, 0.93f);
        public static readonly Color PanelBorder = new Color(0.86f, 0.71f, 0.36f, 0.35f);
        public static readonly Color Accent = new Color(0.87f, 0.72f, 0.38f);
        public static readonly Color Text = new Color(0.93f, 0.94f, 0.96f);
        public static readonly Color TextDim = new Color(0.62f, 0.66f, 0.73f);
        public static readonly Color Good = new Color(0.50f, 0.82f, 0.55f);
        public static readonly Color Bad = new Color(0.93f, 0.47f, 0.42f);
        public static readonly Color ButtonColor = new Color(0.15f, 0.18f, 0.24f);
        public static readonly Color ButtonHover = new Color(0.22f, 0.27f, 0.35f);
        public static readonly Color PrimaryColor = new Color(0.62f, 0.47f, 0.18f);
        public static readonly Color PrimaryHover = new Color(0.74f, 0.57f, 0.24f);
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.08f);

        public static AudioManager Audio { get; set; }

        public static VisualElement Element(string name = null, bool pickable = false)
        {
            var e = new VisualElement { name = name };
            e.pickingMode = pickable ? PickingMode.Position : PickingMode.Ignore;
            return e;
        }

        public static VisualElement Row()
        {
            var e = Element();
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        public static VisualElement Column()
        {
            var e = Element();
            e.style.flexDirection = FlexDirection.Column;
            return e;
        }

        /// <summary>A framed, opaque panel that blocks clicks to the map.</summary>
        public static VisualElement Panel(float padding = 14)
        {
            var e = Element(null, true);
            e.style.backgroundColor = PanelColor;
            Border(e, PanelBorder, 1);
            Radius(e, 8);
            Padding(e, padding);
            return e;
        }

        public static Label Label(string text, int size = 15, Color? color = null, bool bold = false)
        {
            var l = new Label(text);
            l.pickingMode = PickingMode.Ignore;
            l.style.fontSize = size;
            l.style.color = color ?? Text;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.marginLeft = 0;
            l.style.marginRight = 0;
            l.style.paddingLeft = 0;
            l.style.paddingRight = 0;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static Button Button(string text, Action onClick, bool primary = false, int fontSize = 15)
        {
            var normal = primary ? PrimaryColor : ButtonColor;
            var hover = primary ? PrimaryHover : ButtonHover;
            var b = new Button(() =>
            {
                Audio?.Play(Sfx.UiClick);
                onClick?.Invoke();
            })
            {
                text = text,
            };
            // Not focusable, so Space/Enter never "click" a button the mouse pressed earlier;
            // those keys belong to the game (e.g. Space pauses).
            b.focusable = false;
            b.style.fontSize = fontSize;
            b.style.color = Text;
            b.style.unityFontStyleAndWeight = primary ? FontStyle.Bold : FontStyle.Normal;
            b.style.backgroundColor = normal;
            Border(b, primary ? Accent : new Color(1f, 1f, 1f, 0.12f), 1);
            Radius(b, 5);
            b.style.paddingLeft = 12;
            b.style.paddingRight = 12;
            b.style.paddingTop = 6;
            b.style.paddingBottom = 6;
            b.style.marginLeft = 3;
            b.style.marginRight = 3;
            b.style.marginTop = 3;
            b.style.marginBottom = 3;
            b.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (!b.enabledInHierarchy) return;
                b.style.backgroundColor = hover;
                Audio?.Play(Sfx.UiHover);
            });
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = normal);
            return b;
        }

        public static VisualElement Swatch(Color color, float width = 22, float height = 16)
        {
            var e = Element();
            e.style.width = width;
            e.style.height = height;
            e.style.backgroundColor = color;
            Border(e, new Color(0, 0, 0, 0.6f), 1);
            Radius(e, 3);
            return e;
        }

        public static VisualElement DividerLine()
        {
            var e = Element();
            e.style.height = 1;
            e.style.backgroundColor = Divider;
            e.style.marginTop = 10;
            e.style.marginBottom = 10;
            return e;
        }

        public static VisualElement StatRow(string label, string value, Color? valueColor = null)
        {
            var row = Row();
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 2;
            row.style.marginBottom = 2;
            row.Add(Label(label, 14, TextDim));
            var v = Label(value, 14, valueColor ?? Text, true);
            v.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(v);
            return row;
        }

        public static void Absolute(VisualElement e, float? left = null, float? top = null, float? right = null, float? bottom = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }

        public static void Fill(VisualElement e) => Absolute(e, 0, 0, 0, 0);

        public static void Border(VisualElement e, Color color, float width)
        {
            e.style.borderTopColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftColor = color;
            e.style.borderRightColor = color;
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
        }

        public static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        public static void Padding(VisualElement e, float p)
        {
            e.style.paddingTop = p;
            e.style.paddingBottom = p;
            e.style.paddingLeft = p;
            e.style.paddingRight = p;
        }

        public static void Show(VisualElement e, bool visible) =>
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public static Color ToColor(int rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        public static string FormatPopulation(long people)
        {
            if (people >= 1_000_000_000) return (people / 1e9).ToString("0.00") + "B";
            if (people >= 1_000_000) return (people / 1e6).ToString("0.0") + "M";
            if (people >= 1_000) return (people / 1e3).ToString("0") + "K";
            return people.ToString();
        }

        public static string FormatMoneyMillions(double millions)
        {
            if (millions >= 1_000_000) return "$" + (millions / 1e6).ToString("0.00") + "T";
            if (millions >= 1_000) return "$" + (millions / 1e3).ToString("0.0") + "B";
            return "$" + millions.ToString("0") + "M";
        }

        public static string FormatPercent(double value) => (value * 100).ToString("0") + "%";
    }
}
