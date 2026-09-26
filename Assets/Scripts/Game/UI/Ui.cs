using System;
using System.Collections.Generic;
using System.Globalization;
using GrandStrategy.Game.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>
    /// Builders and shared resources for the code-built UI Toolkit interface.
    /// Look and feel lives in Resources/UI/Game.uss (classes prefixed "gs-"); this class adds
    /// fonts, icons and flags, which are loaded from Resources.
    /// </summary>
    public static class Ui
    {
        public static readonly Color Gold = new Color32(226, 188, 104, 255);
        public static readonly Color Text = new Color32(230, 233, 238, 255);
        public static readonly Color TextDim = new Color32(150, 160, 176, 255);
        public static readonly Color Good = new Color32(126, 212, 136, 255);
        public static readonly Color Bad = new Color32(240, 116, 104, 255);
        public static readonly Color Warn = new Color32(242, 196, 92, 255);
        public static readonly Color Neutral = new Color32(200, 205, 214, 255);

        // Kept for older call sites.
        public static Color Accent => Gold;

        public static AudioManager Audio { get; set; }

        // ------------------------------------------------------------------ resources

        static Font _regular, _medium, _semiBold, _bold, _title;
        static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> Flags = new Dictionary<string, Texture2D>();

        public enum Weight
        {
            Regular,
            Medium,
            SemiBold,
            Bold,
            Title,
        }

        static Font LoadFont(ref Font cache, string name)
        {
            if (cache == null)
                cache = Resources.Load<Font>("Fonts/" + name);
            return cache;
        }

        public static Font FontFor(Weight weight)
        {
            switch (weight)
            {
                case Weight.Medium: return LoadFont(ref _medium, "BarlowSemiCondensed-Medium");
                case Weight.SemiBold: return LoadFont(ref _semiBold, "BarlowSemiCondensed-SemiBold");
                case Weight.Bold: return LoadFont(ref _bold, "BarlowSemiCondensed-Bold");
                case Weight.Title: return LoadFont(ref _title, "BarlowCondensed-Bold");
                default: return LoadFont(ref _regular, "BarlowSemiCondensed-Regular");
            }
        }

        public static void SetFont(VisualElement e, Weight weight)
        {
            var font = FontFor(weight);
            if (font != null)
                e.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        public static Texture2D Icon(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (!Icons.TryGetValue(name, out var tex))
            {
                tex = Resources.Load<Texture2D>("Icons/" + name);
                Icons[name] = tex;
            }
            return tex;
        }

        public static Texture2D Flag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return null;
            if (!Flags.TryGetValue(tag, out var tex))
            {
                tex = Resources.Load<Texture2D>("Flags/" + tag);
                Flags[tag] = tex;
            }
            return tex;
        }

        // ------------------------------------------------------------------ elements

        public static VisualElement Element(string className = null, bool pickable = false)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(className))
                foreach (var c in className.Split(' '))
                    e.AddToClassList(c);
            e.pickingMode = pickable ? PickingMode.Position : PickingMode.Ignore;
            return e;
        }

        public static VisualElement Row(string className = null)
        {
            var e = Element(className);
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        public static VisualElement Column(string className = null)
        {
            var e = Element(className);
            e.style.flexDirection = FlexDirection.Column;
            return e;
        }

        public static VisualElement Spacer()
        {
            var e = Element();
            e.style.flexGrow = 1;
            return e;
        }

        /// <summary>A framed panel that blocks clicks to the map.</summary>
        public static VisualElement Panel(string extraClass = null)
        {
            var e = Element("gs-panel" + (extraClass == null ? "" : " " + extraClass), true);
            return e;
        }

        public static Label Label(string text, int size = 15, Color? color = null, Weight weight = Weight.Regular, string className = null)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("gs-text");
            if (!string.IsNullOrEmpty(className))
                foreach (var c in className.Split(' '))
                    l.AddToClassList(c);
            l.style.fontSize = size;
            if (color.HasValue)
                l.style.color = color.Value;
            SetFont(l, weight);
            return l;
        }

        // Older call sites used a bool for bold.
        public static Label Label(string text, int size, Color? color, bool bold) =>
            Label(text, size, color, bold ? Weight.Bold : Weight.Regular);

        public static Label Title(string text, int size = 26) => Label(text, size, null, Weight.Title, "gs-title");

        public static Label SectionTitle(string text)
        {
            var l = Label(text.ToUpperInvariant(), 13, null, Weight.SemiBold, "gs-section-title");
            return l;
        }

        public static VisualElement IconElement(string name, int size = 18, Color? tint = null, string extraClass = null)
        {
            var e = Element("gs-icon" + (extraClass == null ? "" : " " + extraClass));
            var tex = Icon(name);
            if (tex != null)
                e.style.backgroundImage = new StyleBackground(tex);
            e.style.width = size;
            e.style.height = size;
            if (tint.HasValue)
                e.style.unityBackgroundImageTintColor = tint.Value;
            return e;
        }

        public static VisualElement FlagElement(string tag, string sizeClass = null)
        {
            var e = Element("gs-flag" + (sizeClass == null ? "" : " " + sizeClass));
            var tex = Flag(tag);
            if (tex != null)
                e.style.backgroundImage = new StyleBackground(tex);
            else
                e.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
            return e;
        }

        public static void SetFlag(VisualElement e, string tag)
        {
            var tex = Flag(tag);
            e.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
        }

        public static Button Button(string text, Action onClick, bool primary = false, int fontSize = 15, string icon = null)
        {
            var b = new Button(() =>
            {
                Audio?.Play(Sfx.UiClick);
                onClick?.Invoke();
            });
            b.text = icon == null ? text : "";
            b.focusable = false; // Space/Enter belong to the game (Space pauses)
            b.AddToClassList("gs-btn");
            if (primary)
                b.AddToClassList("gs-btn--primary");
            b.style.fontSize = fontSize;
            SetFont(b, primary ? Weight.Bold : Weight.SemiBold);
            if (icon != null)
            {
                b.style.flexDirection = FlexDirection.Row;
                b.style.alignItems = Align.Center;
                b.Add(IconElement(icon, fontSize + 3, primary ? new Color(0.1f, 0.08f, 0.03f) : (Color?)null));
                if (!string.IsNullOrEmpty(text))
                {
                    var l = Label(text, fontSize, primary ? new Color(0.1f, 0.08f, 0.03f) : (Color?)null, primary ? Weight.Bold : Weight.SemiBold);
                    l.style.marginLeft = 6;
                    b.Add(l);
                }
            }
            b.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (b.enabledInHierarchy)
                    Audio?.Play(Sfx.UiHover);
            });
            return b;
        }

        public static Button IconButton(string icon, Action onClick, int size = 18)
        {
            var b = Button(null, onClick, false, size, icon);
            b.AddToClassList("gs-btn--icon");
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

        public static VisualElement DividerLine() => Element("gs-divider");

        public static VisualElement StatRow(string label, string value, Color? valueColor = null, string icon = null)
        {
            var row = Element("gs-stat-row", true);
            var left = Row();
            if (icon != null)
            {
                var i = IconElement(icon, 16);
                i.style.marginRight = 6;
                left.Add(i);
            }
            left.Add(Label(label, 14, TextDim));
            row.Add(left);
            var v = Label(value, 15, valueColor ?? Text, Weight.SemiBold);
            v.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(v);
            return row;
        }

        /// <summary>A small labelled value box for two-column grids.</summary>
        public static VisualElement StatCard(string icon, string label, string value, Color? valueColor = null)
        {
            var card = Element("gs-stat-card", true);
            if (icon != null)
            {
                var i = IconElement(icon, 22);
                i.style.marginRight = 8;
                card.Add(i);
            }
            var col = Column();
            col.Add(Label(label, 12, null, Weight.Medium, "gs-stat-card__label"));
            col.Add(Label(value, 18, valueColor, Weight.SemiBold, "gs-stat-card__value"));
            card.Add(col);
            return card;
        }

        /// <summary>A 0..1 progress bar, optionally with a marker for where the value is heading.</summary>
        public static VisualElement Bar(double value, Color color, double? marker = null)
        {
            var bar = Element("gs-bar");
            var fill = Element("gs-bar__fill");
            fill.style.width = Length.Percent((float)(Math.Max(0, Math.Min(1, value)) * 100));
            fill.style.backgroundColor = color;
            bar.Add(fill);
            if (marker.HasValue)
            {
                var m = Element("gs-bar__marker");
                m.style.left = Length.Percent((float)(Math.Max(0, Math.Min(1, marker.Value)) * 100));
                bar.Add(m);
            }
            return bar;
        }

        // ------------------------------------------------------------------ layout helpers

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

        public static bool IsShown(VisualElement e) => e.resolvedStyle.display != DisplayStyle.None && e.style.display != DisplayStyle.None;

        public static Color ToColor(int rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        // ------------------------------------------------------------------ formatting

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string FormatPopulation(long people)
        {
            if (people >= 1_000_000_000) return (people / 1e9).ToString("0.00", Inv) + "B";
            if (people >= 1_000_000) return (people / 1e6).ToString("0.0", Inv) + "M";
            if (people >= 1_000) return (people / 1e3).ToString("0", Inv) + "K";
            return people.ToString(Inv);
        }

        public static string FormatMoneyMillions(double millions)
        {
            double a = Math.Abs(millions);
            string sign = millions < 0 ? "-" : "";
            if (a >= 1_000_000) return sign + "$" + (a / 1e6).ToString("0.00", Inv) + "T";
            if (a >= 1_000) return sign + "$" + (a / 1e3).ToString(a >= 100_000 ? "0" : "0.0", Inv) + "B";
            return sign + "$" + a.ToString("0", Inv) + "M";
        }

        public static string FormatSignedMoney(double millions) => (millions >= 0 ? "+" : "") + FormatMoneyMillions(millions);

        /// <summary>0.253 -> "25%".</summary>
        public static string FormatPercent(double value) => (value * 100).ToString("0", Inv) + "%";

        /// <summary>0.253 -> "25.3%".</summary>
        public static string FormatPercent1(double value) => (value * 100).ToString("0.0", Inv) + "%";

        /// <summary>2.35 -> "+2.4%" (value already in percent).</summary>
        public static string FormatGrowth(double percent) => (percent >= 0 ? "+" : "") + percent.ToString("0.0", Inv) + "%";

        public static string FormatSigned(double v, string format = "0.#") => (v >= 0 ? "+" : "") + v.ToString(format, Inv);

        public static Color ScoreColor(double value, double bad, double good)
        {
            if (good > bad)
                return value >= good ? Good : value <= bad ? Bad : Warn;
            return value <= good ? Good : value >= bad ? Bad : Warn;
        }
    }
}
