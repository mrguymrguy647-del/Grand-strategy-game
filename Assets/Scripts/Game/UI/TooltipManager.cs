using System;
using System.Collections.Generic;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>Content for one tooltip: a title, optional text and value rows.</summary>
    public sealed class TooltipContent
    {
        public string Title;
        public string Text;
        public readonly List<(string label, string value, Color color)> Rows = new List<(string, string, Color)>();
        public (string label, string value, Color color)? Total;
        public string Footer;

        public TooltipContent(string title = null, string text = null)
        {
            Title = title;
            Text = text;
        }

        public TooltipContent Row(string label, string value, Color? color = null)
        {
            Rows.Add((label, value, color ?? Ui.Text));
            return this;
        }

        /// <summary>Adds every factor of a breakdown; positive = good unless <paramref name="higherIsBad"/>.</summary>
        public TooltipContent Factors(Breakdown b, Func<double, string> format, bool higherIsBad = false)
        {
            foreach (var f in b.Factors)
                Rows.Add((f.Label, format(f.Value), (f.Value >= 0) != higherIsBad ? Ui.Good : Ui.Bad));
            return this;
        }

        public TooltipContent WithTotal(string label, string value, Color? color = null)
        {
            Total = (label, value, color ?? Ui.Gold);
            return this;
        }
    }

    /// <summary>
    /// Hover tooltips for runtime UI (UI Toolkit only shows its built-in tooltips in the editor).
    /// Content is built when the pointer enters, so it always shows current numbers.
    /// </summary>
    public sealed class TooltipManager
    {
        readonly VisualElement _root;
        readonly VisualElement _box;
        VisualElement _owner;

        public TooltipManager(VisualElement root)
        {
            _root = root;
            _box = Ui.Element("gs-tooltip");
            Ui.Show(_box, false);
            root.Add(_box);
        }

        public void Attach(VisualElement target, Func<TooltipContent> content)
        {
            if (target.pickingMode == PickingMode.Ignore)
                target.pickingMode = PickingMode.Position;
            target.RegisterCallback<PointerEnterEvent>(e =>
            {
                _owner = target;
                Build(content?.Invoke());
                Move(e.position);
            });
            target.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_owner == target)
                    Move(e.position);
            });
            target.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_owner == target)
                    Hide();
            });
            target.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (_owner == target)
                    Hide();
            });
        }

        public void Attach(VisualElement target, string title, string text) => Attach(target, () => new TooltipContent(title, text));

        public void Hide()
        {
            _owner = null;
            Ui.Show(_box, false);
        }

        void Build(TooltipContent c)
        {
            _box.Clear();
            if (c == null)
            {
                Ui.Show(_box, false);
                return;
            }
            if (!string.IsNullOrEmpty(c.Title))
                _box.Add(Ui.Label(c.Title, 16, null, Ui.Weight.Bold, "gs-tooltip__title"));
            if (!string.IsNullOrEmpty(c.Text))
            {
                var t = Ui.Label(c.Text, 14, Ui.TextDim);
                t.style.marginBottom = c.Rows.Count > 0 ? 6 : 0;
                _box.Add(t);
            }
            foreach (var (label, value, color) in c.Rows)
                _box.Add(RowElement(label, value, color, false));
            if (c.Total.HasValue)
                _box.Add(RowElement(c.Total.Value.label, c.Total.Value.value, c.Total.Value.color, true));
            if (!string.IsNullOrEmpty(c.Footer))
            {
                var f = Ui.Label(c.Footer, 13, Ui.TextDim);
                f.style.marginTop = 6;
                _box.Add(f);
            }
            Ui.Show(_box, true);
            _box.BringToFront();
        }

        static VisualElement RowElement(string label, string value, Color color, bool total)
        {
            var row = Ui.Element("gs-tooltip__row" + (total ? " gs-tooltip__total" : ""));
            var l = Ui.Label(label, 14, total ? Ui.Text : Ui.TextDim, total ? Ui.Weight.SemiBold : Ui.Weight.Regular);
            l.style.marginRight = 14;
            row.Add(l);
            row.Add(Ui.Label(value, 14, color, Ui.Weight.SemiBold));
            return row;
        }

        void Move(Vector2 panelPos)
        {
            float w = _root.layout.width;
            float h = _root.layout.height;
            float bw = float.IsNaN(_box.layout.width) ? 260 : _box.layout.width;
            float bh = float.IsNaN(_box.layout.height) ? 120 : _box.layout.height;
            float x = panelPos.x + 18;
            float y = panelPos.y + 16;
            if (!float.IsNaN(w) && x + bw > w - 8) x = panelPos.x - bw - 12;
            if (!float.IsNaN(h) && y + bh > h - 8) y = h - bh - 8;
            _box.style.left = Mathf.Max(4, x);
            _box.style.top = Mathf.Max(4, y);
        }
    }
}
