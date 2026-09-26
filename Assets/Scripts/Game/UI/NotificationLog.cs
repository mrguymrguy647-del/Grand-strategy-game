using System.Collections.Generic;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>
    /// News feed on the right: what happened to you and to the world's big players.
    /// Click an item to look at the country it is about.
    /// </summary>
    public sealed class NotificationLog
    {
        const int MaxItems = 40;

        readonly GameRoot _game;
        readonly ScrollView _list;
        readonly VisualElement _body;
        readonly Label _toggle;
        bool _collapsed;
        bool _worldNews = true;
        readonly Button _filter;

        public VisualElement Root { get; }

        public NotificationLog(GameRoot game, TooltipManager tips)
        {
            _game = game;
            Root = Ui.Panel();
            Ui.Absolute(Root, null, 76, 12);
            Root.style.width = 340;
            Root.style.maxHeight = 460;

            var header = Ui.Element("gs-panel__header", true);
            header.style.paddingTop = 6;
            header.style.paddingBottom = 6;
            header.Add(Ui.IconElement("news", 18));
            var title = Ui.Label("NEWS", 15, Ui.Gold, Ui.Weight.Bold);
            title.style.marginLeft = 8;
            title.style.letterSpacing = 1.5f;
            header.Add(title);
            header.Add(Ui.Spacer());
            _filter = Ui.Button("World", ToggleFilter, false, 12);
            tips.Attach(_filter, "Filter", "World: your news and the big powers'. Mine: only news about us.");
            header.Add(_filter);
            _toggle = Ui.Label("-", 18, Ui.TextDim, Ui.Weight.Bold);
            _toggle.style.marginLeft = 8;
            _toggle.pickingMode = PickingMode.Position;
            _toggle.RegisterCallback<ClickEvent>(_ => SetCollapsed(!_collapsed));
            tips.Attach(_toggle, "Collapse", null);
            header.Add(_toggle);
            Root.Add(header);

            _body = Ui.Element();
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.maxHeight = 400;
            _list.contentContainer.style.paddingLeft = 8;
            _list.contentContainer.style.paddingRight = 8;
            _list.contentContainer.style.paddingTop = 6;
            _list.contentContainer.style.paddingBottom = 6;
            _body.Add(_list);
            Root.Add(_body);
            Add(new NewsItem(WorldStartDate(), NewsImportance.Player, "Welcome, leader",
                "Pick a nation to begin. News about your country and the great powers will appear here.", null, null));
        }

        static GameDate WorldStartDate() => new GameDate(2026, 1, 1);

        void ToggleFilter()
        {
            _worldNews = !_worldNews;
            _filter.text = _worldNews ? "World" : "Mine";
            foreach (var child in _list.contentContainer.Children())
                if (child.userData is NewsItem n)
                    Ui.Show(child, Visible(n));
        }

        void SetCollapsed(bool collapsed)
        {
            _collapsed = collapsed;
            _toggle.text = collapsed ? "+" : "-";
            Ui.Show(_body, !collapsed);
        }

        bool Visible(NewsItem n) => n.Importance == NewsImportance.Player || (_worldNews && n.Importance == NewsImportance.Notable);

        public void Add(NewsItem n)
        {
            if (n.Importance == NewsImportance.Minor)
                return;
            var item = Ui.Element("gs-news" + (n.Importance == NewsImportance.Player ? " gs-news--player" : " gs-news--notable"), true);
            item.userData = n;
            var head = Ui.Row();
            if (!string.IsNullOrEmpty(n.CountryTag))
            {
                var f = Ui.FlagElement(n.CountryTag, "gs-flag--small");
                f.style.marginRight = 6;
                head.Add(f);
            }
            var title = Ui.Label(n.Title, 14, n.Importance == NewsImportance.Player ? Ui.Gold : Ui.Text, Ui.Weight.SemiBold);
            title.style.flexShrink = 1;
            head.Add(title);
            item.Add(head);
            var meta = Ui.Label($"{n.Date}" + (string.IsNullOrEmpty(n.Text) ? "" : "   " + n.Text), 12, Ui.TextDim);
            item.Add(meta);
            if (!string.IsNullOrEmpty(n.CountryTag))
                item.RegisterCallback<ClickEvent>(_ => _game.SelectCountry(_game.World.GetCountry(n.OtherTag != null && n.CountryTag == _game.World.PlayerTag ? n.OtherTag : n.CountryTag)));
            Ui.Show(item, Visible(n));
            _list.contentContainer.Insert(0, item);
            while (_list.contentContainer.childCount > MaxItems)
                _list.contentContainer.RemoveAt(_list.contentContainer.childCount - 1);
            _list.scrollOffset = Vector2.zero;
        }
    }
}
