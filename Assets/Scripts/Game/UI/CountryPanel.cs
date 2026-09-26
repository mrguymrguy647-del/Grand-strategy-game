using System;
using System.Collections.Generic;
using System.Linq;
using GrandStrategy.Game.Audio;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>
    /// The panel that opens when you click a country: header with flag and relations, and tabs
    /// for Overview, Economy, Politics and Diplomacy. For your own nation the Economy and
    /// Politics tabs have sliders and decisions; for others the Diplomacy tab has actions.
    /// </summary>
    public sealed class CountryPanel
    {
        enum Tab
        {
            Overview,
            Economy,
            Politics,
            Diplomacy,
        }

        readonly GameRoot _game;
        readonly TooltipManager _tips;
        readonly VisualElement _flag;
        readonly Label _name;
        readonly Label _subtitle;
        readonly VisualElement _badge;
        readonly Label _badgeText;
        readonly Dictionary<Tab, Button> _tabs = new Dictionary<Tab, Button>();
        readonly ScrollView _scroll;
        Tab _tab = Tab.Overview;
        Country _country;
        int _provinceId;
        bool _dirty;
        bool _hovered;
        float _lastRebuild;

        // While the pointer is over the panel, the monthly refresh waits at least this long
        // between rebuilds, so rows under the pointer don't keep flickering at high speed.
        const float HoveredRebuildSeconds = 1f;

        // Live sections of the Economy tab, refreshed while sliders move.
        VisualElement _budgetBox;
        VisualElement _growthBox;
        VisualElement _approvalPreview;

        public VisualElement Root { get; }
        public Country Country => _country;

        GameSimulation Sim => _game.Sim;
        Country Player => _game.World?.Player;
        bool IsOwn => _country != null && Player != null && _country == Player && _game.Phase == GamePhase.Playing;
        bool IsPlaying => _game.Phase == GamePhase.Playing && Player != null && !Player.IsEliminated;

        public CountryPanel(GameRoot game, TooltipManager tips)
        {
            _game = game;
            _tips = tips;
            Root = Ui.Panel();
            Ui.Absolute(Root, 12, 76, null, 56);
            Root.style.width = 480;
            Root.style.flexDirection = FlexDirection.Column;
            Root.RegisterCallback<PointerEnterEvent>(_ => _hovered = true);
            Root.RegisterCallback<PointerLeaveEvent>(_ => _hovered = false);

            var header = Ui.Header();
            _flag = Ui.FlagElement(null, "gs-flag--large");
            _flag.style.marginRight = 12;
            header.Add(_flag);
            var names = Ui.Column();
            names.style.flexGrow = 1;
            names.style.flexShrink = 1;
            _name = Ui.Title("", 28);
            _subtitle = Ui.Label("", 14, Ui.TextDim, Ui.Weight.Medium);
            names.Add(_name);
            names.Add(_subtitle);
            _badge = Ui.Row();
            _badge.style.marginTop = 4;
            _badge.pickingMode = PickingMode.Position;
            _badge.Add(Ui.IconElement("diplomacy", 16));
            _badgeText = Ui.Label("", 14, null, Ui.Weight.SemiBold);
            _badgeText.style.marginLeft = 5;
            _badge.Add(_badgeText);
            _tips.Attach(_badge, () => Tips.Relations(Sim, Player, _country));
            names.Add(_badge);
            header.Add(names);
            var close = Ui.Button("X", () => _game.SelectCountry(null), false, 14);
            close.style.alignSelf = Align.FlexStart;
            _tips.Attach(close, "Close", "Keyboard: Esc or right click on the map.");
            header.Add(close);
            Root.Add(header);

            var tabs = Ui.Element("gs-tabs", true);
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                var b = new Button(() =>
                {
                    Ui.Audio?.Play(Sfx.UiClick);
                    _tab = tab;
                    Rebuild();
                })
                {
                    text = tab.ToString(),
                    focusable = false,
                };
                b.AddToClassList("gs-tab");
                Ui.SetFont(b, Ui.Weight.SemiBold);
                b.style.fontSize = 15;
                _tabs[tab] = b;
                tabs.Add(b);
            }
            Root.Add(tabs);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.contentContainer.AddToClassList("gs-panel__body");
            Root.Add(_scroll);

            Ui.Show(Root, false);
        }

        // ------------------------------------------------------------------ public

        public void Show(Country country, int provinceId)
        {
            bool changed = country != _country;
            _country = country;
            _provinceId = provinceId;
            if (country == null)
            {
                Ui.Show(Root, false);
                return;
            }
            if (changed && _tab == Tab.Economy && !(IsOwn))
                _tab = Tab.Overview;
            if (changed && _tab == Tab.Diplomacy && country == Player && IsPlaying)
                _tab = Tab.Overview;
            Ui.Show(Root, true);
            Rebuild();
        }

        public void OpenTab(string name)
        {
            if (Enum.TryParse(name, out Tab t))
            {
                _tab = t;
                Rebuild();
            }
        }

        /// <summary>Rebuild at the next frame (monthly tick, events...).</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>Called every frame by the HUD.</summary>
        public void Tick()
        {
            if (!_dirty || _country == null || !Ui.IsShown(Root))
                return;
            // Never rebuild while a mouse button is down: that would swap the button being
            // clicked (or the slider being dragged) for a new one, and the click would be lost.
            if (GameInput.PointerHeld(PointerButton.Left) || GameInput.PointerHeld(PointerButton.Right) ||
                GameInput.PointerHeld(PointerButton.Middle))
                return;
            if (_hovered && Time.unscaledTime - _lastRebuild < HoveredRebuildSeconds)
                return;
            Rebuild();
        }

        /// <summary>A policy changed: update the live numbers without rebuilding the sliders.</summary>
        public void RefreshLive()
        {
            if (_country == null)
                return;
            if (_tab == Tab.Economy && _budgetBox != null)
            {
                FillBudget(_budgetBox);
                FillGrowth(_growthBox);
                FillApprovalPreview(_approvalPreview);
            }
            else
            {
                _dirty = true;
            }
        }

        // ------------------------------------------------------------------ build

        void Rebuild()
        {
            _dirty = false;
            _lastRebuild = Time.unscaledTime;
            if (_country == null || Sim == null)
                return;

            var c = _country;
            _name.text = c.Name;
            Ui.SetFlag(_flag, c.Tag);
            _subtitle.text = $"{c.Politics.Government.DisplayName()}   |   Capital: {c.CapitalName}";
            bool showBadge = IsPlaying && c != Player;
            Ui.Show(_badge, showBadge);
            if (showBadge)
            {
                double r = Sim.Diplomacy.Relations(Player, c);
                _badgeText.text = $"Relations with us: {r:+0;-0;0}" + (Sim.Diplomacy.AreAllied(Player, c) ? "   |   Ally" : "");
                _badgeText.style.color = r >= 30 ? Ui.Good : r <= -30 ? Ui.Bad : Ui.Warn;
            }

            foreach (var kv in _tabs)
                kv.Value.EnableInClassList("gs-tab--active", kv.Key == _tab);

            var offset = _scroll.scrollOffset;
            var body = _scroll.contentContainer;
            body.Clear();
            _budgetBox = _growthBox = _approvalPreview = null;
            if (c.IsEliminated)
            {
                body.Add(Ui.Label($"{c.Name} no longer exists.", 16, Ui.Bad));
                return;
            }

            switch (_tab)
            {
                case Tab.Overview: BuildOverview(body, c); break;
                case Tab.Economy: BuildEconomy(body, c); break;
                case Tab.Politics: BuildPolitics(body, c); break;
                default: BuildDiplomacy(body, c); break;
            }
            _scroll.schedule.Execute(() => _scroll.scrollOffset = offset);
        }

        // ------------------------------------------------------------------ overview

        void BuildOverview(VisualElement body, Country c)
        {
            if (_game.Phase == GamePhase.NationSelect)
            {
                var play = Ui.Button($"Play as {c.Name}", () => _game.ChooseNation(c.Tag), true, 19);
                play.style.height = 44;
                play.style.marginBottom = 10;
                body.Add(play);
            }

            var e = c.Economy;
            var p = c.Politics;
            long pop = _game.World.PopulationOf(c);
            var grid = Ui.Element("gs-stat-grid");
            grid.Add(Card("population", "Population", Ui.FormatPopulation(pop), null, null));
            grid.Add(Card("gdp", "GDP", Ui.FormatMoneyMillions(e.Gdp), null, () => Tips.Growth(c)));
            grid.Add(Card("wealth", "GDP per person", pop > 0 ? "$" + (e.Gdp * 1e6 / pop).ToString("#,0") : "-", null, null));
            grid.Add(Card("growth", "Real growth", Ui.FormatGrowth(e.RealGrowth), e.RealGrowth >= 1 ? Ui.Good : e.RealGrowth >= 0 ? Ui.Warn : Ui.Bad, () => Tips.Growth(c)));
            grid.Add(Card("debt", "Debt", $"{Ui.FormatPercent(e.DebtToGdp)} of GDP  ({e.Rating})", null, () => Tips.Debt(c)));
            grid.Add(Card("government", "Government", p.Government.DisplayName(), null, null));
            body.Add(grid);

            body.Add(MeterRow("stability", "Stability", p.Stability, p.StabilityTarget.Total, () => Tips.Stability(c)));
            body.Add(MeterRow("approval", "Approval", p.Approval, p.ApprovalTarget.Total, () => Tips.Approval(c)));

            var blocs = Sim.Diplomacy.BlocsOf(c).ToList();
            if (blocs.Count > 0)
            {
                body.Add(Ui.SectionTitle("Memberships"));
                var wrap = Ui.Row();
                wrap.style.flexWrap = Wrap.Wrap;
                foreach (var b in blocs)
                    wrap.Add(BlocChip(b));
                body.Add(wrap);
            }

            var province = _game.World.GetProvince(_provinceId);
            if (province != null && province.OwnerTag == c.Tag)
            {
                body.Add(Ui.SectionTitle("Selected province"));
                var card = Ui.Element("gs-card");
                var head = Ui.Row();
                head.Add(Ui.Label(province.Name, 17, null, Ui.Weight.SemiBold));
                if (c.CapitalProvinceId == province.Id)
                {
                    var cap = Ui.Label("  CAPITAL", 12, Ui.Gold, Ui.Weight.Bold);
                    head.Add(cap);
                }
                card.Add(head);
                card.Add(Ui.Label($"Population {Ui.FormatPopulation(province.Population)}   |   GDP {Ui.FormatMoneyMillions(province.GdpMillions)}" +
                                  (province.IsCoastal ? "   |   Coastal" : ""), 14, Ui.TextDim));
                body.Add(card);
            }

            BuildModifiers(body, c);

            if (IsPlaying && c != Player)
            {
                var go = Ui.Button("Diplomatic actions", () => { _tab = Tab.Diplomacy; Rebuild(); }, true, 16, "diplomacy");
                go.style.marginTop = 12;
                body.Add(go);
            }
        }

        VisualElement Card(string icon, string label, string value, Color? color, Func<TooltipContent> tip)
        {
            var card = Ui.StatCard(icon, label, value, color);
            if (tip != null)
                _tips.Attach(card, tip);
            return card;
        }

        VisualElement MeterRow(string icon, string label, double value, double target, Func<TooltipContent> tip)
        {
            var box = Ui.Element("gs-card", true);
            var row = Ui.Row();
            row.Add(Ui.IconElement(icon, 18));
            var l = Ui.Label(label, 15, null, Ui.Weight.SemiBold);
            l.style.marginLeft = 6;
            row.Add(l);
            row.Add(Ui.Spacer());
            double t = Math.Max(0, Math.Min(100, target));
            string arrow = t > value + 1 ? "  rising" : t < value - 1 ? "  falling" : "";
            row.Add(Ui.Label($"{value:0}{arrow}", 15, Ui.ScoreColor(value, 30, 55), Ui.Weight.SemiBold));
            box.Add(row);
            box.Add(Ui.Bar(value / 100, Ui.ScoreColor(value, 30, 55), t / 100));
            _tips.Attach(box, tip);
            return box;
        }

        VisualElement BlocChip(Bloc b)
        {
            var chip = Ui.Element("gs-chip", true);
            chip.style.marginBottom = 4;
            chip.Add(Ui.IconElement(b.IsAlliance ? "alliance" : b.IsTradeArea ? "trade" : "diplomacy", 15));
            var l = Ui.Label(b.Name, 13, null, Ui.Weight.Medium);
            l.style.marginLeft = 5;
            chip.Add(l);
            string kind = b.Kind == BlocKind.Alliance ? "Mutual defence alliance"
                : b.Kind == BlocKind.Union ? "Political and economic union (free trade)"
                : b.Kind == BlocKind.Trade ? "Free-trade area" : "Forum of countries with shared interests";
            _tips.Attach(chip, () => new TooltipContent(b.Name, $"{kind}. {b.Members.Count} members."));
            return chip;
        }

        void BuildModifiers(VisualElement body, Country c)
        {
            var mods = c.Modifiers.ToList();
            if (mods.Count == 0)
                return;
            body.Add(Ui.SectionTitle("Active effects"));
            foreach (var m in mods)
            {
                bool good = m.Growth + m.Approval + m.Stability + m.Morale * 100 - m.InterestPremium * 100 >= 0;
                var row = Ui.StatRow(m.Name, m.IsPermanent ? "permanent" : $"{m.DaysRemaining} days", good ? Ui.Good : Ui.Bad);
                _tips.Attach(row, () => Tips.Modifier(m));
                body.Add(row);
            }
        }

        // ------------------------------------------------------------------ economy

        void BuildEconomy(VisualElement body, Country c)
        {
            var e = c.Economy;
            body.Add(Row("gdp", "GDP", Ui.FormatMoneyMillions(e.Gdp), null, () => Tips.Growth(c)));
            body.Add(Row("growth", "Real growth", Ui.FormatGrowth(e.RealGrowth), e.RealGrowth >= 0 ? Ui.Good : Ui.Bad, () => Tips.Growth(c)));
            body.Add(Row("treasury", "Treasury", Ui.FormatMoneyMillions(e.Treasury), null, () => Tips.Budget(c)));
            body.Add(Row("debt", "Debt", $"{Ui.FormatMoneyMillions(e.Debt)}  ({Ui.FormatPercent(e.DebtToGdp)} of GDP)", null, () => Tips.Debt(c)));
            body.Add(Row("bank", "Interest rate / rating", $"{Ui.FormatPercent1(e.InterestRate)}  |  {e.Rating}",
                e.Rating <= CreditRating.A ? Ui.Good : e.Rating <= CreditRating.BB ? Ui.Warn : Ui.Bad, () => Tips.Debt(c)));

            body.Add(Ui.SectionTitle("Monthly budget"));
            _budgetBox = Ui.Column();
            FillBudget(_budgetBox);
            body.Add(_budgetBox);

            if (IsOwn)
            {
                body.Add(Ui.SectionTitle("Taxes & spending"));
                var hint = Ui.Label("Changes apply now. Their effects on growth and approval build up over the next months. " +
                                    "The mark on each slider is where you started.", 13, Ui.TextDim);
                hint.style.marginBottom = 6;
                body.Add(hint);
                body.Add(SliderRow(c, "income", "Tax rate", e.TaxRate, e.StartTaxRate, 0.05, 0.60,
                    v => Sim.Economy.SetTaxRate(c, v), "Higher taxes raise revenue but slow growth and cost approval."));
                body.Add(SpendingSlider(c, SpendingCategory.Military, "military", "Military", 0.15,
                    "Security spending. In autocracies it props up stability. Armies arrive in Phase 2."));
                body.Add(SpendingSlider(c, SpendingCategory.Welfare, "welfare", "Welfare & health", 0.35,
                    "Pensions, healthcare, benefits. Popular: raises approval."));
                body.Add(SpendingSlider(c, SpendingCategory.Education, "education", "Education", 0.12,
                    "Schools and universities. Raises growth a little and approval slightly."));
                body.Add(SpendingSlider(c, SpendingCategory.Infrastructure, "infrastructure", "Infrastructure", 0.12,
                    "Roads, rail, energy. The strongest lever for growth."));
                body.Add(SpendingSlider(c, SpendingCategory.Administration, "admin", "Administration", 0.15,
                    "Civil service and police. Cutting it lowers tax collection and stability."));

                _approvalPreview = Ui.Column();
                FillApprovalPreview(_approvalPreview);
                body.Add(_approvalPreview);

                body.Add(Ui.SectionTitle("Finance"));
                double chunk = Math.Max(100, e.Gdp * 0.01);
                var fin = Ui.Row();
                fin.style.flexWrap = Wrap.Wrap;
                var borrow = Ui.Button($"Borrow {Ui.FormatMoneyMillions(chunk)}", () => { Sim.Economy.Borrow(c, chunk); Rebuild(); }, false, 14, "bank");
                _tips.Attach(borrow, "Borrow", "Adds cash to the treasury and the same amount to debt.");
                fin.Add(borrow);
                var repay = Ui.Button($"Repay {Ui.FormatMoneyMillions(chunk)}", () => { Sim.Economy.Repay(c, chunk); Rebuild(); }, false, 14, "treasury");
                repay.SetEnabled(e.Treasury > 1 && e.Debt > 1);
                _tips.Attach(repay, "Repay debt", "Uses treasury cash to pay back debt. Lower debt means lower interest.");
                fin.Add(repay);
                body.Add(fin);
                body.Add(DecisionCard(c, DecisionId.StimulusPackage));
            }

            body.Add(Ui.SectionTitle("Why growth is what it is"));
            _growthBox = Ui.Column();
            FillGrowth(_growthBox);
            body.Add(_growthBox);
        }

        VisualElement Row(string icon, string label, string value, Color? color, Func<TooltipContent> tip)
        {
            var r = Ui.StatRow(label, value, color, icon);
            if (tip != null)
                _tips.Attach(r, tip);
            return r;
        }

        void FillBudget(VisualElement box)
        {
            if (box == null) return;
            box.Clear();
            var e = _country.Economy;
            foreach (var f in e.MonthlyIncome.Factors)
                box.Add(Ui.StatRow(f.Label, Ui.FormatSignedMoney(f.Value), Ui.Good));
            foreach (var f in e.MonthlyCosts.Factors)
                box.Add(Ui.StatRow(f.Label, Ui.FormatSignedMoney(-f.Value), Ui.Bad));
            var total = Ui.StatRow("Balance per month", Ui.FormatSignedMoney(e.MonthlyBalance), e.MonthlyBalance >= 0 ? Ui.Good : Ui.Bad);
            total.style.borderTopWidth = 1;
            total.style.borderTopColor = new Color(1, 1, 1, 0.2f);
            box.Add(total);
        }

        void FillGrowth(VisualElement box)
        {
            if (box == null) return;
            box.Clear();
            var e = _country.Economy;
            foreach (var f in e.GrowthFactors.Factors)
                box.Add(Ui.StatRow(f.Label, Ui.FormatGrowth(f.Value), f.Value >= 0 ? Ui.Good : Ui.Bad));
            box.Add(Ui.StatRow("Real growth per year", Ui.FormatGrowth(e.RealGrowth), Ui.Gold));
        }

        void FillApprovalPreview(VisualElement box)
        {
            if (box == null) return;
            box.Clear();
            var p = _country.Politics;
            var row = Ui.StatRow("Approval is heading towards", $"{Math.Max(0, Math.Min(100, p.ApprovalTarget.Total)):0}%  (now {p.Approval:0}%)",
                Ui.ScoreColor(p.ApprovalTarget.Total, 30, 50), "approval");
            _tips.Attach(row, () => Tips.Approval(_country));
            box.Add(row);
        }

        VisualElement SpendingSlider(Country c, SpendingCategory cat, string icon, string label, double max, string help) =>
            SliderRow(c, icon, label, c.Economy.GetSpending(cat), c.Economy.GetStartSpending(cat), 0, max,
                v => Sim.Economy.SetSpending(c, cat, v), help);

        VisualElement SliderRow(Country c, string icon, string label, double value, double start, double min, double max,
            Action<double> apply, string help)
        {
            var box = Ui.Element("gs-slider-row", true);
            var head = Ui.Row();
            head.Add(Ui.IconElement(icon, 16));
            var name = Ui.Label(label, 15, null, Ui.Weight.SemiBold);
            name.style.marginLeft = 6;
            head.Add(name);
            head.Add(Ui.Spacer());
            var valueLabel = Ui.Label("", 15, Ui.Gold, Ui.Weight.SemiBold);
            head.Add(valueLabel);
            box.Add(head);

            void ShowValue(double v)
            {
                double diff = (v - start) * 100;
                valueLabel.text = $"{v * 100:0.0}% of GDP" + (Math.Abs(diff) >= 0.05 ? $"  ({diff:+0.0;-0.0})" : "");
            }
            ShowValue(value);

            var slider = new Slider((float)(min * 100), (float)(max * 100)) { value = (float)(value * 100), focusable = false };
            slider.style.marginLeft = 0;
            slider.style.marginRight = 0;
            var startMark = Ui.Element("gs-bar__marker");
            startMark.style.top = 4;
            startMark.style.bottom = 4;
            startMark.style.left = Length.Percent((float)((start - min) / (max - min) * 100));
            slider.Add(startMark);
            slider.RegisterValueChangedCallback(evt =>
            {
                double v = Math.Round(evt.newValue * 2) / 200.0; // steps of 0.5 point
                apply(v);
                ShowValue(v);
                RefreshLive();
            });
            box.Add(slider);
            _tips.Attach(head, label, help);
            return box;
        }

        // ------------------------------------------------------------------ politics

        void BuildPolitics(VisualElement body, Country c)
        {
            var p = c.Politics;
            body.Add(Row("government", "Government", p.Government.DisplayName(), null,
                () => new TooltipContent(p.Government.DisplayName(), GovernmentHelp(p.Government))));
            if (p.NextElection.HasValue)
                body.Add(Row("ballot", "Next election", $"{p.NextElection.Value.MonthName} {p.NextElection.Value.Year}   |   win chance {Sim.Politics.ReelectionChance(c) * 100:0}%",
                    null, () => new TooltipContent("Elections", "The government wins if approval is high enough on election day (45% in democracies, 30% in hybrid regimes). Losing brings a new cabinet, but you keep leading the nation.")));
            else
                body.Add(Row("ballot", "Elections", "No competitive elections", Ui.TextDim, null));

            body.Add(Ui.SectionTitle("Stability"));
            body.Add(MeterRow("stability", "Stability", p.Stability, p.StabilityTarget.Total, () => Tips.Stability(c)));
            foreach (var f in p.StabilityTarget.Factors)
                body.Add(Ui.StatRow(f.Label, Ui.FormatSigned(f.Value, "0"), f.Label == "Baseline" ? Ui.Text : f.Value >= 0 ? Ui.Good : Ui.Bad));

            body.Add(Ui.SectionTitle("Approval"));
            body.Add(MeterRow("approval", "Approval", p.Approval, p.ApprovalTarget.Total, () => Tips.Approval(c)));
            foreach (var f in p.ApprovalTarget.Factors)
                body.Add(Ui.StatRow(f.Label, Ui.FormatSigned(f.Value, "0"), f.Label == "Baseline" ? Ui.Text : f.Value >= 0 ? Ui.Good : Ui.Bad));

            if (IsOwn)
            {
                body.Add(Ui.SectionTitle("Decisions"));
                foreach (var id in new[] { DecisionId.PropagandaCampaign, DecisionId.ReformProgram, DecisionId.Crackdown, DecisionId.EarlyElection })
                    body.Add(DecisionCard(c, id));
            }
            BuildModifiers(body, c);
        }

        static string GovernmentHelp(GovernmentType g)
        {
            switch (g)
            {
                case GovernmentType.FullDemocracy: return "Free elections and strong institutions. Stable, but voters punish bad policy.";
                case GovernmentType.FlawedDemocracy: return "Free elections with weaker institutions.";
                case GovernmentType.HybridRegime: return "Elections are held, but the playing field is tilted towards the government.";
                case GovernmentType.Authoritarian: return "No free elections. Security forces keep order; crackdowns are possible.";
                default: return "The monarch rules. No elections; security and wealth keep the throne safe.";
            }
        }

        VisualElement DecisionCard(Country c, DecisionId id)
        {
            var info = Sim.Decisions.Describe(c, id);
            var card = Ui.Element("gs-card", true);
            var head = Ui.Row();
            head.Add(Ui.IconElement(DecisionIcon(id), 20));
            var name = Ui.Label(info.Name, 16, null, Ui.Weight.SemiBold);
            name.style.marginLeft = 6;
            head.Add(name);
            head.Add(Ui.Spacer());
            if (info.CostMillions > 0)
                head.Add(Ui.Label(Ui.FormatMoneyMillions(info.CostMillions), 14, Ui.Warn, Ui.Weight.SemiBold));
            card.Add(head);
            card.Add(Ui.Label(info.Description, 13, Ui.TextDim));
            card.Add(Ui.Label(info.Effects, 13, Ui.Gold));
            var foot = Ui.Row();
            foot.style.marginTop = 4;
            if (!info.Available)
                foot.Add(Ui.Label(info.Reason, 13, Ui.Bad));
            foot.Add(Ui.Spacer());
            var go = Ui.Button("Enact", () => _game.EnactDecision(id), true, 14);
            go.SetEnabled(info.Available);
            foot.Add(go);
            card.Add(foot);
            return card;
        }

        static string DecisionIcon(DecisionId id)
        {
            switch (id)
            {
                case DecisionId.StimulusPackage: return "factory";
                case DecisionId.PropagandaCampaign: return "media";
                case DecisionId.ReformProgram: return "reform";
                case DecisionId.Crackdown: return "protest";
                default: return "ballot";
            }
        }

        // ------------------------------------------------------------------ diplomacy

        void BuildDiplomacy(VisualElement body, Country c)
        {
            if (!IsPlaying || c == Player)
            {
                BuildForeignOverview(body, c);
                return;
            }
            var d = Sim.Diplomacy;
            double r = d.Relations(Player, c);
            var target = Sim.DiplomacySystem.RelationsTarget(Player, c);

            var relBox = Ui.Element("gs-card", true);
            var head = Ui.Row();
            head.Add(Ui.IconElement("diplomacy", 20));
            var t = Ui.Label("Relations", 16, null, Ui.Weight.SemiBold);
            t.style.marginLeft = 6;
            head.Add(t);
            head.Add(Ui.Spacer());
            head.Add(Ui.Label($"{r:+0;-0;0}", 22, r >= 30 ? Ui.Good : r <= -30 ? Ui.Bad : Ui.Warn, Ui.Weight.Title));
            relBox.Add(head);
            relBox.Add(Ui.Bar((r + 100) / 200, r >= 30 ? Ui.Good : r <= -30 ? Ui.Bad : Ui.Warn, (Math.Max(-100, Math.Min(100, target.Total)) + 100) / 200));
            _tips.Attach(relBox, () => Tips.Relations(Sim, Player, c));
            body.Add(relBox);

            var status = new List<string>();
            if (d.AreAllied(Player, c)) status.Add("Allies");
            if (d.AreTradePartners(Player, c)) status.Add("Trade partners");
            if (d.Sanctions(Player, c)) status.Add("We sanction them");
            if (d.Sanctions(c, Player)) status.Add("They sanction us");
            if (status.Count > 0)
                body.Add(Ui.Label(string.Join("   |   ", status), 14, Ui.Gold, Ui.Weight.SemiBold));

            body.Add(Ui.SectionTitle("Actions"));
            body.Add(ActionCard(c, DiplomaticAction.ImproveRelations, "dove"));
            body.Add(ActionCard(c, d.HasTradeDeal(Player, c) ? DiplomaticAction.CancelTrade : DiplomaticAction.ProposeTrade, "trade"));
            body.Add(ActionCard(c, d.Sanctions(Player, c) ? DiplomaticAction.LiftSanctions : DiplomaticAction.ImposeSanctions, "sanctions"));
            if (d.AreAllied(Player, c))
                body.Add(ActionCard(c, DiplomaticAction.LeaveAlliance, "alliance_leave"));
            else
            {
                body.Add(ActionCard(c, DiplomaticAction.ProposePact, "alliance"));
                var join = Sim.DiplomacySystem.Preview(Player, c, DiplomaticAction.JoinAlliance);
                if (join.Alliance != null)
                    body.Add(ActionCard(c, DiplomaticAction.JoinAlliance, "alliance"));
            }
            body.Add(ActionCard(c, DiplomaticAction.Denounce, "denounce"));
            body.Add(ActionCard(c, DiplomaticAction.DeclareWar, "war"));

            var blocs = d.BlocsOf(c).ToList();
            if (blocs.Count > 0)
            {
                body.Add(Ui.SectionTitle("Their memberships"));
                var wrap = Ui.Row();
                wrap.style.flexWrap = Wrap.Wrap;
                foreach (var b in blocs)
                    wrap.Add(BlocChip(b));
                body.Add(wrap);
            }
        }

        VisualElement ActionCard(Country c, DiplomaticAction action, string icon)
        {
            var p = Sim.DiplomacySystem.Preview(Player, c, action);
            var card = Ui.Element("gs-card", true);
            var head = Ui.Row();
            head.Add(Ui.IconElement(icon, 20, action == DiplomaticAction.DeclareWar ? Ui.Bad : (Color?)null));
            var name = Ui.Label(p.Title, 16, null, Ui.Weight.SemiBold);
            name.style.marginLeft = 6;
            head.Add(name);
            head.Add(Ui.Spacer());
            if (p.CostMillions > 0)
                head.Add(Ui.Label(Ui.FormatMoneyMillions(p.CostMillions), 14, Ui.Warn, Ui.Weight.SemiBold));
            card.Add(head);
            card.Add(Ui.Label(p.Description, 13, Ui.TextDim));

            var foot = Ui.Row();
            foot.style.marginTop = 4;
            if (!p.Available)
            {
                foot.Add(Ui.Label(p.Reason, 13, Ui.Bad));
            }
            else if (p.NeedsConsent)
            {
                var verdict = Ui.Label(p.WillAccept ? "They will accept" : "They will refuse", 14, p.WillAccept ? Ui.Good : Ui.Bad, Ui.Weight.SemiBold);
                verdict.pickingMode = PickingMode.Position;
                _tips.Attach(verdict, () => Tips.Opinion(p, c.Name));
                foot.Add(verdict);
                var why = Ui.Label("  (hover for reasons)", 12, Ui.TextDim);
                foot.Add(why);
            }
            foot.Add(Ui.Spacer());
            var go = Ui.Button(p.NeedsConsent ? "Propose" : "Do it", () => _game.DoDiplomacy(c, action),
                !p.NeedsConsent || p.WillAccept, 14);
            go.SetEnabled(p.Available);
            foot.Add(go);
            card.Add(foot);
            if (p.NeedsConsent && p.Available)
                _tips.Attach(card, () => Tips.Opinion(p, c.Name));
            return card;
        }

        void BuildForeignOverview(VisualElement body, Country c)
        {
            var d = Sim.Diplomacy;
            var others = _game.World.Countries.Where(o => o != c && !o.IsEliminated).ToList();
            var friends = others.OrderByDescending(o => d.Relations(c, o)).Take(6).ToList();
            var rivals = others.OrderBy(o => d.Relations(c, o)).Take(6).ToList();

            body.Add(Ui.SectionTitle("Closest friends"));
            foreach (var f in friends)
                body.Add(CountryRow(c, f));
            body.Add(Ui.SectionTitle("Worst relations"));
            foreach (var f in rivals)
                body.Add(CountryRow(c, f));

            var sanctions = others.Where(o => d.Sanctions(c, o)).Select(o => o.Name).ToList();
            var sanctioned = others.Where(o => d.Sanctions(o, c)).Select(o => o.Name).ToList();
            if (sanctions.Count > 0 || sanctioned.Count > 0)
            {
                body.Add(Ui.SectionTitle("Sanctions"));
                if (sanctions.Count > 0)
                    body.Add(Ui.Label($"Imposes sanctions on: {string.Join(", ", sanctions.Take(12))}{(sanctions.Count > 12 ? "..." : "")}", 13, Ui.TextDim));
                if (sanctioned.Count > 0)
                    body.Add(Ui.Label($"Sanctioned by {sanctioned.Count} countries: {string.Join(", ", sanctioned.Take(12))}{(sanctioned.Count > 12 ? "..." : "")}", 13, Ui.TextDim));
            }
            if (IsPlaying && c == Player)
                body.Add(Ui.Label("Click another country on the map to see diplomatic actions.", 14, Ui.Gold));
        }

        VisualElement CountryRow(Country from, Country other)
        {
            double r = Sim.Diplomacy.Relations(from, other);
            var row = Ui.Element("gs-stat-row", true);
            var left = Ui.Row();
            left.Add(Ui.FlagElement(other.Tag, "gs-flag--small"));
            var n = Ui.Label(other.Name, 14, null, Ui.Weight.Medium);
            n.style.marginLeft = 8;
            left.Add(n);
            row.Add(left);
            row.Add(Ui.Label($"{r:+0;-0;0}", 15, r >= 30 ? Ui.Good : r <= -30 ? Ui.Bad : Ui.Warn, Ui.Weight.SemiBold));
            row.RegisterCallback<ClickEvent>(_ => _game.SelectCountry(other));
            _tips.Attach(row, () => Tips.Relations(Sim, from, other));
            return row;
        }
    }
}
